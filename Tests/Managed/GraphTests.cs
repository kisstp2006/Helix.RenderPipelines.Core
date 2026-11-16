using System;
using HelixEngine;
using HelixEngine.Experimental.Rendering;
using HelixEngine.Rendering;
using HelixEngine.Rendering.RenderGraphModule;
using HelixEngine.Rendering.RendererUtils;

namespace Tests
{
    // A pipeline of a render graph: opaque into a graph texture, a second pass into the same attachments (merged), a
    // pass nothing reads (culled), one that may not be culled, one sharing that one's texture (aliased) and publishing
    // it as a global, the color into the backbuffer, and an unsafe pass drawing the global into a corner.
    public class CsGraphPipeline : RenderPipeline
    {
        public static bool native;
        public static Mesh quad;
        public static Material blue;
        static readonly ShaderTagId s_Unlit = new ShaderTagId("SRPDefaultUnlit");
        static readonly int s_Late = Shader.PropertyToID("_LateTexture");
        readonly RenderGraph m_Graph = new RenderGraph("TestGraph");

        class OpaqueData
        {
            public RendererListHandle list;
        }

        class DrawData
        {
            public TextureHandle source;
        }

        protected override void Render(ScriptableRenderContext context, Camera[] cameras)
        {
            foreach (Camera camera in cameras)
            {
                if (camera.name != "GraphEye" || !camera.TryGetCullingParameters(out ScriptableCullingParameters parameters))
                    continue;
                CullingResults culling = context.Cull(ref parameters);
                context.SetupCameraProperties(camera);
                RTHandles.SetReferenceSize(camera.pixelWidth, camera.pixelHeight);
                var cmd = new CommandBuffer { name = "Graph" };
                m_Graph.nativeRenderPassesEnabled = native;
                m_Graph.BeginRecording(new RenderGraphParameters { commandBuffer = cmd, scriptableRenderContext = context, executionName = "Test", currentFrameIndex = Time.frameCount });
                TextureHandle backbuffer = m_Graph.ImportBackbuffer(BuiltinRenderTextureType.CameraTarget,
                                                                     new RenderTargetInfo { width = camera.pixelWidth, height = camera.pixelHeight, volumeDepth = 1, msaaSamples = 1, format = GraphicsFormat.R8G8B8A8_UNorm });
                var colorDesc = new TextureDesc(camera.pixelWidth, camera.pixelHeight) { colorFormat = GraphicsFormat.R8G8B8A8_UNorm, clearBuffer = true, clearColor = Color.black, name = "Color" };
                TextureHandle color = m_Graph.CreateTexture(colorDesc);
                TextureHandle depth = m_Graph.CreateTexture(new TextureDesc(Vector2.one) { colorFormat = GraphicsFormat.None, depthBufferBits = DepthBits.Depth32, clearBuffer = true, name = "Depth" });
                RendererListHandle list = m_Graph.CreateRendererList(new RendererListDesc(s_Unlit, culling, camera) { sortingCriteria = SortingCriteria.CommonOpaque, renderQueueRange = RenderQueueRange.opaque });

                using (IRasterRenderGraphBuilder builder = m_Graph.AddRasterRenderPass("Opaque", out OpaqueData data))
                {
                    data.list = list;
                    builder.SetRenderAttachment(color, 0);
                    builder.SetRenderAttachmentDepth(depth);
                    builder.UseRendererList(list);
                    builder.SetRenderFunc((OpaqueData d, RasterGraphContext ctx) => ctx.cmd.DrawRendererList(d.list));
                }
                using (IRasterRenderGraphBuilder builder = m_Graph.AddRasterRenderPass("Tint", out DrawData data))
                {
                    builder.SetRenderAttachment(color, 0);
                    builder.SetRenderAttachmentDepth(depth);
                    builder.SetRenderFunc((DrawData d, RasterGraphContext ctx) =>
                        ctx.cmd.DrawMesh(quad, Matrix4x4.TRS(new Vector3(1, 0, 0), Quaternion.identity, new Vector3(0.75f, 0.75f, 1)), blue));
                }
                var smallDesc = new TextureDesc(8, 8) { colorFormat = GraphicsFormat.R8G8B8A8_UNorm, clearBuffer = true, clearColor = Color.green, name = "Small" };
                using (IRasterRenderGraphBuilder builder = m_Graph.AddRasterRenderPass("Unused", out DrawData data))
                {
                    builder.SetRenderAttachment(m_Graph.CreateTexture(smallDesc), 0);
                    builder.SetRenderFunc((DrawData d, RasterGraphContext ctx) => Notes.Note("unused pass ran"));
                }
                using (IRasterRenderGraphBuilder builder = m_Graph.AddRasterRenderPass("Kept", out DrawData data))
                {
                    builder.SetRenderAttachment(m_Graph.CreateTexture(smallDesc), 0);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc((DrawData d, RasterGraphContext ctx) => { });
                }
                TextureHandle late = m_Graph.CreateTexture(smallDesc);
                using (IRasterRenderGraphBuilder builder = m_Graph.AddRasterRenderPass("Late", out DrawData data))
                {
                    builder.SetRenderAttachment(late, 0, AccessFlags.WriteAll);
                    builder.SetGlobalTextureAfterPass(late, s_Late);
                    builder.SetRenderFunc((DrawData d, RasterGraphContext ctx) => { });
                }
                using (IRasterRenderGraphBuilder builder = m_Graph.AddRasterRenderPass("ToScreen", out DrawData data))
                {
                    data.source = color;
                    builder.UseTexture(color);
                    builder.SetRenderAttachment(backbuffer, 0, AccessFlags.WriteAll);
                    builder.SetRenderFunc((DrawData d, RasterGraphContext ctx) => Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1, 1, 0, 0), 0, false));
                }
                using (IUnsafeRenderGraphBuilder builder = m_Graph.AddUnsafePass("Corner", out DrawData data))
                {
                    data.source = late;
                    builder.UseGlobalTexture(s_Late);
                    builder.UseTexture(backbuffer, AccessFlags.Write);
                    builder.SetRenderFunc((DrawData d, UnsafeGraphContext ctx) =>
                    {
                        ctx.cmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                        Blitter.BlitQuad(ctx.cmd, (Texture)d.source, new Vector4(1, 1, 0, 0), new Vector4(0.25f, 0.25f, 0.75f, 0.75f), 0, false);
                    });
                }
                m_Graph.EndRecordingAndExecute();
                context.ExecuteCommandBuffer(cmd);
                cmd.Release();
                context.Submit();
                RenderGraphStatistics s = m_Graph.statistics;
                Notes.Note($"graph:{native}:{s.passes}:{s.culledPasses}:{s.renderPasses}:{s.mergedPasses}:{s.textures}:{s.pooledTexturesCreated}:{s.aliasedTextures}:{s.pooledTextures}");
                m_Graph.EndFrame();
            }
        }

        protected override void Dispose(bool disposing) => m_Graph.Cleanup();
    }

    public class CsGraphPipelineAsset : RenderPipelineAsset
    {
        protected override RenderPipeline CreatePipeline() => new CsGraphPipeline();
        public override Type pipelineType => typeof(CsGraphPipeline);
    }

    // The scene of the graph pipeline: a red quad on the left; the pipeline draws the blue one; the frames alternate the native render passes.
    public class CsGraphScene : GameBehaviour
    {
        void Start()
        {
            Blitter.Initialize(Shader.Find("Hidden/Core/Blit"), Shader.Find("Hidden/Core/BlitColorAndDepth"));
            var eye = new GameObject("GraphEye");
            var cam = eye.AddComponent<Camera>();
            eye.transform.position = new Vector3(0, 0, -10);
            cam.orthographic = true;
            cam.orthographicSize = 2;
            var red = new Material(Shader.Find("Tests/Unlit"));
            red.SetColor("color", Color.red);
            CsGraphPipeline.quad = Quad();
            CsGraphPipeline.blue = new Material(Shader.Find("Tests/Unlit"));
            CsGraphPipeline.blue.SetColor("color", Color.blue);
            var shape = new GameObject("GraphShape");
            shape.AddComponent<MeshFilter>().sharedMesh = CsGraphPipeline.quad;
            shape.AddComponent<MeshRenderer>().sharedMaterial = red;
            shape.transform.position = new Vector3(-1, 0, 0);
            shape.transform.localScale = new Vector3(0.75f, 0.75f, 1);
            CsGraphPipeline.native = true;
        }

        void Update() => CsGraphPipeline.native = !CsGraphPipeline.native;

        void OnDestroy() => Blitter.Cleanup();

        // A quad of side 2 around the origin facing -z.
        static Mesh Quad()
        {
            var mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0), new Vector3(1, -1, 0) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    // C# passes on a graph made and executed in C++ (the C++ test's): a clear into the graph's color texture.
    public class CsGraphRecorder : CoreTests.CppGraphRecorder
    {
        class ClearData
        {
            public Color color;
        }

        public override void Prepare(VolumeProfile profile) => profile.Add<CsTintPost>().tint.Override(new Color(1, 0.5f, 1, 1));

        public override void Record(ulong graph, ulong color)
        {
            RenderGraph g = Helix.Interop.RenderGraphInterop.GraphOf(graph);
            TextureHandle target = Helix.Interop.RenderGraphInterop.TextureOf(color);
            using (IRasterRenderGraphBuilder builder = g.AddRasterRenderPass("CsClear", out ClearData data))
            {
                data.color = Color.green;
                builder.SetRenderAttachment(target, 0, AccessFlags.WriteAll);
                builder.SetRenderFunc((ClearData d, RasterGraphContext ctx) => ctx.cmd.ClearRenderTarget(false, true, d.color));
            }
            Notes.Note($"borrowed:{g.name}:{g.nativeRenderPassesEnabled}:{ReferenceEquals(g, Helix.Interop.RenderGraphInterop.GraphOf(graph))}");
        }
    }
}
