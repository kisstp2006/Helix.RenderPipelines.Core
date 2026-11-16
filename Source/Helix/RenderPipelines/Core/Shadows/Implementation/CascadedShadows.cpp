#include <Helix/RenderPipelines/Core/Shadows/CascadedShadows.h>

#include <Helix/Core/Strings/Format.h>
#include <Helix/Engine/Rendering/GL.h>
#include <Helix/Engine/Rendering/Light.h>
#include <Helix/Engine/Shader/Shader.h>

#include <cmath>

namespace helix::rendering
{
    using namespace engine;

    Matrix4x4 CascadedShadows::GetShadowTransform(const Matrix4x4& projection, const Matrix4x4& view)
    {
        // The engine's GPU projection has its depth in 0 to 1 already: only xy go from -1..1 to 0..1.
        Matrix4x4 gpu = GL::GetGPUProjectionMatrix(projection, false);
        Matrix4x4 scaleBias = Matrix4x4::identity;
        scaleBias(0, 0) = 0.5f;
        scaleBias(1, 1) = 0.5f;
        scaleBias(0, 3) = 0.5f;
        scaleBias(1, 3) = 0.5f;
        return scaleBias * gpu * view;
    }

    bool CascadedShadows::ExtractDirectionalLightMatrix(const CullingResults& cullResults, i32 shadowLightIndex, i32 cascadeIndex, i32 cascadeCount,
                                                        Vector3 splitRatio, i32 shadowmapWidth, i32 shadowmapHeight, i32 shadowResolution, f32 shadowNearPlane,
                                                        Vector4& cascadeSplitDistance, ShadowSliceData& slice)
    {
        CullingResults culling = cullResults;
        bool ok = culling.ComputeDirectionalShadowMatricesAndCullingPrimitives(shadowLightIndex, cascadeIndex, Max(1, cascadeCount), splitRatio, shadowResolution,
                                                                                shadowNearPlane, slice.viewMatrix, slice.projectionMatrix, slice.splitData);
        cascadeSplitDistance = slice.splitData.cullingSphere;
        slice.splitData.shadowCascadeBlendCullingFactor = 1.0f;
        slice.offsetX = (cascadeIndex % 2) * shadowResolution;
        slice.offsetY = (cascadeIndex / 2) * shadowResolution;
        slice.resolution = shadowResolution;
        slice.shadowTransform = GetShadowTransform(slice.projectionMatrix, slice.viewMatrix);
        // Cascades share the atlas: each transform goes to its tile.
        if (cascadeCount > 1)
            ApplySliceTransform(slice, shadowmapWidth, shadowmapHeight);
        return ok;
    }

    void CascadedShadows::ApplySliceTransform(ShadowSliceData& slice, i32 atlasWidth, i32 atlasHeight)
    {
        Matrix4x4 sliceTransform = Matrix4x4::identity;
        f32 oneOverAtlasWidth = 1.0f / f32(atlasWidth), oneOverAtlasHeight = 1.0f / f32(atlasHeight);
        sliceTransform(0, 0) = f32(slice.resolution) * oneOverAtlasWidth;
        sliceTransform(1, 1) = f32(slice.resolution) * oneOverAtlasHeight;
        sliceTransform(0, 3) = f32(slice.offsetX) * oneOverAtlasWidth;
        sliceTransform(1, 3) = f32(slice.offsetY) * oneOverAtlasHeight;
        slice.shadowTransform = sliceTransform * slice.shadowTransform;
    }

    i32 CascadedShadows::GetMaxTileResolutionInAtlas(i32 atlasWidth, i32 atlasHeight, i32 tileCount)
    {
        i32 resolution = Min(atlasWidth, atlasHeight);
        if (resolution <= 0 || tileCount <= 0)
            return 0;
        i32 count = atlasWidth / resolution * atlasHeight / resolution;
        while (count < tileCount)
        {
            resolution >>= 1;
            if (resolution == 0)
                return 0;
            count = atlasWidth / resolution * atlasHeight / resolution;
        }
        return resolution;
    }

    Vector4 CascadedShadows::GetShadowBias(const VisibleLight& shadowLight, Vector2 bias, bool soft, const Matrix4x4& lightProjectionMatrix, f32 shadowResolution)
    {
        if (shadowResolution <= 0)
            return Vector4();
        f32 frustumSize = 0;
        switch (shadowLight.lightType)
        {
        case LightType::Directional: frustumSize = 2.0f / lightProjectionMatrix(0, 0); break;
        case LightType::Spot: frustumSize = std::tan(shadowLight.spotAngle * 0.5f * mathf::Deg2Rad) * shadowLight.range; break;
        case LightType::Point: frustumSize = std::tan(90.0f * 0.5f * mathf::Deg2Rad) * shadowLight.range; break;
        default: break;
        }
        f32 texelSize = frustumSize / shadowResolution;
        f32 depthBias = -bias.x * texelSize;
        f32 normalBias = -bias.y * texelSize;
        // A soft shadow samples up to a few texels away: the bias scaled by the filter's radius.
        if (soft)
        {
            constexpr f32 kernelRadius = 2.5f;
            depthBias *= kernelRadius;
            normalBias *= kernelRadius;
        }
        return Vector4(depthBias, normalBias, 0, 0);
    }

