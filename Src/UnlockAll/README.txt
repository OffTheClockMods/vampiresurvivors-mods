Unlock All - Vampire Survivors
Version 1.0.3
by Off the Clock Software

Unlocks every character (and marks them as bought), every stage with its Hyper mode, every
weapon and passive item, every arcana card and every relic - for the base game and the DLC you own.
Content of DLC you don't own is never unlocked; the log names what stayed locked and why.

How to use (it is opt-in, nothing happens by itself):
1. Start the game once with the mod installed and quit. This creates the config file.
2. Open BepInEx/config/offtheclock.unlockall.cfg and set RunUnlockAtNextStart = true.
   Switch off any category you don't want (Characters, BuyCharacters, Stages, HyperModes,
   WeaponsAndPassives, Arcanas, Relics).
3. Start the game. At the main menu the mod unlocks everything, saves, and sets
   RunUnlockAtNextStart back to false. If a screen still shows something locked, go back to
   the main menu once or restart the game.
   If a category fails, the mod saves what worked and tries the rest once more the next time
   the main menu shows (open any menu and go back). Running it again later is safe: anything
   already unlocked is skipped.

Your save:
- Before changing anything the mod copies your Steam save file(s)
  (Steam/userdata/<account>/1794680/remote/SaveData*) to
  BepInEx/offtheclock-save-backups/<date-time>/ and asks the game to make its own backup too.
- The unlocks are written into your save, so they stay after you remove the mod.
- To undo: quit the game, copy the backup file back over the Steam save file (same name) and
  start the game. Steam Cloud may offer a sync conflict; pick the local (older) file.
- The mod also sets the game's own "cheat code used" flag on the save (the same flag the game's
  built-in intro cheat sets). It never clears it. The game currently only stores that flag.

Achievements: the mod doesn't award any. Achievements are how the game normally unlocks things,
and they stay untouched, so achievements tied to unlocking are not "earned" by this. The game may
still award progress achievements later through its own checks.

Online co-op: the mod does nothing while you are in an online lobby. Vampire Survivors has no
online leaderboards.

Requirements:
- Vampire Survivors 1.16 (Steam, Unity version)
- BepInEx 6 Unity IL2CPP (Bleeding Edge, be.788 or newer)
- If the game crashes on the first BepInEx start: set UnityLogListening = false under
  [Logging] in BepInEx/config/BepInEx.cfg

Install: put UnlockAll.dll in Vampire Survivors/BepInEx/plugins/, then follow "How to use".

Config (created on first launch): BepInEx/config/offtheclock.unlockall.cfg
- RunUnlockAtNextStart  (default false)  set true to unlock once at the next start
- Characters, BuyCharacters, Stages, HyperModes, WeaponsAndPassives, Arcanas, Relics
                        (default true)   what gets unlocked

Log (BepInEx/LogOutput.log), per category (characters, stages, weapons and passives, arcanas,
relics): "unlocked now N, already unlocked N, left locked (DLC not owned) N, failed N", then the
names of what was unlocked, what was left locked and why, and anything that failed. It ends with
"Unlock All: DONE - ..."; the DLC owned / not owned line is near the top.

Arcanas: "every arcana card" means the arcana cards the game's Collection shows (base game and
the DLC you own). Character skill cards and other internal entries are not arcana cards and are
left alone.

Uninstall: delete UnlockAll.dll (and the .cfg if you like). Unlocks already saved stay.

Changelog
1.0.3
- Ready for custom and Workshop characters (coming with Vampire Survivors 1.17): they are always
  left locked and never marked as bought, so nothing of them is written into your save.
- Paid DLC: unchanged - only DLC you own is unlocked. New log check after saving: nothing new in
  the save belongs to a DLC you don't own ("[SELFCHECK] UnlockAll dlc_respected PASS ...").
- Safer with game updates: if a future patch renames what the mod reads at the main menu, the
  log says so clearly instead of the mod silently doing nothing.
1.0.2
- Under the hood: built on Off the Clock's shared plugin helpers. Nothing changes in game: same
  effect, config and defaults.
- The log now confirms each category after saving: every entry unlocked is read back from
  the save ("[SELFCHECK] UnlockAll characters PASS ..."), and the cheat-code mark too.
1.0.1
- Fixed: characters, stages and relics were not unlocked (each stopped with an error on its first
  base-game entry). Base-game content has an empty DLC tag, and reading that empty tag failed; the
  mod now reads it safely.
- Fixed: arcanas were skipped (0 unlocked). The mod now unlocks the cards the Collection shows.
- The log now lists per category what was unlocked, already unlocked, left locked (DLC not owned)
  and failed. One failing entry no longer stops its whole category; a failed category is tried
  once more at the next main menu.
1.0.0
- First release
