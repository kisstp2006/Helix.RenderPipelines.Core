#include <Helix/RenderPipelines/Core/Native/GraphNative.h>

namespace helix::rendering
{
    namespace
    {
        // A C# scale function of a graph texture, kept by the graph for the frame.
        struct ManagedScale
        {
            u64 function;
            u64 user;
        };

        Vector2Int CallManaged(Vector2Int size, void* user)
        {
            auto* m = static_cast<ManagedScale*>(user);
            i32 w = size.x, h = size.y;
            reinterpret_cast<void (*)(u64, i32, i32, i32*, i32*)>(m->function)(m->user, size.x, size.y, &w, &h);
            return Vector2Int(w, h);
        }

        RenderGraphBuilder* BuilderOf(u64 builder) { return reinterpret_cast<RenderGraphBuilder*>(uptr(builder)); }

        template <class F>
        i32 Op(u64 builder, F&& f)
        {
            RenderGraphBuilder* b = BuilderOf(builder);
            if (!b)
                return i32(ErrorKind::InvalidOperation);
            f(*b);
            return i32(b->errorKind());
        }
    }

    TextureDesc GraphNative::DescOf(RenderGraph& graph, const TextureDescData& d, StringView name, u64 function, u64 user)
    {
        TextureDesc desc;
        desc.sizeMode = TextureSizeMode(d.sizeMode);
        desc.width = d.width, desc.height = d.height, desc.slices = d.slices, desc.scale = d.scale;
        if (desc.sizeMode == TextureSizeMode::Functor && function)
        {
            auto* m = new ManagedScale{function, user};
            graph.KeepData(m, [](void* p) { delete static_cast<ManagedScale*>(p); });
            desc.func = CallManaged;
            desc.funcUser = m;
        }
        desc.depthBufferBits = DepthBits(d.depthBufferBits);
        desc.colorFormat = GraphicsFormat(d.format);
        desc.filterMode = engine::FilterMode(d.filterMode);
        desc.wrapMode = engine::TextureWrapMode(d.wrapMode);
        desc.dimension = engine::TextureDimension(d.dimension);
        desc.enableRandomWrite = (d.flags & 1) != 0;
        desc.useMipMap = (d.flags & 2) != 0;
        desc.autoGenerateMips = (d.flags & 4) != 0;
        desc.isShadowMap = (d.flags & 8) != 0;
        desc.bindTextureMS = (d.flags & 16) != 0;
        desc.useDynamicScale = (d.flags & 32) != 0;
        desc.clearBuffer = (d.flags & 128) != 0;
        desc.discardBuffer = (d.flags & 256) != 0;
        desc.anisoLevel = d.anisoLevel;
        desc.mipMapBias = d.mipMapBias;
        desc.msaaSamples = MSAASamples(d.msaaSamples < 1 ? 1 : d.msaaSamples);
        desc.memoryless = engine::RenderTextureMemoryless(d.memoryless);
        desc.vrUsage = engine::VRTextureUsage(d.vrUsage);
        desc.clearColor = d.clearColor;
        desc.name = String(name);
        return desc;
    }

    TextureDescData GraphNative::DataOf(const TextureDesc& desc)
    {
        TextureDescData d;
        d.sizeMode = i32(desc.sizeMode);
        d.width = desc.width, d.height = desc.height, d.slices = desc.slices, d.scale = desc.scale;
        d.depthBufferBits = i32(desc.depthBufferBits);
        d.format = i32(desc.colorFormat);
        d.filterMode = i32(desc.filterMode);
        d.wrapMode = i32(desc.wrapMode);
        d.dimension = i32(desc.dimension);
        d.flags = (desc.enableRandomWrite ? 1 : 0) | (desc.useMipMap ? 2 : 0) | (desc.autoGenerateMips ? 4 : 0) | (desc.isShadowMap ? 8 : 0) |
                  (desc.bindTextureMS ? 16 : 0) | (desc.useDynamicScale ? 32 : 0) | (desc.clearBuffer ? 128 : 0) | (desc.discardBuffer ? 256 : 0);
        d.anisoLevel = desc.anisoLevel;
        d.mipMapBias = desc.mipMapBias;
        d.msaaSamples = i32(desc.msaaSamples);
        d.memoryless = i32(desc.memoryless);
        d.vrUsage = i32(desc.vrUsage);
        d.clearColor = desc.clearColor;
        return d;
    }

