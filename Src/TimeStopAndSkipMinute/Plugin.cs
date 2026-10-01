using System;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using OffTheClock.Lib;
using UnityEngine;
using VampireSurvivors.Framework;
using VampireSurvivors.Objects.Characters;

namespace TimeStopAndSkipMinute;

// Time Stop and Skip Minute Keys: three keys during a run. Numpad 7 freezes / unfreezes the run clock, Numpad 8 jumps to
// the next minute, Numpad 9 heals you to full.
//
// Read from the game code (Vampire Survivors 1.16.107, GameAssembly.dll, 2026-09-30, our test notes):
// - The run clock is GameManager.SurvivedSeconds. GameManager.OnUpdate adds up real time only while
//   GameManager._canRunTickerTimer is true; each full second OnTickerCallback adds 1, updates the clock display and, on a
//   whole minute, calls Stage.CheckMinute (new enemy waves, bosses, events; the Reaper at the stage's end). The flag is set
//   by InitializeGameSession and cleared by ResetGameSession; nothing else in the game writes it during a run (its two
//   helpers RemoveTickerTimer/ResumeTickerTimer are never called). Freezing = clearing that flag: enemies, weapons and
//   pickups keep moving, only the clock (and so the minute events) stops.
// - Skip minute: the game's own debug function Stage.DebugNextMinute sets the clock to the last second of the minute so the
//   next tick lands on the full minute. We do the same with whole seconds (and on :59 jump a whole minute), so the game
//   processes the minute itself (CheckMinute). While frozen, the clock runs for exactly one tick and freezes again.
// - Heal: CharacterController.SetHealthToMax() (CurrentHp = MaxHp()), for every living local player. Dead players are not
//   revived.
// Keys only act during a run (not paused, not on the game-over screen) and never in online co-op. Vampire Survivors has no
// leaderboards; nothing is saved.
//
// Keys: Numpad 7/8/9 are unbound in the game (its Rewired keyboard map binds W A S D, arrows, Enter, Space, Keypad Enter,
// Esc, Page Up/Down, Tab, Left Alt/AltGr; hard-coded reads are Return/Down arrow and letters on the cheat/Secrets screens),
// not used by our other Vampire Survivors mods (Gold and XP Multiplier: Numpad * and /; Auto Level Up: Numpad 4), and not
// by the popular VS mods on Nexus (F1, F2, F9, F10, = and -). Num Lock must be on (with it off Windows sends Home/Up/PgUp).
//
// Self-checks: clock_frozen (3 s after freezing the clock still reads the same), minute_skipped (after a skip the clock
// reached the next minute), healed (after a heal the game's CurrentHp equals MaxHp()).
[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static ManualLogSource LogSrc;
    internal static ConfigEntry<KeyCode> FreezeKey, SkipKey, HealKey;
    internal static ConfigEntry<bool> ShowNotice;
    internal const string Id = "TimeStopAndSkipMinute";   // Src folder name: log prefix of the shared helpers + self-check id
    internal static OtcSelfCheck SC;
    internal static OtcNotice Notice;

    public override void Load()
    {
        LogSrc = Log;
        FreezeKey = Config.Bind("Keys", "FreezeClockKey", KeyCode.Keypad7,
            "Freezes the run clock; press again to let it run. Enemies and weapons keep moving, but no new minute starts, so the next wave, boss or the Reaper never comes. None = no key.");
        SkipKey = Config.Bind("Keys", "SkipMinuteKey", KeyCode.Keypad8,
            "Jumps the clock to the next minute (the game then starts that minute's enemies and events). Skipped minutes' bosses and events are not repeated. None = no key.");
        HealKey = Config.Bind("Keys", "HealKey", KeyCode.Keypad9,
            "Heals every living player to full health. Does not revive. None = no key.");
        ShowNotice = Config.Bind("Keys", "ShowOnScreenNotice", true, "Show a short message on screen after a key press (it is always written to the log).");

        OtcPatch.Init(Log, Id);
        OtcOnline.Configure(Log, Id, () => { var mm = MultiplayerManager.Instance; return mm != null && mm.IsOnlineMultiplayer; }, "online co-op");
        SC = new OtcSelfCheck(Log, Id);
        SC.Expect("clock_frozen", "in a run press Numpad 7, wait 3 seconds");
        SC.Expect("minute_skipped", "in a run press Numpad 8");
        SC.Expect("healed", "in a run take a hit, then press Numpad 9");

        if (!OtcHost.Start(this, Log, Id))
        {
            LogSrc.LogError($"{PluginInfo.PLUGIN_NAME}: the key reader could not be started. The mod does nothing.");
            SC.FailOpen("the keys could not be started");
            return;
        }
        var keys = new OtcKeys(Log, Id)
        {
            CanAct = Run.CanAct,
            OnBlocked = name => LogSrc.LogInfo($"{PluginInfo.PLUGIN_NAME}: '{name}' only works during a run (not paused, not online)."),
        };
        keys.Add("freeze/unfreeze clock", FreezeKey, Run.ToggleFreeze, cooldown: 0.2f);
        keys.Add("skip to next minute", SkipKey, Run.SkipMinute, cooldown: 0.2f);
        keys.Add("heal to full", HealKey, Run.Heal, cooldown: 0.2f);
        keys.Start();
        OtcHost.OnUpdate("clock", Run.Tick);
        Notice = new OtcNotice(Log, Id, () => ShowNotice.Value) { FontSize = 26 };
        Notice.Start();
        LogSrc.LogInfo($"{PluginInfo.PLUGIN_NAME} {PluginInfo.PLUGIN_VERSION} loaded - {OtcKeys.KeyName(FreezeKey.Value)} freeze clock, {OtcKeys.KeyName(SkipKey.Value)} next minute, {OtcKeys.KeyName(HealKey.Value)} heal (during runs; Num Lock on; off in online co-op)");
    }
}

