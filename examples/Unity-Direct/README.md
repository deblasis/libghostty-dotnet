# Unity-Direct

Pure C# integration of libghostty into Unity 6 (URP). No C++ native plugin required.

## Approach

This example calls libghostty's C API directly from C# via P/Invoke, then uses D3D11 COM vtable calls to copy ghostty's render texture into a Unity `Texture2D` each frame.

The frame loop:

1. `ghostty_app_tick()` processes ghostty events
2. `ghostty_surface_get_d3d11_texture()` gets ghostty's current render target
3. `CopyResource` copies it to a D3D11 staging texture (created on ghostty's device)
4. `Map` + `Buffer.MemoryCopy` reads pixels into `Texture2D.GetRawTextureData()`
5. `Texture2D.Apply()` uploads to the GPU

## Tradeoffs

**Pros:**
- Zero native build dependencies beyond `ghostty.dll`
- All code is C# -- easy to modify, debug, and understand
- No CMake, no Visual Studio C++ toolchain needed

**Cons:**
- CPU readback every frame (staging texture Map/Unmap)
- Unsafe C# code for D3D11 vtable pointer arithmetic
- Higher per-frame overhead than GPU-to-GPU texture sharing

## Requirements

- Unity 6 LTS (6000.x) with Universal Render Pipeline
- Windows x86_64
- `ghostty.dll` in `Assets/Plugins/Ghostty/x86_64/` (run `just copy-dll` from the repo root)
- Project Settings: Allow unsafe code, .NET Standard 2.1, Input System package

## Project Structure

```
Assets/
  Plugins/Ghostty/       C# plugin (GhosttyNative, GhosttyTexture, GhosttyInput, GhosttyTerminal)
  Editor/                GhosttyEditorWindow (dockable + floating terminal)
  Runtime/NostromoConsole/  CRT shader, scene scripts, materials
  Scenes/                Demo scenes
```
