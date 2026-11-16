#include <Helix/RenderPipelines/Core/RenderGraph/RenderGraph.h>
#include <Helix/RenderPipelines/Core/Utilities/CoreUtils.h>

#include <Helix/Core/Containers/HashMap.h>
#include <Helix/Core/Strings/Format.h>
#include <Helix/Engine/Texture/Texture.h>

namespace helix::rendering
{
    namespace
    {
        constexpr i32 kStaleFrames = 10;
        u32 gNextExecution = 0;
        thread_local RenderGraph* gExecuting = nullptr;

        HashMap<u64, RenderGraph*>& Graphs()
        {
            static HashMap<u64, RenderGraph*> graphs;
            return graphs;
        }
        u64 gNextGraph = 0;

        struct Attachment
        {
            u32 texture = 0;
            AccessFlags flags = AccessFlags::None;
            i32 mipLevel = 0, depthSlice = -1;
        };

        struct Use
        {
            u32 texture;
            AccessFlags flags;
        };

        struct Input
        {
            i32 index;
            u32 texture;
        };

        struct GlobalAfter
        {
            u32 texture;
            i32 propertyId;
        };

        struct Pass
        {
            String name;
            PassKind kind = PassKind::Raster;
            detail::PassCallable* run = nullptr;
            Array<Use> uses = Array<Use>();
            Attachment colors[RenderGraph::kMaxMRTCount] = {};
            i32 colorCount = 0;
            Attachment depth;
            Array<Input> inputs = Array<Input>();
            Array<u32> lists = Array<u32>();
            Array<i32> globalReads = Array<i32>();
            Array<GlobalAfter> globalsAfter = Array<GlobalAfter>();
            bool allowCulling = true, globalState = false, allGlobals = false, culled = false;
            i32 renderPass = -1;
            // Added while the graph could not take it (or disposed without a render function): never runs nor counts.
            bool broken = false;

            template <class F>
            void All(F&& f) const
            {
                for (const Use& u : uses)
                    f(u.texture, u.flags);
                for (i32 i = 0; i < colorCount; i++)
                    if (colors[i].texture)
                        f(colors[i].texture, colors[i].flags);
                if (depth.texture)
                    f(depth.texture, depth.flags);
                for (const Input& u : inputs)
                    f(u.texture, AccessFlags::Read);
            }
        };

        struct PoolEntry
        {
            RTHandle* handle = nullptr;
            TextureDesc desc;
            Vector2Int size;
            bool inUse = false, usedThisFrame = false;
            i32 lastFrame = 0;
        };

        struct GraphTexture
        {
            TextureDesc desc;
            RTHandle* imported = nullptr;
            ImportResourceParams import;
            bool backbuffer = false, transient = false, hasContents = false;
            PoolEntry* pooled = nullptr;
            i32 firstPass = -1, lastPass = -1;

            RTHandle* Handle() const { return imported ? imported : pooled ? pooled->handle : nullptr; }
            bool isImported() const { return imported != nullptr; }
        };

        struct GraphList
        {
            engine::RendererUtils::RendererList list;
        };

        struct Group
        {
            Array<u32> passes = Array<u32>();
            bool raster = false;
        };

        bool Reads(AccessFlags f) { return Has(f, AccessFlags::Read) || (Has(f, AccessFlags::Write) && !Has(f, AccessFlags::Discard)); }
        bool Writes(AccessFlags f) { return Has(f, AccessFlags::Write); }

        bool SameKind(const TextureDesc& a, const TextureDesc& b)
        {
            return a.slices == b.slices && a.depthBufferBits == b.depthBufferBits && a.colorFormat == b.colorFormat && a.filterMode == b.filterMode &&
                   a.wrapMode == b.wrapMode && a.dimension == b.dimension && a.enableRandomWrite == b.enableRandomWrite && a.useMipMap == b.useMipMap &&
                   a.autoGenerateMips == b.autoGenerateMips && a.isShadowMap == b.isShadowMap && a.anisoLevel == b.anisoLevel && a.mipMapBias == b.mipMapBias &&
                   a.msaaSamples == b.msaaSamples && a.bindTextureMS == b.bindTextureMS && a.useDynamicScale == b.useDynamicScale && a.memoryless == b.memoryless &&
                   a.vrUsage == b.vrUsage;
        }
    }

    struct RenderGraph::Impl
    {
        RenderGraphParameters parameters;
        Array<GraphTexture> textures = Array<GraphTexture>();
        Array<GraphList> lists = Array<GraphList>();
        Array<Pass> passes = Array<Pass>();
        Array<Group> groups = Array<Group>();
        Array<PoolEntry*> pool = Array<PoolEntry*>();
        HashMap<u64, RTHandle*> backbuffers;
        struct Kept
        {
            void* data;
            void (*destroy)(void*);
        };
        Array<Kept> kept = Array<Kept>();
        i32 openPass = -1;
        i32 frame = 0;
        RTHandle *black = nullptr, *white = nullptr, *clear = nullptr, *magenta = nullptr;
        engine::EntityId clearTexture, magentaTexture;
        String passError = String();
        ErrorKind passErrorKind = ErrorKind::None;
        void SetPassError(ErrorKind kind, String message)
        {
            passErrorKind = kind;
            passError = std::move(message);
        }