internal static class PluginInfo
{
    public const string PLUGIN_GUID = "offtheclock.timestopandskipminute";
    public const string PLUGIN_NAME = "Time Stop and Skip Minute Keys";
    public const string PLUGIN_VERSION = "1.0.0";
}

// Everything runs on Unity's main thread (OtcKeys/OtcHost call from the host MonoBehaviour's Update, each in its own
// try/catch). Methods touching game members are separate (NoInlining), so a renamed member fails inside that catch.
internal static class Run
{
    static bool _frozen;
    static IntPtr _frozenRun;
    static bool _pulse;                 // frozen clock running for one tick after a skip
    static float _pulseTarget;
    static float _pulseStarted;
    // self-check bookkeeping
    static float _freezeAt = -1f, _freezeClock;
    static float _skipAt = -1f; static int _skipFromMinute; static float _skipTarget;

    static GameManager Gm() { try { return GM.Core; } catch { return null; } }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool CanAct()
    {
        var gm = Gm();
        if (gm == null || OtcOnline.IsOnline) return false;
        try { return !gm.IsPaused && !gm.InGameOverState; } catch { return false; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ToggleFreeze()
    {
        var gm = Gm();
        if (gm == null) return;
        if (!_frozen)
        {
            if (!gm._canRunTickerTimer) { Say("The clock is already stopped by the game.", "Clock already stopped"); return; }
            gm._canRunTickerTimer = false;
            _frozen = true; _pulse = false;
            _frozenRun = gm.Pointer;
            _freezeAt = Time.unscaledTime; _freezeClock = gm.SurvivedSeconds;
            Say($"Clock frozen at {Clock.Mmss(gm.SurvivedSeconds)} (press {OtcKeys.KeyName(Plugin.FreezeKey.Value)} again to let it run).", "Clock frozen " + Clock.Mmss(gm.SurvivedSeconds));
        }
        else
        {
            _frozen = false; _pulse = false; _freezeAt = -1f;
            gm._canRunTickerTimer = true;
            Say($"Clock running again from {Clock.Mmss(gm.SurvivedSeconds)}.", "Clock running");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void SkipMinute()
    {
        var gm = Gm();
        if (gm == null) return;
        float now = gm.SurvivedSeconds;
        float target = Clock.SkipTarget(now);
        gm._SurvivedSeconds_k__BackingField = target;
        _skipAt = Time.unscaledTime; _skipFromMinute = Clock.Minute(now); _skipTarget = target;
        if (_frozen)
        {
            // Let exactly one tick through so the game processes the new minute, then freeze again (Tick()).
            _pulse = true; _pulseTarget = target; _pulseStarted = Time.unscaledTime;
            gm._canRunTickerTimer = true;
        }
        try { gm.MainUI?.UpdateTimeDisplay(target); } catch { }
        Say($"Jumped to {Clock.Mmss(target + 1f)} (was {Clock.Mmss(now)}).", "Next minute: " + Clock.Mmss(target + 1f));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Heal()
    {
        var gm = Gm();
        var list = gm?.MainPlayers;
        if (list == null) return;
        int healed = 0;
        CharacterController first = null;
        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i];
            if (c == null) continue;
            bool dead = false;
            try { dead = c.IsDead; } catch { }
            if (dead) continue;
            c.SetHealthToMax();
            healed++;
            first ??= c;
        }
        if (healed == 0) { Say("Nobody to heal (no living player).", "Nobody to heal"); return; }
        Say(healed == 1 ? "Healed to full." : $"Healed {healed} players to full.", "Healed");
        if (Plugin.SC.Want("healed") && first != null)
        {
            float hp = first.CurrentHp, max = first.MaxHp();
            Plugin.SC.Check("healed", max > 0f && Math.Abs(hp - max) <= 0.01f + 0.0001f * max, "after the heal CurrentHp reads {0:0.##} of MaxHp {1:0.##}", hp, max);
        }
    }

    // Every frame (OtcHost "clock" callback).
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Tick()
    {
        if (!_frozen && _skipAt < 0f) return;
        var gm = Gm();
        if (gm == null || (_frozen && gm.Pointer != _frozenRun))
        {
            if (_frozen) LogSrc($"Time Stop: the run ended; the freeze is cleared.");
            _frozen = false; _pulse = false; _freezeAt = -1f; _skipAt = -1f;
            return;
        }
        float t = gm.SurvivedSeconds;
        if (_frozen && _pulse)
        {
            // One tick went through (or 3 s passed): freeze again.
            if (t >= _pulseTarget + 1f || Time.unscaledTime - _pulseStarted > 3f)
            {
                gm._canRunTickerTimer = false;
                _pulse = false;
                _freezeClock = t; _freezeAt = Time.unscaledTime;
            }
        }
        else if (_frozen && gm._canRunTickerTimer)
        {
            // Something started the clock (a new game session): our freeze is over.
            _frozen = false; _freezeAt = -1f;
            LogSrc("Time Stop: the game started the clock again (new run?); the freeze is cleared.");
        }
        if (_frozen && !_pulse && _freezeAt >= 0f && Time.unscaledTime - _freezeAt > 3f && Plugin.SC.Want("clock_frozen"))
        {
            Plugin.SC.Check("clock_frozen", Math.Abs(t - _freezeClock) < 0.5f, "3 s after freezing the clock reads {0} (frozen at {1})", Clock.Mmss(t), Clock.Mmss(_freezeClock));
            _freezeAt = -1f;
        }
        if (_skipAt >= 0f)
        {
            bool reached = Clock.Minute(t) > _skipFromMinute;
            bool paused = false;
            try { paused = gm.IsPaused; } catch { }
            if (reached || (!paused && Time.unscaledTime - _skipAt > 6f))
            {
                int stageMinute = -1;
                try { stageMinute = gm.Stage != null ? gm.Stage.CurrentMinute : -1; } catch { }
                if (Plugin.SC.Want("minute_skipped"))
                    Plugin.SC.Check("minute_skipped", reached, "clock {0} -> {1} after the skip (minute {2} -> {3}; the stage's own minute counter reads {4})",
                                    Clock.Mmss(_skipFromMinute * 60f), Clock.Mmss(t), _skipFromMinute, Clock.Minute(t), stageMinute);
                _skipAt = -1f;
            }
        }
    }

    static void Say(string log, string notice)
    {
        Plugin.LogSrc.LogInfo("Time Stop and Skip Minute: " + log);
        Plugin.Notice?.Show(notice);
    }

    static void LogSrc(string text) => Plugin.LogSrc.LogInfo(text);
}
