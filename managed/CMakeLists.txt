# The managed half of the Fabric Connector, built into the extension's own folder (docs/plans/EXTENSION.md section 3.6):
#
#   Aver.Fabric          -> bin/Extensions/FabricConnector/managed/Aver.Fabric.dll   the runtime the host loads and converted scripts compile against
#   Aver.Fabric.Convert  -> bin/Extensions/FabricConnector/tools/                    the script converter (+ Roslyn); conversion time only, never shipped
#
# Both are OPTIONAL like the scripting bridge: without the .NET SDK nothing here builds, and the extension's manifest then lists a file that is
# not there, so the loader refuses it with that reason. Aver.Fabric.Tests is registered with ctest below (FabricManagedTests).
#
# The engine they build against is AVER_ENGINE_SOURCE_DIR (Directory.Build.props: AverEngineDir); the projects reference its Aver.Framework.
find_program(AVER_DOTNET_EXE dotnet)
if(NOT AVER_DOTNET_EXE)
  message(STATUS "Fabric Connector: no `dotnet` on PATH - the managed half (Aver.Fabric, Aver.Fabric.Convert) will not be built")
  return()
endif()

# The flags modules/scripting/CMakeLists.txt in the engine explains: no compiler server and no reused worker nodes, so nothing holds a file the next
# build writes. AverEngineDir and AverBinDir are what Directory.Build.props would otherwise look up.
set(AVER_FABRIC_DOTNET_FLAGS -c Release --nologo -v quiet -p:UseSharedCompilation=false -nodeReuse:false
    -p:AverEngineDir=${AVER_ENGINE_SOURCE_DIR} -p:AverBinDir=${CMAKE_BINARY_DIR}/bin)
set(AVER_FABRIC_MANAGED_DIR ${CMAKE_CURRENT_SOURCE_DIR})

