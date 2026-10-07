# Changelog

## 0.4.0-rc1

- Replaced the SkunkVision RenderHook dependency with an independent Decal/Direct3D9 rendering path.
- Brightness is applied at Decal RenderPreUI, after the 3D scene and before the AC interface.
- Removed all Direct3D vtable patching and VirtualProtect usage.
- Removed RenderHook.dll and Interop.RenderHookLib.dll requirements.
- Preserved the existing /acgamma command set and settings-file format.
- Added Direct3D state capture/restore around the brightness pass.
- Reduced per-frame overhead by caching device delegates and reusing the state block and unmanaged quad buffer.
- Verified numeric levels, on/off, up/down, reset, persistence, relog, multiple clients and fullscreen/windowed transitions.

## 0.3.0

- First release-ready standalone build.
- Command-only world brightness control from level 0 through 25.
- Brightens the AC 3D world without changing the 2D UI or Windows desktop.
- Saves and restores the selected level.
- Used the historical SkunkVision RenderHook component.
