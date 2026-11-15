#include <Helix/RenderPipelines/Core/Textures/RTHandle.h>
#include <Helix/RenderPipelines/Core/Utilities/CoreUtils.h>

#include <Helix/Core/Containers/HashMap.h>
#include <Helix/Core/Logging/Log.h>
#include <Helix/Core/Strings/Format.h>
#include <Helix/Engine/Object/Object.h>

namespace helix::rendering
{
    namespace
    {
        LogChannel RTHandleLog{"Rendering"};

        // Handles and systems by id, for the C# side.
        struct Registry
        {
            HashMap<u64, RTHandle*> handles;
            HashMap<u64, RTHandleSystem*> systems;
            u64 next = 0;
        };

        Registry& R()
        {
            static Registry r;
            return r;
        }

        i32 Max1(i32 v) { return v < 1 ? 1 : v; }

        void RemoveValue(Array<RTHandle*>& list, RTHandle* h)
        {
            if (RTHandle** at = list.Find(h))
                list.RemoveAt(usize(at - list.Data()));
        }
        i32 RoundToInt(f32 v) { return i32(v < 0 ? v - 0.5f : v + 0.5f); }

        RenderTexture* Create(i32 width, i32 height, const RTHandleAllocInfo& info)
        {
            bool depthOnly = info.isShadowMap || info.format == GraphicsFormat::None;
            DepthBits depth = depthOnly && info.depthBufferBits == DepthBits::None ? DepthBits::Depth32 : info.depthBufferBits;
            RenderTextureDescriptor desc(width, height, depthOnly ? GraphicsFormat::None : info.format, i32(depth));
            desc.dimension = info.dimension;
            desc.volumeDepth = Max1(info.slices);
            desc.enableRandomWrite = info.enableRandomWrite;
            desc.useMipMap = info.useMipMap;
            desc.autoGenerateMips = info.autoGenerateMips;
            desc.msaaSamples = i32(info.msaaSamples);
            desc.bindMS = info.bindTextureMS;
            desc.useDynamicScale = info.useDynamicScale || info.useDynamicScaleExplicit;
            desc.memoryless = info.memoryless;
            desc.vrUsage = info.vrUsage;
            if (info.isShadowMap)
                desc.shadowSamplingMode = engine::ShadowSamplingMode::CompareDepths;
            RenderTexture* rt = RenderTexture::New(desc);
            rt->SetFilterMode(info.filterMode);
            rt->SetWrapMode(info.wrapMode);
            rt->SetAnisoLevel(info.anisoLevel);
            rt->SetMipMapBias(info.mipMapBias);
            rt->hideFlags = engine::HideFlags::HideAndDontSave;
            rt->SetName(CoreUtils::GetRenderTargetAutoName(width, height, desc.volumeDepth, info.format, info.dimension, info.name, info.useMipMap,
                                                           info.msaaSamples != MSAASamples::None, info.msaaSamples));
            rt->Create();
            return rt;
        }
    }

    // --------------------------------------------------------------------------------------------------- RTHandle

    RTHandle::RTHandle(RTHandleSystem* owner) : owner_(owner)
    {
        id_ = ++R().next;
        R().handles.Set(id_, this);
        if (owner_)
            owner_->all_.Push(this);
    }

    RTHandle::~RTHandle() { R().handles.Remove(id_); }

    RTHandle* RTHandle::Find(u64 id)
    {
        RTHandle** h = R().handles.Find(id);
        return h ? *h : nullptr;
    }

    RenderTexture* RTHandle::rt() const { return engine::EntityIdToObject<RenderTexture>(rt_); }
    Texture* RTHandle::externalTexture() const { return engine::EntityIdToObject<Texture>(external_); }

    RTHandleProperties RTHandle::rtHandleProperties() const
    {
        if (useCustomProperties_)
            return customProperties_;
        return owner_ ? owner_->rtHandleProperties() : RTHandleProperties{};
    }

    void RTHandle::SetCustomHandleProperties(const RTHandleProperties& properties)
    {
        useCustomProperties_ = true;
        customProperties_ = properties;
    }

    void RTHandle::SetRenderTexture(RenderTexture* texture)
    {
        rt_ = texture ? texture->GetEntityId() : engine::EntityId();
        external_ = engine::EntityId();
        nameID_ = texture ? RenderTargetIdentifier(static_cast<Texture*>(texture)) : RenderTargetIdentifier(engine::BuiltinRenderTextureType::None);
    }

    void RTHandle::SetTexture(Texture* texture)
    {
        rt_ = engine::EntityId();
        external_ = texture ? texture->GetEntityId() : engine::EntityId();
        nameID_ = texture ? RenderTargetIdentifier(texture) : RenderTargetIdentifier(engine::BuiltinRenderTextureType::None);
    }

    void RTHandle::SetTexture(const RenderTargetIdentifier& target)
    {
        rt_ = external_ = engine::EntityId();
        nameID_ = target;
    }

