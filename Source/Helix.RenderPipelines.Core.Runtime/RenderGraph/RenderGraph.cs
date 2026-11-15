using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using HelixEngine.Rendering.Internal;
using HelixEngine.Rendering.RendererUtils;

namespace HelixEngine.Rendering.RenderGraphModule
{
    /// <summary>
    /// A frame's passes recorded with the textures and renderer lists they use, then compiled and executed: passes
    /// whose results nothing uses are culled, the textures the graph makes live from their first use to their last
    /// and share pooled textures of the same kind, consecutive raster passes with the same attachments run as one
    /// render pass, and every pass records into the frame's command buffer. The native core's graph by id.
    /// </summary>
    public partial class RenderGraph
    {
        public static readonly int kMaxMRTCount = 8;

        [ThreadStatic] internal static Exception s_Failure;
        // The graph executing a frame on this thread (C# post process components render with its commands).
        [ThreadStatic] internal static RenderGraph s_Executing;

        readonly string m_Name;
        ulong m_Id;
        bool m_NativeRenderPasses;
        RenderGraphParameters m_Parameters;
        bool m_Recording;
        int m_Execution;
        internal RenderGraphBuilder m_OpenBuilder;
        // The render functions and scale functions of the frame, freed after it executes.
        readonly List<GCHandle> m_FrameHandles = new List<GCHandle>();
        readonly Dictionary<int, ScaleFunc> m_ScaleFuncs = new Dictionary<int, ScaleFunc>();

        internal RasterCommandBuffer m_RasterCommands;
        internal ComputeCommandBuffer m_ComputeCommands;
        internal UnsafeCommandBuffer m_UnsafeCommands;

        public RenderGraph(string name = "RenderGraph")
        {
            m_Name = name;
            defaultResources = new RenderGraphDefaultResources();
        }

        public string name => m_Name;

        // The native graph (made again after Cleanup).
        internal ulong id
        {
            get
            {
                if (m_Id == 0)
                {
                    m_Id = GraphNative.Create(m_Name ?? "");
                    GraphNative.SetNativeRenderPasses(m_Id, m_NativeRenderPasses);
                }
                return m_Id;
            }
        }

        /// <summary>Consecutive raster passes with the same attachments run as one native render pass (subpasses); off: one target bind per group.</summary>
        public bool nativeRenderPassesEnabled
        {
            get => m_NativeRenderPasses;
            set
            {
                m_NativeRenderPasses = value;
                if (m_Id != 0)
                    GraphNative.SetNativeRenderPasses(m_Id, value);
            }
        }

        public RenderGraphDefaultResources defaultResources { get; }

        /// <summary>The viewer is editor work: never active in a player.</summary>
        public static bool isRenderGraphViewerActive => false;

        /// <summary>The counts of the last executed frame, for the tests and the debug display.</summary>
        internal RenderGraphStatistics statistics => m_Id != 0 ? GraphNative.Statistics(m_Id) : default;

        // The exception of an error kind of the native graph (0: none).
        internal static void Throw(int kind, string message)
        {
            switch (kind)
            {
                case 0:
                    return;
                case 1:
                    throw new ArgumentException(message);
                case 3:
                    throw new ArgumentOutOfRangeException("index", message);
                default:
                    throw new InvalidOperationException(message);
            }
        }

        // ------------------------------------------------------------------------------------------------ recording

        public void BeginRecording(in RenderGraphParameters parameters)
        {
            if (m_Recording)
                throw new InvalidOperationException("RenderGraph.BeginRecording: the graph is already recording; call EndRecordingAndExecute first.");
            if (parameters.commandBuffer == null)
                throw new ArgumentException("RenderGraphParameters.commandBuffer is null.");
            m_Parameters = parameters;
            ulong context = parameters.scriptableRenderContext != default ? parameters.scriptableRenderContext.id : 0;
            Throw(GraphNative.BeginRecording(id, context, parameters.commandBuffer.Id, parameters.currentFrameIndex, parameters.rendererListCulling,
                                             parameters.executionName ?? ""), GraphNative.Error(id));
            m_Recording = true;
            m_Execution = (int)GraphNative.Execution(id);
            m_RasterCommands = new RasterCommandBuffer(parameters.commandBuffer);
            m_ComputeCommands = new ComputeCommandBuffer(parameters.commandBuffer);
            m_UnsafeCommands = new UnsafeCommandBuffer(parameters.commandBuffer);
            defaultResources.Import(this);
        }

