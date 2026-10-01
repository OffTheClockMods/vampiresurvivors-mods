using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using OffTheClock.Lib;
using UnityEngine;
using VampireSurvivors.Data;
using VampireSurvivors.Framework;
using VampireSurvivors.Objects.Characters;

namespace AutoLevelUp;

// Auto Level Up - Auto Pick Upgrades: level-ups pick an upgrade for you, from your own priority list, without stopping
// the game for the level-up screen.
//
// Read from the game code (Vampire Survivors 1.16.107, GameAssembly.dll, 2026-09-30, our test notes):
// GameManager.SwapToLevelUpScreen(adjustXpFactors) runs for every level-up. With something left to pick
// (LevelUpFactory.HasPowerupsInStore(player), player = GameSessionData.ActiveCharacter) it either opens the level-up
// screen, or, with the game's own "Random level-ups" option, takes LevelUpFactory.GetLevelUpPowerups(player) (the 3-4
// upgrades the screen would offer), picks one at random (PickRnd) and calls GameManager.ApplyRandomLevelUpWeapon(choice,
// player): the upgrade is applied with the game's overhead icon and sound, no screen, no pause. Online co-op goes through
// StartOnlineLevelUpFromHost instead.
// This mod does exactly what that option does, with its own choice instead of PickRnd: a prefix asks the game for the
// same offers, chooses from them with the player's priority list (and avoid list), calls ApplyRandomLevelUpWeapon and
// skips the original. Nothing left to pick (everything maxed: coin bag / Floor Chicken / limit breaks) = the original runs
// untouched (Always Pick Coin Bag covers that part). If nothing on the list is offered the fallback decides: a random
// allowed offer (default), the first one, or the normal screen. Everything on the avoid list = the normal screen.
// Off in online co-op (MultiplayerManager.IsOnlineMultiplayer). Vampire Survivors has no leaderboards. Nothing is saved.
//
// Key: Numpad 4 switches auto-picking off and on for this session (unbound in the game's Rewired keyboard map and its
// hard-coded reads; not used by our Gold and XP Multiplier (Numpad * and /) or Time Stop keys (Numpad 7/8/9), nor by the
// popular VS mods on Nexus (F1, F2, F9, F10, = and -)). Num Lock must be on.
//
// Self-checks: auto_picked (after a pick the chosen weapon/passive is held at a higher level than before, read from the
// character's own equipment managers), priority_used (optional: a pick came from the priority list).
[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static ManualLogSource LogSrc;
    internal static ConfigEntry<bool> Enabled, ShowNotice;
    internal static ConfigEntry<string> PriorityText, AvoidText, FallbackText;
    internal static ConfigEntry<KeyCode> ToggleKey;
    internal const string Id = "AutoLevelUp";   // Src folder name: log prefix of the shared helpers + self-check id
    internal static OtcSelfCheck SC;
    internal static OtcNotice Notice;
    internal static volatile bool On = true;
    internal static List<string> Priority = new List<string>(), Avoid = new List<string>();
    internal static Fallback Fall = Fallback.Random;

    public override void Load()
    {
        LogSrc = Log;
        Enabled = Config.Bind("General", "AutoPickEnabled", true,
            "Level-ups pick an upgrade for you (no level-up screen while something is left to pick). The toggle key switches it off and on during play.");
        PriorityText = Config.Bind("General", "PriorityList", "",
            "Upgrades to take first, best first, comma-separated. Use the names the game shows (Whip, King Bible, Spinach) or its internal names (WHIP, HOLYBOOK, POWER); " +
            "the log lists both for every level-up. Example: Whip, Hollow Heart, Spinach, Garlic. Empty = no preference.");
        AvoidText = Config.Bind("General", "AvoidList", "",
            "Upgrades the mod never takes for you (example: Skull O'Maniac, Duplicator). If only these are offered, the normal level-up screen opens so you decide.");
        FallbackText = Config.Bind("General", "WhenNothingFromListIsOffered", "Random", new ConfigDescription(
            "What happens when nothing from PriorityList is offered: Random = a random allowed offer (like the game's own Random level-ups option), " +
            "First = the first allowed offer, ShowScreen = the normal level-up screen opens and you pick.",
            new AcceptableValueList<string>("Random", "First", "ShowScreen")));
        ToggleKey = Config.Bind("Keys", "ToggleKey", KeyCode.Keypad4, "Switches auto-picking off and on for this session (Num Lock on). None = no key.");
        ShowNotice = Config.Bind("Keys", "ShowOnScreenNotice", true, "Show 'Auto Level Up on/off' on screen after a key press (it is always written to the log).");
        On = Enabled.Value;
        Refresh();
        PriorityText.SettingChanged += OnSettingChanged;
        AvoidText.SettingChanged += OnSettingChanged;
        FallbackText.SettingChanged += OnSettingChanged;
        Enabled.SettingChanged += OnEnabledChanged;

        OtcPatch.Init(Log, Id);
        OtcOnline.Configure(Log, Id, () => { var mm = MultiplayerManager.Instance; return mm != null && mm.IsOnlineMultiplayer; }, "online co-op");
        SC = new OtcSelfCheck(Log, Id);
        SC.Expect("auto_picked", "start a run and level up once (with something left to pick)");
        SC.Expect("priority_used", "put an upgrade you can get in PriorityList, start a run and level up until it is offered", optional: true);

        var p = new OtcPatcher(new Harmony(PluginInfo.PLUGIN_GUID), Log, Id, SC);
        p.Class(typeof(LevelUpPatch), required: true, feature: "auto pick (GameManager.SwapToLevelUpScreen)");
        if (!p.Finish()) return;

        string keyText = "";
        if (OtcHost.Start(this, Log, Id))
        {
            var keys = new OtcKeys(Log, Id);
            keys.Add("auto pick on/off", ToggleKey, Toggle, cooldown: 0.2f);
            keys.Start();
            Notice = new OtcNotice(Log, Id, () => ShowNotice.Value) { FontSize = 26 };
            Notice.Start();
            keyText = $"; {OtcKeys.KeyName(ToggleKey.Value)} switches it off/on";
        }
        else LogSrc.LogWarning($"{PluginInfo.PLUGIN_NAME}: the toggle key is unavailable; the config still works.");
        LogSrc.LogInfo($"{PluginInfo.PLUGIN_NAME} {PluginInfo.PLUGIN_VERSION} loaded - auto pick {(On ? "on" : "off")}, priority list: {ListText(Priority)}, avoid: {ListText(Avoid)}, otherwise {Fall}{keyText} (off in online co-op)");
    }

    static void OnSettingChanged(object sender, EventArgs e) => Refresh();
    static void OnEnabledChanged(object sender, EventArgs e) { On = Enabled.Value; }

    internal static void Refresh()
    {
        Priority = Chooser.ParseList(PriorityText.Value);
        Avoid = Chooser.ParseList(AvoidText.Value);
        Fall = Chooser.ParseFallback(FallbackText.Value);
    }

    internal static string ListText(List<string> l) => l.Count == 0 ? "(empty)" : string.Join(", ", l);

    static void Toggle()
    {
        On = !On;
        LogSrc.LogInfo($"Auto Level Up: auto pick {(On ? "ON - level-ups pick for you" : "OFF - the normal level-up screen opens")} (this session; the config default is {(Enabled.Value ? "on" : "off")}).");
        Notice?.Show(On ? "Auto Level Up ON" : "Auto Level Up OFF");
    }
}

