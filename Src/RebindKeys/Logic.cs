using System;
using System.Collections.Generic;

namespace RebindKeys;

// Pure planning, no game types (keys are Unity KeyCode names as strings): compiled into the plugin and into the offline
// tests (Src\tests\VsWave2Tests).
internal readonly struct Binding
{
    internal readonly int ElementId, ActionId, Pole, Flags;
    internal readonly string Key;
    internal Binding(int elementId, int actionId, int pole, string key, int flags) { ElementId = elementId; ActionId = actionId; Pole = pole; Key = key ?? ""; Flags = flags; }
}

internal readonly struct Op
{
    internal readonly bool Replace;       // true = move binding ElementId to NewKey; false = add a new binding (old key keeps working)
    internal readonly Binding From;
    internal readonly string NewKey;
    internal Op(bool replace, Binding from, string newKey) { Replace = replace; From = from; NewKey = newKey; }
    public override string ToString() => $"{(Replace ? "move" : "add")} {From.Key}->{NewKey} (action {From.ActionId})";
}

internal static class Remap
{
    internal static readonly string[] Presets = { "None", "AZERTY", "ESDF" };

    internal static string PresetText(string preset)
    {
        switch ((preset ?? "").Trim().ToUpperInvariant())
        {
            case "AZERTY": return "W=Z, A=Q";
            case "ESDF": return "W=E, A=S, S=D, D=F";
            default: return "";
        }
    }

    /// <summary>
    /// "W=Z, A=Q" (also "W>Z", "W->Z", "W:Z") -> old key -> new key, using resolve() to turn names into the game's key
    /// names (null = unknown name, reported in bad). Later entries win; a key mapped to itself is dropped.
    /// </summary>
    internal static Dictionary<string, string> Parse(string text, Func<string, string> resolve, List<string> bad)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text)) return map;
        foreach (var raw in text.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string part = raw.Trim();
            if (part.Length == 0) continue;
            string[] lr = part.Split(new[] { "->", "=>", "=", ">", ":" }, 2, StringSplitOptions.None);
            if (lr.Length != 2) { bad?.Add(part); continue; }
            string from = resolve(lr[0].Trim()), to = resolve(lr[1].Trim());
            if (from == null || to == null) { bad?.Add(part); continue; }
            if (from == to) { map.Remove(from); continue; }
            map[from] = to;
        }
        return map;
    }

    /// <summary>Preset first, then the custom list on top (custom entries win).</summary>
    internal static Dictionary<string, string> Combine(string preset, string custom, Func<string, string> resolve, List<string> bad)
    {
        var map = Parse(PresetText(preset), resolve, bad);
        foreach (var kv in Parse(custom, resolve, bad)) map[kv.Key] = kv.Value;
        return map;
    }

    /// <summary>
    /// What to do with one keyboard map, decided from a snapshot of its bindings (so chains like ESDF's S=D, D=F use the
    /// original keys). keepOld: the old key keeps working too, unless another remap uses it as its new key (then it must
    /// move, or one key would do two things). Bindings that already exist are not added twice.
    /// </summary>
    internal static List<Op> Plan(IReadOnlyList<Binding> snapshot, Dictionary<string, string> map, bool keepOld)
    {
        var ops = new List<Op>();
        if (snapshot == null || map == null || map.Count == 0) return ops;
        var targets = new HashSet<string>(map.Values, StringComparer.Ordinal);
        var existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var b in snapshot) existing.Add(b.ActionId + "|" + b.Pole + "|" + b.Key);
        foreach (var b in snapshot)
        {
            if (!map.TryGetValue(b.Key, out string to)) continue;
            bool mustMove = !keepOld || targets.Contains(b.Key);
            string sig = b.ActionId + "|" + b.Pole + "|" + to;
            if (!mustMove && existing.Contains(sig)) continue;   // already added on an earlier pass
            ops.Add(new Op(mustMove, b, to));
            existing.Add(sig);
        }
        return ops;
    }

    /// <summary>A stable text of a map's bindings, to notice when the game rebuilt or reset a map we already changed.</summary>
    internal static string Fingerprint(IReadOnlyList<Binding> bindings)
    {
        var parts = new List<string>(bindings.Count);
        foreach (var b in bindings) parts.Add(b.ActionId + ":" + b.Pole + ":" + b.Key + ":" + b.Flags);
        parts.Sort(StringComparer.Ordinal);
        return string.Join(",", parts);
    }

    /// <summary>Key name aliases on top of Unity's KeyCode names: single digits = the number row, "Num5" = Keypad5.</summary>
    internal static string Alias(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        string n = name.Trim().Replace(" ", "");
        if (n.Length == 1 && char.IsDigit(n[0])) return "Alpha" + n;
        if (n.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && n.Length == 4 && char.IsDigit(n[3])) return "Keypad" + n[3];
        if (n.StartsWith("Numpad", StringComparison.OrdinalIgnoreCase) && n.Length == 7 && char.IsDigit(n[6])) return "Keypad" + n[6];
        switch (n.ToLowerInvariant())
        {
            case "up": return "UpArrow";
            case "down": return "DownArrow";
            case "left": return "LeftArrow";
            case "right": return "RightArrow";
            case "enter": return "Return";
            case "esc": return "Escape";
            case "ctrl": case "control": return "LeftControl";
            case "shift": return "LeftShift";
            case "alt": return "LeftAlt";
        }
        return n;
    }
}
