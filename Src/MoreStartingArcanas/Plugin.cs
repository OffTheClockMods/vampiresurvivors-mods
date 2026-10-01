using System;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using OffTheClock.Lib;
using VampireSurvivors;
using VampireSurvivors.Data;
using VampireSurvivors.Framework;

namespace MoreStartingArcanas;

// More Starting Arcanas: choose 2, 3 or more arcanas at the start of a run instead of 1.
//
// Read from the game code (Vampire Survivors 1.16.107, GameAssembly.dll, 2026-09-30, our test notes):
// - At the start of a run GameManager.PostStageInit() calls GameManager.QueueOpenArcana(ArcanaUiType.MAIN, null) when
//   arcanas are on for the run (the arcana relic collected, not a special stage). QueueOpenArcana checks that the player
//   still has unlocked arcanas that are not active (PlayerOptionsData.UnlockedArcanas.Count > ArcanaManager.ActiveArcanas
//   .Count) and queues a UI transition that runs GameManager.OpenMainArcana: the arcana selection screen.
// - Picking a card: GameStateArcanaSelectionMain.AddArcana adds it to ArcanaManager.ActiveArcanas, triggers it and returns
//   to the game. The selection screen leaves out arcanas that are already active.
// - The same queue is used by treasure chests (GameStateTreasure.ReturnToGame) and characters that open several arcana
//   screens (TP_Nathan_Character.OpenArcana), so queueing more than one is a normal case for the game.
// - ArcanaManager.MaxArcanasPerRun is 3 (ArcanaManager.Initialize); treasure chests (TreasureChest.InternalUpdate,
//   TreasureFactory.MakePrizePairFromAvailablePowerUps) stop offering arcanas at that number, and several characters raise
//   it by one in their own setup. The mod raises it by its extra picks (option), the same way.
// This mod: a postfix on PostStageInit queues (StartingArcanas - 1) more selections right after the game's own one, only
// when the game queued that one itself. Off in online co-op (MultiplayerManager.IsOnlineMultiplayer). No leaderboards.
// Nothing is saved.
//
// Self-checks: extra_queued (the extra selections were queued and the run's limit reads as expected), arcanas_chosen
// (after the start picks ArcanaManager.ActiveArcanas holds StartingArcanas cards).
[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static ManualLogSource LogSrc;
    internal static ConfigEntry<int> Starting;
    internal static ConfigEntry<bool> RaiseLimit;
    internal const string Id = "MoreStartingArcanas";   // Src folder name: log prefix of the shared helpers + self-check id
    internal static OtcSelfCheck SC;

    public override void Load()
    {
        LogSrc = Log;
        Starting = Config.Bind("Arcanas", "StartingArcanas", 3, new ConfigDescription(
            "How many arcanas you choose at the start of a run (the game: 1). The selection screen opens again after each pick. " +
            "Only when the game offers an arcana at the start (arcanas on for the run). Limited by how many arcanas you have unlocked. " +
            "More arcanas make runs much stronger. Range 1 to 30.",
            new AcceptableValueRange<int>(1, Picks.MaxStarting)));
        RaiseLimit = Config.Bind("Arcanas", "RaiseArcanaLimit", true,
            "Also raise the run's arcana limit (the game: 3) by your extra start picks, so the treasure chests later in the run can still give arcanas. " +
            "false = the extra start picks count toward the game's normal limit (fewer arcanas from chests).");

        OtcPatch.Init(Log, Id);
        OtcOnline.Configure(Log, Id, () => { var mm = MultiplayerManager.Instance; return mm != null && mm.IsOnlineMultiplayer; }, "online co-op");
        SC = new OtcSelfCheck(Log, Id);
        SC.Expect("extra_queued", "start a run with arcanas on (StartingArcanas 2 or more)");
        SC.Expect("arcanas_chosen", "start a run with arcanas on and pick a card on every arcana screen");

        var p = new OtcPatcher(new Harmony(PluginInfo.PLUGIN_GUID), Log, Id, SC);
        p.Method("start of run (GameManager.PostStageInit)", typeof(GameManager), nameof(GameManager.PostStageInit), Type.EmptyTypes,
                 typeof(Patches), prefix: nameof(Patches.BeforeStart), postfix: nameof(Patches.AfterStart), required: true, declaredOnly: true);
        p.Method("arcana queue watch (GameManager.QueueOpenArcana)", typeof(GameManager), nameof(GameManager.QueueOpenArcana), null,
                 typeof(Patches), postfix: nameof(Patches.AfterQueue), required: true, declaredOnly: true);
        if (!p.Method("pick count (GameStateArcanaSelectionMain.AddArcana)", typeof(GameStateArcanaSelectionMain), nameof(GameStateArcanaSelectionMain.AddArcana), null,
                      typeof(Patches), postfix: nameof(Patches.AfterPick), declaredOnly: true))
            SC.Fail("arcanas_chosen", "the pick hook could not be applied (the extra selections still work)");
        if (!p.Finish()) return;
        LogSrc.LogInfo($"{PluginInfo.PLUGIN_NAME} {PluginInfo.PLUGIN_VERSION} loaded - {Starting.Value} arcana(s) at the start of a run, arcana limit {(RaiseLimit.Value ? "raised to match" : "unchanged")} (off in online co-op)");
    }
}

