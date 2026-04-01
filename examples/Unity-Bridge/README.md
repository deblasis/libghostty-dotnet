# Unity-Bridge

C++ native plugin integration of libghostty into Unity 6 (URP). The GPU interop stays in native code where it belongs.

## Approach

A C++ plugin (`GhosttyBridge.dll`) owns the entire libghostty lifecycle and D3D11 texture sharing. It captures Unity's D3D11 device via `UnityPluginLoad`, creates the ghostty surface in shared texture mode, and opens the DXGI shared handle directly on Unity's device. C# gets back an `ID3D11ShaderResourceView*` that it passes to `Texture2D.CreateExternalTexture`.

The frame loop:

1. C# calls `GhosttyBridge_Tick()` which runs `ghostty_app_tick()`
2. Ghostty renders to its shared texture
3. Unity samples the SRV in its render pass -- zero-copy, GPU-to-GPU

## Tradeoffs

**Pros:**
- Zero-copy texture sharing (no CPU readback)
- GPU interop handled natively in C++ (no vtable hacks from managed code)
- Lower per-frame overhead
- Thin, clean C# layer

**Cons:**
- Requires building `GhosttyBridge.dll` (CMake + C++ toolchain)
- Two DLLs to manage (`ghostty.dll` + `GhosttyBridge.dll`)
- Harder to debug (native + managed boundary)

## Building GhosttyBridge.dll

```powershell
cd examples/Unity-Bridge/Assets/Plugins/GhosttyBridge
cmake -B build -A x64
cmake --build build --config Release
```

This outputs `GhosttyBridge.dll` to `Assets/Plugins/Ghostty/x86_64/`.

Requires `ghostty.lib` (import library) at `Assets/Plugins/Ghostty/x86_64/ghostty.lib`. Copy it from `libghostty/lib/ghostty.lib` after running `setup.ps1`.

## Requirements

- Unity 6 LTS (6000.x) with Universal Render Pipeline
- Windows x86_64
- Visual Studio or CMake + C++ toolchain (for building the bridge DLL)
- `ghostty.dll` in `Assets/Plugins/Ghostty/x86_64/` (run `just copy-dll` from the repo root)
- Project Settings: Allow unsafe code, .NET Standard 2.1, Input System package

## Project Structure

```
Assets/
  Plugins/
    Ghostty/             C# wrapper (GhosttyBridge.cs, GhosttyTerminal, GhosttyKeyMap)
      x86_64/            ghostty.dll + GhosttyBridge.dll (runtime)
    GhosttyBridge/       C++ source + CMakeLists.txt
  Editor/                GhosttyEditorWindow (dockable + floating terminal)
  Runtime/NostromoConsole/  CRT shader, scene scripts, materials
  Scenes/                Demo scenes
```