    void GraphNative::SetNativeRenderPasses(u64 graph, bool enabled)
    {
        if (RenderGraph* g = RenderGraph::Find(graph))
            g->nativeRenderPassesEnabled = enabled;
    }

    bool GraphNative::NativeRenderPasses(u64 graph)
    {
        RenderGraph* g = RenderGraph::Find(graph);
        return g && g->nativeRenderPassesEnabled;
    }

    String GraphNative::Error(u64 graph)
    {
        RenderGraph* g = RenderGraph::Find(graph);
        return g ? String(g->error()) : String("The render graph was disposed.");
    }

    i32 GraphNative::BeginRecording(u64 graph, u64 context, u64 buffer, i32 frameIndex, bool rendererListCulling, StringView executionName)
    {
        RenderGraph* g = RenderGraph::Find(graph);
        if (!g)
            return i32(ErrorKind::InvalidOperation);
        RenderGraphParameters p;
        p.executionName = String(executionName);
        p.currentFrameIndex = frameIndex;
        p.rendererListCulling = rendererListCulling;
        p.scriptableRenderContext = context ? ScriptableRenderContext::Find(context) : nullptr;
        p.commandBuffer = buffer ? CommandBuffer::Find(buffer) : nullptr;
        g->BeginRecording(p);
        return i32(g->errorKind());
    }

    i32 GraphNative::EndRecordingAndExecute(u64 graph)
    {
        RenderGraph* g = RenderGraph::Find(graph);
        if (!g)
            return i32(ErrorKind::InvalidOperation);
        g->EndRecordingAndExecute();
        return i32(g->errorKind());
    }

    u32 GraphNative::Execution(u64 graph)
    {
        RenderGraph* g = RenderGraph::Find(graph);
        return g ? g->execution() : 0;
    }

    String GraphNative::Name(u64 graph)
    {
        RenderGraph* g = RenderGraph::Find(graph);
        return g ? String(g->name()) : String();
    }

    bool GraphNative::Recording(u64 graph)
    {
        RenderGraph* g = RenderGraph::Find(graph);
        return g && g->recording();
    }

    u64 GraphNative::CommandBufferOf(u64 graph)
    {
        RenderGraph* g = RenderGraph::Find(graph);
        return g && g->parameters().commandBuffer ? g->parameters().commandBuffer->Id() : 0;
    }

    u64 GraphNative::ContextOf(u64 graph)
    {
        RenderGraph* g = RenderGraph::Find(graph);
        return g && g->parameters().scriptableRenderContext ? g->parameters().scriptableRenderContext->Id() : 0;
    }

    u64 GraphNative::ExecutingGraph()
    {
        RenderGraph* g = RenderGraph::Executing();
        return g ? g->Id() : 0;
    }

    u64 GraphNative::DefaultTexture(u64 graph, i32 which)
    {
        RenderGraph* g = RenderGraph::Find(graph);
        if (!g)
            return 0;
        const RenderGraphDefaultResources& r = g->defaultResources();
        const TextureHandle all[] = {r.blackTexture,         r.whiteTexture,       r.clearTextureXR,   r.magentaTextureXR, r.blackTextureXR,
                                     r.blackTextureArrayXR, r.blackUIntTextureXR, r.blackTexture3DXR, r.whiteTextureXR,   r.defaultShadowTexture};
        return which >= 0 && which < i32(sizeof(all) / sizeof(all[0])) ? Pack(all[which]) : 0;
    }

    u64 GraphNative::ImportTexture(u64 graph, u64 handle, bool clearOnFirstUse, Color clearColor, bool discardOnLastUse)
    {
        RenderGraph* g = RenderGraph::Find(graph);
        if (!g)
            return 0;
        ImportResourceParams params{clearOnFirstUse, clearColor, discardOnLastUse};
        return Pack(g->ImportTexture(RTHandle::Find(handle), params));
    }

    u64 GraphNative::ImportBackbuffer(u64 graph, TargetData target, i32 width, i32 height, i32 volumeDepth, i32 format, bool clearOnFirstUse, Color clearColor,
                                      bool discardOnLastUse)
    {
        RenderGraph* g = RenderGraph::Find(graph);
        if (!g)
            return 0;
        RenderTargetInfo info;
        info.width = width, info.height = height, info.volumeDepth = volumeDepth, info.format = GraphicsFormat(format);
        ImportResourceParams params{clearOnFirstUse, clearColor, discardOnLastUse};
        return Pack(g->ImportBackbuffer(target.ToIdentifier(), info, params));
    }

