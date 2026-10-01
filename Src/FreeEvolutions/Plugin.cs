using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using OffTheClock.Lib;
using VampireSurvivors.Data;
using VampireSurvivors.Data.Weapons;
using VampireSurvivors.Framework;
using VampireSurvivors.Objects;
using VampireSurvivors.Objects.Characters;

namespace FreeEvolutions;

// Free Evolutions: a weapon at max level evolves from a chest without the paired passive item.
//
// Read from the game code (Vampire Survivors 1.16.107, GameAssembly.dll, 2026-09-29):
// When a chest may evolve something, LevelUpFactory.PullEvolution asks LevelUpFactory.HasEvolutionRequirements(
// evolutionData, heldEquipment, character) for each candidate. The original returns true when
//   data.isEvolution && data.rarity >= 1,
//   every weapon in data.evolvesFrom is held,
//   every item in data.requires is held,                       <- the paired passive(s), e.g. Hollow Heart for Whip
//   every item in data.requiresMax is held at its top level,   <- unions (both weapons maxed) and some passives
//   and, if data.hasUniqueRequirements, CheckUniqueRequirements(...) agrees (a few special cases).
// The "weapon must be maxed" rule lives in the caller and is not touched. This prefix replaces the check with the
// same logic minus the passive items: an entry of requires/requiresMax that is a passive (WeaponData.isPowerUp) is
// skipped; weapons in those lists are still required (unions still need both weapons). Special cases keep the
// game's own CheckUniqueRequirements unless KeepUniqueRules is switched off.
// Off in online co-op (MultiplayerManager.IsOnlineMultiplayer): the original check runs unchanged there.
//
// 1.0.1: built on the shared Off the Clock helpers (Src\otc_bepinex): OtcPatcher applies the prefix (required),
// OtcOnline is the online co-op guard (was VsOnline.cs), an error in the check is logged at most 3 times (was: every
// call), and the prefix takes __runOriginal (HarmonyX runs every prefix: if another mod's prefix already decided, this
// one leaves the result alone). Self-checks: evolution_allowed (the prefix let an evolution through with a passive
// skipped, weapons held) and evolution_free (optional: at a later chest that evolved weapon is read back in the
// character's equipment list while the skipped passive is still not held).
//
// 1.0.2 (hardening for Vampire Survivors 1.17, our test notes): the prefix body no longer touches any game
// member itself. Everything that reads WeaponData / LevelUpFactory / Equipment lives in Decide() (NoInlining). If a
// game update renames or removes one of those members, .NET fails to compile Decide() when it is first called; that
// exception is thrown at the call inside our try/catch, so the game's own rule runs (logged, at most 3 times). Before,
// the same missing member made the whole prefix fail to compile outside any try/catch; Il2CppInterop's trampoline
// then returned false to the game for every call = no weapon could evolve at all. New self-check rule_active: PASS
// the first time our rule decided a check without falling back; FAIL (with the reason) if it fell back to the game's.
[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static ManualLogSource LogSrc;
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<bool> KeepUniqueRules;
    internal const string Id = "FreeEvolutions";   // Src folder name: log prefix of the shared helpers + self-check id
    internal static OtcSelfCheck SC;

    public override void Load()
    {
        LogSrc = Log;
        Enabled = Config.Bind("General", "Enabled", true, "Weapons at max level evolve without the paired passive item.");
        KeepUniqueRules = Config.Bind("General", "KeepUniqueRules", true,
            "Evolutions with special rules (the game's \"unique requirements\", e.g. shield and Saboteur cases) keep those rules. Set to false to skip them too.");
        OtcPatch.Init(Log, Id);
        OtcOnline.Configure(Log, Id, () => { var mm = MultiplayerManager.Instance; return mm != null && mm.IsOnlineMultiplayer; }, "online co-op");
        SC = new OtcSelfCheck(Log, Id);
        SC.Expect("evolution_allowed", "max a weapon without its passive item, then open a chest");
        SC.Expect("evolution_free", "after that evolution, open one more chest", optional: true);
        SC.Expect("rule_active", "max any weapon, then open a chest");
        var p = new OtcPatcher(new Harmony(PluginInfo.PLUGIN_GUID), Log, Id, SC);
        p.Class(typeof(Patches), required: true, feature: "evolution check (LevelUpFactory.HasEvolutionRequirements)");
        if (!p.Finish()) return;
        LogSrc.LogInfo($"{PluginInfo.PLUGIN_NAME} {PluginInfo.PLUGIN_VERSION} loaded - {(Enabled.Value ? "on" : "off")}, unique rules {(KeepUniqueRules.Value ? "kept" : "skipped")} (off in online co-op)");
    }
}

