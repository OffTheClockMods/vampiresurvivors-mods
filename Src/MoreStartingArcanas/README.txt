More Starting Arcanas - Vampire Survivors
Version 1.0.0
by Off the Clock Software

Choose 2, 3 or more arcanas at the start of a run instead of 1.

What the game does without this mod (checked in the game code, 1.16):
- With arcanas on for the run, you choose 1 arcana at the start. Treasure chests later in
  the run can give more, up to 3 arcanas per run (a few characters raise that limit).

What this mod adds:
- StartingArcanas: the arcana screen opens again after each pick at the start, until you
  have this many (default 3). Already chosen arcanas are not offered again.
- RaiseArcanaLimit (default on): the run's arcana limit goes up by your extra picks, so the
  treasure chests can still give you arcanas later. Off = the extra start picks count toward
  the game's normal limit.
- Only when the game itself offers an arcana at the start of the run. Limited by how many
  arcanas you have unlocked.
- Off in online co-op. Nothing is written to your save. No keys.
- More arcanas make runs much stronger.

Requirements:
- Vampire Survivors 1.16 (Steam, Unity version)
- BepInEx 6 Unity IL2CPP (Bleeding Edge, be.788 or newer)
- If the game crashes on the first BepInEx start: set UnityLogListening = false under
  [Logging] in BepInEx/config/BepInEx.cfg

Install:
1. Put MoreStartingArcanas.dll in Vampire Survivors/BepInEx/plugins/
2. Start a run with arcanas on

Config (created on first launch): BepInEx/config/offtheclock.morestartingarcanas.cfg
- StartingArcanas   (default 3, 1 to 30)
- RaiseArcanaLimit  (default true)

Log (BepInEx/LogOutput.log): "2 extra arcana selection(s) queued after the game's own (3 at
the start); run arcana limit 3 -> 5", then "arcana 1 of 3 chosen" and so on.

Uninstall: delete MoreStartingArcanas.dll (and the .cfg if you like).

Changelog
1.0.0
- First release