        void Compile(RenderGraphStatistics& stats);
        void Execute(RenderGraph& graph, RenderGraphStatistics& stats);
        void Allocate(u32 first, u32 last, RenderGraphStatistics& stats);
        void Free(u32 first, u32 last);
        engine::RenderBufferLoadAction LoadOf(u32 texture, AccessFlags flags) const;
        Color ClearColorOf(u32 texture) const;
        engine::RenderBufferStoreAction StoreOf(u32 texture, u32 lastPassOfGroup) const;
        void ClearFirstUses(CommandBuffer& cmd, const Pass& pass);
        void Bind(CommandBuffer& cmd, const Pass& head);
        void Run(RenderGraph& graph, CommandBuffer& cmd, Pass& pass);
        void RunNative(RenderGraph& graph, ScriptableRenderContext& context, CommandBuffer& cmd, const Group& group);

        void FreeFrame()
        {
            for (Pass& p : passes)
                delete p.run;
            passes.Clear();
            for (Kept& k : kept)
                k.destroy(k.data);
            kept.Clear();
            for (GraphTexture& t : textures)
                if (t.pooled)
                    t.pooled->inUse = false;
            textures.Clear();
            lists.Clear();
            groups.Clear();
        }

        PoolEntry* Acquire(const TextureDesc& desc, Vector2Int size, RenderGraphStatistics& stats)
        {
            for (PoolEntry* e : pool)
                if (!e->inUse && e->size == size && SameKind(e->desc, desc))
                {
                    if (e->usedThisFrame)
                        stats.aliasedTextures++;
                    e->inUse = e->usedThisFrame = true;
                    e->lastFrame = frame;
                    return e;
                }
            RTHandleAllocInfo info;
            info.slices = desc.slices;
            info.depthBufferBits = desc.depthBufferBits;
            info.format = desc.colorFormat;
            info.filterMode = desc.filterMode;
            info.wrapMode = desc.wrapMode;
            info.dimension = desc.dimension;
            info.enableRandomWrite = desc.enableRandomWrite;
            info.useMipMap = desc.useMipMap;
            info.autoGenerateMips = desc.autoGenerateMips;
            info.isShadowMap = desc.isShadowMap;
            info.anisoLevel = desc.anisoLevel;
            info.mipMapBias = desc.mipMapBias;
            info.msaaSamples = desc.msaaSamples;
            info.bindTextureMS = desc.bindTextureMS;
            info.useDynamicScale = desc.useDynamicScale;
            info.memoryless = desc.memoryless;
            info.vrUsage = desc.vrUsage;
            info.name = desc.name.Empty() ? String("RenderGraphTexture") : desc.name;
            auto* e = new PoolEntry();
            e->handle = RTHandles::DefaultInstance().Alloc(size.x, size.y, info);
            e->desc = desc;
            e->size = size;
            e->inUse = e->usedThisFrame = true;
            e->lastFrame = frame;
            pool.Push(e);
            stats.pooledTexturesCreated++;
            return e;
        }
    };

    // ------------------------------------------------------------------------------------------------ TextureDesc

    TextureDesc::TextureDesc(const RTHandle& h)
    {
        if (RenderTexture* rt = h.rt())
        {
            const RenderTextureDescriptor& d = rt->descriptor();
            width = d.width, height = d.height, slices = d.volumeDepth, dimension = d.dimension, colorFormat = d.graphicsFormat;
            depthBufferBits = DepthBits(d.depthBufferBits());
            enableRandomWrite = d.enableRandomWrite, useMipMap = d.useMipMap, autoGenerateMips = d.autoGenerateMips;
            msaaSamples = MSAASamples(d.msaaSamples < 1 ? 1 : d.msaaSamples), bindTextureMS = d.bindMS, useDynamicScale = d.useDynamicScale;
            memoryless = d.memoryless, vrUsage = d.vrUsage;
            filterMode = rt->filterMode(), wrapMode = rt->wrapMode(), anisoLevel = rt->anisoLevel(), mipMapBias = rt->mipMapBias();
        }
        if (h.useScaling())
        {
            sizeMode = h.scaleFunc() ? TextureSizeMode::Functor : TextureSizeMode::Scale;
            scale = h.scaleFactor();
            func = h.scaleFunc();
            funcUser = h.scaleUser();
        }
        name = String(h.name());
    }

    Vector2Int TextureDesc::Size(Vector2Int reference) const
    {
        auto max1 = [](i32 v) { return v < 1 ? 1 : v; };
        switch (sizeMode)
        {
        case TextureSizeMode::Scale:
            return Vector2Int(max1(i32(scale.x * f32(reference.x) + 0.5f)), max1(i32(scale.y * f32(reference.y) + 0.5f)));
        case TextureSizeMode::Functor:
        {
            Vector2Int s = func ? func(reference, funcUser) : reference;
            return Vector2Int(max1(s.x), max1(s.y));
        }
        default:
            return Vector2Int(max1(width), max1(height));
        }
    }

    // ------------------------------------------------------------------------------------------------ the builder

    void RenderGraphBuilder::Dispose()
    {
        if (!graph_)
            return;
        RenderGraph& g = *graph_;
        graph_ = nullptr;
        if (g.impl_->openPass != i32(pass_))
            return;
        Pass& p = g.impl_->passes[pass_];
        g.impl_->openPass = -1;
        if (!p.run)
        {
            g.SetError(ErrorKind::InvalidOperation, Format("Render graph pass {} has no render function: call SetRenderFunc before the builder is disposed.", p.name));
            p.broken = true;
        }
    }

    StringView RenderGraphBuilder::error() const { return graph_ ? StringView(graph_->impl_->passError) : StringView(); }
    ErrorKind RenderGraphBuilder::errorKind() const { return graph_ ? graph_->impl_->passErrorKind : ErrorKind::None; }

