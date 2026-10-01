Enemy Health and Damage Multiplier - Vampire Survivors
Version 1.0.0
by Off the Clock Software

Difficulty settings for Vampire Survivors: make enemies tougher or weaker, and make their
hits hurt more or less. Two numbers in a config file.

What the game does without this mod (checked in the game code, 1.16):
- Enemy health grows with the stage's own settings over time, your Curse stat and some game
  modes and cards. There is no free difficulty setting.

What this mod adds:
- EnemyHealthMultiplier: every enemy and boss spawned from now on gets this much health
  (x0.01 to x100), on top of everything the game already does.
- DamageTakenMultiplier: damage you take is multiplied by this (x0 to x100). Your armor still
  counts (it is subtracted after the multiplier). 0 = no damage at all.
- Damage from your own weapons (a few characters hurt themselves) stays normal.
- Changes apply right away when you save the config file (enemy health from the next spawn).
- Off in online co-op. Nothing is written to your save. No keys.

Honest limits:
- A few special characters handle damage their own way, and a few special enemies set their
  own health; those keep the game's numbers.
- Very high enemy health makes runs much longer. Very low health makes most enemies die to
  any hit.

Requirements:
- Vampire Survivors 1.16 (Steam, Unity version)
- BepInEx 6 Unity IL2CPP (Bleeding Edge, be.788 or newer)
- If the game crashes on the first BepInEx start: set UnityLogListening = false under
  [Logging] in BepInEx/config/BepInEx.cfg

Install:
1. Put EnemyHealthAndDamage.dll in Vampire Survivors/BepInEx/plugins/
2. Start the game once, then edit the config (below) and play a run

Config (created on first launch): BepInEx/config/offtheclock.enemyhealthanddamage.cfg
- EnemyHealthMultiplier  (default 1, 0.01 to 100)  3 = three times as tough
- DamageTakenMultiplier  (default 1, 0 to 100)     0.5 = half damage, 0 = none

Log (BepInEx/LogOutput.log):
- At start: both values.
- In a run: one line for the first enemy ("max health 10 -> 30") and the first hit
  ("hit 8 -> 4 before armor").

Uninstall: delete EnemyHealthAndDamage.dll (and the .cfg if you like).

Changelog
1.0.0
- First release
