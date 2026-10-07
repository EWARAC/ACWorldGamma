# AC World Gamma

A lightweight Decal plugin for **Asheron's Call** that adjusts the brightness of the **3D world** without changing the game UI, chat, radar, inventory, spell bars, plugin windows, or the Windows desktop.

## v0.3.0

AC World Gamma v0.3.0 is the first release-ready standalone build.

It has been tested successfully in both **windowed** and **fullscreen** Asheron's Call. The selected level is saved and restored when AC starts again.

## Commands

```text
/acgamma on
/acgamma off
/acgamma 0
/acgamma 1
...
/acgamma 25
/acgamma up
/acgamma down
/acgamma reset
/acgamma status
/acgamma help
```

- `0` = normal Asheron's Call world lighting
- `1` = smallest extra brightness level
- `25` = maximum

## Installation

1. Close Asheron's Call.
2. Download **AC World Gamma Setup v0.3.0.exe** from the Releases page.
3. Run the installer.
4. Start AC normally.
5. Type `/acgamma status`.

Default install location:

```text
C:\Games\Decal Plugins\AC World Gamma
```

## Building from source

Requirements:

- Windows
- Decal 3.0
- .NET Framework 4.x compiler
- 32-bit/x86 target
- `RenderHook.dll` and `Interop.RenderHookLib.dll` in the project's `ThirdParty` folder

Run:

```text
BUILD RELEASE.cmd
```

The release installer is written to:

```text
release\AC World Gamma Setup v0.3.0.exe
```

## Technical note

AC World Gamma loads its private rendering component directly and does not require SkunkVision to be installed, enabled, or registered at runtime.

The brightness control uses the old SkunkVision world-light rendering technique rather than changing the Windows desktop gamma. This is why the AC interface remains unchanged.

See `THIRD_PARTY_NOTES.txt` for attribution and licensing information.

## Tested

v0.3.0 has been verified to:

- install and load through Decal
- preserve the selected brightness level between sessions
- switch cleanly between normal and adjusted world lighting
- alter the 3D world without altering the 2D UI
- work in windowed mode
- work in fullscreen mode
- run without SkunkVision installed
- rebuild successfully without SkunkVision installed
