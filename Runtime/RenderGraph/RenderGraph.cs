using System;
using System.Collections.Generic;
using Helix.Collections;
using HelixEngine.Rendering.RendererUtils;

namespace HelixEngine.Rendering.RenderGraphModule
{
    /// <summary>
    /// A frame's passes recorded with the textures and renderer lists they use, then compiled and executed: passes
    /// whose results nothing uses are culled, the textures the graph makes live from their first use to their last
    /// and share pooled textures of the same kind, consecutive raster passes with the same attachments run as one
    /// render pass, and every pass records into the frame's command buffer.
    /// </summary>
    public partial class RenderGraph
    {
        public static readonly int kMaxMRTCount = 8;

        static int s_NextExecution;
        [ThreadStatic] static RenderGraph s_Current;

        readonly string m_Name;
        readonly List<GraphTexture> m_Textures = new List<GraphTexture>();
        readonly List<GraphList> m_Lists = new List<GraphList>();
        readonly List<GraphPass> m_Passes = new List<GraphPass>();
        readonly List<RenderPassGroup> m_Groups = new List<RenderPassGroup>();
        readonly TexturePool m_Pool = new TexturePool();
        readonly Dictionary<RenderTargetIdentifier, RTHandle> m_Backbuffers = new Dictionary<RenderTargetIdentifier, RTHandle>();
        RenderGraphParameters m_Parameters;
        bool m_Recording;
        bool m_Executing;
        int m_Execution;
        int m_Frame;
        GraphPass m_OpenPass;

        internal RasterCommandBuffer m_RasterCommands;
        internal ComputeCommandBuffer m_ComputeCommands;
        internal UnsafeCommandBuffer m_UnsafeCommands;

        /// <summary>The counts of the last executed frame, for the tests and the debug display.</summary>
        internal RenderGraphStatistics statistics;

        public RenderGraph(string name = "RenderGraph")
        {
            m_Name = name;
            defaultResources = new RenderGraphDefaultResources();
        }

        public string name => m_Name;

        /// <summary>Consecutive raster passes with the same attachments run as one native render pass (subpasses); off: one target bind per group.</summary>
        public bool nativeRenderPassesEnabled { get; set; }

        public RenderGraphDefaultResources defaultResources { get; }

        /// <summary>The viewer is editor work: never active in a player.</summary>
        public static bool isRenderGraphViewerActive => false;

        // ------------------------------------------------------------------------------------------------ recording

        public void BeginRecording(in RenderGraphParameters parameters)
        {
            if (m_Recording)
                throw new InvalidOperationException("RenderGraph.BeginRecording: the graph is already recording; call EndRecordingAndExecute first.");
            m_Parameters = parameters;
            if (parameters.commandBuffer == null)
                throw new ArgumentException("RenderGraphParameters.commandBuffer is null.");
            m_Recording = true;
            m_Execution = ++s_NextExecution;
            m_Textures.Clear();
            m_Textures.Add(null);
            m_Lists.Clear();
            m_Lists.Add(null);
            m_Passes.Clear();
            m_Groups.Clear();
            m_RasterCommands = new RasterCommandBuffer(parameters.commandBuffer);
            m_ComputeCommands = new ComputeCommandBuffer(parameters.commandBuffer);
            m_UnsafeCommands = new UnsafeCommandBuffer(parameters.commandBuffer);
            defaultResources.Import(this);
        }

        public void EndRecordingAndExecute()
        {
            if (!m_Recording)
                throw new InvalidOperationException("RenderGraph.EndRecordingAndExecute: BeginRecording was not called.");
            if (m_OpenPass != null)
                throw new InvalidOperationException($"RenderGraph.EndRecordingAndExecute: the builder of pass {m_OpenPass.name} was not disposed.");
            try
            {
                Compile();
                Execute();
            }
            finally
            {
                foreach (GraphTexture t in m_Textures)
                    if (t != null && t.pooled != null)
                        m_Pool.Release(t.pooled);
                m_Recording = false;
                s_Current = null;
                m_Executing = false;
            }
        }

        void CheckRecording(string what)
        {
            if (!m_Recording)
                throw new InvalidOperationException($"RenderGraph.{what}: the graph is not recording (call BeginRecording first).");
        }

        public TextureHandle ImportTexture(RTHandle rt) => ImportTexture(rt, new ImportResourceParams());

        public TextureHandle ImportTexture(RTHandle rt, ImportResourceParams importParams)
        {
            CheckRecording(nameof(ImportTexture));
            if (rt == null)
                throw new ArgumentNullException(nameof(rt));
            m_Textures.Add(new GraphTexture { imported = rt, import = importParams, desc = new TextureDesc(rt) });
            return new TextureHandle(m_Textures.Count - 1, m_Execution);
        }

        public TextureHandle ImportBackbuffer(RenderTargetIdentifier rt) => ImportBackbuffer(rt, new RenderTargetInfo(), new ImportResourceParams());

