using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using OffTheClock.Lib;
using UnityEngine;
using VampireSurvivors.Framework;
using VampireSurvivors.UI;
using VampireSurvivors.Objects.Characters;

namespace MoreWeaponAndPassiveSlots;

// More Weapon Slots and Passive Slots: extra weapon and passive (accessory) slots in single player and local co-op.
//
// Read from the game code (Vampire Survivors 1.16.107, GameAssembly.dll, 2026-09-30; details in
// our test notes):
// - CharacterController has a base count and a bonus per kind: _maxWeaponCount + _maxWeaponBonus and
//   _maxAccessoryCount + _maxAccessoryBonus. Everything that limits slots adds the two: the level-up pool
//   (LevelUpFactory.CalculateWeights / GetLevelUpPowerups), the level-up page, the merchant, evolution hints and the HUD.
// - The base is rewritten by the game: AfterFullInitialization sets the weapon base from the character screen's own
//   "max weapons" selector (1-6), UpdateMaxWeaponCount sets 6/4/3/2 for 1/2/3/4 players in co-op (and, if the co-op rules
//   say so, copies it to passives), and pair characters copy the leader's total into their follower every frame.
// - The bonus is what the game's items use: a DLC Mimic item adds weapon slots at its levels 2 and 4 (so 8 weapons already
//   happen in the unmodded game), Pearl Magatama / Velvet Dodecahedron / Mimic / a gold-count survarot add passive slots,
//   all as +1/-1 on the current value. Only two characters overwrite the passive bonus: Gekkojin (= 1) and LEM_002
//   (= 100), both right after the base AfterFullInitialization.
// So this mod ADDS its amount to the two bonus fields at run start and remembers what it added per character: it never
// lowers or replaces the game's own bonus, and it adds itself again after those two characters' own setup. Followers
// (IsFollower) are skipped: the game gives them the leader's total.
// Optional: in local co-op, give each player the solo slot numbers back (the difference is added to the bonus as well).
// Off in online co-op (MultiplayerManager.IsOnlineMultiplayer), like all our Vampire Survivors mods.
// Nothing is saved: the fields live on the run's character objects only.
//
// 1.0.1: built on the shared Off the Clock helpers (Src\otc_bepinex): OtcPatcher.Method replaces the Hook() helper
// 1:1 (same targets, declared-only lookup, run-start hook required, the rest optional; the two character overrides are
// found by name), OtcOnline is the online co-op guard (was VsOnline.cs), errors in the hooks are logged at most 3 times
// (were: every call). Self-checks: slots_added (at run start the game's own MaxWeaponCount / MaxAccessoryCount getters
// read the game's number + the extra slots) and slots_held (at the first level-up our extra slots are still counted).
[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static ManualLogSource LogSrc;
    internal static ConfigEntry<int> ExtraWeapons;
    internal static ConfigEntry<int> ExtraPassives;
    internal static ConfigEntry<bool> RestoreCoop;
    internal static ConfigEntry<bool> HudReport;

    internal const int MaxExtra = 999;   // 1.0.2: high enough that no run ever fills it (Design decision 2026-09-30; 36/36 was filled at level 365)
    internal const string Id = "MoreWeaponAndPassiveSlots";   // Src folder name: log prefix of the shared helpers + self-check id
    internal static OtcSelfCheck SC;

    public override void Load()
    {
        LogSrc = Log;
        ExtraWeapons = Config.Bind("Slots", "ExtraWeaponSlots", 2, new ConfigDescription(
            "Weapon slots added to what the game gives you (solo: 6, so 2 = 8 weapons). 0 = no change. The game has no hard limit; past about 8 the HUD icon row is untested, see the README.",
            new AcceptableValueRange<int>(0, MaxExtra)));
        ExtraPassives = Config.Bind("Slots", "ExtraPassiveSlots", 2, new ConfigDescription(
            "Passive item slots added to what the game gives you (6, plus what the game's own items and characters add; 2 = 8). 0 = no change.",
            new AcceptableValueRange<int>(0, MaxExtra)));
        RestoreCoop = Config.Bind("Local co-op", "RestoreSoloSlotsInLocalCoop", false,
            "In local co-op the game gives each player fewer weapon slots (4 / 3 / 2 with 2 / 3 / 4 players). true = every player gets the solo numbers back first, then the extra slots above are added. Online co-op is never changed.");
        HudReport = Config.Bind("Log", "HudReport", true,
            "Write one line to the log whenever a HUD icon row passes 6 icons or reaches a new maximum (how many rows, whether all icons are on screen). Only reads the HUD.");

        OtcPatch.Init(Log, Id);
        OtcOnline.Configure(Log, Id, () => { var mm = MultiplayerManager.Instance; return mm != null && mm.IsOnlineMultiplayer; }, "online co-op");
        SC = new OtcSelfCheck(Log, Id);
        SC.Expect("slots_added", "start a run");
        SC.Expect("slots_held", "level up once in a run", grace: 3);

        // The run-start hook is required; everything else only adds robustness and logs, so each part may fail alone.
        // Declared-only lookup (declaredOnly: true) so an override is patched, not its base.
        var p = new OtcPatcher(new Harmony(PluginInfo.PLUGIN_GUID), Log, Id, SC);
        p.Method("run-start slots", typeof(CharacterController), nameof(CharacterController.AfterFullInitialization), Type.EmptyTypes,
                 typeof(Patches), postfix: nameof(Patches.AfterInit), required: true, declaredOnly: true);
        p.Method("co-op slot recount", typeof(CharacterController), nameof(CharacterController.UpdateMaxWeaponCount), Type.EmptyTypes,
                 typeof(Patches), postfix: nameof(Patches.AfterCoopCount), declaredOnly: true);
        if (!p.Method("level-up check", typeof(LevelUpFactory), nameof(LevelUpFactory.CalculateWeights), new[] { typeof(CharacterController) },
                      typeof(Patches), prefix: nameof(Patches.BeforeLevelUpPool), declaredOnly: true))
            SC.Fail("slots_held", "the level-up hook could not be applied (the slots themselves still work)");
        // Characters whose own setup overwrites the passive bonus right after the base setup (see the header). If one is
        // missing (game update), that character may keep only the game's own passive bonus.
        p.Method("Gekkojin passive bonus", "VampireSurvivors.Objects.Characters.CharacterController_Gekkojin", nameof(CharacterController.AfterFullInitialization),
                 Type.EmptyTypes, typeof(Patches), postfix: nameof(Patches.AfterOverrideInit), declaredOnly: true);
        p.Method("LEM_002 passive bonus", "VampireSurvivors.Objects.Characters.LEM_CharacterController_002", nameof(CharacterController.AfterFullInitialization),
                 Type.EmptyTypes, typeof(Patches), postfix: nameof(Patches.AfterOverrideInit), declaredOnly: true);
        if (!p.Finish()) return;

        LogSrc.LogInfo($"{PluginInfo.PLUGIN_NAME} {PluginInfo.PLUGIN_VERSION} loaded - +{ExtraWeapons.Value} weapon slots, +{ExtraPassives.Value} passive slots" +
                       (RestoreCoop.Value ? ", solo slot numbers in local co-op" : "") + " (off in online co-op)");
    }
}

