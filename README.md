# AC World Gamma

AC World Gamma changes the brightness of the **Asheron's Call 3D world only**.
The AC user interface, chat, panels and Windows desktop gamma are left unchanged.

## Version

Current release: **v0.4.0**

## Commands

```text
/acgamma on
/acgamma off
/acgamma 0-25
/acgamma up
/acgamma down
/acgamma reset
/acgamma status
/acgamma help
```

Level 0 is normal AC rendering. Level 25 is the maximum added brightness.

Settings are stored in:

```text
Documents\Decal Plugins\AC World Gamma\Settings.txt
```

The setting is shared by AC clients. Each running client keeps its current level;
a new or reloaded client reads the latest saved setting.

## Rendering design

v0.4.0 no longer uses the historical SkunkVision RenderHook component.

The plugin:

- registers with Decal's public `IInjectService` / `IRender3DSink` interface
- receives the live `IDirect3DDevice9` used by AC
- uses Decal's `RenderPreUI` event to run after the 3D scene and before the UI
- draws one alpha-blended white fullscreen quad to brighten only the 3D view
- captures and restores Direct3D state around that draw
- caches Direct3D delegates and reuses a state block to reduce per-frame overhead

It does **not**:

- load `RenderHook.dll`
- reference `Interop.RenderHookLib.dll`
- patch or replace Direct3D vtable entries
- use `VirtualProtect`
- change Windows desktop gamma

## Build

Requirements:

- Windows
- Decal 3.0
- .NET Framework 4.x
- x86 target

Run:

```text
BUILD RELEASE.cmd
```

Output:

```text
build\ACWorldGamma.dll
```

## v0.4.0 validation

Verified in Asheron's Call:

- numeric brightness levels
- on/off
- up/down
- reset
- saved setting reload
- UI remains unchanged
- character logout/login
- multiple simultaneous AC clients
- per-process live brightness changes
- fullscreen/windowed mode transition
- rendering continues correctly after the display-mode transition

This branch contains the tested v0.4.0 release implementation.
