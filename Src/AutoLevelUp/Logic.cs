using System;
using System.Collections.Generic;
using System.Text;

namespace AutoLevelUp;

// Pure choice logic, no game types: compiled into the plugin and into the offline tests (Src\tests\VsWave2Tests).
internal enum Fallback { Random, First, ShowScreen }

internal readonly struct Offer
{
    internal readonly string Id;        // the game's internal name, e.g. MAGIC_MISSILE
    internal readonly string Display;   // the name the game shows, e.g. Magic Wand (may be empty)
    internal Offer(string id, string display) { Id = id ?? ""; Display = display ?? ""; }
    public override string ToString() => string.IsNullOrEmpty(Display) || Norm(Display) == Norm(Id) ? Id : $"{Display} ({Id})";
    internal static string Norm(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (char c in s) if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
}

internal static class Chooser
{
    /// <summary>Splits a priority / avoid list ("Whip, King Bible; SPINACH") into normalized entries, order kept, duplicates dropped.</summary>
    internal static List<string> ParseList(string text)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return list;
        foreach (var part in text.Split(new[] { ',', ';', '\n', '\r', '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string n = Offer.Norm(part);
            if (n.Length > 0 && !list.Contains(n)) list.Add(n);
        }
        return list;
    }

    internal static Fallback ParseFallback(string text)
    {
        string n = Offer.Norm(text);
        if (n == "first" || n == "leftmost") return Fallback.First;
        if (n == "showscreen" || n == "screen" || n == "manual" || n == "ask") return Fallback.ShowScreen;
        return Fallback.Random;
    }

    /// <summary>Exact match on the internal or shown name; for entries of 4+ letters also a part of either name.</summary>
    internal static bool Matches(string entry, Offer o, bool allowPartial)
    {
        string id = Offer.Norm(o.Id), disp = Offer.Norm(o.Display);
        if (entry == id || (disp.Length > 0 && entry == disp)) return true;
        return allowPartial && entry.Length >= 4 && (id.Contains(entry) || (disp.Length > 0 && disp.Contains(entry)));
    }

    /// <summary>
    /// Index of the offer to take, or -1 = open the level-up screen (nothing allowed is offered and the fallback is
    /// ShowScreen, or every offer is on the avoid list). reason says why, for the log.
    /// The priority list is walked in order; for each entry exact matches win over partial ones.
    /// </summary>
    internal static int Pick(IReadOnlyList<Offer> offers, List<string> priority, List<string> avoid, Fallback fallback, Random rng, out string reason)
    {
        reason = "";
        if (offers == null || offers.Count == 0) { reason = "nothing offered"; return -1; }
        var allowed = new List<int>();
        for (int i = 0; i < offers.Count; i++)
        {
            bool avoided = false;
            if (avoid != null) foreach (var a in avoid) if (Matches(a, offers[i], true)) { avoided = true; break; }
            if (!avoided) allowed.Add(i);
        }
        if (allowed.Count == 0) { reason = "everything offered is on your avoid list"; return -1; }
        if (priority != null)
        {
            for (int p = 0; p < priority.Count; p++)
            {
                foreach (int i in allowed) if (Matches(priority[p], offers[i], false)) { reason = $"priority #{p + 1}"; return i; }
                foreach (int i in allowed) if (Matches(priority[p], offers[i], true)) { reason = $"priority #{p + 1} (partial name)"; return i; }
            }
        }
        switch (fallback)
        {
            case Fallback.ShowScreen: reason = "nothing from your priority list offered"; return -1;
            case Fallback.First: reason = "first allowed offer (nothing from your list)"; return allowed[0];
            default:
                reason = "random allowed offer (nothing from your list)";
                return allowed[(rng ?? new Random()).Next(allowed.Count)];
        }
    }
}
