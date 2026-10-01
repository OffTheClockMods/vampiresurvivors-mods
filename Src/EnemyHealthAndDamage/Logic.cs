using System;

namespace EnemyHealthAndDamage;

// Pure math, no game types: compiled into the plugin and into the offline tests (Src\tests\VsWave2Tests).
internal static class Mult
{
    internal const float MinHealth = 0.01f, MaxHealth = 100f;
    internal const float MinDamage = 0f, MaxDamage = 100f;

    internal static float Health(float v) => Clamp(v, MinHealth, MaxHealth, 1f);
    internal static float Damage(float v) => Clamp(v, MinDamage, MaxDamage, 1f);

    internal static float Clamp(float v, float lo, float hi, float fallback) =>
        float.IsNaN(v) || float.IsInfinity(v) ? fallback : (v < lo ? lo : (v > hi ? hi : v));

    /// <summary>
    /// New (hp, maxHp) after a freshly spawned enemy got its health from the game. Health is kept at least 1 hit point
    /// above 0 when the game gave it any (x0.01 must not spawn enemies that are already dead), never above the new max.
    /// </summary>
    internal static void ScaleEnemy(float hp, float maxHp, float m, out float newHp, out float newMax)
    {
        newHp = hp; newMax = maxHp;
        if (Math.Abs(m - 1f) < 0.0001f || float.IsNaN(hp) || float.IsNaN(maxHp)) return;
        newMax = maxHp * m;
        newHp = hp * m;
        if (maxHp > 0f && newMax < 1f) newMax = 1f;
        if (hp > 0f && newHp < 1f) newHp = 1f;
        if (newHp > newMax && newMax > 0f) newHp = newMax;
    }

    /// <summary>Damage you take after the multiplier (never negative).</summary>
    internal static float ScaleDamage(float amount, float m)
    {
        if (float.IsNaN(amount) || amount <= 0f) return amount;
        float v = amount * m;
        return v < 0f ? 0f : v;
    }

    internal static string X(float m) => "x" + m.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
