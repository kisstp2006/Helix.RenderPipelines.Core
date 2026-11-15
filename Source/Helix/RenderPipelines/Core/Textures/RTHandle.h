#pragma once

#include <Helix/Core/Basics/Tags.h>
#include <Helix/Core/Containers/Array.h>
#include <Helix/Core/Strings/String.h>
#include <Helix/Engine/Rendering/Pipeline/RenderTargets.h>
#include <Helix/Engine/Texture/RenderTexture.h>
#include <Helix/Math/Vector.h>

// Render textures sized from a reference size: a handle made with a scale or a scale function follows its system's
// reference size and is made again when the reference grows past the largest one so far; a smaller reference only
// changes the viewport (rtHandleScale is the used part). Handles of a texture or a target identifier wrap them.
namespace helix::rendering
{
    using engine::GraphicsFormat;
    using engine::RenderTargetIdentifier;
    using engine::RenderTexture;
    using engine::RenderTextureDescriptor;
    using engine::Texture;

    HX_ENUM(Namespace = "HelixEngine.Rendering")
    enum class DepthBits : i32
    {
        None = 0,
        Depth8 = 8,
        Depth16 = 16,
        Depth24 = 24,
        Depth32 = 32,
    };

    HX_ENUM(Namespace = "HelixEngine.Rendering")
    enum class MSAASamples : i32
    {
        None = 1,
        MSAA2x = 2,
        MSAA4x = 4,
        MSAA8x = 8,
    };

    // The reference sizes of a handle system this frame and the one before, and the used part of its targets.
    HX_STRUCT(Namespace = "HelixEngine.Rendering")
    struct RTHandleProperties
    {
        HX_FIELD() Vector2Int previousViewportSize;
        HX_FIELD() Vector2Int previousRenderTargetSize;
        HX_FIELD() Vector2Int currentViewportSize;
        HX_FIELD() Vector2Int currentRenderTargetSize;
        // xy: the current viewport over the current target size, zw: the previous one over the previous size.
        HX_FIELD() Vector4 rtHandleScale;
    };

    // A size from a reference size (the user value the function was given with).
    using ScaleFunc = Vector2Int (*)(Vector2Int size, void* user);

    // How a handle's render texture is made.
    struct RTHandleAllocInfo
    {
        i32 slices = 1;
        DepthBits depthBufferBits = DepthBits::None;
        GraphicsFormat format = GraphicsFormat::R8G8B8A8_SRGB;
        engine::FilterMode filterMode = engine::FilterMode::Point;
        engine::TextureWrapMode wrapMode = engine::TextureWrapMode::Repeat;
        engine::TextureDimension dimension = engine::TextureDimension::Tex2D;
        bool enableRandomWrite = false;
        bool useMipMap = false;
        bool autoGenerateMips = true;
        bool isShadowMap = false;
        i32 anisoLevel = 1;
        f32 mipMapBias = 0;
        MSAASamples msaaSamples = MSAASamples::None;
        bool bindTextureMS = false;
        bool useDynamicScale = false;
        bool useDynamicScaleExplicit = false;
        engine::RenderTextureMemoryless memoryless = engine::RenderTextureMemoryless::None;
        engine::VRTextureUsage vrUsage = engine::VRTextureUsage::None;
        String name = String();
    };

    class RTHandleSystem;
    class TexturesNative;

    class RTHandle
    {
    public:
        // The handle of an id (0 or a released handle: null).
        static RTHandle* Find(u64 id);
        u64 Id() const { return id_; }

        StringView name() const { return name_; }
        RenderTexture* rt() const;
        Texture* externalTexture() const;
        const RenderTargetIdentifier& nameID() const { return nameID_; }
        operator RenderTargetIdentifier() const { return nameID_; }
        bool isMSAAEnabled() const { return enableMSAA_; }
        Vector2 scaleFactor() const { return scaleFactor_; }
        bool useScaling() const { return useScaling_; }
        ScaleFunc scaleFunc() const { return scaleFunc_; }
        void* scaleUser() const { return scaleUser_; }
        Vector2Int referenceSize() const { return referenceSize_; }
        RTHandleProperties rtHandleProperties() const;
        RTHandleSystem* owner() const { return owner_; }

        void SetCustomHandleProperties(const RTHandleProperties& properties);
        void ClearCustomHandleProperties() { useCustomProperties_ = false; }
        // The handle takes a render texture (not owned: Release does not destroy it).
        void SetRenderTexture(RenderTexture* rt);
        void SetTexture(Texture* texture);
        void SetTexture(const RenderTargetIdentifier& target);

        // The size for a reference size (its own size when it does not scale).
        Vector2Int GetScaledSize(Vector2Int referenceSize) const;
        Vector2Int GetScaledSize() const;

        // Destroys the render texture the system made, leaves the system and deletes the handle.
        void Release();

    private:
        friend class RTHandleSystem;
        friend class TexturesNative;
        RTHandle(RTHandleSystem* owner);
        ~RTHandle();
        void Resize(Vector2Int size);

