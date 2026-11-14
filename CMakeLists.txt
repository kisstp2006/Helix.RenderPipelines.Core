cmake_minimum_required(VERSION 3.28)
project(Helix.RenderPipelines.Core LANGUAGES CXX)

if(NOT COMMAND helix_add_library)
    include("${CMAKE_CURRENT_SOURCE_DIR}/External/Helix.CMake/Helix.cmake")
endif()

helix_require(ScriptCore)

# Three assemblies, built with the .NET SDK of the build host: the runtime over HelixEngine.CoreModule, the shader
# library (C# shader code over the engine's), and the core's shaders (compiled from their IL by the engine at load).
find_program(HELIX_DOTNET NAMES dotnet HINTS "$ENV{HELIX_DOTNET_ROOT}" "$ENV{DOTNET_ROOT}" REQUIRED)
set(core_artifacts "${CMAKE_BINARY_DIR}/HelixRenderPipelinesCore")
helix_dependency_dir(shaders_dir Shaders)
helix_dependency_dir(engine_dir Engine)

set(runtime_dir "${CMAKE_CURRENT_SOURCE_DIR}/Runtime")
set(runtime_dll "${core_artifacts}/bin/Helix.RenderPipelines.Core.Runtime/release/Helix.RenderPipelines.Core.Runtime.dll")
file(GLOB_RECURSE runtime_sources CONFIGURE_DEPENDS "${runtime_dir}/*.cs" "${runtime_dir}/*.csproj")
list(FILTER runtime_sources EXCLUDE REGEX "/(bin|obj)/")
add_custom_command(
    OUTPUT "${runtime_dll}"
    COMMAND "${HELIX_DOTNET}" build "${runtime_dir}/Helix.RenderPipelines.Core.Runtime.csproj" -c Release --artifacts-path "${core_artifacts}" --nologo
            -v quiet "-p:HelixCoreModule=${HELIX_CORE_MODULE}"
    DEPENDS ${runtime_sources} "${HELIX_CORE_MODULE}"
    COMMENT "Building Helix.RenderPipelines.Core.Runtime")
add_custom_target(helix_render_pipelines_core_runtime ALL DEPENDS "${runtime_dll}")
add_dependencies(helix_render_pipelines_core_runtime helix_core_module)

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

# Helix::RenderPipelines.Core for the pipeline repos: linking it builds the assemblies first.
add_library(helix_render_pipelines_core INTERFACE)
add_library(Helix::RenderPipelines.Core ALIAS helix_render_pipelines_core)
target_link_libraries(helix_render_pipelines_core INTERFACE Helix::ScriptCore)
add_dependencies(helix_render_pipelines_core helix_render_pipelines_core_runtime helix_render_pipelines_core_shaders)
set(HELIX_RENDER_PIPELINES_CORE "${runtime_dll}" CACHE INTERNAL "")
set(HELIX_RENDER_PIPELINES_CORE_SHADERS "${core_shaders_dll}" CACHE INTERNAL "")
set(HELIX_RENDER_PIPELINES_CORE_SHADER_LIBRARY "${core_library_dll}" CACHE INTERNAL "")

# Tests: C# test scripts (the runtime under test with them) run by a C++ program in the engine, with the engine's
# test shaders and the core's shaders as assets: the shared engine on the desktop, a .NET web app in node on the Web.
if(HELIX_PLATFORM STREQUAL "windows" OR HELIX_PLATFORM STREQUAL "linux")
    helix_add_test_suite(helix_render_pipelines_core_tests EXCLUDE "/Managed/" DEPS Helix::EngineShared)
elseif(HELIX_PLATFORM STREQUAL "web")
    helix_add_test_suite(helix_render_pipelines_core_tests DOTNET EXCLUDE "/Managed/" DEPS Helix::Engine)
endif()
if(TARGET helix_render_pipelines_core_tests)
    set(tests_dir "${CMAKE_CURRENT_SOURCE_DIR}/Tests")
    set(tests_dll "${core_artifacts}/bin/Helix.RenderPipelines.Core.Tests/release/Helix.RenderPipelines.Core.Tests.dll")
    get_filename_component(tests_bin "${tests_dll}" DIRECTORY)
    file(GLOB_RECURSE test_sources CONFIGURE_DEPENDS "${tests_dir}/Managed/*.cs" "${tests_dir}/Managed/*.csproj")
    list(FILTER test_sources EXCLUDE REGEX "/(bin|obj)/")
    add_custom_command(
        OUTPUT "${tests_dll}"
        COMMAND "${HELIX_DOTNET}" build "${tests_dir}/Managed/Helix.RenderPipelines.Core.Tests.csproj" -c Release --artifacts-path "${core_artifacts}"
                --nologo -v quiet "-p:HelixCoreModule=${HELIX_CORE_MODULE}" "-p:HelixRenderPipelinesCore=${runtime_dll}"
                "-p:HelixScriptingRuntime=${HELIX_SCRIPTING_RUNTIME}"
        DEPENDS ${test_sources} "${runtime_dll}"
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
