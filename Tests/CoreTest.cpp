#include <Support/CoreHost.h>
#include <Support/Golden.h>

#include <Helix/Engine/GameObject/GameObject.h>
#include <Helix/Engine/GameObject/Lifecycle.h>
#include <Helix/Engine/Rendering/Graphics.h>
#include <Helix/Engine/Script/ScriptRuntime.h>
#include <Helix/Testing/Test.h>

using namespace helix;
using namespace helix::engine;
using namespace helix::rpcore::testing;

namespace
{
    bool NearColor(u32 a, u32 b, i32 tolerance = 3)
    {
        for (int shift = 0; shift < 32; shift += 8)
        {
            i32 ca = i32((a >> shift) & 0xFF), cb = i32((b >> shift) & 0xFF);
            if (ca - cb > tolerance || cb - ca > tolerance)
                return false;
        }
        return true;
    }

    constexpr u32 Rgba(u32 r, u32 g, u32 b, u32 a = 255) { return r | g << 8 | b << 16 | a << 24; }
}

HX_TEST(RTHandles, ScaledHandlesFollowTheReferenceSize)
{
    HX_REQUIRE(StartScripts());
    ShaderFolder shaders;
    Notes().Clear();
    GameObject* host = GameObject::Create("RTHandleHost");
    host->AddComponent(CSharp("CsRTHandleTests").type);
    RunScriptPhase(ScriptMessage::Update);
    HX_CHECK(SameNotes({"alloc:32x16:16x32:10x10:True:False:Half_32x16_R8G8B8A8_UNorm_Tex2D",
                        "smaller:32x16:0.625,0.625,0.000,0.000:(20, 10)",
                        "larger:50x25:25x50:10x10:100x50:1.000,1.000,0.625,0.625:(100, 50)",
                        "reset:10x5:20x10:(100, 50)",
                        "wrapped:True:True:Camera:True:(4, 4)",
                        "released:True:True",
                        "disposed:True:True",
                        "names:Color_8x4_Mips_R16G16B16A16_SFloat:Texture_Lut_Tex2D8x4_RGBA32",
                        "materials:True:True"}));
    DestroyImmediate(host);
}

HX_TEST(Blitter, BlitsKeepTexturesUpright)
{
    HX_REQUIRE(StartScripts());
    constexpr u32 size = 16;
    u32 devices = ForEachDevice([&](rhi::Device& device) {
        ShaderFolder shaders;
        rhi::TextureHandle color =
            device.CreateTexture({.format = rhi::Format::RGBA8Unorm, .width = size, .height = size, .usage = rhi::TextureRenderTarget | rhi::TextureSampled});
        rhi::TextureHandle depth = device.CreateTexture({.format = rhi::Format::D32Float, .width = size, .height = size, .usage = rhi::TextureDepthStencil});
        Graphics::SetDevice(&device);
        Graphics::SetTarget({color, depth, size, size, rhi::Format::RGBA8Unorm, rhi::Format::D32Float});
        Notes().Clear();
        GameObject* host = GameObject::Create("BlitHost");
        host->AddComponent(CSharp("CsBlitTests").type);
        Array<u32> pixels;
        pixels.Resize(size * size);
        auto at = [&](u32 x, u32 y) { return pixels[y * size + x]; };
        const u32 red = Rgba(255, 0, 0), green = Rgba(0, 255, 0), blue = Rgba(0, 0, 255), white = Rgba(255, 255, 255), black = Rgba(0, 0, 0);
        // The screen's rows are top first: the texture's bottom row (red, green) is at the bottom.
        for (u32 mode = 0; mode < 4; mode++)
        {
            RunScriptPhase(ScriptMessage::Update);
            HX_REQUIRE(device.ReadTexture(color, 0, pixels.Data(), pixels.Size() * 4));
            bool upright = mode == 2 ? NearColor(at(10, 6), red) && NearColor(at(14, 6), green) && NearColor(at(10, 2), blue) && NearColor(at(14, 2), white) &&
                                           NearColor(at(4, 12), black) && NearColor(at(4, 4), black)
                                     : NearColor(at(4, 12), red) && NearColor(at(12, 12), green) && NearColor(at(4, 4), blue) && NearColor(at(12, 4), white);
            if (!upright)
                std::printf("    mode %u: %08x %08x %08x %08x\n", mode, at(4, 12), at(12, 12), at(4, 4), at(12, 4));
            HX_CHECK(upright);
        }
        HX_CHECK(SameNotes({}));
        DestroyImmediate(host);
        Graphics::ReleaseResources();
        device.Destroy(color);
        device.Destroy(depth);
    });
    std::printf("    %u devices\n", devices);
}

