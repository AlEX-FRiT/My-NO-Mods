# Nuclear Option Mods

BepInEx mods for Nuclear Option. Tested with BepInEx 5.x.

**⚠️ 100% vibe coded, no warranty, use at your own risk.**

## Prerequisites

1. Install [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) into the game directory
2. Install [ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager) plugin

## Critical: BepInEx Config

**`BepInEx/config/BepInEx.cfg` must have `HideManagerGameObject = true`:**

```ini
[Chainloader]
HideManagerGameObject = true
```

The default is `false`. When `false`, BepInEx creates a visible `BepInEx_Manager` GameObject in the scene. This can interfere with the game's own systems and cause:

- `ConfigurationManager` to fail to load (no F1 menu)
- Mods that depend on `ConfigurationManager` to not initialize
- Mods that reference other mods via `[BepInDependency]` to silently fail

Setting this to `true` hides the manager object with `HideFlags.HideAndDontSave`, preventing conflicts with the game engine.

## Building

Each mod is a standalone .NET Framework 4.7.2 project:

```bash
dotnet build -c Release ModProject/ModProject.csproj
```

Deploy output DLL to `BepInEx/plugins/ModName/ModName.dll`.

## Mods

| Mod | Description |
|-----|-------------|
| **DebugGraphMod** | Real-time signal visualization: floating IMGUI chart windows with GL-rendered line graphs. Other mods register data streams via static API. Toggle with F1 ConfigManager. |
| **HorizonLineMod** | Adds a horizon line / attitude indicator HUD overlay |
| **MouseAimMod** | Mouse-controlled aircraft aiming with MPC (golden-section + exact discrete model), FBW pitchAdjuster compensation, output Scale, ErrorExp, roll auto-centering, hover throttle for VTOL, preset save/load (5 slots), and StabilityKbOff toggle. Soft-depends on DebugGraphMod for per-axis error/output graphs. |
| **SureFireMod** | Prevents firing laser-guided weapons when target is not lased |
| **ThirdEyeMod** | Third-person camera overhaul: Orbit mode with mouse-driven view, HUD and minimap visible, aircraft-relative camera positioning |

## Known Issues

- **Third-person turret crosshair frozen**: `Turret.FixedUpdate()` only updates `manualVector` when `currentState == cockpitState`. In Orbit mode the turret cursor doesn't track mouse input. Fix: patch the condition in `FixedUpdate` to also accept `orbitState`.
- **ThirdEyeMod: mouse wheel Zoom View not implemented**: In cockpit mode, scroll wheel controls FOV. In the original third-person Orbit mode, scroll wheel controls camera distance (`viewDistAdjust`). ThirdEyeMod replaces `CameraMotion` but does not read `viewDistAdjust` — camera distance is fixed at the `CameraDistance` config value (default 30). To fix: read `viewDistAdjust` from the instance and apply it to the distance calculation.