    Vector2Int RTHandle::GetScaledSize(Vector2Int size) const
    {
        if (!useScaling_)
            return size;
        if (scaleFunc_)
            return scaleFunc_(size, scaleUser_);
        return Vector2Int(Max1(RoundToInt(scaleFactor_.x * f32(size.x))), Max1(RoundToInt(scaleFactor_.y * f32(size.y))));
    }

    Vector2Int RTHandle::GetScaledSize() const { return useScaling_ ? GetScaledSize(referenceSize_) : referenceSize_; }

    void RTHandle::Release()
    {
        if (owner_)
        {
            RemoveValue(owner_->autoSized_, this);
            RemoveValue(owner_->all_, this);
        }
        if (RenderTexture* t = rt(); t && owns_)
            CoreUtils::Destroy(t);
        if (releaseScaleUser_)
            releaseScaleUser_(scaleUser_);
        delete this;
    }

    void RTHandle::Resize(Vector2Int size)
    {
        RenderTexture* t = rt();
        if (!t || (t->width() == size.x && t->height() == size.y))
            return;
        RenderTextureDescriptor desc = t->descriptor();
        desc.width = size.x;
        desc.height = size.y;
        t->Release();
        t->SetDescriptor(desc);
        t->SetName(CoreUtils::GetRenderTargetAutoName(size.x, size.y, desc.volumeDepth, desc.graphicsFormat, desc.dimension, name_, desc.useMipMap,
                                                      enableMSAA_, MSAASamples(desc.msaaSamples)));
        t->Create();
    }

    // --------------------------------------------------------------------------------------------- RTHandleSystem

    RTHandleSystem::RTHandleSystem()
    {
        id_ = ++R().next;
        R().systems.Set(id_, this);
    }

    RTHandleSystem::~RTHandleSystem()
    {
        Dispose();
        // Handles of textures and targets go with the system.
        while (!all_.Empty())
            all_.Back()->Release();
        R().systems.Remove(id_);
    }

    RTHandleSystem* RTHandleSystem::Find(u64 id)
    {
        RTHandleSystem** s = R().systems.Find(id);
        return s ? *s : nullptr;
    }

    void RTHandleSystem::Initialize(i32 width, i32 height)
    {
        if (!autoSized_.Empty())
        {
            String names;
            for (RTHandle* h : autoSized_)
                names += names.Empty() ? String(h->name()) : Format(", {}", h->name());
            HX_LOG_WARNING(RTHandleLog, "RTHandle.Initialize: There are uncleared RTHandles: {}. Please release them first.", names);
        }
        maxWidth_ = width;
        maxHeight_ = height;
        hwDynamicRes_ = false;
    }

    void RTHandleSystem::Dispose()
    {
        while (!autoSized_.Empty())
            autoSized_.Back()->Release();
    }

    void RTHandleSystem::Release(RTHandle* handle)
    {
        if (handle)
            handle->Release();
    }

    void RTHandleSystem::SetReferenceSize(i32 width, i32 height, bool reset)
    {
        properties_.previousViewportSize = properties_.currentViewportSize;
        properties_.previousRenderTargetSize = properties_.currentRenderTargetSize;
        Vector2 lastMax{f32(maxWidth_), f32(maxHeight_)};
        width = Max1(width);
        height = Max1(height);
        if (width > maxWidth_ || height > maxHeight_ || reset)
            Resize(width, height, reset);
        properties_.currentViewportSize = Vector2Int(width, height);
        properties_.currentRenderTargetSize = Vector2Int(maxWidth_, maxHeight_);
        Vector2 maxSize{f32(maxWidth_), f32(maxHeight_)};
        properties_.rtHandleScale = Vector4(f32(width) / maxSize.x, f32(height) / maxSize.y, f32(properties_.previousViewportSize.x) / lastMax.x,
                                            f32(properties_.previousViewportSize.y) / lastMax.y);
    }

    void RTHandleSystem::Resize(i32 width, i32 height, bool reset)
    {
        maxWidth_ = reset ? width : Max(width, maxWidth_);
        maxHeight_ = reset ? height : Max(height, maxHeight_);
        Vector2Int maxSize(maxWidth_, maxHeight_);
        for (RTHandle* h : autoSized_)
        {
            h->referenceSize_ = maxSize;
            h->Resize(h->GetScaledSize(maxSize));
        }
    }

    RTHandle* RTHandleSystem::Alloc(i32 width, i32 height, const RTHandleAllocInfo& info)
    {
        auto* h = new RTHandle(this);
        h->SetRenderTexture(Create(width, height, info));
        h->owns_ = true;
        h->enableRandomWrite_ = info.enableRandomWrite;
        h->enableMSAA_ = info.msaaSamples != MSAASamples::None;
        h->hwDynamicScale_ = info.useDynamicScale;
        h->name_ = info.name;
        h->referenceSize_ = Vector2Int(width, height);
        return h;
    }

    RTHandle* RTHandleSystem::AutoSized(RTHandle* h, Vector2 scaleFactor, ScaleFunc scaleFunc, void* user, Vector2Int referenceSize)
    {
        h->useScaling_ = true;
        h->scaleFactor_ = scaleFactor;
        h->scaleFunc_ = scaleFunc;
        h->scaleUser_ = user;
        h->referenceSize_ = referenceSize;
        autoSized_.Push(h);
        return h;
    }

