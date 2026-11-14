using System;
using System.Collections.Generic;
using HelixEngine;
using HelixEngine.Experimental.Rendering;
using HelixEngine.Rendering;
using HelixEngine.Rendering.RenderGraphModule;
using HelixEngine.Rendering.RendererUtils;

namespace Tests
{
    // A tint by a color (Tests/Core/Tint): active when the color is not white.
    public class CsTintPost : PostProcessVolumeComponent
    {
        public ColorParameter tint = new ColorParameter(Color.white);
        Material m_Material;
        readonly MaterialPropertyBlock m_Block = new MaterialPropertyBlock();
        static readonly int s_Tint = Shader.PropertyToID("_TintColor");
        static readonly int s_ScaleBias = Shader.PropertyToID("_BlitScaleBias");
        static readonly int s_Texture = Shader.PropertyToID("_BlitTexture");

        public override bool IsActive() => tint.value != Color.white;
        public override PostProcessInjectionPoint injectionPoint => PostProcessInjectionPoint.AfterOpaqueAndSky;

        public override void Setup() => m_Material = CoreUtils.CreateEngineMaterial("Tests/Core/Tint");

        public override void Render(RasterCommandBuffer cmd, RTHandle source)
        {
            m_Block.SetColor(s_Tint, tint.value);
            m_Block.SetVector(s_ScaleBias, new Vector4(1, 1, 0, 0));
            cmd.SetGlobalTexture(s_Texture, source);
            cmd.DrawProcedural(Matrix4x4.identity, m_Material, 0, MeshTopology.Triangles, 3, 1, m_Block);
        }

        public override void Cleanup()
        {
            CoreUtils.Destroy(m_Material);
            m_Material = null;
        }
    }

    // A mirror left to right with the core's blit: active when mirror is on.
    public class CsMirrorPost : PostProcessVolumeComponent
    {
        public BoolParameter mirror = new BoolParameter(false);

        public override bool IsActive() => mirror.value;
        public override PostProcessInjectionPoint injectionPoint => PostProcessInjectionPoint.AfterOpaqueAndSky;

        public override void Render(RasterCommandBuffer cmd, RTHandle source) => Blitter.BlitTexture(cmd, source, new Vector4(-1, 1, 1, 0), 0, false);
    }

    // Opaque into a graph texture, the post process components of the main stack after it, the result into the backbuffer.
    public class CsPostPipeline : RenderPipeline
    {
        static readonly ShaderTagId s_Unlit = new ShaderTagId("SRPDefaultUnlit");
        readonly RenderGraph m_Graph = new RenderGraph("PostGraph");
        readonly PostProcessPasses m_Post = new PostProcessPasses();

        class OpaqueData
        {
            public RendererListHandle list;
        }

        class CopyData
        {
            public TextureHandle source;
        }

        public CsPostPipeline() => m_Post.order.AddRange(new[] { typeof(CsMirrorPost), typeof(CsTintPost) });

        protected override void Render(ScriptableRenderContext context, Camera[] cameras)
        {
            foreach (Camera camera in cameras)
            {
                if (camera.name != "PostEye" || !camera.TryGetCullingParameters(out ScriptableCullingParameters parameters))
                    continue;
                CullingResults culling = context.Cull(ref parameters);
                context.SetupCameraProperties(camera);
                RTHandles.SetReferenceSize(camera.pixelWidth, camera.pixelHeight);
                VolumeManager.instance.Update(null, ~0);
                var cmd = new CommandBuffer { name = "Post" };
                m_Graph.BeginRecording(new RenderGraphParameters { commandBuffer = cmd, scriptableRenderContext = context });
                TextureHandle backbuffer = m_Graph.ImportBackbuffer(BuiltinRenderTextureType.CameraTarget);
                TextureHandle color = m_Graph.CreateTexture(new TextureDesc(Vector2.one) { colorFormat = GraphicsFormat.R8G8B8A8_UNorm, clearBuffer = true, name = "Color" });
                RendererListHandle list = m_Graph.CreateRendererList(new RendererListDesc(s_Unlit, culling, camera) { sortingCriteria = SortingCriteria.CommonOpaque, renderQueueRange = RenderQueueRange.opaque });
                using (IRasterRenderGraphBuilder builder = m_Graph.AddRasterRenderPass("Opaque", out OpaqueData data))
                {
                    data.list = list;
                    builder.SetRenderAttachment(color, 0);
                    builder.UseRendererList(list);
                    builder.SetRenderFunc((OpaqueData d, RasterGraphContext ctx) => ctx.cmd.DrawRendererList(d.list));
                }
                List<PostProcessVolumeComponent> active = m_Post.ActiveComponents(VolumeManager.instance.stack, PostProcessInjectionPoint.AfterOpaqueAndSky);
                TextureHandle result = m_Post.Record(m_Graph, VolumeManager.instance.stack, PostProcessInjectionPoint.AfterOpaqueAndSky, color);
                using (IRasterRenderGraphBuilder builder = m_Graph.AddRasterRenderPass("ToScreen", out CopyData data))
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
                Notes.Note($"post:{string.Join(",", active.ConvertAll(c => c.GetType().Name))}:{m_Graph.statistics.passes}:{m_Graph.statistics.textures}");
                m_Graph.EndFrame();
            }
        }

