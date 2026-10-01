using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using OffTheClock.Lib;
using VampireSurvivors.Framework;
using VampireSurvivors.Objects.Characters;

namespace AlwaysPickCoinBag;

// Always Pick Coin Bag: once every weapon and passive is maxed, level-ups take the coin bag on their own instead of
// stopping the game with a level-up screen that only offers the coin bag or the Floor Chicken.
//
// Read from the game code (Vampire Survivors 1.16.107, GameAssembly.dll, re-checked 2026-09-29):
// GameManager.SwapToLevelUpScreen first asks LevelUpFactory.HasPowerupsInStore(character). If something is left, the
// normal level-up screen opens (or, with the "Random level-ups" option, a random pick is applied). If nothing is left
// and GameManager.CanLimitBreak() is false (the stage's Limit Break option is off):
//   - the "Random level-ups" option (PlayerOptionsData.SelectedRandomLevels) sets CharacterController.AlwaysRoast;
//   - AlwaysRoast and health not full -> ApplyRoastLevelUp (Floor Chicken heal), no screen;
//   - otherwise AlwaysCoinBag or AlwaysRoast -> ApplyCoinBagLevelUp, no screen;
//   - otherwise the level-up screen opens with the coin bag / Floor Chicken choice (vanilla default).
// With Limit Break on (and limit breaks available) the game offers limit breaks instead; AlwaysCoinBag isn't consulted
// there (this mod doesn't change that).
// ApplyCoinBagLevelUp fires the game's AutoLevelUpSignal, spawns and collects a coin bag, and shows the game's own
// feedback above the character: the coin icon with the gold amount (GizmoManager.DisplayIconOverhead) and the
// "LEVEL UP" text (GizmoManager.DisplayLevelUp). That overhead "LEVEL UP" without a pause IS the coin bag being taken.
// In the unmodded game AlwaysCoinBag is only set by a hidden level-up item (AlwaysCoinBag2 pickup). This mod sets the
// flag(s) on your character when the run starts (CharacterController.AfterFullInitialization).
// 1.0.1: logs every auto-pick (first 5 per run, then every 10th with the count) via a postfix on
// GameManager.ApplyCoinBagLevelUp / ApplyRoastLevelUp, and at run start says when Limit Break or Random level-ups
// changes what happens. Off in online co-op (MultiplayerManager.IsOnlineMultiplayer).
// 1.0.2: built on the shared Off the Clock helpers (Src\otc_bepinex): OtcPatcher applies the run-start hook
// (required) and the two log hooks (optional, each on its own: a missing one no longer switches off the mod), OtcOnline
// is the online co-op guard (was VsOnline.cs), errors in the hooks are logged at most 3 times (were silently dropped).
// Self-checks: flag_set (CharacterController.AlwaysCoinBag reads true after the run-start hook) and coin_bag_picked
// (optional: the game took a coin bag on its own, and the flag still read true at that moment).
[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static ManualLogSource LogSrc;
    internal static ConfigEntry<bool> CoinBag;
    internal static ConfigEntry<bool> Roast;
    internal const string Id = "AlwaysPickCoinBag";   // Src folder name: log prefix of the shared helpers + self-check id
    internal static OtcSelfCheck SC;

    public override void Load()
    {
        LogSrc = Log;
        CoinBag = Config.Bind("General", "AlwaysCoinBag", true,
            "When everything is maxed, level-ups take the coin bag automatically (no level-up screen).");
        Roast = Config.Bind("General", "AlsoRoastWhenHurt", false,
            "Also take the Floor Chicken (heal) instead of the coin bag while your health is not full (the game's own rule for its \"Random level-ups\" option).");
        OtcPatch.Init(Log, Id);
        OtcOnline.Configure(Log, Id, () => { var mm = MultiplayerManager.Instance; return mm != null && mm.IsOnlineMultiplayer; }, "online co-op");
        SC = new OtcSelfCheck(Log, Id);
        SC.Expect("flag_set", "start a run");
        SC.Expect("coin_bag_picked", "max every weapon and passive in a run (Limit Break off), then level up", optional: true);

        // The run-start hook is the mod; the two others only log the auto-picks, so each may fail alone.
        var p = new OtcPatcher(new Harmony(PluginInfo.PLUGIN_GUID), Log, Id, SC);
        p.Class(typeof(RunStartPatch), required: true, feature: "run-start flag (CharacterController.AfterFullInitialization)");
        if (!p.Class(typeof(CoinBagLogPatch), feature: "coin bag log (GameManager.ApplyCoinBagLevelUp)"))
            SC.Fail("coin_bag_picked", "the coin bag log hook could not be applied (the auto-pick itself still works)");
        p.Class(typeof(RoastLogPatch), feature: "Floor Chicken log (GameManager.ApplyRoastLevelUp)");
        if (!p.Finish()) return;
        LogSrc.LogInfo($"{PluginInfo.PLUGIN_NAME} {PluginInfo.PLUGIN_VERSION} loaded - coin bag {(CoinBag.Value ? "on" : "off")}, Floor Chicken when hurt {(Roast.Value ? "on" : "off")} (off in online co-op)");
    }
}

