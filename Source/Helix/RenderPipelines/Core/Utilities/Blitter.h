#pragma once

#include <Helix/Engine/Material/Material.h>
#include <Helix/Engine/Rendering/Pipeline/CommandBuffer.h>
#include <Helix/RenderPipelines/Core/Textures/RTHandle.h>

// Copies with the core's blit shaders: a full screen triangle (or a quad) reading _BlitTexture (a global the Blitter
// sets) scaled and offset by _BlitScaleBias at _BlitMipLevel. The pipelines give Initialize their blit shaders
// (Hidden/Core/Blit and Hidden/Core/BlitColorAndDepth are the core's).
namespace helix::rendering
{
    using engine::CommandBuffer;

    class Blitter
    {
    public:
        static void Initialize(engine::Shader* blitPS, engine::Shader* blitColorAndDepthPS);
        static void Cleanup();
        static bool IsInitialized();
        // The material of 2D blits (the devices of this milestone blit one slice of the other kinds).
        static engine::Material* GetBlitMaterial(engine::TextureDimension dimension, bool singleSlice = false);

        // Into the bound target.
        static void BlitTexture(CommandBuffer& cmd, const RenderTargetIdentifier& source, Vector4 scaleBias, f32 mipLevel, bool bilinear);
        static void BlitTexture(CommandBuffer& cmd, const RenderTargetIdentifier& source, Vector4 scaleBias, engine::Material* material, i32 pass);
        // A material's full screen pass without a source.
        static void BlitTexture(CommandBuffer& cmd, Vector4 scaleBias, engine::Material* material, i32 pass);
        // Into a target.
        static void BlitTexture(CommandBuffer& cmd, const RenderTargetIdentifier& source, const RenderTargetIdentifier& destination, engine::Material* material,
                                i32 pass);
        static void BlitTexture(CommandBuffer& cmd, const RenderTargetIdentifier& source, const RenderTargetIdentifier& destination,
                                engine::RenderBufferLoadAction load, engine::RenderBufferStoreAction store, engine::Material* material, i32 pass);
        // The used part of a handle into another (its viewport set).
        static void BlitCameraTexture(CommandBuffer& cmd, const RTHandle& source, const RTHandle& destination, f32 mipLevel = 0, bool bilinear = false);
        static void BlitCameraTexture(CommandBuffer& cmd, const RTHandle& source, const RTHandle& destination, Vector4 scaleBias, f32 mipLevel = 0,
                                      bool bilinear = false);
        static void BlitCameraTexture(CommandBuffer& cmd, const RTHandle& source, const RTHandle& destination, Rect destViewport, f32 mipLevel = 0,
                                      bool bilinear = false);
        static void BlitCameraTexture(CommandBuffer& cmd, const RTHandle& source, const RTHandle& destination, engine::Material* material, i32 pass);
        static void BlitCameraTexture(CommandBuffer& cmd, const RTHandle& source, const RTHandle& destination, engine::RenderBufferLoadAction load,
                                      engine::RenderBufferStoreAction store, engine::Material* material, i32 pass);
        // The source (scaleBiasTex of it) into the part of the target scaleBiasRT gives (xy size, zw offset, 0 to 1).
        static void BlitQuad(CommandBuffer& cmd, Texture* source, Vector4 scaleBiasTex, Vector4 scaleBiasRT, i32 mipLevelTex, bool bilinear);
        // A color texture into the bound target, and its depth from a depth texture when blitDepth.
        static void BlitColorAndDepth(CommandBuffer& cmd, Texture* sourceColor, RenderTexture* sourceDepth, Vector4 scaleBias, f32 mipLevel, bool blitDepth);

        // The part of a handle used this frame (its scale when it follows the reference size).
        static Vector2 ViewportScale(const RTHandle& source);
    };
}