internal static class PluginInfo
{
    public const string PLUGIN_GUID = "offtheclock.moreweaponandpassiveslots";
    public const string PLUGIN_NAME = "More Weapon and Passive Slots";
    public const string PLUGIN_VERSION = "1.0.2";
}

internal static class Patches
{
    internal static void AfterInit(CharacterController __instance)
    {
        try { Slots.Apply(__instance, null, false); }
        catch (Exception ex)
        {
            if (OtcPatch.ErrorCount("run-start slots") == 0) Plugin.SC.Fail("slots_added", "could not add the slots: " + ex.GetType().Name + ": " + ex.Message);
            OtcPatch.Report(ex, "run-start slots");
        }
    }

    internal static void AfterCoopCount(CharacterController __instance)
    {
        try { Slots.Apply(__instance, "the co-op slot count was recalculated", false); }
        catch (Exception ex) { OtcPatch.Report(ex, "co-op slot recount"); }
    }

    internal static void AfterOverrideInit(CharacterController __instance)
    {
        try { Slots.Apply(__instance, "this character's own setup set its passive bonus", true); }
        catch (Exception ex) { OtcPatch.Report(ex, "re-add after the character's own setup"); }
    }

    internal static void BeforeLevelUpPool(CharacterController character)
    {
        try { Slots.Check(character); }
        catch (Exception ex) { OtcPatch.Report(ex, "level-up slot check"); }
    }
}

