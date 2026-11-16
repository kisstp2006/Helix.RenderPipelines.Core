#pragma once

#include <Helix/Engine/Rendering/Pipeline/CommandBuffer.h>
#include <Helix/Engine/Rendering/Pipeline/RenderStates.h>
#include <Helix/Engine/Rendering/Pipeline/Shadows.h>
#include <Helix/Math/Matrix4x4.h>
#include <Helix/RenderPipelines/Core/RenderGraph/RenderGraph.h>

// The main light's cascaded shadows, shared by the pipelines: a cascade's view, projection and atlas tile, the bias of
// its casters, the fade at the shadow distance, and the pass that draws the casters of all cascades into one atlas
// with the globals the shader library's CascadedShadows reads (_MainLightWorldToShadow, _CascadeShadowSplitSpheres0-3,
// _CascadeShadowSplitSphereRadii, _MainLightShadowParams, _MainLightShadowmapSize, _MainLightShadowOffset0-1 and the
// atlas _MainLightShadowmapTexture).
namespace helix::rendering
{
    // A shadow map tile of a light or cascade.
    struct ShadowSliceData
    {
        engine::ShadowSplitData splitData;
        i32 offsetX = 0, offsetY = 0;
        i32 resolution = 0;
        Matrix4x4 viewMatrix = Matrix4x4::identity;
        Matrix4x4 projectionMatrix = Matrix4x4::identity;
        // World to the atlas's texels (xy 0 to 1) and depth (0 to 1).
        Matrix4x4 shadowTransform = Matrix4x4::identity;

        void Clear() { *this = ShadowSliceData(); }
    };

    // What the main light's cascades are drawn with.
    struct CascadedShadowSettings
    {
        i32 lightIndex = -1;
        // 1 to 4; the ends of the first three cascades as shares of the shadow distance.
        i32 cascadeCount = 1;
        Vector3 splits = Vector3(1, 0, 0);
        i32 atlasWidth = 2048, atlasHeight = 2048;
        i32 depthBits = 16;
        // The depth and normal bias in texels of a tile; soft shadows (bias times the filter's radius).
        Vector2 bias = Vector2(1, 1);
        bool soft = false;
        // The filter the shaders take: 0 hard, 1 low, 2 medium, 3 high.
        f32 softQuality = 0;
        f32 strength = 1;
        f32 shadowDistance = 50;
        // The share of the last cascade over which the shadow fades.
        f32 cascadeBorder = 0.2f;
        f32 nearPlane = 0.2f;
    };

    struct CascadedShadows
    {
        // The cascade's view and projection, its sphere (cascadeSplitDistance) and its tile in the atlas (side by side,
        // then the next row); false when the light casts no shadow there.
        static bool ExtractDirectionalLightMatrix(const engine::CullingResults& cullResults, i32 shadowLightIndex, i32 cascadeIndex, i32 cascadeCount,
                                                  Vector3 splitRatio, i32 shadowmapWidth, i32 shadowmapHeight, i32 shadowResolution, f32 shadowNearPlane,
                                                  Vector4& cascadeSplitDistance, ShadowSliceData& shadowSliceData);
        // The tile's place in the atlas applied to its transform.
        static void ApplySliceTransform(ShadowSliceData& shadowSliceData, i32 atlasWidth, i32 atlasHeight);
        // The largest square tile of which the atlas holds tileCount.
        static i32 GetMaxTileResolutionInAtlas(i32 atlasWidth, i32 atlasHeight, i32 tileCount);
        // A light's depth bias (x) and normal bias (y) in world units: the bias in texels times the size of one texel of
        // the tile (2.5 times that for soft shadows); negative, as the caster passes apply them.
        static Vector4 GetShadowBias(const engine::VisibleLight& shadowLight, Vector2 bias, bool soft, const Matrix4x4& lightProjectionMatrix,
                                     f32 shadowResolution);
        // The fade of shadows over the last part of a distance (squared distances, as the shaders compare them):
        // fade = saturate(d^2 * scale + bias).
        static void GetScaleAndBiasForLinearDistanceFade(f32 fadeDistance, f32 border, f32& scale, f32& bias);
        // World to the atlas's texels for a light's projection and view (the GPU projection of a texture target).
        static Matrix4x4 GetShadowTransform(const Matrix4x4& projection, const Matrix4x4& view);
        // What the caster passes read while a light's casters draw: _ShadowBias, _LightDirection, _LightPosition.
        static void SetupShadowCasterConstantBuffer(engine::CommandBuffer& cmd, const engine::VisibleLight& shadowLight, const Vector4& shadowBias);

        // The pass that draws the light's casters into an atlas of its cascades (MainLightShadow), the casters culled
        // for them, and the globals set into `globals` (the frame's command buffer, before the graph runs); the atlas,
        // invalid when the light casts no shadow. The camera's view and projection are set again after the casters.
        static TextureHandle Record(RenderGraph& graph, engine::ScriptableRenderContext& context, engine::CullingResults culling,
                                    const CascadedShadowSettings& settings, const Matrix4x4& cameraView, const Matrix4x4& cameraProjection,
                                    engine::CommandBuffer& globals);
    };
}
