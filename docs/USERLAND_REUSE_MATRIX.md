# Userland reuse matrix

The Legacy and UEFI userland is recognizably guideXOS, but the active implementation is a monolithic managed desktop with global state and direct kernel access. The new project should preserve the identity, behavior, assets, and application ideas while moving the UI behind public OS services.

| Area | Donor | Strategy | Coupling or risk to remove |
|---|---|---|---|
| Desktop shell | Legacy/UEFI `guideXOS\GUI\Desktop.cs` | Reimplement from behavior | Direct access to `Program`, `Framebuffer`, global images, filesystem state, and fixed assumptions. |
| Window manager | Legacy/UEFI `WindowManager.cs`, `Window.cs`, `WindowBase.cs` | Adapt in a new compositor-facing layer | Global window collections, direct framebuffer drawing, static event state, and allocation patterns. |
| Controls | Legacy/UEFI `GUI\Widgets`, `Control.cs`, buttons/dialogs | Adapt to new UI primitives | Controls assume old `Image`, `Point`, `Rectangle`, and mouse globals. |
| Rendering | Legacy `Kernel\Graph\Graphics.cs`, `Framebuffer.cs`; Server compositor/framebuffer | Reimplement with a surface API | Direct framebuffer pointers and assumptions about 32-bit pixels/pitch. |
| Event dispatch | Legacy/UEFI GUI event paths | Reimplement | Input and window state are routed through global static objects instead of an explicit event queue. |
| Input routing | Legacy PS/2/USB/HID and UEFI capability providers | Adapt | BIOS/port assumptions, firmware protocol lifetime, static mouse state, and device-specific paths. |
| Taskbar/launcher | Legacy/UEFI `Taskbar.cs`, Start Menu assets, Server built-in metadata | Reimplement using App Model resolution | Current taskbar routes display labels into hard-coded launch branches. |
| File Explorer | Legacy/UEFI `ComputerFiles.cs`, `FileExplorer`-style code, Server `file_explorer.cpp` | Reimplement from behavior | Direct filesystem internals, hard-coded roots, and target-specific launch names. |
| Settings | Legacy/UEFI `DisplayOptions.cs`, `UISettings.cs`, configuration classes | Adapt | Persistence and display resolution are global and often initialized during desktop startup. |
| Clipboard | Legacy UI and Server `file_clipboard.cpp` | Reimplement as an OS service | UI should not own clipboard storage or use direct filesystem internals. |
| Built-in applications | Legacy/UEFI `DefaultApps\*.cs` | Adapt incrementally through App Model | Many apps assume static `Program` fields, direct `Framebuffer`, and synchronous kernel calls. |
| GXM applications | Legacy `GXM.Apps`, `GXM.Apps\Apps`, ramdisk payloads | Preserve format/behavior; adapt host | GXM is a useful identity and asset source, but its runtime is coupled to the old shell and filesystem. |
| Themes | Legacy/UEFI assets and style code; Server desktop theme files | Copy assets, reimplement loader | Theme selection and persistence are global; define stable resource IDs. |
| Icons | Legacy/UEFI `Icons.cs`, `Ramdisk\Images`, UEFI-safe loaders | Copy assets; adapt loader | PNG/native P/Invoke path is explicitly unsafe after `ExitBootServices`; use a freestanding decoder. |
| Fonts | Legacy/UEFI `BitFont`, `TrueTypeFont`, `Ramdisk\Fonts` | Copy assets; adapt renderer | Direct framebuffer and allocation assumptions; keep deterministic bitmap font first. |
| Wallpapers | Legacy/UEFI `Ramdisk\Backgrounds`, Server wallpaper registry | Copy assets; adapt persistence | Fixed screen sizes and direct image allocation; use a display service and scaling policy. |
| Persistence | Legacy `Configuration`, `VirtualDiskAutoMount`; Server VFS/package stores | Reimplement | Persistence is mixed with boot mode, raw disks, and hosted/bare-metal differences. |

## Direct-coupling inventory

Observed coupling that must not cross the new public boundary:

- `Program` holds static cursor, wallpaper, console, and UI objects.
- Desktop and window code use `Framebuffer` and image buffers directly.
- Input providers update `System.Windows.Forms.Control` mouse state globally.
- Applications call filesystem and kernel classes directly rather than an App Model service.
- Old UI code contains screen-size and pixel-format assumptions.
- Legacy build assets are placed in ramdisks with paths that application code treats as stable implementation details.
- UEFI adds many safe-mode compile flags and step probes because normal rendering and PNG paths are not yet safe after `ExitBootServices`.
- Server has a hosted manifest registry and a separate bare-metal kernel app registry; names, availability, and file associations can drift.

## Reuse policy

Preserve the guideXOS identity through assets, names, visual language, application behavior, and file associations. Retire direct dependencies on BIOS, GRUB, firmware protocols, fixed resolutions, raw framebuffer pointers, and global kernel state. The first userland deliverable should be a diagnostics app built on the future App Model, not a desktop port.

