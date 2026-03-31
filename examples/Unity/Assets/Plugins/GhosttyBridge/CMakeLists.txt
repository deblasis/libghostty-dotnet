cmake_minimum_required(VERSION 3.20)
project(GhosttyBridge LANGUAGES CXX)

add_library(GhosttyBridge SHARED GhosttyBridge.cpp)
target_link_libraries(GhosttyBridge PRIVATE d3d11 dxgi)
set_target_properties(GhosttyBridge PROPERTIES
    OUTPUT_NAME "GhosttyBridge"
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
)

# Copy to Unity plugins folder after build
add_custom_command(TARGET GhosttyBridge POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy
        "$<TARGET_FILE:GhosttyBridge>"
        "${CMAKE_SOURCE_DIR}/../Ghostty/x86_64/GhosttyBridge.dll"
)