internal static class PluginInfo
{
    public const string PLUGIN_GUID = "offtheclock.morestartingarcanas";
    public const string PLUGIN_NAME = "More Starting Arcanas";
    public const string PLUGIN_VERSION = "1.0.0";
}

internal static class Patches
{
    static bool _inStart, _adding;
    static int _gameQueuedMain;
    static int _target;            // arcanas wanted after the start picks (0 = not watching)

    internal static void BeforeStart()
    {
        _inStart = true; _gameQueuedMain = 0;
    }

    // QueueOpenArcana(ArcanaUiType type, CharacterController chestWinner): only counts the game's own MAIN call at start.
    internal static void AfterQueue(ArcanaUiType type)
    {
        try { if (_inStart && !_adding && type == ArcanaUiType.MAIN) _gameQueuedMain++; }
        catch (Exception ex) { OtcPatch.Report(ex, "queue watch"); }
    }

    internal static void AfterStart(GameManager __instance)
    {
        bool queued = _gameQueuedMain > 0;
        _inStart = false;
        try { if (queued) AddExtra(__instance); }
        catch (Exception ex) { OtcPatch.Report(ex, "extra arcana selections"); }
        finally { _adding = false; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void AddExtra(GameManager gm)
    {
        if (gm == null || OtcOnline.IsOnline) return;
        int wanted = Plugin.Starting.Value;
        int unlocked = -1;
        try { unlocked = gm.PlayerOptions?.Config?.UnlockedArcanas?.Count ?? -1; } catch { }
        int extra = Picks.Extra(wanted, unlocked);
        _target = 0;
        if (extra <= 0)
        {
            if (wanted > 1) Plugin.LogSrc.LogInfo($"More Starting Arcanas: only {unlocked} arcana(s) unlocked, so no extra selection this run.");
            if (wanted > 1) Plugin.SC.Wait("extra_queued", "not enough arcanas unlocked for an extra pick");
            return;
        }
        var am = gm.ArcanaManager;
        int limitBefore = -1, limitAfter = -1;
        if (am != null)
        {
            limitBefore = am.MaxArcanasPerRun;
            int want = Picks.Limit(limitBefore, extra, Plugin.RaiseLimit.Value);
            if (want != limitBefore) am.MaxArcanasPerRun = want;
            limitAfter = am.MaxArcanasPerRun;
        }
        _adding = true;
        for (int i = 0; i < extra; i++) gm.QueueOpenArcana(ArcanaUiType.MAIN, null);
        _adding = false;
        _target = 1 + extra;
        Plugin.LogSrc.LogInfo($"More Starting Arcanas: {extra} extra arcana selection(s) queued after the game's own ({_target} at the start); run arcana limit {limitBefore} -> {limitAfter}.");
        if (Plugin.SC.Want("extra_queued"))
        {
            bool limitOk = limitBefore < 0 || limitAfter == Picks.Limit(limitBefore, extra, Plugin.RaiseLimit.Value);
            Plugin.SC.Check("extra_queued", limitOk, "{0} extra selection(s) queued; arcana limit {1} -> {2} (raise {3})", extra, limitBefore, limitAfter, Plugin.RaiseLimit.Value);
        }
    }

    // After every arcana pick: once the start picks are done, check the game's own active list.
    internal static void AfterPick()
    {
        try { if (_target > 0) CheckPicks(); }
        catch (Exception ex) { OtcPatch.Report(ex, "pick count"); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void CheckPicks()
    {
        var am = GM.Core?.ArcanaManager;
        var active = am?.ActiveArcanas;
        if (active == null) return;
        int n = active.Count;
        if (n < _target) { Plugin.LogSrc.LogInfo($"More Starting Arcanas: arcana {n} of {_target} chosen."); return; }
        Plugin.LogSrc.LogInfo($"More Starting Arcanas: all {_target} start arcanas chosen.");
        if (Plugin.SC.Want("arcanas_chosen"))
            Plugin.SC.Check("arcanas_chosen", n >= _target, "ArcanaManager.ActiveArcanas holds {0} card(s) after the start picks (wanted {1})", n, _target);
        _target = 0;
    }
}
