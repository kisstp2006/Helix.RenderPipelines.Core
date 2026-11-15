#include <Helix/RenderPipelines/Core/Native/TexturesNative.h>

namespace helix::rendering
{
    namespace
    {
        RTHandleSystem* SystemOf(u64 id) { return id ? RTHandleSystem::Find(id) : &RTHandles::DefaultInstance(); }

        template <class F>
        void WithBuffer(u64 buffer, F&& f)
        {
            if (CommandBuffer* b = CommandBuffer::Find(buffer))
                f(*b);
        }

        // A C# scale function: an unmanaged function of (user, width, height, out width, out height), its user a GC handle.
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

        void FreeManaged(void* user) { delete static_cast<ManagedScale*>(user); }
    }

    RenderTargetIdentifier TargetData::ToIdentifier() const
    {
        RenderTargetIdentifier r;
        r.type = engine::BuiltinRenderTextureType(type), r.nameID = nameID, r.texture = engine::EntityId::FromULong(texture), r.mipLevel = mipLevel;
        r.face = engine::CubemapFace(face), r.depthSlice = depthSlice;
        return r;
    }

    TargetData DataOf(const RenderTargetIdentifier& id)
    {
        TargetData d;
        d.type = i32(id.type), d.nameID = id.nameID, d.texture = engine::EntityId::ToULong(id.texture), d.mipLevel = id.mipLevel;
        d.face = i32(id.face), d.depthSlice = id.depthSlice;
        return d;
    }

    RTHandleAllocInfo InfoOf(const AllocData& d, StringView name)
    {
        RTHandleAllocInfo info;
        info.slices = d.slices;
        info.depthBufferBits = DepthBits(d.depthBufferBits);
        info.format = GraphicsFormat(d.format);
        info.filterMode = engine::FilterMode(d.filterMode);
        info.wrapMode = engine::TextureWrapMode(d.wrapMode);
        info.dimension = engine::TextureDimension(d.dimension);
        info.enableRandomWrite = (d.flags & 1) != 0;
        info.useMipMap = (d.flags & 2) != 0;
        info.autoGenerateMips = (d.flags & 4) != 0;
        info.isShadowMap = (d.flags & 8) != 0;
        info.bindTextureMS = (d.flags & 16) != 0;
        info.useDynamicScale = (d.flags & 32) != 0;
        info.useDynamicScaleExplicit = (d.flags & 64) != 0;
        info.anisoLevel = d.anisoLevel;
        info.mipMapBias = d.mipMapBias;
        info.msaaSamples = MSAASamples(d.msaaSamples < 1 ? 1 : d.msaaSamples);
        info.memoryless = engine::RenderTextureMemoryless(d.memoryless);
        info.vrUsage = engine::VRTextureUsage(d.vrUsage);
        info.name = String(name);
        return info;
    }

    void TexturesNative::DestroySystem(u64 system)
    {
        if (RTHandleSystem* s = RTHandleSystem::Find(system); s && s != &RTHandles::DefaultInstance())
            delete s;
    }

    void TexturesNative::Initialize(u64 system, i32 width, i32 height)
    {
        if (RTHandleSystem* s = SystemOf(system))
            s->Initialize(width, height);
    }

    void TexturesNative::Dispose(u64 system)
    {
        if (RTHandleSystem* s = SystemOf(system))
            s->Dispose();
    }

    i32 TexturesNative::MaxWidth(u64 system) { return SystemOf(system) ? SystemOf(system)->GetMaxWidth() : 0; }
    i32 TexturesNative::MaxHeight(u64 system) { return SystemOf(system) ? SystemOf(system)->GetMaxHeight() : 0; }
    RTHandleProperties TexturesNative::Properties(u64 system) { return SystemOf(system) ? SystemOf(system)->rtHandleProperties() : RTHandleProperties{}; }

    void TexturesNative::SetReferenceSize(u64 system, i32 width, i32 height, bool reset)
    {
        if (RTHandleSystem* s = SystemOf(system))
            s->SetReferenceSize(width, height, reset);
    }

    void TexturesNative::SetHardwareDynamicResolutionState(u64 system, bool value)
    {
        if (RTHandleSystem* s = SystemOf(system))
            s->SetHardwareDynamicResolutionState(value);
    }

    bool TexturesNative::HardwareDynamicResRequested(u64 system) { return SystemOf(system) && SystemOf(system)->hardwareDynamicResRequested(); }

    u64 TexturesNative::Alloc(u64 system, i32 width, i32 height, AllocData data, StringView name)
    {
        RTHandleSystem* s = SystemOf(system);
        return s ? s->Alloc(width, height, InfoOf(data, name))->Id() : 0;
    }

    u64 TexturesNative::AllocScaled(u64 system, Vector2 scale, AllocData data, StringView name)
    {
        RTHandleSystem* s = SystemOf(system);
        return s ? s->Alloc(scale, InfoOf(data, name))->Id() : 0;
    }

