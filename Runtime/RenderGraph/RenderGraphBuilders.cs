using System;
using System.Collections.Generic;

namespace HelixEngine.Rendering.RenderGraphModule
{
    /// <summary>What a pass runs when the graph executes: its data, and the commands of its kind.</summary>
    public delegate void BaseRenderFunc<PassData, ContextType>(PassData data, ContextType renderGraphContext) where PassData : class, new();

    public struct RasterGraphContext
    {
        public RasterCommandBuffer cmd;
        public RenderGraphDefaultResources defaultResources;
    }

    public struct ComputeGraphContext
    {
        public ComputeCommandBuffer cmd;
        public RenderGraphDefaultResources defaultResources;
    }

    public struct UnsafeGraphContext
    {
        public UnsafeCommandBuffer cmd;
        public RenderGraphDefaultResources defaultResources;
    }

    /// <summary>What every kind of pass declares: the textures and renderer lists it uses, culling, globals; disposed when the pass is recorded.</summary>
    public interface IBaseRenderGraphBuilder : IDisposable
    {
        void UseTexture(in TextureHandle input, AccessFlags flags = AccessFlags.Read);
        void UseGlobalTexture(int propertyId, AccessFlags flags = AccessFlags.Read);
        void UseAllGlobalTextures(bool enable);
        void UseRendererList(in RendererListHandle input);
        /// <summary>False: the pass runs even when nothing uses what it writes.</summary>
        void AllowPassCulling(bool value);
        /// <summary>True: the pass may change global state (it is never culled).</summary>
        void AllowGlobalStateModification(bool value);
        void SetGlobalTextureAfterPass(in TextureHandle input, int propertyId);
        /// <summary>A texture only this pass uses.</summary>
        TextureHandle CreateTransientTexture(in TextureDesc desc);
        TextureHandle CreateTransientTexture(in TextureHandle texture);
    }

    /// <summary>A raster pass: the graph binds its attachments (consecutive passes with the same ones share one render pass).</summary>
    public interface IRasterRenderGraphBuilder : IBaseRenderGraphBuilder
    {
        void SetRenderAttachment(TextureHandle tex, int index, AccessFlags flags = AccessFlags.Write);
        void SetRenderAttachment(TextureHandle tex, int index, AccessFlags flags, int mipLevel, int depthSlice);
        void SetInputAttachment(TextureHandle tex, int index, AccessFlags flags = AccessFlags.Read);
        void SetRenderAttachmentDepth(TextureHandle tex, AccessFlags flags = AccessFlags.Write);
        void SetRenderAttachmentDepth(TextureHandle tex, AccessFlags flags, int mipLevel, int depthSlice);
        void SetRenderFunc<PassData>(BaseRenderFunc<PassData, RasterGraphContext> renderFunc) where PassData : class, new();
    }

    public interface IComputeRenderGraphBuilder : IBaseRenderGraphBuilder
    {
        /// <summary>The devices have one queue: the pass runs in order with the others.</summary>
        void EnableAsyncCompute(bool value);
        void SetRenderFunc<PassData>(BaseRenderFunc<PassData, ComputeGraphContext> renderFunc) where PassData : class, new();
    }

    /// <summary>A pass that sets its own targets: the graph binds nothing and merges it with no other pass.</summary>
    public interface IUnsafeRenderGraphBuilder : IBaseRenderGraphBuilder
    {
        void SetRenderFunc<PassData>(BaseRenderFunc<PassData, UnsafeGraphContext> renderFunc) where PassData : class, new();
    }

    internal enum PassKind
    {
        Raster,
        Compute,
        Unsafe,
    }

    internal struct Attachment
    {
        public int texture;
        public AccessFlags flags;
        public int mipLevel;
        public int depthSlice;
    }

    // A recorded pass: what it uses, how it runs; culled and grouped into render passes when the graph compiles.
    internal sealed class GraphPass
    {
        public string name;
        public PassKind kind;
        public object data;
        public Action<RenderGraph> run;
        public readonly List<(int texture, AccessFlags flags)> uses = new List<(int, AccessFlags)>();
        public readonly Attachment[] colors = new Attachment[RenderGraph.kMaxMRTCount];
        public int colorCount;
        public Attachment depth;
        public readonly List<(int index, int texture)> inputs = new List<(int, int)>();
        public readonly List<int> lists = new List<int>();
        public readonly List<int> globalReads = new List<int>();
        public readonly List<(int texture, int propertyId)> globalsAfter = new List<(int, int)>();
        public bool allowCulling = true;
        public bool globalState;
        public bool allGlobals;
        public bool culled;
        public int renderPass = -1;

        // Every texture the pass touches, with how.
        public IEnumerable<(int texture, AccessFlags flags)> All()
        {
            foreach (var u in uses)
                yield return u;
            for (int i = 0; i < colorCount; i++)
                if (colors[i].texture != 0)
                    yield return (colors[i].texture, colors[i].flags);
            if (depth.texture != 0)
                yield return (depth.texture, depth.flags);
            foreach (var i in inputs)
                yield return (i.texture, AccessFlags.Read);
        }
    }

    // The builder of every kind of pass.
    internal sealed class RenderGraphBuilder : IRasterRenderGraphBuilder, IComputeRenderGraphBuilder, IUnsafeRenderGraphBuilder
    {
        readonly RenderGraph m_Graph;
        readonly GraphPass m_Pass;
        bool m_Disposed;

        internal RenderGraphBuilder(RenderGraph graph, GraphPass pass)
        {
            m_Graph = graph;
            m_Pass = pass;
        }

