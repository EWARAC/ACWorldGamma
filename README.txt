AC WORLD GAMMA
Version 0.4.0-rc1
Publisher: EWARAC

PURPOSE
-------
Adjusts the brightness of the Asheron's Call 3D world without changing the
2D interface or Windows desktop gamma.

COMMANDS
--------
/acgamma on
/acgamma off
/acgamma 0-25
/acgamma up
/acgamma down
/acgamma reset
/acgamma status
/acgamma help

SETTINGS
--------
Documents\Decal Plugins\AC World Gamma\Settings.txt

The saved setting is shared by AC clients. Existing running clients retain their
current level; new or reloaded clients read the latest saved value.

TECHNICAL
---------
This version uses Decal's render-sink interface to obtain AC's live
IDirect3DDevice9 and applies brightness at RenderPreUI, after the 3D scene and
before AC draws its interface.

No RenderHook.dll is required.
No Interop.RenderHookLib.dll is required.
No Direct3D vtable patching is used.
No Windows desktop gamma is changed.