internal static class PluginInfo
{
    public const string PLUGIN_GUID = "offtheclock.alwayspickcoinbag";
    public const string PLUGIN_NAME = "Always Pick Coin Bag";
    public const string PLUGIN_VERSION = "1.0.2";
}

internal static class Patches
{
    const int LogFirst = 5;      // log each of the first 5 auto-picks per run
    const int LogEvery = 10;     // then every 10th with the running count
    static IntPtr _run;          // GameManager of the current run
    static int _bags, _roasts;

    static void CheckRun()
    {
        IntPtr gm = IntPtr.Zero;
        try { var core = GM.Core; if (core != null) gm = core.Pointer; } catch { }
        if (gm != _run) { _run = gm; _bags = 0; _roasts = 0; }
    }

    internal static void AfterCharacterReady(CharacterController __instance)
    {
        try
        {
            if (__instance == null || OtcOnline.IsOnline) return;
            if (!Plugin.CoinBag.Value && !Plugin.Roast.Value) return;
            if (Plugin.CoinBag.Value) __instance.AlwaysCoinBag = true;
            if (Plugin.Roast.Value) __instance.AlwaysRoast = true;
            CheckRun();
            Plugin.LogSrc.LogInfo($"Always Pick Coin Bag: {SafeName(__instance)} will auto-pick " +
                                  (Plugin.Roast.Value ? "the Floor Chicken when hurt, otherwise the coin bag" : "the coin bag") +
                                  " once everything is maxed (you'll see the game's overhead \"LEVEL UP\" and a coin icon instead of the level-up screen).");
            LogStageOptions();
            // Self-check (read-only): the game's own getter reports the flag.
            if (Plugin.SC.Want("flag_set"))
            {
                try
                {
                    if (!Plugin.CoinBag.Value) Plugin.SC.Wait("flag_set", "AlwaysCoinBag is off in the config");
                    else Plugin.SC.Check("flag_set", __instance.AlwaysCoinBag, "{0}: AlwaysCoinBag reads {1} after the run-start hook", SafeName(__instance), __instance.AlwaysCoinBag);
                }
                catch (Exception ex) { OtcPatch.Report(ex, "flag self-check"); }
            }
        }
        catch (Exception ex)
        {
            if (OtcPatch.ErrorCount("run-start flag") == 0) Plugin.SC.Fail("flag_set", "could not set the flag: " + ex.GetType().Name + ": " + ex.Message);
            OtcPatch.Report(ex, "run-start flag");
        }
    }

