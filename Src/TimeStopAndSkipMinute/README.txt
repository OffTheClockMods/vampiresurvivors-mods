Time Stop and Skip Minute Keys - Vampire Survivors
Version 1.0.0
by Off the Clock Software

Three keys during a run: freeze the run clock, jump to the next minute, heal to full.

Keys (Num Lock on; change them in the config):
- Numpad 7  freeze / unfreeze the run clock. Enemies and weapons keep moving, but no new
            minute starts: no next wave, no next boss, no Reaper. Press again to let it run.
- Numpad 8  jump to the next minute. The game starts that minute's enemies and events itself.
            Bosses and events of minutes you jump over are not repeated. Works while the
            clock is frozen too (it runs one second, then freezes again).
- Numpad 9  heal every living player to full health. Does not revive.

How it works (checked in the game code, 1.16):
- The run clock only counts while a game switch is on; Numpad 7 turns that switch off and
  on. Numpad 8 does what the game's own debug "next minute" function does: it sets the clock
  to the last second of the minute, and the game's next tick starts the new minute.
- Keys only work during a run (not paused, not on the game-over screen).
- Off in online co-op. Nothing is written to your save.

Requirements:
- Vampire Survivors 1.16 (Steam, Unity version)
- BepInEx 6 Unity IL2CPP (Bleeding Edge, be.788 or newer)
- If the game crashes on the first BepInEx start: set UnityLogListening = false under
  [Logging] in BepInEx/config/BepInEx.cfg

Install:
1. Put TimeStopAndSkipMinute.dll in Vampire Survivors/BepInEx/plugins/
2. Start a run and press the keys

Config (created on first launch): BepInEx/config/offtheclock.timestopandskipminute.cfg
- FreezeClockKey      (default Keypad7)  None = no key
- SkipMinuteKey       (default Keypad8)  None = no key
- HealKey             (default Keypad9)  None = no key
- ShowOnScreenNotice  (default true)     short message on screen after a press

The game does not use these keys. Our other Vampire Survivors mods use Numpad * and /
(Gold and XP Multiplier) and Numpad 4 (Auto Level Up).

Log (BepInEx/LogOutput.log): one line per key press ("Clock frozen at 12:34", "Jumped to
13:00", "Healed to full").

Uninstall: delete TimeStopAndSkipMinute.dll (and the .cfg if you like).

Changelog
1.0.0
- First release
