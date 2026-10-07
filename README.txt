AC WORLD GAMMA
Version 0.3.0
Publisher: EWARAC

PURPOSE
-------
AC World Gamma adds command-controlled brightness to the Asheron's Call 3D world
without changing the brightness of the game interface, chat, radar, inventory,
spell bars, plugin windows, or the Windows desktop.

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

  AC World Gamma Setup v0.3.0.exe

Default install location:

  C:\Games\Decal Plugins\AC World Gamma

FIRST TEST
----------
Start AC normally and type:

  /acgamma status

Then try:

  /acgamma 1
  /acgamma off

Only the 3D world should change brightness.

UNINSTALL
---------
Use Windows Installed Apps / Programs and Features and remove:

  AC World Gamma

TECHNICAL NOTE
--------------
AC World Gamma includes the native rendering component required to alter
AC's world lighting. It does not require another plugin to be installed or
enabled at runtime.

See THIRD_PARTY_NOTES.txt for attribution and licensing information.
