#include <Helix/RenderPipelines/Core/Utilities/Blitter.h>
#include <Helix/RenderPipelines/Core/Utilities/CoreUtils.h>

#include <Helix/Core/Logging/Log.h>
#include <Helix/Engine/Shader/Shader.h>

namespace helix::rendering
{
    namespace
    {
        LogChannel BlitLog{"Rendering"};

        struct State
        {
            engine::EntityId blit, colorAndDepth;
            engine::MaterialPropertyBlock block;
        };

        State& S()
        {
            static State s;
            return s;
        }

        // The passes of the blit shader, in its order.
        constexpr i32 kNearest = 0, kBilinear = 1, kNearestQuad = 2, kBilinearQuad = 3;

        struct Ids
        {
            i32 texture = engine::PropertyToID("_BlitTexture");
            i32 scaleBias = engine::PropertyToID("_BlitScaleBias");
            i32 scaleBiasRt = engine::PropertyToID("_BlitScaleBiasRt");
            i32 mipLevel = engine::PropertyToID("_BlitMipLevel");
            i32 inputDepth = engine::PropertyToID("_InputDepthTexture");
        };

        const Ids& I()
        {
            static Ids ids;
            return ids;
        }

        engine::Material* Blit()
        {
            engine::Material* m = engine::EntityIdToObject<engine::Material>(S().blit);
            if (!m)
                HX_LOG_ERROR(BlitLog, "Blitter.Initialize was not called.");
            return m;
        }

        void SetSource(CommandBuffer& cmd, const RenderTargetIdentifier& source, Vector4 scaleBias, f32 mipLevel)
        {
            cmd.SetGlobalTexture(I().texture, source);
            S().block.SetVector(I().scaleBias, scaleBias);
            S().block.SetFloat(I().mipLevel, mipLevel);
        }

        void DrawTriangle(CommandBuffer& cmd, engine::Material* material, i32 pass)
        {
            if (material)
                cmd.DrawProcedural(Matrix4x4::identity, material, pass, engine::MeshTopology::Triangles, 3, 1, &S().block);
        }

        void DrawQuad(CommandBuffer& cmd, engine::Material* material, i32 pass)
        {
            if (material)
                cmd.DrawProcedural(Matrix4x4::identity, material, pass, engine::MeshTopology::Triangles, 6, 1, &S().block);
        }
    }

    void Blitter::Initialize(engine::Shader* blitPS, engine::Shader* blitColorAndDepthPS)
    {
        if (IsInitialized())
        {
            HX_LOG_ERROR(BlitLog, "Blitter is already initialized. Please only initialize the blitter once or you will leak engine resources. If you need "
                                  "to re-initialize the blitter with different shaders destroy & recreate it.");
            return;
        }
        engine::Material* blit = CoreUtils::CreateEngineMaterial(blitPS);
        engine::Material* both = CoreUtils::CreateEngineMaterial(blitColorAndDepthPS);
        S().blit = blit ? blit->GetEntityId() : engine::EntityId();
        S().colorAndDepth = both ? both->GetEntityId() : engine::EntityId();
        S().block.Clear();
    }

    void Blitter::Cleanup()
    {
        CoreUtils::Destroy(engine::EntityIdToObject(S().blit));
        CoreUtils::Destroy(engine::EntityIdToObject(S().colorAndDepth));
        S().blit = S().colorAndDepth = engine::EntityId();
    }

    bool Blitter::IsInitialized() { return engine::EntityIdToObject(S().blit) != nullptr; }

    engine::Material* Blitter::GetBlitMaterial(engine::TextureDimension, bool) { return engine::EntityIdToObject<engine::Material>(S().blit); }

    void Blitter::BlitTexture(CommandBuffer& cmd, const RenderTargetIdentifier& source, Vector4 scaleBias, f32 mipLevel, bool bilinear)
    {
        S().block.Clear();
        SetSource(cmd, source, scaleBias, mipLevel);
        DrawTriangle(cmd, Blit(), bilinear ? kBilinear : kNearest);
    }

    void Blitter::BlitTexture(CommandBuffer& cmd, const RenderTargetIdentifier& source, Vector4 scaleBias, engine::Material* material, i32 pass)
    {
        S().block.Clear();
        SetSource(cmd, source, scaleBias, 0);
        DrawTriangle(cmd, material, pass);
    }

    void Blitter::BlitTexture(CommandBuffer& cmd, Vector4 scaleBias, engine::Material* material, i32 pass)
    {
        S().block.Clear();
        S().block.SetVector(I().scaleBias, scaleBias);
        DrawTriangle(cmd, material, pass);
    }

