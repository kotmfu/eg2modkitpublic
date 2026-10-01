# Eg2 ModKit

A mod manager and mod-making toolkit for **Evil Genius 2: World Domination**

## What it does

- **Play:** quick tweaks, and installing or removing mods with one click.
  Uninstall removes only the files ModKit wrote.
- **Make mods:** new furniture from existing items, costs and furniture
  effects, research numbers and research trees, text, lair maps and islands,
  and texture recolours.
- **Game files:** browse every sound, texture, mesh, animation and model in the
  game. You can extract them (WAV, PNG/DDS, OBJ), dump the lot in one go, or
  replace them with your own files.
- **Share:** export a mod as a `.eg2mod` file. Players can install it with the
  standalone installer, without the full kit.
- **Diagnostics:** a self-test of the format code, and a lossless round-trip
  check of every archive in your install.

Mods never change the game's own files. Instead ModKit writes `.asrpatch`
override files, which the engine checks for next to every data file it opens.
New content goes into the engine's empty development package slots. Runtime
tweaks use a small `winmm.dll` proxy that edits game data in memory; it never
patches game code.

## Requirements

- Windows 10/11, x64
- Evil Genius 2 (Steam)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build
- Visual Studio C++ build tools, only if you want to rebuild the `winmm.dll` proxy

## Build and run

```bash
dotnet build modkit/Eg2ModKit.slnx -c Release
```

```bash
dotnet run --project modkit/src/Eg2.ModManager -c Release
```

The proxy DLL is prebuilt separately and copied in when it exists:

```bash
modkit/native/winmm/build.cmd
```

ModKit finds the game folder automatically, or you can set it in Settings.
Mods live in `Documents\Eg2ModKit\Mods` and settings in
`%APPDATA%\Eg2ModKit\settings.json`. Each install is recorded in
`<game>\eg2modkit.installed.json`.

**Back up your saves.** A save made with mods active can depend on them.

## Layout

```
modkit/
  src/Eg2.Asura/        Asura format library: archives, block compression, chunks, data objects
  src/Eg2.ModKit/       game data, mod definitions, builder, installer, assets, maps, self-test
  src/Eg2.ModManager/   the WinForms GUI (Eg2ModManager.exe)
  src/Eg2.ModInstaller/ player-facing .eg2mod installer (Eg2ModInstaller.exe)
  native/winmm/         runtime tweak proxy (C++/asm)
  reference/            golden outputs the self-test checks against, and the scripts that make them
docs/REFERENCE.md       file formats, where every file lives, how mods are built and installed
```

## Legal

This is an unofficial fan project, not affiliated with or endorsed by
Rebellion. The format notes come from inspecting files in a legitimately
bought copy, for interoperability. The repository contains no game assets, and
you need your own copy of the game.