internal static class Slots
{
    const int SoloSlots = 6;

    sealed class State
    {
        public int AddW, AddA;           // what we added to _maxWeaponBonus / _maxAccessoryBonus
        public int SeenW, SeenA;         // bonus values right after our last write / check
        public int LoggedW = -1, LoggedA = -1;
        public int HudW = SoloSlots, HudA = SoloSlots;
        public string Name;
    }

    static readonly Dictionary<IntPtr, State> _chars = new();
    static IntPtr _run;

    // Per run: characters are new objects every run, so the table is cleared when the GameManager changes.
    static void CheckRun()
    {
        IntPtr gm = IntPtr.Zero;
        try { var core = GM.Core; if (core != null) gm = core.Pointer; } catch { }
        if (gm != _run) { _run = gm; _chars.Clear(); }
    }

    static bool Eligible(CharacterController c)
    {
        if (c == null || OtcOnline.IsOnline) return false;
        try { if (c.IsFollower) return false; } catch { }
        return true;
    }

    static int PlayerCount()
    {
        try { var mm = MultiplayerManager.Instance; return mm != null ? mm.GetPlayerCount() : 1; } catch { return 1; }
    }

    // The solo weapon number is the character screen's own "max weapons" choice (1-6, default 6).
    static int SoloWeapons()
    {
        try
        {
            int v = GM.Core.PlayerOptions.Config.SelectedMaxWeapons;
            if (v >= 1 && v <= SoloSlots) return v;
        }
        catch { }
        return SoloSlots;
    }

    // What we want to have added right now (extra + optional co-op restore).
    static void Desired(CharacterController c, out int w, out int a, out int restoreW, out int restoreA)
    {
        restoreW = restoreA = 0;
        if (Plugin.RestoreCoop.Value && PlayerCount() > 1)
        {
            restoreW = Math.Max(0, SoloWeapons() - c._maxWeaponCount);
            restoreA = Math.Max(0, SoloSlots - c._maxAccessoryCount);
        }
        w = Plugin.ExtraWeapons.Value + restoreW;
        a = Plugin.ExtraPassives.Value + restoreA;
    }

    // Adds (or re-adds) our share. `cause` = null on the normal run start. `overrideWrote` = called right after a
    // character's own setup that is known to OVERWRITE the passive bonus: any change since our last write is theirs.
    internal static void Apply(CharacterController c, string cause, bool overrideWrote)
    {
        if (!Eligible(c)) return;
        CheckRun();
        bool isNew = !_chars.TryGetValue(c.Pointer, out var st);
        if (isNew) { st = new State { Name = SafeName(c) }; _chars[c.Pointer] = st; }

        int bw = c._maxWeaponBonus, ba = c._maxAccessoryBonus;
        bool resetW = false, resetA = false;
        if (!isNew)
        {
            // Relative changes by the game (items: +1/-1) keep our share inside the value; an overwrite drops it.
            resetW = overrideWrote ? bw != st.SeenW : bw < st.AddW;
            resetA = overrideWrote ? ba != st.SeenA : ba < st.AddA;
            if (resetW) st.AddW = 0;
            if (resetA) st.AddA = 0;
        }
        Desired(c, out int dw, out int da, out int rw, out int ra);
        bw += dw - st.AddW;
        ba += da - st.AddA;
        if (bw != c._maxWeaponBonus) c._maxWeaponBonus = bw;
        if (ba != c._maxAccessoryBonus) c._maxAccessoryBonus = ba;
        st.AddW = dw; st.AddA = da;
        st.SeenW = bw; st.SeenA = ba;
        if (isNew && Plugin.SC.Want("slots_added"))
            SelfCheck(c, "slots_added", $"{st.Name} at run start", c._maxWeaponCount + bw - dw, c._maxAccessoryCount + ba - da, dw, da);

        int totW = c._maxWeaponCount + bw, totA = c._maxAccessoryCount + ba;
        if (totW == st.LoggedW && totA == st.LoggedA) return;
        bool firstLine = st.LoggedW < 0;
        st.LoggedW = totW; st.LoggedA = totA;
        string detail = $"weapon slots {totW} ({Parts(c._maxWeaponCount + bw - dw, rw, Plugin.ExtraWeapons.Value)}), " +
                        $"passive slots {totA} ({Parts(c._maxAccessoryCount + ba - da, ra, Plugin.ExtraPassives.Value)})";
        if (firstLine)
            Plugin.LogSrc.LogInfo($"More Weapon and Passive Slots: {st.Name} - {detail}.");
        else
            Plugin.LogSrc.LogInfo($"More Weapon and Passive Slots: {st.Name} - {cause ?? "slots changed"}" +
                                  ((resetW || resetA) ? "; our extra slots were added on top again" : "") + $": {detail}.");
    }

