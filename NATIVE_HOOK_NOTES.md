# Native light hook provenance and design

This document records the origin and design of the experimental AC World Gamma
native light hook introduced on the `native-light-hook` branch.

## Purpose

The goal is to remove the runtime and build-time dependency on the historical
SkunkVision RenderHook component while preserving AC World Gamma's behaviour:
adjust the brightness of the Asheron's Call 3D world without changing the 2D UI
or Windows desktop gamma.

## Independent implementation

The prototype in `Direct3DLightHook.cs` was written specifically for
AC World Gamma.

It does not call, link to, embed, or require:

- `RenderHook.dll`
- `Interop.RenderHookLib.dll`
- the SkunkVision `ISVRenderHook` COM interface
- the SkunkVision RenderHook CLSID or IID

The stable v0.3.0 implementation remains separately documented in
`THIRD_PARTY_NOTES.txt`.

## Public interfaces used

The prototype is based on public platform/interface behaviour:

1. Decal exposes its active renderer through `Host.Render`.
2. Decal.Adapter's `RenderServiceWrapper` exposes `UnsafeDevice` as an
   `IUnknown`-marshalled object.
3. The Direct3D9 `IDirect3DDevice9` COM interface has a documented method order,
   including `SetLight`.
4. Windows `VirtualProtect` permits changing the protection of the vtable slot
   while the replacement function pointer is installed/restored.

## Hook behaviour

The prototype intercepts only `IDirect3DDevice9::SetLight`.

When AC World Gamma is off, the light structure is forwarded unchanged.

When enabled, the RGB values of the light's Diffuse and Ambient colour values
are blended toward white according to the selected AC World Gamma level before
the call is forwarded to the original Direct3D function.

The implementation does not modify:

- textures
- HUD drawing
- chat
- radar
- inventory
- desktop gamma
- display calibration

## Safety behaviour

The hook:

- stores the original SetLight function pointer before replacing it
- keeps the managed callback delegate alive for the lifetime of the hook
- catches managed exceptions before they can cross the unmanaged Direct3D call
- restores the original pointer on plugin shutdown
- restores only when the vtable slot still points to this implementation, so it
  does not overwrite another component that may have subsequently hooked it

## Status

This is an experimental implementation and must be validated in live AC before
it replaces the stable v0.3.0 rendering component.

Required tests include:

- initial load
- level 0/off equivalence to normal AC
- low and high brightness levels
- repeated on/off changes
- windowed mode
- fullscreen mode
- graphics reset / mode transition
- logout and login
- multiple AC clients in separate processes

Only after those tests pass should the old RenderHook dependency be removed from
the release branch.