    void RenderGraphBuilder::SetCallable(detail::PassCallable* callable)
    {
        if (!graph_)
        {
            delete callable;
            return;
        }
        Pass& p = graph_->impl_->passes[pass_];
        delete p.run;
        p.run = callable;
    }

    void RenderGraphBuilder::SetManagedRenderFunc(u64 function, u64 user) { SetCallable(new detail::ManagedCallable(function, user)); }

    void RenderGraphBuilder::UseTexture(TextureHandle texture, AccessFlags flags)
    {
        RenderGraph::Impl& m = *graph_->impl_;
        m.SetPassError(ErrorKind::None, String());
        u32 t = graph_->CheckTexture(texture);
        if (!t)
        {
            m.SetPassError(graph_->errorKind_, graph_->error_);
            return;
        }
        Pass& p = m.passes[pass_];
        for (i32 i = 0; i < p.colorCount; i++)
            if (p.colors[i].texture == t)
            {
                m.SetPassError(ErrorKind::InvalidOperation, Format("Pass {}: a texture set as a render attachment can not also be used as a texture.", p.name));
                return;
            }
        if (p.depth.texture == t)
        {
            m.SetPassError(ErrorKind::InvalidOperation, Format("Pass {}: a texture set as a render attachment can not also be used as a texture.", p.name));
            return;
        }
        p.uses.Push({t, flags});
    }

    void RenderGraphBuilder::UseGlobalTexture(i32 propertyId, AccessFlags) { graph_->impl_->passes[pass_].globalReads.Push(propertyId); }
    void RenderGraphBuilder::UseAllGlobalTextures(bool enable) { graph_->impl_->passes[pass_].allGlobals = enable; }

    void RenderGraphBuilder::UseRendererList(RendererListHandle list)
    {
        RenderGraph::Impl& m = *graph_->impl_;
        m.SetPassError(ErrorKind::None, String());
        if (!list.IsValid() || list.execution != graph_->execution_ || list.index >= m.lists.Size())
        {
            m.SetPassError(ErrorKind::Argument, Format("Pass {}: the renderer list handle is not valid (null, or made in another frame or graph).", m.passes[pass_].name));
            return;
        }
        m.passes[pass_].lists.Push(list.index);
    }

    void RenderGraphBuilder::AllowPassCulling(bool value) { graph_->impl_->passes[pass_].allowCulling = value; }
    void RenderGraphBuilder::AllowGlobalStateModification(bool value) { graph_->impl_->passes[pass_].globalState = value; }

    void RenderGraphBuilder::SetGlobalTextureAfterPass(TextureHandle texture, i32 propertyId)
    {
        RenderGraph::Impl& m = *graph_->impl_;
        m.SetPassError(ErrorKind::None, String());
        if (u32 t = graph_->CheckTexture(texture))
            m.passes[pass_].globalsAfter.Push({t, propertyId});
        else
            m.SetPassError(graph_->errorKind_, graph_->error_);
    }

    TextureHandle RenderGraphBuilder::CreateTransientTexture(const TextureDesc& desc)
    {
        TextureHandle h = graph_->CreateTexture(desc);
        if (h.IsValid())
            graph_->impl_->textures[h.index].transient = true;
        return h;
    }

    TextureHandle RenderGraphBuilder::CreateTransientTexture(TextureHandle texture) { return CreateTransientTexture(graph_->GetTextureDesc(texture)); }

    void RenderGraphBuilder::SetRenderAttachment(TextureHandle texture, i32 index, AccessFlags flags, i32 mipLevel, i32 depthSlice)
    {
        RenderGraph::Impl& m = *graph_->impl_;
        m.SetPassError(ErrorKind::None, String());
        u32 t = graph_->CheckTexture(texture);
        Pass& p = m.passes[pass_];
        if (!t)
        {
            m.SetPassError(graph_->errorKind_, graph_->error_);
            return;
        }
        if (index < 0 || index >= RenderGraph::kMaxMRTCount)
        {
            m.SetPassError(ErrorKind::OutOfRange, Format("Pass {}: attachment index {} is not between 0 and {}.", p.name, index, RenderGraph::kMaxMRTCount - 1));
            return;
        }
        for (const Use& u : p.uses)
            if (u.texture == t)
            {
                m.SetPassError(ErrorKind::InvalidOperation, Format("Pass {}: a texture used as a texture can not also be a render attachment.", p.name));
                return;
            }
        p.colors[index] = Attachment{t, flags, mipLevel, depthSlice};
        p.colorCount = index + 1 > p.colorCount ? index + 1 : p.colorCount;
    }

    void RenderGraphBuilder::SetInputAttachment(TextureHandle texture, i32 index, AccessFlags)
    {
        RenderGraph::Impl& m = *graph_->impl_;
        m.SetPassError(ErrorKind::None, String());
        if (u32 t = graph_->CheckTexture(texture))
            m.passes[pass_].inputs.Push({index, t});
        else
            m.SetPassError(graph_->errorKind_, graph_->error_);
    }

    void RenderGraphBuilder::SetRenderAttachmentDepth(TextureHandle texture, AccessFlags flags, i32 mipLevel, i32 depthSlice)
    {
        RenderGraph::Impl& m = *graph_->impl_;
        m.SetPassError(ErrorKind::None, String());
        if (u32 t = graph_->CheckTexture(texture))
            m.passes[pass_].depth = Attachment{t, flags, mipLevel, depthSlice};
        else
            m.SetPassError(graph_->errorKind_, graph_->error_);
    }

    // ------------------------------------------------------------------------------------------------ recording

