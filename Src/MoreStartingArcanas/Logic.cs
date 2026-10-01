using System;

namespace MoreStartingArcanas;

// Pure counting, no game types: compiled into the plugin and into the offline tests (Src\tests\VsWave2Tests).
internal static class Picks
{
    internal const int MaxStarting = 30;

    /// <summary>
    /// Extra arcana selections to queue after the game's own first one: (wanted - 1), but never more than the player has
    /// unlocked arcanas left to choose from (the first pick takes one). unlocked &lt; 0 = unknown (no limit from it).
    /// </summary>
    internal static int Extra(int wanted, int unlocked)
    {
        int w = Math.Max(1, Math.Min(MaxStarting, wanted));
        int extra = w - 1;
        if (unlocked >= 0) extra = Math.Min(extra, Math.Max(0, unlocked - 1));
        return Math.Max(0, extra);
    }

    /// <summary>The run's arcana limit after the mod: the game's own limit plus the extra start picks (if raised).</summary>
    internal static int Limit(int gameLimit, int extra, bool raise) => raise ? gameLimit + Math.Max(0, extra) : gameLimit;
}
