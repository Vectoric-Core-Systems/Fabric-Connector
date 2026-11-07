# FabricConvertTest -- `AverAssetC fabric` over the hand-written Godot project in tests/fabric/data/ConvertGame, then every
# file it wrote read back with the engine's own loaders (.ocproject, .ocworld, .ocprefab, .ocinput, .ocmat, .ocmesh).
# It runs the tool as a process, like AverAssetCTest, so it depends on that target; the tool reaches the converter through the extension loader
# (bin/Extensions/FabricConnector, the Aver.Fabric.Native target). No device, no .NET, no engine run.
# Registered with ctest by the root CMakeLists.txt's walk (aver_fabric_register_ctest_in: every target that ends in Test).
# The engine's tree (AVER_ENGINE_SOURCE_DIR) supplies AverAssetC, the extension loader and the editor's import-dialog sources FabricImportTest compiles.
set(FABRIC_TEST_DATA ${AVER_FABRIC_DIR}/tests/fabric/data)
set(FABRIC_CONVERTER_DIR ${AVER_FABRIC_DIR}/native/tools)   # holds fabric/FabricProcess.hpp (FabricE2ETest)

# A test that runs `AverAssetC fabric` as a process needs the tool and the extension it loads.
function(fabric_needs_converter target)
  add_dependencies(${target} AverAssetC Aver.Fabric.Native)
endfunction()

add_executable(FabricConvertTest src/FabricConvertTest.cpp)
target_link_libraries(FabricConvertTest PRIVATE Aver.Formats.Godot Aver.Formats Aver.Platform Aver.Core)
if(TARGET Aver.Formats.Material)
  target_link_libraries(FabricConvertTest PRIVATE Aver.Formats.Material Aver.Render.PBR.Materials)
  target_compile_definitions(FabricConvertTest PRIVATE AVER_FABRIC_TEST_OCMAT=1)