    RenderGraph::RenderGraph(StringView name) : name_(name), impl_(new Impl())
    {
        id_ = ++gNextGraph;
        Graphs().Set(id_, this);
    }

    RenderGraph::~RenderGraph()
    {
        Cleanup();
        impl_->FreeFrame();
        delete impl_;
        Graphs().Remove(id_);
    }

    RenderGraph* RenderGraph::Find(u64 id)
    {
        RenderGraph** g = Graphs().Find(id);
        return g ? *g : nullptr;
    }

    const RenderGraphParameters& RenderGraph::parameters() const { return impl_->parameters; }

    bool RenderGraph::BeginRecording(const RenderGraphParameters& parameters)
    {
        SetError(ErrorKind::None, String());
        if (recording_)
            return Fail(ErrorKind::InvalidOperation, "RenderGraph.BeginRecording: the graph is already recording; call EndRecordingAndExecute first.", false);
        if (!parameters.commandBuffer)
            return Fail(ErrorKind::Argument, "RenderGraphParameters.commandBuffer is null.", false);
        impl_->FreeFrame();
        impl_->parameters = parameters;
        recording_ = true;
        execution_ = ++gNextExecution;
        impl_->textures.Emplace();
        impl_->lists.Emplace();
        impl_->openPass = -1;
        ImportDefaults();
        return true;
    }

    void RenderGraph::ImportDefaults()
    {
        Impl& m = *impl_;
        if (!m.black)
        {
            engine::Texture2D* clear = engine::Texture2D::Create(1, 1, engine::TextureFormat::RGBA32, false, true);
            clear->SetName("Clear");
            clear->hideFlags = engine::HideFlags::HideAndDontSave;
            clear->SetPixel(0, 0, Color(0, 0, 0, 0));
            clear->Apply(false);
            engine::Texture2D* magenta = engine::Texture2D::Create(1, 1, engine::TextureFormat::RGBA32, false, true);
            magenta->SetName("Magenta");
            magenta->hideFlags = engine::HideFlags::HideAndDontSave;
            magenta->SetPixel(0, 0, Color(1, 0, 1, 1));
            magenta->Apply(false);
            m.clearTexture = clear->GetEntityId();
            m.magentaTexture = magenta->GetEntityId();
            m.black = RTHandles::DefaultInstance().Alloc(engine::Texture2D::blackTexture());
            m.white = RTHandles::DefaultInstance().Alloc(engine::Texture2D::whiteTexture());
            m.clear = RTHandles::DefaultInstance().Alloc(clear);
            m.magenta = RTHandles::DefaultInstance().Alloc(magenta);
        }
        defaults_.blackTexture = ImportTexture(m.black);
        defaults_.whiteTexture = ImportTexture(m.white);
        defaults_.clearTextureXR = ImportTexture(m.clear);
        defaults_.magentaTextureXR = ImportTexture(m.magenta);
        defaults_.blackTextureXR = defaults_.blackTextureArrayXR = defaults_.blackUIntTextureXR = defaults_.blackTexture3DXR = defaults_.blackTexture;
        defaults_.whiteTextureXR = defaults_.defaultShadowTexture = defaults_.whiteTexture;
    }

    u32 RenderGraph::CheckTexture(TextureHandle texture)
    {
        if (!texture.IsValid() || texture.execution != execution_ || texture.index >= impl_->textures.Size())
        {
            SetError(ErrorKind::Argument, "The texture handle is not valid (null, or made in another frame or graph).");
            return 0;
        }
        return texture.index;
    }

    TextureHandle RenderGraph::ImportTexture(RTHandle* handle, const ImportResourceParams& params)
    {
        if (!recording_ || !handle)
            return !handle ? Fail(ErrorKind::Argument, "RenderGraph.ImportTexture: the handle is null.", TextureHandle{})
                           : Fail(ErrorKind::InvalidOperation, "RenderGraph.ImportTexture: the graph is not recording.", TextureHandle{});
        GraphTexture& t = impl_->textures.Emplace();
        t.imported = handle;
        t.import = params;
        t.desc = TextureDesc(*handle);
        return {u32(impl_->textures.Size() - 1), execution_};
    }

    TextureHandle RenderGraph::ImportBackbuffer(const RenderTargetIdentifier& target, const RenderTargetInfo& info, const ImportResourceParams& params)
    {
        if (!recording_)
            return Fail(ErrorKind::InvalidOperation, "RenderGraph.ImportBackbuffer: the graph is not recording.", TextureHandle{});
        u64 key = (u64(u32(target.type)) << 32) ^ u64(u32(target.nameID)) ^ engine::EntityId::ToULong(target.texture) * 31;
        RTHandle** found = impl_->backbuffers.Find(key);
        RTHandle* h = found ? *found : impl_->backbuffers.Set(key, RTHandles::DefaultInstance().Alloc(target, "Backbuffer"));
        GraphTexture& t = impl_->textures.Emplace();
        t.imported = h;
        t.import = params;
        t.backbuffer = true;
        t.desc = TextureDesc(info.width < 1 ? 1 : info.width, info.height < 1 ? 1 : info.height);
        t.desc.colorFormat = info.format;
        t.desc.slices = info.volumeDepth < 1 ? 1 : info.volumeDepth;
        t.desc.name = "Backbuffer";
        return {u32(impl_->textures.Size() - 1), execution_};
    }

    TextureHandle RenderGraph::CreateTexture(const TextureDesc& desc)
    {
        if (!recording_)
            return Fail(ErrorKind::InvalidOperation, "RenderGraph.CreateTexture: the graph is not recording.", TextureHandle{});
        impl_->textures.Emplace().desc = desc;
        return {u32(impl_->textures.Size() - 1), execution_};
    }

