using System;

namespace TimeStopAndSkipMinute;

// Pure clock math, no game types: compiled into the plugin and into the offline tests (Src\tests\VsWave2Tests).
internal static class Clock
{
    /// <summary>
    /// The run clock (seconds survived) to set so that the game's own next one-second tick lands on the next full
    /// minute: the game adds 1 per tick and calls Stage.CheckMinute when the result is a whole minute. Already on the
    /// last second of a minute = jump one more minute. Whole seconds only (the game keeps the clock whole).
    /// </summary>
    internal static float SkipTarget(float seconds)
    {
        if (float.IsNaN(seconds) || seconds < 0f) seconds = 0f;
        float whole = (float)Math.Floor(seconds);
        float minuteStart = (float)Math.Floor(whole / 60f) * 60f;
        float target = minuteStart + 59f;
        if (whole >= target) target += 60f;
        return target;
    }

    /// <summary>Minute shown by the game's clock for this many seconds.</summary>
    internal static int Minute(float seconds) => seconds <= 0f || float.IsNaN(seconds) ? 0 : (int)Math.Floor(seconds / 60f);

    internal static string Mmss(float seconds)
    {
        if (float.IsNaN(seconds) || seconds < 0f) seconds = 0f;
        int s = (int)Math.Floor(seconds);
        return $"{s / 60:00}:{s % 60:00}";
    }
}
