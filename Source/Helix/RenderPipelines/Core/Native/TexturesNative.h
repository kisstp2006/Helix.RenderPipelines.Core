#pragma once

#include <Helix/Engine/Rendering/Pipeline/RenderTargets.h>
#include <Helix/RenderPipelines/Core/Utilities/Blitter.h>
#include <Helix/RenderPipelines/Core/Utilities/CoreUtils.h>

// What C#'s handles, Blitter and CoreUtils call (internal): handles and systems by id, command buffers by id.
namespace helix::rendering
{
    // How a handle's render texture is made, as plain values (flags: 1 random write, 2 mips, 4 auto mips, 8 shadow map,
    // 16 bind MS, 32 dynamic scale, 64 explicit dynamic scale).
    HX_STRUCT(Namespace = "HelixEngine.Rendering.Internal", Internal)
    struct AllocData
    {
        HX_FIELD() i32 slices = 1;
        HX_FIELD() i32 depthBufferBits = 0;
        HX_FIELD() i32 format = 0;
        HX_FIELD() i32 filterMode = 0;
        HX_FIELD() i32 wrapMode = 0;
        HX_FIELD() i32 dimension = 2;
        HX_FIELD() i32 flags = 4;
        HX_FIELD() i32 anisoLevel = 1;
        HX_FIELD() f32 mipMapBias = 0;
        HX_FIELD() i32 msaaSamples = 1;
        HX_FIELD() i32 memoryless = 0;
        HX_FIELD() i32 vrUsage = 0;
    };

    // A RenderTargetIdentifier as plain values, laid out as C#'s RenderTargetIdentifier is (internal).
    HX_STRUCT(Namespace = "HelixEngine.Rendering.Internal", Internal)
    struct TargetData
    {
        HX_FIELD() i32 type = 0;
        HX_FIELD() i32 nameID = -1;
        HX_FIELD() u64 texture = 0;
        HX_FIELD() i32 mipLevel = 0;
        HX_FIELD() i32 face = -1;
        HX_FIELD() i32 depthSlice = 0;

        RenderTargetIdentifier ToIdentifier() const;
    };

    TargetData DataOf(const RenderTargetIdentifier& id);
    RTHandleAllocInfo InfoOf(const AllocData& data, StringView name);

    HX_CLASS(Namespace = "HelixEngine.Rendering.Internal", Static, Internal)
    class TexturesNative
    {
    public:
        // Systems (0: the default one).
        HX_FUNCTION() static u64 CreateSystem() { return (new RTHandleSystem())->Id(); }
        HX_FUNCTION() static u64 DefaultSystem() { return RTHandles::DefaultInstance().Id(); }
        HX_FUNCTION() static void DestroySystem(u64 system);
        HX_FUNCTION() static void Initialize(u64 system, i32 width, i32 height);
        HX_FUNCTION() static void Dispose(u64 system);
        HX_FUNCTION() static i32 MaxWidth(u64 system);
        HX_FUNCTION() static i32 MaxHeight(u64 system);
        HX_FUNCTION() static RTHandleProperties Properties(u64 system);
        HX_FUNCTION() static void SetReferenceSize(u64 system, i32 width, i32 height, bool reset);
        HX_FUNCTION() static void SetHardwareDynamicResolutionState(u64 system, bool value);
        HX_FUNCTION() static bool HardwareDynamicResRequested(u64 system);
        HX_FUNCTION() static u64 Alloc(u64 system, i32 width, i32 height, AllocData data, StringView name);
        HX_FUNCTION() static u64 AllocScaled(u64 system, Vector2 scale, AllocData data, StringView name);
        // A scale function: an unmanaged function of (user, width, height, out width, out height).
        HX_FUNCTION() static u64 AllocFunc(u64 system, u64 function, u64 user, AllocData data, StringView name);
        HX_FUNCTION() static u64 AllocTexture(u64 system, Texture* texture);
        HX_FUNCTION() static u64 AllocRenderTexture(u64 system, RenderTexture* texture, bool transferOwnership);
        HX_FUNCTION() static u64 AllocTarget(u64 system, TargetData target, StringView name);