    // Tell the player when the stage options change what happens (read-only).
    static void LogStageOptions()
    {
        try
        {
            var cfg = GM.Core?.PlayerOptions?.Config;
            if (cfg == null) return;
            if (cfg.SelectedLimitBreak)
                Plugin.LogSrc.LogInfo("Always Pick Coin Bag: Limit Break is on for this run - once everything is maxed the game offers limit breaks instead, so the coin bag is only auto-picked when no limit break is left.");
            if (cfg.SelectedRandomLevels)
                Plugin.LogSrc.LogInfo("Always Pick Coin Bag: \"Random level-ups\" is on - the game already auto-picks on its own (Floor Chicken when hurt, otherwise the coin bag).");
        }
        catch (Exception ex) { OtcPatch.Report(ex, "stage options log"); }
    }

    internal static void Count(bool bag, CharacterController player)
    {
        try
        {
            if (OtcOnline.IsOnline) return;
            CheckRun();
            if (bag && Plugin.SC.Want("coin_bag_picked"))
            {
                try { CheckPick(player); } catch (Exception ex) { OtcPatch.Report(ex, "auto-pick self-check"); }
            }
            int n = bag ? ++_bags : ++_roasts;
            if (n > LogFirst && n % LogEvery != 0) return;
            string what = bag ? "coin bag" : "Floor Chicken";
            string lvl = "";
            try { if (player != null) lvl = $", level {player.Level}"; } catch { }
            Plugin.LogSrc.LogInfo(n <= LogFirst
                ? $"Always Pick Coin Bag: {what} auto-picked ({SafeName(player)}{lvl}; #{n} this run{(n == LogFirst ? ", from now on every " + LogEvery + "th is logged" : "")})."
                : $"Always Pick Coin Bag: {what} auto-picked {n} times this run ({SafeName(player)}{lvl}).");
        }
        catch (Exception ex) { OtcPatch.Report(ex, "auto-pick log"); }
    }

    // Self-check (read-only): the game took a coin bag on its own while our flag was still set. With the game's own
    // "Random level-ups" option the game auto-picks without the mod, so such a pick proves nothing about the mod.
    static void CheckPick(CharacterController player)
    {
        if (!Plugin.CoinBag.Value) { Plugin.SC.Wait("coin_bag_picked", "AlwaysCoinBag is off in the config"); return; }
        bool random = false;
        try { random = GM.Core?.PlayerOptions?.Config?.SelectedRandomLevels ?? false; } catch { }
        if (random) { Plugin.SC.Wait("coin_bag_picked", "Random level-ups is on (the game auto-picks by itself); turn it off to test the mod"); return; }
        if (player == null) return;
        int lvl = -1;
        try { lvl = player.Level; } catch { }
        Plugin.SC.Check("coin_bag_picked", player.AlwaysCoinBag, "{0} at level {1}: coin bag taken without a level-up screen; AlwaysCoinBag still reads {2}",
                        SafeName(player), lvl, player.AlwaysCoinBag);
    }

    static string SafeName(CharacterController c)
    {
        try { return c.CharacterType.ToString(); } catch { return "the character"; }
    }
}

// One patch class per part, so that OtcPatcher can apply (and skip) each on its own.
[HarmonyPatch(typeof(CharacterController), nameof(CharacterController.AfterFullInitialization))]
internal static class RunStartPatch
{
    [HarmonyPostfix]
    static void AfterCharacterReady(CharacterController __instance) { try { Patches.AfterCharacterReady(__instance); } catch (Exception ex) { OtcPatch.Report(ex, "run-start postfix"); } }
}

[HarmonyPatch(typeof(GameManager), nameof(GameManager.ApplyCoinBagLevelUp))]
internal static class CoinBagLogPatch
{
    [HarmonyPostfix]
    static void AfterCoinBag(CharacterController player) { try { Patches.Count(true, player); } catch (Exception ex) { OtcPatch.Report(ex, "coin bag postfix"); } }
}

[HarmonyPatch(typeof(GameManager), nameof(GameManager.ApplyRoastLevelUp))]
internal static class RoastLogPatch
{
    [HarmonyPostfix]
    static void AfterRoast(CharacterController player) { try { Patches.Count(false, player); } catch (Exception ex) { OtcPatch.Report(ex, "Floor Chicken postfix"); } }
}