        public TextureHandle ImportBackbuffer(RenderTargetIdentifier rt, RenderTargetInfo info, ImportResourceParams importParams = new ImportResourceParams())
        {
            CheckRecording(nameof(ImportBackbuffer));
            if (!m_Backbuffers.TryGetValue(rt, out RTHandle handle))
                m_Backbuffers[rt] = handle = RTHandles.Alloc(rt, "Backbuffer");
            var desc = new TextureDesc(Mathf.Max(info.width, 1), Mathf.Max(info.height, 1)) { colorFormat = info.format, slices = Mathf.Max(info.volumeDepth, 1), name = "Backbuffer" };
            m_Textures.Add(new GraphTexture { imported = handle, import = importParams, desc = desc, backbuffer = true });
            return new TextureHandle(m_Textures.Count - 1, m_Execution);
        }

        public TextureHandle CreateTexture(in TextureDesc desc)
        {
            CheckRecording(nameof(CreateTexture));
            m_Textures.Add(new GraphTexture { desc = desc });
            return new TextureHandle(m_Textures.Count - 1, m_Execution);
        }

        public TextureHandle CreateTexture(TextureHandle texture) => CreateTexture(GetTextureDesc(texture));

        public TextureDesc GetTextureDesc(TextureHandle texture) => m_Textures[CheckTexture(texture, "GetTextureDesc")].desc;

        public RendererListHandle CreateRendererList(in RendererListDesc desc)
        {
            CheckRecording(nameof(CreateRendererList));
            m_Lists.Add(new GraphList { desc = desc, fromDesc = true });
            return new RendererListHandle(m_Lists.Count - 1, m_Execution);
        }

        public RendererListHandle CreateRendererList(in RendererListParams desc)
        {
            CheckRecording(nameof(CreateRendererList));
            m_Lists.Add(new GraphList { parameters = desc });
            return new RendererListHandle(m_Lists.Count - 1, m_Execution);
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
            if (m_OpenPass != null)
                throw new InvalidOperationException($"RenderGraph: pass {passName} was added while the builder of pass {m_OpenPass.name} is open; dispose it first.");
            passData = new PassData();
            m_OpenPass = new GraphPass { name = passName ?? "", kind = kind, data = passData };
            return new RenderGraphBuilder(this, m_OpenPass);
        }

        internal void EndPass()
        {
            m_Passes.Add(m_OpenPass);
            m_OpenPass = null;
        }

        internal void MarkTransient(TextureHandle handle) => m_Textures[handle.m_Index].transient = true;

        internal int CheckTexture(in TextureHandle handle, string pass)
        {
            if (!handle.IsValid() || handle.m_Graph != m_Execution || handle.m_Index >= m_Textures.Count)
                throw new ArgumentException($"Pass {pass}: the texture handle is not valid (null, or made in another frame or graph).");
            return handle.m_Index;
        }

        internal int CheckList(in RendererListHandle handle, string pass)
        {
            if (!handle.IsValid() || handle.m_Graph != m_Execution || handle.m_Index >= m_Lists.Count)
                throw new ArgumentException($"Pass {pass}: the renderer list handle is not valid (null, or made in another frame or graph).");
            return handle.m_Index;
        }

        // --------------------------------------------------------------------------------------------- lifetime

        /// <summary>Pooled textures unused for some frames are released.</summary>
        public void EndFrame()
        {
            m_Frame++;
            m_Pool.Trim(m_Frame, kStaleFrames);
        }

        const int kStaleFrames = 10;

        public void Cleanup()
        {
            m_Pool.Clear();
            foreach (RTHandle h in m_Backbuffers.Values)
                h.Release();
            m_Backbuffers.Clear();
            defaultResources.Cleanup();
        }

        // ----------------------------------------------------------------------------------------------- execution

        internal static RTHandle Resolve(TextureHandle handle)
        {
            RenderGraph g = s_Current;
            if (g == null || !g.m_Executing || handle.m_Graph != g.m_Execution || handle.m_Index >= g.m_Textures.Count)
                throw new InvalidOperationException("A render graph texture can only be read in the render function of a pass of its frame.");
            GraphTexture t = g.m_Textures[handle.m_Index];
            RTHandle rt = t.Handle;
            if (rt == null)
                throw new InvalidOperationException($"The render graph texture {t.desc.name} is not allocated: only the passes that declared it may read it.");
            return rt;
        }

        internal static RendererList ResolveList(RendererListHandle handle)
        {
            RenderGraph g = s_Current;
            if (g == null || !g.m_Executing || handle.m_Graph != g.m_Execution || handle.m_Index >= g.m_Lists.Count)
                throw new InvalidOperationException("A render graph renderer list can only be read in the render function of a pass of its frame.");
            GraphList l = g.m_Lists[handle.m_Index];
            if (!l.made)
            {
                ScriptableRenderContext context = g.m_Parameters.scriptableRenderContext;
                if (l.fromDesc)
                    l.list = context.CreateRendererList(l.desc);
                else
                    l.list = context.CreateRendererList(ref l.parameters);
                l.made = true;
            }
            return l.list;
        }
    }