        // Handles (an id no handle has answers the defaults).
        HX_FUNCTION() static bool IsValid(u64 handle) { return RTHandle::Find(handle) != nullptr; }
        HX_FUNCTION() static String Name(u64 handle);
        HX_FUNCTION() static RenderTexture* RenderTextureOf(u64 handle);
        HX_FUNCTION() static Texture* ExternalTexture(u64 handle);
        HX_FUNCTION() static TargetData NameID(u64 handle);
        HX_FUNCTION() static bool IsMSAAEnabled(u64 handle);
        HX_FUNCTION() static Vector2 ScaleFactor(u64 handle);
        HX_FUNCTION() static bool UseScaling(u64 handle);
        HX_FUNCTION() static Vector2Int ReferenceSize(u64 handle);
        HX_FUNCTION() static RTHandleProperties HandleProperties(u64 handle);
        HX_FUNCTION() static void SetCustomHandleProperties(u64 handle, RTHandleProperties properties);
        HX_FUNCTION() static void ClearCustomHandleProperties(u64 handle);
        HX_FUNCTION() static void SetRenderTexture(u64 handle, RenderTexture* texture);
        HX_FUNCTION() static void SetTexture(u64 handle, Texture* texture);
        HX_FUNCTION() static void SetTarget(u64 handle, TargetData target);
        HX_FUNCTION() static Vector2Int ScaledSize(u64 handle, Vector2Int referenceSize);
        HX_FUNCTION() static Vector2Int OwnScaledSize(u64 handle);
        HX_FUNCTION() static void Release(u64 handle);

        // CoreUtils.
        HX_FUNCTION() static void ClearRenderTarget(u64 buffer, i32 clearFlag, Color clearColor);
        HX_FUNCTION() static void SetHandleTarget(u64 buffer, u64 color, u64 depth, i32 clearFlag, Color clearColor, i32 mipLevel, i32 face, i32 depthSlice);
        HX_FUNCTION() static void SetHandleTargetActions(u64 buffer, u64 handle, i32 loadAction, i32 storeAction, i32 clearFlag, Color clearColor);
        HX_FUNCTION() static void SetViewport(u64 buffer, u64 handle, i32 mipLevel);
        HX_FUNCTION() static engine::Material* CreateEngineMaterial(engine::Shader* shader) { return CoreUtils::CreateEngineMaterial(shader); }
        HX_FUNCTION() static engine::Material* CreateEngineMaterialByName(StringView shaderPath) { return CoreUtils::CreateEngineMaterial(shaderPath); }
        HX_FUNCTION() static void Destroy(engine::Object* object) { CoreUtils::Destroy(object); }
        HX_FUNCTION() static String RenderTargetAutoName(i32 width, i32 height, i32 depth, StringView format, i32 dimension, StringView name, bool mips,
                                                         bool enableMSAA, i32 msaaSamples, bool dynamicRes)
        {
            return CoreUtils::GetRenderTargetAutoName(width, height, depth, format, engine::TextureDimension(dimension), name, mips, enableMSAA,
                                                      MSAASamples(msaaSamples), dynamicRes);
        }
        HX_FUNCTION() static String TextureAutoName(i32 width, i32 height, StringView format, i32 dimension, StringView name, bool mips, i32 depth)
        {
            return CoreUtils::GetTextureAutoName(width, height, format, engine::TextureDimension(dimension), name, mips, depth);
        }

        // Blitter.
        HX_FUNCTION() static void BlitterInitialize(engine::Shader* blit, engine::Shader* colorAndDepth) { Blitter::Initialize(blit, colorAndDepth); }
        HX_FUNCTION() static void BlitterCleanup() { Blitter::Cleanup(); }
        HX_FUNCTION() static bool BlitterInitialized() { return Blitter::IsInitialized(); }
        HX_FUNCTION() static engine::Material* BlitMaterial() { return Blitter::GetBlitMaterial(engine::TextureDimension::Tex2D); }
        HX_FUNCTION() static void BlitTexture(u64 buffer, TargetData source, Vector4 scaleBias, f32 mipLevel, bool bilinear);
        HX_FUNCTION() static void BlitTextureMaterial(u64 buffer, TargetData source, Vector4 scaleBias, engine::Material* material, i32 pass);
        HX_FUNCTION() static void BlitNoSource(u64 buffer, Vector4 scaleBias, engine::Material* material, i32 pass);
        HX_FUNCTION() static void BlitTextureInto(u64 buffer, TargetData source, TargetData destination, i32 loadAction,
                                                  i32 storeAction, engine::Material* material, i32 pass);
        HX_FUNCTION() static void BlitCameraTexture(u64 buffer, u64 source, u64 destination, Vector4 scaleBias, bool ownScale, f32 mipLevel, bool bilinear);
        HX_FUNCTION() static void BlitCameraTextureViewport(u64 buffer, u64 source, u64 destination, Rect viewport, f32 mipLevel, bool bilinear);
        HX_FUNCTION() static void BlitCameraTextureMaterial(u64 buffer, u64 source, u64 destination, i32 loadAction, i32 storeAction, engine::Material* material,
                                                            i32 pass);
        HX_FUNCTION() static void BlitQuad(u64 buffer, Texture* source, Vector4 scaleBiasTex, Vector4 scaleBiasRT, i32 mipLevelTex, bool bilinear);
        HX_FUNCTION() static void BlitColorAndDepth(u64 buffer, Texture* sourceColor, RenderTexture* sourceDepth, Vector4 scaleBias, f32 mipLevel, bool blitDepth);
    };
}