    u64 GraphNative::CreateTexture(u64 graph, TextureDescData desc, StringView name, u64 function, u64 user)
    {
        RenderGraph* g = RenderGraph::Find(graph);
        return g ? Pack(g->CreateTexture(DescOf(*g, desc, name, function, user))) : 0;
    }

    TextureDescData GraphNative::GetTextureDesc(u64 graph, u64 texture)
    {
        RenderGraph* g = RenderGraph::Find(graph);
        return g ? DataOf(g->GetTextureDesc(Unpack(texture))) : TextureDescData();
    }

    String GraphNative::TextureName(u64 graph, u64 texture)
    {
        RenderGraph* g = RenderGraph::Find(graph);
        return g ? g->GetTextureDesc(Unpack(texture)).name : String();
    }

    u64 GraphNative::ImportedHandle(u64 graph, u64 texture)
    {
        RenderGraph* g = RenderGraph::Find(graph);
        if (!g)
            return 0;
        TextureHandle h = Unpack(texture);
        if (!h.IsValid() || h.execution != g->execution_ || h.index >= g->TextureCount())
            return 0;
        RTHandle* imported = g->ImportedOf(h);
        return imported ? imported->Id() : 0;
    }

    u64 GraphNative::ImportRendererList(u64 graph, u64 list)
    {
        RenderGraph* g = RenderGraph::Find(graph);
        if (!g)
            return 0;
        RendererListHandle h = g->ImportRendererList(engine::RendererUtils::RendererList{list});
        return u64(h.execution) << 32 | h.index;
    }

    u64 GraphNative::ImportBuffer(u64 graph, u64 buffer)
    {
        RenderGraph* g = RenderGraph::Find(graph);
        return g ? Pack(g->ImportBuffer(engine::GraphicsBuffer::Find(buffer))) : 0;
    }

    u64 GraphNative::CreateBuffer(u64 graph, BufferDescData desc, StringView name)
    {
        RenderGraph* g = RenderGraph::Find(graph);
        return g ? Pack(g->CreateBuffer(BufferDescOf(desc, name))) : 0;
    }

    BufferDescData GraphNative::GetBufferDesc(u64 graph, u64 buffer)
    {
        RenderGraph* g = RenderGraph::Find(graph);
        BufferDescData d;
        if (!g)
            return d;
        const BufferDesc& desc = g->GetBufferDesc(UnpackBuffer(buffer));
        d.count = desc.count;
        d.stride = desc.stride;
        d.target = i32(desc.target);
        d.usageFlags = desc.usageFlags;
        return d;
    }

    String GraphNative::BufferName(u64 graph, u64 buffer)
    {
        RenderGraph* g = RenderGraph::Find(graph);
        return g ? g->GetBufferDesc(UnpackBuffer(buffer)).name : String();
    }

    i32 GraphNative::UseBuffer(u64 builder, u64 buffer, i32 flags)
    {
        return Op(builder, [&](RenderGraphBuilder& b) { b.UseBuffer(UnpackBuffer(buffer), AccessFlags(flags)); });
    }

    u64 GraphNative::CreateTransientBuffer(u64 builder, BufferDescData desc, StringView name)
    {
        RenderGraphBuilder* b = BuilderOf(builder);
        if (!b || !b->graph_)
            return 0;
        return Pack(b->CreateTransientBuffer(BufferDescOf(desc, name)));
    }

    u64 GraphNative::ResolveBuffer(u64 buffer)
    {
        RenderGraph* g = RenderGraph::Executing();
        engine::GraphicsBuffer* b = g ? g->Resolve(UnpackBuffer(buffer)) : nullptr;
        return b ? b->Id() : 0;
    }

    u64 GraphNative::AddPass(u64 graph, StringView name, i32 kind)
    {
        RenderGraph* g = RenderGraph::Find(graph);
        if (!g)
            return 0;
        auto* b = new RenderGraphBuilder(g->AddPass(name, PassKind(kind)));
        return u64(uptr(b));
    }

    i32 GraphNative::DisposeBuilder(u64 builder)
    {
        RenderGraphBuilder* b = BuilderOf(builder);
        if (!b)
            return 0;
        RenderGraph* g = b->graph_;
        if (g)
            g->SetError(ErrorKind::None, String());
        delete b;
        return g ? i32(g->errorKind()) : 0;
    }

