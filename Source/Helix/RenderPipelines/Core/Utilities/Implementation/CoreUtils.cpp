#include <Helix/RenderPipelines/Core/Utilities/CoreUtils.h>

#include <Helix/Core/Logging/Log.h>
#include <Helix/Core/Strings/Format.h>
#include <Helix/Engine/Application/Application.h>
#include <Helix/Engine/Material/Material.h>
#include <Helix/Engine/Shader/Shader.h>
#include <Helix/Scripting/Types/Type.h>

namespace helix::rendering
{
    namespace
    {
        LogChannel CoreLog{"Rendering"};

        void ViewportOf(CommandBuffer& cmd, const RTHandle& target)
        {
            if (!target.useScaling())
                return;
            Vector2Int size = target.GetScaledSize(target.rtHandleProperties().currentViewportSize);
            cmd.SetViewport(Rect(0, 0, f32(size.x), f32(size.y)));
        }
    }

    void CoreUtils::ClearRenderTarget(CommandBuffer& cmd, ClearFlag clearFlag, Color clearColor)
    {
        if (clearFlag != ClearFlag::None)
            cmd.ClearRenderTarget(engine::RTClearFlags(i32(clearFlag)), clearColor, 1.0f, 0);
    }

    void CoreUtils::SetRenderTarget(CommandBuffer& cmd, const RenderTargetIdentifier& buffer, ClearFlag clearFlag, Color clearColor, i32 mipLevel,
                                    engine::CubemapFace face, i32 depthSlice)
    {
        cmd.SetRenderTarget(buffer, mipLevel, face, depthSlice);
        ClearRenderTarget(cmd, clearFlag, clearColor);
    }

    void CoreUtils::SetRenderTarget(CommandBuffer& cmd, const RenderTargetIdentifier& color, const RenderTargetIdentifier& depth, ClearFlag clearFlag,
                                    Color clearColor, i32 mipLevel, engine::CubemapFace face, i32 depthSlice)
    {
        cmd.SetRenderTarget(color, depth, mipLevel, face, depthSlice);
        ClearRenderTarget(cmd, clearFlag, clearColor);
    }

    void CoreUtils::SetRenderTarget(CommandBuffer& cmd, Span<const RenderTargetIdentifier> colors, const RenderTargetIdentifier& depth, ClearFlag clearFlag,
                                    Color clearColor)
    {
        cmd.SetRenderTarget(colors, depth);
        ClearRenderTarget(cmd, clearFlag, clearColor);
    }

    void CoreUtils::SetRenderTarget(CommandBuffer& cmd, const RenderTargetIdentifier& buffer, engine::RenderBufferLoadAction load,
                                    engine::RenderBufferStoreAction store, ClearFlag clearFlag, Color clearColor)
    {
        cmd.SetRenderTarget(buffer, load, store);
        ClearRenderTarget(cmd, clearFlag, clearColor);
    }

    void CoreUtils::SetRenderTarget(CommandBuffer& cmd, const RTHandle& buffer, ClearFlag clearFlag, Color clearColor, i32 mipLevel, engine::CubemapFace face,
                                    i32 depthSlice)
    {
        cmd.SetRenderTarget(buffer.nameID(), mipLevel, face, depthSlice);
        ViewportOf(cmd, buffer);
        ClearRenderTarget(cmd, clearFlag, clearColor);
    }

    void CoreUtils::SetRenderTarget(CommandBuffer& cmd, const RTHandle& color, const RTHandle& depth, ClearFlag clearFlag, Color clearColor, i32 mipLevel,
                                    engine::CubemapFace face, i32 depthSlice)
    {
        cmd.SetRenderTarget(color.nameID(), depth.nameID(), mipLevel, face, depthSlice);
        ViewportOf(cmd, color);
        ClearRenderTarget(cmd, clearFlag, clearColor);
    }

    void CoreUtils::SetRenderTarget(CommandBuffer& cmd, const RTHandle& buffer, engine::RenderBufferLoadAction load, engine::RenderBufferStoreAction store,
                                    ClearFlag clearFlag, Color clearColor)
    {
        cmd.SetRenderTarget(buffer.nameID(), load, store);
        ViewportOf(cmd, buffer);
        ClearRenderTarget(cmd, clearFlag, clearColor);
    }

