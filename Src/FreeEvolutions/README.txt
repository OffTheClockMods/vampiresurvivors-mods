Free Evolutions - Vampire Survivors
Version 1.0.2
by Off the Clock Software

Weapons at max level evolve from a chest without the paired passive item (Whip evolves without
Hollow Heart, Magic Wand without Empty Tome, and so on).

- Everything else stays the game's rule: the weapon must be at max level, and unions still need
  both weapons (only passive items become optional).
- Evolutions with special rules (the game's "unique requirements", a few shield and Saboteur
  cases) keep those rules unless you set KeepUniqueRules = false.
- Off in online co-op: the game's normal check runs there. Nothing is written to your save. No keys.

Requirements:
- Vampire Survivors 1.16 (Steam, Unity version)
- BepInEx 6 Unity IL2CPP (Bleeding Edge, be.788 or newer)
- If the game crashes on the first BepInEx start: set UnityLogListening = false under
  [Logging] in BepInEx/config/BepInEx.cfg

Install:
1. Put FreeEvolutions.dll in Vampire Survivors/BepInEx/plugins/
2. Start the game, max a weapon, open a chest

Config (created on first launch): BepInEx/config/offtheclock.freeevolutions.cfg
- Enabled           (default true)
- KeepUniqueRules   (default true)  special-rule evolutions keep the game's check

Log (BepInEx/LogOutput.log): "Free Evolutions: <weapon> can evolve without <item>." the first
time an evolution is allowed without its passive.

Uninstall: delete FreeEvolutions.dll (and the .cfg if you like).

Changelog
1.0.2
- Safer with game updates: if a future Vampire Survivors patch (e.g. 1.17) changes the game's
  evolution data, the mod now steps aside and the game's normal evolution rule runs (one line in
  the log) instead of possibly blocking evolutions. Nothing changes in game on 1.16.
1.0.1
- Under the hood: built on Off the Clock's shared plugin helpers. Nothing changes in game: same
  effect, config and defaults.
- An error in the evolution check is logged at most 3 times, not on every chest; the game's
  own rule is used in that case, as before. Plays well with other mods that change the same check.
- The log confirms the first free evolution ("[SELFCHECK]" lines).
1.0.0
- First release
