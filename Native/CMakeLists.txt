cmake_minimum_required(VERSION 3.16)
project(prism_ffmpeg VERSION 1.0.0 LANGUAGES C)

# Options
option(BUILD_SHARED_LIBS "Build shared library" ON)
option(PRISM_USE_SYSTEM_FFMPEG "Use system FFmpeg instead of bundled" OFF)

# Set C standard
set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)

# Output directories
set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib)
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib)
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# Platform detection
if(WIN32)
    set(PRISM_PLATFORM "Windows")
    set(PRISM_PLUGIN_DIR "${CMAKE_SOURCE_DIR}/../Plugins/Windows/x86_64")
elseif(APPLE)
    set(PRISM_PLATFORM "macOS")
    set(PRISM_PLUGIN_DIR "${CMAKE_SOURCE_DIR}/../Plugins/macOS")
elseif(UNIX)
    set(PRISM_PLATFORM "Linux")
    set(PRISM_PLUGIN_DIR "${CMAKE_SOURCE_DIR}/../Plugins/Linux/x86_64")
endif()

message(STATUS "Building for: ${PRISM_PLATFORM}")
message(STATUS "Plugin output: ${PRISM_PLUGIN_DIR}")

# Find FFmpeg
if(PRISM_USE_SYSTEM_FFMPEG)
    find_package(PkgConfig REQUIRED)
    pkg_check_modules(AVCODEC REQUIRED libavcodec)
    pkg_check_modules(AVFORMAT REQUIRED libavformat)
    pkg_check_modules(AVUTIL REQUIRED libavutil)
    pkg_check_modules(SWSCALE REQUIRED libswscale)
    pkg_check_modules(SWRESAMPLE REQUIRED libswresample)

    set(FFMPEG_INCLUDE_DIRS
        ${AVCODEC_INCLUDE_DIRS}
        ${AVFORMAT_INCLUDE_DIRS}
        ${AVUTIL_INCLUDE_DIRS}
        ${SWSCALE_INCLUDE_DIRS}
        ${SWRESAMPLE_INCLUDE_DIRS}
    )

    set(FFMPEG_LIBRARIES
        ${AVCODEC_LIBRARIES}
        ${AVFORMAT_LIBRARIES}
        ${AVUTIL_LIBRARIES}
        ${SWSCALE_LIBRARIES}
        ${SWRESAMPLE_LIBRARIES}
    )

    set(FFMPEG_LIBRARY_DIRS
        ${AVCODEC_LIBRARY_DIRS}
        ${AVFORMAT_LIBRARY_DIRS}
        ${AVUTIL_LIBRARY_DIRS}
        ${SWSCALE_LIBRARY_DIRS}
        ${SWRESAMPLE_LIBRARY_DIRS}
    )
else()
    # Use bundled FFmpeg (user must provide)
    if(WIN32)
        set(FFMPEG_ROOT "${CMAKE_SOURCE_DIR}/ffmpeg/windows" CACHE PATH "FFmpeg root directory")
    elseif(APPLE)
        set(FFMPEG_ROOT "${CMAKE_SOURCE_DIR}/ffmpeg/macos" CACHE PATH "FFmpeg root directory")
    else()
        set(FFMPEG_ROOT "${CMAKE_SOURCE_DIR}/ffmpeg/linux" CACHE PATH "FFmpeg root directory")
    endif()

    set(FFMPEG_INCLUDE_DIRS "${FFMPEG_ROOT}/include")
    set(FFMPEG_LIBRARY_DIRS "${FFMPEG_ROOT}/lib")

    if(WIN32)
        set(FFMPEG_LIBRARIES
            avcodec
            avformat
            avutil
            swscale
            swresample
        )
    else()
        set(FFMPEG_LIBRARIES
            avcodec
            avformat
            avutil
            swscale
            swresample
        )
    endif()

    if(NOT EXISTS "${FFMPEG_ROOT}")
        message(WARNING "FFmpeg not found at ${FFMPEG_ROOT}")
        message(WARNING "Please download FFmpeg development libraries and place them in the ffmpeg directory")
        message(WARNING "Windows: https://www.gyan.dev/ffmpeg/builds/ (get the shared build)")
        message(WARNING "Linux: Use system FFmpeg with -DPRISM_USE_SYSTEM_FFMPEG=ON")
        message(WARNING "macOS: brew install ffmpeg")
    endif()
