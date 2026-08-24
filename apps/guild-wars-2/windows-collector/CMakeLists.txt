cmake_minimum_required(VERSION 3.24)

project(theorymancer_gw2_collector LANGUAGES CXX ASM_MASM)

if(NOT WIN32)
    message(FATAL_ERROR "The Guild Wars 2 collector targets Windows only.")
endif()

set(SYSTEM_D3D11_DLL "$ENV{SystemRoot}/System32/d3d11.dll")
set(GENERATED_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/generated")
set(GENERATED_PROXY_ASM "${GENERATED_DIRECTORY}/d3d11_proxy.asm")
set(GENERATED_PROXY_DEF "${GENERATED_DIRECTORY}/d3d11_proxy.def")
set(GENERATED_PROXY_EXPORTS_HEADER "${GENERATED_DIRECTORY}/d3d11_proxy_exports.h")

if(NOT EXISTS "${SYSTEM_D3D11_DLL}")
    message(FATAL_ERROR "Could not locate the 64-bit system d3d11.dll at ${SYSTEM_D3D11_DLL}.")
endif()

file(MAKE_DIRECTORY "${GENERATED_DIRECTORY}")
execute_process(
    COMMAND powershell -NoProfile -ExecutionPolicy Bypass
        -File "${CMAKE_CURRENT_SOURCE_DIR}/tools/generate-d3d11-proxy.ps1"
        -ReferenceDll "${SYSTEM_D3D11_DLL}"
        -AssemblyOutput "${GENERATED_PROXY_ASM}"
        -DefinitionOutput "${GENERATED_PROXY_DEF}"
        -HeaderOutput "${GENERATED_PROXY_EXPORTS_HEADER}"
    RESULT_VARIABLE GENERATE_PROXY_RESULT
    OUTPUT_VARIABLE GENERATE_PROXY_OUTPUT
    ERROR_VARIABLE GENERATE_PROXY_ERROR
)
if(NOT GENERATE_PROXY_RESULT EQUAL 0)
    message(FATAL_ERROR
        "Could not generate the D3D11 proxy export surface.\n"
        "${GENERATE_PROXY_OUTPUT}${GENERATE_PROXY_ERROR}")
endif()

set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS
    "${CMAKE_CURRENT_SOURCE_DIR}/tools/generate-d3d11-proxy.ps1")

add_library(theorymancer_d3d11_proxy SHARED
    src/collector.cpp
    src/d3d11_proxy.cpp
    "${GENERATED_PROXY_ASM}"
    "${GENERATED_PROXY_DEF}"
)

target_compile_features(theorymancer_d3d11_proxy PRIVATE cxx_std_20)
target_compile_definitions(theorymancer_d3d11_proxy PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_compile_options(theorymancer_d3d11_proxy PRIVATE
    $<$<COMPILE_LANGUAGE:CXX>:/W4>
    $<$<COMPILE_LANGUAGE:CXX>:/WX>
    $<$<COMPILE_LANGUAGE:CXX>:/permissive->
)
target_include_directories(theorymancer_d3d11_proxy PRIVATE "${GENERATED_DIRECTORY}")
target_link_libraries(theorymancer_d3d11_proxy PRIVATE user32)

set_target_properties(theorymancer_d3d11_proxy PROPERTIES
    OUTPUT_NAME d3d11
)

include(CTest)
if(BUILD_TESTING)
    add_executable(d3d11_proxy_smoke_test
        tests/d3d11_proxy_smoke_test.cpp
    )
    target_compile_features(d3d11_proxy_smoke_test PRIVATE cxx_std_20)
    target_compile_definitions(d3d11_proxy_smoke_test PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_compile_options(d3d11_proxy_smoke_test PRIVATE /W4 /WX /permissive-)

    add_executable(d3d11_proxy_missing_export_test
        tests/d3d11_proxy_missing_export_test.cpp
        src/collector.cpp
        src/d3d11_proxy.cpp
    )
    target_compile_features(d3d11_proxy_missing_export_test PRIVATE cxx_std_20)
    target_compile_definitions(d3d11_proxy_missing_export_test PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_compile_options(d3d11_proxy_missing_export_test PRIVATE /W4 /WX /permissive-)
    target_include_directories(d3d11_proxy_missing_export_test PRIVATE "${GENERATED_DIRECTORY}")
    target_link_libraries(d3d11_proxy_missing_export_test PRIVATE user32)

    add_test(
        NAME d3d11_proxy_exports
        COMMAND powershell -NoProfile -ExecutionPolicy Bypass
            -File "${CMAKE_CURRENT_SOURCE_DIR}/tools/generate-d3d11-proxy.ps1"
            -ReferenceDll "${SYSTEM_D3D11_DLL}"
            -CandidateDll "$<TARGET_FILE:theorymancer_d3d11_proxy>"
            -Verify
    )
    add_test(
        NAME d3d11_proxy_device_creation
        COMMAND d3d11_proxy_smoke_test "$<TARGET_FILE:theorymancer_d3d11_proxy>"
    )
    add_test(
        NAME d3d11_proxy_missing_system_export
        COMMAND d3d11_proxy_missing_export_test
    )
endif()
