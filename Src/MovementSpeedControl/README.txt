Movement Speed Control - Vampire Survivors
Version 1.0.0
by Off the Clock Software

Move faster or slower, and set a floor and a cap for your move speed, so stacked speed
bonuses never make you too fast (or slow characters never feel sluggish).

What the game does without this mod (checked in the game code, 1.16):
- Your move speed is your character's MoveSpeed stat (Wings, the MoveSpeed PowerUp, golden
  eggs and some characters add to it) times a per-character factor. There is no setting for
  it and no upper limit.

What this mod adds:
- MoveSpeedMultiplier: your final move speed times this (1 = normal, 1.5 = 50% faster).
- MinimumMoveSpeed: never slower than this (1 = 100%, a character with no speed bonus).
- MaximumMoveSpeed: never faster than this. If it is lower than the minimum, the cap wins.
- Works for every character, including the four with their own speed rules (the mod finds
  them in the game code at start).
- Changes apply right away when you save the config file (no restart needed).
- Off in online co-op. Nothing is written to your save. No keys.
- The stats panel still shows the game's own MoveSpeed number, not the result of this mod.
- Weapons that scale with your move speed use the changed value too.

Requirements:
- Vampire Survivors 1.16 (Steam, Unity version)
- BepInEx 6 Unity IL2CPP (Bleeding Edge, be.788 or newer)
- If the game crashes on the first BepInEx start: set UnityLogListening = false under
  [Logging] in BepInEx/config/BepInEx.cfg

Install:
1. Put MovementSpeedControl.dll in Vampire Survivors/BepInEx/plugins/
2. Start the game once, then edit the config (below) and play a run

Config (created on first launch): BepInEx/config/offtheclock.movementspeedcontrol.cfg
- MoveSpeedMultiplier  (default 1, 0.1 to 20)  1.5 = 50% faster. Very high values are hard
                                               to steer; above 20 the stage's background
                                               may not keep up with you
- MinimumMoveSpeed     (default 0 = off)       1 = never slower than 100%
- MaximumMoveSpeed     (default 0 = off)       1.5 = never faster than 150%

Log (BepInEx/LogOutput.log):
- At start: the settings and how many characters with their own speed rule are covered.
- In a run: "active - move speed x1.5 (game speed 100% -> 150%)" once.

Uninstall: delete MovementSpeedControl.dll (and the .cfg if you like).

Changelog
1.0.0
- First release