    void CascadedShadows::GetScaleAndBiasForLinearDistanceFade(f32 fadeDistance, f32 border, f32& scale, f32& bias)
    {
        if (border < 0.0001f)
        {
            constexpr f32 multiplier = 1000.0f;
            scale = multiplier;
            bias = -fadeDistance * multiplier;
            return;
        }
        border = 1.0f - border;
        border *= border;
        // A linear fade from (1 - border)^2 of the distance to the distance itself.
        f32 distanceFadeNear = border * fadeDistance;
        scale = 1.0f / (fadeDistance - distanceFadeNear);
        bias = -distanceFadeNear / (fadeDistance - distanceFadeNear);
    }

    void CascadedShadows::SetupShadowCasterConstantBuffer(CommandBuffer& cmd, const VisibleLight& shadowLight, const Vector4& shadowBias)
    {
        static const i32 biasId = Shader::PropertyToID("_ShadowBias");
        static const i32 directionId = Shader::PropertyToID("_LightDirection");
        static const i32 positionId = Shader::PropertyToID("_LightPosition");
        cmd.SetGlobalVector(biasId, shadowBias);
        // Toward the light: the negated forward axis of its transform.
        Vector4 forward = shadowLight.localToWorldMatrix.GetColumn(2);
        cmd.SetGlobalVector(directionId, Vector4(-forward.x, -forward.y, -forward.z, 0));
        Vector4 position = shadowLight.localToWorldMatrix.GetColumn(3);
        cmd.SetGlobalVector(positionId, Vector4(position.x, position.y, position.z, 1));
    }

    namespace
    {
        struct ShadowPassData
        {
            RendererListHandle lists[4];
            RenderGraph* graph = nullptr;
            Rect tiles[4];
            Matrix4x4 views[4], projections[4];
            // Per cascade the caster's depth and normal bias; the light they are for.
            Vector4 biases[4];
            VisibleLight light;
            // The camera's matrices, set again after the cascades.
            Matrix4x4 cameraView, cameraProjection;
            i32 count = 0;
        };
    }

