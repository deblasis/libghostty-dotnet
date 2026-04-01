cmake_minimum_required(VERSION 3.20)
project(GhosttyBridge LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)

add_library(GhosttyBridge SHARED GhosttyBridge.cpp)

target_link_libraries(GhosttyBridge PRIVATE
    d3d11
    dxgi
    ${CMAKE_CURRENT_SOURCE_DIR}/../../Ghostty/x86_64/ghostty.lib
)

# Output the DLL next to ghostty.dll
set_target_properties(GhosttyBridge PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/../../Ghostty/x86_64"
)