    u64 TexturesNative::AllocFunc(u64 system, u64 function, u64 user, AllocData data, StringView name)
    {
        RTHandleSystem* s = SystemOf(system);
        if (!s)
            return 0;
        auto* m = new ManagedScale{function, user};
        RTHandle* h = s->Alloc(CallManaged, m, InfoOf(data, name));
        h->releaseScaleUser_ = FreeManaged;
        return h->Id();
    }

    u64 TexturesNative::AllocTexture(u64 system, Texture* texture)
    {
        RTHandleSystem* s = SystemOf(system);
        return s ? s->Alloc(texture)->Id() : 0;
    }

    u64 TexturesNative::AllocRenderTexture(u64 system, RenderTexture* texture, bool transferOwnership)
    {
        RTHandleSystem* s = SystemOf(system);
        return s ? s->Alloc(texture, transferOwnership)->Id() : 0;
    }

    u64 TexturesNative::AllocTarget(u64 system, TargetData target, StringView name)
    {
        RTHandleSystem* s = SystemOf(system);
        return s ? s->Alloc(target.ToIdentifier(), name)->Id() : 0;
    }

    String TexturesNative::Name(u64 handle)
    {
        RTHandle* h = RTHandle::Find(handle);
        return h ? String(h->name()) : String();
    }

    RenderTexture* TexturesNative::RenderTextureOf(u64 handle)
    {
        RTHandle* h = RTHandle::Find(handle);
        return h ? h->rt() : nullptr;
    }

    Texture* TexturesNative::ExternalTexture(u64 handle)
    {
        RTHandle* h = RTHandle::Find(handle);
        return h ? h->externalTexture() : nullptr;
    }

    TargetData TexturesNative::NameID(u64 handle)
    {
        RTHandle* h = RTHandle::Find(handle);
        return DataOf(h ? h->nameID() : RenderTargetIdentifier(engine::BuiltinRenderTextureType::None));
    }

    bool TexturesNative::IsMSAAEnabled(u64 handle) { return RTHandle::Find(handle) && RTHandle::Find(handle)->isMSAAEnabled(); }
    Vector2 TexturesNative::ScaleFactor(u64 handle) { return RTHandle::Find(handle) ? RTHandle::Find(handle)->scaleFactor() : Vector2(1, 1); }
    bool TexturesNative::UseScaling(u64 handle) { return RTHandle::Find(handle) && RTHandle::Find(handle)->useScaling(); }
    Vector2Int TexturesNative::ReferenceSize(u64 handle) { return RTHandle::Find(handle) ? RTHandle::Find(handle)->referenceSize() : Vector2Int(0, 0); }

    RTHandleProperties TexturesNative::HandleProperties(u64 handle)
    {
        RTHandle* h = RTHandle::Find(handle);
        return h ? h->rtHandleProperties() : RTHandleProperties{};
    }

    void TexturesNative::SetCustomHandleProperties(u64 handle, RTHandleProperties properties)
    {
        if (RTHandle* h = RTHandle::Find(handle))
            h->SetCustomHandleProperties(properties);
    }

    void TexturesNative::ClearCustomHandleProperties(u64 handle)
    {
        if (RTHandle* h = RTHandle::Find(handle))
            h->ClearCustomHandleProperties();
    }

    void TexturesNative::SetRenderTexture(u64 handle, RenderTexture* texture)
    {
        if (RTHandle* h = RTHandle::Find(handle))
            h->SetRenderTexture(texture);
    }

    void TexturesNative::SetTexture(u64 handle, Texture* texture)
    {
        if (RTHandle* h = RTHandle::Find(handle))
            h->SetTexture(texture);
    }

    void TexturesNative::SetTarget(u64 handle, TargetData target)
    {
        if (RTHandle* h = RTHandle::Find(handle))
            h->SetTexture(target.ToIdentifier());
    }

    Vector2Int TexturesNative::ScaledSize(u64 handle, Vector2Int referenceSize)
    {
        RTHandle* h = RTHandle::Find(handle);
        return h ? h->GetScaledSize(referenceSize) : referenceSize;
    }

    Vector2Int TexturesNative::OwnScaledSize(u64 handle)
    {
        RTHandle* h = RTHandle::Find(handle);
        return h ? h->GetScaledSize() : Vector2Int(0, 0);
    }

    void TexturesNative::Release(u64 handle)
    {
        if (RTHandle* h = RTHandle::Find(handle))
            h->Release();
    }

    void TexturesNative::ClearRenderTarget(u64 buffer, i32 clearFlag, Color clearColor)
    {
        WithBuffer(buffer, [&](CommandBuffer& b) { CoreUtils::ClearRenderTarget(b, ClearFlag(clearFlag), clearColor); });
    }

    void TexturesNative::SetHandleTarget(u64 buffer, u64 color, u64 depth, i32 clearFlag, Color clearColor, i32 mipLevel, i32 face, i32 depthSlice)
    {
        RTHandle* c = RTHandle::Find(color);
        RTHandle* d = RTHandle::Find(depth);
        if (!c)
            return;
        WithBuffer(buffer, [&](CommandBuffer& b) {
            if (d)
                CoreUtils::SetRenderTarget(b, *c, *d, ClearFlag(clearFlag), clearColor, mipLevel, engine::CubemapFace(face), depthSlice);
            else
                CoreUtils::SetRenderTarget(b, *c, ClearFlag(clearFlag), clearColor, mipLevel, engine::CubemapFace(face), depthSlice);
        });
    }