    const TextureDesc& RenderGraph::GetTextureDesc(TextureHandle texture) const
    {
        static const TextureDesc none;
        if (!texture.IsValid() || texture.execution != execution_ || texture.index >= impl_->textures.Size())
            return none;
        return impl_->textures[texture.index].desc;
    }

    RendererListHandle RenderGraph::ImportRendererList(engine::RendererUtils::RendererList list)
    {
        impl_->lists.Emplace().list = list;
        return {u32(impl_->lists.Size() - 1), execution_};
    }

    RendererListHandle RenderGraph::CreateRendererList(const engine::RendererUtils::RendererListDesc& desc)
    {
        ScriptableRenderContext* c = impl_->parameters.scriptableRenderContext;
        return ImportRendererList(c && desc.IsValid() ? c->CreateRendererList(desc) : engine::RendererUtils::RendererList::nullRendererList());
    }

    RendererListHandle RenderGraph::CreateRendererList(const engine::RendererListParams& desc)
    {
        ScriptableRenderContext* c = impl_->parameters.scriptableRenderContext;
        return ImportRendererList(c ? c->CreateRendererList(desc) : engine::RendererUtils::RendererList::nullRendererList());
    }

    RendererListHandle RenderGraph::CreateSkyboxRendererList(engine::Camera* camera)
    {
        ScriptableRenderContext* c = impl_->parameters.scriptableRenderContext;
        return ImportRendererList(c && camera ? c->CreateSkyboxRendererList(camera) : engine::RendererUtils::RendererList::nullRendererList());
    }

    RendererListHandle RenderGraph::CreateShadowRendererList(const engine::ShadowDrawingSettings& settings)
    {
        ScriptableRenderContext* c = impl_->parameters.scriptableRenderContext;
        return ImportRendererList(c ? c->CreateShadowRendererList(settings) : engine::RendererUtils::RendererList::nullRendererList());
    }

    u32 RenderGraph::BeginPass(StringView name, PassKind kind)
    {
        SetError(ErrorKind::None, String());
        if (!recording_)
            SetError(ErrorKind::InvalidOperation, "RenderGraph: a pass was added while the graph is not recording (call BeginRecording first).");
        else if (impl_->openPass >= 0)
            SetError(ErrorKind::InvalidOperation, Format("RenderGraph: pass {} was added while the builder of pass {} is open; dispose it first.", name, impl_->passes[u32(impl_->openPass)].name));
        Pass& p = impl_->passes.Emplace();
        p.name = String(name);
        p.kind = kind;
        if (!error_.Empty())
            p.broken = true;
        impl_->openPass = i32(impl_->passes.Size() - 1);
        return u32(impl_->passes.Size() - 1);
    }

    RenderGraphBuilder RenderGraph::AddPass(StringView name, PassKind kind) { return RenderGraphBuilder(*this, BeginPass(name, kind)); }

    void RenderGraph::KeepData(void* data, void (*destroy)(void*)) { impl_->kept.Push({data, destroy}); }

    // ------------------------------------------------------------------------------------------------ lifetime

    void RenderGraph::EndFrame()
    {
        Impl& m = *impl_;
        m.frame++;
        for (usize i = m.pool.Size(); i-- > 0;)
            if (!m.pool[i]->inUse && m.frame - m.pool[i]->lastFrame > kStaleFrames)
            {
                m.pool[i]->handle->Release();
                delete m.pool[i];
                m.pool.RemoveAt(i);
            }
    }

    void RenderGraph::Cleanup()
    {
        Impl& m = *impl_;
        for (PoolEntry* e : m.pool)
        {
            if (RTHandle::Find(e->handle ? e->handle->Id() : 0))
                e->handle->Release();
            delete e;
        }
        m.pool.Clear();
        for (auto e : m.backbuffers)
            if (RTHandle::Find(e.value->Id()))
                e.value->Release();
        m.backbuffers.Clear();
        for (RTHandle* h : {m.black, m.white, m.clear, m.magenta})
            if (h && RTHandle::Find(h->Id()))
                h->Release();
        m.black = m.white = m.clear = m.magenta = nullptr;
        CoreUtils::Destroy(engine::EntityIdToObject(m.clearTexture));
        CoreUtils::Destroy(engine::EntityIdToObject(m.magentaTexture));
        m.clearTexture = m.magentaTexture = engine::EntityId();
    }

    RenderGraph* RenderGraph::Executing() { return gExecuting; }

    usize RenderGraph::TextureCount() const { return impl_->textures.Size(); }

    RTHandle* RenderGraph::ImportedOf(TextureHandle texture) const
    {
        if (texture.execution != execution_ || texture.index == 0 || texture.index >= impl_->textures.Size())
            return nullptr;
        return impl_->textures[texture.index].imported;
    }

    RTHandle* RenderGraph::Resolve(TextureHandle texture) const
    {
        if (!executing_ || texture.execution != execution_ || texture.index == 0 || texture.index >= impl_->textures.Size())
            return nullptr;
        return impl_->textures[texture.index].Handle();
    }

    engine::RendererUtils::RendererList RenderGraph::ResolveList(RendererListHandle list) const
    {
        if (!executing_ || list.execution != execution_ || list.index == 0 || list.index >= impl_->lists.Size())
            return engine::RendererUtils::RendererList::nullRendererList();
        return impl_->lists[list.index].list;
    }

    // ------------------------------------------------------------------------------------------------ compile

