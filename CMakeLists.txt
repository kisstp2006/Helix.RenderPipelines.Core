cmake_minimum_required(VERSION 3.28)
project(Helix.RenderPipelines.Core LANGUAGES CXX)

if(NOT COMMAND helix_add_library)
    include("${CMAKE_CURRENT_SOURCE_DIR}/External/Helix.CMake/Helix.cmake")
endif()

helix_require(ScriptCore)
include("${HELIX_BINDGEN_ROOT}/CMake/HelixBindgen.cmake")

find_program(HELIX_DOTNET NAMES dotnet HINTS "$ENV{HELIX_DOTNET_ROOT}" "$ENV{DOTNET_ROOT}" REQUIRED)
set(core_artifacts "${CMAKE_BINARY_DIR}/HelixRenderPipelinesCore")
helix_dependency_dir(shaders_dir Shaders)
helix_dependency_dir(engine_dir Engine)
helix_dependency_dir(math_dir Math)
set(engine_source "${engine_dir}/Source")
set(core_source "${CMAKE_CURRENT_SOURCE_DIR}/Source")

# The core in C++: a native module (HelixRenderPipelinesCore) over the engine, a shared library beside the engine's (a
# static one linked into the app on the Web). Pipelines of C++ link it; C# reaches it through its bindings.
file(GLOB_RECURSE core_cpp CONFIGURE_DEPENDS "${core_source}/Helix/RenderPipelines/Core/*.cpp")
file(GLOB_RECURSE core_headers CONFIGURE_DEPENDS "${core_source}/Helix/RenderPipelines/Core/*.h")
if(HELIX_PLATFORM STREQUAL "web")
    add_library(helix_render_pipelines_core STATIC ${core_cpp} ${core_headers})
    target_link_libraries(helix_render_pipelines_core PUBLIC Helix::Engine)
else()
    add_library(helix_render_pipelines_core SHARED ${core_cpp} ${core_headers})
    target_link_libraries(helix_render_pipelines_core PUBLIC Helix::EngineShared)
    set_target_properties(helix_render_pipelines_core PROPERTIES WINDOWS_EXPORT_ALL_SYMBOLS ON RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}"
                                                                 LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}")
endif()
add_library(Helix::RenderPipelines.Core ALIAS helix_render_pipelines_core)
set_target_properties(helix_render_pipelines_core PROPERTIES OUTPUT_NAME HelixRenderPipelinesCore)
target_include_directories(helix_render_pipelines_core PUBLIC "${core_source}")
helix_apply_options(helix_render_pipelines_core)
# The headers whose tagged types C# sees.
set(core_bound_headers "${core_source}/Helix/RenderPipelines/Core/Textures/RTHandle.h" "${core_source}/Helix/RenderPipelines/Core/Utilities/CoreUtils.h"
    "${core_source}/Helix/RenderPipelines/Core/Native/TexturesNative.h" "${core_source}/Helix/RenderPipelines/Core/RenderGraph/RenderGraph.h"
    "${core_source}/Helix/RenderPipelines/Core/Native/GraphNative.h" "${core_source}/Helix/RenderPipelines/Core/Volume/VolumeParameter.h"
    "${core_source}/Helix/RenderPipelines/Core/Volume/VolumeComponent.h" "${core_source}/Helix/RenderPipelines/Core/Volume/Volume.h"
    "${core_source}/Helix/RenderPipelines/Core/Native/VolumesNative.h" "${core_source}/Helix/RenderPipelines/Core/PostProcessing/PostProcessing.h"
    "${core_source}/Helix/RenderPipelines/Core/Native/PostNative.h" "${core_source}/Helix/RenderPipelines/Core/Debugging/DebugUI.h"
    "${core_source}/Helix/RenderPipelines/Core/Native/DebugNative.h")
