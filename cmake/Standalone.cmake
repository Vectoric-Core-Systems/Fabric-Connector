# The top level of the Fabric-Connector repository: get an Aver Engine, configure it with this extension added to it, build and test the pair.
# Included by the root CMakeLists.txt (see its header for why the engine is configured as a project of its own).
#
#   cmake -S . -B build -DAVER_ENGINE_DIR=<an Aver Engine checkout>     a local engine, for work on both
#   cmake -S . -B build                                                 the engine at AVER_ENGINE_REF below, fetched from GitHub
#   cmake --build build                                                 builds what the extension and its tests use (not the editor or the engine's tests)
#   ctest --test-dir build                                              the extension's tests
#   -DFABRIC_DEPLOY_TO=<engine build>/bin                               also copies the extension to <engine build>/bin/Extensions/FabricConnector
#
# The engine's own build tree is <build>/engine; the extension is in <build>/engine/bin/Extensions/FabricConnector, beside AverAssetC in
# <build>/engine/bin: {extension.json, native/<platform>/Aver.Fabric.Native.*, managed/Aver.Fabric.dll, tools/Aver.Fabric.Convert.*, data/extension_api.json}.
set(AVER_ENGINE_DIR "" CACHE PATH "A local Aver Engine checkout to build against (empty: fetch AVER_ENGINE_REF)")
set(AVER_ENGINE_REPO "https://github.com/Vectoric-Core-Systems/Aver-Engine.git" CACHE STRING "Where the pinned engine is fetched from")
# THE PIN: the Aver Engine commit this extension is built and tested against. Change this one line to move to another engine.
set(AVER_ENGINE_REF "3bfb8f04e08909324836f15068e1cafe887a3a57" CACHE STRING "The Aver Engine commit (or tag) to fetch when AVER_ENGINE_DIR is empty")
set(AVER_ENGINE_CMAKE_ARGS "" CACHE STRING "More options for the engine's configure, as a ;-separated list of -D... arguments")
option(AVER_FABRIC_BUILD_TESTS "Build and register the extension's tests" ON)
# Opt-in: after each build, copy the built extension folder to <FABRIC_DEPLOY_TO>/Extensions/FabricConnector. Point it at the bin folder of your own
# engine build (e.g. <engine checkout>/build/bin) and the extension sits beside that build's editor and AverAssetC. Empty: nothing is copied.
set(FABRIC_DEPLOY_TO "" CACHE PATH "A folder to copy the built extension into, as <path>/Extensions/FabricConnector (empty: do not copy)")

if(NOT CMAKE_BUILD_TYPE AND NOT CMAKE_CONFIGURATION_TYPES)
  set(CMAKE_BUILD_TYPE Debug CACHE STRING "" FORCE)
endif()

# ---- The engine's source ----------------------------------------------------------------------------------------------------------
if(AVER_ENGINE_DIR)
  cmake_path(ABSOLUTE_PATH AVER_ENGINE_DIR NORMALIZE OUTPUT_VARIABLE AVER_ENGINE_SOURCE_DIR)
  message(STATUS "Fabric Connector: engine from AVER_ENGINE_DIR = ${AVER_ENGINE_SOURCE_DIR}")
else()
  include(FetchContent)
  # SOURCE_SUBDIR names a directory that is not there, so FetchContent only fetches: the engine is configured below, as a project of its own.
  FetchContent_Declare(AverEngine
    GIT_REPOSITORY ${AVER_ENGINE_REPO}
    GIT_TAG ${AVER_ENGINE_REF}
    SOURCE_SUBDIR none)
  FetchContent_MakeAvailable(AverEngine)
  set(AVER_ENGINE_SOURCE_DIR "${averengine_SOURCE_DIR}")
  message(STATUS "Fabric Connector: engine ${AVER_ENGINE_REF} fetched to ${AVER_ENGINE_SOURCE_DIR}")
endif()
if(NOT EXISTS "${AVER_ENGINE_SOURCE_DIR}/modules/extensions/include/aver/extensions/aver_ext.h")
  message(FATAL_ERROR "${AVER_ENGINE_SOURCE_DIR} is not an Aver Engine with the extension loader (modules/extensions); set AVER_ENGINE_DIR or AVER_ENGINE_REF")
endif()
# Two things this extension needs of the engine: AverAssetC built as a tool (not only with the engine's tests), and AVER_EXTERNAL_EXTENSIONS.
file(READ "${AVER_ENGINE_SOURCE_DIR}/CMakeLists.txt" _aver_engine_root)
if(NOT EXISTS "${AVER_ENGINE_SOURCE_DIR}/tools/CMakeLists.txt" OR NOT _aver_engine_root MATCHES "AVER_EXTERNAL_EXTENSIONS")
  message(FATAL_ERROR "This engine cannot build an extension from outside its tree (no tools/CMakeLists.txt for AverAssetC, or no AVER_EXTERNAL_EXTENSIONS). "
                      "Use a newer engine: set AVER_ENGINE_DIR, or move AVER_ENGINE_REF to a commit that has both.")