    bool RenderGraph::EndRecordingAndExecute()
    {
        Impl& m = *impl_;
        if (!recording_)
            return Fail(ErrorKind::InvalidOperation, "RenderGraph.EndRecordingAndExecute: BeginRecording was not called.", false);
        if (m.openPass >= 0)
        {
            SetError(ErrorKind::InvalidOperation, Format("RenderGraph.EndRecordingAndExecute: the builder of pass {} was not disposed.", m.passes[u32(m.openPass)].name));
            m.passes[u32(m.openPass)].broken = true;
            m.openPass = -1;
        }
        else
            SetError(ErrorKind::None, String());
        stats_ = RenderGraphStatistics();
        m.Compile(stats_);
        executing_ = true;
        RenderGraph* outer = gExecuting;
        gExecuting = this;
        m.Execute(*this, stats_);
        gExecuting = outer;
        executing_ = false;
        for (GraphTexture& t : m.textures)
            if (t.pooled)
            {
                t.pooled->inUse = false;
                t.pooled = nullptr;
            }
        recording_ = false;
        return error_.Empty();
    }

    namespace
    {
        bool SameAttachments(const Pass& a, const Pass& b)
        {
            if (a.colorCount != b.colorCount || a.depth.texture != b.depth.texture || a.depth.mipLevel != b.depth.mipLevel || a.depth.depthSlice != b.depth.depthSlice)
                return false;
            for (i32 i = 0; i < a.colorCount; i++)
                if (a.colors[i].texture != b.colors[i].texture || a.colors[i].mipLevel != b.colors[i].mipLevel || a.colors[i].depthSlice != b.colors[i].depthSlice)
                    return false;
            return a.colorCount > 0 || a.depth.texture != 0;
        }

        bool ReadsAttachmentOf(const Pass& pass, const Pass& first)
        {
            for (const Use& use : pass.uses)
            {
                for (i32 i = 0; i < first.colorCount; i++)
                    if (first.colors[i].texture == use.texture)
                        return true;
                if (first.depth.texture == use.texture)
                    return true;
            }
            return false;
        }

        bool Contains(const Array<i32>& values, i32 v)
        {
            for (i32 x : values)
                if (x == v)
                    return true;
            return false;
        }

        i32 IndexOf(const Array<u32>& values, u32 v)
        {
            for (usize i = 0; i < values.Size(); i++)
                if (values[i] == v)
                    return i32(i);
            return -1;
        }
    }

    void RenderGraph::Impl::Compile(RenderGraphStatistics& stats)
    {
        u32 count = u32(passes.Size());
        for (const Pass& p : passes)
            stats.passes += p.broken ? 0 : 1;

        // Culling, from the last pass back: a pass runs when it may not be culled, changes global state, or writes
        // what a later running pass reads, an imported texture keeps, or a global a later pass reads.
        Array<u8> needed;
        needed.Resize(textures.Size());
        for (usize t = 1; t < textures.Size(); t++)
            needed[t] = textures[t].isImported() && !textures[t].import.discardOnLastUse;
        Array<i32> neededGlobals;
        bool allGlobals = false;
        for (u32 p = count; p-- > 0;)
        {
            Pass& pass = passes[p];
            if (pass.broken)
            {
                pass.culled = true;
                continue;
            }
            bool alive = !pass.allowCulling || pass.globalState;
            pass.All([&](u32 t, AccessFlags f) { alive = alive || (Writes(f) && needed[t]); });
            for (const GlobalAfter& g : pass.globalsAfter)
                alive = alive || allGlobals || Contains(neededGlobals, g.propertyId);
            pass.culled = !alive;
            if (!alive)
            {
                stats.culledPasses++;
                continue;
            }
            for (const GlobalAfter& g : pass.globalsAfter)
            {
                for (usize i = neededGlobals.Size(); i-- > 0;)
                    if (neededGlobals[i] == g.propertyId)
                        neededGlobals.RemoveAt(i);
                needed[g.texture] = 1;
            }
            pass.All([&](u32 t, AccessFlags f) {
                if (Writes(f))
                    needed[t] = 0;
            });
            pass.All([&](u32 t, AccessFlags f) {
                if (Reads(f))
                    needed[t] = 1;
            });
            for (i32 id : pass.globalReads)
                if (!Contains(neededGlobals, id))
                    neededGlobals.Push(id);
            allGlobals = allGlobals || pass.allGlobals;
        }

        // Lifetimes over the passes that run.
        for (u32 p = 0; p < count; p++)
        {
            const Pass& pass = passes[p];
            if (pass.culled)
                continue;
            pass.All([&](u32 t, AccessFlags) {
                GraphTexture& tex = textures[t];
                if (tex.firstPass < 0)
                    tex.firstPass = i32(p);
                tex.lastPass = i32(p);
            });
            for (const GlobalAfter& g : pass.globalsAfter)
                textures[g.texture].lastPass = i32(count);
        }
        for (usize t = 1; t < textures.Size(); t++)
            if (!textures[t].isImported() && textures[t].firstPass >= 0)
                stats.textures++;

        // Render passes: consecutive raster passes with the same attachments, none reading one of them as a texture.
        Group* group = nullptr;
        for (u32 p = 0; p < count; p++)
        {
            Pass& pass = passes[p];
            if (pass.culled)
                continue;
            bool joins = group && group->raster && pass.kind == PassKind::Raster && SameAttachments(passes[group->passes[0]], pass) &&
                         !ReadsAttachmentOf(pass, passes[group->passes[0]]);
            if (!joins)
            {
                group = &groups.Emplace();
                group->raster = pass.kind == PassKind::Raster;
                if (group->raster)
                    stats.renderPasses++;
            }
            else
                stats.mergedPasses++;
            pass.renderPass = i32(groups.Size() - 1);
            group->passes.Push(p);
        }
    }