set(engine_references "${engine_source}/Helix/Engine/Object/EntityId.h" "${engine_source}/Helix/Engine/Object/Object.h"
    "${engine_source}/Helix/Engine/GameObject/GameObject.h" "${engine_source}/Helix/Engine/Script/GameBehaviour.h"
    "${engine_source}/Helix/Engine/Script/ScriptableObject.h" "${engine_source}/Helix/Engine/Texture/Texture.h" "${engine_source}/Helix/Engine/Transform/Transform.h"
    "${engine_source}/Helix/Engine/Texture/RenderTexture.h" "${engine_source}/Helix/Engine/Material/Material.h" "${engine_source}/Helix/Engine/Shader/Shader.h"
    "${engine_source}/Helix/Engine/Rendering/Pipeline/RenderTargets.h" "${math_dir}/Source/Helix/Math/Vector.h" "${math_dir}/Source/Helix/Math/Color.h"
    "${math_dir}/Source/Helix/Math/Geometry.h")
helix_bindgen(helix_render_pipelines_core MODULE HelixRenderPipelinesCore NAMESPACE HelixEngine.Rendering KIND NativeGame HEADERS ${core_bound_headers}
              REFERENCES ${engine_references} REFERENCE_NAMESPACE HelixEngine INCLUDE_ROOTS "${core_source}" "${engine_source}" "${math_dir}/Source"
              CS_OUTPUT core_cs)

# The C# side: the generated bindings and the hand written C# in the reference's shape, over HelixEngine.CoreModule.
set(runtime_dir "${core_source}/Helix.RenderPipelines.Core.Runtime")
set(runtime_dll "${core_artifacts}/bin/Helix.RenderPipelines.Core.Runtime/release/Helix.RenderPipelines.Core.Runtime.dll")
file(GLOB_RECURSE runtime_sources CONFIGURE_DEPENDS "${runtime_dir}/*.cs" "${runtime_dir}/*.csproj")
list(FILTER runtime_sources EXCLUDE REGEX "/(bin|obj)/")
add_custom_command(
    OUTPUT "${runtime_dll}"
    COMMAND "${HELIX_DOTNET}" build "${runtime_dir}/Helix.RenderPipelines.Core.Runtime.csproj" -c Release --artifacts-path "${core_artifacts}" --nologo
            -v quiet "-p:HelixCoreModule=${HELIX_CORE_MODULE}" "-p:HelixScriptingRuntime=${HELIX_SCRIPTING_RUNTIME}" "-p:HelixModuleBindings=${core_cs}"
    DEPENDS ${runtime_sources} "${HELIX_CORE_MODULE}" "${core_cs}"
    COMMENT "Building Helix.RenderPipelines.Core.Runtime")
add_custom_target(helix_render_pipelines_core_runtime ALL DEPENDS "${runtime_dll}")
add_dependencies(helix_render_pipelines_core_runtime helix_core_module helix_render_pipelines_core)