endif()

# Source files
set(PRISM_SOURCES
    src/prism_ffmpeg.c
)

set(PRISM_HEADERS
    include/prism_ffmpeg.h
)

# Create library
add_library(prism_ffmpeg ${PRISM_SOURCES} ${PRISM_HEADERS})

# Include directories
target_include_directories(prism_ffmpeg
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/include
    PRIVATE
        ${FFMPEG_INCLUDE_DIRS}
)

# Link directories
target_link_directories(prism_ffmpeg PRIVATE ${FFMPEG_LIBRARY_DIRS})

# Link libraries
target_link_libraries(prism_ffmpeg PRIVATE ${FFMPEG_LIBRARIES})

# Platform-specific settings
if(WIN32)
    target_compile_definitions(prism_ffmpeg PRIVATE
        PRISM_FFMPEG_EXPORTS
        _CRT_SECURE_NO_WARNINGS
    )

    # Link Windows-specific libraries
    target_link_libraries(prism_ffmpeg PRIVATE
        ws2_32
        secur32
        bcrypt
    )

    # Set output name
    set_target_properties(prism_ffmpeg PROPERTIES
        OUTPUT_NAME "prism_ffmpeg"
        PREFIX ""
        SUFFIX ".dll"
    )
elseif(APPLE)
    target_compile_options(prism_ffmpeg PRIVATE -fvisibility=hidden)

    set_target_properties(prism_ffmpeg PROPERTIES
        OUTPUT_NAME "prism_ffmpeg"
        PREFIX "lib"
        SUFFIX ".dylib"
        INSTALL_RPATH "@loader_path"
        BUILD_WITH_INSTALL_RPATH TRUE
    )

    # Link macOS frameworks
    target_link_libraries(prism_ffmpeg PRIVATE
        "-framework CoreFoundation"
        "-framework CoreMedia"
        "-framework CoreVideo"
        "-framework VideoToolbox"
    )
else()
    target_compile_options(prism_ffmpeg PRIVATE -fvisibility=hidden -fPIC)

    set_target_properties(prism_ffmpeg PROPERTIES
        OUTPUT_NAME "prism_ffmpeg"
        PREFIX "lib"
        SUFFIX ".so"
        INSTALL_RPATH "$ORIGIN"
        BUILD_WITH_INSTALL_RPATH TRUE
    )

    # Link pthread
    target_link_libraries(prism_ffmpeg PRIVATE pthread m)
endif()

# Installation
install(TARGETS prism_ffmpeg
    LIBRARY DESTINATION ${PRISM_PLUGIN_DIR}
    RUNTIME DESTINATION ${PRISM_PLUGIN_DIR}
    ARCHIVE DESTINATION ${PRISM_PLUGIN_DIR}
)

# Copy FFmpeg DLLs on Windows (if using bundled)
if(WIN32 AND NOT PRISM_USE_SYSTEM_FFMPEG)
    if(EXISTS "${FFMPEG_ROOT}/bin")
        file(GLOB FFMPEG_DLLS "${FFMPEG_ROOT}/bin/*.dll")
        install(FILES ${FFMPEG_DLLS} DESTINATION ${PRISM_PLUGIN_DIR})
    endif()
endif()

# Custom target to copy to Unity Plugins folder
add_custom_target(install_plugin
    COMMAND ${CMAKE_COMMAND} --install ${CMAKE_BINARY_DIR} --prefix ${CMAKE_SOURCE_DIR}/..
    DEPENDS prism_ffmpeg
    COMMENT "Installing plugin to Unity Plugins folder"
)

# Print configuration summary
message(STATUS "")
message(STATUS "=== Prism FFmpeg Configuration ===")
message(STATUS "Platform: ${PRISM_PLATFORM}")
message(STATUS "Build type: ${CMAKE_BUILD_TYPE}")
message(STATUS "Shared library: ${BUILD_SHARED_LIBS}")
message(STATUS "FFmpeg includes: ${FFMPEG_INCLUDE_DIRS}")
message(STATUS "FFmpeg libraries: ${FFMPEG_LIBRARIES}")
message(STATUS "Output directory: ${PRISM_PLUGIN_DIR}")
message(STATUS "")