endif()
set(_aver_engine_args_fabric "")
if(_aver_engine_root MATCHES "AVER_WITH_FABRIC")   # an engine from before the extension left its tree: its in-tree copy is off, this repository is the source
  set(_aver_engine_args_fabric -DAVER_WITH_FABRIC=OFF)
endif()
unset(_aver_engine_root)

# ---- The engine, configured with the extension added to it ------------------------------------------------------------------------
# Only what the extension needs: no editor, no runtime host, no engine tests.
# AVER_EXTERNAL_EXTENSIONS makes the engine add this repository to its build (cmake/Extension.cmake).
set(AVER_ENGINE_BUILD_DIR ${CMAKE_BINARY_DIR}/engine)
set(_aver_engine_args
  -G "${CMAKE_GENERATOR}"
  -S "${AVER_ENGINE_SOURCE_DIR}"
  -B "${AVER_ENGINE_BUILD_DIR}"
  -DCMAKE_BUILD_TYPE=${CMAKE_BUILD_TYPE}
  -DAVER_BUILD_SANDBOX=OFF
  -DAVER_BUILD_GAME=OFF
  -DAVER_BUILD_TESTS=OFF
  ${_aver_engine_args_fabric}
  -DAVER_EXTERNAL_EXTENSIONS=${CMAKE_CURRENT_SOURCE_DIR}
  -DAVER_FABRIC_BUILD_TESTS=${AVER_FABRIC_BUILD_TESTS})
# What the caller chose for the toolchain goes through to the engine's configure.
foreach(_v CMAKE_MAKE_PROGRAM CMAKE_C_COMPILER CMAKE_CXX_COMPILER CMAKE_TOOLCHAIN_FILE CMAKE_OSX_DEPLOYMENT_TARGET CMAKE_OSX_ARCHITECTURES CMAKE_GENERATOR_PLATFORM)
  if(DEFINED ${_v} AND NOT "${${_v}}" STREQUAL "")
    list(APPEND _aver_engine_args -D${_v}=${${_v}})
  endif()
endforeach()
list(APPEND _aver_engine_args ${AVER_ENGINE_CMAKE_ARGS})

message(STATUS "Fabric Connector: configuring the engine in ${AVER_ENGINE_BUILD_DIR}")
execute_process(COMMAND ${CMAKE_COMMAND} ${_aver_engine_args} RESULT_VARIABLE _aver_engine_rc)
if(NOT _aver_engine_rc EQUAL 0)
  message(FATAL_ERROR "The engine's configure failed (${_aver_engine_rc}); see the output above")
endif()

# Where the engine and its bin/ are, for `dotnet build` / `dotnet run` in managed/ by hand (managed/Directory.Build.props reads it). Written only when it changes.
configure_file(${CMAKE_CURRENT_SOURCE_DIR}/cmake/fabric-build.props.in ${CMAKE_CURRENT_SOURCE_DIR}/managed/fabric-build.props @ONLY)

# ---- Build and test -------------------------------------------------------------------------------------------------------------
# One target builds the engine's part of the closure (AverAssetC, the libraries and tools the extension links, the .NET projects) and the extension;
# the engine's own `all` (editor excluded already) would also build modules nothing here uses. The nested build is the engine's: ctest here
# includes its test list, so `ctest --test-dir <build>` and `ctest --test-dir <build>/engine` run the same tests. The number of parallel jobs is the
# build tool's default, or CMAKE_BUILD_PARALLEL_LEVEL when that is set.
add_custom_target(AverEngineWithFabric ALL
  COMMAND ${CMAKE_COMMAND} --build "${AVER_ENGINE_BUILD_DIR}" --target Aver.Fabric.All
  USES_TERMINAL
  COMMENT "Building the Aver Engine pieces and the Fabric Connector (${AVER_ENGINE_BUILD_DIR})")
if(FABRIC_DEPLOY_TO)
  cmake_path(ABSOLUTE_PATH FABRIC_DEPLOY_TO NORMALIZE OUTPUT_VARIABLE _fabric_deploy_root)
  message(STATUS "Fabric Connector: the built extension is copied to ${_fabric_deploy_root}/Extensions/FabricConnector after each build")
  # Merges into what is there (files of the same name are replaced, nothing is deleted). On Windows a running editor or AverAssetC holds the
  # library open and the copy of that file fails: close it first.
  add_custom_command(TARGET AverEngineWithFabric POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_directory "${AVER_ENGINE_BUILD_DIR}/bin/Extensions/FabricConnector" "${_fabric_deploy_root}/Extensions/FabricConnector"
    COMMENT "Copying the Fabric Connector to ${_fabric_deploy_root}/Extensions/FabricConnector")
endif()
enable_testing()
# `subdirs` with the absolute path makes ctest read the engine build's test file from its own directory, as ctest run there does.
file(WRITE "${CMAKE_BINARY_DIR}/engine-tests.cmake" "subdirs(\"${AVER_ENGINE_BUILD_DIR}\")\n")
set_directory_properties(PROPERTIES TEST_INCLUDE_FILES "${CMAKE_BINARY_DIR}/engine-tests.cmake")