internal static class PluginInfo
{
    public const string PLUGIN_GUID = "offtheclock.freeevolutions";
    public const string PLUGIN_NAME = "Free Evolutions";
    public const string PLUGIN_VERSION = "1.0.2";
}

internal static class Patches
{
    static readonly HashSet<int> _logged = new HashSet<int>();

    [HarmonyPatch(typeof(LevelUpFactory), nameof(LevelUpFactory.HasEvolutionRequirements))]
    [HarmonyPrefix]
    static bool BeforeHasEvolutionRequirements(bool __runOriginal, LevelUpFactory __instance, WeaponData data,
        Il2CppSystem.Collections.Generic.List<Equipment> held, CharacterController characterController, ref bool __result)
    {
        // Only our own members here (1.0.2): a renamed game member must fail inside the try (in Decide), never here.
        if (!__runOriginal) return false;                                       // another mod's prefix already decided
        try
        {
            if (!Plugin.Enabled.Value || OtcOnline.IsOnline) return true;       // original check
            bool runOriginal = Decide(__instance, data, held, characterController, ref __result);
            SelfChecks.RuleRan();
            return runOriginal;
        }
        catch (Exception ex)
        {
            OtcPatch.Report(ex, "evolution check (the game's rule was used)");
            SelfChecks.FellBack(ex);
            return true;
        }
    }

    // The rule itself. Returns false (with __result set) when it decided, true to let the game's check run.
    // NoInlining: must stay its own method, so a missing game member throws at the call inside the prefix's try.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static bool Decide(LevelUpFactory factory, WeaponData data, Il2CppSystem.Collections.Generic.List<Equipment> held,
        CharacterController characterController, ref bool __result)
    {
        SelfChecks.Look(held);                                                  // read-only: an earlier free evolution now held?
        if (data == null || held == null) { __result = false; return false; }
        if (!data.isEvolution || data.rarity < 1) { __result = false; return false; }

        // 1. the weapons it evolves from must be held (the game's own rule; max level is checked by the caller)
        if (!AllHeld(data.evolvesFrom, held, false, factory, null)) { __result = false; return false; }

        // 2. required items: passives are free, weapons are still required
        var skipped = new List<string>();
        if (!AllHeld(data.requires, held, false, factory, skipped)) { __result = false; return false; }
        if (!AllHeld(data.requiresMax, held, true, factory, skipped)) { __result = false; return false; }

        // 3. special cases keep the game's own rule
        if (data.hasUniqueRequirements && Plugin.KeepUniqueRules.Value)
        {
            __result = LevelUpFactory.CheckUniqueRequirements(data, held, characterController);
            if (!__result) return false;
        }

        __result = true;
        if (skipped.Count > 0)
        {
            int key = (int)data.bulletType;
            if (_logged.Add(key))
                Plugin.LogSrc.LogInfo($"Free Evolutions: {data.bulletType} can evolve without {string.Join(", ", skipped)}.");
            SelfChecks.Allowed(data, skipped, held);
        }
        return false;
    }

    // Every type in `list` is held (and, if mustBeMax, at its top level). Passive items are skipped when `skipped`
    // is given (their names are collected for the log).
    static bool AllHeld(Il2CppSystem.Collections.Generic.List<WeaponType> list, Il2CppSystem.Collections.Generic.List<Equipment> held,
        bool mustBeMax, LevelUpFactory factory, List<string> skipped)
    {
        if (list == null) return true;
        int n = list.Count;
        for (int i = 0; i < n; i++)
        {
            WeaponType t = list[i];
            if (skipped != null && IsPassive(factory, t)) { skipped.Add(t.ToString()); continue; }
            var e = Find(held, t);
            if (e == null) return false;
            if (mustBeMax && !e.IsMaxLevel()) return false;
        }
        return true;
    }

    static Equipment Find(Il2CppSystem.Collections.Generic.List<Equipment> held, WeaponType t)
    {
        int n = held.Count;
        for (int i = 0; i < n; i++)
        {
            var e = held[i];
            if (e != null && e.Type == t) return e;
        }
        return null;
    }

    static bool IsPassive(LevelUpFactory factory, WeaponType t)
    {
        try
        {
            var weapons = factory.GetWeapons();
            if (weapons == null || !weapons.ContainsKey(t)) return false;
            var levels = weapons[t];
            if (levels == null || levels.Count == 0) return false;
            var first = levels[0];
            return first != null && first.isPowerUp;
        }
        catch
        {
            return false;   // unknown: treat as a weapon (still required), the safe side
        }
    }
}

