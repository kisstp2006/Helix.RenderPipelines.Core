using System;
using HelixEngine.Rendering.Internal;

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
    public partial interface IBaseRenderGraphBuilder : IDisposable
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

    // The builder of every kind of pass: the native core's builder until it is disposed.
    internal sealed unsafe partial class RenderGraphBuilder : IRasterRenderGraphBuilder, IComputeRenderGraphBuilder, IUnsafeRenderGraphBuilder
    {
        readonly RenderGraph m_Graph;
        readonly object m_Data;
        ulong m_Ptr;
        internal readonly string passName;

        internal RenderGraphBuilder(RenderGraph graph, ulong ptr, string name, object data)
        {
            m_Graph = graph;
            m_Ptr = ptr;
            passName = name;
            m_Data = data;
        }

        void CheckOpen()
        {
            if (m_Ptr == 0)
                throw new InvalidOperationException("The builder of render graph pass " + passName + " was disposed; record the pass in its using block.");
        }

        void Check(int kind)
        {
            if (kind != 0)
                RenderGraph.Throw(kind, GraphNative.BuilderError(m_Ptr));
        }

        ulong Texture(in TextureHandle handle)
        {
            CheckOpen();
            m_Graph.CheckTexture(handle, passName);
            return handle.packed;
        }

        public void UseTexture(in TextureHandle input, AccessFlags flags = AccessFlags.Read) => Check(GraphNative.UseTexture(m_Ptr, Texture(input), (int)flags));

        public void UseGlobalTexture(int propertyId, AccessFlags flags = AccessFlags.Read)
        {
            CheckOpen();
            GraphNative.UseGlobalTexture(m_Ptr, propertyId, (int)flags);
        }

        public void UseAllGlobalTextures(bool enable)
        {
            CheckOpen();
            GraphNative.UseAllGlobalTextures(m_Ptr, enable);
        }

        public void UseRendererList(in RendererListHandle input)
        {
            CheckOpen();
            Check(GraphNative.UseRendererList(m_Ptr, input.packed));
        }

        public void AllowPassCulling(bool value)
        {
            CheckOpen();
            GraphNative.AllowPassCulling(m_Ptr, value);
        }

        public void AllowGlobalStateModification(bool value)
        {
            CheckOpen();
            GraphNative.AllowGlobalStateModification(m_Ptr, value);
        }

        public void SetGlobalTextureAfterPass(in TextureHandle input, int propertyId) => Check(GraphNative.SetGlobalTextureAfterPass(m_Ptr, Texture(input), propertyId));

        public TextureHandle CreateTransientTexture(in TextureDesc desc)
        {
            CheckOpen();
            TextureDescData data = desc.Data(m_Graph, out ulong function, out ulong user);
            TextureHandle h = TextureHandle.Unpack(GraphNative.CreateTransientTexture(m_Ptr, data, desc.name ?? "", function, user));
            m_Graph.Created(h, desc);
            return h;
        }

        public TextureHandle CreateTransientTexture(in TextureHandle texture) => CreateTransientTexture(m_Graph.GetTextureDesc(texture));

        public void SetRenderAttachment(TextureHandle tex, int index, AccessFlags flags = AccessFlags.Write) => SetRenderAttachment(tex, index, flags, 0, -1);

        public void SetRenderAttachment(TextureHandle tex, int index, AccessFlags flags, int mipLevel, int depthSlice) =>
            Check(GraphNative.SetRenderAttachment(m_Ptr, Texture(tex), index, (int)flags, mipLevel, depthSlice));

        public void SetInputAttachment(TextureHandle tex, int index, AccessFlags flags = AccessFlags.Read) =>
            Check(GraphNative.SetInputAttachment(m_Ptr, Texture(tex), index, (int)flags));

        public void SetRenderAttachmentDepth(TextureHandle tex, AccessFlags flags = AccessFlags.Write) => SetRenderAttachmentDepth(tex, flags, 0, -1);

        public void SetRenderAttachmentDepth(TextureHandle tex, AccessFlags flags, int mipLevel, int depthSlice) =>
            Check(GraphNative.SetRenderAttachmentDepth(m_Ptr, Texture(tex), (int)flags, mipLevel, depthSlice));

        public void EnableAsyncCompute(bool value) => CheckOpen();

        void SetRun(Action run)
        {
            CheckOpen();
            GraphNative.SetRenderFunc(m_Ptr, (ulong)(delegate* unmanaged[Cdecl]<ulong, ulong, int, void>)&RenderGraph.RunPass, m_Graph.KeepForFrame(run));
        }

        public void SetRenderFunc<PassData>(BaseRenderFunc<PassData, RasterGraphContext> renderFunc) where PassData : class, new()
        {
            var data = (PassData)m_Data;
            RenderGraph g = m_Graph;
            SetRun(() => renderFunc(data, new RasterGraphContext { cmd = g.m_RasterCommands, defaultResources = g.defaultResources }));
        }

        public void SetRenderFunc<PassData>(BaseRenderFunc<PassData, ComputeGraphContext> renderFunc) where PassData : class, new()
        {
            var data = (PassData)m_Data;
            RenderGraph g = m_Graph;
            SetRun(() => renderFunc(data, new ComputeGraphContext { cmd = g.m_ComputeCommands, defaultResources = g.defaultResources }));
        }

        public void SetRenderFunc<PassData>(BaseRenderFunc<PassData, UnsafeGraphContext> renderFunc) where PassData : class, new()
        {
            var data = (PassData)m_Data;
            RenderGraph g = m_Graph;
            SetRun(() => renderFunc(data, new UnsafeGraphContext { cmd = g.m_UnsafeCommands, defaultResources = g.defaultResources }));
        }

        public void Dispose()
        {
            if (m_Ptr == 0)
                return;
            ulong ptr = m_Ptr;
            m_Ptr = 0;
            if (m_Graph.m_OpenBuilder == this)
                m_Graph.m_OpenBuilder = null;
            RenderGraph.Throw(GraphNative.DisposeBuilder(ptr), GraphNative.Error(m_Graph.id));
        }
    }
}