    // ------------------------------------------------------------------------------------------------ execute

    namespace
    {
        Vector2Int ReferenceSize()
        {
            Vector2Int v = RTHandles::rtHandleProperties().currentViewportSize;
            if (v.x > 0 && v.y > 0)
                return v;
            i32 w = RTHandles::maxWidth(), h = RTHandles::maxHeight();
            return Vector2Int(w < 1 ? 1 : w, h < 1 ? 1 : h);
        }

        RenderTargetIdentifier None() { return RenderTargetIdentifier(engine::BuiltinRenderTextureType::None); }
    }

    void RenderGraph::Impl::Allocate(u32 first, u32 last, RenderGraphStatistics& stats)
    {
        Vector2Int reference = ReferenceSize();
        for (usize t = 1; t < textures.Size(); t++)
        {
            GraphTexture& tex = textures[t];
            if (tex.isImported() || tex.pooled || tex.firstPass < i32(first) || tex.firstPass > i32(last))
                continue;
            tex.pooled = Acquire(tex.desc, tex.desc.Size(reference), stats);
        }
    }

    void RenderGraph::Impl::Free(u32 first, u32 last)
    {
        for (usize t = 1; t < textures.Size(); t++)
        {
            GraphTexture& tex = textures[t];
            if (tex.pooled && tex.lastPass >= i32(first) && tex.lastPass <= i32(last))
            {
                tex.pooled->inUse = false;
                tex.pooled = nullptr;
            }
        }
    }

    // The load of an attachment at the start of its group: clear at its first use when asked, load when kept, else nothing.
    engine::RenderBufferLoadAction RenderGraph::Impl::LoadOf(u32 texture, AccessFlags flags) const
    {
        const GraphTexture& t = textures[texture];
        bool first = !t.hasContents;
        if (first && (t.isImported() ? t.import.clearOnFirstUse : t.desc.clearBuffer))
            return engine::RenderBufferLoadAction::Clear;
        if (Has(flags, AccessFlags::Discard))
            return engine::RenderBufferLoadAction::DontCare;
        return t.isImported() || !first ? engine::RenderBufferLoadAction::Load : engine::RenderBufferLoadAction::DontCare;
    }

    Color RenderGraph::Impl::ClearColorOf(u32 texture) const
    {
        const GraphTexture& t = textures[texture];
        return t.isImported() ? t.import.clearColor : t.desc.clearColor;
    }

    // Kept after the group when a later pass uses it or it is imported (unless discarded).
    engine::RenderBufferStoreAction RenderGraph::Impl::StoreOf(u32 texture, u32 lastPassOfGroup) const
    {
        const GraphTexture& t = textures[texture];
        if (t.isImported())
            return t.import.discardOnLastUse && t.lastPass <= i32(lastPassOfGroup) ? engine::RenderBufferStoreAction::DontCare : engine::RenderBufferStoreAction::Store;
        return t.lastPass > i32(lastPassOfGroup) ? engine::RenderBufferStoreAction::Store : engine::RenderBufferStoreAction::DontCare;
    }

    void RenderGraph::Impl::Execute(RenderGraph& graph, RenderGraphStatistics& stats)
    {
        for (PoolEntry* e : pool)
            e->usedThisFrame = false;
        CommandBuffer& cmd = *parameters.commandBuffer;
        ScriptableRenderContext* context = parameters.scriptableRenderContext;
        bool native = graph.nativeRenderPassesEnabled && context;
        for (const Group& group : groups)
        {
            u32 first = group.passes[0], last = group.passes[group.passes.Size() - 1];
            Allocate(first, last, stats);
            if (group.raster && native)
                RunNative(graph, *context, cmd, group);
            else
            {
                if (group.raster)
                    Bind(cmd, passes[first]);
                for (u32 p : group.passes)
                    Run(graph, cmd, passes[p]);
            }
            for (u32 p : group.passes)
                passes[p].All([&](u32 t, AccessFlags f) {
                    if (Writes(f))
                        textures[t].hasContents = true;
                });
            Free(first, last);
        }
        stats.pooledTextures = i32(pool.Size());
    }

    // Clears of the textures a non raster pass first uses (raster passes clear by their load action).
    void RenderGraph::Impl::ClearFirstUses(CommandBuffer& cmd, const Pass& pass)
    {
        for (const Use& use : pass.uses)
        {
            GraphTexture& t = textures[use.texture];
            if (!t.hasContents && Writes(use.flags) && (t.isImported() ? t.import.clearOnFirstUse : t.desc.clearBuffer))
            {
                cmd.SetRenderTarget(t.Handle()->nameID());
                cmd.ClearRenderTarget(engine::RTClearFlags::All, ClearColorOf(use.texture), 1.0f, 0);
                t.hasContents = true;
            }
        }
    }