// Read-only self-checks (our test tooling); each entry point has its own try/catch so a check never changes the result.
internal static class SelfChecks
{
    // Evolutions this mod let through with a passive skipped: evolved weapon -> the passives that were skipped.
    static readonly Dictionary<int, (string Name, List<string> Skipped)> _allowed = new Dictionary<int, (string, List<string>)>();

    // skipped = the passives the rule skipped; only those NOT held prove a free evolution (the game would have said no).
    internal static void Allowed(WeaponData data, List<string> skipped, Il2CppSystem.Collections.Generic.List<Equipment> held)
    {
        try
        {
            var sc = Plugin.SC;
            if (!sc.Want("evolution_allowed") && !sc.Want("evolution_free")) return;
            var types = Held(held);
            var missing = skipped.FindAll(x => !types.Contains(x));
            if (missing.Count == 0) return;
            string name = data.bulletType.ToString();
            if (sc.Want("evolution_allowed"))
                sc.Pass("evolution_allowed", "{0} may evolve without {1} (not held; the weapons it needs are held)", name, string.Join(", ", missing));
            if (sc.Want("evolution_free") && _allowed.Count < 50) _allowed[(int)data.bulletType] = (name, missing);
        }
        catch (Exception ex) { OtcPatch.Report(ex, "evolution self-check"); }
    }

    // rule_active: our rule decided a check without an exception (PASS once), or the prefix fell back to the game's
    // rule because something in Decide() threw (FAIL, e.g. a member renamed by a game update).
    internal static void RuleRan()
    {
        try { if (Plugin.SC.Want("rule_active")) Plugin.SC.Pass("rule_active", "the mod's evolution rule decided a chest check (no fallback to the game's rule)"); }
        catch (Exception ex) { OtcPatch.Report(ex, "evolution self-check"); }
    }

    internal static void FellBack(Exception error)
    {
        try
        {
            var sc = Plugin.SC;
            if (sc != null && sc.State("rule_active") != "FAIL")
                sc.Fail("rule_active", "fell back to the game's rule (evolutions need their passive again): {0}: {1}", error.GetType().Name, error.Message);
        }
        catch (Exception ex) { OtcPatch.Report(ex, "evolution self-check"); }
    }

    // At a later chest: is one of those evolutions in the character's equipment now, with the skipped passive still missing?
    internal static void Look(Il2CppSystem.Collections.Generic.List<Equipment> held)
    {
        if (_allowed.Count == 0 || held == null) return;
        try
        {
            if (!Plugin.SC.Want("evolution_free")) { _allowed.Clear(); return; }
            var types = Held(held);
            foreach (var kv in _allowed)
            {
                if (!types.Contains(kv.Value.Name)) continue;
                bool passiveMissing = true;
                foreach (var p in kv.Value.Skipped) if (types.Contains(p)) passiveMissing = false;
                if (!passiveMissing) continue;      // the player picked up the passive later: proves nothing
                Plugin.SC.Pass("evolution_free", "{0} is in the equipment (read back at a later chest) while {1} is not held",
                               kv.Value.Name, string.Join(", ", kv.Value.Skipped));
                _allowed.Clear();
                return;
            }
        }
        catch (Exception ex) { OtcPatch.Report(ex, "evolution self-check"); }
    }

    static HashSet<string> Held(Il2CppSystem.Collections.Generic.List<Equipment> held)
    {
        var types = new HashSet<string>();
        int n = held == null ? 0 : held.Count;
        for (int i = 0; i < n; i++)
        {
            var e = held[i];
            if (e != null) types.Add(e.Type.ToString());
        }
        return types;
    }
}
