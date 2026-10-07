AC WORLD GAMMA NATIVE TEST
Version 0.4.0-alpha1
Publisher: EWARAC

PURPOSE
-------
Experimental build of AC World Gamma using an independently written Direct3D9
light hook. This branch does not require RenderHook.dll or
Interop.RenderHookLib.dll.

COMMANDS
--------
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

LEVELS
------
0  = normal Asheron's Call world lighting
1  = smallest extra world-light level
25 = maximum

The selected level is saved and restored the next time AC starts.

INSTALLATION
------------
Close Asheron's Call and run:

  AC World Gamma Native Test v0.4.0-alpha1.exe

Default test install location:

  C:\Games\Decal Plugins\AC World Gamma Native Test

IMPORTANT
---------
This installer uses the same Decal plugin GUID as the stable AC World Gamma
release so Decal loads the test build instead of loading both hooks at once.

The stable v0.3.0 files are not overwritten because the test build installs
into a separate folder.

FIRST TEST
----------
Start AC normally and type:

  /acgamma status

Then try:

  /acgamma 1
  /acgamma 5
  /acgamma off

Only the 3D world should change brightness.

TECHNICAL NOTE
--------------
The test build obtains Decal's exposed Direct3D9 device through
Host.Render.UnsafeDevice and independently intercepts the documented
IDirect3DDevice9::SetLight entry.

No SkunkVision RenderHook code, COM interface, DLL, or generated interop assembly
is required by this build.

See NATIVE_HOOK_NOTES.md for implementation provenance and design notes.
