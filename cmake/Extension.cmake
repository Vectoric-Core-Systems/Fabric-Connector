# The extension's targets, in an Aver Engine's build (the engine is the top-level project: CMAKE_SOURCE_DIR is its checkout and CMAKE_BINARY_DIR its
# build). Included by the root CMakeLists.txt when an engine adds this repository: the standalone build (cmake/Standalone.cmake) passes
# AVER_EXTERNAL_EXTENSIONS to the engine's configure, and an engine tree could add it the same way.
#
# The output is bin/Extensions/FabricConnector/ in the engine's build: extension.json, native/<platform>/Aver.Fabric.Native.*, managed/, tools/ and
# data/. Nothing else in the engine's build names one of these targets: it reaches the extension only through its loader (modules/extensions).
set(AVER_ENGINE_SOURCE_DIR ${CMAKE_SOURCE_DIR})
set(AVER_FABRIC_DIR ${CMAKE_CURRENT_SOURCE_DIR})
set(AVER_FABRIC_STAGE ${CMAKE_BINARY_DIR}/bin/Extensions/FabricConnector)
if(NOT DEFINED AVER_FABRIC_BUILD_TESTS)
  set(AVER_FABRIC_BUILD_TESTS ON)
endif()

add_subdirectory(managed)   # before native: its targets are what the library's completeness depends on
add_subdirectory(native)
if(AVER_FABRIC_BUILD_TESTS)
  add_subdirectory(tests/formats.godot)
  add_subdirectory(tests/fabric)   # FabricConvertTest and the rest: AverAssetC fabric (through the loader) end to end
  # The engine's walk (aver_register_ctest_in, at the end of its CMakeLists.txt) registers every executable whose name ends in Test, with the
  # engine's timeout, failure pattern and skip code, in every directory when AVER_BUILD_TESTS is on - these two included. With it off the walk does
  # not run, so it is called for them here, once the engine has defined it (the end of the engine's own directory). A deferred call expands its
  # variables when it runs, in the engine's directory, so EVAL expands them now.
  if(NOT AVER_BUILD_TESTS)
    foreach(_aver_fabric_dir tests/formats.godot tests/fabric)
      cmake_language(EVAL CODE "cmake_language(DEFER DIRECTORY [[${CMAKE_SOURCE_DIR}]] CALL aver_register_ctest_in [[${CMAKE_CURRENT_SOURCE_DIR}/${_aver_fabric_dir}]])")
    endforeach()
  endif()
endif()

# What `cmake --build --target Aver.Fabric.All` builds: the extension, its tests and the engine pieces they use (AverAssetC, the ABI libraries the
# managed suites load), and nothing else of the engine.
add_custom_target(Aver.Fabric.All)
foreach(_aver_fabric_target AverAssetC Aver.Fabric.Native Aver.Fabric.Managed Aver.Fabric.Convert Aver.Fabric.TestLibs)
  if(TARGET ${_aver_fabric_target})
    add_dependencies(Aver.Fabric.All ${_aver_fabric_target})
  endif()
endforeach()
if(AVER_FABRIC_BUILD_TESTS)
  foreach(_aver_fabric_dir tests/formats.godot tests/fabric)
    get_property(_aver_fabric_tests DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/${_aver_fabric_dir}" PROPERTY BUILDSYSTEM_TARGETS)
    foreach(_aver_fabric_target IN LISTS _aver_fabric_tests)
      add_dependencies(Aver.Fabric.All ${_aver_fabric_target})
    endforeach()
  endforeach()
endif()
set_target_properties(Aver.Fabric.All PROPERTIES FOLDER "extensions")
