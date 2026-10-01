Unlimited Rerolls, Skips and Banishes - Vampire Survivors
Version 1.0.2
by Off the Clock Software

The level-up screen always has rerolls, skips and banishes left: each count is topped up to
the number in the config (default 999) every time the screen opens. Optional: extra revivals.

- Uses the game's own counters on your character, the ones the Reroll / Skip / Banish PowerUps
  and golden eggs fill. Set an amount to 0 to leave that counter alone.
- Revivals (default 0 = unchanged): keeps at least N unused revivals, topped up on the level-up
  screen and after each revival.
- Off in online co-op. Nothing is written to your save. No keys.

Requirements:
- Vampire Survivors 1.16 (Steam, Unity version)
- BepInEx 6 Unity IL2CPP (Bleeding Edge, be.788 or newer)
- If the game crashes on the first BepInEx start: set UnityLogListening = false under
  [Logging] in BepInEx/config/BepInEx.cfg

Install:
1. Put UnlimitedRerolls.dll in Vampire Survivors/BepInEx/plugins/
2. Start the game and level up

Config (created on first launch): BepInEx/config/offtheclock.unlimitedrerolls.cfg
- Rerolls    (default 999, 0-9999)  0 = unchanged
- Skips      (default 999, 0-9999)  0 = unchanged
- Banishes   (default 999, 0-9999)  0 = unchanged
- Revivals   (default 0, 0-9999)    unused revivals kept at least at this; 0 = unchanged

Log (BepInEx/LogOutput.log): "counts topped up on the level-up screen (rerolls 999, ...)" once
per run.

Uninstall: delete UnlimitedRerolls.dll (and the .cfg if you like).

Changelog
1.0.2
- Safer with game updates: if a future Vampire Survivors patch (e.g. 1.17) renames what the mod
  reads on the level-up screen, the mod now steps aside (one line in the log) and the screen's
  buttons keep working normally. Nothing changes in game on 1.16.
1.0.1
- Under the hood: built on Off the Clock's shared plugin helpers. Nothing changes in game: same
  effect, config and defaults.
- The revival part is optional: if a game update breaks it, rerolls, skips and banishes keep
  working. An error is logged at most 3 times, not on every level-up screen.
- The log confirms the topped-up counts on the first level-up screen ("[SELFCHECK]" lines).
1.0.0
- First release
