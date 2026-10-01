using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using OffTheClock.Lib;
using Rewired;
using VampireSurvivors.Framework;
using VampireSurvivors.Objects.Characters;
using VampireSurvivors.UI;

namespace RebindKeys;

// Rebind Keys - AZERTY and Custom Keys: change the game's keyboard keys from a config file (one line for ZQSD).
//
// Read from the game code (Vampire Survivors 1.16.107, 2026-09-30, our test notes and our test notes):
// - The game reads input through Rewired. Its keyboard map (25 bindings in resources.assets) binds W A S D and the arrows
//   (move + menus), Enter/Space/Keypad Enter (submit), Esc (cancel/pause), Page Up/Down (menu pages), Tab (speed toggle)
//   and Left Alt/AltGr. There is no rebinding screen in the game.
// - Rewired's own runtime API changes a player's keyboard map in memory: Player.controllers.maps.GetAllMaps(Keyboard)
//   lists the maps, ControllerMap.GetElementMaps lists each binding (ActionElementMap: id, actionId, axis pole, keyCode,
//   modifier keys), ControllerMap.ReplaceElementMap(id, actionId, pole, newKey, modifiers) moves one binding to another
//   key and CreateElementMap(actionId, pole, newKey, modifiers) adds a second key for the same action. Nothing is saved:
//   the game has no rebinding data to store, so removing the mod gives the normal keys back.
// - Keys the game reads directly (not through Rewired) stay as they are: Return and Down arrow on some prompts
//   (GameManager.CheckValidToastieInputs), letters on the intro cheat screen and on the Secrets page.
// When: at the main menu (MainMenuPage.OnShowStart) and at every run start (CharacterController.AfterFullInitialization),
// for every Rewired player including the system player. A map we already changed is recognised by its fingerprint and
// left alone, so chains like ESDF (S=D, D=F) are applied once; a map the game rebuilt or reset is changed again.
// Off in online co-op? Key bindings are local only and change nothing about the game or other players, so this mod also
// works in online co-op (like a keyboard layout setting).
//
// Self-check: keys_changed (after applying, every remapped action has a binding on its new key, read back from the map).
[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static ManualLogSource LogSrc;
    internal static ConfigEntry<string> Preset, Custom;
    internal static ConfigEntry<bool> KeepOld;
    internal const string Id = "RebindKeys";   // Src folder name: log prefix of the shared helpers + self-check id
    internal static OtcSelfCheck SC;

    public override void Load()
    {
        LogSrc = Log;
        Preset = Config.Bind("Keys", "Preset", "None", new ConfigDescription(
            "Ready-made layouts: AZERTY = Z Q S D to move (W=Z, A=Q), ESDF = E S D F to move (W=E, A=S, S=D, D=F), None = only your own Remap list.",
            new AcceptableValueList<string>(Remap.Presets)));
        Custom = Config.Bind("Keys", "Remap", "",
            "Your own changes, old key = new key, comma-separated (applied after the preset). Example: W=Z, A=Q, Space=J. " +
            "Use Unity key names (A-Z, Alpha1 or just 1, Keypad5 or Num5, UpArrow or Up, Return or Enter, Escape or Esc, Space, Tab, LeftShift ...). " +
            "The log lists every key the game uses with its action.");
        KeepOld = Config.Bind("Keys", "KeepOldKeysWorking", true,
            "true = the old key keeps working as well (safest: nothing is lost). It is moved anyway when another remap uses it as a new key, " +
            "so one key never does two things because of this mod. false = old keys stop working.");

        OtcPatch.Init(Log, Id);
        SC = new OtcSelfCheck(Log, Id);
        SC.Expect("keys_changed", "set Preset = AZERTY (or a Remap line) and open the main menu");

        var p = new OtcPatcher(new Harmony(PluginInfo.PLUGIN_GUID), Log, Id, SC);
        bool menu = p.Class(typeof(MenuPatch), feature: "apply at the main menu (MainMenuPage.OnShowStart)");
        bool run = p.Class(typeof(RunPatch), feature: "apply at run start (CharacterController.AfterFullInitialization)");
        p.Finish();
        if (!menu && !run)
        {
            LogSrc.LogError($"{PluginInfo.PLUGIN_NAME}: neither hook could be applied (game update?). The mod does nothing.");
            SC.FailOpen("no hook could be applied");
            return;
        }
        LogSrc.LogInfo($"{PluginInfo.PLUGIN_NAME} {PluginInfo.PLUGIN_VERSION} loaded - preset {Preset.Value}, remap \"{Custom.Value}\", old keys {(KeepOld.Value ? "keep working" : "stop working")}");
    }
}

