using System;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using OffTheClock.Lib;
using VampireSurvivors.Framework;
using VampireSurvivors.Objects.Characters;

namespace EnemyHealthAndDamage;

// Enemy Health and Damage Multiplier: two sliders for difficulty. Enemy health (x0.01 to x100) and the damage you take
// (x0 to x100).
//
// Read from the game code (Vampire Survivors 1.16.107, GameAssembly.dll, 2026-09-30, our test notes):
// - Enemy health: EnemyController.OnRecycleEnemy() runs every time an enemy is spawned from its pool (InitEnemy calls it
//   through the virtual-method table). It sets _maxHp = base health x GameManager.EnemyHealthMultiplier x
//   GameManager.DifficultyAdjustmentEnemyHPMultiplier x Stage.EnemyHealthMultiplier (the stage's own per-minute and
//   per-cycle growth) and then _hp = _maxHp. Every override (bosses, Vicious Hunger, ...) calls the base first. A postfix
//   on the base multiplies both fields once per spawn, on top of everything the game already applied. The game's own
//   multipliers are left alone (the Bloodmoon "risk" survarots and the stage's minute growth keep working).
// - Damage you take: CharacterController.GetDamaged(float damageAmount) is the one place a character loses health
//   (armor, shields, invulnerability and revivals are handled inside it). A prefix scales damageAmount first, so armor
//   still subtracts from the scaled hit. Damage from your own weapons (GetDamagedByOwnWeapon, e.g. some characters'
//   self-damage) is left alone.
// - Some special characters have their own GetDamaged without calling the base; some special enemies set their own
//   health in their own InitEnemy. Those keep the game's numbers (the README says so).
// Off in online co-op (MultiplayerManager.IsOnlineMultiplayer). Vampire Survivors has no leaderboards. Nothing is saved.
//
// Self-checks: health_scaled (a spawned enemy's max health reads base x multiplier afterwards), damage_scaled (a hit was
// handed to the game as hit x multiplier).
[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static ManualLogSource LogSrc;
    internal static ConfigEntry<float> HealthMult, DamageMult;
    internal const string Id = "EnemyHealthAndDamage";   // Src folder name: log prefix of the shared helpers + self-check id
    internal static OtcSelfCheck SC;
    internal static volatile float LiveHealth = 1f, LiveDamage = 1f;

    public override void Load()
    {
        LogSrc = Log;
        HealthMult = Config.Bind("Difficulty", "EnemyHealthMultiplier", 1f, new ConfigDescription(
            "Enemy health is multiplied by this (1 = normal, 3 = three times as tough, 0.5 = half). Applies to every enemy and boss spawned from now on, " +
            "on top of the game's own health growth over time. High values make runs much longer and harder; very low values make most enemies die to any hit. " +
            "Range 0.01 to 100.",
            new AcceptableValueRange<float>(Mult.MinHealth, Mult.MaxHealth)));
        DamageMult = Config.Bind("Difficulty", "DamageTakenMultiplier", 1f, new ConfigDescription(
            "Damage you take from enemies is multiplied by this (1 = normal, 2 = double, 0.5 = half, 0 = no damage at all). Armor still counts. " +
            "Range 0 to 100 (at 100 almost any hit kills you).",
            new AcceptableValueRange<float>(Mult.MinDamage, Mult.MaxDamage)));
        Refresh();
        HealthMult.SettingChanged += OnSettingChanged;
        DamageMult.SettingChanged += OnSettingChanged;

        OtcPatch.Init(Log, Id);
        OtcOnline.Configure(Log, Id, () => { var mm = MultiplayerManager.Instance; return mm != null && mm.IsOnlineMultiplayer; }, "online co-op");
        SC = new OtcSelfCheck(Log, Id);
        SC.Expect("health_scaled", "set EnemyHealthMultiplier to something other than 1 and start a run");
        SC.Expect("damage_scaled", "set DamageTakenMultiplier to something other than 1, start a run and get hit once");

        // Health and damage are separate parts: if a game update removes one hook, the other keeps working.
        var p = new OtcPatcher(new Harmony(PluginInfo.PLUGIN_GUID), Log, Id, SC);
        bool hp = p.Method("enemy health (EnemyController.OnRecycleEnemy)", typeof(EnemyController), nameof(EnemyController.OnRecycleEnemy), Type.EmptyTypes,
                           typeof(Patches), postfix: nameof(Patches.AfterSpawn), declaredOnly: true);
        bool dmg = p.Method("damage taken (CharacterController.GetDamaged)", typeof(CharacterController), nameof(CharacterController.GetDamaged), new[] { typeof(float) },
                            typeof(Patches), prefix: nameof(Patches.BeforeDamage), declaredOnly: true);
        if (dmg)
            p.Method("own-weapon damage stays normal (CharacterController.GetDamagedByOwnWeapon)", typeof(CharacterController), nameof(CharacterController.GetDamagedByOwnWeapon),
                     new[] { typeof(float) }, typeof(Patches), prefix: nameof(Patches.OwnStart), postfix: nameof(Patches.OwnEnd), declaredOnly: true);
        p.Finish();
        if (!hp) SC.Fail("health_scaled", "the enemy health hook could not be applied (see the WARNING above)");
        if (!dmg) SC.Fail("damage_scaled", "the damage hook could not be applied (see the WARNING above)");
        if (!hp && !dmg)
        {
            LogSrc.LogError($"{PluginInfo.PLUGIN_NAME}: could not hook the game's enemy health or damage code (game update?). The mod does nothing.");
            SC.FailOpen("no hook could be applied");
            return;
        }
        LogSrc.LogInfo($"{PluginInfo.PLUGIN_NAME} {PluginInfo.PLUGIN_VERSION} loaded - enemy health {Mult.X(LiveHealth)}, damage taken {Mult.X(LiveDamage)} (1 = normal; off in online co-op)");
    }

    static void OnSettingChanged(object sender, EventArgs e) => Refresh();

    internal static void Refresh()
    {
        LiveHealth = Mult.Health(HealthMult.Value);
        LiveDamage = Mult.Damage(DamageMult.Value);
        Patches.SettingsChanged();
    }
}