HX_TEST(RenderGraph, PassesCulledMergedAndTexturesAliased)
{
    HX_REQUIRE(StartScripts());
    constexpr u32 size = 16;
    u32 devices = ForEachDevice([&](rhi::Device& device) {
        ShaderFolder shaders;
        rhi::TextureHandle color =
            device.CreateTexture({.format = rhi::Format::RGBA8Unorm, .width = size, .height = size, .usage = rhi::TextureRenderTarget | rhi::TextureSampled});
        rhi::TextureHandle depth = device.CreateTexture({.format = rhi::Format::D32Float, .width = size, .height = size, .usage = rhi::TextureDepthStencil});
        Graphics::SetDevice(&device);
        Graphics::SetTarget({color, depth, size, size, rhi::Format::RGBA8Unorm, rhi::Format::D32Float});
        ScriptableObject* asset = ScriptableObject::CreateInstanceByName("CsGraphPipelineAsset");
        HX_REQUIRE(asset != nullptr);
        GraphicsSettings::SetDefaultRenderPipeline(asset);
        Notes().Clear();
        GameObject* host = GameObject::Create("GraphHost");
        host->AddComponent(CSharp("CsGraphScene").type);
        Array<u32> pixels;
        pixels.Resize(size * size);
        auto at = [&](u32 x, u32 y) { return pixels[y * size + x]; };
        // Without native render passes, with them, without: the same image.
        for (u32 frame = 0; frame < 3; frame++)
        {
            RunScriptPhase(ScriptMessage::Update);
            UpdateWorldMatrices();
            Graphics::RenderFrame();
            HX_REQUIRE(device.ReadTexture(color, 0, pixels.Data(), pixels.Size() * 4));
            bool same = NearColor(at(4, 8), Rgba(255, 0, 0)) && NearColor(at(12, 8), Rgba(0, 0, 255)) && NearColor(at(14, 2), Rgba(0, 255, 0)) &&
                        NearColor(at(2, 2), Rgba(0, 0, 0));
            if (!same)
                std::printf("    frame %u: %08x %08x %08x %08x\n", frame, at(4, 8), at(12, 8), at(14, 2), at(2, 2));
            HX_CHECK(same);
        }
        // Passes, culled, render passes, merged into one, textures, pool textures made, aliased, in the pool.
        HX_CHECK(SameNotes({"graph:False:7:1:4:1:4:3:1:3", "graph:True:7:1:4:1:4:0:1:3", "graph:False:7:1:4:1:4:0:1:3"}));
        DestroyImmediate(host);
        for (const char* name : {"GraphEye", "GraphShape"})
            if (GameObject* g = GameObject::Find(name))
                DestroyImmediate(g);
        GraphicsSettings::SetDefaultRenderPipeline(nullptr);
        DestroyImmediate(asset);
        Graphics::ReleaseResources();
        device.Destroy(color);
        device.Destroy(depth);
    });
    std::printf("    %u devices\n", devices);
}

HX_TEST(Volumes, ProfilesBlendByPriorityWeightDistanceAndLayer)
{
    HX_REQUIRE(StartScripts());
    Notes().Clear();
    GameObject* host = GameObject::Create("VolumeHost");
    host->AddComponent(CSharp("CsVolumeTests").type);
    RunScriptPhase(ScriptMessage::Update);
    // Intensity, tint's green, flag, steps (no interpolation), untouched, the default profile's count, overridden.
    HX_CHECK(SameNotes({"init:True:True:True",
                        "params:5:True:True:20",
                        "global:7.000:0.000:True:8.000:5.000:7:True",
                        "near:16.750:0.000:True:8.000:5.000:7:True",
                        "far:7.000:0.000:True:8.000:5.000:7:True",
                        "layers:4.000:0.000:True:2.000:5.000:7:True:3:1",
                        "weight:5.500:1.000:False:8.000:5.000:7:True",
                        "inactive:5.500:1.000:False:8.000:5.000:7:True",
                        "priority:4.000:0.000:True:8.000:5.000:7:True",
                        "active:True:False",
                        "instance:False:True:4.000:0.000:0.000:True:8.000:5.000:7:True",
                        "profile:True:True:False:1"}));
    DestroyImmediate(host);
    // The profiles and components the test made.
    engine::testing::Cleanup();
}

