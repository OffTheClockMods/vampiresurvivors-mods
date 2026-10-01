using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using OffTheClock.Lib;
using VampireSurvivors.Data;
using VampireSurvivors.Framework;
using VampireSurvivors.Framework.DLC;
using VampireSurvivors.Objects;
using VampireSurvivors.UI;

namespace UnlockAll;

// Unlock All (owned content only): at the main menu, once, unlocks every character, stage (+ hyper mode), weapon,
// passive item, arcana card and relic of the base game and of the DLC you own, then saves. Never unlocks content of
// DLC you don't own. Opt-in: nothing happens until RunUnlockAtNextStart = true in the config; after the run the
// setting switches itself back to false.
//
// Read from the game code (Vampire Survivors 1.16.107, GameAssembly.dll, 2026-09-29):
// - Unlocks are plain lists on the save (PlayerOptionsData.UnlockedCharacters, BoughtCharacters, UnlockedStages,
//   UnlockedHypers, UnlockedWeapons, UnlockedArcanas, CollectedItems). PlayerOptions.UnlockCharacter / BuyCharacter /
//   UnlockStage / UnlockHyper / UnlockWeapon / UnlockArcana / UnlockItem(type, config) add to them (no duplicates).
//   BuyCharacter only marks the character as bought (the shop UI takes the gold before calling it), so unlocking
//   and buying here costs nothing. No achievement is awarded by these calls; achievements are the game's own
//   unlock mechanism and stay untouched (the game may still award progress achievements later on its own checks).
// - Content lists: DataManager.GetConvertedCharacterData / GetConvertedStages / GetConvertedWeapons / AllArcanas /
//   AllItems. Records carry a ContentGroupType (BASE, EXTRA, MOONSPELL ... BLOODMOON, WORKSHOP, CUSTOM); groups
//   MOONSPELL..BLOODMOON map to DlcType 0..7. Ownership = DlcSystem.OwnedDlc (+ IncludedDlc). DLC membership of
//   stages and characters also comes from DataManager.AllDlcStageData / AllDlcCharacterData (the dictionaries the
//   game's own DlcUtils.GetStageDlcType / GetCharacterDlcType search); relics also have ItemData.requiresDLC.
//   Anything belonging to a DLC that isn't owned is skipped and named in the log.
// - Arcanas: the Collection (CollectionsPage.SpawnElements) lists the entries of AllArcanas with `major` set, and
//   shows them as found when `unlocked` is set (PlayerOptions.ApplyUnlocksToData copies UnlockedArcanas into it).
//   So "every arcana" = every `major` card. (1.0.0 filtered on ArcanaData.enabled, which the game doesn't use for
//   this, and skipped all 76 entries.)
// - Save: PlayerOptions.Save(commitImmediately: true, createBackup: true) -> the game's SaveSystem, including the
//   game's own backup. Before that this mod copies the Steam save file(s) (Steam\userdata\<id>\1794680\remote\
//   SaveData*) to BepInEx\offtheclock-save-backups\<timestamp>\.
// - Honesty mark: PlayerOptionsData.CheatCodeUsed is the game's own "a cheat code was used" save flag (set by its
//   built-in intro cheat, IntroSceneCheatManager.UnlockMortaccio). This mod sets it too and never clears it.
// - Hook: MainMenuPage.OnShowStart, when the save is loaded (PlayerOptions.IsInitialized). Never in online co-op.
// Vampire Survivors has no online leaderboards (no leaderboard code exists in the game).
//
// 1.0.1 - why 1.0.0 failed on characters, stages and relics (our tester's log 2026-09-29): all three stack traces end in
// the interop Nullable`1..ctor -> Il2CppObjectBase.CreateGCHandle (NullReferenceException), from
// CharacterData.get_contentGroup, DlcUtils.GetStageDlcType and ItemData.get_requiresDLC. Those return
// Nullable<enum>. Il2CppInterop hands a value-type Nullable back by boxing it, and IL2CPP boxes a Nullable that has no
// value to a null pointer, which the interop wrapper can't wrap. Base-game content has no DLC (null), so the first
// base-game entry stopped each category. Weapons (non-nullable contentGroup) worked in the same pass, so it was not a
// timing problem. 1.0.1 never calls a Nullable-returning interop member: nullable fields are read with
// il2cpp_field_get_value_object (null pointer = no value, else unbox the enum) - see NullableField below.
//
// 1.0.2: built on the shared Off the Clock helpers (Src\otc_bepinex): OtcPatcher applies the main-menu hook
// (required: without it the mod does nothing), OtcOnline is the online co-op guard (was VsOnline.cs). Self-checks
// (only when the unlock is armed): after the save, every entry unlocked now is read back from the save lists
// (UnlockedCharacters, BoughtCharacters, UnlockedStages, UnlockedHypers, UnlockedWeapons, UnlockedArcanas,
// CollectedItems) and the CheatCodeUsed flag is read back: one [SELFCHECK] line per category.
//
// 1.0.3 (hardening for Vampire Survivors 1.17, our test notes): 1.17 adds custom characters (JSON files) and
// Workshop characters. The game gives them IDs from CharacterType.CUSTOM_RANGE_START (805306368) and
// WORKSHOP_RANGE_START (1073741824) up. Their content group should be CUSTOM / WORKSHOP (left locked already), but a
// custom character with an empty content group would have been read as base game and unlocked/bought, writing its big
// ID into the save (an unsubscribed Workshop item would then leave an unknown ID behind). Every character ID in those
// ranges is now left locked ("workshop/custom content"), whatever its content group says. Also: the main-menu postfix
// no longer touches a game member itself (MainMenuPage fields moved into TryRun, NoInlining), so a renamed field after
// a game update lands in the catch below (logged "stopped (game update?)", self-checks FAIL) instead of failing silently.
// New self-check dlc_respected: the save lists are read before and after the unlock; every character, bought
// character, stage, hyper and relic that is NEW in the save afterwards is looked up in the game's own per-DLC tables
// (characters/stages), the relic's requiresDLC and the custom/Workshop ID range. PASS = none belongs to a DLC you
// don't own or to custom/Workshop content (this also catches entries the game itself adds as a side effect).
// Paid DLC: unchanged - ownership still comes only from the game's licence lists (DlcSystem.OwnedDlc + IncludedDlc).
[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static ManualLogSource LogSrc;
    internal static ConfigEntry<bool> RunNext;
    internal static ConfigEntry<bool> DoCharacters, BuyCharacters, DoStages, DoHypers, DoWeapons, DoArcanas, DoRelics;
    internal const string Id = "UnlockAll";          // Src folder name: log prefix of the shared helpers + self-check id
    internal static OtcSelfCheck SC;

    public override void Load()
    {
        LogSrc = Log;
        RunNext = Config.Bind("Unlock", "RunUnlockAtNextStart", false,
            "Set to true and start the game: at the main menu the categories below are unlocked once, the save is written, and this switches itself back to false. Nothing happens while it is false.");
        DoCharacters = Config.Bind("Unlock", "Characters", true, "Unlock every character (base game + DLC you own).");
        BuyCharacters = Config.Bind("Unlock", "BuyCharacters", true, "Also mark unlocked characters as bought (they normally cost gold in the character screen).");
        DoStages = Config.Bind("Unlock", "Stages", true, "Unlock every stage.");
        DoHypers = Config.Bind("Unlock", "HyperModes", true, "Unlock Hyper mode for every stage.");
        DoWeapons = Config.Bind("Unlock", "WeaponsAndPassives", true, "Unlock every weapon and passive item for the level-up pool (evolutions are found by evolving, as usual).");
        DoArcanas = Config.Bind("Unlock", "Arcanas", true, "Unlock every arcana card (the cards shown in the Collection).");
        DoRelics = Config.Bind("Unlock", "Relics", true, "Unlock every relic (Randomazzo, Grim Grimoire, Ars Gouda, Milky Way Map, Magic Banger, Sorceress' Tears, Glass Vizard, Yellow Sign, Gracia's Mirror, Seventh Trumpet ...).");
        OtcPatch.Init(Log, Id);
        OtcOnline.Configure(Log, Id, () => { var mm = MultiplayerManager.Instance; return mm != null && mm.IsOnlineMultiplayer; }, "online co-op");
        SC = new OtcSelfCheck(Log, Id);
        if (RunNext.Value)
        {
            // Only when the unlock is armed: otherwise there is nothing to prove this session.
            if (DoCharacters.Value) SC.Expect("characters", "set RunUnlockAtNextStart = true, start the game to the main menu");
            if (DoStages.Value || DoHypers.Value) SC.Expect("stages", "set RunUnlockAtNextStart = true, start the game to the main menu");
            if (DoWeapons.Value) SC.Expect("weapons", "set RunUnlockAtNextStart = true, start the game to the main menu");
            if (DoArcanas.Value) SC.Expect("arcanas", "set RunUnlockAtNextStart = true, start the game to the main menu");
            if (DoRelics.Value) SC.Expect("relics", "set RunUnlockAtNextStart = true, start the game to the main menu");
            SC.Expect("cheat_flag", "set RunUnlockAtNextStart = true, start the game to the main menu");
            SC.Expect("dlc_respected", "set RunUnlockAtNextStart = true, start the game to the main menu");
        }
        var p = new OtcPatcher(new Harmony(PluginInfo.PLUGIN_GUID), Log, Id, SC);
        p.Class(typeof(Patches), required: true, feature: "main-menu hook (MainMenuPage.OnShowStart)");
        if (!p.Finish()) return;
        LogSrc.LogInfo($"{PluginInfo.PLUGIN_NAME} {PluginInfo.PLUGIN_VERSION} loaded - " +
                       (RunNext.Value ? "RunUnlockAtNextStart is true: the unlock runs at the main menu." : "waiting (set RunUnlockAtNextStart = true in the config to unlock once)."));
    }
}

