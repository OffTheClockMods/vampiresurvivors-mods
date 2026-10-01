using System;

namespace MovementSpeedControl;

// Pure math, no game types: compiled into the plugin and into the offline tests (Src\tests\VsWave2Tests).
internal static class SpeedMath
{
    internal const float MinMultiplier = 0.1f, MaxMultiplier = 20f;
    internal const float MaxLimit = 50f;

    /// <summary>
    /// The game's move speed factor (1 = 100%, a character with no speed bonus) after the mod: multiplied, then kept
    /// between the floor and the cap. 0 (or less) for floor/cap = that limit is off. A cap below the floor wins
    /// (the player asked for "never faster than"). NaN/infinity from the game pass through unchanged.
    /// </summary>
    internal static float Apply(float gameValue, float multiplier, float floor, float cap)
    {
        if (float.IsNaN(gameValue) || float.IsInfinity(gameValue)) return gameValue;
        float m = Clamp(multiplier, MinMultiplier, MaxMultiplier);
        float v = gameValue * m;
        if (floor > 0f && v < floor) v = Math.Min(floor, MaxLimit);
        if (cap > 0f && v > cap) v = cap;
        return v;
    }

    /// <summary>True when the settings change nothing at all (multiplier 1, no floor, no cap).</summary>
    internal static bool IsNeutral(float multiplier, float floor, float cap) =>
        Math.Abs(multiplier - 1f) < 0.0001f && floor <= 0f && cap <= 0f;

    internal static float Clamp(float v, float lo, float hi) => float.IsNaN(v) ? 1f : (v < lo ? lo : (v > hi ? hi : v));

    internal static string Pct(float factor) => (factor * 100f).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";
}
