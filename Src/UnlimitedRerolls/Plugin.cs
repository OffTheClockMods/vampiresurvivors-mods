using System;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using OffTheClock.Lib;
using VampireSurvivors.Framework;
using VampireSurvivors.Objects;
using VampireSurvivors.Objects.Characters;
using VampireSurvivors.UI;

namespace UnlimitedRerolls;

// Unlimited Rerolls, Skips and Banishes: the level-up screen always has rerolls, skips and banishes left
// (topped up to the configured number every time the screen opens). Optional: extra revivals.
//
// Read from the game code (Vampire Survivors 1.16.107, GameAssembly.dll, 2026-09-29):
// The three counts live on the character's PlayerModifierStats (ReRolls, Skips, Banish; "EggFloat" numbers that
// include golden-egg bonuses). LevelUpPage.ValidateButtonStates reads them (> 0 enables each button) each time the
// screen is shown, and GameManager.OnReRollLevelUp / OnLevelUpSkipped / OnLevelUpBanish subtract one (unless the
// Recycle stat preserves it). This mod runs before ValidateButtonStates and raises any count that is below the
// configured amount, so the buttons never run out. Revivals (EggDouble) work the same way: the character keeps at
// least N unused revivals (Revivals - UsedRevivals), topped up on the level-up screen and after each revival.
// Off in online co-op (MultiplayerManager.IsOnlineMultiplayer). No leaderboards exist in the game.
//
// 1.0.1: built on the shared Off the Clock helpers (Src\otc_bepinex): OtcPatcher applies the level-up screen hook
// (required) and the revival hook (optional, on its own: a missing one no longer switches off the mod), OtcOnline is the
// online co-op guard (was VsOnline.cs), an error in a hook is logged at most 3 times (was: every level-up screen).
// Self-check counts_topped: right after a top-up, the game's own getters (EggFloat.GetValue, bonuses included) read at
// least the configured amounts.
//
// 1.0.2 (hardening for Vampire Survivors 1.17, our test notes): the level-up screen prefix no longer touches a
// game member itself; LevelUpPage.GetCharacterControllingUi() moved into OnLevelUpScreen() (NoInlining). If a game update
// renames it, .NET fails to compile OnLevelUpScreen() at its first call, inside our try/catch: logged, counts not topped
// up, and the game's ValidateButtonStates still runs. Before, the prefix itself failed to compile outside any try/catch;
// Il2CppInterop's trampoline then skipped the original ValidateButtonStates (buttons not refreshed). counts_topped now
// also FAILs with the reason when the top-up falls back like that (it only waited before).
[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static ManualLogSource LogSrc;
    internal static ConfigEntry<int> Rerolls, Skips, Banishes, Revivals;
    internal const string Id = "UnlimitedRerolls";   // Src folder name: log prefix of the shared helpers + self-check id
    internal static OtcSelfCheck SC;

    public override void Load()
    {
        LogSrc = Log;
        var range = new AcceptableValueRange<int>(0, 9999);
        Rerolls = Config.Bind("Amounts", "Rerolls", 999, new ConfigDescription("Rerolls are kept at least at this number on every level-up screen. 0 = leave the game's count alone.", range));
        Skips = Config.Bind("Amounts", "Skips", 999, new ConfigDescription("Skips are kept at least at this number on every level-up screen. 0 = leave the game's count alone.", range));
        Banishes = Config.Bind("Amounts", "Banishes", 999, new ConfigDescription("Banishes are kept at least at this number on every level-up screen. 0 = leave the game's count alone.", range));
        Revivals = Config.Bind("Amounts", "Revivals", 0, new ConfigDescription("Unused revivals are kept at least at this number (topped up on the level-up screen and after each revival). 0 = leave the game's count alone.", range));
        OtcPatch.Init(Log, Id);
        OtcOnline.Configure(Log, Id, () => { var mm = MultiplayerManager.Instance; return mm != null && mm.IsOnlineMultiplayer; }, "online co-op");
        SC = new OtcSelfCheck(Log, Id);
        SC.Expect("counts_topped", "level up once in a run");
        var p = new OtcPatcher(new Harmony(PluginInfo.PLUGIN_GUID), Log, Id, SC);
        p.Class(typeof(Patches), required: true, feature: "level-up screen top-up (LevelUpPage.ValidateButtonStates)");
        p.Class(typeof(RevivalPatch), feature: "revival top-up (CharacterController.PerformRevival)");
        if (!p.Finish()) return;
        LogSrc.LogInfo($"{PluginInfo.PLUGIN_NAME} {PluginInfo.PLUGIN_VERSION} loaded - rerolls {Rerolls.Value}, skips {Skips.Value}, banishes {Banishes.Value}, revivals {(Revivals.Value > 0 ? Revivals.Value.ToString() : "unchanged")} (0 = unchanged; off in online co-op)");
    }
}

internal static class PluginInfo
{
    public const string PLUGIN_GUID = "offtheclock.unlimitedrerolls";
    public const string PLUGIN_NAME = "Unlimited Rerolls Skips and Banishes";
    public const string PLUGIN_VERSION = "1.0.2";
}

internal static class Patches
{
    static bool _loggedThisRun;
    static IntPtr _lastCharacter;