    static string Parts(int game, int restore, int extra)
    {
        string s = $"game {game}";
        if (restore > 0) s += $" + {restore} solo co-op restore";
        return s + $" + {extra} extra";
    }

    // Right before the game reads the slots for a level-up or chest: notice changes the game made, re-add if something
    // replaced the bonus, and (optionally) report how the HUD rows look.
    internal static void Check(CharacterController c)
    {
        if (!Eligible(c)) return;
        CheckRun();
        if (!_chars.TryGetValue(c.Pointer, out var st))
        {
            Apply(c, "slots added late (the run-start hook did not run for this character)", false);
            return;
        }
        if (Plugin.SC.Want("slots_held")) HeldCheck(c, st);
        int bw = c._maxWeaponBonus, ba = c._maxAccessoryBonus;
        if (bw != st.SeenW || ba != st.SeenA)
        {
            if (bw < st.AddW || ba < st.AddA)
                Apply(c, "the game replaced the slot bonus", false);
            else
            {
                st.SeenW = bw; st.SeenA = ba;
                int totW = c._maxWeaponCount + bw, totA = c._maxAccessoryCount + ba;
                if (totW != st.LoggedW || totA != st.LoggedA)
                {
                    st.LoggedW = totW; st.LoggedA = totA;
                    Plugin.LogSrc.LogInfo($"More Weapon and Passive Slots: {st.Name} - the game changed the slot bonus (an item or ability): " +
                                          $"weapon slots {totW}, passive slots {totA} (our +{st.AddW} / +{st.AddA} still included).");
                }
            }
        }
        if (Plugin.HudReport.Value) HudCheck.Report(c, st.Name, ref st.HudW, ref st.HudA);
    }

    // Self-check (read-only): the game's own getters (MaxWeaponCount = _maxWeaponCount + _maxWeaponBonus, same for
    // accessories) report the game's number plus what this mod added. gameW/gameA = the totals without our share.
    static void SelfCheck(CharacterController c, string id, string where, int gameW, int gameA, int addW, int addA)
    {
        try
        {
            int w = c.MaxWeaponCount, a = c.MaxAccessoryCount;
            Plugin.SC.Check(id, w == gameW + addW && a == gameA + addA,
                            "{0}: MaxWeaponCount {1} = game {2} + {3}, MaxAccessoryCount {4} = game {5} + {6} (config +{7} / +{8})",
                            where, w, gameW, addW, a, gameA, addA, Plugin.ExtraWeapons.Value, Plugin.ExtraPassives.Value);
        }
        catch (Exception ex) { OtcPatch.Report(ex, "slots self-check"); }
    }

