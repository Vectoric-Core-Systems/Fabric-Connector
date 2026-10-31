# Fabric Connector -- an Aver Engine extension that converts a Godot 4 project into an Aver project (docs/FABRIC.md,
# docs/plans/EXTENSION.md). This one file serves two roles:
#
#   top level (cmake -S . -B build)   cmake/Standalone.cmake: gets an Aver Engine (a local checkout through AVER_ENGINE_DIR, else the commit
#                                     AVER_ENGINE_REF fetched from GitHub), configures it in <build>/engine with this extension added to it, and
#                                     builds and tests the pair. Nothing is compiled by this level itself.
#   added by an engine                cmake/Extension.cmake: the extension's own targets, in the engine's build (<build>/engine/bin/Extensions/FabricConnector).
#
# WHY NOT add_subdirectory(<engine>) FROM HERE. The engine's CMake reads CMAKE_SOURCE_DIR as "the engine's directory" (about a thousand uses), and
# CMake resets that variable in every directory to the top-level source directory, so it only works while the engine is the top level. The
# extension therefore goes into the engine's build, which is where the in-tree extension has always been built.
#
#   native/    the Godot readers (formats.godot), the GDExtension host (gdext) and the converter (tools/fabric), one SHARED library
#   managed/   Aver.Fabric (the runtime converted scripts compile against), Aver.Fabric.Convert (the script converter), Aver.Fabric.Tests
#   tests/     the C++ tests (tests/fabric, tests/formats.godot) and their fixtures
#   third_party/godot-gdextension  Godot's published GDExtension interface header and API dump (MIT)
cmake_minimum_required(VERSION 3.24)
if(NOT CMAKE_SOURCE_DIR STREQUAL CMAKE_CURRENT_SOURCE_DIR)
  include(${CMAKE_CURRENT_LIST_DIR}/cmake/Extension.cmake)
  return()
endif()

# Same macOS floor as the engine (set before project(), where CMake reads it).
if(CMAKE_HOST_SYSTEM_NAME STREQUAL "Darwin" AND NOT CMAKE_OSX_DEPLOYMENT_TARGET)
  set(CMAKE_OSX_DEPLOYMENT_TARGET "14.0" CACHE STRING "Minimum macOS version")
endif()
project(FabricConnector VERSION 0.1.0 LANGUAGES NONE)
include(${CMAKE_CURRENT_LIST_DIR}/cmake/Standalone.cmake)
