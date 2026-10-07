# Third-party build files

The release builder expects these two files in this directory:

- `RenderHook.dll`
- `Interop.RenderHookLib.dll`

These files are **third-party components** used by AC World Gamma.

`RenderHook.dll` is the RenderHook component from **SkunkVision**, originally
developed by **Greg Kusnick (`gkusnick`) / SkunkWorks**.

`Interop.RenderHookLib.dll` is the generated managed COM interop assembly for
that RenderHook type library.

Original project:

- https://sourceforge.net/projects/skunkworks/
- https://sourceforge.net/p/skunkworks/code/HEAD/tree/SkunkVision/trunk/RenderHook/

SourceForge currently lists the SkunkWorks project under the **MIT License**.

These files are bundled in the tested release installer so AC World Gamma does
not require SkunkVision itself to be installed. Full attribution and the MIT
permission notice are in `../THIRD_PARTY_NOTES.txt`.