        public void EndRecordingAndExecute()
        {
            if (!m_Recording)
                throw new InvalidOperationException("RenderGraph.EndRecordingAndExecute: BeginRecording was not called.");
            if (m_OpenBuilder != null)
                throw new InvalidOperationException($"RenderGraph.EndRecordingAndExecute: the builder of pass {m_OpenBuilder.passName} was not disposed.");
            Exception failure;
            RenderGraph outer = s_Executing;
            try
            {
                s_Failure = null;
                s_Executing = this;
                GraphNative.EndRecordingAndExecute(id);
            }
            finally
            {
                s_Executing = outer;
                failure = s_Failure;
                s_Failure = null;
                foreach (GCHandle h in m_FrameHandles)
                    h.Free();
                m_FrameHandles.Clear();
                m_ScaleFuncs.Clear();
                m_Recording = false;
            }
            // The first exception of a render function (the passes after it ran).
            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }

        internal void CheckRecording(string what)
        {
            if (!m_Recording)
                throw new InvalidOperationException($"RenderGraph.{what}: the graph is not recording (call BeginRecording first).");
        }

        internal ulong KeepForFrame(object value)
        {
            GCHandle h = GCHandle.Alloc(value);
            m_FrameHandles.Add(h);
            return (ulong)GCHandle.ToIntPtr(h);
        }

        public TextureHandle ImportTexture(RTHandle rt) => ImportTexture(rt, new ImportResourceParams());

        public TextureHandle ImportTexture(RTHandle rt, ImportResourceParams importParams)
        {
            CheckRecording(nameof(ImportTexture));
            if (rt == null)
                throw new ArgumentNullException(nameof(rt));
            return TextureHandle.Unpack(GraphNative.ImportTexture(id, rt.m_Id, importParams.clearOnFirstUse, importParams.clearColor, importParams.discardOnLastUse));
        }

        public TextureHandle ImportBackbuffer(RenderTargetIdentifier rt) => ImportBackbuffer(rt, new RenderTargetInfo(), new ImportResourceParams());

        public TextureHandle ImportBackbuffer(RenderTargetIdentifier rt, RenderTargetInfo info, ImportResourceParams importParams = new ImportResourceParams())
        {
            CheckRecording(nameof(ImportBackbuffer));
            return TextureHandle.Unpack(GraphNative.ImportBackbuffer(id, Targets.Data(rt), info.width, info.height, info.volumeDepth, (int)info.format,
                                                                     importParams.clearOnFirstUse, importParams.clearColor, importParams.discardOnLastUse));
        }

        public TextureHandle CreateTexture(in TextureDesc desc)
        {
            CheckRecording(nameof(CreateTexture));
            TextureDescData data = desc.Data(this, out ulong function, out ulong user);
            TextureHandle h = TextureHandle.Unpack(GraphNative.CreateTexture(id, data, desc.name ?? "", function, user));
            Created(h, desc);
            return h;
        }

        // A texture of a scale function: GetTextureDesc gives the function back.
        internal void Created(TextureHandle h, in TextureDesc desc)
        {
            if (desc.sizeMode == TextureSizeMode.Functor && desc.func != null)
                m_ScaleFuncs[h.m_Index] = desc.func;
        }

        public TextureHandle CreateTexture(TextureHandle texture) => CreateTexture(GetTextureDesc(texture));

        public TextureDesc GetTextureDesc(TextureHandle texture)
        {
            CheckTexture(texture, "GetTextureDesc");
            TextureDesc desc = TextureDesc.Of(GraphNative.GetTextureDesc(id, texture.packed), GraphNative.TextureName(id, texture.packed));
            if (desc.sizeMode == TextureSizeMode.Functor)
            {
                if (m_ScaleFuncs.TryGetValue(texture.m_Index, out ScaleFunc f))
                    desc.func = f;
                else
                    desc.func = RTHandle.Of(GraphNative.ImportedHandle(id, texture.packed))?.scaleFunc;
            }
            return desc;
        }

