# Fabric Connector

An extension for the [Aver Engine](https://github.com/Vectoric-Core-Systems/Aver-Engine) that converts a Godot 4 project into an Aver project: scenes, assets, GDScript and GodotSharp, and compiled GDExtension plugins. User guide: [docs/FABRIC.md](docs/FABRIC.md). How an extension plugs into the engine: [EXTENSIONS.md](https://github.com/Vectoric-Core-Systems/Aver-Engine/blob/main/docs/EXTENSIONS.md) in the engine repository. The design of the extension and of this repository: [docs/plans/EXTENSION.md](docs/plans/EXTENSION.md).

```
extension.json     the manifest the engine's loader reads
CMakeLists.txt     top level: builds the engine and this extension together (cmake/Standalone.cmake); added by an engine: the extension's targets (cmake/Extension.cmake)
native/            Aver.Fabric.Native, one SHARED library
  formats.godot/     the Godot text and binary readers, the shading-language front end
  gdext/             the GDExtension host (the aver_gdext_* exports the C# runtime P/Invokes)
  tools/fabric/      the converter
  src/               aver_extension_entry (the extension ABI) and the stb_image_write implementation
managed/           Aver.Fabric (the runtime the host loads and converted scripts compile against), Aver.Fabric.Convert (the script converter), Aver.Fabric.Tests
tests/             fabric (the converter end to end, the GDExtension host) and formats.godot, with their fixtures
test-content/      the Aver-side levels and meshes the runtime tests load
tools/             godot-dump: the converter checked against a headless Godot 4 (dump_scene.gd and runner scripts; see its README)
third_party/       godot-gdextension: Godot's published GDExtension interface header and API dump (MIT)
docs/              FABRIC.md (user guide), plans/ (design and contracts), api/Aver.Fabric.md
```

## Build

The extension is built against an Aver Engine, which this repository does not contain. Needed: CMake 3.24 or later, Ninja (or another generator), a C++20 compiler (MSVC, GCC, Clang or Apple Clang) and the .NET SDK in `global.json`. The engine's own requirements apply (on Linux its SDL build dependencies, see `.github/workflows/ci.yml`).

Against an engine checkout:

```
cmake -S . -B build -G Ninja -DAVER_ENGINE_DIR=<path to an Aver Engine checkout>
cmake --build build
```

Against the engine commit this extension is pinned to (`AVER_ENGINE_REF`, one line in `cmake/Standalone.cmake`), fetched from GitHub:

```
cmake -S . -B build -G Ninja
cmake --build build
```

The engine is configured in `build/engine` (no editor, no runtime host, no engine tests) with this extension added to it, and only what the extension and its tests use is built. The result is in `build/engine/bin/Extensions/FabricConnector/`: `extension.json`, `native/<platform>/Aver.Fabric.Native.*`, `managed/Aver.Fabric.dll`, `tools/Aver.Fabric.Convert.dll` (with Roslyn; never shipped in a game) and `data/extension_api.json`, beside the engine's `AverAssetC` in `build/engine/bin`. Convert a project with:

```
build/engine/bin/AverAssetC fabric <godot-project-dir> <out-dir>
```

Options: `AVER_ENGINE_DIR`, `AVER_ENGINE_REF`, `AVER_ENGINE_REPO`, `AVER_ENGINE_CMAKE_ARGS` (more `-D` options for the engine's configure), `AVER_FABRIC_BUILD_TESTS` (default ON), `FABRIC_DEPLOY_TO` (below).

### Use it with your own engine build

`FABRIC_DEPLOY_TO` (a path, empty by default) copies the built extension folder (`extension.json`, `native/<platform>/`, `managed/`, `tools/`, `data/`) to `<FABRIC_DEPLOY_TO>/Extensions/FabricConnector` at the end of every build. Point it at the `bin` folder of the engine build you work in and the extension sits beside that build's editor and `AverAssetC`:

```
cmake -S . -B build -G Ninja -DAVER_ENGINE_DIR=<engine checkout> -DFABRIC_DEPLOY_TO=<engine checkout>/build/bin
cmake --build build
<engine checkout>/build/bin/AverAssetC fabric <godot-project-dir> <out-dir>
```

The copy merges into the folder (a file of the same name is replaced, nothing is deleted). Close the editor and any running `AverAssetC` first: on Windows they hold the extension's library open and that one file cannot be replaced. The number of parallel jobs is the build tool's default, or `CMAKE_BUILD_PARALLEL_LEVEL`.

## Test

```
ctest --test-dir build --output-on-failure -C Debug
```

runs the C++ suites (`tests/fabric`, `tests/formats.godot`) and `FabricManagedTests`, which is `dotnet run` in `managed/Aver.Fabric.Tests`. The same tests run from `build/engine`. Suites that need a GPU or a device are not part of this repository. `dotnet run -c Debug` in `managed/Aver.Fabric.Tests` also works by hand: the last configure writes `managed/fabric-build.props` (not tracked) with the engine checkout and bin folder, and `managed/Directory.Build.props` reads it; `-p:AverEngineDir=...`, `-p:AverBinDir=...` or the `AVER_ENGINE_DIR` and `AVER_BIN_DIR` variables override it.

## Releases

A release will be one `.avext` file per platform set (the extension folder zipped; `pack-avext.ps1` and the engine's Extensions panel are phase 3 of the plan, not built yet). Until then the build output above is the installable folder: copy `Extensions/FabricConnector/` next to an engine's executables.

License: LGPL-2.1 (see `LICENSE`).