internal static class PluginInfo
{
    public const string PLUGIN_GUID = "offtheclock.rebindkeys";
    public const string PLUGIN_NAME = "Rebind Keys";
    public const string PLUGIN_VERSION = "1.0.0";
}

[HarmonyPatch(typeof(MainMenuPage), nameof(MainMenuPage.OnShowStart))]
internal static class MenuPatch
{
    [HarmonyPostfix]
    static void After() { try { Rebinder.Apply("main menu"); } catch (Exception ex) { OtcPatch.Report(ex, "main menu postfix"); } }
}

[HarmonyPatch(typeof(CharacterController), nameof(CharacterController.AfterFullInitialization))]
internal static class RunPatch
{
    [HarmonyPostfix]
    static void After() { try { Rebinder.Apply("run start"); } catch (Exception ex) { OtcPatch.Report(ex, "run start postfix"); } }
}

internal static class Rebinder
{
    static readonly Dictionary<IntPtr, string> Done = new Dictionary<IntPtr, string>();   // map -> fingerprint after our change
    static bool _listedKeys;

    static string Resolve(string name)
    {
        string n = Remap.Alias(name);
        if (string.IsNullOrEmpty(n)) return null;
        return Enum.TryParse(n, true, out UnityEngine.KeyCode k) && k != UnityEngine.KeyCode.None ? k.ToString() : null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Apply(string when)
    {
        if (!ReInput.isReady) return;
        var bad = new List<string>();
        var map = Remap.Combine(Plugin.Preset.Value, Plugin.Custom.Value, Resolve, bad);
        foreach (var b in bad) OtcPatch.Report(new FormatException($"'{b}' is not a valid 'old = new' pair (unknown key name?) - skipped"), "remap entry " + b);
        var players = new List<Player>();
        try
        {
            int n = ReInput.players.playerCount;
            for (int i = 0; i < n; i++) { var pl = ReInput.players.GetPlayer(i); if (pl != null) players.Add(pl); }
            var sys = ReInput.players.GetSystemPlayer();
            if (sys != null) players.Add(sys);
        }
        catch (Exception ex) { OtcPatch.Report(ex, "player list"); }

        int changedMaps = 0, moved = 0, added = 0, failed = 0;
        var maps = new Il2CppSystem.Collections.Generic.List<ControllerMap>();
        var elems = new Il2CppSystem.Collections.Generic.List<ActionElementMap>();
        foreach (var pl in players)
        {
            maps.Clear();
            pl.controllers.maps.GetAllMaps(ControllerType.Keyboard, maps);
            for (int m = 0; m < maps.Count; m++)
            {
                var cm = maps[m];
                if (cm == null) continue;
                var snap = Snapshot(cm, elems);
                if (!_listedKeys && pl.id == 0) ListKeys(cm, snap);
                if (map.Count == 0) continue;
                string fp = Remap.Fingerprint(snap);
                if (Done.TryGetValue(cm.Pointer, out string doneFp) && doneFp == fp) continue;   // already ours, unchanged
                var ops = Remap.Plan(snap, map, Plugin.KeepOld.Value);
                foreach (var op in ops)
                {
                    bool ok;
                    Enum.TryParse(op.NewKey, out UnityEngine.KeyCode key);
                    if (op.Replace)
                        ok = cm.ReplaceElementMap(op.From.ElementId, op.From.ActionId, (Pole)op.From.Pole, key, (ModifierKeyFlags)op.From.Flags);
                    else
                        ok = cm.CreateElementMap(op.From.ActionId, (Pole)op.From.Pole, key, (ModifierKeyFlags)op.From.Flags);
                    if (!ok) { failed++; Plugin.LogSrc.LogWarning($"Rebind Keys: the game refused to {op} ({ActionName(op.From.ActionId)})."); }
                    else if (op.Replace) moved++; else added++;
                }
                var after = Snapshot(cm, elems);
                Done[cm.Pointer] = Remap.Fingerprint(after);
                if (ops.Count > 0) { changedMaps++; Verify(map, snap, after, when); }
            }
        }
        _listedKeys = true;
        if (changedMaps > 0)
            Plugin.LogSrc.LogInfo($"Rebind Keys ({when}): {changedMaps} keyboard map(s) changed - {moved} key(s) moved, {added} key(s) added{(failed > 0 ? $", {failed} refused" : "")}. Active changes: {Describe(map)}.");
    }

    static List<Binding> Snapshot(ControllerMap cm, Il2CppSystem.Collections.Generic.List<ActionElementMap> elems)
    {
        var list = new List<Binding>();
        elems.Clear();
        cm.GetElementMaps(elems);
        for (int i = 0; i < elems.Count; i++)
        {
            var e = elems[i];
            if (e == null) continue;
            var k = e.keyCode;
            if (k == UnityEngine.KeyCode.None) continue;
            list.Add(new Binding(e.id, e.actionId, (int)e.axisContribution, k.ToString(), (int)e.modifierKeyFlags));
        }
        return list;
    }

    // Read-back: every action that had an old key now has a binding on its new key.
    static void Verify(Dictionary<string, string> map, List<Binding> before, List<Binding> after, string when)
    {
        if (!Plugin.SC.Want("keys_changed")) return;
        var have = new HashSet<string>();
        foreach (var b in after) have.Add(b.ActionId + "|" + b.Pole + "|" + b.Key);
        int need = 0, ok = 0;
        var missing = new List<string>();
        foreach (var b in before)
        {
            if (!map.TryGetValue(b.Key, out string to)) continue;
            need++;
            if (have.Contains(b.ActionId + "|" + b.Pole + "|" + to)) ok++; else missing.Add($"{b.Key}->{to} ({ActionName(b.ActionId)})");
        }
        if (need == 0) return;
        Plugin.SC.Check("keys_changed", ok == need, "{0}: {1}/{2} remapped bindings read back on their new key{3}", when, ok, need,
                        missing.Count > 0 ? "; missing " + string.Join(", ", missing) : "");
    }

    // Once per session: the keys the game uses, so players can write their Remap line.
    static void ListKeys(ControllerMap cm, List<Binding> snap)
    {
        var parts = new List<string>();
        foreach (var b in snap) parts.Add($"{b.Key} = {ActionName(b.ActionId)}{(b.Pole == 1 ? " (-)" : "")}");
        string cat = "";
        try { cat = " (" + cm.categoryId + ")"; } catch { }
        Plugin.LogSrc.LogInfo($"Rebind Keys: keyboard map{cat} - {string.Join(", ", parts)}.");
    }

    static string ActionName(int id)
    {
        try
        {
            var a = ReInput.mapping.GetAction(id);
            if (a != null) return string.IsNullOrEmpty(a.descriptiveName) ? a.name : a.descriptiveName;
        }
        catch { }
        return "action " + id;
    }

    static string Describe(Dictionary<string, string> map)
    {
        var parts = new List<string>();
        foreach (var kv in map) parts.Add(kv.Key + "->" + kv.Value);
        return parts.Count == 0 ? "none" : string.Join(", ", parts);
    }
}