HX_TEST(PostProcessing, VolumeComponentsRunInOrderBetweenTwoTextures)
{
    HX_REQUIRE(StartScripts());
    constexpr u32 size = 16;
    u32 devices = ForEachDevice([&](rhi::Device& device) {
        ShaderFolder shaders;
        rhi::TextureHandle color =
            device.CreateTexture({.format = rhi::Format::RGBA8Unorm, .width = size, .height = size, .usage = rhi::TextureRenderTarget | rhi::TextureSampled});
        rhi::TextureHandle depth = device.CreateTexture({.format = rhi::Format::D32Float, .width = size, .height = size, .usage = rhi::TextureDepthStencil});
        Graphics::SetDevice(&device);
        Graphics::SetTarget({color, depth, size, size, rhi::Format::RGBA8Unorm, rhi::Format::D32Float});
        ScriptableObject* asset = ScriptableObject::CreateInstanceByName("CsPostPipelineAsset");
        HX_REQUIRE(asset != nullptr);
        GraphicsSettings::SetDefaultRenderPipeline(asset);
        Notes().Clear();
        GameObject* host = GameObject::Create("PostHost");
        host->AddComponent(CSharp("CsPostScene").type);
        Array<u32> pixels;
        pixels.Resize(size * size);
        auto at = [&](u32 x, u32 y) { return pixels[y * size + x]; };
        // Mirrored then tinted; then the volume weighs nothing.
        const u32 left[] = {Rgba(0, 0, 0), Rgba(255, 0, 0)}, right[] = {Rgba(128, 0, 0), Rgba(0, 0, 0)};
        for (u32 frame = 0; frame < 2; frame++)
        {
            RunScriptPhase(ScriptMessage::Update);
            UpdateWorldMatrices();
            Graphics::RenderFrame();
            HX_REQUIRE(device.ReadTexture(color, 0, pixels.Data(), pixels.Size() * 4));
            bool same = NearColor(at(4, 8), left[frame]) && NearColor(at(12, 8), right[frame]);
            if (!same)
                std::printf("    frame %u: %08x %08x\n", frame, at(4, 8), at(12, 8));
            HX_CHECK(same);
        }
        HX_CHECK(SameNotes({"post:CsMirrorPost,CsTintPost:4:3", "post::2:1"}));
        DestroyImmediate(host);
        GraphicsSettings::SetDefaultRenderPipeline(nullptr);
        DestroyImmediate(asset);
        Graphics::ReleaseResources();
        device.Destroy(color);
        device.Destroy(depth);
    });
    std::printf("    %u devices\n", devices);
}

HX_TEST(ShaderLibrary, EveryFunctionIsRightOnEveryDevice)
{
    HX_REQUIRE(StartScripts());
    constexpr u32 size = 16;
    u32 devices = ForEachDevice([&](rhi::Device& device) {
        ShaderFolder shaders;
        rhi::TextureHandle color =
            device.CreateTexture({.format = rhi::Format::RGBA8Unorm, .width = size, .height = size, .usage = rhi::TextureRenderTarget | rhi::TextureSampled});
        rhi::TextureHandle depth = device.CreateTexture({.format = rhi::Format::D32Float, .width = size, .height = size, .usage = rhi::TextureDepthStencil});
        Graphics::SetDevice(&device);
        Graphics::SetTarget({color, depth, size, size, rhi::Format::RGBA8Unorm, rhi::Format::D32Float});
        Notes().Clear();
        GameObject* host = GameObject::Create("LibraryHost");
        host->AddComponent(CSharp("CsLibraryScene").type);
        RunScriptPhase(ScriptMessage::Update);
        UpdateWorldMatrices();
        Graphics::RenderFrame();
        Array<u32> pixels;
        pixels.Resize(size * size);
        HX_REQUIRE(device.ReadTexture(color, 0, pixels.Data(), pixels.Size() * 4));
        // One draw on the left, two instances on the right.
        u32 one = pixels[8 * size + 4], top = pixels[4 * size + 12], bottom = pixels[12 * size + 12];
        bool white = NearColor(one, Rgba(255, 255, 255)) && NearColor(top, Rgba(255, 255, 255)) && NearColor(bottom, Rgba(255, 255, 255));
        if (!white)
            std::printf("    %08x %08x %08x\n", one, top, bottom);
        HX_CHECK(white);
        HX_CHECK(SameNotes({"library:True:1"}));
        DestroyImmediate(host);
        if (GameObject* g = GameObject::Find("LibraryEye"))
            DestroyImmediate(g);
        Graphics::ReleaseResources();
        device.Destroy(color);
        device.Destroy(depth);
    });
    std::printf("    %u devices\n", devices);
}

