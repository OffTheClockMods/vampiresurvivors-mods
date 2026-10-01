Always Pick Coin Bag - Vampire Survivors
Version 1.0.2
by Off the Clock Software

Once every weapon and passive is maxed, level-ups take the coin bag automatically, without
pausing the game.

What the game does without this mod (checked in the game code, 1.16):
- Once nothing is left to level, every level-up still opens the level-up screen, which then
  offers a choice between a coin bag (gold) and Floor Chicken (heal). You pick one each time.
- With the game's "Random level-ups" option on, the game picks by itself: Floor Chicken while
  your health isn't full, otherwise the coin bag. (That option also picks random upgrades
  before you are maxed.)
- After 100 big coin bag pickups (lifetime), the game adds two more choices to that maxed
  screen: "always coin bag" and "always Floor Chicken".

What this mod adds:
- It switches the game's own "always coin bag" setting on for your character at the start of
  every run, so from the first maxed level-up on, the coin bag is taken automatically. No screen,
  no pause, and you keep manual level-up choices while there is still something to pick.
- What you see: the game's own feedback above your character, a coin icon with the gold amount
  and "LEVEL UP", while the game keeps running. That is the coin bag being taken.
- Option AlsoRoastWhenHurt: take the Floor Chicken instead while your health isn't full,
  otherwise the coin bag (the rule the game uses for "Random level-ups").
- Limit Break: if the stage's Limit Break option is on, the game offers limit breaks once
  weapons are maxed, and the coin bag is only auto-picked when no limit break is left. The mod
  doesn't change that.
- Off in online co-op. Nothing is written to your save. No keys.

Requirements:
- Vampire Survivors 1.16 (Steam, Unity version)
- BepInEx 6 Unity IL2CPP (Bleeding Edge, be.788 or newer)
- If the game crashes on the first BepInEx start: set UnityLogListening = false under
  [Logging] in BepInEx/config/BepInEx.cfg

Install:
1. Put AlwaysPickCoinBag.dll in Vampire Survivors/BepInEx/plugins/
2. Start the game and play a run until everything is maxed

Config (created on first launch): BepInEx/config/offtheclock.alwayspickcoinbag.cfg
- AlwaysCoinBag       (default true)   auto-pick the coin bag when everything is maxed
- AlsoRoastWhenHurt   (default false)  Floor Chicken while health is missing, otherwise coin bag

Log (BepInEx/LogOutput.log):
- At the start of every run: "<character> will auto-pick the coin bag once everything is
  maxed", plus a note when Limit Break or Random level-ups is on for that run.
- Each auto-pick: "coin bag auto-picked (<character>, level N; #1 this run)" for the first 5
  in a run, then every 10th with the count ("coin bag auto-picked 20 times this run").

Uninstall: delete AlwaysPickCoinBag.dll (and the .cfg if you like).

Changelog
1.0.2
- Under the hood: built on Off the Clock's shared plugin helpers. Nothing changes in game: same
  effect, config and defaults.
- The two log hooks are optional: if a game update breaks one, the auto-pick itself keeps
  working. Errors are logged (at most 3 times) instead of being dropped silently.
- The log confirms the setting at run start and the first auto-pick ("[SELFCHECK]" lines).
1.0.1
- The log now shows every auto-pick (first 5 per run, then every 10th with a count) and says
  at run start when Limit Break or Random level-ups changes what happens.
- Clearer description of what the game does by itself and what the mod adds.
1.0.0
- First release