# ---- Aver.Fabric ----
# `dotnet build -o` stages the project's whole reference closure (Aver.Framework and what it references) beside the output, so it goes to a private
# folder and only Aver.Fabric.dll is copied into the extension: the engine's assemblies are the host's, not the extension's.
file(GLOB_RECURSE AVER_FABRIC_RUNTIME_SOURCES CONFIGURE_DEPENDS
  ${AVER_FABRIC_MANAGED_DIR}/Aver.Fabric/*.cs
  ${AVER_FABRIC_MANAGED_DIR}/Aver.Fabric/*.csproj)
list(FILTER AVER_FABRIC_RUNTIME_SOURCES EXCLUDE REGEX "/(obj|bin)/")

set(AVER_FABRIC_RUNTIME_BUILD ${CMAKE_BINARY_DIR}/extensions/FabricConnector/managed)
# The bridge's outputs: Aver.Fabric compiles against the same Aver.Framework, so a re-staged bridge (Framework or its closure changed) rebuilds it, and
# the bridge's own build of the shared project folders finishes first.
get_property(AVER_BRIDGE_OUTPUTS GLOBAL PROPERTY AVER_BRIDGE_OUTPUTS)
# The stamp is the rule's output: copy_if_different leaves an identical Aver.Fabric.dll untouched, and an output older than the bridge's would rebuild every time.
add_custom_command(
  OUTPUT ${AVER_FABRIC_RUNTIME_BUILD}/Aver.Fabric.stamp
  BYPRODUCTS ${AVER_FABRIC_STAGE}/managed/Aver.Fabric.dll
  COMMAND ${AVER_DOTNET_EXE} build ${AVER_FABRIC_MANAGED_DIR}/Aver.Fabric/Aver.Fabric.csproj ${AVER_FABRIC_DOTNET_FLAGS}
          -o ${AVER_FABRIC_RUNTIME_BUILD}
  COMMAND ${CMAKE_COMMAND} -E make_directory ${AVER_FABRIC_STAGE}/managed
  COMMAND ${CMAKE_COMMAND} -E copy_if_different ${AVER_FABRIC_RUNTIME_BUILD}/Aver.Fabric.dll ${AVER_FABRIC_STAGE}/managed/Aver.Fabric.dll
  COMMAND ${CMAKE_COMMAND} -E touch ${AVER_FABRIC_RUNTIME_BUILD}/Aver.Fabric.stamp
  DEPENDS ${AVER_FABRIC_RUNTIME_SOURCES} ${AVER_BRIDGE_OUTPUTS}
  COMMENT "Fabric Connector: building Aver.Fabric (-> bin/Extensions/FabricConnector/managed)"
  VERBATIM)
add_custom_target(Aver.Fabric.Managed DEPENDS ${AVER_FABRIC_RUNTIME_BUILD}/Aver.Fabric.stamp)
set_target_properties(Aver.Fabric.Managed PROPERTIES FOLDER "extensions")
# The engine's bridge build (modules/scripting) stages bin/Scripting, which the converter compiles scripts against, and shares the project folders'
# obj/ with this build: it finishes first.
if(TARGET Aver.Scripting.Bridge)
  add_dependencies(Aver.Fabric.Managed Aver.Scripting.Bridge)
endif()

# ---- Aver.Fabric.Convert ----
# Roslyn 5.6.0 comes from the engine's vendored third_party/nuget folder (Directory.Build.props, NuGet.config), so the restore needs no network. It
# compiles the converted scripts against Aver.Fabric.dll and the engine's assemblies at RUN time (AverAssetC fabric passes the folders), so it has no
# build dependency on either.
file(GLOB AVER_FABRIC_CONVERT_SOURCES CONFIGURE_DEPENDS
  ${AVER_FABRIC_MANAGED_DIR}/Aver.Fabric.Convert/*.cs
  ${AVER_FABRIC_MANAGED_DIR}/Aver.Fabric.Convert/GdScript/*.cs
  ${AVER_FABRIC_MANAGED_DIR}/Aver.Fabric.Convert/*.csproj)
set(AVER_FABRIC_CONVERT_OUTPUTS
  ${AVER_FABRIC_STAGE}/tools/Aver.Fabric.Convert.dll
  ${AVER_FABRIC_STAGE}/tools/Aver.Fabric.Convert.runtimeconfig.json)
add_custom_command(
  OUTPUT ${AVER_FABRIC_CONVERT_OUTPUTS}
  COMMAND ${AVER_DOTNET_EXE} build ${AVER_FABRIC_MANAGED_DIR}/Aver.Fabric.Convert/Aver.Fabric.Convert.csproj ${AVER_FABRIC_DOTNET_FLAGS}
          -o ${AVER_FABRIC_STAGE}/tools
  DEPENDS ${AVER_FABRIC_CONVERT_SOURCES}
  COMMENT "Fabric Connector: building Aver.Fabric.Convert (-> bin/Extensions/FabricConnector/tools)"
  VERBATIM)
add_custom_target(Aver.Fabric.Convert DEPENDS ${AVER_FABRIC_CONVERT_OUTPUTS})
set_target_properties(Aver.Fabric.Convert PROPERTIES FOLDER "extensions")

# ---- Aver.Fabric.Tests ----
# The native libraries the managed suites load beside their assembly (the engine's ABI libraries Aver.Fabric P/Invokes), built with the rest: the
# engine is added without its own `all`, so nothing else asks for them. A suite whose library is missing skips and says so.
add_custom_target(Aver.Fabric.TestLibs)
foreach(_aver_fabric_lib Aver.Anim.Abi Aver.Audio.Abi Aver.Nav.Abi Aver.Net.Abi Aver.UI.Abi Aver.Scene)
  if(TARGET ${_aver_fabric_lib})
    add_dependencies(Aver.Fabric.TestLibs ${_aver_fabric_lib})
  endif()
endforeach()
set_target_properties(Aver.Fabric.TestLibs PROPERTIES FOLDER "extensions")

if(AVER_FABRIC_BUILD_TESTS)
  # Aver.Fabric (the Godot-compatible runtime): math, Variant, signals, node tree and lifecycle, run by the managed node host. The suites that
  # load the engine's ABI libraries or run AverAssetC find them in this build's bin (AVER_BIN_DIR) and skip, saying so, when they are not built.
  # The `dotnet run` builds Aver.Framework into the engine's obj/, so it must not overlap another dotnet build: one at a time.
  add_test(NAME FabricManagedTests
    COMMAND "${AVER_DOTNET_EXE}" run --project "${AVER_FABRIC_MANAGED_DIR}/Aver.Fabric.Tests/Aver.Fabric.Tests.csproj" --configuration Release --nologo
    WORKING_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
  set_tests_properties(FabricManagedTests PROPERTIES
    TIMEOUT 900
    ENVIRONMENT "DOTNET_CLI_TELEMETRY_OPTOUT=1;DOTNET_NOLOGO=1;AVER_ENGINE_DIR=${AVER_ENGINE_SOURCE_DIR};AVER_BIN_DIR=${CMAKE_BINARY_DIR}/bin"
    FAIL_REGULAR_EXPRESSION "FAIL: |FAIL  "
    RESOURCE_LOCK aver_managed_build)
endif()
