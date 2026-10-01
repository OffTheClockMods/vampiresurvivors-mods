Gold and XP Multiplier - Vampire Survivors
Version 1.1.3
by Off the Clock Software

Gold and experience earned during runs are multiplied: x1 to x100 each (default: gold x5,
experience x3). Change them in game with two keys:
- Numpad *  cycles gold:        x1 -> x2 -> x5 -> x10 -> x25 -> x50 -> x100 -> x1 ...
- Numpad /  cycles experience:  x1 -> x2 -> x3 -> x5 -> x10 -> x25 -> x50 -> x100 -> x1 ...
The new value shows on screen for a moment and in the log, applies at once, and is saved in the
config (so it's still set next time). Keys and the lists of values are configurable.
The default keys are not used by Vampire Survivors (its keyboard controls are WASD, arrows,
Enter, Space, Keypad Enter, Esc, Page Up/Down, Tab and Alt) and work with Num Lock on or off.

- Works on every coin that reaches your total in a run (coins, coin bags, chest gold, Gold
  Fever, gold piles) and on every bit of experience (gems, skip XP...), through the game's own
  AddCoins / AddXp code. It stacks with hyper mode, Greed and Growth like extra pickups would.
- Only during runs. Achievement rewards and refunds in the menus are not multiplied.
- Off in online co-op: nothing changes while you are in an online lobby.
- Nothing is written to your save except the gold you actually earned.

Requirements:
- Vampire Survivors 1.16 (Steam, Unity version)
- BepInEx 6 Unity IL2CPP (Bleeding Edge, be.788 or newer)
- If the game crashes on the first BepInEx start: set UnityLogListening = false under
  [Logging] in BepInEx/config/BepInEx.cfg

Install:
1. Put GoldAndXpMultiplier.dll in Vampire Survivors/BepInEx/plugins/
2. Start the game and play a run

Config (created on first launch): BepInEx/config/offtheclock.goldandxpmultiplier.cfg
[Multipliers]
- Gold                (default 5, 1-10)       gold earned during runs x this
- Experience          (default 3, 1-10)       experience earned during runs x this
[Keys]
- GoldCycleKey        (default KeypadMultiply) next gold value; None = no key
- ExperienceCycleKey  (default KeypadDivide)   next experience value; None = no key
- GoldSteps           (default 1, 2, 5, 10)    values the gold key cycles through (1-10)
- ExperienceSteps     (default 1, 2, 3, 5, 10) values the experience key cycles through (1-10)
- ShowOnScreenNotice  (default true)           show the new value on screen for a moment
Key names are Unity KeyCode names (e.g. F6, KeypadPlus, KeypadMinus, Keypad0).

Log (BepInEx/LogOutput.log): one line per run when the first coin and the first gem are
multiplied, e.g. "gold x5 active (pickup 1 -> 5)"; a line for every key press, e.g. "Gold
multiplier now x10 (cycle: x1/x2/x5/x10)", and the next multiplied pickup is logged again.

Uninstall: delete GoldAndXpMultiplier.dll (and the .cfg if you like).

Changelog
1.1.3
- Fixed: pressing a key quickly several times could leave the multiplier on the previous value
  (the XP key showed x25 but the next gem still used x10). The keys now always apply the value
  they show. The config file is still updated with every press.
1.1.2
- The keys now go up to x100 (x25, x50, x100 added to both cycles); an unchanged old list is updated automatically.
1.1.1
- Under the hood: built on Off the Clock's shared plugin helpers. Nothing changes in game: same
  effect, config and defaults. Keys and the on-screen box work as before.
- The gold and XP parts are set up separately: if a game update breaks one, the other
  keeps working. An error while multiplying is logged at most 3 times, not on every pickup.
- The log confirms the first multiplied coin and gem ("[SELFCHECK] ... PASS" lines).
1.1.0
- New: Numpad * cycles the gold multiplier, Numpad / the experience multiplier, with a short
  on-screen notice. Keys and value lists are configurable; config defaults unchanged (x5 / x3).
1.0.0
- First release
