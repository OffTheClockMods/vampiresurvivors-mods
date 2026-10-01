using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using OffTheClock.Lib;
using VampireSurvivors.Framework;
using VampireSurvivors.Objects.Characters;

namespace MovementSpeedControl;

// Movement Speed Control: a move speed multiplier, plus an optional floor ("never slower than") and cap ("never faster
// than") for your character's final move speed.
//
// Read from the game code (Vampire Survivors 1.16.107, GameAssembly.dll, 2026-09-30, our test notes):
// - CharacterController.PMoveSpeed() is the character's move speed factor: PlayerStats.MoveSpeed (an EggFloat, so
//   golden-egg bonuses are in) x CharacterController.MoveSpeedMultiplier. 1 = 100% (a character with no speed bonus).
// - The game calls it through the virtual-method table from CharacterController.get_ScaledVelocity (movement each frame:
//   GameManager.PlayerPxSpeed x PMoveSpeed() x two of the character's own slow factors) and get_Speed, so a postfix on it
//   changes how fast you actually move. The clamp after it in get_ScaledVelocity is only +-2^31 (no real cap).
// - Four characters override PMoveSpeed without calling the base (FB_Bradfang, TP_Aeon_Character, TP_Zephyr_Character,
//   CharacterController_EX_Chulareh); the mod finds every override by reflection at start and patches each one, so
//   those characters (and any added by a game update) are covered too.
// - The stats panel shows the game's own MoveSpeed stat, not the result of this mod.
// Off in online co-op (MultiplayerManager.IsOnlineMultiplayer). Vampire Survivors has no leaderboards. Nothing is saved.
//
// Self-checks: speed_changed (a PMoveSpeed result was changed as configured, read back from the value the game gets),
// limit_used (optional: the floor or cap actually limited a value).
[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static ManualLogSource LogSrc;
    internal static ConfigEntry<float> Multiplier, Floor, Cap;
    internal const string Id = "MovementSpeedControl";   // Src folder name: log prefix of the shared helpers + self-check id
    internal static OtcSelfCheck SC;

    // In-memory copies read by the per-frame hook (refreshed when the cfg file changes).
    internal static volatile float LiveMult = 1f, LiveFloor, LiveCap;

    public override void Load()
    {
        LogSrc = Log;
        Multiplier = Config.Bind("Speed", "MoveSpeedMultiplier", 1f, new ConfigDescription(
            "Your move speed is multiplied by this (1 = normal, 1.5 = 50% faster, 0.5 = half speed). Stacks with Wings and every other speed bonus. " +
            "Very high values make it hard to steer and you can run past pickups before they reach you. Range 0.1 to 20 (above 20 the stage's background " +
            "tiles may not keep up with you).",
            new AcceptableValueRange<float>(SpeedMath.MinMultiplier, SpeedMath.MaxMultiplier)));
        Floor = Config.Bind("Speed", "MinimumMoveSpeed", 0f, new ConfigDescription(
            "Never slower than this (1 = 100%, the speed of a character with no speed bonus; 0 = off). Useful for slow characters or slowing curses.",
            new AcceptableValueRange<float>(0f, SpeedMath.MaxLimit)));
        Cap = Config.Bind("Speed", "MaximumMoveSpeed", 0f, new ConfigDescription(
            "Never faster than this (1 = 100%; 0 = off). For players who find stacked speed bonuses too fast: 1.5 keeps you at 150% at most. " +
            "If it is lower than MinimumMoveSpeed, this cap wins.",
            new AcceptableValueRange<float>(0f, SpeedMath.MaxLimit)));
        Refresh();
        Multiplier.SettingChanged += OnSettingChanged;
        Floor.SettingChanged += OnSettingChanged;
        Cap.SettingChanged += OnSettingChanged;

        OtcPatch.Init(Log, Id);
        OtcOnline.Configure(Log, Id, () => { var mm = MultiplayerManager.Instance; return mm != null && mm.IsOnlineMultiplayer; }, "online co-op");
        SC = new OtcSelfCheck(Log, Id);
        SC.Expect("speed_changed", "set MoveSpeedMultiplier to something other than 1 and start a run");
        SC.Expect("limit_used", "set MinimumMoveSpeed or MaximumMoveSpeed and start a run (the cap needs a character faster than it)", optional: true);

        var p = new OtcPatcher(new Harmony(PluginInfo.PLUGIN_GUID), Log, Id, SC);
        p.Method("move speed (CharacterController.PMoveSpeed)", typeof(CharacterController), nameof(CharacterController.PMoveSpeed), Type.EmptyTypes,
                 typeof(SpeedPatch), postfix: nameof(SpeedPatch.Postfix), required: true, declaredOnly: true);
        int overrides = 0;
        foreach (Type t in Overrides())
        {
            if (p.Method($"move speed of {t.Name} (own PMoveSpeed)", t, "PMoveSpeed", Type.EmptyTypes, typeof(SpeedPatch),
                         postfix: nameof(SpeedPatch.Postfix), declaredOnly: true))
                overrides++;
        }
        if (!p.Finish()) return;
        LogSrc.LogInfo($"{PluginInfo.PLUGIN_NAME} {PluginInfo.PLUGIN_VERSION} loaded - {Describe()}; {overrides} character(s) with their own speed rule covered (off in online co-op)");
    }

    static void OnSettingChanged(object sender, EventArgs e) => Refresh();

    internal static void Refresh()
    {
        LiveMult = SpeedMath.Clamp(Multiplier.Value, SpeedMath.MinMultiplier, SpeedMath.MaxMultiplier);
        LiveFloor = Math.Max(0f, Floor.Value);
        LiveCap = Math.Max(0f, Cap.Value);
        SpeedPatch.SettingsChanged();
    }

    internal static string Describe()
    {
        var parts = new List<string> { "move speed x" + LiveMult.ToString("0.##", CultureInfo.InvariantCulture) };
        if (LiveFloor > 0f) parts.Add("never below " + SpeedMath.Pct(LiveFloor));
        if (LiveCap > 0f) parts.Add("never above " + SpeedMath.Pct(LiveCap));
        return string.Join(", ", parts);
    }

    // Every CharacterController subclass that declares its own PMoveSpeed (the 1.16.107 game has four).
    static IEnumerable<Type> Overrides()
    {
        Type baseType = typeof(CharacterController);
        Type[] types;
        try { types = baseType.Assembly.GetTypes(); }
        catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
        catch (Exception ex) { LogSrc.LogWarning($"{Id}: could not list the game's character classes ({ex.GetType().Name}); only the normal speed rule is covered."); yield break; }
        foreach (Type t in types)
        {
            bool match = false;
            try
            {
                match = t != baseType && baseType.IsAssignableFrom(t) &&
                        t.GetMethod("PMoveSpeed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null) != null;
            }
            catch { }
            if (match) yield return t;
        }
    }
}

internal static class PluginInfo
{
    public const string PLUGIN_GUID = "offtheclock.movementspeedcontrol";
    public const string PLUGIN_NAME = "Movement Speed Control";
    public const string PLUGIN_VERSION = "1.0.0";
}

internal static class SpeedPatch
{
    static int _frame = -1;
    static bool _online;
    static bool _loggedFirst;

    internal static void SettingsChanged() { _loggedFirst = false; }

    // Postfix on CharacterController.PMoveSpeed (and each override). Touches no game member itself (1.17 hardening):
    // the online check lives in Online() (NoInlining) inside the try.
    internal static void Postfix(ref float __result)
    {
        try
        {
            float m = Plugin.LiveMult, lo = Plugin.LiveFloor, hi = Plugin.LiveCap;
            if (SpeedMath.IsNeutral(m, lo, hi) || Online()) return;
            float before = __result;
            float after = SpeedMath.Apply(before, m, lo, hi);
            __result = after;
            Checks(before, after, m, lo, hi);
        }
        catch (Exception ex) { OtcPatch.Report(ex, "move speed postfix"); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static bool Online()
    {
        int f = UnityEngine.Time.frameCount;
        if (f != _frame) { _frame = f; _online = OtcOnline.IsOnline; }
        return _online;
    }

    static void Checks(float before, float after, float m, float lo, float hi)
    {
        if (!_loggedFirst)
        {
            _loggedFirst = true;
            Plugin.LogSrc.LogInfo($"Movement Speed Control: active - {Plugin.Describe()} (game speed {SpeedMath.Pct(before)} -> {SpeedMath.Pct(after)}).");
        }
        var sc = Plugin.SC;
        if (sc.Want("speed_changed") && Math.Abs(m - 1f) > 0.0001f)
        {
            float raw = before * m;
            bool limited = (lo > 0f && raw < lo) || (hi > 0f && raw > hi);
            if (!limited)
                sc.Check("speed_changed", Math.Abs(after - raw) <= 0.0001f + 0.0001f * Math.Abs(raw),
                         "game speed {0} -> {1} handed back to the game (x{2})", SpeedMath.Pct(before), SpeedMath.Pct(after), m);
        }
        if (sc.Want("limit_used"))
        {
            float raw = before * m;
            if (hi > 0f && raw > hi) sc.Check("limit_used", Math.Abs(after - hi) < 0.0001f, "cap {0}: {1} -> {2}", SpeedMath.Pct(hi), SpeedMath.Pct(raw), SpeedMath.Pct(after));
            else if (lo > 0f && raw < lo) sc.Check("limit_used", Math.Abs(after - Math.Min(lo, SpeedMath.MaxLimit)) < 0.0001f, "floor {0}: {1} -> {2}", SpeedMath.Pct(lo), SpeedMath.Pct(raw), SpeedMath.Pct(after));
        }
    }
}