        public RendererListHandle CreateRendererList(in RendererListDesc desc)
        {
            CheckRecording(nameof(CreateRendererList));
            ScriptableRenderContext context = m_Parameters.scriptableRenderContext;
            RendererList list = context != default && desc.IsValid() ? context.CreateRendererList(desc) : RendererList.nullRendererList;
            return RendererListHandle.Unpack(GraphNative.ImportRendererList(id, list.m_Id));
        }

        public RendererListHandle CreateRendererList(in RendererListParams desc)
        {
            CheckRecording(nameof(CreateRendererList));
            ScriptableRenderContext context = m_Parameters.scriptableRenderContext;
            RendererListParams p = desc;
            RendererList list = context != default ? context.CreateRendererList(ref p) : RendererList.nullRendererList;
            return RendererListHandle.Unpack(GraphNative.ImportRendererList(id, list.m_Id));
        }

        public IRasterRenderGraphBuilder AddRasterRenderPass<PassData>(string passName, out PassData passData) where PassData : class, new() =>
            AddPass(passName, PassKind.Raster, out passData);

        public IComputeRenderGraphBuilder AddComputePass<PassData>(string passName, out PassData passData) where PassData : class, new() =>
            AddPass(passName, PassKind.Compute, out passData);

        public IUnsafeRenderGraphBuilder AddUnsafePass<PassData>(string passName, out PassData passData) where PassData : class, new() =>
            AddPass(passName, PassKind.Unsafe, out passData);

        RenderGraphBuilder AddPass<PassData>(string passName, PassKind kind, out PassData passData) where PassData : class, new()
        {
            CheckRecording("Add" + kind + "Pass");
            if (m_OpenBuilder != null)
                throw new InvalidOperationException($"RenderGraph: pass {passName} was added while the builder of pass {m_OpenBuilder.passName} is open; dispose it first.");
            passData = new PassData();
            m_OpenBuilder = new RenderGraphBuilder(this, GraphNative.AddPass(id, passName ?? "", (int)kind), passName ?? "", passData);
            return m_OpenBuilder;
        }

        internal void CheckTexture(in TextureHandle handle, string pass)
        {
            if (!handle.IsValid() || handle.m_Graph != m_Execution)
                throw new ArgumentException($"Pass {pass}: the texture handle is not valid (null, or made in another frame or graph).");
        }

        // --------------------------------------------------------------------------------------------- lifetime

        /// <summary>Pooled textures unused for some frames are released.</summary>
        public void EndFrame()
        {
            if (m_Id != 0)
                GraphNative.EndFrame(m_Id);
        }

        public void Cleanup()
        {
            if (m_Id == 0)
                return;
            GraphNative.Destroy(m_Id);
            m_Id = 0;
        }

        // ----------------------------------------------------------------------------------------------- execution

        // A render function of the frame: the closure the GC handle holds; an exception waits for EndRecordingAndExecute.
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        internal static void RunPass(ulong user, ulong buffer, int kind)
        {
            try
            {
                ((Action)GCHandle.FromIntPtr((IntPtr)user).Target)();
            }
            catch (Exception e)
            {
                s_Failure ??= e;
            }
        }

        internal static RTHandle Resolve(TextureHandle handle)
        {
            ulong rt = GraphNative.Resolve(handle.packed);
            if (rt == 0)
            {
                if (!GraphNative.Executing((uint)handle.m_Graph))
                    throw new InvalidOperationException("A render graph texture can only be read in the render function of a pass of its frame.");
                throw new InvalidOperationException("The render graph texture is not allocated: only the passes that declared it may read it.");
            }
            return RTHandle.Of(rt);
        }

        internal static RendererList ResolveList(RendererListHandle handle)
        {
            if (!GraphNative.Executing((uint)handle.m_Graph))
                throw new InvalidOperationException("A render graph renderer list can only be read in the render function of a pass of its frame.");
            return new RendererList(GraphNative.ResolveList(handle.packed));
        }
    }
}
