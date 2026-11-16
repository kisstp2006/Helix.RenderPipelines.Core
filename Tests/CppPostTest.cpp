#include <Support/CoreHost.h>

#include <Cpp/CppPost.h>

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
    bool Near(u32 a, u32 b)
    {
        for (int shift = 0; shift < 32; shift += 8)
        {
            i32 ca = i32((a >> shift) & 0xFF), cb = i32((b >> shift) & 0xFF);
            if (ca - cb > 3 || cb - ca > 3)
                return false;
        }
        return true;
    }

    constexpr u32 Rgba(u32 r, u32 g, u32 b, u32 a = 255) { return r | g << 8 | b << 16 | a << 24; }
}

HX_TEST(Cpp, PostProcessComponentsRunInTheCSharpPipeline)
{
    // The C# post test's scenario with the C++ effects and scene: the same pixels, the C++ types after the listed ones.
    HX_REQUIRE(StartScripts());
    constexpr u32 size = 16;
    scripting::TypeId sceneType = scripting::TypeOf<CppPostScene>();
    HX_REQUIRE(sceneType.IsValid());
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
        host->AddComponent(sceneType);
        Array<u32> pixels;
        pixels.Resize(size * size);
        auto at = [&](u32 x, u32 y) { return pixels[y * size + x]; };
        const u32 left[] = {Rgba(0, 0, 0), Rgba(255, 0, 0)}, right[] = {Rgba(128, 0, 0), Rgba(0, 0, 0)};
        for (u32 frame = 0; frame < 2; frame++)
        {
            RunScriptPhase(ScriptMessage::Update);
            UpdateWorldMatrices();
            Graphics::RenderFrame();
            HX_REQUIRE(device.ReadTexture(color, 0, pixels.Data(), pixels.Size() * 4));
            bool same = Near(at(4, 8), left[frame]) && Near(at(12, 8), right[frame]);
            if (!same)
                std::printf("    frame %u: %08x %08x\n", frame, at(4, 8), at(12, 8));
            HX_CHECK(same);
        }
        HX_CHECK(SameNotes({"post:CppMirrorPost,CppTintPost:4:3", "post::2:1"}));
        DestroyImmediate(host);
        GraphicsSettings::SetDefaultRenderPipeline(nullptr);
        DestroyImmediate(asset);
        Graphics::ReleaseResources();
        device.Destroy(color);
        device.Destroy(depth);
    });
    std::printf("    %u devices\n", devices);
}