    // A texture of a frame: imported (with its handle) or made by the graph (a pooled texture while it lives).
    internal sealed class GraphTexture
    {
        public TextureDesc desc;
        public RTHandle imported;
        public ImportResourceParams import;
        public bool backbuffer;
        public bool transient;
        public TexturePool.Entry pooled;
        public int firstPass = -1, lastPass = -1;
        // Written by a pass that ran before (so a later Write loads it).
        public bool hasContents;

        public RTHandle Handle => imported ?? pooled?.handle;
        public bool isImported => imported != null;
    }

    internal sealed class GraphList
    {
        public RendererListDesc desc;
        public RendererListParams parameters;
        public bool fromDesc;
        public bool made;
        public RendererList list;
    }

    internal sealed class RenderPassGroup
    {
        public readonly List<int> passes = new List<int>();
        public bool raster;
    }

    /// <summary>The counts of a frame of a graph.</summary>
    internal struct RenderGraphStatistics
    {
        public int passes;
        public int culledPasses;
        public int renderPasses;
        public int mergedPasses;
        public int textures;
        public int pooledTexturesCreated;
        public int aliasedTextures;
        public int pooledTextures;
    }

    // Render textures of the graph kept across frames by kind: a texture another one used before in the frame is shared
    // (aliasing); kinds unused for some frames are released.
    internal sealed class TexturePool
    {
        internal sealed class Entry
        {
            public RTHandle handle;
            public TextureDesc desc;
            public Vector2Int size;
            public bool inUse;
            public bool usedThisFrame;
            public int lastFrame;
        }

        readonly List<Entry> m_Entries = new List<Entry>();

        public int Count => m_Entries.Count;

        public void BeginFrame()
        {
            foreach (Entry e in m_Entries)
                e.usedThisFrame = false;
        }

        static bool Same(in TextureDesc a, in TextureDesc b) =>
            a.slices == b.slices && a.depthBufferBits == b.depthBufferBits && a.colorFormat == b.colorFormat && a.filterMode == b.filterMode && a.wrapMode == b.wrapMode &&
            a.dimension == b.dimension && a.enableRandomWrite == b.enableRandomWrite && a.useMipMap == b.useMipMap && a.autoGenerateMips == b.autoGenerateMips &&
            a.isShadowMap == b.isShadowMap && a.anisoLevel == b.anisoLevel && a.mipMapBias == b.mipMapBias && a.msaaSamples == b.msaaSamples &&
            a.bindTextureMS == b.bindTextureMS && a.useDynamicScale == b.useDynamicScale && a.memoryless == b.memoryless && a.vrUsage == b.vrUsage;

        // A texture of the kind and size: a free one (aliased when another texture used it this frame), else a new one.
        public Entry Acquire(in TextureDesc desc, Vector2Int size, int frame, ref RenderGraphStatistics stats)
        {
            foreach (Entry e in m_Entries)
                if (!e.inUse && e.size == size && Same(e.desc, desc))
                {
                    if (e.usedThisFrame)
                        stats.aliasedTextures++;
                    e.inUse = e.usedThisFrame = true;
                    e.lastFrame = frame;
                    return e;
                }
            RTHandle h = RTHandles.Alloc(size.x, size.y, desc.slices, desc.depthBufferBits, desc.colorFormat, desc.filterMode, desc.wrapMode, desc.dimension,
                                         desc.enableRandomWrite, desc.useMipMap, desc.autoGenerateMips, desc.isShadowMap, desc.anisoLevel, desc.mipMapBias, desc.msaaSamples,
                                         desc.bindTextureMS, desc.useDynamicScale, false, desc.memoryless, desc.vrUsage, string.IsNullOrEmpty(desc.name) ? "RenderGraphTexture" : desc.name);
            var n = new Entry { handle = h, desc = desc, size = size, inUse = true, usedThisFrame = true, lastFrame = frame };
            m_Entries.Add(n);
            stats.pooledTexturesCreated++;
            return n;
        }

        public void Release(Entry e) => e.inUse = false;

        public void Trim(int frame, int staleFrames)
        {
            for (int i = m_Entries.Count - 1; i >= 0; i--)
                if (!m_Entries[i].inUse && frame - m_Entries[i].lastFrame > staleFrames)
                {
                    m_Entries[i].handle.Release();
                    m_Entries.RemoveAt(i);
                }
        }

        public void Clear()
        {
            foreach (Entry e in m_Entries)
                e.handle.Release();
            m_Entries.Clear();
        }
    }
}
