using System;
using HelixEngine;
using HelixEngine.Experimental.Rendering;
using HelixEngine.Rendering;
using HelixEngine.Rendering.RenderGraphModule;
using HelixEngine.Rendering.RendererUtils;

namespace Tests
{
    // The core's whole stack: per camera the volumes blended for it, opaque then transparent renderer lists into
    // graph textures (one render pass), the post process components after them, the result blitted into the camera's
    // target. Native render passes when CsCorePipeline.native.
    public class CsCorePipeline : RenderPipeline
    {
        public static bool native;
        static readonly ShaderTagId[] s_Passes = { new ShaderTagId("SRPDefaultUnlit"), new ShaderTagId("ForwardBase") };
        readonly RenderGraph m_Graph = new RenderGraph("CoreGraph");
        readonly PostProcessPasses m_Post = new PostProcessPasses();
        VolumeStack m_Stack;

        class ListData
        {
            public RendererListHandle list;
        }

        class CopyData
        {
            public TextureHandle source;
        }

        public CsCorePipeline()
        {
            Blitter.Initialize(Shader.Find("Hidden/Core/Blit"), Shader.Find("Hidden/Core/BlitColorAndDepth"));
            if (!VolumeManager.instance.isInitialized)
                VolumeManager.instance.Initialize();
            m_Stack = VolumeManager.instance.CreateStack();
        }

        void Draw(string name, RendererListHandle list, TextureHandle color, TextureHandle depth)
        {
            using (IRasterRenderGraphBuilder builder = m_Graph.AddRasterRenderPass(name, out ListData data))
            {
                data.list = list;
                builder.SetRenderAttachment(color, 0);
                builder.SetRenderAttachmentDepth(depth);
                builder.UseRendererList(list);
                builder.SetRenderFunc((ListData d, RasterGraphContext ctx) => ctx.cmd.DrawRendererList(d.list));
            }
        }

