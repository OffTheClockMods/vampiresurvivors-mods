More Weapon Slots and Passive Slots - Vampire Survivors
Version 1.0.1
by Off the Clock Software

More weapon slots and more passive item slots in every run: +2 of each by default (8 weapons
and 8 passives in single player instead of 6 and 6), set anywhere from +0 to +999 in the config (999 = never full).
Works in single player and local co-op.

- The level-up screen, chests and the merchant offer new weapons and passives until the new
  limit is reached, exactly as they do up to 6 in the normal game.
- Stacks with the game's own extra-slot items, survarots and characters (several DLC items and
  characters add slots). The mod only adds; it never lowers or replaces a bonus the game gives
  you.
- Also stacks with the character screen's own "max weapons" setting: if you lower it to 4 for a
  challenge, +2 gives you 6.
- Local co-op: the game gives each player fewer weapon slots (4 / 3 / 2 with 2 / 3 / 4 players).
  The extra slots are added on top. Set RestoreSoloSlotsInLocalCoop = true to give every player
  the solo numbers back first (then the extra slots on top).
- Off in online co-op: nothing changes while you are in an online lobby.
- Nothing is written to your save. Remove the mod and the next run is normal again.

How it looks (HUD):
- Single player: the HUD shows the game's usual 6 weapon frames at the start of a run; weapon 7,
  8 ... appear next to them as you pick them up. Passives work the same way.
- The game itself can already reach 8 weapons and many more than 6 passives with some DLC items
  and characters, so +2 is inside what the game's HUD is built for. Past that, how the
  icon row looks (wrapping, growing sideways or running off screen) has not been checked yet.
  Every slot still works even if an icon ends up off screen.
- The log writes a short "HUD check" line whenever an icon row passes 6 icons, so you can see
  whether all icons are on screen.

There is no hard limit in the game's code (slots are just numbers the game compares against).
The practical limit is how many different weapons and passives you have unlocked: once the
level-up pool has nothing new to offer, more slots change nothing.

Requirements:
- Vampire Survivors 1.16 (Steam, Unity version)
- BepInEx 6 Unity IL2CPP (Bleeding Edge, be.788 or newer)
- If the game crashes on the first BepInEx start: set UnityLogListening = false under
  [Logging] in BepInEx/config/BepInEx.cfg

Install:
1. Put MoreWeaponAndPassiveSlots.dll in Vampire Survivors/BepInEx/plugins/
2. Start the game and play a run

Config (created on first launch): BepInEx/config/offtheclock.moreweaponandpassiveslots.cfg
[Slots]
- ExtraWeaponSlots              (default 2, 0-999) weapon slots added (solo 6 + 2 = 8)
- ExtraPassiveSlots             (default 2, 0-999) passive slots added (6 + 2 = 8)
[Local co-op]
- RestoreSoloSlotsInLocalCoop   (default false)     every player gets the solo numbers back first
[Log]
- HudReport                     (default true)      "HUD check" log line when an icon row passes 6
Changes apply from the next run.

Log (BepInEx/LogOutput.log): one line per character at run start, e.g.
"More Weapon and Passive Slots: Antonio - weapon slots 8 (game 6 + 2 extra), passive slots 8
(game 6 + 2 extra)." and one line whenever the game changes the numbers during the run (for
example "the game changed the slot bonus (an item or ability): ... passive slots 9").

Uninstall: delete MoreWeaponAndPassiveSlots.dll (and the .cfg if you like).

Changelog
1.0.2
- The limit is now +999 each (was +30), so even a run that takes every item never fills the slots. With very many items the icon rows at the top run past the screen edge; the items still work.
1.0.1
- Under the hood: built on Off the Clock's shared plugin helpers. Nothing changes in game: same
  effect, config and defaults.
- An error in one of the hooks is logged at most 3 times, not on every level-up.
- The log confirms the slot counts at run start and at the first level-up, as the
  game itself reports them ("[SELFCHECK]" lines).
1.0.0
- First release
