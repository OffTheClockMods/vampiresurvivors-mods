# Vampire Survivors Mods by Off the Clock

Source code of the Off the Clock mods for **Vampire Survivors**: BepInEx 6 IL2CPP plugins (C#, Unity 6), Steam PC.

## The mods

| Mod | What it does | Version | Download |
|---|---|---|---|
| **Always Pick Coin Bag** (`Src/AlwaysPickCoinBag`) | Always pick coin bag for Vampire Survivors 1.16: once every weapon and passive is maxed, level-ups take the coin bag automatically instead of opening the level-up screen each time. Uses the game's own switch; you still pick upgrades until maxed. Off in online co-op. Vampire Survivors (Steam) + BepInEx 6. | 1.0.2 | [Nexus Mods](https://www.nexusmods.com/vampiresurvivors/mods/113) |
| **Auto Level Up - Auto Pick Upgrades** (`Src/AutoLevelUp`) | Auto level up for Vampire Survivors 1.16: level-ups pick an upgrade for you from your own priority list (auto pick upgrades), with an avoid list, so the game stops pausing for the level-up screen. Numpad 4 switches it off and on. Picks only from the game's own offers. Off in online co-op. Vampire Survivors (Steam) + BepInEx 6. | 1.0.0 | [Nexus Mods](https://www.nexusmods.com/vampiresurvivors/mods/119) |
| **Enemy Health and Damage Multiplier** (`Src/EnemyHealthAndDamage`) | Enemy health and damage multiplier for Vampire Survivors 1.16: difficulty settings in two numbers. Enemies and bosses get x0.01 to x100 health, and the damage you take goes from x0 (none) to x100. Armor still counts. Config only, no save editing, off in online co-op. Vampire Survivors (Steam) + BepInEx 6. | 1.0.0 | [Nexus Mods](https://www.nexusmods.com/vampiresurvivors/mods/120) |
| **Free Evolutions - No Passive Item Needed** (`Src/FreeEvolutions`) | Free evolutions for Vampire Survivors 1.16: a weapon at max level evolves from a chest without the paired passive item (evolve without Hollow Heart, Empty Tome and the rest). Unions still need both weapons. Off in online co-op. No keys, no save editing. Vampire Survivors (Steam) + BepInEx 6. | 1.0.2 | [Nexus Mods](https://www.nexusmods.com/vampiresurvivors/mods/114) |
| **Gold and XP Multiplier (x2 to x100)** (`Src/GoldAndXpMultiplier`) | Gold multiplier and XP multiplier for Vampire Survivors 1.16: earn x2 to x100 gold and experience in every run (more gold for PowerUps, faster level ups). Numpad * and Numpad / cycle the values in game; defaults in a config file. Stacks with hyper mode; only during runs; off in online co-op. Vampire Survivors (Steam) + BepInEx 6. | 1.1.3 | [Nexus Mods](https://www.nexusmods.com/vampiresurvivors/mods/117) |
| **More Starting Arcanas** (`Src/MoreStartingArcanas`) | More starting arcanas for Vampire Survivors 1.16: choose 2, 3 or more arcanas at the start of a run instead of 1 (multiple arcanas). The arcana screen opens again after each pick, and chests can still give arcanas later. Config only, no save editing, off in online co-op. Vampire Survivors (Steam) + BepInEx 6. | 1.0.0 | [Nexus Mods](https://www.nexusmods.com/vampiresurvivors/mods/122) |
| **More Weapon Slots and Passive Slots** (`Src/MoreWeaponAndPassiveSlots`) | More weapon slots and passive slots for Vampire Survivors 1.16: carry 8 weapons and 8 passive items instead of 6 (extra weapon slots, +0 to +999 in the config, so it never fills up). Stacks with the game's own slot bonuses; optional solo slots in local co-op. Off in online co-op, no save editing. Vampire Survivors (Steam) + BepInEx 6. | 1.0.2 | [Nexus Mods](https://www.nexusmods.com/vampiresurvivors/mods/116) |
| **Movement Speed Control - Speed Cap** (`Src/MovementSpeedControl`) | Movement speed control for Vampire Survivors 1.16: move faster or slower (x0.1 to x20) and set a speed cap so stacked move speed bonuses never make you too fast, plus a floor for slow characters. Works for every character. Config only, no save editing, off in online co-op. Vampire Survivors (Steam) + BepInEx 6. | 1.0.0 | [Nexus Mods](https://www.nexusmods.com/vampiresurvivors/mods/121) |
| **Rebind Keys - AZERTY and Custom Keys** (`Src/RebindKeys`) | Rebind keys in Vampire Survivors 1.16: change the game's keyboard keys in a config file (key rebinding the game lacks). AZERTY (ZQSD) and ESDF presets, or your own list such as W=Z, A=Q. Old keys keep working if you want. Nothing saved: remove it and the normal keys are back. Vampire Survivors (Steam) + BepInEx 6. | 1.0.0 | [Nexus Mods](https://www.nexusmods.com/vampiresurvivors/mods/118) |
| **Time Stop and Skip Minute Keys** (`Src/TimeStopAndSkipMinute`) | Time stop and skip minute keys for Vampire Survivors 1.16: Numpad 7 freezes the run clock (no next wave, no Reaper), Numpad 8 jumps to the next minute, Numpad 9 heals you to full. The missing parts of the old debug mode. Keys in the config, no save editing, off in online co-op. Vampire Survivors (Steam) + BepInEx 6. | 1.0.0 | [Nexus Mods](https://www.nexusmods.com/vampiresurvivors/mods/123) |
| **Unlimited Rerolls Skips and Banishes** (`Src/UnlimitedRerolls`) | Unlimited rerolls, skips and banishes for Vampire Survivors 1.16: the level-up screen always has them left (topped up to 999, configurable), plus an option for extra revivals. Uses the game's own counters. Off in online co-op. No keys, no save editing. Vampire Survivors (Steam) + BepInEx 6. | 1.0.2 | [Nexus Mods](https://www.nexusmods.com/vampiresurvivors/mods/115) |
| **Unlock All Characters and Everything** (`Src/UnlockAll`) | Unlock all characters in Vampire Survivors 1.16, plus every stage and Hyper mode, all weapons and passives, all arcana cards and all relics (unlock everything) with one config switch and one start. Base game and the DLC you own only; unowned DLC stays locked. Backs up your save first. Vampire Survivors (Steam) + BepInEx 6. | 1.0.3 | [Nexus Mods](https://www.nexusmods.com/vampiresurvivors/mods/112) |

Download the mods from the pages above. This repository is the source code, so you can see exactly what each mod does.

## Build it yourself

You need Windows, the [.NET SDK](https://dotnet.microsoft.com/download) (8 or newer) and the game with BepInEx
installed. The projects compile against the game's and BepInEx's own DLLs **from your install**; no game files are
in this repository. Copy the DLLs listed in `Src/Directory.Build.props` (or in the mod's `.csproj`) into the `refs\`
folder named there (from the game's `Managed` / BepInEx `core` / BepInEx `interop` folders), or point the build at
another folder with `-p:<RefsProperty>=<folder>`. Then:

```powershell
dotnet build Src/<Mod> -c Release
```

The plugin DLL lands in `Src/<Mod>/bin/`; copy it to `<game folder>\BepInEx\plugins\`. Players should install from
the mod pages above instead.

`Src/otc_bepinex/` is a vendored copy of [otc-bepinex](https://github.com/offtheclocksoftware/otc-bepinex) (MIT): fail-safe
Harmony patching, hotkeys and `[SELFCHECK]` lines shared by all our plugins.

## What the mods do and don't do

- **Only what the mod page says.** Single player; nothing affects other players or online modes.
- **Settings at the game's own values by default**, with wide ranges and a short note beside each setting.
- **Honest side effects**: each mod page and README says what it changes (saves, achievements, cheat flags).

## Bugs and ideas

Open an [issue](https://github.com/OffTheClockMods/vampiresurvivors-mods/issues). Please include the mod and version, the game version, what you did and what happened, and
the log lines that mention the mod.

## Licence

[MIT](LICENSE). You may learn from, reuse and adapt this code; keep the copyright notice. A friendly request (not a
licence term): please don't re-upload our mods. Link to our pages instead, so players always get the current version.

Written with AI coding help (Claude). Every release passes our offline tests and an automated in-game check before
it is published; we also play-test the mods by hand.

Vampire Survivors is a trademark of its owner (poncle). This project is not affiliated with or endorsed by them.