    // Self-check (read-only), when the game reads the slots for a level-up and before this mod repairs anything: our
    // extra slots are still inside the bonus the game counts (nothing replaced it since run start). A miss is repaired
    // right after (re-added) and counted; FAIL after 3 misses.
    static void HeldCheck(CharacterController c, State st)
    {
        try
        {
            int w = c.MaxWeaponCount, a = c.MaxAccessoryCount;
            bool ok = c._maxWeaponBonus >= st.AddW && c._maxAccessoryBonus >= st.AddA;
            Plugin.SC.Check("slots_held", ok, ok
                ? "{0} at a level-up: MaxWeaponCount {1}, MaxAccessoryCount {2} still include our +{3} / +{4}"
                : "{0} at a level-up: MaxWeaponCount {1}, MaxAccessoryCount {2} had lost our +{3} / +{4} (re-added now)",
                st.Name, w, a, st.AddW, st.AddA);
        }
        catch (Exception ex) { OtcPatch.Report(ex, "slots self-check"); }
    }

    static string SafeName(CharacterController c)
    {
        try { return c.CharacterType.ToString(); } catch { return "character"; }
    }
}

// Read-only HUD report: the game appends weapon/passive icons past its 6 empty frames to the same row container
// (GameEquipmentPanel.RebuildWeaponSlots / RebuildAccessorySlots); how the row looks then depends on the row's Unity
// layout component, which is scene data, not code. This logs what actually happens, once per new maximum.
internal static class HudCheck
{
    static bool _broken;

    internal static void Report(CharacterController c, string name, ref int lastW, ref int lastA)
    {
        if (_broken) return;
        try
        {
            var panel = GameEquipmentPanel.GetPanelForCharacter(c);
            if (panel == null) return;
            Row(panel._WeaponContainer, name, "weapon", ref lastW);
            Row(panel._AccessoryContainer, name, "passive", ref lastA);
        }
        catch (Exception ex)
        {
            _broken = true;
            Plugin.LogSrc.LogWarning("More Weapon and Passive Slots: HUD report unavailable (" + ex.Message + "); the slots still work.");
        }
    }

    static void Row(RectTransform row, string name, string kind, ref int last)
    {
        if (row == null) return;
        int n = 0;
        for (int i = 0; i < row.childCount; i++)
            if (row.GetChild(i).gameObject.activeSelf) n++;
        if (n <= 6 || n <= last) return;
        last = n;

        Camera cam = null;
        var canvas = row.GetComponentInParent<Canvas>();
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) cam = canvas.worldCamera;
        float minX = float.MaxValue, maxX = float.MinValue, iconPx = 0f;
        var rowYs = new List<float>();
        int off = 0;
        var corners = new Il2CppStructArray<Vector3>(4);
        for (int i = 0; i < row.childCount; i++)
        {
            var t = row.GetChild(i);
            if (!t.gameObject.activeSelf) continue;
            var rt = t.TryCast<RectTransform>();
            if (rt == null) continue;
            rt.GetWorldCorners(corners);
            Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);   // bottom left
            Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);   // top right
            minX = Math.Min(minX, a.x); maxX = Math.Max(maxX, b.x);
            iconPx = Math.Max(iconPx, b.x - a.x);
            float y = (float)Math.Round(b.y);
            bool seen = false;
            foreach (var ry in rowYs) if (Math.Abs(ry - y) < 2f) { seen = true; break; }
            if (!seen) rowYs.Add(y);
            if (a.x < 0f || b.x > Screen.width || a.y < 0f || b.y > Screen.height) off++;
        }
        Plugin.LogSrc.LogInfo($"More Weapon and Passive Slots: HUD check ({name}) - {kind} row shows {n} icons in {rowYs.Count} row(s), " +
                              $"x {minX:0}-{maxX:0} px of {Screen.width}, icon ~{iconPx:0} px, " +
                              (off == 0 ? "all on screen" : $"{off} icon(s) partly or fully off screen") +
                              $" (row: {Components(row)}; parent: {Components(row.parent)}).");
    }

    static string Components(Transform t)
    {
        if (t == null) return "-";
        var names = new List<string>();
        foreach (var comp in t.GetComponents<Component>())
        {
            if (comp == null) continue;
            string n = comp.GetIl2CppType().Name;
            if (n != "RectTransform" && n != "CanvasRenderer") names.Add(n);
        }
        return names.Count == 0 ? "no layout component" : string.Join(", ", names);
    }
}