        u64 id_ = 0;
        RTHandleSystem* owner_ = nullptr;
        engine::EntityId rt_;
        engine::EntityId external_;
        RenderTargetIdentifier nameID_ = RenderTargetIdentifier(engine::BuiltinRenderTextureType::None);
        bool owns_ = false;
        bool enableMSAA_ = false;
        bool enableRandomWrite_ = false;
        bool hwDynamicScale_ = false;
        String name_ = String();
        bool useCustomProperties_ = false;
        RTHandleProperties customProperties_{};
        Vector2 scaleFactor_ = Vector2(1, 1);
        ScaleFunc scaleFunc_ = nullptr;
        void* scaleUser_ = nullptr;
        // Frees the scale function's user value when the handle goes (C#'s delegates).
        void (*releaseScaleUser_)(void*) = nullptr;
        bool useScaling_ = false;
        Vector2Int referenceSize_;
    };

    class RTHandleSystem
    {
    public:
        RTHandleSystem();
        ~RTHandleSystem();
        RTHandleSystem(const RTHandleSystem&) = delete;
        RTHandleSystem& operator=(const RTHandleSystem&) = delete;

        static RTHandleSystem* Find(u64 id);
        u64 Id() const { return id_; }

        void Initialize(i32 width, i32 height);
        // Releases every handle that follows the reference size.
        void Dispose();
        void Release(RTHandle* handle);

        i32 GetMaxWidth() const { return maxWidth_; }
        i32 GetMaxHeight() const { return maxHeight_; }
        const RTHandleProperties& rtHandleProperties() const { return properties_; }
        void SetHardwareDynamicResolutionState(bool enable) { hwDynamicRes_ = enable; }
        bool hardwareDynamicResRequested() const { return hwDynamicRes_; }

        // The size this frame renders at: the scaled targets grow to it when it is larger than before (or with reset
        // become its size).
        void SetReferenceSize(i32 width, i32 height, bool reset = false);
        void ResetReferenceSize(i32 width, i32 height) { SetReferenceSize(width, height, true); }

        RTHandle* Alloc(i32 width, i32 height, const RTHandleAllocInfo& info = {});
        RTHandle* Alloc(Vector2 scaleFactor, const RTHandleAllocInfo& info = {});
        RTHandle* Alloc(ScaleFunc scaleFunc, void* user, const RTHandleAllocInfo& info = {});
        RTHandle* Alloc(const RenderTextureDescriptor& descriptor, engine::FilterMode filterMode = engine::FilterMode::Point,
                        engine::TextureWrapMode wrapMode = engine::TextureWrapMode::Repeat, bool isShadowMap = false, i32 anisoLevel = 1,
                        f32 mipMapBias = 0, StringView name = {});
        RTHandle* Alloc(Vector2 scaleFactor, const RenderTextureDescriptor& descriptor, engine::FilterMode filterMode = engine::FilterMode::Point,
                        engine::TextureWrapMode wrapMode = engine::TextureWrapMode::Repeat, bool isShadowMap = false, i32 anisoLevel = 1,
                        f32 mipMapBias = 0, StringView name = {});
        // A handle of a texture (not owned).
        RTHandle* Alloc(Texture* texture);
        // A handle of a render texture; with transferOwnership Release destroys it.
        RTHandle* Alloc(RenderTexture* texture, bool transferOwnership);
        RTHandle* Alloc(const RenderTargetIdentifier& target, StringView name = {});

        // The allocation info of a descriptor.
        static RTHandleAllocInfo InfoOf(const RenderTextureDescriptor& descriptor, engine::FilterMode filterMode, engine::TextureWrapMode wrapMode,
                                        bool isShadowMap, i32 anisoLevel, f32 mipMapBias, StringView name);

    private:
        friend class RTHandle;
        void Resize(i32 width, i32 height, bool reset);
        RTHandle* AutoSized(RTHandle* handle, Vector2 scaleFactor, ScaleFunc scaleFunc, void* user, Vector2Int referenceSize);

        u64 id_ = 0;
        Array<RTHandle*> autoSized_ = Array<RTHandle*>();
        Array<RTHandle*> all_ = Array<RTHandle*>();
        i32 maxWidth_ = 0, maxHeight_ = 0;
        bool hwDynamicRes_ = false;
        RTHandleProperties properties_{};
    };

    // The default handle system, shared by the pipelines.
    class RTHandles
    {
    public:
        static RTHandleSystem& DefaultInstance();
        static i32 maxWidth() { return DefaultInstance().GetMaxWidth(); }
        static i32 maxHeight() { return DefaultInstance().GetMaxHeight(); }
        static const RTHandleProperties& rtHandleProperties() { return DefaultInstance().rtHandleProperties(); }
        static void Initialize(i32 width, i32 height) { DefaultInstance().Initialize(width, height); }
        static void Release(RTHandle* handle) { DefaultInstance().Release(handle); }
        static void SetHardwareDynamicResolutionState(bool value) { DefaultInstance().SetHardwareDynamicResolutionState(value); }
        static void SetReferenceSize(i32 width, i32 height) { DefaultInstance().SetReferenceSize(width, height); }
        static void ResetReferenceSize(i32 width, i32 height) { DefaultInstance().ResetReferenceSize(width, height); }
        static Vector2Int CalculateDimensions(Vector2 scaleFactor);
        static Vector2Int CalculateDimensions(ScaleFunc scaleFunc, void* user);
    };
}