    [HarmonyPatch(typeof(LevelUpPage), nameof(LevelUpPage.ValidateButtonStates))]
    [HarmonyPrefix]
    static void BeforeValidateButtons(LevelUpPage __instance)
    {
        // Only our own members here (1.0.2): a renamed game member must fail inside the try (in OnLevelUpScreen).
        try
        {
            if (OtcOnline.IsOnline) return;
            OnLevelUpScreen(__instance);
        }
        catch (Exception ex)
        {
            OtcPatch.Report(ex, "level-up screen top-up");
            FellBack(ex);
        }
    }

    // NoInlining: must stay its own method, so a missing game member throws at the call inside the prefix's try.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void OnLevelUpScreen(LevelUpPage page)
    {
        if (page == null) return;
        var character = page.GetCharacterControllingUi();
        if (character == null) return;
        TopUp(character, "level-up screen");
    }

    // Self-check: the top-up fell back (exception) = counts not topped up this time; say why (first time only).
    static void FellBack(Exception error)
    {
        try
        {
            var sc = Plugin.SC;
            if (sc != null && sc.State("counts_topped") != "FAIL")
                sc.Fail("counts_topped", "level-up screen top-up skipped (the game's own counts stay): {0}: {1}", error.GetType().Name, error.Message);
        }
        catch (Exception ex) { OtcPatch.Report(ex, "top-up self-check"); }
    }

    internal static void AfterRevival(CharacterController __instance)
    {
        try
        {
            if (__instance == null || Plugin.Revivals.Value <= 0 || OtcOnline.IsOnline) return;
            var stats = __instance.PlayerStats;
            if (stats == null) return;
            if (TopUpRevivals(stats))
                Plugin.LogSrc.LogInfo($"Unlimited Rerolls: revivals topped up after a revival (unused now {Plugin.Revivals.Value}).");
        }
        catch (Exception ex)
        {
            OtcPatch.Report(ex, "revival top-up");
        }
    }

    static void TopUp(CharacterController character, string where)
    {
        var stats = character.PlayerStats;
        if (stats == null) return;
        if (character.Pointer != _lastCharacter) { _lastCharacter = character.Pointer; _loggedThisRun = false; }

        bool changed = false;
        changed |= TopUpFloat(stats.ReRolls, Plugin.Rerolls.Value);
        changed |= TopUpFloat(stats.Skips, Plugin.Skips.Value);
        changed |= TopUpFloat(stats.Banish, Plugin.Banishes.Value);
        if (Plugin.Revivals.Value > 0) changed |= TopUpRevivals(stats);

        if (Plugin.SC.Want("counts_topped")) CheckCounts(stats, where);
        if (changed && !_loggedThisRun)
        {
            _loggedThisRun = true;
            Plugin.LogSrc.LogInfo($"Unlimited Rerolls: counts topped up on the {where} (rerolls {Read(stats.ReRolls)}, skips {Read(stats.Skips)}, banishes {Read(stats.Banish)}" +
                                  (Plugin.Revivals.Value > 0 ? $", unused revivals {stats.Revivals.GetValue() - stats.UsedRevivals:0}" : "") + ").");
        }
    }

    // EggFloat.GetValue() includes golden-egg bonuses; SetValue sets the base value, so the shown number is >= target.
    static bool TopUpFloat(VampireSurvivors.Framework.NumberTypes.EggFloat stat, int target)
    {
        if (target <= 0 || stat == null) return false;
        if (stat.GetValue() >= target) return false;
        stat.SetValue(target);
        return true;
    }

    static bool TopUpRevivals(PlayerModifierStats stats)
    {
        var rev = stats.Revivals;
        if (rev == null) return false;
        double want = stats.UsedRevivals + Plugin.Revivals.Value;
        if (rev.GetValue() >= want) return false;
        rev.SetValue(want);
        return true;
    }

    // Self-check (read-only): after the top-up the game's own getters report at least the configured amounts.
    static void CheckCounts(PlayerModifierStats stats, string where)
    {
        try
        {
            int r = Plugin.Rerolls.Value, s = Plugin.Skips.Value, b = Plugin.Banishes.Value;
            if (r <= 0 && s <= 0 && b <= 0) { Plugin.SC.Wait("counts_topped", "Rerolls, Skips and Banishes are all 0 in the config (nothing to top up)"); return; }
            double vr = stats.ReRolls.GetValue(), vs = stats.Skips.GetValue(), vb = stats.Banish.GetValue();
            bool ok = (r <= 0 || vr >= r) && (s <= 0 || vs >= s) && (b <= 0 || vb >= b);
            Plugin.SC.Check("counts_topped", ok, "on the {0}: rerolls {1:0}, skips {2:0}, banishes {3:0} (config {4} / {5} / {6}; 0 = left alone)", where, vr, vs, vb, r, s, b);
        }
        catch (Exception ex) { OtcPatch.Report(ex, "top-up self-check"); }
    }

    static string Read(VampireSurvivors.Framework.NumberTypes.EggFloat stat)
    {
        try { return stat == null ? "?" : stat.GetValue().ToString("0"); } catch { return "?"; }
    }
}

// Revivals are optional: their own patch class, so a missing PerformRevival (game update) only skips this part.
[HarmonyPatch(typeof(CharacterController), nameof(CharacterController.PerformRevival))]
internal static class RevivalPatch
{
    [HarmonyPostfix]
    static void AfterRevival(CharacterController __instance) { try { Patches.AfterRevival(__instance); } catch (Exception ex) { OtcPatch.Report(ex, "revival postfix"); } }
}