set(core_shaders_bin "${core_artifacts}/bin/Helix.RenderPipelines.Core.Shaders/release")
set(core_shaders_dll "${core_shaders_bin}/Helix.RenderPipelines.Core.Shaders.dll")
set(core_library_dll "${core_shaders_bin}/Helix.RenderPipelines.Core.ShaderLibrary.dll")
file(GLOB_RECURSE core_shader_sources CONFIGURE_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/Shaders/*.cs" "${CMAKE_CURRENT_SOURCE_DIR}/Shaders/*.csproj"
     "${CMAKE_CURRENT_SOURCE_DIR}/ShaderLibrary/*.cs" "${CMAKE_CURRENT_SOURCE_DIR}/ShaderLibrary/*.csproj" "${shaders_dir}/Source/*.cs"
     "${engine_dir}/Shaders/*.cs")
list(FILTER core_shader_sources EXCLUDE REGEX "/(bin|obj)/")
add_custom_command(
    OUTPUT "${core_shaders_dll}" "${core_library_dll}"
    COMMAND "${HELIX_DOTNET}" build "${CMAKE_CURRENT_SOURCE_DIR}/Shaders/Helix.RenderPipelines.Core.Shaders.csproj" -c Release
            --artifacts-path "${core_artifacts}" --nologo -v quiet "-p:HelixShadersDir=${shaders_dir}" "-p:HelixEngineDir=${engine_dir}"
    DEPENDS ${core_shader_sources}
    COMMENT "Building the core's C# shaders and shader library")
add_custom_target(helix_render_pipelines_core_shaders ALL DEPENDS "${core_shaders_dll}" "${core_library_dll}")

# Linking Helix::RenderPipelines.Core builds the shaders too.
add_dependencies(helix_render_pipelines_core helix_render_pipelines_core_shaders)
set(HELIX_RENDER_PIPELINES_CORE "${runtime_dll}" CACHE INTERNAL "")
set(HELIX_RENDER_PIPELINES_CORE_SHADERS "${core_shaders_dll}" CACHE INTERNAL "")
set(HELIX_RENDER_PIPELINES_CORE_SHADER_LIBRARY "${core_library_dll}" CACHE INTERNAL "")

# Tests: C# test scripts (the runtime under test with them) run by a C++ program in the engine, with the engine's
# test shaders and the core's shaders as assets: the shared engine on the desktop, a .NET web app in node on the Web.
if(HELIX_PLATFORM STREQUAL "windows" OR HELIX_PLATFORM STREQUAL "linux")
    helix_add_test_suite(helix_render_pipelines_core_tests EXCLUDE "/(Managed|Shaders)/" DEPS Helix::EngineShared Helix::RenderPipelines.Core)
elseif(HELIX_PLATFORM STREQUAL "web")
    helix_add_test_suite(helix_render_pipelines_core_tests DOTNET EXCLUDE "/(Managed|Shaders)/" DEPS Helix::Engine Helix::RenderPipelines.Core)
endif()
if(TARGET helix_render_pipelines_core_tests)
    set(tests_dir "${CMAKE_CURRENT_SOURCE_DIR}/Tests")
    # The test program's own game module: C++ volume and post process components below the core's, the C# tests'
    # scenes and pipeline in C++; their C# classes are compiled into the C# tests.
    helix_bindgen(helix_render_pipelines_core_tests MODULE RenderPipelinesCoreTests NAMESPACE CoreTests KIND NativeGame
                  HEADERS "${tests_dir}/Cpp/CppVolumes.h" "${tests_dir}/Cpp/CppPost.h" "${tests_dir}/Cpp/CppScenes.h" REFERENCES ${engine_references} ${core_bound_headers}
                  REFERENCE_NAMESPACE HelixEngine INCLUDE_ROOTS "${tests_dir}" "${core_source}" "${engine_source}" "${math_dir}/Source"
                  CS_OUTPUT test_bindings_cs)
    set(tests_dll "${core_artifacts}/bin/Helix.RenderPipelines.Core.Tests/release/Helix.RenderPipelines.Core.Tests.dll")
    get_filename_component(tests_bin "${tests_dll}" DIRECTORY)
    file(GLOB_RECURSE test_sources CONFIGURE_DEPENDS "${tests_dir}/Managed/*.cs" "${tests_dir}/Managed/*.csproj")
    list(FILTER test_sources EXCLUDE REGEX "/(bin|obj)/")
    add_custom_command(
        OUTPUT "${tests_dll}"
        COMMAND "${HELIX_DOTNET}" build "${tests_dir}/Managed/Helix.RenderPipelines.Core.Tests.csproj" -c Release --artifacts-path "${core_artifacts}"
                --nologo -v quiet "-p:HelixCoreModule=${HELIX_CORE_MODULE}" "-p:HelixRenderPipelinesCore=${runtime_dll}"
                "-p:HelixScriptingRuntime=${HELIX_SCRIPTING_RUNTIME}" "-p:HelixModuleBindings=${test_bindings_cs}"
        DEPENDS ${test_sources} "${runtime_dll}" "${test_bindings_cs}"
        COMMENT "Building the C# tests of Helix.RenderPipelines.Core")
    add_custom_target(helix_render_pipelines_core_test_scripts DEPENDS "${tests_dll}")
    add_dependencies(helix_render_pipelines_core_test_scripts helix_render_pipelines_core_runtime)
    add_dependencies(helix_render_pipelines_core_tests helix_render_pipelines_core_test_scripts helix_render_pipelines_core_shaders)

    # The engine's C# test shaders built against this repo's Helix.Shaders, and the core's shaders: embedded, written
    # into an asset folder by the tests.
    set(test_shaders_dir "${core_artifacts}/bin/HelixEngine.TestShaders/release")
    file(GLOB_RECURSE test_shader_sources CONFIGURE_DEPENDS "${engine_dir}/Tests/Shaders/*.cs" "${engine_dir}/Tests/Shaders/*.csproj"
         "${engine_dir}/Shaders/*.cs" "${engine_dir}/Shaders/*.csproj")
    list(FILTER test_shader_sources EXCLUDE REGEX "/(bin|obj)/")
    add_custom_command(
        OUTPUT "${test_shaders_dir}/HelixEngine.TestShaders.dll" "${test_shaders_dir}/HelixEngine.TestShaders.pdb"
        COMMAND "${HELIX_DOTNET}" build "${engine_dir}/Tests/Shaders/HelixEngine.TestShaders.csproj" -c Release --artifacts-path "${core_artifacts}"
                --nologo -v quiet "-p:HelixShadersDir=${shaders_dir}"
        DEPENDS ${test_shader_sources}
        COMMENT "Building the engine's C# test shaders")
    # The core's test shaders (a post effect, the shader library called whole).
    set(core_test_shaders_dll "${core_artifacts}/bin/Helix.RenderPipelines.Core.TestShaders/release/Helix.RenderPipelines.Core.TestShaders.dll")
    file(GLOB_RECURSE core_test_shader_sources CONFIGURE_DEPENDS "${tests_dir}/Shaders/*.cs" "${tests_dir}/Shaders/*.csproj")
    list(FILTER core_test_shader_sources EXCLUDE REGEX "/(bin|obj)/")
    add_custom_command(
        OUTPUT "${core_test_shaders_dll}"
        COMMAND "${HELIX_DOTNET}" build "${tests_dir}/Shaders/Helix.RenderPipelines.Core.TestShaders.csproj" -c Release --artifacts-path "${core_artifacts}"
                --nologo -v quiet "-p:HelixShadersDir=${shaders_dir}" "-p:HelixEngineDir=${engine_dir}"
        DEPENDS ${core_test_shader_sources} ${core_shader_sources}
        COMMENT "Building the core's C# test shaders")
    set(tests_generated "${CMAKE_CURRENT_BINARY_DIR}/Generated")
    set(embedded "${tests_generated}/TestShadersAssembly.h" "${tests_generated}/CoreShadersAssembly.h" "${tests_generated}/CoreShaderLibraryAssembly.h"
        "${tests_generated}/CoreTestShadersAssembly.h")
    add_custom_command(
        OUTPUT ${embedded}
        COMMAND "${CMAKE_COMMAND}" -E make_directory "${tests_generated}"
        COMMAND "${CMAKE_COMMAND}" "-DINPUT=${test_shaders_dir}/HelixEngine.TestShaders.dll" "-DOUTPUT=${tests_generated}/TestShadersAssembly.h"
                -DNAME=kTestShadersAssembly -P "${HELIX_EMBED_FILE}"
        COMMAND "${CMAKE_COMMAND}" "-DINPUT=${core_shaders_dll}" "-DOUTPUT=${tests_generated}/CoreShadersAssembly.h" -DNAME=kCoreShadersAssembly
                -P "${HELIX_EMBED_FILE}"
        COMMAND "${CMAKE_COMMAND}" "-DINPUT=${core_library_dll}" "-DOUTPUT=${tests_generated}/CoreShaderLibraryAssembly.h" -DNAME=kCoreShaderLibraryAssembly
                -P "${HELIX_EMBED_FILE}"
        COMMAND "${CMAKE_COMMAND}" "-DINPUT=${core_test_shaders_dll}" "-DOUTPUT=${tests_generated}/CoreTestShadersAssembly.h" -DNAME=kCoreTestShadersAssembly
                -P "${HELIX_EMBED_FILE}"
        DEPENDS "${test_shaders_dir}/HelixEngine.TestShaders.dll" "${core_shaders_dll}" "${core_library_dll}" "${core_test_shaders_dll}" "${HELIX_EMBED_FILE}"
        COMMENT "Embedding the test shaders and the core's shaders")
    target_sources(helix_render_pipelines_core_tests PRIVATE ${embedded})
    target_include_directories(helix_render_pipelines_core_tests PRIVATE "${tests_dir}" "${tests_generated}" "${engine_dir}/Tests")
    target_compile_definitions(helix_render_pipelines_core_tests PRIVATE "HX_TEST_RUNTIME_ASSEMBLY=\"${HELIX_SCRIPTING_RUNTIME}\""
                                                                        "HX_TEST_CORE_MODULE=\"${HELIX_CORE_MODULE}\"" "HX_TEST_SCRIPTS=\"${tests_dll}\""
                                                                        "HX_TEST_CORE_RUNTIME=\"${tests_bin}/Helix.RenderPipelines.Core.Runtime.dll\""
                                                                        "HELIX_ENGINE_GOLDEN_DIR=\"${tests_dir}/Golden\"")
    # The web app's assemblies.
    set_property(TARGET helix_render_pipelines_core_tests APPEND PROPERTY HELIX_DOTNET_ASSEMBLIES "${HELIX_CORE_MODULE}" ${HELIX_CORE_MODULE_REFERENCES}
                                                                                                  "${runtime_dll}" "${tests_dll}")
    if(TARGET helix_render_pipelines_core_tests_web)
        add_dependencies(helix_render_pipelines_core_tests_web helix_render_pipelines_core_test_scripts helix_core_module)
    endif()
    # Once more on the coreclr starter (as Android hosts the runtime).
    if(HELIX_PLATFORM STREQUAL "windows" OR HELIX_PLATFORM STREQUAL "linux")
        add_test(NAME helix_render_pipelines_core_tests_coreclr COMMAND helix_render_pipelines_core_tests)
        set_tests_properties(helix_render_pipelines_core_tests_coreclr PROPERTIES ENVIRONMENT "HELIX_DOTNET_RUNTIME=coreclr")
        # The render thread and persistent blocks on.
        add_test(NAME helix_render_pipelines_core_tests_render_thread COMMAND helix_render_pipelines_core_tests)
        set_tests_properties(helix_render_pipelines_core_tests_render_thread PROPERTIES ENVIRONMENT "HELIX_RENDER_THREAD=1;HELIX_RENDER_BATCHING=1")
    endif()
endif()

# The public surface of the runtime against its inventory (Tests/Api/Helix.RenderPipelines.Core.csv), with Helix.ScriptCore's tool.
if(HELIX_BUILD_TESTS AND PROJECT_IS_TOP_LEVEL AND HELIX_API_INVENTORY)
    add_test(NAME helix_render_pipelines_core_api COMMAND "${HELIX_DOTNET}" "${HELIX_API_INVENTORY}" --assembly "${runtime_dll}" --reference "${HELIX_CORE_MODULE}"
             --reference "${HELIX_SCRIPTING_RUNTIME}" --inventory "${CMAKE_CURRENT_SOURCE_DIR}/Tests/Api/Helix.RenderPipelines.Core.csv")
    add_dependencies(helix_render_pipelines_core_runtime helix_api_inventory)
endif()

if(TARGET helix_render_pipelines_core_tests AND HELIX_ENABLE_ASAN AND HELIX_PLATFORM STREQUAL "linux")
    helix_dependency_dir(scripting_dir Scripting)
    set(suppressions "LSAN_OPTIONS=suppressions=${scripting_dir}/Tests/Support/LeakSuppressions.txt")
    set_tests_properties(helix_render_pipelines_core_tests PROPERTIES ENVIRONMENT "${suppressions}")
    set_tests_properties(helix_render_pipelines_core_tests_coreclr PROPERTIES ENVIRONMENT "HELIX_DOTNET_RUNTIME=coreclr;${suppressions}")
    set_tests_properties(helix_render_pipelines_core_tests_render_thread PROPERTIES ENVIRONMENT "HELIX_RENDER_THREAD=1;HELIX_RENDER_BATCHING=1;${suppressions}")
endif()
