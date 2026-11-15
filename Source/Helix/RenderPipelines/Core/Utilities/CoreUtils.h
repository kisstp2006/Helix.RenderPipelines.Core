#pragma once

#include <Helix/Core/Strings/String.h>
#include <Helix/Engine/Rendering/Pipeline/CommandBuffer.h>
#include <Helix/Engine/Texture/RenderTexture.h>
#include <Helix/RenderPipelines/Core/Textures/RTHandle.h>

// Helpers of the pipelines: targets set and cleared (with the viewport of a handle), engine materials, keywords, full
// screen draws, the names of render targets and textures.
namespace helix::rendering
{
    using engine::CommandBuffer;

    HX_ENUM(Namespace = "HelixEngine.Rendering", Attributes = "[System.Flags]")
    enum class ClearFlag : i32
    {
        None = 0,
        Color = 1,
        Depth = 2,
        Stencil = 4,
        DepthStencil = 6,
        ColorStencil = 5,
        All = 7,
    };

    class CoreUtils
    {
    public:
        static void ClearRenderTarget(CommandBuffer& cmd, ClearFlag clearFlag, Color clearColor);
        static void SetRenderTarget(CommandBuffer& cmd, const RenderTargetIdentifier& buffer, ClearFlag clearFlag = ClearFlag::None,
                                    Color clearColor = Color(0, 0, 0, 0), i32 mipLevel = 0, engine::CubemapFace face = engine::CubemapFace::Unknown,
                                    i32 depthSlice = -1);
        static void SetRenderTarget(CommandBuffer& cmd, const RenderTargetIdentifier& color, const RenderTargetIdentifier& depth,
                                    ClearFlag clearFlag = ClearFlag::None, Color clearColor = Color(0, 0, 0, 0), i32 mipLevel = 0,
                                    engine::CubemapFace face = engine::CubemapFace::Unknown, i32 depthSlice = -1);
        static void SetRenderTarget(CommandBuffer& cmd, Span<const RenderTargetIdentifier> colors, const RenderTargetIdentifier& depth,
                                    ClearFlag clearFlag = ClearFlag::None, Color clearColor = Color(0, 0, 0, 0));
        static void SetRenderTarget(CommandBuffer& cmd, const RenderTargetIdentifier& buffer, engine::RenderBufferLoadAction load,
                                    engine::RenderBufferStoreAction store, ClearFlag clearFlag, Color clearColor);
        // A handle as the target: the viewport is its scaled size this frame.
        static void SetRenderTarget(CommandBuffer& cmd, const RTHandle& buffer, ClearFlag clearFlag = ClearFlag::None, Color clearColor = Color(0, 0, 0, 0),
                                    i32 mipLevel = 0, engine::CubemapFace face = engine::CubemapFace::Unknown, i32 depthSlice = -1);
        static void SetRenderTarget(CommandBuffer& cmd, const RTHandle& color, const RTHandle& depth, ClearFlag clearFlag = ClearFlag::None,
                                    Color clearColor = Color(0, 0, 0, 0), i32 mipLevel = 0, engine::CubemapFace face = engine::CubemapFace::Unknown,
                                    i32 depthSlice = -1);
        static void SetRenderTarget(CommandBuffer& cmd, const RTHandle& buffer, engine::RenderBufferLoadAction load, engine::RenderBufferStoreAction store,
                                    ClearFlag clearFlag, Color clearColor);
        static void SetViewport(CommandBuffer& cmd, const RTHandle* target, i32 mipLevel = 0);

        // A material of a shader of the engine or a pipeline, kept out of scenes (null with an error when the shader is missing).
        static engine::Material* CreateEngineMaterial(StringView shaderPath);
        static engine::Material* CreateEngineMaterial(engine::Shader* shader);
        // Destroyed at once outside play mode, at the frame's end in it.
        static void Destroy(engine::Object* object);

        static void SetKeyword(CommandBuffer& cmd, StringView keyword, bool state);
        static void SetKeyword(engine::Material& material, StringView keyword, bool state);

        // A full screen triangle: three vertices whose positions the shader makes from the vertex id.
        static void DrawFullScreen(CommandBuffer& cmd, engine::Material* material, const engine::MaterialPropertyBlock* properties = nullptr,
                                   i32 shaderPassId = 0);
        static void DrawFullScreen(CommandBuffer& cmd, engine::Material* material, const RenderTargetIdentifier& colorBuffer,
                                   const engine::MaterialPropertyBlock* properties = nullptr, i32 shaderPassId = 0);

        // "<name>_<width>x<height>[x<depth>][_Mips]_<format>[_<dimension>][_<samples>][_dynamic]".
        static String GetRenderTargetAutoName(i32 width, i32 height, i32 depth, GraphicsFormat format, engine::TextureDimension dimension, StringView name,
                                              bool mips = false, bool enableMSAA = false, MSAASamples msaaSamples = MSAASamples::None, bool dynamicRes = false);
        static String GetRenderTargetAutoName(i32 width, i32 height, i32 depth, StringView format, engine::TextureDimension dimension, StringView name,
                                              bool mips, bool enableMSAA, MSAASamples msaaSamples, bool dynamicRes);
        // "Texture_[<name>_][<dimension>]<width>x<height>[x<depth>][_Mips]_<format>".
        static String GetTextureAutoName(i32 width, i32 height, StringView format, engine::TextureDimension dimension, StringView name, bool mips, i32 depth);
        // The name of an enum value (its C# name), the enum found by its C# full name (the engine's types are
        // registered by the engine's binary, where TypeOf of this module does not see them).
        static String EnumName(StringView fullName, i64 value);
        static String EnumName(GraphicsFormat value) { return EnumName("HelixEngine.Experimental.Rendering.GraphicsFormat", i64(value)); }
        static String EnumName(engine::TextureDimension value) { return EnumName("HelixEngine.Rendering.TextureDimension", i64(value)); }
        static String EnumName(MSAASamples value) { return EnumName("HelixEngine.Rendering.MSAASamples", i64(value)); }
    };
}
