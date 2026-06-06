# Archimedes Screw

Mechanically powered water lifting for Vintage Story.

## Features

- Vertical Archimedes screw multiblock with intake, straight segments, and outlet.
- Intake can be placed before water arrives; activation still requires water at the intake.
- Places a single regular level-7 water source at the outlet when the assembly is valid, powered, and the intake sits in water.
- The output source matches the intake fluid family (water, salt water, or boiling water).
- Removes the created source as soon as the assembly is invalidated (power lost, structure broken, or intake out of water).
- Optional RealisticWater compatibility (places and sustains a realistic-water outlet when that mod is installed).

## Build

Requirements:

- .NET 10 SDK
- Vintage Story 1.22+

Build:

```bash
dotnet build
```

If your game path is not auto-detected, set `VINTAGE_STORY` before building.

Build output is under `bin/Debug/Mods/mod/`.

## Install

Copy the contents of `bin/Debug/Mods/mod/` into your Vintage Story mods folder, or zip that folder for distribution.