internal static class PluginInfo
{
    public const string PLUGIN_GUID = "offtheclock.unlockall";
    public const string PLUGIN_NAME = "Unlock All";
    public const string PLUGIN_VERSION = "1.0.3";
}

internal static class Patches
{
    const int MaxAttempts = 2;   // a failed category is tried once more the next time the main menu shows
    static int _attempts;
    static bool _finished;
    static int _waits;

    [HarmonyPatch(typeof(MainMenuPage), nameof(MainMenuPage.OnShowStart))]
    [HarmonyPostfix]
    static void AfterMainMenuShown(MainMenuPage __instance)
    {
        // Only our own members here (1.0.3): a renamed game member must fail inside the try (in TryRun).
        try
        {
            if (_finished || !Plugin.RunNext.Value) return;
            if (OtcOnline.IsOnline) { Plugin.LogSrc.LogInfo("Unlock All: not while in online co-op."); return; }
            TryRun(__instance);
        }
        catch (Exception ex)
        {
            _finished = true;
            Plugin.LogSrc.LogError("Unlock All stopped (game update?): " + ex);
            Plugin.SC?.FailOpen("the unlock stopped: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    // NoInlining: must stay its own method, so a missing game member throws at the call inside the postfix's try.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void TryRun(MainMenuPage page)
    {
        if (page == null) return;
        var po = page._playerOptions;
        var data = page._dataManager ?? page.Data;
        if (po == null || data == null || !po.IsInitialized)
        {
            if (_waits++ < 3) Plugin.LogSrc.LogInfo("Unlock All: save not loaded yet, trying again the next time the main menu shows.");
            return;
        }
        _attempts++;
        bool last = _attempts >= MaxAttempts;
        bool allOk = Unlocker.Run(po, data, _attempts, last);
        if (allOk || last) _finished = true;
    }
}

// Per-category bookkeeping for the log: what was unlocked now, already unlocked, left locked on purpose, failed.
internal sealed class Tally
{
    readonly string _name;
    public readonly List<string> Now = new List<string>();
    public readonly List<string> Locked = new List<string>();
    public readonly List<string> Failed = new List<string>();
    public readonly List<(string Name, Func<bool> InSave)> ReadBack = new List<(string, Func<bool>)>();   // self-check
    public int Already, Skipped;
    public string SkippedWhat = "internal entries";
    public Tally(string name) { _name = name; }

    public void Log(ManualLogSource l)
    {
        l.LogInfo($"Unlock All: {_name} - unlocked now {Now.Count}, already unlocked {Already}, left locked (DLC not owned) {Locked.Count}, failed {Failed.Count}" +
                  (Skipped > 0 ? $" ({Skipped} {SkippedWhat} skipped)." : "."));
        if (Now.Count > 0) l.LogInfo($"Unlock All: {_name} unlocked now: {Join(Now)}.");
        if (Locked.Count > 0) l.LogInfo($"Unlock All: {_name} left locked: {Join(Locked)}.");
        if (Failed.Count > 0) l.LogWarning($"Unlock All: {_name} failed: {Join(Failed)}.");
    }

    public string Summary() => $"{_name} +{Now.Count} ({Already} already, {Locked.Count} locked, {Failed.Count} failed)";

    internal static string Join(List<string> items)
    {
        const int max = 80;
        if (items.Count <= max) return string.Join(", ", items);
        return string.Join(", ", items.GetRange(0, max)) + $" ... and {items.Count - max} more";
    }
}

internal static class Unlocker
{
    static ManualLogSource L => Plugin.LogSrc;
    static bool _backedUp;

    // Returns true when no category failed.
    internal static bool Run(PlayerOptions po, DataManager data, int attempt, bool last)
    {
        var cfg = po.MainGameConfig ?? po.Config;
        if (cfg == null) { L.LogWarning("Unlock All: no save data found, nothing done."); return false; }

        string saveName = Safe(() => cfg.saveName);
        L.LogInfo($"Unlock All {PluginInfo.PLUGIN_VERSION}: starting" + (saveName.Length > 0 ? " on save \"" + saveName + "\"" : "") +
                  (attempt > 1 ? $" (attempt {attempt}, only what is still missing)." : "."));
        var owned = OwnedDlc();
        var before = SaveIds.Take(cfg);                  // self-check dlc_respected (read-only; null = unreadable)
        if (!_backedUp) { BackupSaveFiles(); _backedUp = true; }

        var parts = new List<string>();
        var results = new List<(string, Tally, string)>();
        bool anyFailed = false;
        if (Plugin.DoCharacters.Value) anyFailed |= !Step("characters", "characters", parts, results, () => Characters(po, data, cfg, owned));
        if (Plugin.DoStages.Value || Plugin.DoHypers.Value) anyFailed |= !Step("stages", "stages", parts, results, () => Stages(po, data, cfg, owned));
        if (Plugin.DoWeapons.Value) anyFailed |= !Step("weapons and passives", "weapons", parts, results, () => Weapons(po, data, cfg, owned));
        if (Plugin.DoArcanas.Value) anyFailed |= !Step("arcanas", "arcanas", parts, results, () => Arcanas(po, data, cfg, owned));
        if (Plugin.DoRelics.Value) anyFailed |= !Step("relics", "relics", parts, results, () => Relics(po, data, cfg, owned));

        // The game's own "cheat code used" save flag (set by its built-in intro cheat). Set, never cleared.
        try { cfg.CheatCodeUsed = true; L.LogInfo("Unlock All: the game's own \"cheat code used\" flag is set on this save (never cleared by this mod)."); }
        catch (Exception ex) { L.LogWarning("Unlock All: could not set the game's cheat-code flag: " + ex.Message); }

        try { po.Save(true, true); L.LogInfo("Unlock All: saved (the game's own backup was requested too)."); }
        catch (Exception ex) { L.LogError("Unlock All: saving failed: " + ex.Message + " (the game normally saves again on its own)."); }

        // Self-checks (read-only): what the save lists and the flag say now, after the save.
        foreach (var (check, tally, error) in results) CheckCategory(check, tally, error, last);
        if (Plugin.SC.Want("cheat_flag"))
        {
            bool flag = false;
            try { flag = cfg.CheatCodeUsed; } catch { }
            if (flag) Plugin.SC.Pass("cheat_flag", "PlayerOptionsData.CheatCodeUsed reads true after the save");
            else if (last) Plugin.SC.Fail("cheat_flag", "PlayerOptionsData.CheatCodeUsed reads false after the save");
            else Plugin.SC.Wait("cheat_flag", "flag not set yet, tried again at the next main menu");
        }
        if (Plugin.SC.Want("dlc_respected")) CheckDlcRespected(data, cfg, owned, before);

        if (anyFailed && !last)
        {
            L.LogWarning("Unlock All: something failed (see above). It is tried once more the next time the main menu shows (go into any menu and back). RunUnlockAtNextStart stays true until then.");
            L.LogInfo("Unlock All: so far - " + string.Join("; ", parts) + ".");
            return false;
        }
        Plugin.RunNext.Value = false;
        L.LogInfo("Unlock All: DONE - " + string.Join("; ", parts) + ". RunUnlockAtNextStart is back to false.");
        L.LogInfo("Unlock All: if a screen still shows something locked, go back to the main menu once or restart the game.");
        return !anyFailed;
    }

    // Runs one category; a category-level exception is logged and counted as failed.
    static bool Step(string name, string check, List<string> parts, List<(string, Tally, string)> results, Func<Tally> f)
    {
        try
        {
            var t = f();
            t.Log(L);
            parts.Add(t.Summary());
            results.Add((check, t, null));
            return t.Failed.Count == 0;
        }
        catch (Exception ex)
        {
            parts.Add(name + " FAILED");
            results.Add((check, null, ex.GetType().Name + ": " + ex.Message));
            L.LogError($"Unlock All: {name} failed completely: {ex}");
            return false;
        }
    }

    // Unlock one entry; a failure is recorded against that entry only, the category carries on.
    // inSave = the self-check read-back (is the entry in the save list now?).
    static void TryUnlock(Tally t, string name, Action unlock, Func<bool> inSave)
    {
        try { unlock(); t.Now.Add(name); t.ReadBack.Add((name, inSave)); }
        catch (Exception ex) { t.Failed.Add(name + " (" + ex.GetType().Name + ": " + ex.Message + ")"); }
    }

    // Self-check of one category: nothing failed and every entry unlocked now is in the save list after the save.
    // Before the last attempt a failure only waits (the category is tried again at the next main menu).
    static void CheckCategory(string id, Tally t, string error, bool last)
    {
        var sc = Plugin.SC;
        if (sc == null || !sc.Want(id)) return;
        if (t == null)
        {
            if (last) sc.Fail(id, "failed completely: " + error);
            else sc.Wait(id, "failed completely, tried again at the next main menu");
            return;
        }
        var missing = new List<string>();
        foreach (var (name, inSave) in t.ReadBack)
        {
            bool ok = false;
            try { ok = inSave(); } catch { }
            if (!ok) missing.Add(name);
        }
        string detail = $"unlocked now {t.Now.Count}, already {t.Already}, left locked {t.Locked.Count}, failed {t.Failed.Count}; " +
                        $"read back from the save: {t.ReadBack.Count - missing.Count}/{t.ReadBack.Count} new entries present" +
                        (missing.Count > 0 ? " - missing: " + Tally.Join(missing) : "");
        if (t.Failed.Count == 0 && missing.Count == 0) sc.Pass(id, detail);
        else if (last) sc.Fail(id, detail);
        else sc.Wait(id, $"failed {t.Failed.Count}, missing {missing.Count}; tried again at the next main menu");
    }

    // Self-check dlc_respected (read-only, own try/catch): nothing NEW in the save belongs to a DLC that isn't owned
    // or to custom/Workshop content. "New" = in the save lists now but not before this attempt.
    static void CheckDlcRespected(DataManager data, PlayerOptionsData cfg, HashSet<DlcType> owned, SaveIds before)
    {
        var sc = Plugin.SC;
        try
        {
            if (before == null) { sc.Wait("dlc_respected", "the save lists were unreadable before the unlock"); return; }
            var after = SaveIds.Take(cfg);
            if (after == null) { sc.Wait("dlc_respected", "the save lists are unreadable after the unlock"); return; }
            Dictionary<int, DlcType> charDlc, stageDlc;
            try { charDlc = DlcMembers(data.AllDlcCharacterData, k => (int)k); stageDlc = DlcMembers(data.AllDlcStageData, k => (int)k); }
            catch (Exception ex) { sc.Wait("dlc_respected", "the game's per-DLC lists are unreadable (" + ex.GetType().Name + ")"); return; }
            var items = data.AllItems;
            var bad = new List<string>();
            int fresh = 0;
            void Look(HashSet<int> now, HashSet<int> was, string what, Func<int, string> block, Func<int, string> name)
            {
                foreach (int id in now)
                {
                    if (was.Contains(id)) continue;
                    fresh++;
                    string why = block(id);
                    if (why != null) bad.Add(what + " " + name(id) + " (" + why + ")");
                }
            }
            string CharBlock(int id)
            {
                if (id >= CustomRangeStart) return "custom/Workshop";
                return charDlc.TryGetValue(id, out var d) && !owned.Contains(d) ? "DLC not owned: " + d : null;
            }
            string StageBlock(int id) => stageDlc.TryGetValue(id, out var d) && !owned.Contains(d) ? "DLC not owned: " + d : null;
            string RelicBlock(int id)
            {
                var key = (ItemType)id;
                if (items == null || !items.ContainsKey(key)) return null;
                int? dlc = NullableField.ReadEnum(items[key], "<requiresDLC>k__BackingField");
                return dlc.HasValue && !owned.Contains((DlcType)dlc.Value) ? "DLC not owned: " + (DlcType)dlc.Value : null;
            }
            Look(after.Characters, before.Characters, "character", CharBlock, id => ((CharacterType)id).ToString());
            Look(after.Bought, before.Bought, "bought character", CharBlock, id => ((CharacterType)id).ToString());
            Look(after.Stages, before.Stages, "stage", StageBlock, id => ((StageType)id).ToString());
            Look(after.Hypers, before.Hypers, "hyper", StageBlock, id => ((StageType)id).ToString());
            Look(after.Relics, before.Relics, "relic", RelicBlock, id => ((ItemType)id).ToString());
            if (bad.Count == 0)
                sc.Pass("dlc_respected", "{0} new save entries (characters, bought, stages, hypers, relics), none from a DLC you don't own or custom/Workshop content", fresh);
            else
                sc.Fail("dlc_respected", "{0} of {1} new save entries belong to a DLC you don't own or custom/Workshop content: {2}", bad.Count, fresh, Tally.Join(bad));
        }
        catch (Exception ex) { OtcPatch.Report(ex, "DLC self-check"); }
    }

    static string Safe(Func<string> f) { try { return f() ?? ""; } catch { return ""; } }

    // ---------- DLC ownership ----------
    static HashSet<DlcType> OwnedDlc()
    {
        var set = new HashSet<DlcType>();
        try
        {
            var owned = DlcSystem.OwnedDlc;
            if (owned != null) for (int i = 0; i < owned.Count; i++) set.Add(owned[i]);
            var included = DlcSystem.IncludedDlc;
            if (included != null) for (int i = 0; i < included.Count; i++) set.Add(included[i]);
        }
        catch (Exception ex) { L.LogWarning("Unlock All: could not read the DLC licenses (" + ex.Message + "); DLC content stays locked."); }
        var all = new List<string>(); var not = new List<string>();
        foreach (DlcType d in Enum.GetValues(typeof(DlcType))) (set.Contains(d) ? all : not).Add(d.ToString());
        L.LogInfo("Unlock All: DLC owned: " + (all.Count > 0 ? string.Join(", ", all) : "none") + "; not owned (stays locked): " + (not.Count > 0 ? string.Join(", ", not) : "none") + ".");
        return set;
    }

    // First ID of the game's custom-character range (CharacterType.CUSTOM_RANGE_START = 805306368 in 1.16.107; the
    // Workshop range starts above it at WORKSHOP_RANGE_START = 1073741824). Compile-time constant from the interop.
    const int CustomRangeStart = (int)CharacterType.CUSTOM_RANGE_START;

    // ContentGroupType MOONSPELL(2)..BLOODMOON(9) = DlcType 0..7; BASE/EXTRA are free; WORKSHOP/CUSTOM are not ours.
    // Returns null when allowed, else the reason.
    static string GroupBlock(int g, HashSet<DlcType> owned)
    {
        if (g == (int)ContentGroupType.BASE || g == (int)ContentGroupType.EXTRA) return null;
        if (g == (int)ContentGroupType.WORKSHOP || g == (int)ContentGroupType.CUSTOM) return "workshop/custom content";
        int dlc = g - (int)ContentGroupType.MOONSPELL;
        if (dlc < 0 || !Enum.IsDefined(typeof(DlcType), dlc)) return "unknown content group " + g + " (newer than this mod)";
        var t = (DlcType)dlc;
        return owned.Contains(t) ? null : "DLC not owned: " + t;
    }

    static string DlcBlock(int? dlc, HashSet<DlcType> owned)
    {
        if (!dlc.HasValue) return null;
        if (!Enum.IsDefined(typeof(DlcType), dlc.Value)) return "unknown DLC " + dlc.Value + " (newer than this mod)";
        var t = (DlcType)dlc.Value;
        return owned.Contains(t) ? null : "DLC not owned: " + t;
    }

    // DlcType of every character/stage listed in the game's per-DLC data (what DlcUtils searches). Missing = base game.
    static Dictionary<int, DlcType> DlcMembers<TKey, TVal>(Il2CppSystem.Collections.Generic.Dictionary<DlcType, Il2CppSystem.Collections.Generic.Dictionary<TKey, TVal>> perDlc, Func<TKey, int> key)
    {
        var map = new Dictionary<int, DlcType>();
        if (perDlc == null) return map;
        foreach (var kv in perDlc)
        {
            if (kv.Value == null) continue;
            foreach (var inner in kv.Value) map[key(inner.Key)] = kv.Key;
        }
        return map;
    }

    // ---------- categories ----------
    static Tally Characters(PlayerOptions po, DataManager data, PlayerOptionsData cfg, HashSet<DlcType> owned)
    {
        var t = new Tally("characters") { SkippedWhat = "internal/hidden entries" };
        var all = data.GetConvertedCharacterData();
        if (all == null) throw new InvalidOperationException("the game's character list is not loaded");
        Dictionary<int, DlcType> dlcOf;
        try { dlcOf = DlcMembers(data.AllDlcCharacterData, k => (int)k); }
        catch (Exception ex) { dlcOf = new Dictionary<int, DlcType>(); L.LogWarning("Unlock All: per-DLC character list unreadable (" + ex.Message + "); using content groups only."); }
        var unlockedList = cfg.UnlockedCharacters; var boughtList = cfg.BoughtCharacters;
        int bought = 0, custom = 0;
        foreach (var kv in all)
        {
            var type = kv.Key; var levels = kv.Value;
            if ((int)type == 0 || levels == null || levels.Count == 0) continue;
            var d = levels[0];
            if (d == null) continue;
            string name = type.ToString();
            // 1.0.3: custom (1.17) and Workshop characters have their own ID ranges; left locked whatever their content
            // group says (an empty group would read as base game). Never unlocked or bought, nothing written to the save.
            if ((int)type >= CustomRangeStart) { t.Locked.Add(name + " (workshop/custom content)"); custom++; continue; }
            if (d.alwaysHidden || name.Contains("FOLLOWER")) { t.Skipped++; continue; }
            string why = null;
            try { why = GroupBlock(NullableField.ReadEnum(d, "<contentGroup>k__BackingField") ?? 0, owned); }
            catch (Exception ex) { why = "content group unreadable (" + ex.Message + ")"; }
            if (why == null && dlcOf.TryGetValue((int)type, out var dlc)) why = DlcBlock((int)dlc, owned);
            if (why != null) { t.Locked.Add(name + " (" + why + ")"); continue; }
            if (unlockedList != null && unlockedList.Contains(type)) t.Already++;
            else TryUnlock(t, name, () => po.UnlockCharacter(type, cfg), () => cfg.UnlockedCharacters.Contains(type));
            if (Plugin.BuyCharacters.Value && (boughtList == null || !boughtList.Contains(type)))
            {
                try { po.BuyCharacter(type, cfg); bought++; t.ReadBack.Add((name + " (bought)", () => cfg.BoughtCharacters.Contains(type))); }
                catch (Exception ex) { t.Failed.Add(name + " (buy: " + ex.Message + ")"); }
            }
        }
        if (Plugin.BuyCharacters.Value) L.LogInfo($"Unlock All: characters marked as bought now: {bought}.");
        L.LogInfo($"Unlock All: custom/Workshop characters (IDs from {CustomRangeStart}) left locked: {custom}.");
        return t;
    }

    static Tally Stages(PlayerOptions po, DataManager data, PlayerOptionsData cfg, HashSet<DlcType> owned)
    {
        var t = new Tally("stages") { SkippedWhat = "adventure/test/hidden entries" };
        var all = data.GetConvertedStages();
        if (all == null) throw new InvalidOperationException("the game's stage list is not loaded");
        Dictionary<int, DlcType> dlcOf;
        try { dlcOf = DlcMembers(data.AllDlcStageData, k => (int)k); }
        catch (Exception ex) { dlcOf = null; L.LogWarning("Unlock All: per-DLC stage list unreadable (" + ex.Message + "); every stage is left locked to be safe."); }
        var stageList = cfg.UnlockedStages; var hyperList = cfg.UnlockedHypers;
        int hypersNow = 0, hypersAlready = 0;
        foreach (var kv in all)
        {
            var type = kv.Key; var levels = kv.Value;
            if (levels == null || levels.Count == 0) continue;
            var d = levels[0];
            if (d == null) continue;
            string name = type.ToString();
            if (d.alwaysHidden || (int)type >= 1000 || name.Contains("TEST")) { t.Skipped++; continue; }   // adventure stages, test stages
            string why = null;
            if (dlcOf == null) why = "DLC membership unknown";
            else if (dlcOf.TryGetValue((int)type, out var dlc)) why = DlcBlock((int)dlc, owned);
            if (why != null) { t.Locked.Add(name + " (" + why + ")"); continue; }
            if (Plugin.DoStages.Value)
            {
                if (stageList != null && stageList.Contains(type)) t.Already++;
                else TryUnlock(t, name, () => po.UnlockStage(type, cfg), () => cfg.UnlockedStages.Contains(type));
            }
            if (Plugin.DoHypers.Value)
            {
                if (hyperList != null && hyperList.Contains(type)) hypersAlready++;
                else
                {
                    try { po.UnlockHyper(type, cfg); hypersNow++; t.ReadBack.Add((name + " (hyper)", () => cfg.UnlockedHypers.Contains(type))); }
                    catch (Exception ex) { t.Failed.Add(name + " (hyper: " + ex.Message + ")"); }
                }
            }
        }
        if (Plugin.DoHypers.Value) L.LogInfo($"Unlock All: hyper modes unlocked now {hypersNow}, already unlocked {hypersAlready}.");
        return t;
    }

    static Tally Weapons(PlayerOptions po, DataManager data, PlayerOptionsData cfg, HashSet<DlcType> owned)
    {
        var t = new Tally("weapons and passives");
        var all = data.GetConvertedWeapons();
        if (all == null) throw new InvalidOperationException("the game's weapon list is not loaded");
        int evos = 0, hidden = 0, gifts = 0;
        var list = cfg.UnlockedWeapons;
        foreach (var kv in all)
        {
            var type = kv.Key; var levels = kv.Value;
            if (levels == null || levels.Count == 0) continue;
            var d = levels[0];
            if (d == null) continue;
            if (d.alwaysHidden) { hidden++; continue; }
            if (d.isEvolution) { evos++; continue; }       // found by evolving, never in the level-up pool
            if (d.isGift) { gifts++; continue; }           // character-specific gifts stay with their character
            string name = type.ToString();
            string why = GroupBlock((int)d.contentGroup, owned);
            if (why != null) { t.Locked.Add(name + " (" + why + ")"); continue; }
            if (list != null && list.Contains(type)) t.Already++;
            else TryUnlock(t, name, () => po.UnlockWeapon(type, cfg), () => cfg.UnlockedWeapons.Contains(type));
        }
        t.Skipped = evos + gifts + hidden;
        t.SkippedWhat = $"entries: {evos} evolutions, {gifts} gifts, {hidden} hidden";
        return t;
    }

    static Tally Arcanas(PlayerOptions po, DataManager data, PlayerOptionsData cfg, HashSet<DlcType> owned)
    {
        var t = new Tally("arcanas") { SkippedWhat = "non-card entries (character skill cards etc.)" };
        var all = data.AllArcanas;
        if (all == null) throw new InvalidOperationException("the game's arcana list is not loaded");
        var list = cfg.UnlockedArcanas;
        foreach (var kv in all)
        {
            var type = kv.Key; var d = kv.Value;
            if (d == null) continue;
            // The Collection shows `major` entries only; everything else (sub-skill cards, survarots) isn't a card here.
            if (!d.major || d.alwaysHidden || (int)type < 0) { t.Skipped++; continue; }
            string name = type.ToString();
            string why = GroupBlock((int)d.contentGroup, owned);
            if (why != null) { t.Locked.Add(name + " (" + why + ")"); continue; }
            if (list != null && list.Contains(type)) t.Already++;
            else TryUnlock(t, name, () => po.UnlockArcana(type, cfg), () => cfg.UnlockedArcanas.Contains(type));
        }
        return t;
    }

    static Tally Relics(PlayerOptions po, DataManager data, PlayerOptionsData cfg, HashSet<DlcType> owned)
    {
        var t = new Tally("relics");
        var all = data.AllItems;
        if (all == null) throw new InvalidOperationException("the game's item list is not loaded");
        var list = cfg.CollectedItems;
        foreach (var kv in all)
        {
            var type = kv.Key; var d = kv.Value;
            if (d == null || !d.isRelic || d.alwaysHidden) continue;
            string name = type.ToString();
            string why;
            try { why = DlcBlock(NullableField.ReadEnum(d, "<requiresDLC>k__BackingField"), owned) ?? GroupBlock((int)d.contentGroup, owned); }
            catch (Exception ex) { why = "DLC requirement unreadable (" + ex.Message + ")"; }
            if (why != null) { t.Locked.Add(name + " (" + why + ")"); continue; }
            if (list != null && list.Contains(type)) t.Already++;
            else TryUnlock(t, name, () => po.UnlockItem(type, cfg), () => cfg.CollectedItems.Contains(type));
        }
        return t;
    }

    // ---------- save file copy ----------
    // Steam keeps the Unity-era save at <Steam>\userdata\<account>\1794680\remote\SaveData (Steam Cloud). Copy every
    // SaveData* found there into BepInEx\offtheclock-save-backups\<timestamp>\ before anything is changed.
    static void BackupSaveFiles()
    {
        try
        {
            var roots = new List<string>();
            foreach (var env in new[] { "ProgramFiles(x86)", "ProgramFiles" })
            {
                var p = Environment.GetEnvironmentVariable(env);
                if (!string.IsNullOrEmpty(p)) roots.Add(Path.Combine(p, "Steam", "userdata"));
            }
            roots.Add(@"C:\Program Files (x86)\Steam\userdata");
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string dest = Path.Combine(Paths.BepInExRootPath, "offtheclock-save-backups", stamp);
            int copied = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                if (!seen.Add(root) || !Directory.Exists(root)) continue;
                foreach (var account in Directory.GetDirectories(root))
                {
                    string remote = Path.Combine(account, "1794680", "remote");
                    if (!Directory.Exists(remote)) continue;
                    foreach (var file in Directory.GetFiles(remote, "SaveData*"))
                    {
                        Directory.CreateDirectory(dest);
                        string target = Path.Combine(dest, Path.GetFileName(account) + "-" + Path.GetFileName(file));
                        File.Copy(file, target, false);
                        copied++;
                        L.LogInfo("Unlock All: save file copied to " + target);
                    }
                }
            }
            if (copied == 0) L.LogWarning("Unlock All: no Steam save file found to copy (looked for Steam\\userdata\\<account>\\1794680\\remote\\SaveData*). The game's own backup is still requested when saving.");
        }
        catch (Exception ex) { L.LogWarning("Unlock All: save file copy failed: " + ex.Message); }
    }
}

// Save-list IDs (read-only copy) for the dlc_respected self-check.
internal sealed class SaveIds
{
    public HashSet<int> Characters, Bought, Stages, Hypers, Relics;

    internal static SaveIds Take(PlayerOptionsData cfg)
    {
        try
        {
            return new SaveIds
            {
                Characters = Ids(cfg.UnlockedCharacters, x => (int)x),
                Bought = Ids(cfg.BoughtCharacters, x => (int)x),
                Stages = Ids(cfg.UnlockedStages, x => (int)x),
                Hypers = Ids(cfg.UnlockedHypers, x => (int)x),
                Relics = Ids(cfg.CollectedItems, x => (int)x),
            };
        }
        catch (Exception ex) { OtcPatch.Report(ex, "DLC self-check (save lists)"); return null; }
    }

    static HashSet<int> Ids<T>(Il2CppSystem.Collections.Generic.List<T> list, Func<T, int> id)
    {
        var set = new HashSet<int>();
        if (list == null) return set;
        int n = list.Count;
        for (int i = 0; i < n; i++) set.Add(id(list[i]));
        return set;
    }
}

// Reads a Nullable<enum> field without Il2CppInterop's Nullable wrapper (which throws when the value is null).
// il2cpp_field_get_value_object boxes the field the IL2CPP way: a Nullable without a value becomes a null pointer,
// one with a value becomes a boxed enum, whose first 4 bytes after unboxing are the int value.
internal static class NullableField
{
    static readonly Dictionary<(IntPtr, string), IntPtr> Cache = new Dictionary<(IntPtr, string), IntPtr>();

    internal static int? ReadEnum(Il2CppObjectBase obj, string fieldName)
    {
        if (obj == null || obj.Pointer == IntPtr.Zero) throw new ArgumentNullException(nameof(obj));
        IntPtr klass = IL2CPP.il2cpp_object_get_class(obj.Pointer);
        if (!Cache.TryGetValue((klass, fieldName), out IntPtr field))
        {
            field = IntPtr.Zero;
            for (IntPtr k = klass; k != IntPtr.Zero && field == IntPtr.Zero; k = IL2CPP.il2cpp_class_get_parent(k))
                field = IL2CPP.il2cpp_class_get_field_from_name(k, fieldName);
            Cache[(klass, fieldName)] = field;
        }
        if (field == IntPtr.Zero) throw new MissingFieldException("field " + fieldName + " not found (game update?)");
        IntPtr boxed = IL2CPP.il2cpp_field_get_value_object(field, obj.Pointer);
        if (boxed == IntPtr.Zero) return null;
        IntPtr raw = IL2CPP.il2cpp_object_unbox(boxed);
        return raw == IntPtr.Zero ? null : Marshal.ReadInt32(raw);
    }
}