internal static class PluginInfo
{
    public const string PLUGIN_GUID = "offtheclock.enemyhealthanddamage";
    public const string PLUGIN_NAME = "Enemy Health and Damage Multiplier";
    public const string PLUGIN_VERSION = "1.0.0";
}

internal static class Patches
{
    static bool _ownWeapon;
    static bool _loggedHp, _loggedDmg;

    internal static void SettingsChanged() { _loggedHp = false; _loggedDmg = false; }

    // Postfix on EnemyController.OnRecycleEnemy. No game member in the body itself (1.17 hardening): Scale() is NoInlining.
    internal static void AfterSpawn(EnemyController __instance)
    {
        try
        {
            float m = Plugin.LiveHealth;
            if (Math.Abs(m - 1f) < 0.0001f || __instance == null) return;
            Scale(__instance, m);
        }
        catch (Exception ex) { OtcPatch.Report(ex, "enemy health postfix"); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Scale(EnemyController e, float m)
    {
        if (OtcOnline.IsOnline) return;
        float hp = e._hp, max = e._maxHp;
        Mult.ScaleEnemy(hp, max, m, out float newHp, out float newMax);
        e._maxHp = newMax;
        e._hp = newHp;
        if (!_loggedHp)
        {
            _loggedHp = true;
            string what = "an enemy";
            try { what = e.EnemyType.ToString(); } catch { }
            Plugin.LogSrc.LogInfo($"Enemy Health and Damage: enemy health {Mult.X(m)} active ({what}: {max:0.##} -> {newMax:0.##} max health).");
        }
        if (Plugin.SC.Want("health_scaled"))
        {
            float readMax = e._maxHp;   // read back from the game object
            Plugin.SC.Check("health_scaled", Math.Abs(readMax - newMax) <= 0.01f + 0.0001f * Math.Abs(newMax) && (max <= 0f || Math.Abs(newMax - Math.Max(1f, max * m)) <= 0.01f + 0.0001f * newMax),
                            "spawned enemy max health {0:0.##} -> {1:0.##} ({2}); reads {3:0.##} afterwards", max, newMax, Mult.X(m), readMax);
        }
    }

    // Prefix on CharacterController.GetDamaged(float damageAmount). Only our own fields and pure math in here.
    internal static void BeforeDamage(ref float damageAmount)
    {
        try
        {
            float m = Plugin.LiveDamage;
            if (Math.Abs(m - 1f) < 0.0001f || _ownWeapon || damageAmount <= 0f || IsOnline()) return;
            float before = damageAmount;
            damageAmount = Mult.ScaleDamage(before, m);
            if (!_loggedDmg)
            {
                _loggedDmg = true;
                Plugin.LogSrc.LogInfo($"Enemy Health and Damage: damage taken {Mult.X(m)} active (hit {before:0.##} -> {damageAmount:0.##} before armor).");
            }
            if (Plugin.SC.Want("damage_scaled"))
                Plugin.SC.Check("damage_scaled", Math.Abs(damageAmount - before * m) <= 0.001f + 0.0001f * before * m,
                                "hit {0:0.##} -> {1:0.##} handed to the game ({2}; armor is subtracted after this)", before, damageAmount, Mult.X(m));
        }
        catch (Exception ex) { OtcPatch.Report(ex, "damage prefix"); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static bool IsOnline() => OtcOnline.IsOnline;

    internal static void OwnStart() { _ownWeapon = true; }
    internal static void OwnEnd() { _ownWeapon = false; }
}
