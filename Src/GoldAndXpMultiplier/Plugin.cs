using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using OffTheClock.Lib;
using UnityEngine;
using VampireSurvivors.Framework;
using VampireSurvivors.Objects;
using VampireSurvivors.Objects.Characters;

namespace GoldAndXpMultiplier;

// Gold and XP Multiplier: gold and experience earned during a run are multiplied (config, x1 to x10 each).
// 1.1.0: one key cycles the gold multiplier, another the XP multiplier, through lists set in the config.
//
// Read from the game code (Vampire Survivors 1.16.107, GameAssembly.dll, 2026-09-29):
// - Every coin that reaches your total during a run goes through PlayerOptions.AddCoins(value, player):
//   coins and coin bags picked up, chest gold, Gold Fever, gold piles, merchant refunds. Inside, the game
//   computes value * GameManager.GoldMultiplier (the stage's/hyper's own multiplier) * the player's Greed and adds
//   the result to Coins and RunCoins. The prefix below scales `value` first, so it stacks with hyper mode and
//   Greed exactly like more coins would.
// - Experience goes through CharacterController.AddXp(value, mode): value * GameManager.ExperienceMultiplier
//   [* the arcana XP multiplier]. Mode IgnoreAll is the game's "exact amount" path and is left alone.
// Both prefixes do nothing in online co-op (MultiplayerManager.IsOnlineMultiplayer, the same check the game uses),
// and AddCoins is only scaled for coins a character earned in a loaded run (player != null, GM.Core alive), never for
// the menus' AddCoinsFlat path (achievement rewards, refunds), which passes no character.
// Vampire Survivors has no online leaderboards (no leaderboard code exists in the game), so nothing to mark.
//
// Keys (1.1.0): read with UnityEngine.Input on Unity's main thread (a MonoBehaviour's Update; same pattern as
// our BALL x PIT plugins). Legacy Input is active in this game: its own SecretsPage.Update calls Input.GetKey.
// Default keys Numpad * (gold) and Numpad / (XP) are unbound in the game: its Rewired keyboard map (the InputManager
// data in resources.assets, decoded 2026-09-29) binds only W A S D, the arrows, Enter, Space, Keypad Enter, Esc,
// Page Up/Down, Tab (speed toggle) and Left Alt/AltGr; the game's hard-coded key reads are Return/Down arrow and
// letters on the intro cheat and Secrets screens. Numpad * and / are not affected by Num Lock. None of our other
// Vampire Survivors mods uses keys, and the current popular VS mods on Nexus use F1/F2/F9/F10/=/- (checked 2026-09-29).
//
// 1.1.1: built on the shared Off the Clock helpers (Src\otc_bepinex): OtcPatcher applies the gold and XP hooks as
// two separate parts (one missing after a game update no longer switches off the other), OtcHost + OtcKeys read the
// keys (one action per key press, as before), OtcNotice draws the on-screen box (same size and text), OtcOnline is the
// online co-op guard (was VsOnline.cs), and an error inside a prefix is logged at most 3 times (was: every call).
// Self-checks: gold_multiplied (the first boosted coin was handed to the game as base x multiplier, and the run's gold
// (PlayerOptionsData.RunCoins) had grown when the next coin came), xp_multiplied (the first boosted gem was handed to
// the game as base x multiplier), cycle_key (optional: after a key press the next pickup uses the new value).
//
// 1.1.3: the gold/XP hooks and Cycle()'s own "current step" read now use a plain in-memory float (GoldMultLive/
// XpMultLive), never ConfigEntry.Value after startup. 2026-09-30 test session 20260930-122403: cycle_key FAILed
// (XP key set x25, the next gem still used x10) and the log showed the XP step stuck re-announcing "x25" on every
// later press instead of advancing to x50 - both match ConfigEntry.Value handing back the last value flushed to
// disk instead of the one just set (rapid presses write the cfg file several times within milliseconds; BepInEx's
// own file-watcher can reload in between one of those writes). Writing to the ConfigEntry is kept (cfg file, any
// config-editor UI) but nothing reads it back anymore.
[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static ManualLogSource LogSrc;
    internal static ConfigEntry<float> GoldMult;
    internal static ConfigEntry<float> XpMult;
    internal static ConfigEntry<KeyCode> GoldKey;
    internal static ConfigEntry<KeyCode> XpKey;
    internal static ConfigEntry<string> GoldStepsText;
    internal static ConfigEntry<string> XpStepsText;
    internal static ConfigEntry<bool> ShowNotice;
    internal static float[] GoldSteps, XpSteps;
    internal const string Id = "GoldAndXpMultiplier";   // Src folder name: log prefix of the shared helpers + self-check id
    internal static OtcSelfCheck SC;
    static OtcNotice _notice;

    // 1.1.3: the value the hooks and Cycle() actually read, kept in memory only. ConfigEntry.Value still gets written
    // (for the cfg file and any config-editor UI) but is never read back: rapid presses (Numpad * / held / tapped fast)
    // write the cfg file several times within milliseconds, and BepInEx's own file-watcher can reload the file in between
    // those writes, handing back the value that was last flushed to disk instead of the one just set - so both the hook
    // (wrong multiplier applied) and Cycle()'s own "current" read (NextStep stuck repeating the same step) could see a
    // stale value. See our test notes (cycle_key FAIL: XP key set x25, x10 used).
    internal static float GoldMultLive, XpMultLive;

    internal const KeyCode DefaultGoldKey = KeyCode.KeypadMultiply;
    internal const KeyCode DefaultXpKey = KeyCode.KeypadDivide;

    public override void Load()
    {
        LogSrc = Log;
        GoldMult = Config.Bind("Multipliers", "Gold", 5f, new ConfigDescription(
            "Gold earned during runs is multiplied by this (1 = the normal amount). Stacks with hyper mode and Greed. The gold key changes it in game (and saves the new value here).",
            new AcceptableValueRange<float>(1f, 100f)));
        XpMult = Config.Bind("Multipliers", "Experience", 3f, new ConfigDescription(
            "Experience earned during runs is multiplied by this (1 = the normal amount). Stacks with hyper mode and Growth. The XP key changes it in game (and saves the new value here).",
            new AcceptableValueRange<float>(1f, 100f)));
        GoldKey = Config.Bind("Keys", "GoldCycleKey", DefaultGoldKey,
            "Each press moves the gold multiplier to the next value of GoldSteps (after the last it starts again at the first). None = no key.");
        XpKey = Config.Bind("Keys", "ExperienceCycleKey", DefaultXpKey,
            "Each press moves the experience multiplier to the next value of ExperienceSteps. None = no key.");
        GoldStepsText = Config.Bind("Keys", "GoldSteps", "1, 2, 5, 10, 25, 50, 100", "Values the gold key cycles through (1 to 100, comma-separated).");
        XpStepsText = Config.Bind("Keys", "ExperienceSteps", "1, 2, 3, 5, 10, 25, 50, 100", "Values the experience key cycles through (1 to 100, comma-separated).");
        ShowNotice = Config.Bind("Keys", "ShowOnScreenNotice", true, "Show the new value on screen for a moment after a key press (it is always written to the log).");
        // 1.1.2: the step lists go up to x100. A config still holding the old default list gets the new default
        // (a list the player changed is kept as is).
        if (GoldStepsText.Value.Replace(" ", "") == "1,2,5,10") GoldStepsText.Value = (string)GoldStepsText.DefaultValue;
        if (XpStepsText.Value.Replace(" ", "") == "1,2,3,5,10") XpStepsText.Value = (string)XpStepsText.DefaultValue;
        GoldSteps = ParseSteps(GoldStepsText.Value, new[] { 1f, 2f, 5f, 10f, 25f, 50f, 100f }, "GoldSteps");
        XpSteps = ParseSteps(XpStepsText.Value, new[] { 1f, 2f, 3f, 5f, 10f, 25f, 50f, 100f }, "ExperienceSteps");
        GoldMultLive = GoldMult.Value;
        XpMultLive = XpMult.Value;

        OtcPatch.Init(Log, Id);
        OtcOnline.Configure(Log, Id, () => { var mm = MultiplayerManager.Instance; return mm != null && mm.IsOnlineMultiplayer; }, "online co-op");
        SC = new OtcSelfCheck(Log, Id);
        SC.Expect("gold_multiplied", "start a run and pick up two coins", grace: 2);
        SC.Expect("xp_multiplied", "start a run and pick up an XP gem");
        SC.Expect("cycle_key", "in a run press Numpad * or Numpad /, then pick up a coin or a gem", optional: true);

        // Gold and XP are separate parts: if a game update removes one hook, the other keeps working.
        var p = new OtcPatcher(new Harmony(PluginInfo.PLUGIN_GUID), Log, Id, SC);
        bool gold = p.Class(typeof(GoldPatch), feature: "gold multiplier (PlayerOptions.AddCoins)");
        bool xp = p.Class(typeof(XpPatch), feature: "XP multiplier (CharacterController.AddXp)");
        p.Finish();
        if (!gold) SC.Fail("gold_multiplied", "the gold hook could not be applied (see the WARNING above)");
        if (!xp) SC.Fail("xp_multiplied", "the XP hook could not be applied (see the WARNING above)");
        if (!gold && !xp)
        {
            LogSrc.LogError($"{PluginInfo.PLUGIN_NAME}: could not hook the game's coin/XP code (game update?). The mod does nothing.");
            SC.FailOpen("no hook could be applied");
            return;
        }

        string keys = "";
        if (OtcHost.Start(this, Log, Id))
        {
            var k = new OtcKeys(Log, Id);
            k.Add("gold cycle", GoldKey, () => Cycle(true));
            k.Add("XP cycle", XpKey, () => Cycle(false));
            k.Start();
            _notice = new OtcNotice(Log, Id, () => ShowNotice.Value) { FontSize = 26 };
            _notice.Start();
            keys = $"; {OtcKeys.KeyName(GoldKey.Value)} cycles gold ({StepsText(GoldSteps)}), {OtcKeys.KeyName(XpKey.Value)} cycles XP ({StepsText(XpSteps)})";
        }
        else
        {
            LogSrc.LogWarning($"{PluginInfo.PLUGIN_NAME}: the cycle keys are unavailable; the config values still work.");
            SC.Fail("cycle_key", "the keys could not be started");
        }
        LogSrc.LogInfo($"{PluginInfo.PLUGIN_NAME} {PluginInfo.PLUGIN_VERSION} loaded - gold x{GoldMult.Value:0.##}, XP x{XpMult.Value:0.##} during runs{keys} (off in online co-op)");
    }

    static float[] ParseSteps(string text, float[] fallback, string name)
    {
        var list = new List<float>();
        foreach (var part in (text ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (float.TryParse(part.Trim().TrimStart('x', 'X'), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && v >= 1f && v <= 100f && !list.Contains(v))
                list.Add(v);
        }
        if (list.Count == 0)
        {
            LogSrc.LogWarning($"{PluginInfo.PLUGIN_NAME}: {name} has no valid values (1 to 100), using {StepsText(fallback)}.");
            return fallback;
        }
        list.Sort();
        return list.ToArray();
    }

    internal static string StepsText(float[] steps)
    {
        var s = new List<string>();
        foreach (var v in steps) s.Add("x" + v.ToString("0.##", CultureInfo.InvariantCulture));
        return string.Join("/", s);
    }

    // Next value after `current` in the sorted list; after the last one, back to the first.
    internal static float NextStep(float[] steps, float current)
    {
        foreach (var v in steps) if (v > current + 0.001f) return v;
        return steps[0];
    }

    // A cycle key was pressed (OtcKeys calls this on Unity's main thread, inside its own try/catch).
    static void Cycle(bool gold)
    {
        var entry = gold ? GoldMult : XpMult;
        float current = gold ? GoldMultLive : XpMultLive;
        float next = NextStep(gold ? GoldSteps : XpSteps, current);
        entry.Value = next;                       // BepInEx saves the config file on change (not read back - see GoldMultLive/XpMultLive)
        if (gold) GoldMultLive = next; else XpMultLive = next;
        Patches.ValueChanged(gold);
        SelfChecks.KeyPressed(gold, next);
        string what = gold ? "Gold" : "XP";
        _notice?.Show($"{what} x{next.ToString("0.##", CultureInfo.InvariantCulture)}");
        bool online = OtcOnline.IsOnline;
        LogSrc.LogInfo($"Gold and XP Multiplier: {what} multiplier now x{next:0.##} (cycle: {StepsText(gold ? GoldSteps : XpSteps)})" +
                       (online ? " - not applied while in online co-op." : (next <= 1f ? " - normal amount." : ".")));
    }
}

internal static class PluginInfo
{
    public const string PLUGIN_GUID = "offtheclock.goldandxpmultiplier";
    public const string PLUGIN_NAME = "Gold and XP Multiplier";
    public const string PLUGIN_VERSION = "1.1.3";
}

// Shared state of the two prefixes (run detection, "first multiplied pickup" log lines).
internal static class Patches
{
    static IntPtr _runSeen;          // GameManager of the run we last logged for
    internal static bool LoggedGold, LoggedXp;

    // After a key press the next multiplied pickup is logged again, so the log proves the new value is used.
    internal static void ValueChanged(bool gold) { if (gold) LoggedGold = false; else LoggedXp = false; }

    internal static bool InRun(out bool newRun)
    {
        newRun = false;
        GameManager gm;
        try { gm = GM.Core; } catch { return false; }
        if (gm == null) return false;
        if (gm.Pointer != _runSeen) { _runSeen = gm.Pointer; LoggedGold = false; LoggedXp = false; newRun = true; }
        return true;
    }
}

// `player` is the character that earned the coins. The menus' path (achievement rewards, refunds:
// PlayerOptions.AddCoinsFlat) calls AddCoins with player == null, so that stays unmultiplied.
[HarmonyPatch(typeof(PlayerOptions), nameof(PlayerOptions.AddCoins), new[] { typeof(float), typeof(CharacterController) })]
internal static class GoldPatch
{
    [HarmonyPrefix]
    static void BeforeAddCoins(ref float value, CharacterController player)
    {
        try
        {
            SelfChecks.BeforeAnyCoin();           // read-only: judges the previous boosted coin (the game has added it by now)
            float m = Plugin.GoldMultLive;
            if (m <= 1f || value <= 0f || player == null || OtcOnline.IsOnline) return;
            if (!Patches.InRun(out _)) return;
            float before = value;
            value *= m;
            SelfChecks.GoldBoosted(before, value, m);
            if (!Patches.LoggedGold)
            {
                Patches.LoggedGold = true;
                Plugin.LogSrc.LogInfo($"Gold and XP Multiplier: gold x{m:0.##} active (pickup {before:0.##} -> {value:0.##}).");
            }
        }
        catch (Exception ex) { OtcPatch.Report(ex, "gold prefix"); }
    }
}

[HarmonyPatch(typeof(CharacterController), nameof(CharacterController.AddXp))]
internal static class XpPatch
{
    [HarmonyPrefix]
    static void BeforeAddXp(ref float value, XPMultiplierMode multiplierMode)
    {
        try
        {
            float m = Plugin.XpMultLive;
            if (m <= 1f || value <= 0f || multiplierMode == XPMultiplierMode.IgnoreAll || OtcOnline.IsOnline) return;
            Patches.InRun(out _);
            float before = value;
            value *= m;
            SelfChecks.XpBoosted(before, value, m);
            if (!Patches.LoggedXp)
            {
                Patches.LoggedXp = true;
                Plugin.LogSrc.LogInfo($"Gold and XP Multiplier: XP x{m:0.##} active (gem {before:0.##} -> {value:0.##}).");
            }
        }
        catch (Exception ex) { OtcPatch.Report(ex, "XP prefix"); }
    }
}

// Read-only self-checks (our test tooling). Each costs one lookup per pickup until it has a result.
internal static class SelfChecks
{
    static bool _goldPending;
    static float _gBase, _gSent, _gMult, _gRun0;
    static IntPtr _gRunPtr;
    static float _keyGold, _keyXp;    // value set by the last key press (> 1), 0 = none pending

    static OtcSelfCheck SC => Plugin.SC;

    static bool RunGold(out float coins, out IntPtr run)
    {
        coins = 0f; run = IntPtr.Zero;
        var gm = GM.Core;
        var cfg = gm?.PlayerOptions?.Config;
        if (cfg == null) return false;
        run = gm.Pointer;
        coins = cfg.RunCoins;
        return true;
    }

    static bool Same(float a, float b) => Math.Abs(a - b) <= 0.01f + 0.0001f * Math.Abs(b);

    // Every entry point has its own try/catch: a self-check must never change what the mod does.
    // First boosted coin: remember what was handed to the game and the run gold before it.
    internal static void GoldBoosted(float before, float sent, float m)
    {
        try { GoldBoostedCore(before, sent, m); } catch (Exception ex) { OtcPatch.Report(ex, "gold self-check"); }
    }

    static void GoldBoostedCore(float before, float sent, float m)
    {
        if (_keyGold > 0f)
        {
            SC.Check("cycle_key", Same(m, _keyGold), "gold key set x{0}; the next coin used x{1} ({2:0.##} -> {3:0.##})", _keyGold, m, before, sent);
            _keyGold = 0f;
        }
        if (_goldPending || !SC.Want("gold_multiplied")) return;
        if (!RunGold(out _gRun0, out _gRunPtr))
        {
            SC.Check("gold_multiplied", Same(sent, before * m), "coin {0:0.##} -> {1:0.##} handed to the game (x{2}); run gold not readable", before, sent, m);
            return;
        }
        _gBase = before; _gSent = sent; _gMult = m;
        _goldPending = true;
    }

    // Next coin (any): the game has finished adding the boosted one, so RunCoins must have grown.
    internal static void BeforeAnyCoin()
    {
        if (!_goldPending) return;
        try { JudgeGold(); } catch (Exception ex) { OtcPatch.Report(ex, "gold self-check"); }
    }

    static void JudgeGold()
    {
        _goldPending = false;
        if (!RunGold(out float now, out IntPtr run) || run != _gRunPtr) return;   // new run or no run: wait for the next boosted coin
        float gained = now - _gRun0;
        SC.Check("gold_multiplied", Same(_gSent, _gBase * _gMult) && gained > 0f,
                 "coin {0:0.##} -> {1:0.##} handed to the game (x{2}); run gold +{3:0.##} when it was added (the game's own Greed/stage bonus x{4:0.##} on top)",
                 _gBase, _gSent, _gMult, gained, _gSent > 0f ? gained / _gSent : 0f);
    }

    internal static void XpBoosted(float before, float sent, float m)
    {
        try { XpBoostedCore(before, sent, m); } catch (Exception ex) { OtcPatch.Report(ex, "XP self-check"); }
    }

    static void XpBoostedCore(float before, float sent, float m)
    {
        if (_keyXp > 0f)
        {
            SC.Check("cycle_key", Same(m, _keyXp), "XP key set x{0}; the next gem used x{1} ({2:0.##} -> {3:0.##})", _keyXp, m, before, sent);
            _keyXp = 0f;
        }
        if (SC.Want("xp_multiplied"))
            SC.Check("xp_multiplied", Same(sent, before * m), "gem {0:0.##} -> {1:0.##} handed to the game (x{2})", before, sent, m);
    }

    // Only values above x1 can be seen in a pickup (x1 = the prefix leaves it alone).
    internal static void KeyPressed(bool gold, float value)
    {
        if (!SC.Want("cycle_key")) return;
        if (gold) _keyGold = value > 1f ? value : 0f;
        else _keyXp = value > 1f ? value : 0f;
    }
}