    void CoreUtils::SetViewport(CommandBuffer& cmd, const RTHandle* target, i32 mipLevel)
    {
        if (!target || !target->useScaling())
            return;
        Vector2Int size = target->GetScaledSize(target->rtHandleProperties().currentViewportSize);
        i32 w = size.x >> mipLevel, h = size.y >> mipLevel;
        cmd.SetViewport(Rect(0, 0, f32(w < 1 ? 1 : w), f32(h < 1 ? 1 : h)));
    }

    engine::Material* CoreUtils::CreateEngineMaterial(StringView shaderPath)
    {
        engine::Shader* shader = engine::Shader::Find(shaderPath);
        if (!shader)
        {
            HX_LOG_ERROR(CoreLog, "Cannot create required material because shader {} could not be found", shaderPath);
            return nullptr;
        }
        return CreateEngineMaterial(shader);
    }

    engine::Material* CoreUtils::CreateEngineMaterial(engine::Shader* shader)
    {
        if (!shader)
        {
            HX_LOG_ERROR(CoreLog, "Cannot create required material because shader is null");
            return nullptr;
        }
        engine::Material* m = engine::Material::Create(shader);
        m->hideFlags = engine::HideFlags::HideAndDontSave;
        return m;
    }

    void CoreUtils::Destroy(engine::Object* object)
    {
        if (!object)
            return;
        if (engine::Application::isPlaying())
            engine::Object::Destroy(object);
        else
            engine::Object::DestroyImmediate(object);
    }

    void CoreUtils::SetKeyword(CommandBuffer& cmd, StringView keyword, bool state)
    {
        if (state)
            cmd.EnableShaderKeyword(keyword);
        else
            cmd.DisableShaderKeyword(keyword);
    }

    void CoreUtils::SetKeyword(engine::Material& material, StringView keyword, bool state)
    {
        if (state)
            material.EnableKeyword(keyword);
        else
            material.DisableKeyword(keyword);
    }

    void CoreUtils::DrawFullScreen(CommandBuffer& cmd, engine::Material* material, const engine::MaterialPropertyBlock* properties, i32 shaderPassId)
    {
        cmd.DrawProcedural(Matrix4x4::identity, material, shaderPassId, engine::MeshTopology::Triangles, 3, 1, properties);
    }

    void CoreUtils::DrawFullScreen(CommandBuffer& cmd, engine::Material* material, const RenderTargetIdentifier& colorBuffer,
                                   const engine::MaterialPropertyBlock* properties, i32 shaderPassId)
    {
        cmd.SetRenderTarget(colorBuffer, 0, engine::CubemapFace::Unknown, -1);
        DrawFullScreen(cmd, material, properties, shaderPassId);
    }

    String CoreUtils::EnumName(StringView fullName, i64 value)
    {
        for (const scripting::EnumValueInfo& e : scripting::EnumValues(scripting::FindType(fullName)))
            if (e.value == value)
                return String(e.name);
        return Format("{}", value);
    }

    String CoreUtils::GetRenderTargetAutoName(i32 width, i32 height, i32 depth, GraphicsFormat format, engine::TextureDimension dimension, StringView name,
                                              bool mips, bool enableMSAA, MSAASamples msaaSamples, bool dynamicRes)
    {
        return GetRenderTargetAutoName(width, height, depth, EnumName(format), dimension, name, mips, enableMSAA, msaaSamples, dynamicRes);
    }

    String CoreUtils::GetRenderTargetAutoName(i32 width, i32 height, i32 depth, StringView format, engine::TextureDimension dimension, StringView name, bool mips,
                                              bool enableMSAA, MSAASamples msaaSamples, bool dynamicRes)
    {
        String result = Format("{}_{}x{}", name, width, height);
        if (depth > 1)
            result += Format("x{}", depth);
        if (mips)
            result += "_Mips";
        result += Format("_{}", format);
        if (dimension != engine::TextureDimension::None)
            result += Format("_{}", EnumName(dimension));
        if (enableMSAA)
            result += Format("_{}", EnumName(msaaSamples));
        if (dynamicRes)
            result += "_dynamic";
        return result;
    }

    String CoreUtils::GetTextureAutoName(i32 width, i32 height, StringView format, engine::TextureDimension dimension, StringView name, bool mips, i32 depth)
    {
        String size = depth == 0 ? Format("{}x{}{}_{}", width, height, mips ? "_Mips" : "", format)
                                 : Format("{}x{}x{}{}_{}", width, height, depth, mips ? "_Mips" : "", format);
        String prefix = name.Empty() ? String() : Format("{}_", name);
        String dim = dimension == engine::TextureDimension::None ? String() : EnumName(dimension);
        return Format("Texture_{}{}{}", prefix, dim, size);
    }
}
