#pragma once

#include <Helix/Core/Strings/Format.h>
#include <Helix/Engine/Asset/Asset.h>
#include <Helix/Engine/Script/ScriptClass.h>
#include <Helix/Engine/Script/ScriptRuntime.h>
#include <Helix/Engine/Shader/Shader.h>
#include <Helix/Platform/IO/FileSystem.h>
#include <Helix/Scripting/Bindings/Interop.h>
#include <Helix/Scripting/Host/DotnetHost.h>
#include <Helix/Scripting/Types/Type.h>
#include <Helix/Engine/Rendering/Graphics.h>
#include <Helix/Platform/Window/Window.h>
#include <Helix/Engine/Rendering/Pipeline/RenderPipeline.h>
#include <Helix/RHI/Device/Device.h>

#include <Support/TestClasses.h>

#include <CoreShaderLibraryAssembly.h>
#include <CoreShadersAssembly.h>
#include <CoreTestShadersAssembly.h>
#include <TestShadersAssembly.h>

#include <bit>
#include <cstdio>
#include <cstdlib>

// The publish function of the core's module (in its library on the desktop, linked in on the Web).
extern "C" void HxBindings_HelixRenderPipelinesCore_Publish();
// The test program's own game module (its C++ volume components).
HX_BINDINGS_EXPORT void HxBindings_RenderPipelinesCoreTests_Publish();

// The engine's C# side with the C# tests of the core (and the core's runtime they reference), started once per process;
// the notes the tests write; the shaders the tests draw with as assets.
namespace helix::rpcore::testing
{
    inline Array<String>& Notes()
    {
        static Array<String> notes;
        return notes;
    }

    inline void NoteFromCSharp(const char* text, i32 size) { Notes().Push(String(StringView(text, usize(size)))); }

    inline bool StartScripts()
    {
        static int state = 0;
        if (state != 0)
            return state > 0;
        state = -1;
        using namespace scripting;
        RegisterNativeExport("CoreTests.Note", std::bit_cast<const void*>(&NoteFromCSharp));
        String error;
        // The core's native module (linked into the test program), before the C# that binds to it.
        HxBindings_HelixRenderPipelinesCore_Publish();
        if (!engine::RegisterLinkedGameModule("HelixRenderPipelinesCore", error))
        {
            std::printf("    %.*s\n", int(error.Size()), error.Data());
            return false;
        }
        HxBindings_RenderPipelinesCoreTests_Publish();
        if (!engine::RegisterLinkedGameModule("RenderPipelinesCoreTests", error))
        {
            std::printf("    %.*s\n", int(error.Size()), error.Data());
            return false;
        }
        DotnetHostDesc desc;
        desc.runtimeAssembly = HX_TEST_RUNTIME_ASSEMBLY;
        if (!DotnetHost::Start(desc, error))
        {
            std::printf("    %.*s\n", int(error.Size()), error.Data());
            return false;
        }
        (void)FindType("HelixEngine.Object");
        AssemblyHandle core = DotnetHost::LoadAssembly(HX_TEST_CORE_MODULE, error);
        auto start = std::bit_cast<i32 (*)()>(DotnetHost::GetFunction(core, "HelixEngine.Internal.ScriptBridge", "Start", error));
        auto load = std::bit_cast<i32 (*)(const char*, i32)>(DotnetHost::GetFunction(core, "HelixEngine.Internal.ScriptBridge", "LoadScripts", error));
        if (!start || !load || start() != 0)
        {
            std::printf("    the engine's C# side did not start: %.*s\n", int(error.Size()), error.Data());
            return false;
        }
        // The core's runtime (its volume components and profiles are script classes), then the tests.
        StringView runtime = HX_TEST_CORE_RUNTIME;
        // Its classes are the native core's (generated), so it registers none of its own.
        if (load(runtime.Data(), i32(runtime.Size())) < 0)
        {
            std::printf("    the core's runtime did not load\n");
            return false;
        }
        StringView scripts = HX_TEST_SCRIPTS;
        if (load(scripts.Data(), i32(scripts.Size())) <= 0)
        {
            std::printf("    no C# test scripts were registered\n");
            return false;
        }
        state = 1;
        return true;
    }

    inline const engine::ScriptClass& CSharp(const char* name)
    {
        const engine::ScriptClass* s = engine::FindScriptClass("Helix.RenderPipelines.Core.Tests", "Tests", name);
        HX_ASSERT(s != nullptr);
        return *s;
    }