    RTHandle* RTHandleSystem::Alloc(Vector2 scaleFactor, const RTHandleAllocInfo& info)
    {
        Vector2Int maxSize(maxWidth_, maxHeight_);
        Vector2Int size(Max1(RoundToInt(scaleFactor.x * f32(maxWidth_))), Max1(RoundToInt(scaleFactor.y * f32(maxHeight_))));
        return AutoSized(Alloc(size.x, size.y, info), scaleFactor, nullptr, nullptr, maxSize);
    }

    RTHandle* RTHandleSystem::Alloc(ScaleFunc scaleFunc, void* user, const RTHandleAllocInfo& info)
    {
        Vector2Int maxSize(maxWidth_, maxHeight_);
        Vector2Int size = scaleFunc ? scaleFunc(maxSize, user) : maxSize;
        return AutoSized(Alloc(Max1(size.x), Max1(size.y), info), Vector2(1, 1), scaleFunc, user, maxSize);
    }

    RTHandleAllocInfo RTHandleSystem::InfoOf(const RenderTextureDescriptor& d, engine::FilterMode filterMode, engine::TextureWrapMode wrapMode, bool isShadowMap,
                                             i32 anisoLevel, f32 mipMapBias, StringView name)
    {
        RTHandleAllocInfo info;
        info.slices = d.volumeDepth;
        info.depthBufferBits = DepthBits(d.depthBufferBits());
        info.format = d.graphicsFormat;
        info.filterMode = filterMode;
        info.wrapMode = wrapMode;
        info.dimension = d.dimension;
        info.enableRandomWrite = d.enableRandomWrite;
        info.useMipMap = d.useMipMap;
        info.autoGenerateMips = d.autoGenerateMips;
        info.isShadowMap = isShadowMap;
        info.anisoLevel = anisoLevel;
        info.mipMapBias = mipMapBias;
        info.msaaSamples = MSAASamples(d.msaaSamples < 1 ? 1 : d.msaaSamples);
        info.bindTextureMS = d.bindMS;
        info.useDynamicScale = d.useDynamicScale;
        info.memoryless = d.memoryless;
        info.vrUsage = d.vrUsage;
        info.name = String(name);
        return info;
    }

    RTHandle* RTHandleSystem::Alloc(const RenderTextureDescriptor& d, engine::FilterMode filterMode, engine::TextureWrapMode wrapMode, bool isShadowMap,
                                    i32 anisoLevel, f32 mipMapBias, StringView name)
    {
        return Alloc(d.width, d.height, InfoOf(d, filterMode, wrapMode, isShadowMap, anisoLevel, mipMapBias, name));
    }

    RTHandle* RTHandleSystem::Alloc(Vector2 scaleFactor, const RenderTextureDescriptor& d, engine::FilterMode filterMode, engine::TextureWrapMode wrapMode,
                                    bool isShadowMap, i32 anisoLevel, f32 mipMapBias, StringView name)
    {
        return Alloc(scaleFactor, InfoOf(d, filterMode, wrapMode, isShadowMap, anisoLevel, mipMapBias, name));
    }

    RTHandle* RTHandleSystem::Alloc(Texture* texture)
    {
        auto* h = new RTHandle(this);
        h->SetTexture(texture);
        h->name_ = texture ? String(texture->Name()) : String();
        h->referenceSize_ = texture ? Vector2Int(texture->width(), texture->height()) : Vector2Int(0, 0);
        return h;
    }

    RTHandle* RTHandleSystem::Alloc(RenderTexture* texture, bool transferOwnership)
    {
        auto* h = new RTHandle(this);
        h->SetRenderTexture(texture);
        h->owns_ = transferOwnership;
        h->enableMSAA_ = texture && texture->antiAliasing() > 1;
        h->name_ = texture ? String(texture->Name()) : String();
        h->referenceSize_ = texture ? Vector2Int(texture->width(), texture->height()) : Vector2Int(0, 0);
        return h;
    }

    RTHandle* RTHandleSystem::Alloc(const RenderTargetIdentifier& target, StringView name)
    {
        auto* h = new RTHandle(this);
        h->SetTexture(target);
        h->name_ = String(name);
        return h;
    }

    // ------------------------------------------------------------------------------------------------- RTHandles

    RTHandleSystem& RTHandles::DefaultInstance()
    {
        static RTHandleSystem* s = new RTHandleSystem();
        return *s;
    }

    Vector2Int RTHandles::CalculateDimensions(Vector2 scaleFactor)
    {
        return Vector2Int(Max1(RoundToInt(scaleFactor.x * f32(maxWidth()))), Max1(RoundToInt(scaleFactor.y * f32(maxHeight()))));
    }

    Vector2Int RTHandles::CalculateDimensions(ScaleFunc scaleFunc, void* user)
    {
        Vector2Int size = scaleFunc(Vector2Int(maxWidth(), maxHeight()), user);
        return Vector2Int(Max1(size.x), Max1(size.y));
    }
}