    String GraphNative::BuilderError(u64 builder)
    {
        RenderGraphBuilder* b = BuilderOf(builder);
        return b ? String(b->error()) : String();
    }

    i32 GraphNative::UseTexture(u64 builder, u64 texture, i32 flags)
    {
        return Op(builder, [&](RenderGraphBuilder& b) { b.UseTexture(Unpack(texture), AccessFlags(flags)); });
    }

    void GraphNative::UseGlobalTexture(u64 builder, i32 propertyId, i32 flags)
    {
        Op(builder, [&](RenderGraphBuilder& b) { b.UseGlobalTexture(propertyId, AccessFlags(flags)); });
    }

    void GraphNative::UseAllGlobalTextures(u64 builder, bool enable)
    {
        Op(builder, [&](RenderGraphBuilder& b) { b.UseAllGlobalTextures(enable); });
    }

    i32 GraphNative::UseRendererList(u64 builder, u64 list)
    {
        return Op(builder, [&](RenderGraphBuilder& b) { b.UseRendererList(RendererListHandle{u32(list), u32(list >> 32)}); });
    }

    void GraphNative::AllowPassCulling(u64 builder, bool value)
    {
        Op(builder, [&](RenderGraphBuilder& b) { b.AllowPassCulling(value); });
    }

    void GraphNative::AllowGlobalStateModification(u64 builder, bool value)
    {
        Op(builder, [&](RenderGraphBuilder& b) { b.AllowGlobalStateModification(value); });
    }

    i32 GraphNative::SetGlobalTextureAfterPass(u64 builder, u64 texture, i32 propertyId)
    {
        return Op(builder, [&](RenderGraphBuilder& b) { b.SetGlobalTextureAfterPass(Unpack(texture), propertyId); });
    }

    u64 GraphNative::CreateTransientTexture(u64 builder, TextureDescData desc, StringView name, u64 function, u64 user)
    {
        RenderGraphBuilder* b = BuilderOf(builder);
        if (!b || !b->graph_)
            return 0;
        return Pack(b->CreateTransientTexture(DescOf(*b->graph_, desc, name, function, user)));
    }

    i32 GraphNative::SetRenderAttachment(u64 builder, u64 texture, i32 index, i32 flags, i32 mipLevel, i32 depthSlice)
    {
        return Op(builder, [&](RenderGraphBuilder& b) { b.SetRenderAttachment(Unpack(texture), index, AccessFlags(flags), mipLevel, depthSlice); });
    }

    i32 GraphNative::SetInputAttachment(u64 builder, u64 texture, i32 index, i32 flags)
    {
        return Op(builder, [&](RenderGraphBuilder& b) { b.SetInputAttachment(Unpack(texture), index, AccessFlags(flags)); });
    }

    i32 GraphNative::SetRenderAttachmentDepth(u64 builder, u64 texture, i32 flags, i32 mipLevel, i32 depthSlice)
    {
        return Op(builder, [&](RenderGraphBuilder& b) { b.SetRenderAttachmentDepth(Unpack(texture), AccessFlags(flags), mipLevel, depthSlice); });
    }

    void GraphNative::SetRenderFunc(u64 builder, u64 function, u64 user)
    {
        Op(builder, [&](RenderGraphBuilder& b) { b.SetManagedRenderFunc(function, user); });
    }

    void GraphNative::EndFrame(u64 graph)
    {
        if (RenderGraph* g = RenderGraph::Find(graph))
            g->EndFrame();
    }

    void GraphNative::Cleanup(u64 graph)
    {
        if (RenderGraph* g = RenderGraph::Find(graph))
            g->Cleanup();
    }

    RenderGraphStatistics GraphNative::Statistics(u64 graph)
    {
        RenderGraph* g = RenderGraph::Find(graph);
        return g ? g->statistics() : RenderGraphStatistics();
    }

    bool GraphNative::Executing(u32 execution)
    {
        RenderGraph* g = RenderGraph::Executing();
        return g && g->execution() == execution;
    }

    u64 GraphNative::Resolve(u64 texture)
    {
        RenderGraph* g = RenderGraph::Executing();
        RTHandle* h = g ? g->Resolve(Unpack(texture)) : nullptr;
        return h ? h->Id() : 0;
    }

    u64 GraphNative::ResolveList(u64 list)
    {
        RenderGraph* g = RenderGraph::Executing();
        return g ? g->ResolveList(RendererListHandle{u32(list), u32(list >> 32)}).id : 0;
    }
}