        void CheckOpen()
        {
            if (m_Disposed)
                throw new InvalidOperationException("The builder of render graph pass " + m_Pass.name + " was disposed; record the pass in its using block.");
        }

        int Check(in TextureHandle handle)
        {
            CheckOpen();
            return m_Graph.CheckTexture(handle, m_Pass.name);
        }

        bool IsAttachment(int texture)
        {
            for (int i = 0; i < m_Pass.colorCount; i++)
                if (m_Pass.colors[i].texture == texture)
                    return true;
            return m_Pass.depth.texture == texture;
        }

        public void UseTexture(in TextureHandle input, AccessFlags flags = AccessFlags.Read)
        {
            int t = Check(input);
            if (IsAttachment(t))
                throw new InvalidOperationException($"Pass {m_Pass.name}: a texture set as a render attachment can not also be used as a texture.");
            m_Pass.uses.Add((t, flags));
        }

        public void UseGlobalTexture(int propertyId, AccessFlags flags = AccessFlags.Read)
        {
            CheckOpen();
            m_Pass.globalReads.Add(propertyId);
        }

        public void UseAllGlobalTextures(bool enable)
        {
            CheckOpen();
            m_Pass.allGlobals = enable;
        }

        public void UseRendererList(in RendererListHandle input)
        {
            CheckOpen();
            m_Pass.lists.Add(m_Graph.CheckList(input, m_Pass.name));
        }

        public void AllowPassCulling(bool value)
        {
            CheckOpen();
            m_Pass.allowCulling = value;
        }

        public void AllowGlobalStateModification(bool value)
        {
            CheckOpen();
            m_Pass.globalState = value;
        }

        public void SetGlobalTextureAfterPass(in TextureHandle input, int propertyId) => m_Pass.globalsAfter.Add((Check(input), propertyId));

        public TextureHandle CreateTransientTexture(in TextureDesc desc)
        {
            CheckOpen();
            TextureHandle h = m_Graph.CreateTexture(desc);
            m_Graph.MarkTransient(h);
            return h;
        }

        public TextureHandle CreateTransientTexture(in TextureHandle texture) => CreateTransientTexture(m_Graph.GetTextureDesc(texture));

        void SetColor(TextureHandle tex, int index, AccessFlags flags, int mipLevel, int depthSlice)
        {
            int t = Check(tex);
            if (index < 0 || index >= RenderGraph.kMaxMRTCount)
                throw new ArgumentOutOfRangeException(nameof(index), $"Pass {m_Pass.name}: attachment index {index} is not between 0 and {RenderGraph.kMaxMRTCount - 1}.");
            foreach (var u in m_Pass.uses)
                if (u.texture == t)
                    throw new InvalidOperationException($"Pass {m_Pass.name}: a texture used as a texture can not also be a render attachment.");
            m_Pass.colors[index] = new Attachment { texture = t, flags = flags, mipLevel = mipLevel, depthSlice = depthSlice };
            m_Pass.colorCount = Math.Max(m_Pass.colorCount, index + 1);
        }

        public void SetRenderAttachment(TextureHandle tex, int index, AccessFlags flags = AccessFlags.Write) => SetColor(tex, index, flags, 0, -1);
        public void SetRenderAttachment(TextureHandle tex, int index, AccessFlags flags, int mipLevel, int depthSlice) => SetColor(tex, index, flags, mipLevel, depthSlice);

        public void SetInputAttachment(TextureHandle tex, int index, AccessFlags flags = AccessFlags.Read)
        {
            int t = Check(tex);
            m_Pass.inputs.Add((index, t));
        }

        public void SetRenderAttachmentDepth(TextureHandle tex, AccessFlags flags = AccessFlags.Write) => SetRenderAttachmentDepth(tex, flags, 0, -1);

        public void SetRenderAttachmentDepth(TextureHandle tex, AccessFlags flags, int mipLevel, int depthSlice)
        {
            int t = Check(tex);
            m_Pass.depth = new Attachment { texture = t, flags = flags, mipLevel = mipLevel, depthSlice = depthSlice };
        }

        public void EnableAsyncCompute(bool value) => CheckOpen();

        public void SetRenderFunc<PassData>(BaseRenderFunc<PassData, RasterGraphContext> renderFunc) where PassData : class, new()
        {
            CheckOpen();
            GraphPass pass = m_Pass;
            pass.run = g => renderFunc((PassData)pass.data, new RasterGraphContext { cmd = g.m_RasterCommands, defaultResources = g.defaultResources });
        }

        public void SetRenderFunc<PassData>(BaseRenderFunc<PassData, ComputeGraphContext> renderFunc) where PassData : class, new()
        {
            CheckOpen();
            GraphPass pass = m_Pass;
            pass.run = g => renderFunc((PassData)pass.data, new ComputeGraphContext { cmd = g.m_ComputeCommands, defaultResources = g.defaultResources });
        }

        public void SetRenderFunc<PassData>(BaseRenderFunc<PassData, UnsafeGraphContext> renderFunc) where PassData : class, new()
        {
            CheckOpen();
            GraphPass pass = m_Pass;
            pass.run = g => renderFunc((PassData)pass.data, new UnsafeGraphContext { cmd = g.m_UnsafeCommands, defaultResources = g.defaultResources });
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;
            m_Disposed = true;
            if (m_Pass.run == null)
                throw new InvalidOperationException($"Render graph pass {m_Pass.name} has no render function: call SetRenderFunc before the builder is disposed.");
            m_Graph.EndPass();
        }
    }
}