endif()
fabric_needs_converter(FabricConvertTest)
target_include_directories(FabricConvertTest PRIVATE ${AVER_ENGINE_SOURCE_DIR}/modules/world/include)
target_compile_definitions(FabricConvertTest PRIVATE
  AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
  AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
set_target_properties(FabricConvertTest PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# FabricMeshExtraTest -- G4 R1 (vertex colour, tangent, UV2) through the converter on tests/fabric/data/ExtraGame: the extra streams in the
# converted .ocmesh files and the vertexcolor flag in the .ocmat. Runs the tool as a process, like FabricConvertTest.
add_executable(FabricMeshExtraTest src/FabricMeshExtraTest.cpp)
target_link_libraries(FabricMeshExtraTest PRIVATE Aver.Formats.Godot Aver.Formats Aver.Platform Aver.Core)
if(TARGET Aver.Formats.Material)
  target_link_libraries(FabricMeshExtraTest PRIVATE Aver.Formats.Material Aver.Render.PBR.Materials)
  target_compile_definitions(FabricMeshExtraTest PRIVATE AVER_FABRIC_TEST_OCMAT=1)
endif()
fabric_needs_converter(FabricMeshExtraTest)
target_compile_definitions(FabricMeshExtraTest PRIVATE
  AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
  AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
set_target_properties(FabricMeshExtraTest PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# GdextHostTest -- the GDExtension host (native/gdext) against FabricTestExt, a GDExtension written in C against
# gdextension_interface.h only (gdext_test/fabric_test_ext.c; no godot-cpp). The extension and the .gdextension fixtures
# (data/gdext/) land in bin/gdext_test. Headless: no device, no .NET, no engine run.
add_library(FabricTestExt SHARED gdext_test/fabric_test_ext.c)
target_include_directories(FabricTestExt PRIVATE ${AVER_FABRIC_DIR}/third_party/godot-gdextension)
set_target_properties(FabricTestExt PROPERTIES
  OUTPUT_NAME FabricTestExt
  FOLDER "tests"
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin/gdext_test
  LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin/gdext_test
  ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/gdext_test_import)
add_custom_command(TARGET FabricTestExt POST_BUILD
  COMMAND ${CMAKE_COMMAND} -E copy_directory ${CMAKE_CURRENT_SOURCE_DIR}/data/gdext ${CMAKE_BINARY_DIR}/bin/gdext_test
  COMMENT "FabricTestExt: .gdextension fixtures -> bin/gdext_test")

add_executable(GdextHostTest src/GdextHostTest.cpp)
target_link_libraries(GdextHostTest PRIVATE Aver.Fabric.GDExt Aver.Formats.Godot Aver.Formats Aver.Platform Aver.Core)
target_include_directories(GdextHostTest PRIVATE ${AVER_FABRIC_DIR}/native/gdext/src)
add_dependencies(GdextHostTest FabricTestExt)
target_compile_definitions(GdextHostTest PRIVATE
  AVER_GDEXT_TEST_DIR="${CMAKE_BINARY_DIR}/bin/gdext_test"
  AVER_GDEXT_API_JSON="${AVER_FABRIC_DIR}/third_party/godot-gdextension/extension_api.json"
  AVER_GDEXT_HEADER="${AVER_FABRIC_DIR}/third_party/godot-gdextension/gdextension_interface.h")
set_target_properties(GdextHostTest PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# FabricE2ETest -- the whole connector through its real command line on tests/fabric/data/E2EGame: scenes, assets, GDScript and GodotSharp
# conversion (Aver.Fabric.Convert on dotnet, compiled against Aver.Fabric), the GDExtension plugin (FabricTestExt, copied in under the
# three platform names) and the merged report. Needs the script converter and the managed runtime staged beside AverAssetC, so it
# depends on those targets when they exist and skips (77) when dotnet or they are absent.
add_executable(FabricE2ETest src/FabricE2ETest.cpp ${AVER_FABRIC_DIR}/native/tools/fabric/FabricProcess.cpp)
target_link_libraries(FabricE2ETest PRIVATE Aver.Formats.Godot Aver.Formats Aver.Platform Aver.Core)
target_include_directories(FabricE2ETest PRIVATE ${FABRIC_CONVERTER_DIR} ${AVER_ENGINE_SOURCE_DIR}/modules/world/include)
fabric_needs_converter(FabricE2ETest)
add_dependencies(FabricE2ETest FabricTestExt)
if(TARGET Aver.Fabric.Convert)
  add_dependencies(FabricE2ETest Aver.Fabric.Convert)
endif()
if(TARGET Aver.Fabric.Managed)
  add_dependencies(FabricE2ETest Aver.Fabric.Managed)
endif()
target_compile_definitions(FabricE2ETest PRIVATE
  AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
  AVER_FABRIC_TEST_EXT="$<TARGET_FILE:FabricTestExt>"
  AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
set_target_properties(FabricE2ETest PROPERTIES RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# FabricImportTest -- File > Import Godot Project... through the editor's generic import dialog (sandbox/src/ExtensionImport.cpp, compiled in without
# ImGui): the importer read from the installed extension.json by the loader, folder checks, the command line, the converter's summary, and the
# importer running the real AverAssetC on a worker thread. The generic logic has its own engine test (ExtensionImportTest).
add_executable(FabricImportTest src/FabricImportTest.cpp
  ${AVER_ENGINE_SOURCE_DIR}/sandbox/src/ExtensionImport.cpp
  ${AVER_ENGINE_SOURCE_DIR}/sandbox/src/ProcessStream.cpp
  ${AVER_ENGINE_SOURCE_DIR}/sandbox/src/EditorNotifications.cpp)
target_link_libraries(FabricImportTest PRIVATE Aver.Extensions Aver.Formats Aver.Platform Aver.Core)
target_include_directories(FabricImportTest PRIVATE ${AVER_ENGINE_SOURCE_DIR}/sandbox/src)
fabric_needs_converter(FabricImportTest)
if(TARGET Aver.Fabric.Convert)
  add_dependencies(FabricImportTest Aver.Fabric.Convert)
endif()
if(TARGET Aver.Fabric.Managed)
  add_dependencies(FabricImportTest Aver.Fabric.Managed)
endif()
target_compile_definitions(FabricImportTest PRIVATE AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
set_target_properties(FabricImportTest PROPERTIES RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# FabricLightsTest -- G4 R2 (omni/spot/sun calibration and ratings) through the converter on tests/fabric/data/LightsGame: the SUN record's lux,
# the light Props, one sun, and the per-property report. Runs the tool as a process, like FabricConvertTest.
add_executable(FabricLightsTest src/FabricLightsTest.cpp)
target_link_libraries(FabricLightsTest PRIVATE Aver.Formats.Godot Aver.Formats Aver.Platform Aver.Core)
fabric_needs_converter(FabricLightsTest)
target_compile_definitions(FabricLightsTest PRIVATE
  AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
  AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
set_target_properties(FabricLightsTest PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# FabricCameraTest -- G4 R4 (camera projection, keep_aspect, offsets, cull masks, render layers, visibility ranges) through the converter on
# tests/fabric/data/CameraGame: the Props, the made-current first camera, and the per-property report. Runs the tool as a process.
add_executable(FabricCameraTest src/FabricCameraTest.cpp)
target_link_libraries(FabricCameraTest PRIVATE Aver.Formats.Godot Aver.Formats Aver.Platform Aver.Core)
fabric_needs_converter(FabricCameraTest)
target_compile_definitions(FabricCameraTest PRIVATE
  AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
  AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
set_target_properties(FabricCameraTest PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# FabricEnvTest -- G4 R3 (WorldEnvironment, tonemapper, adjustments, fog, panorama sky, CameraAttributes) through the converter on
# tests/fabric/data/EnvGame, and LightsGame (no environment) for the unchanged default. Runs the tool as a process, like FabricConvertTest.
add_executable(FabricEnvTest src/FabricEnvTest.cpp)
target_link_libraries(FabricEnvTest PRIVATE Aver.Formats.Godot Aver.Formats Aver.Platform Aver.Core)
fabric_needs_converter(FabricEnvTest)
target_compile_definitions(FabricEnvTest PRIVATE
  AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
  AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
set_target_properties(FabricEnvTest PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# FabricEnvVolTest -- G4 R8 (FogVolume + FogMaterial, ReflectionProbe, LightmapGI, LightmapProbe, VoxelGI, volumetric fog, the screen-space / SDF flags) through the
# converter on tests/fabric/data/EnvVolGame; EnvGame and LightsGame for the unchanged default. Runs the tool as a process, like FabricEnvTest.
add_executable(FabricEnvVolTest src/FabricEnvVolTest.cpp)
target_link_libraries(FabricEnvVolTest PRIVATE Aver.Formats.Godot Aver.Formats Aver.Platform Aver.Core)
fabric_needs_converter(FabricEnvVolTest)
target_compile_definitions(FabricEnvVolTest PRIVATE
  AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
  AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
set_target_properties(FabricEnvVolTest PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# FabricViewportTest -- G4 R5 (SubViewport, ViewportTexture, SubViewportContainer) through the converter on tests/fabric/data/ViewportGame: the view's key and
# consumers, the '@rt:viewport.<key>' texture reference in the .ocmat, cameras below a view, the container's Props, and the per-property report. Runs the tool
# as a process, like FabricConvertTest.
add_executable(FabricViewportTest src/FabricViewportTest.cpp)
target_link_libraries(FabricViewportTest PRIVATE Aver.Formats.Godot Aver.Formats Aver.Platform Aver.Core)
if(TARGET Aver.Formats.Material)
  target_link_libraries(FabricViewportTest PRIVATE Aver.Formats.Material Aver.Render.PBR.Materials)
  target_compile_definitions(FabricViewportTest PRIVATE AVER_FABRIC_TEST_OCMAT=1)
endif()
fabric_needs_converter(FabricViewportTest)
target_compile_definitions(FabricViewportTest PRIVATE
  AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
  AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
set_target_properties(FabricViewportTest PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# FabricMaterialFxTest -- G4 R6 (StandardMaterial3D / ORMMaterial3D features: UV transforms, triplanar, cull / blend / depth modes, alpha hash, billboard,
# fades, rim, clearcoat, parallax, detail ...) through the converter on tests/fabric/data/MatGame: the .ocmat files read back with the engine's own loader
# and the per-property report. Runs the tool as a process, like FabricConvertTest.
if(TARGET Aver.Formats.Material)
  add_executable(FabricMaterialFxTest src/FabricMaterialFxTest.cpp)
  target_link_libraries(FabricMaterialFxTest PRIVATE Aver.Formats.Godot Aver.Formats Aver.Formats.Material Aver.Render.PBR.Materials Aver.Platform Aver.Core)
  fabric_needs_converter(FabricMaterialFxTest)
  target_compile_definitions(FabricMaterialFxTest PRIVATE
    AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
    AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
  set_target_properties(FabricMaterialFxTest PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)
endif()

# FabricWave2Test -- wave 2 of the Godot parity plan through the converter on tests/fabric/data/Wave2Game: the sampler and the F35 fields in the .ocmat, procedural and
# alias textures baked to pictures, per-surface material overrides in the level, the project's anisotropy. Runs the tool as a process, like FabricMaterialFxTest.
if(TARGET Aver.Formats.Material)
  add_executable(FabricWave2Test src/FabricWave2Test.cpp)
  target_link_libraries(FabricWave2Test PRIVATE Aver.Formats.Godot Aver.Formats Aver.Formats.Material Aver.Render.PBR.Materials Aver.Platform Aver.Core)
  fabric_needs_converter(FabricWave2Test)
  target_compile_definitions(FabricWave2Test PRIVATE
    AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
    AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
  set_target_properties(FabricWave2Test PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)
endif()

# FabricWave4Test -- wave 4 of the Godot parity plan (rendering settings, lights, shadows, environment): the pure mappings of GodotRender.hpp and the converter on
# tests/fabric/data/Wave4Game, Wave4PhysGame and Wave4SkyGame (the procedural sky off and on). Runs the tool as a process, like FabricEnvTest.
add_executable(FabricWave4Test src/FabricWave4Test.cpp)
target_link_libraries(FabricWave4Test PRIVATE Aver.Formats.Godot Aver.Formats Aver.Platform Aver.Core)
fabric_needs_converter(FabricWave4Test)
target_compile_definitions(FabricWave4Test PRIVATE
  AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
  AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
set_target_properties(FabricWave4Test PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# FabricWave6Test -- wave 6 of the Godot parity plan (particles, decals, sprites): the pure mappings of GodotParticles.hpp and the converter on tests/fabric/data/Wave6Game, each
# written .ocparticle read back with the engine's parser. Runs the tool as a process, like FabricWave4Test.
if(TARGET Aver.Formats.Particles)
  add_executable(FabricWave6Test src/FabricWave6Test.cpp)
  target_link_libraries(FabricWave6Test PRIVATE Aver.Formats.Godot Aver.Formats.Particles Aver.Formats Aver.Particles Aver.Platform Aver.Core)
  fabric_needs_converter(FabricWave6Test)
  target_compile_definitions(FabricWave6Test PRIVATE
    AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
    AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
  set_target_properties(FabricWave6Test PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)
endif()

# FabricWave8Test -- wave 8a of the Godot parity plan (text and UI): the converter on tests/fabric/data/Wave8Game (a Label3D's ratings, a WOFF font converted, a WOFF2 font unsupported).
# Runs the tool as a process, like FabricWave6Test.
add_executable(FabricWave8Test src/FabricWave8Test.cpp)
target_link_libraries(FabricWave8Test PRIVATE Aver.Platform Aver.Core)
fabric_needs_converter(FabricWave8Test)
target_compile_definitions(FabricWave8Test PRIVATE
  AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
  AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
set_target_properties(FabricWave8Test PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# FabricGeometryTest -- G4 R7 (ArrayMesh, MultiMesh, trail meshes, CSG, Sprite3D, Label3D, GridMap) through the converter on tests/fabric/data/GeomGame.
# Runs the tool as a process, like FabricConvertTest.
add_executable(FabricGeometryTest src/FabricGeometryTest.cpp)
target_link_libraries(FabricGeometryTest PRIVATE Aver.Formats.Godot Aver.Formats Aver.Platform Aver.Core)
if(TARGET Aver.Formats.Material)
  target_link_libraries(FabricGeometryTest PRIVATE Aver.Formats.Material Aver.Render.PBR.Materials)
  target_compile_definitions(FabricGeometryTest PRIVATE AVER_FABRIC_TEST_OCMAT=1)
endif()
fabric_needs_converter(FabricGeometryTest)
target_compile_definitions(FabricGeometryTest PRIVATE
  AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
  AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
set_target_properties(FabricGeometryTest PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# FabricPlacedTest -- the placed visuals (the plain mesh placements a node's drawing rides on, so the editor shows it without playing) through the converter
# on tests/fabric/data/PlacedGame. Runs the tool as a process, like FabricConvertTest.
add_executable(FabricPlacedTest src/FabricPlacedTest.cpp)
target_link_libraries(FabricPlacedTest PRIVATE Aver.Formats.Godot Aver.Formats Aver.Platform Aver.Core)
fabric_needs_converter(FabricPlacedTest)
target_compile_definitions(FabricPlacedTest PRIVATE
  AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
  AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
set_target_properties(FabricPlacedTest PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# FabricModelImportTest -- a model's own import settings (nodes/root_scale, a material the import replaces) and a glTF's physics extension (OMI_physics_body /
# OMI_physics_shape: bodies and shapes) through the converter on tests/fabric/data/ModelGame.
add_executable(FabricModelImportTest src/FabricModelImportTest.cpp)
target_link_libraries(FabricModelImportTest PRIVATE Aver.Formats.Godot Aver.Formats Aver.Platform Aver.Core)
fabric_needs_converter(FabricModelImportTest)
target_compile_definitions(FabricModelImportTest PRIVATE
  AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
  AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
set_target_properties(FabricModelImportTest PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# FabricGodotCompareTest -- the converter checked against Godot itself (tools/godot-dump): a converted level mapped back to Godot space and compared with the
# JSON dump_scene.gd writes. By itself it converts tests/fabric/data/PlacedGame and compares the level with what Godot says, written out by hand, and with that answer
# made wrong in each way (no Godot needed). With the environment variable GODOT_EXE naming a Godot 4 editor binary it also runs Godot --headless on the fixture.
# The same binary is the command line for a whole project: FabricGodotCompareTest compare --converted <dir> --godot <exe> --godot-project <dir>.
add_executable(FabricGodotCompareTest src/FabricGodotCompareTest.cpp ${AVER_FABRIC_DIR}/native/tools/fabric/FabricProcess.cpp)
target_link_libraries(FabricGodotCompareTest PRIVATE Aver.Formats.Godot Aver.Formats Aver.Platform Aver.Core)
target_include_directories(FabricGodotCompareTest PRIVATE ${AVER_FABRIC_DIR}/native/tools/fabric ${AVER_ENGINE_SOURCE_DIR}/modules/world/include)
fabric_needs_converter(FabricGodotCompareTest)
target_compile_definitions(FabricGodotCompareTest PRIVATE
  AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
  AVER_FABRIC_DATA="${FABRIC_TEST_DATA}"
  AVER_GODOT_DUMP_SCRIPT="${AVER_FABRIC_DIR}/tools/godot-dump/dump_scene.gd")
set_target_properties(FabricGodotCompareTest PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# FabricAnimTest -- G5 A1 (AnimationPlayer) through the converter on tests/fabric/data/AnimGame: Animation sub-resources and an external .tres become
# .ocanim files with property tracks (transform tracks through the axis map), the player node keeps its libraries, and every track is rated.
add_executable(FabricAnimTest src/FabricAnimTest.cpp)
target_link_libraries(FabricAnimTest PRIVATE Aver.Formats.Godot Aver.Formats Aver.Platform Aver.Core)
fabric_needs_converter(FabricAnimTest)
target_compile_definitions(FabricAnimTest PRIVATE
  AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
  AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
set_target_properties(FabricAnimTest PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# FabricAudioTest -- G5 S1 (audio) through the converter on tests/fabric/data/AudioGame: sound files copied under Content/Audio, stream resources as Object(...) values, the audio
# nodes placed as their classes, the default AudioBusLayout as an .ocbus named by AUDIO.BUSLAYOUT, and every item rated (the Ogg / MP3 decoders, the effects, VideoStreamPlayer).
add_executable(FabricAudioTest src/FabricAudioTest.cpp)
target_link_libraries(FabricAudioTest PRIVATE Aver.Formats.Audio Aver.Formats.Godot Aver.Formats Aver.Audio Aver.Platform Aver.Core)
fabric_needs_converter(FabricAudioTest)
target_compile_definitions(FabricAudioTest PRIVATE
  AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
  AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
set_target_properties(FabricAudioTest PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# FabricNavTest -- G5 N1 (navigation) through the converter on tests/fabric/data/NavGame: regions, links, obstacles and agents placed as their classes, baked
# NavigationMesh resources as Object(...) values, a never-baked mesh flagged for a run-time bake, and every node and baking parameter rated.
add_executable(FabricNavTest src/FabricNavTest.cpp)
target_link_libraries(FabricNavTest PRIVATE Aver.Formats.Godot Aver.Formats Aver.Platform Aver.Core)
fabric_needs_converter(FabricNavTest)
target_compile_definitions(FabricNavTest PRIVATE
  AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
  AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
set_target_properties(FabricNavTest PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# FabricNetTest -- G6 NET2 (high-level multiplayer) through the converter on tests/fabric/data/NetGame: MultiplayerSpawner / MultiplayerSynchronizer placed as their classes, the
# SceneReplicationConfig sub-resource as an Object(...) value, the spawnable scene list as converted level paths, and every node, property and entry rated.
add_executable(FabricNetTest src/FabricNetTest.cpp)
target_link_libraries(FabricNetTest PRIVATE Aver.Formats.Godot Aver.Formats Aver.Platform Aver.Core)
fabric_needs_converter(FabricNetTest)
target_compile_definitions(FabricNetTest PRIVATE
  AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
  AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
set_target_properties(FabricNetTest PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# FabricUiTest -- G5 U1 (Control foundations) through the converter on tests/fabric/data/UiGame: the Control family placed as its classes, style boxes as Object(...) values,
# Theme resources as .octheme files the engine's parser reads, the project's stretch / theme / tooltip settings as UI.* keys, and every property rated.
add_executable(FabricUiTest src/FabricUiTest.cpp)
target_link_libraries(FabricUiTest PRIVATE Aver.UI Aver.Formats.Godot Aver.Formats Aver.Platform Aver.Core)
fabric_needs_converter(FabricUiTest)
target_compile_definitions(FabricUiTest PRIVATE
  AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
  AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
set_target_properties(FabricUiTest PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# FabricCanvas2DTest -- 2D scenes through the converter on tests/fabric/data/Menu2DGame: Node2D, Sprite2D and CanvasLayer placed as their classes with the engine's COMPONENT C2DTransform /
# CSprite2D / C2DLayer records (read back with the engine's parser), the tree order, hidden and transparent sprites, and the report.
add_executable(FabricCanvas2DTest src/FabricCanvas2DTest.cpp)
target_link_libraries(FabricCanvas2DTest PRIVATE Aver.Formats.Godot Aver.Formats Aver.Platform Aver.Core)
fabric_needs_converter(FabricCanvas2DTest)
target_compile_definitions(FabricCanvas2DTest PRIVATE
  AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
  AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
set_target_properties(FabricCanvas2DTest PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# FabricWidgetTest -- G5 U2 (widgets and rich text) through the converter on tests/fabric/data/WidgetGame: widgets placed as their classes, every property rated, LabelSettings /
# FontVariation / SystemFont / ButtonGroup / Shortcut as Object(...) values or one shared Resource(), and UI/Fonts.ocfonts the engine's font set parser reads.
add_executable(FabricWidgetTest src/FabricWidgetTest.cpp)
target_link_libraries(FabricWidgetTest PRIVATE Aver.UI Aver.Formats.Godot Aver.Formats Aver.Platform Aver.Core)
fabric_needs_converter(FabricWidgetTest)
target_compile_definitions(FabricWidgetTest PRIVATE
  AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
  AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
set_target_properties(FabricWidgetTest PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# FabricTreeTest -- G5 A2 (AnimationTree, skeleton, path and helper nodes) through the converter on tests/fabric/data/TreeGame: the tree's graph becomes one
# Object(...) value, the nodes are placed as their classes, curves and bones keep their data, and every node and property is rated.
add_executable(FabricTreeTest src/FabricTreeTest.cpp)
target_link_libraries(FabricTreeTest PRIVATE Aver.Formats.Godot Aver.Formats Aver.Platform Aver.Core)
fabric_needs_converter(FabricTreeTest)
target_compile_definitions(FabricTreeTest PRIVATE
  AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
  AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
set_target_properties(FabricTreeTest PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# FabricSkinTest -- wave 7 (skeleton, animation, ragdoll) through the converter on tests/fabric/data/SkinGame: a skinned glTF model's Skeleton3D bound to the
# importer's skeleton, its LINEAR / CUBICSPLINE / STEP animations as property clips on bone paths, PhysicalBone3D nodes written as the skeleton's .ocragdoll,
# and an Animation resource's markers saved with its clip. Runs the tool as a process, like FabricTreeTest.
add_executable(FabricSkinTest src/FabricSkinTest.cpp)
target_link_libraries(FabricSkinTest PRIVATE Aver.Formats.Godot Aver.Formats Aver.Platform Aver.Core)
fabric_needs_converter(FabricSkinTest)
target_compile_definitions(FabricSkinTest PRIVATE
  AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
  AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
set_target_properties(FabricSkinTest PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# FabricViewmodelTest -- Fabric step 9 of docs/plans/FIRST_PERSON.md on tests/fabric/data/ViewmodelGame: a viewmodel shader (own projection, POSITION.z depth push) is
# recognised and the meshes that draw with it (material_override, surface override, a mesh's own material, a model's import remap) get aver_first_person.
add_executable(FabricViewmodelTest src/FabricViewmodelTest.cpp)
target_link_libraries(FabricViewmodelTest PRIVATE Aver.Formats.Godot Aver.Formats Aver.Platform Aver.Core)
fabric_needs_converter(FabricViewmodelTest)
target_compile_definitions(FabricViewmodelTest PRIVATE
  AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
  AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
set_target_properties(FabricViewmodelTest PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# FabricCorpusPatternsTest -- the patterns two Godot demo projects showed, on tests/fabric/data/CorpusGame: children and overrides below the nodes of an
# instanced glTF model, Godot 3 key codes and joypad numbers, libraries/ properties, a curve's _limits. Runs the tool as a process, like FabricTreeTest.
add_executable(FabricCorpusPatternsTest src/FabricCorpusPatternsTest.cpp)
target_link_libraries(FabricCorpusPatternsTest PRIVATE Aver.Formats.Godot Aver.Formats Aver.Platform Aver.Core)
fabric_needs_converter(FabricCorpusPatternsTest)
target_compile_definitions(FabricCorpusPatternsTest PRIVATE
  AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
  AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
set_target_properties(FabricCorpusPatternsTest PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# FabricShaderTest -- G6 SH2 (ShaderMaterial over .gdshader: the shader written as a material graph, the material as GRAPHREF + FX + EMBREGS + TEX slots) through the
# converter on tests/fabric/data/ShaderGame. Runs the tool as a process, like FabricMaterialFxTest.
if(TARGET Aver.Formats.Material)
  add_executable(FabricShaderTest src/FabricShaderTest.cpp)
  target_link_libraries(FabricShaderTest PRIVATE Aver.Formats.Godot Aver.Formats Aver.Formats.Material Aver.Render.PBR.Materials Aver.RHI Aver.Platform Aver.Core)
  fabric_needs_converter(FabricShaderTest)
  target_compile_definitions(FabricShaderTest PRIVATE
    AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
    AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
  set_target_properties(FabricShaderTest PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)
endif()

# FabricEnvShaderTest -- G6 SH3 (sky, fog and particles shaders, GPUParticles3D, VisualShader) through the converter on tests/fabric/data/EnvShaderGame.
if(TARGET Aver.Formats.Material AND TARGET Aver.Formats.Particles)
  add_executable(FabricEnvShaderTest src/FabricEnvShaderTest.cpp)
  target_link_libraries(FabricEnvShaderTest PRIVATE Aver.Formats.Godot Aver.Formats Aver.Formats.Material Aver.Formats.Particles Aver.Render.PBR.Materials Aver.RHI Aver.Platform Aver.Core)
  fabric_needs_converter(FabricEnvShaderTest)
  target_compile_definitions(FabricEnvShaderTest PRIVATE
    AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>"
    AVER_FABRIC_DATA="${FABRIC_TEST_DATA}")
  set_target_properties(FabricEnvShaderTest PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)
endif()

# FabricBinaryTest -- binary .res / .scn (RSRC) through the converter: a binary ArrayMesh, BoxShape3D and GridMap scene built by the test (fixtures shared with
# tests/formats.godot/src/GodotBinaryTest.cpp), converted by the tool run as a process. No device, no .NET, no engine run.
add_executable(FabricBinaryTest src/FabricBinaryTest.cpp)
target_link_libraries(FabricBinaryTest PRIVATE Aver.Formats.Godot Aver.Formats Aver.Platform Aver.Core)
fabric_needs_converter(FabricBinaryTest)
target_compile_definitions(FabricBinaryTest PRIVATE AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>")
set_target_properties(FabricBinaryTest PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# FabricConverterBugsTest -- the converter bugs of docs/plans/GODOT_PARITY.md section 3.4 that live in the scene and project converter (C1, C12 to C20): script
# class names, binary resources found by their magic, PhysicsMaterial, a uid in a setting, editor-only settings, Jolt limits, a Radiance .hdr, InputEventAction in a
# Shortcut, key modifiers and wheel notches, and the per-instance rendering flags. The project is built by the test; the tool is run as a process.
add_executable(FabricConverterBugsTest src/FabricConverterBugsTest.cpp)
target_link_libraries(FabricConverterBugsTest PRIVATE Aver.Formats.Godot Aver.Formats Aver.Platform Aver.Core)
if(TARGET Aver.Formats.Material)
  target_link_libraries(FabricConverterBugsTest PRIVATE Aver.Formats.Material)
  target_compile_definitions(FabricConverterBugsTest PRIVATE AVER_FABRIC_TEST_OCMAT=1)
endif()
fabric_needs_converter(FabricConverterBugsTest)
target_compile_definitions(FabricConverterBugsTest PRIVATE AVER_ASSETC_EXE="$<TARGET_FILE:AverAssetC>")
set_target_properties(FabricConverterBugsTest PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)