    // The attachments of a group as the target, cleared where their first use asks.
    void RenderGraph::Impl::Bind(CommandBuffer& cmd, const Pass& head)
    {
        RTHandle* depth = head.depth.texture ? textures[head.depth.texture].Handle() : nullptr;
        if (head.colorCount == 1)
        {
            RTHandle* c = textures[head.colors[0].texture].Handle();
            RenderTargetIdentifier color = c ? c->nameID() : None();
            if (depth)
                cmd.SetRenderTarget(color, depth->nameID(), head.colors[0].mipLevel, engine::CubemapFace::Unknown, head.colors[0].depthSlice);
            else
                cmd.SetRenderTarget(color, head.colors[0].mipLevel, engine::CubemapFace::Unknown, head.colors[0].depthSlice);
        }
        else if (head.colorCount > 1)
        {
            RenderTargetIdentifier colors[RenderGraph::kMaxMRTCount];
            for (i32 i = 0; i < head.colorCount; i++)
                colors[i] = head.colors[i].texture ? textures[head.colors[i].texture].Handle()->nameID() : None();
            cmd.SetRenderTarget(Span<const RenderTargetIdentifier>(colors, usize(head.colorCount)), depth ? depth->nameID() : None());
        }
        else if (depth)
            cmd.SetRenderTarget(depth->nameID(), head.depth.mipLevel, engine::CubemapFace::Unknown, head.depth.depthSlice);
        bool clearColor = false, clearDepth = false;
        Color color = Color(0, 0, 0, 0);
        for (i32 i = 0; i < head.colorCount; i++)
            if (head.colors[i].texture && LoadOf(head.colors[i].texture, head.colors[i].flags) == engine::RenderBufferLoadAction::Clear)
            {
                clearColor = true;
                color = ClearColorOf(head.colors[i].texture);
            }
        if (head.depth.texture && LoadOf(head.depth.texture, head.depth.flags) == engine::RenderBufferLoadAction::Clear)
            clearDepth = true;
        if (clearColor || clearDepth)
            cmd.ClearRenderTarget(clearDepth, clearColor, color, 1.0f);
    }

    void RenderGraph::Impl::Run(RenderGraph& graph, CommandBuffer& cmd, Pass& pass)
    {
        if (pass.kind != PassKind::Raster)
            ClearFirstUses(cmd, pass);
        else
            // Input attachments without native render passes: read as the textures the engine names for subpass inputs.
            for (const Input& input : pass.inputs)
                cmd.SetGlobalTexture(Format("_HelixInput{}", input.index), textures[input.texture].Handle()->nameID());
        cmd.BeginSample(pass.name);
        pass.run->Run(pass.kind, cmd, graph.defaults_);
        cmd.EndSample(pass.name);
        for (const GlobalAfter& g : pass.globalsAfter)
            cmd.SetGlobalTexture(g.propertyId, textures[g.texture].Handle()->nameID());
    }

    // A group as one native render pass: an attachment per texture, a subpass per pass.
    void RenderGraph::Impl::RunNative(RenderGraph& graph, ScriptableRenderContext& context, CommandBuffer& cmd, const Group& group)
    {
        const Pass& head = passes[group.passes[0]];
        u32 lastPass = group.passes[group.passes.Size() - 1];
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
        Array<u32> used;
        for (i32 i = 0; i < head.colorCount; i++)
            if (head.colors[i].texture && IndexOf(used, head.colors[i].texture) < 0)
                used.Push(head.colors[i].texture);
        i32 depthIndex = -1;
        if (head.depth.texture)
        {
            depthIndex = i32(used.Size());
            used.Push(head.depth.texture);
        }
        for (u32 p : group.passes)
            for (const Input& input : passes[p].inputs)
                if (IndexOf(used, input.texture) < 0)
                    used.Push(input.texture);
        Array<engine::AttachmentDescriptor> attachments;
        i32 width = 1, height = 1, samples = 1;
        for (usize i = 0; i < used.Size(); i++)
        {
            const GraphTexture& t = textures[used[i]];
            RTHandle* h = t.Handle();
            RenderTexture* rt = h ? h->rt() : nullptr;
            engine::AttachmentDescriptor a(rt ? (i32(i) == depthIndex ? rt->depthStencilFormat() : rt->graphicsFormat()) : t.desc.colorFormat);
            AccessFlags flags = i32(i) == depthIndex ? head.depth.flags : AccessFlags::Write;
            for (i32 c = 0; c < head.colorCount; c++)
                if (head.colors[c].texture == used[i])
                    flags = head.colors[c].flags;
            a.loadStoreTarget = h ? h->nameID() : None();
            a.loadAction = LoadOf(used[i], flags);
            a.storeAction = StoreOf(used[i], lastPass);
            if (a.loadAction == engine::RenderBufferLoadAction::Clear)
                a.clearColor = ClearColorOf(used[i]);
            attachments.Push(a);
            if (i == 0 && rt)
            {
                width = rt->width();
                height = rt->height();
                samples = rt->antiAliasing() < 1 ? 1 : rt->antiAliasing();
            }
        }
        context.BeginRenderPass(width, height, samples, Span<const engine::AttachmentDescriptor>(attachments.Data(), attachments.Size()), depthIndex);
        for (u32 p : group.passes)
        {
            Pass& pass = passes[p];
            Array<i32> colors, inputs;
            for (i32 i = 0; i < pass.colorCount; i++)
                colors.Push(IndexOf(used, pass.colors[i].texture));
            for (const Input& input : pass.inputs)
                inputs.Push(IndexOf(used, input.texture));
            context.BeginSubPass(Span<const i32>(colors.Data(), colors.Size()), Span<const i32>(inputs.Data(), inputs.Size()),
                                 pass.depth.texture != 0 && !Writes(pass.depth.flags));
            cmd.BeginSample(pass.name);
            pass.run->Run(pass.kind, cmd, graph.defaults_);
            cmd.EndSample(pass.name);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            context.EndSubPass();
            for (const GlobalAfter& g : pass.globalsAfter)
                cmd.SetGlobalTexture(g.propertyId, textures[g.texture].Handle()->nameID());
        }
        context.EndRenderPass();
    }
}
