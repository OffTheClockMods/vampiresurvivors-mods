Auto Level Up - Auto Pick Upgrades - Vampire Survivors
Version 1.0.0
by Off the Clock Software

Level-ups pick an upgrade for you from your own priority list, without stopping the game
for the level-up screen.

What the game does without this mod (checked in the game code, 1.16):
- Every level-up opens the level-up screen with 3 or 4 offers, and the game waits for you.
- The game's own "Random level-ups" option picks one of those offers at random instead.

What this mod adds:
- The same no-screen level-up as the game's random option, but the pick follows your list:
  the first upgrade on your PriorityList that is offered is taken. Example:
  Whip, Hollow Heart, Spinach, Garlic
- AvoidList: upgrades the mod never takes for you. If only those are offered, the normal
  screen opens so you decide.
- Nothing from your list offered: a random allowed offer (default), the first one, or the
  normal screen (WhenNothingFromListIsOffered = Random / First / ShowScreen).
- Names: the names the game shows (King Bible) or its internal names (HOLYBOOK). Parts of a
  name work too (4+ letters: "bible"). The log lists every offer with both names.
- Numpad 4 switches auto-picking off and on while you play (Num Lock on).
- Once everything is maxed the game's own coin bag / Floor Chicken / limit break choice is
  left alone (our Always Pick Coin Bag mod covers that part).
- Off in online co-op. Nothing is written to your save.

Requirements:
- Vampire Survivors 1.16 (Steam, Unity version)
- BepInEx 6 Unity IL2CPP (Bleeding Edge, be.788 or newer)
- If the game crashes on the first BepInEx start: set UnityLogListening = false under
  [Logging] in BepInEx/config/BepInEx.cfg

Install:
1. Put AutoLevelUp.dll in Vampire Survivors/BepInEx/plugins/
2. Start the game once, write your PriorityList in the config (below), play a run

Config (created on first launch): BepInEx/config/offtheclock.autolevelup.cfg
- AutoPickEnabled               (default true)
- PriorityList                  (default empty)  best first, comma-separated
- AvoidList                     (default empty)
- WhenNothingFromListIsOffered  (default Random) Random / First / ShowScreen
- ToggleKey                     (default Keypad4) None = no key
- ShowOnScreenNotice            (default true)

The game does not use Numpad 4. Our other Vampire Survivors mods use Numpad * and / (Gold
and XP Multiplier) and Numpad 7/8/9 (Time Stop keys).

Log (BepInEx/LogOutput.log): for the first 5 picks of a run, then every 10th:
"level 6 - offered King Bible (HOLYBOOK), Whip (WHIP), Spinach (POWER); took Whip (WHIP)
(priority #1)".

Uninstall: delete AutoLevelUp.dll (and the .cfg if you like).

Changelog
1.0.0
- First release
