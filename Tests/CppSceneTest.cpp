#include <Support/CoreHost.h>
#include <Support/Golden.h>

#include <Cpp/CppScenes.h>

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

    bool SameCppNotes(std::initializer_list<const char*> expected)
    {
        const Array<String>& notes = CppNotes();
        bool same = notes.Size() == expected.size();
        usize i = 0;
        for (const char* e : expected)
        {
            same = same && i < notes.Size() && notes[i] == StringView(e);
            i++;
        }
        if (!same)
            for (const String& n : notes)
                std::printf("    note: %.*s\n", int(n.Size()), n.Data());
        return same;
    }

    scripting::TypeId TestType(const char* name)
    {
        scripting::TypeId t = scripting::FindType(name);
        if (!t.IsValid())
            std::printf("    no type %s\n", name);
        return t;
    }
}

HX_TEST(Cpp, BlitsKeepTexturesUpright)
{
    // The C# blit test's four modes from a C++ scene script: the same pixels.
    HX_REQUIRE(StartScripts());
    scripting::TypeId sceneType = TestType("CoreTests.CppBlitTests");
    HX_REQUIRE(sceneType.IsValid());
    constexpr u32 size = 16;
    u32 devices = ForEachDevice([&](rhi::Device& device) {
        ShaderFolder shaders;
        rhi::TextureHandle color =
            device.CreateTexture({.format = rhi::Format::RGBA8Unorm, .width = size, .height = size, .usage = rhi::TextureRenderTarget | rhi::TextureSampled});
        rhi::TextureHandle depth = device.CreateTexture({.format = rhi::Format::D32Float, .width = size, .height = size, .usage = rhi::TextureDepthStencil});
        Graphics::SetDevice(&device);
        Graphics::SetTarget({color, depth, size, size, rhi::Format::RGBA8Unorm, rhi::Format::D32Float});
        GameObject* host = GameObject::Create("BlitHost");
        host->AddComponent(sceneType);
        Array<u32> pixels;
        pixels.Resize(size * size);
        auto at = [&](u32 x, u32 y) { return pixels[y * size + x]; };
        const u32 red = Rgba(255, 0, 0), green = Rgba(0, 255, 0), blue = Rgba(0, 0, 255), white = Rgba(255, 255, 255), black = Rgba(0, 0, 0);
        for (u32 mode = 0; mode < 4; mode++)
        {
            RunScriptPhase(ScriptMessage::Update);
            HX_REQUIRE(device.ReadTexture(color, 0, pixels.Data(), pixels.Size() * 4));
            bool upright = mode == 2 ? Near(at(10, 6), red) && Near(at(14, 6), green) && Near(at(10, 2), blue) && Near(at(14, 2), white) &&
                                           Near(at(4, 12), black) && Near(at(4, 4), black)
                                     : Near(at(4, 12), red) && Near(at(12, 12), green) && Near(at(4, 4), blue) && Near(at(12, 4), white);
            if (!upright)
                std::printf("    mode %u: %08x %08x %08x %08x\n", mode, at(4, 12), at(12, 12), at(4, 4), at(12, 4));
            HX_CHECK(upright);
        }
        DestroyImmediate(host);
        Graphics::ReleaseResources();
        device.Destroy(color);
        device.Destroy(depth);
    });
    std::printf("    %u devices\n", devices);
}