    void Blitter::BlitTexture(CommandBuffer& cmd, const RenderTargetIdentifier& source, const RenderTargetIdentifier& destination, engine::Material* material,
                              i32 pass)
    {
        cmd.SetRenderTarget(destination, 0, engine::CubemapFace::Unknown, -1);
        BlitTexture(cmd, source, Vector4(1, 1, 0, 0), material, pass);
    }

    void Blitter::BlitTexture(CommandBuffer& cmd, const RenderTargetIdentifier& source, const RenderTargetIdentifier& destination,
                              engine::RenderBufferLoadAction load, engine::RenderBufferStoreAction store, engine::Material* material, i32 pass)
    {
        cmd.SetRenderTarget(destination, load, store);
        BlitTexture(cmd, source, Vector4(1, 1, 0, 0), material, pass);
    }

    Vector2 Blitter::ViewportScale(const RTHandle& source)
    {
        if (!source.useScaling())
            return Vector2(1, 1);
        Vector4 s = source.rtHandleProperties().rtHandleScale;
        return Vector2(s.x, s.y);
    }

    void Blitter::BlitCameraTexture(CommandBuffer& cmd, const RTHandle& source, const RTHandle& destination, f32 mipLevel, bool bilinear)
    {
        Vector2 scale = ViewportScale(source);
        CoreUtils::SetRenderTarget(cmd, destination);
        BlitTexture(cmd, source.nameID(), Vector4(scale.x, scale.y, 0, 0), mipLevel, bilinear);
    }

    void Blitter::BlitCameraTexture(CommandBuffer& cmd, const RTHandle& source, const RTHandle& destination, Vector4 scaleBias, f32 mipLevel, bool bilinear)
    {
        CoreUtils::SetRenderTarget(cmd, destination);
        BlitTexture(cmd, source.nameID(), scaleBias, mipLevel, bilinear);
    }

    void Blitter::BlitCameraTexture(CommandBuffer& cmd, const RTHandle& source, const RTHandle& destination, Rect destViewport, f32 mipLevel, bool bilinear)
    {
        Vector2 scale = ViewportScale(source);
        CoreUtils::SetRenderTarget(cmd, destination);
        cmd.SetViewport(destViewport);
        BlitTexture(cmd, source.nameID(), Vector4(scale.x, scale.y, 0, 0), mipLevel, bilinear);
    }

    void Blitter::BlitCameraTexture(CommandBuffer& cmd, const RTHandle& source, const RTHandle& destination, engine::Material* material, i32 pass)
    {
        Vector2 scale = ViewportScale(source);
        CoreUtils::SetRenderTarget(cmd, destination);
        BlitTexture(cmd, source.nameID(), Vector4(scale.x, scale.y, 0, 0), material, pass);
    }

    void Blitter::BlitCameraTexture(CommandBuffer& cmd, const RTHandle& source, const RTHandle& destination, engine::RenderBufferLoadAction load,
                                    engine::RenderBufferStoreAction store, engine::Material* material, i32 pass)
    {
        Vector2 scale = ViewportScale(source);
        CoreUtils::SetRenderTarget(cmd, destination, load, store, ClearFlag::None, Color(0, 0, 0, 0));
        BlitTexture(cmd, source.nameID(), Vector4(scale.x, scale.y, 0, 0), material, pass);
    }

    void Blitter::BlitQuad(CommandBuffer& cmd, Texture* source, Vector4 scaleBiasTex, Vector4 scaleBiasRT, i32 mipLevelTex, bool bilinear)
    {
        S().block.Clear();
        SetSource(cmd, RenderTargetIdentifier(source), scaleBiasTex, f32(mipLevelTex));
        S().block.SetVector(I().scaleBiasRt, scaleBiasRT);
        DrawQuad(cmd, Blit(), bilinear ? kBilinearQuad : kNearestQuad);
    }

    void Blitter::BlitColorAndDepth(CommandBuffer& cmd, Texture* sourceColor, RenderTexture* sourceDepth, Vector4 scaleBias, f32 mipLevel, bool blitDepth)
    {
        engine::Material* m = engine::EntityIdToObject<engine::Material>(S().colorAndDepth);
        if (!m)
        {
            HX_LOG_ERROR(BlitLog, "Blitter.Initialize was not called.");
            return;
        }
        S().block.Clear();
        SetSource(cmd, RenderTargetIdentifier(sourceColor), scaleBias, mipLevel);
        if (blitDepth)
            cmd.SetGlobalTexture(I().inputDepth, RenderTargetIdentifier(static_cast<Texture*>(sourceDepth)));
        DrawTriangle(cmd, m, blitDepth ? 1 : 0);
    }
}