    // Runs f on every device: each D3D11 adapter, OpenGL, WebGL 2 where there is a window; the number of devices.
    template <class F>
    u32 ForEachDevice(F&& f)
    {
        u32 count = 0;
        for (rhi::Backend b : {rhi::Backend::D3D11, rhi::Backend::OpenGL, rhi::Backend::WebGL2})
        {
            if ((b == rhi::Backend::WebGL2 && !platform::Window::IsSupported()) || !rhi::Device::IsBackendAvailable(b))
                continue;
            char name[128];
            u32 adapters = 0;
            while (b == rhi::Backend::D3D11 && rhi::Device::AdapterName(b, adapters, name, sizeof(name)))
                adapters++;
            for (i32 adapter = adapters ? 0 : -1; adapter < i32(adapters); adapter++)
            {
                // HELIX_RENDER_BATCHING=1: the persistent material and object blocks; HELIX_RENDER_THREAD=1: a render thread owns the backend.
                static const bool batching = std::getenv("HELIX_RENDER_BATCHING") != nullptr;
                static const bool threaded = std::getenv("HELIX_RENDER_THREAD") != nullptr;
                engine::GraphicsSettings::SetUseScriptableRenderPipelineBatching(batching);
                rhi::Device* device = rhi::Device::Create({.backend = b, .adapter = adapter, .vsync = false, .threaded = threaded});
                if (!device)
                    continue;
                name[0] = 0;
                if (adapter >= 0)
                    rhi::Device::AdapterName(b, u32(adapter), name, sizeof(name));
                std::printf("    backend %d %s\n", int(b), name);
                f(*device);
                engine::Graphics::SetDevice(nullptr);
                rhi::Device::Destroy(device);
                count++;
            }
        }
        return count;
    }

    // The notes so far against the expected lines (all printed when they differ); the notes are cleared.
    inline bool SameNotes(std::initializer_list<const char*> expected)
    {
        Array<String> lines = Move(Notes());
        Notes() = Array<String>();
        bool same = lines.Size() == expected.size();
        usize i = 0;
        for (const char* e : expected)
            same = same && i < lines.Size() && lines[i++] == StringView(e);
        if (!same)
            for (const String& l : lines)
                std::printf("    %s\n", l.CStr());
        return same;
    }

    // The engine's test shaders and the core's shaders (with their shader library) as assets of a temporary folder.
    struct ShaderFolder
    {
        String root;
        engine::FolderAssetSource* source = nullptr;

        ShaderFolder()
        {
            root = platform::JoinPath(platform::TempDirectory(), Format("helix-core-shaders-{}", Guid::Generate().ToString()));
            platform::CreateDirectories(root);
            Write("HelixEngine.TestShaders.dll", Span<const u8>(kTestShadersAssembly, sizeof(kTestShadersAssembly)));
            Write("Helix.RenderPipelines.Core.ShaderLibrary.dll", Span<const u8>(kCoreShaderLibraryAssembly, sizeof(kCoreShaderLibraryAssembly)));
            Write("Helix.RenderPipelines.Core.Shaders.dll", Span<const u8>(kCoreShadersAssembly, sizeof(kCoreShadersAssembly)));
            Write("Helix.RenderPipelines.Core.TestShaders.dll", Span<const u8>(kCoreTestShadersAssembly, sizeof(kCoreTestShadersAssembly)));
            source = new engine::FolderAssetSource(root);
            engine::AddAssetSource(source);
            for (const char* name : {"HelixEngine.TestShaders.dll", "Helix.RenderPipelines.Core.ShaderLibrary.dll", "Helix.RenderPipelines.Core.Shaders.dll",
                                     "Helix.RenderPipelines.Core.TestShaders.dll"})
                if (const engine::AssetEntry* e = engine::FindAssetAtPath(name))
                    engine::LoadAsset(e->guid);
        }

        ~ShaderFolder()
        {
            engine::ResetAssets();
            engine::testing::Cleanup();
            engine::Shader::Globals().Clear();
            delete source;
            for (const char* name : {"HelixEngine.TestShaders.dll", "Helix.RenderPipelines.Core.ShaderLibrary.dll", "Helix.RenderPipelines.Core.Shaders.dll",
                                     "Helix.RenderPipelines.Core.TestShaders.dll"})
                platform::RemoveFile(platform::JoinPath(root, name));
            platform::RemoveEmptyDirectory(root);
        }

        void Write(const char* name, Span<const u8> bytes) { platform::WriteFile(platform::JoinPath(root, name), bytes); }
    };
}
