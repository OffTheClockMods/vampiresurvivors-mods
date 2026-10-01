Rebind Keys - AZERTY and Custom Keys - Vampire Survivors
Version 1.0.0
by Off the Clock Software

Change the game's keyboard keys in a config file. ZQSD for AZERTY keyboards is one line:
Preset = AZERTY

What the game does without this mod (checked in the game code, 1.16):
- There is no key rebinding screen. The keyboard keys are fixed: W A S D and the arrows to
  move and in menus, Enter / Space / Keypad Enter to confirm, Esc to go back or pause,
  Page Up / Page Down for menu pages, Tab and Left Alt.

What this mod adds:
- Presets: AZERTY (Z Q S D to move) and ESDF (E S D F to move).
- Remap: your own list, old key = new key, for example W=Z, A=Q, Space=J.
- KeepOldKeysWorking (default on): the old key keeps working too, so nothing is lost. A key
  that becomes the new key of another action is moved anyway, so one key never does two
  things because of this mod.
- The log lists every key the game uses with its action, to help you write your list.
- Applied at the main menu and at every run start. Nothing is saved: remove the mod and the
  normal keys are back.
- Also works in online co-op: it only changes your own keyboard, nothing in the game.

Honest limits:
- A few keys are read by the game directly and stay as they are: Return and Down arrow on
  some prompts, and the letters on the intro cheat screen and the Secrets page.
- Controllers are not changed.

Requirements:
- Vampire Survivors 1.16 (Steam, Unity version)
- BepInEx 6 Unity IL2CPP (Bleeding Edge, be.788 or newer)
- If the game crashes on the first BepInEx start: set UnityLogListening = false under
  [Logging] in BepInEx/config/BepInEx.cfg

Install:
1. Put RebindKeys.dll in Vampire Survivors/BepInEx/plugins/
2. Start the game once, then set the config (below) and restart

Config (created on first launch): BepInEx/config/offtheclock.rebindkeys.cfg
- Preset              (default None)  None / AZERTY / ESDF
- Remap               (default empty) old = new, comma-separated, after the preset.
                      Key names: A-Z, 1 or Alpha1, Num5 or Keypad5, Up or UpArrow, Enter or
                      Return, Esc or Escape, Space, Tab, LeftShift ...
- KeepOldKeysWorking  (default true)

Log (BepInEx/LogOutput.log): the game's keys with their actions, then "2 key(s) added.
Active changes: W->Z, A->Q".

Uninstall: delete RebindKeys.dll (and the .cfg if you like).

Changelog
1.0.0
- First release
