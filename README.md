# libghostty-dotnet

.NET examples and interop bindings for [libghostty windows soft fork](https://github.com/deblasis/ghostty).

This repo also serves as a **visual testing ground**: an automated test suite launches each example, sends input, resizes windows, runs commands, and verifies correct rendering across DPI modes using screenshot comparison.

## Prerequisites

- [Zig](https://ziglang.org/download/) (0.15+): builds libghostty from source
- [.NET SDK 9.0](https://dotnet.microsoft.com/download/dotnet/9.0): builds and runs examples
- [ClangSharp](https://github.com/dotnet/ClangSharp): regenerates bindings when the C API changes

### Install on Windows

```powershell
# winget
winget install zig.zig
winget install Microsoft.DotNet.SDK.9

# or choco
choco install zig dotnet-sdk

# ClangSharp (dotnet global tool)
dotnet tool install --global ClangSharpPInvokeGenerator
```

## Setup

```powershell
./setup.ps1
```

This clones and builds libghostty from source, then restores .NET packages.
After setup, open any example `.slnx` in Visual Studio or run with `dotnet run`.

## Examples

| Example | Description | Status |
|---------|-------------|--------|
| [Win32](examples/Win32/) | Raw Win32 P/Invoke, direct port of the C example | Done |
| [WinForms](examples/WinForms/) | Panel-based embedding with WinForms events | Done |
| [WPF-Simple](examples/WPF-Simple/) | HwndHost embedding with GhosttyApp wrapper | Done |
| [WPF-Direct](examples/WPF-Direct/) | HwndHost embedding with raw NativeMethods | Done |
| [WinUI 3](examples/WinUI3/) | SwapChainPanel composition surface ([#3](https://github.com/deblasis/libghostty-dotnet/issues/3)) | Done |
| [SharedTexture](examples/SharedTexture/) | Offscreen rendering to shared DXGI texture (WinForms readback) | Done |
| [Unity-Direct](examples/Unity-Direct/) | Unity 6 (URP) integration, pure C# with D3D11 staging texture readback | WIP |
| [Unity-Bridge](examples/Unity-Bridge/) | Unity 6 (URP) integration, C++ native plugin with zero-copy GPU texture sharing | WIP |
| Avalonia | Cross-platform NativeControlHost | Planned |

## Visual Testing

The test suite uses [FlaUI](https://github.com/FlaUI/FlaUI) for UI automation and [ImageSharp](https://github.com/SixLabors/ImageSharp) for screenshot comparison.

### Test coverage

| Feature | Tested | Notes |
|---------|--------|-------|
| App launch | ✅ | Window appears within timeout |
| Window title | ✅ | Not empty |
| Window size | ✅ | Valid dimensions, reasonable bounds |
| Terminal renders | ✅ | Screenshot is not blank |
| Clean shutdown | ✅ | WM_CLOSE, exit code 0, no crash dialog |
| Keyboard input (typing) | ✅ | Visible output after keystrokes |
| Enter executes command | ✅ | Screen changes after Enter |
| Backspace | ✅ | Visible change after deleting characters |
| Window resize | ✅ | Terminal updates, two sizes compared |
| Minimum window size | ✅ | Shrink to 320x240, no crash |
| Focus/cursor | ✅ | Cursor visible when focused |
| Command execution | ✅ | `echo` output, prompt returns |
| Scrollback | ✅ | Shift+PageUp after generating output |
| Clipboard copy/paste | ✅ | Select, copy, type, paste cycle |
| Long-running commands | ✅ | Output updates over time (`ping`) |
| DPI: Unaware mode | ✅ | Launches and renders |
| DPI: SystemAware mode | ✅ | Launches and renders |
| DPI: PerMonitorV2 mode | ✅ | Launches and renders |
| DPI: mode affects rendering | ✅ | Screenshots differ across modes (high-DPI displays) |
| DPI: window reports value | ✅ | `GetDpiForWindow` >= 96 |
| Unicode/emoji input | ❌🔨 | |
| Mouse click | ❌🔨 | `SendMouseButton` API exists |
| Mouse selection (drag) | ❌🔨 | |
| Mouse scroll (wheel) | ❌🔨 | `SendMouseScroll` API exists |
| Ctrl+C (interrupt) | ❌🔨 | |
| Tab completion | ❌🔨 | |
| ANSI colors/formatting | ❌🔨 | Verify colored output renders differently |
| Cursor styles | ❌🔨 | Block, beam, underline |
| Window maximize/restore | ❌🔨 | |
| Window minimize/restore | ❌🔨 | |
| Multi-monitor (move between) | ❌🔨 | DPI change on move |
| Fullscreen toggle | ❌🔨 | |
| Content scale changes | ❌🔨 | `SetContentScale` API exists |
| Occlusion handling | ❌🔨 | `SetOcclusion` API exists |
| Modifier keys (Ctrl, Alt, Shift) | ❌🔨 | Key combos beyond clipboard |
| Rapid input (stress) | ❌🔨 | Fast typing, no dropped keys |
| Rapid resize (stress) | ❌🔨 | Continuous resize, no crash |
| Resource cleanup | ❌🔨 | No handle/memory leaks after close |
| Selection clipboard | ❌🔨 | `supports_selection_clipboard` in config |
| Surface close callback | ❌🔨 | Terminal-initiated close |
| Multiple surfaces | ❌🔨 | More than one terminal per app |
| Config loading | ❌🔨 | `ghostty_config_load_default_files` |

```powershell
# Run all visual tests
just test-visual

# Smoke tests only (fast)
just ci-test-smoke

# Full CI pipeline (build + all tests)
just ci

# Update screenshot baselines after intentional visual changes
just update-baselines
```

Tests are parameterized across all examples. Adding a new example to `TestConfiguration.AllExamples` automatically includes it in every test.

On workstations, tests use aggressive focus management to handle other windows competing for foreground. In CI (`CI` env var set), focus handling is lightweight since no contention exists.

## Updating libghostty

```powershell
# Use custom repo/branch/commit
./setup.ps1 -Repo https://github.com/deblasis/ghostty.git -Branch windows -Commit abc123

# Save the override to libghostty.json
./setup.ps1 -Repo https://github.com/deblasis/ghostty.git -Branch windows -Commit abc123 -Save
```

## Regenerating bindings

When the C API changes:

```powershell
./generate-bindings.ps1
```

Requires ClangSharp: `dotnet tool install --global ClangSharpPInvokeGenerator`
