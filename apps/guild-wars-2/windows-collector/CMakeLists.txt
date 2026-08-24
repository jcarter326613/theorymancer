cmake_minimum_required(VERSION 3.24)

project(theorymancer_gw2_collector LANGUAGES CXX)

if(NOT WIN32)
    message(FATAL_ERROR "The Guild Wars 2 collector targets Windows only.")
endif()

add_library(theorymancer_d3d11_proxy SHARED
    src/collector.cpp
    src/d3d11_proxy.cpp
    src/d3d11_proxy.def
)

target_compile_features(theorymancer_d3d11_proxy PRIVATE cxx_std_20)
target_compile_definitions(theorymancer_d3d11_proxy PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_compile_options(theorymancer_d3d11_proxy PRIVATE /W4 /WX /permissive-)
target_link_libraries(theorymancer_d3d11_proxy PRIVATE user32)

set_target_properties(theorymancer_d3d11_proxy PROPERTIES
    OUTPUT_NAME d3d11
)