        protected override void Render(ScriptableRenderContext context, Camera[] cameras)
        {
            foreach (Camera camera in cameras)
            {
                if (!camera.TryGetCullingParameters(out ScriptableCullingParameters parameters))
                    continue;
                CullingResults culling = context.Cull(ref parameters);
                context.SetupCameraProperties(camera);
                RTHandles.SetReferenceSize(camera.pixelWidth, camera.pixelHeight);
                VolumeManager.instance.Update(m_Stack, camera.transform, camera.cullingMask);
                var cmd = new CommandBuffer { name = "Core" };
                m_Graph.nativeRenderPassesEnabled = native;
                m_Graph.BeginRecording(new RenderGraphParameters { commandBuffer = cmd, scriptableRenderContext = context, executionName = camera.name });
                TextureHandle backbuffer = m_Graph.ImportBackbuffer(BuiltinRenderTextureType.CameraTarget,
                                                                     new RenderTargetInfo { width = camera.pixelWidth, height = camera.pixelHeight, volumeDepth = 1, msaaSamples = 1 });
                TextureHandle color = m_Graph.CreateTexture(new TextureDesc(Vector2.one)
                {
                    colorFormat = GraphicsFormat.R8G8B8A8_UNorm, clearBuffer = true, clearColor = camera.backgroundColor, name = "CameraColor",
                });
                TextureHandle depth = m_Graph.CreateTexture(new TextureDesc(Vector2.one) { colorFormat = GraphicsFormat.None, depthBufferBits = DepthBits.Depth32, clearBuffer = true, name = "CameraDepth" });
                var opaque = new RendererListDesc(s_Passes, culling, camera) { sortingCriteria = SortingCriteria.CommonOpaque, renderQueueRange = RenderQueueRange.opaque };
                var transparent = new RendererListDesc(s_Passes, culling, camera) { sortingCriteria = SortingCriteria.CommonTransparent, renderQueueRange = RenderQueueRange.transparent };
                Draw("Opaque", m_Graph.CreateRendererList(opaque), color, depth);
                Draw("Transparent", m_Graph.CreateRendererList(transparent), color, depth);
                TextureHandle result = m_Post.Record(m_Graph, m_Stack, PostProcessInjectionPoint.AfterOpaqueAndSky, color);
                using (IRasterRenderGraphBuilder builder = m_Graph.AddRasterRenderPass("FinalBlit", out CopyData data))
                {
                    data.source = result;
                    builder.UseTexture(result);
                    builder.SetRenderAttachment(backbuffer, 0, AccessFlags.WriteAll);
                    builder.SetRenderFunc((CopyData d, RasterGraphContext ctx) => Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1, 1, 0, 0), 0, false));
                }
                m_Graph.EndRecordingAndExecute();
                context.ExecuteCommandBuffer(cmd);
                cmd.Release();
                context.Submit();
                RenderGraphStatistics s = m_Graph.statistics;
                Notes.Note($"core:{native}:{s.passes}:{s.culledPasses}:{s.renderPasses}:{s.mergedPasses}:{s.textures}");
                m_Graph.EndFrame();
            }
        }

        protected override void Dispose(bool disposing)
        {
            m_Post.Dispose();
            m_Graph.Cleanup();
            VolumeManager.instance.DestroyStack(m_Stack);
            VolumeManager.instance.Deinitialize();
            Blitter.Cleanup();
        }
    }

    public class CsCorePipelineAsset : RenderPipelineAsset
    {
        protected override RenderPipeline CreatePipeline() => new CsCorePipeline();
        public override Type pipelineType => typeof(CsCorePipeline);
    }

    public class CsCoreDrawer : GameBehaviour
    {
        public Mesh mesh;
        public Material material;

        void Update() => Graphics.RenderMesh(new RenderParams(material), mesh, 0, Matrix4x4.TRS(new Vector3(-2, 0, 0), Quaternion.identity, Vector3.one));
    }

    // The scene of the engine's golden image (ScriptApiScene): a camera, a light, a lit quad and a drawn textured mesh;
    // and a global volume tinting it, weighing nothing in the first frame.
    public class CsCoreScene : GameBehaviour
    {
        Volume m_Volume;
        int m_Frame;

        static Mesh Quad()
        {
            var mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0), new Vector3(1, -1, 0) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        void Start()
        {
            var eye = new GameObject("CoreEye");
            var cam = eye.AddComponent<Camera>();
            eye.transform.position = new Vector3(0, 0, -10);
            cam.orthographic = true;
            cam.orthographicSize = 4;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0.25f, 1);

            var sun = new GameObject("CoreSun");
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.5f;

            Mesh quad = Quad();
            var lit = new Material(Shader.Find("Tests/Lambert"));
            lit.color = new Color(1, 0.5f, 0, 1);
            var shape = new GameObject("CoreShape");
            shape.AddComponent<MeshFilter>().sharedMesh = quad;
            shape.AddComponent<MeshRenderer>().sharedMaterial = lit;
            shape.transform.position = new Vector3(2, 0, 0);

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(new[] { new Color32(255, 255, 0, 255), new Color32(0, 255, 0, 255), new Color32(0, 0, 255, 255), new Color32(255, 255, 255, 255) });
            tex.filterMode = FilterMode.Point;
            tex.Apply(false);
            var textured = new Material(Shader.Find("Tests/Textured"));
            textured.mainTexture = tex;
            var drawer = gameObject.AddComponent<CsCoreDrawer>();
            drawer.mesh = quad;
            drawer.material = textured;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.Add<CsTintPost>().tint.Override(new Color(1, 0.5f, 0.25f, 1));
            m_Volume = new GameObject("CoreVolume").AddComponent<Volume>();
            m_Volume.sharedProfile = profile;
            m_Volume.weight = 0;
        }

        // Frame 1: no post; frame 2: tinted; frame 3: tinted with native render passes.
        void Update()
        {
            m_Frame++;
            m_Volume.weight = m_Frame >= 2 ? 1 : 0;
            CsCorePipeline.native = m_Frame >= 3;
        }
    }
}