    void TexturesNative::SetHandleTargetActions(u64 buffer, u64 handle, i32 loadAction, i32 storeAction, i32 clearFlag, Color clearColor)
    {
        if (RTHandle* h = RTHandle::Find(handle))
            WithBuffer(buffer, [&](CommandBuffer& b) {
                CoreUtils::SetRenderTarget(b, *h, engine::RenderBufferLoadAction(loadAction), engine::RenderBufferStoreAction(storeAction), ClearFlag(clearFlag),
                                           clearColor);
            });
    }

    void TexturesNative::SetViewport(u64 buffer, u64 handle, i32 mipLevel)
    {
        WithBuffer(buffer, [&](CommandBuffer& b) { CoreUtils::SetViewport(b, RTHandle::Find(handle), mipLevel); });
    }

    void TexturesNative::BlitTexture(u64 buffer, TargetData source, Vector4 scaleBias, f32 mipLevel, bool bilinear)
    {
        WithBuffer(buffer, [&](CommandBuffer& b) { Blitter::BlitTexture(b, source.ToIdentifier(), scaleBias, mipLevel, bilinear); });
    }

    void TexturesNative::BlitTextureMaterial(u64 buffer, TargetData source, Vector4 scaleBias, engine::Material* material, i32 pass)
    {
        WithBuffer(buffer, [&](CommandBuffer& b) { Blitter::BlitTexture(b, source.ToIdentifier(), scaleBias, material, pass); });
    }

    void TexturesNative::BlitNoSource(u64 buffer, Vector4 scaleBias, engine::Material* material, i32 pass)
    {
        WithBuffer(buffer, [&](CommandBuffer& b) { Blitter::BlitTexture(b, scaleBias, material, pass); });
    }

    void TexturesNative::BlitTextureInto(u64 buffer, TargetData source, TargetData destination, i32 loadAction, i32 storeAction,
                                         engine::Material* material, i32 pass)
    {
        WithBuffer(buffer, [&](CommandBuffer& b) {
            if (loadAction < 0)
                Blitter::BlitTexture(b, source.ToIdentifier(), destination.ToIdentifier(), material, pass);
            else
                Blitter::BlitTexture(b, source.ToIdentifier(), destination.ToIdentifier(), engine::RenderBufferLoadAction(loadAction),
                                     engine::RenderBufferStoreAction(storeAction), material, pass);
        });
    }

    void TexturesNative::BlitCameraTexture(u64 buffer, u64 source, u64 destination, Vector4 scaleBias, bool ownScale, f32 mipLevel, bool bilinear)
    {
        RTHandle* s = RTHandle::Find(source);
        RTHandle* d = RTHandle::Find(destination);
        if (!s || !d)
            return;
        WithBuffer(buffer, [&](CommandBuffer& b) {
            if (ownScale)
                Blitter::BlitCameraTexture(b, *s, *d, mipLevel, bilinear);
            else
                Blitter::BlitCameraTexture(b, *s, *d, scaleBias, mipLevel, bilinear);
        });
    }

    void TexturesNative::BlitCameraTextureViewport(u64 buffer, u64 source, u64 destination, Rect viewport, f32 mipLevel, bool bilinear)
    {
        RTHandle* s = RTHandle::Find(source);
        RTHandle* d = RTHandle::Find(destination);
        if (s && d)
            WithBuffer(buffer, [&](CommandBuffer& b) { Blitter::BlitCameraTexture(b, *s, *d, viewport, mipLevel, bilinear); });
    }

    void TexturesNative::BlitCameraTextureMaterial(u64 buffer, u64 source, u64 destination, i32 loadAction, i32 storeAction, engine::Material* material, i32 pass)
    {
        RTHandle* s = RTHandle::Find(source);
        RTHandle* d = RTHandle::Find(destination);
        if (!s || !d)
            return;
        WithBuffer(buffer, [&](CommandBuffer& b) {
            if (loadAction < 0)
                Blitter::BlitCameraTexture(b, *s, *d, material, pass);
            else
                Blitter::BlitCameraTexture(b, *s, *d, engine::RenderBufferLoadAction(loadAction), engine::RenderBufferStoreAction(storeAction), material, pass);
        });
    }

    void TexturesNative::BlitQuad(u64 buffer, Texture* source, Vector4 scaleBiasTex, Vector4 scaleBiasRT, i32 mipLevelTex, bool bilinear)
    {
        WithBuffer(buffer, [&](CommandBuffer& b) { Blitter::BlitQuad(b, source, scaleBiasTex, scaleBiasRT, mipLevelTex, bilinear); });
    }

    void TexturesNative::BlitColorAndDepth(u64 buffer, Texture* sourceColor, RenderTexture* sourceDepth, Vector4 scaleBias, f32 mipLevel, bool blitDepth)
    {
        WithBuffer(buffer, [&](CommandBuffer& b) { Blitter::BlitColorAndDepth(b, sourceColor, sourceDepth, scaleBias, mipLevel, blitDepth); });
    }
}