internal static class PluginInfo
{
    public const string PLUGIN_GUID = "offtheclock.autolevelup";
    public const string PLUGIN_NAME = "Auto Level Up";
    public const string PLUGIN_VERSION = "1.0.0";
}

[HarmonyPatch(typeof(GameManager), nameof(GameManager.SwapToLevelUpScreen))]
internal static class LevelUpPatch
{
    // HarmonyX runs every prefix: if another mod already skipped the original, do nothing.
    [HarmonyPrefix]
    static bool Before(bool __runOriginal, GameManager __instance)
    {
        if (!__runOriginal) return false;
        try { return Auto.Handle(__instance); }
        catch (Exception ex) { OtcPatch.Report(ex, "level-up prefix"); return true; }   // any trouble: the game's own level-up
    }
}

internal static class Auto
{
    const int LogFirst = 5, LogEvery = 10;
    static readonly System.Random Rng = new System.Random();
    static IntPtr _run, _namesFor;
    static int _picks;
    static readonly Dictionary<int, string> Names = new Dictionary<int, string>();

    // true = let the game run its own SwapToLevelUpScreen (screen or coin bag), false = we applied a pick.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool Handle(GameManager gm)
    {
        if (!Plugin.On || gm == null || OtcOnline.IsOnline) return true;
        var player = gm._gameSessionData?.ActiveCharacter;
        var factory = gm.LevelUpFactory;
        if (player == null || factory == null) return true;
        if (!factory.HasPowerupsInStore(player)) return true;          // maxed: coin bag / chicken / limit break stay the game's
        var list = factory.GetLevelUpPowerups(player);
        if (list == null || list.Count == 0) return true;
        if (gm.Pointer != _run) { _run = gm.Pointer; _picks = 0; }
        LoadNames(gm);
        var types = new List<WeaponType>(list.Count);
        var offers = new List<Offer>(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            WeaponType t = list[i];
            types.Add(t);
            offers.Add(new Offer(t.ToString(), Names.TryGetValue((int)t, out string n) ? n : ""));
        }
        int idx = Chooser.Pick(offers, Plugin.Priority, Plugin.Avoid, Plugin.Fall, Rng, out string reason);
        int lvl = -1;
        try { lvl = player.Level; } catch { }
        if (idx < 0)
        {
            Plugin.LogSrc.LogInfo($"Auto Level Up: level {lvl} - offered {string.Join(", ", offers)}; {reason}, so the level-up screen opens.");
            return true;
        }
        WeaponType pick = types[idx];
        int before = HeldLevel(player, pick);
        gm.ApplyRandomLevelUpWeapon(pick, player);
        int after = HeldLevel(player, pick);
        _picks++;
        if (_picks <= LogFirst || _picks % LogEvery == 0)
            Plugin.LogSrc.LogInfo($"Auto Level Up: level {lvl} - offered {string.Join(", ", offers)}; took {offers[idx]} ({reason}; #{_picks} this run" +
                                  (_picks == LogFirst ? $", from now on every {LogEvery}th is logged" : "") + ").");
        var sc = Plugin.SC;
        if (sc.Want("auto_picked"))
            sc.Check("auto_picked", after > before, "{0}: level {1} -> {2} after the pick (no level-up screen)", offers[idx], before, after);
        if (reason.StartsWith("priority") && sc.Want("priority_used"))
            sc.Pass("priority_used", "took {0} ({1}) out of {2}", offers[idx], reason, string.Join(", ", offers));
        return false;
    }

    // Level of that weapon or passive on the character (0 = not held), from the game's own equipment managers.
    static int HeldLevel(CharacterController c, WeaponType t)
    {
        try
        {
            var w = c.WeaponsManager?.GetEquipmentByType(t, false);
            if (w != null) return w.Level;
            var a = c.AccessoriesManager?.GetEquipmentByType(t, false);
            if (a != null) return a.Level;
        }
        catch (Exception ex) { OtcPatch.Report(ex, "held level read"); }
        return 0;
    }

    // Shown names (WeaponData.name of level 1) once per data set, for the log and for name matching.
    static void LoadNames(GameManager gm)
    {
        try
        {
            var data = gm.DataManager;
            if (data == null || data.Pointer == _namesFor) return;
            _namesFor = data.Pointer;
            Names.Clear();
            var all = data.GetConvertedWeapons();
            if (all == null) return;
            foreach (var kv in all)
            {
                var levels = kv.Value;
                if (levels == null || levels.Count == 0 || levels[0] == null) continue;
                string n = levels[0].name;
                if (!string.IsNullOrWhiteSpace(n)) Names[(int)kv.Key] = n.Trim();
            }
        }
        catch (Exception ex) { OtcPatch.Report(ex, "weapon names"); }
    }
}
