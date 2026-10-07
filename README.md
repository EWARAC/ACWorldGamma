# AC World Gamma — native light hook prototype

This branch contains an **experimental independent replacement** for the
SkunkVision RenderHook dependency used by AC World Gamma v0.3.0.

The stable `main` branch remains unchanged apart from the previously added
third-party attribution.

## What changes in this branch

The prototype:

- does **not** reference `Interop.RenderHookLib.dll`
- does **not** load or ship `RenderHook.dll`
- does **not** use the SkunkVision RenderHook COM interface
- obtains Decal's exposed Direct3D device from `Host.Render.UnsafeDevice`
- independently hooks the documented `IDirect3DDevice9::SetLight` method
- restores the original SetLight function pointer when the plugin shuts down

The new implementation lives in:

```text
Direct3DLightHook.cs
```

## Version

Experimental build:

```text
0.4.0-alpha1
```

This build is intentionally separate from the release-ready v0.3.0 build until
the new rendering path has been tested in live Asheron's Call sessions.

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

## Building

Requirements:

- Windows
- Decal 3.0
- .NET Framework 4.x compiler
- 32-bit/x86 target

No SkunkVision or RenderHook binaries are required to build this branch.

Run:

```text
BUILD RELEASE.cmd
```

The experimental installer is written to:

```text
release\AC World Gamma Native Test v0.4.0-alpha1.exe
```

## Installation safety

The test installer uses the same Decal plugin GUID as the stable plugin so
Decal will not load both lighting hooks at the same time.

The test build installs to a separate folder:

```text
C:\Games\Decal Plugins\AC World Gamma Native Test
```

The stable v0.3.0 files are therefore not overwritten. Reinstalling the stable
v0.3.0 installer restores the stable registration.

## Provenance

The code in `Direct3DLightHook.cs` was written specifically for AC World Gamma
around the documented Direct3D9 COM interface and Windows memory protection
APIs.

It does not contain or require the SkunkVision RenderHook implementation.

The stable v0.3.0 branch continues to retain explicit attribution for the
third-party SkunkVision RenderHook component it uses.

See `NATIVE_HOOK_NOTES.md` for more detail.

## Test status

Not yet release-ready.

The first live tests should verify:

- plugin loads without RenderHook.dll present
- `/acgamma off` leaves normal world lighting unchanged
- brightness levels visibly affect only the 3D world
- repeated on/off and level changes are stable
- windowed mode
- fullscreen mode
- logout/login
- multiple AC clients in separate processes