HX_TEST(Cpp, ShaderLibraryOnEveryDevice)
{
    // The C# shader library scene in C++: the same pixels.
    HX_REQUIRE(StartScripts());
    scripting::TypeId sceneType = TestType("CoreTests.CppLibraryScene");
    HX_REQUIRE(sceneType.IsValid());
    constexpr u32 size = 16;
    u32 devices = ForEachDevice([&](rhi::Device& device) {
        ShaderFolder shaders;
        rhi::TextureHandle color =
            device.CreateTexture({.format = rhi::Format::RGBA8Unorm, .width = size, .height = size, .usage = rhi::TextureRenderTarget | rhi::TextureSampled});
        rhi::TextureHandle depth = device.CreateTexture({.format = rhi::Format::D32Float, .width = size, .height = size, .usage = rhi::TextureDepthStencil});
        Graphics::SetDevice(&device);
        Graphics::SetTarget({color, depth, size, size, rhi::Format::RGBA8Unorm, rhi::Format::D32Float});
        CppNotes().Clear();
        GameObject* host = GameObject::Create("LibraryHost");
        host->AddComponent(sceneType);
        RunScriptPhase(ScriptMessage::Update);
        UpdateWorldMatrices();
        Graphics::RenderFrame();
        Array<u32> pixels;
        pixels.Resize(size * size);
        HX_REQUIRE(device.ReadTexture(color, 0, pixels.Data(), pixels.Size() * 4));
        u32 one = pixels[8 * size + 4], top = pixels[4 * size + 12], bottom = pixels[12 * size + 12];
        bool white = Near(one, Rgba(255, 255, 255)) && Near(top, Rgba(255, 255, 255)) && Near(bottom, Rgba(255, 255, 255));
        if (!white)
            std::printf("    %08x %08x %08x\n", one, top, bottom);
        HX_CHECK(white);
        HX_CHECK(SameCppNotes({"library:True:1"}));
        DestroyImmediate(host);
        if (GameObject* g = GameObject::Find("LibraryEye"))
            DestroyImmediate(g);
        Graphics::ReleaseResources();
        device.Destroy(color);
        device.Destroy(depth);
    });
    std::printf("    %u devices\n", devices);
}

HX_TEST(Cpp, PipelineDrawsTheGoldenSceneOnTheGraph)
{
    // A C++ pipeline on the C++ render graph with the C++ post passes: the C# pipeline's golden images and notes.
    HX_REQUIRE(StartScripts());
    scripting::TypeId assetType = TestType("CoreTests.CppCorePipelineAsset"), sceneType = TestType("CoreTests.CppCoreScene");
    HX_REQUIRE(assetType.IsValid() && sceneType.IsValid());
    constexpr u32 size = 32;
    u32 devices = ForEachDevice([&](rhi::Device& device) {
        ShaderFolder shaders;
        rhi::TextureHandle color =
            device.CreateTexture({.format = rhi::Format::RGBA8Unorm, .width = size, .height = size, .usage = rhi::TextureRenderTarget | rhi::TextureSampled});
        rhi::TextureHandle depth = device.CreateTexture({.format = rhi::Format::D32Float, .width = size, .height = size, .usage = rhi::TextureDepthStencil});
        Graphics::SetDevice(&device);
        Graphics::SetTarget({color, depth, size, size, rhi::Format::RGBA8Unorm, rhi::Format::D32Float});
        Graphics::SetAmbientLight(Color(0.25f, 0.25f, 0.25f, 1));
        ScriptableObject* asset = ScriptableObject::CreateInstance(assetType);
        HX_REQUIRE(asset != nullptr);
        GraphicsSettings::SetDefaultRenderPipeline(asset);
        CppNotes().Clear();
        GameObject* host = GameObject::Create("CoreHost");
        host->AddComponent(sceneType);
        Array<u32> pixels;
        pixels.Resize(size * size);
        for (u32 frame = 0; frame < 3; frame++)
        {
            RunScriptPhase(ScriptMessage::Update);
            UpdateWorldMatrices();
            Graphics::RenderFrame();
            HX_REQUIRE(device.ReadTexture(color, 0, pixels.Data(), pixels.Size() * 4));
            HX_CHECK(engine::testing::MatchGolden(frame == 0 ? "ScriptApiScene" : "CorePostScene", pixels.Data(), size, size));
        }
        HX_CHECK(SameCppNotes({"core:False:3:0:2:1:2", "core:False:4:0:3:1:3", "core:True:4:0:3:1:3"}));
        DestroyImmediate(host);
        GraphicsSettings::SetDefaultRenderPipeline(nullptr);
        DestroyImmediate(asset);
        Graphics::ReleaseResources();
        device.Destroy(color);
        device.Destroy(depth);
    });
    std::printf("    %u devices\n", devices);
}