HX_TEST(DebugDisplay, PanelsOfWidgetsBoundToData)
{
    HX_REQUIRE(StartScripts());
    Notes().Clear();
    GameObject* host = GameObject::Create("DebugHost");
    host->AddComponent(CSharp("CsDebugTests").type);
    RunScriptPhase(ScriptMessage::Update);
    // Validated sets (clamped, the enum's first value for an unknown one), queries, the text view, reset.
    HX_CHECK(SameNotes({"values:8:1:0.000:1.000:0.000:2:0:1",
                        "query:Rendering -> Lighting -> Exposure:True:1:True:0",
                        "text:[Rendering]",
                        "text:  Draw Calls: 42",
                        "text:  Wireframe: False",
                        "text:  Samples: 8",
                        "text:  Lighting: open",
                        "text:    Exposure: 0.00",
                        "text:    Tint: (1.000, 0.500, 0.000, 1.000)",
                        "text:  Mode: Albedo",
                        "text:  Closed: closed",
                        "text:  Capture",
                        "reset:4:1.500:0:True:True",
                        "removed:True:0"}));
    DestroyImmediate(host);
}

HX_TEST(Pipeline, TheGoldenSceneThroughTheCoreWithAndWithoutPost)
{
    HX_REQUIRE(StartScripts());
    constexpr u32 size = 32;
    u32 devices = ForEachDevice([&](rhi::Device& device) {
        ShaderFolder shaders;
        rhi::TextureHandle color =
            device.CreateTexture({.format = rhi::Format::RGBA8Unorm, .width = size, .height = size, .usage = rhi::TextureRenderTarget | rhi::TextureSampled});
        rhi::TextureHandle depth = device.CreateTexture({.format = rhi::Format::D32Float, .width = size, .height = size, .usage = rhi::TextureDepthStencil});
        Graphics::SetDevice(&device);
        Graphics::SetTarget({color, depth, size, size, rhi::Format::RGBA8Unorm, rhi::Format::D32Float});
        Graphics::SetAmbientLight(Color(0.25f, 0.25f, 0.25f, 1));
        ScriptableObject* asset = ScriptableObject::CreateInstanceByName("CsCorePipelineAsset");
        HX_REQUIRE(asset != nullptr);
        GraphicsSettings::SetDefaultRenderPipeline(asset);
        Notes().Clear();
        GameObject* host = GameObject::Create("CoreHost");
        host->AddComponent(CSharp("CsCoreScene").type);
        Array<u32> pixels;
        pixels.Resize(size * size);
        // The engine's golden image without post; tinted by the volume; the same with native render passes.
        for (u32 frame = 0; frame < 3; frame++)
        {
            RunScriptPhase(ScriptMessage::Update);
            UpdateWorldMatrices();
            Graphics::RenderFrame();
            HX_REQUIRE(device.ReadTexture(color, 0, pixels.Data(), pixels.Size() * 4));
            HX_CHECK(engine::testing::MatchGolden(frame == 0 ? "ScriptApiScene" : "CorePostScene", pixels.Data(), size, size));
        }
        HX_CHECK(SameNotes({"core:False:3:0:2:1:2", "core:False:4:0:3:1:3", "core:True:4:0:3:1:3"}));
        DestroyImmediate(host);
        GraphicsSettings::SetDefaultRenderPipeline(nullptr);
        DestroyImmediate(asset);
        Graphics::ReleaseResources();
        device.Destroy(color);
        device.Destroy(depth);
    });
    std::printf("    %u devices\n", devices);
}