        protected override void Dispose(bool disposing)
        {
            m_Post.Dispose();
            m_Graph.Cleanup();
        }
    }

    public class CsPostPipelineAsset : RenderPipelineAsset
    {
        protected override RenderPipeline CreatePipeline() => new CsPostPipeline();
        public override Type pipelineType => typeof(CsPostPipeline);
    }

    // A red quad on the left and a global volume tinting and mirroring it; the second frame the volume weighs nothing.
    public class CsPostScene : GameBehaviour
    {
        Volume m_Volume;
        int m_Frame;

        void Start()
        {
            Blitter.Initialize(Shader.Find("Hidden/Core/Blit"), Shader.Find("Hidden/Core/BlitColorAndDepth"));
            VolumeManager.instance.Initialize();
            var eye = new GameObject("PostEye");
            var cam = eye.AddComponent<Camera>();
            eye.transform.position = new Vector3(0, 0, -10);
            cam.orthographic = true;
            cam.orthographicSize = 2;
            var red = new Material(Shader.Find("Tests/Unlit"));
            red.SetColor("color", Color.red);
            var mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0), new Vector3(1, -1, 0) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            var shape = new GameObject("PostShape");
            shape.AddComponent<MeshFilter>().sharedMesh = mesh;
            shape.AddComponent<MeshRenderer>().sharedMaterial = red;
            shape.transform.position = new Vector3(-1, 0, 0);
            shape.transform.localScale = new Vector3(0.75f, 0.75f, 1);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.Add<CsTintPost>().tint.Override(new Color(0.5f, 1, 1, 1));
            profile.Add<CsMirrorPost>().mirror.Override(true);
            m_Volume = new GameObject("PostVolume").AddComponent<Volume>();
            m_Volume.sharedProfile = profile;
        }

        void Update()
        {
            if (m_Frame++ == 1)
                m_Volume.weight = 0;
        }

        void OnDestroy()
        {
            Blitter.Cleanup();
            VolumeManager.instance.Deinitialize();
        }
    }

    // A quad drawn with the shader library's checks: white where every function is right; then instanced.
    public class CsLibraryScene : GameBehaviour
    {
        Material m_Material;
        Mesh m_Mesh;

        void Start()
        {
            var eye = new GameObject("LibraryEye");
            var cam = eye.AddComponent<Camera>();
            eye.transform.position = new Vector3(0, 0, -10);
            cam.orthographic = true;
            cam.orthographicSize = 2;
            m_Material = new Material(Shader.Find("Tests/Core/Library")) { enableInstancing = true };
            m_Mesh = new Mesh();
            m_Mesh.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0), new Vector3(1, -1, 0) };
            m_Mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m_Mesh.RecalculateBounds();
            Notes.Note($"library:{m_Material.shader.isSupported}:{m_Material.shader.passCount}");
        }

        void Update()
        {
            // Left: one draw; right: two instances.
            Graphics.RenderMesh(new RenderParams(m_Material), m_Mesh, 0, Matrix4x4.TRS(new Vector3(-1, 0, 0), Quaternion.identity, new Vector3(0.75f, 0.75f, 1)));
            var instances = new[]
            {
                Matrix4x4.TRS(new Vector3(1, 1, 0), Quaternion.identity, new Vector3(0.75f, 0.75f, 1)),
                Matrix4x4.TRS(new Vector3(1, -1, 0), Quaternion.identity, new Vector3(0.75f, 0.75f, 1)),
            };
            Graphics.RenderMeshInstanced(new RenderParams(m_Material), m_Mesh, 0, instances);
        }

        void OnDestroy()
        {
            DestroyImmediate(m_Material);
            DestroyImmediate(m_Mesh);
        }
    }
}