    TextureHandle CascadedShadows::Record(RenderGraph& graph, ScriptableRenderContext& context, CullingResults culling, const CascadedShadowSettings& s,
                                          const Matrix4x4& cameraView, const Matrix4x4& cameraProjection, CommandBuffer& cmd)
    {
        Span<const VisibleLight> lights = culling.visibleLights();
        if (s.lightIndex < 0 || u32(s.lightIndex) >= lights.Size())
            return TextureHandle();
        i32 count = Min(Max(s.cascadeCount, 1), 4);
        i32 width = s.atlasWidth, height = s.atlasHeight;
        // The tiles: one whole, two side by side, four in a square for 3 and 4.
        i32 tile = GetMaxTileResolutionInAtlas(width, height, count);
        const VisibleLight& main = lights[u32(s.lightIndex)];
        ShadowPassData* data = nullptr;
        PassBuilder<ShadowPassData> b = graph.AddRasterRenderPass("MainLightShadow", data);
        data->count = count;
        data->graph = &graph;
        data->light = main;
        data->cameraView = cameraView;
        data->cameraProjection = cameraProjection;
        ShadowSliceData slices[4];
        ShadowSplitData splits[4];
        Vector4 spheres[4];
        Array<Matrix4x4> worldToShadow;
        bool ok = tile > 0;
        for (i32 i = 0; i < count && ok; i++)
        {
            ok = ExtractDirectionalLightMatrix(culling, s.lightIndex, i, count, s.splits, width, height, tile, s.nearPlane, spheres[i], slices[i]);
            splits[i] = slices[i].splitData;
            data->views[i] = slices[i].viewMatrix;
            data->projections[i] = slices[i].projectionMatrix;
            data->tiles[i] = Rect(f32(slices[i].offsetX), f32(slices[i].offsetY), f32(tile), f32(tile));
            // Each cascade's bias by the size of its texels.
            data->biases[i] = GetShadowBias(main, s.bias, s.soft, slices[i].projectionMatrix, f32(tile));
            worldToShadow.Push(slices[i].shadowTransform);
        }
        TextureHandle atlas;
        if (ok)
        {
            Array<LightShadowCasterCullingInfo> infos;
            infos.Resize(lights.Size());
            infos[u32(s.lightIndex)].splitStart = 0;
            infos[u32(s.lightIndex)].splitLength = count;
            infos[u32(s.lightIndex)].projectionType = BatchCullingProjectionType::Orthographic;
            context.CullShadowCasters(culling, ShadowCastersCullingInfos{Span<const LightShadowCasterCullingInfo>(infos.Data(), infos.Size()),
                                                                          Span<const ShadowSplitData>(splits, usize(count))});
            TextureDesc atlasDesc(width, height);
            atlasDesc.colorFormat = GraphicsFormat::None;
            atlasDesc.depthBufferBits = DepthBits(s.depthBits);
            atlasDesc.isShadowMap = true;
            atlasDesc.filterMode = FilterMode::Bilinear;
            atlasDesc.wrapMode = TextureWrapMode::Clamp;
            atlasDesc.clearBuffer = true;
            atlasDesc.name = String("_MainLightShadowmapTexture");
            atlas = graph.CreateTexture(atlasDesc);
            for (i32 i = 0; i < count; i++)
            {
                ShadowDrawingSettings shadow(culling, s.lightIndex, BatchCullingProjectionType::Orthographic);
                shadow.splitIndex = i;
                data->lists[i] = graph.CreateShadowRendererList(shadow);
                b.UseRendererList(data->lists[i]);
            }
            b.SetRenderAttachmentDepth(atlas);
            b.AllowGlobalStateModification(true);
            b.SetGlobalTextureAfterPass(atlas, Shader::PropertyToID("_MainLightShadowmapTexture"));
            b.SetRenderFunc([](ShadowPassData& d, RasterGraphContext& ctx) {
                ctx.cmd->SetGlobalDepthBias(1.0f, 2.5f);
                for (i32 i = 0; i < d.count; i++)
                {
                    SetupShadowCasterConstantBuffer(*ctx.cmd, d.light, d.biases[i]);
                    ctx.cmd->SetViewport(d.tiles[i]);
                    ctx.cmd->SetViewProjectionMatrices(d.views[i], d.projections[i]);
                    ctx.cmd->DrawRendererList(d.graph->ResolveList(d.lists[i]));
                }
                ctx.cmd->SetGlobalDepthBias(0.0f, 0.0f);
                ctx.cmd->SetViewProjectionMatrices(d.cameraView, d.cameraProjection);
            });
            // What the shaders read: per cascade the world to shadow matrix (one more for beyond the last), the
            // spheres and their squared radii, the strength, soft shadow quality and fade, the map's size and the
            // offsets of the four taps of the low tier (half a texel each way, two taps in each vector).
            // Always five (a global array keeps the size it was first set with).
            while (worldToShadow.Size() < 5)
                worldToShadow.Push(Matrix4x4::zero);
            cmd.SetGlobalMatrixArray(Shader::PropertyToID("_MainLightWorldToShadow"), Span<const Matrix4x4>(worldToShadow.Data(), worldToShadow.Size()));
            Vector4 radii;
            for (i32 i = 0; i < 4; i++)
            {
                Vector4 sphere = i < count ? spheres[i] : Vector4();
                cmd.SetGlobalVector(Shader::PropertyToID(Format("_CascadeShadowSplitSpheres{}", i)), sphere);
                (&radii.x)[i] = sphere.w * sphere.w;
            }
            cmd.SetGlobalVector(Shader::PropertyToID("_CascadeShadowSplitSphereRadii"), radii);
            f32 fadeScale = 0, fadeBias = 0;
            GetScaleAndBiasForLinearDistanceFade(s.shadowDistance * s.shadowDistance, s.cascadeBorder, fadeScale, fadeBias);
            cmd.SetGlobalVector(Shader::PropertyToID("_MainLightShadowParams"), Vector4(s.strength, s.softQuality, fadeScale, fadeBias));
            f32 halfX = 0.5f / f32(width), halfY = 0.5f / f32(height);
            cmd.SetGlobalVector(Shader::PropertyToID("_MainLightShadowOffset0"), Vector4(-halfX, -halfY, halfX, -halfY));
            cmd.SetGlobalVector(Shader::PropertyToID("_MainLightShadowOffset1"), Vector4(-halfX, halfY, halfX, halfY));
            cmd.SetGlobalVector(Shader::PropertyToID("_MainLightShadowmapSize"), Vector4(1.0f / f32(width), 1.0f / f32(height), f32(width), f32(height)));
        }
        b.Dispose();
        return ok ? atlas : TextureHandle();
    }
}
