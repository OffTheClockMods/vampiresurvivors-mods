using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

// Offline tests for the Vampire Survivors wave 2 plugins. See VsWave2Tests.csproj for what and how.
// Exit code 0 = all passed, 1 = a failure (expected in break mode).
internal static class Program
{
    static int _pass, _fail;
    static string _break = "";          // mod name to break on purpose (break mode)

    static readonly string Src = FindSrc();
    static readonly string Refs = Path.GetFullPath(Path.Combine(Src, "..", "Working", "vs-refs"));

    static int Main(string[] args)
    {
        int bi = Array.IndexOf(args, "break");
        if (bi >= 0 && bi + 1 < args.Length) _break = args[bi + 1];
        if (_break != "") Console.WriteLine($"BREAK MODE: {_break} gets a renamed game method and a wrong expectation (this run must FAIL)\n");

        AssemblyLoadContext.Default.Resolving += Resolve;

        Section("Logic: Movement Speed Control", LogicMovement);
        Section("Logic: Enemy Health and Damage", LogicEnemy);
        Section("Logic: Time Stop and Skip Minute", LogicClock);
        Section("Logic: Auto Level Up", LogicAuto);
        Section("Logic: More Starting Arcanas", LogicArcanas);
        Section("Logic: Rebind Keys", LogicRebind);
        Section("Patch targets (interop for Vampire Survivors 1.16.107)", PatchTargets);

        Console.WriteLine($"\n{_pass} passed, {_fail} failed");
        return _fail == 0 ? 0 : 1;
    }

    // ---------------- helpers ----------------
    static string FindSrc()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Directory.Build.props")) || (d != null && d.Name == "tests")) d = d.Parent;
        // d = ...\Src\ (the first folder up with a Directory.Build.props that is not tests\)
        return d?.FullName ?? throw new DirectoryNotFoundException("Src folder not found");
    }

    static Assembly Resolve(AssemblyLoadContext ctx, AssemblyName an)
    {
        foreach (var dir in new[] { Refs, Path.Combine(Refs, "interop") })
        {
            string p = Path.Combine(dir, an.Name + ".dll");
            if (File.Exists(p)) return ctx.LoadFromAssemblyPath(p);
        }
        return null;
    }

    static void Section(string name, Action body)
    {
        Console.WriteLine("== " + name);
        try { body(); }
        catch (Exception ex) { Fail($"section crashed: {ex.GetType().Name}: {ex.Message}"); }
    }

    static void Check(bool ok, string what)
    {
        if (ok) { _pass++; Console.WriteLine("  PASS " + what); }
        else Fail(what);
    }

    static void Fail(string what) { _fail++; Console.WriteLine("  FAIL " + what); }

    static bool Near(float a, float b) => Math.Abs(a - b) <= 0.0001f + 0.0001f * Math.Abs(b);

    static float Wrong(string mod, float v) => _break == mod ? v + 1f : v;   // break mode: a wrong expectation

    // ---------------- logic ----------------
    static void LogicMovement()
    {
        const string M = "MovementSpeedControl";
        Check(Near(MovementSpeedControl.SpeedMath.Apply(1.2f, 1.5f, 0, 0), Wrong(M, 1.8f)), "x1.5 of 120% = 180%");
        Check(Near(MovementSpeedControl.SpeedMath.Apply(3f, 1f, 0, 1.5f), 1.5f), "cap 150% holds a 300% character at 150%");
        Check(Near(MovementSpeedControl.SpeedMath.Apply(0.6f, 1f, 1f, 0), 1f), "floor 100% lifts a 60% character to 100%");
        Check(Near(MovementSpeedControl.SpeedMath.Apply(0.6f, 1f, 2f, 1.5f), 1.5f), "cap below floor: the cap wins");
        Check(Near(MovementSpeedControl.SpeedMath.Apply(1f, 500f, 0, 0), 20f), "multiplier clamped to x20");
        Check(Near(MovementSpeedControl.SpeedMath.Apply(1f, 0f, 0, 0), 0.1f), "multiplier clamped to x0.1");
        Check(float.IsNaN(MovementSpeedControl.SpeedMath.Apply(float.NaN, 2f, 0, 0)), "NaN from the game passes through");
        Check(MovementSpeedControl.SpeedMath.IsNeutral(1f, 0, 0) && !MovementSpeedControl.SpeedMath.IsNeutral(1f, 0, 1.5f), "neutral settings detected");
        Check(MovementSpeedControl.SpeedMath.Pct(1.5f) == "150%", "percent text");
    }

    static void LogicEnemy()
    {
        const string M = "EnemyHealthAndDamage";
        EnemyHealthAndDamage.Mult.ScaleEnemy(10f, 10f, 3f, out float hp, out float max);
        Check(Near(max, Wrong(M, 30f)) && Near(hp, 30f), "x3 health: 10/10 -> 30/30");
        EnemyHealthAndDamage.Mult.ScaleEnemy(10f, 10f, 0.01f, out hp, out max);
        Check(Near(max, 1f) && Near(hp, 1f), "x0.01 never spawns a dead enemy (at least 1 health)");
        EnemyHealthAndDamage.Mult.ScaleEnemy(5f, 10f, 2f, out hp, out max);
        Check(Near(max, 20f) && Near(hp, 10f), "partial health scales with max");
        EnemyHealthAndDamage.Mult.ScaleEnemy(10f, 10f, 1f, out hp, out max);
        Check(Near(max, 10f) && Near(hp, 10f), "x1 leaves health alone");
        Check(Near(EnemyHealthAndDamage.Mult.ScaleDamage(8f, 0.5f), 4f), "x0.5 damage: 8 -> 4");
        Check(Near(EnemyHealthAndDamage.Mult.ScaleDamage(8f, 0f), 0f), "x0 damage = no damage");
        Check(Near(EnemyHealthAndDamage.Mult.ScaleDamage(-3f, 2f), -3f), "negative amounts (heals) untouched");
        Check(Near(EnemyHealthAndDamage.Mult.Health(1000f), 100f) && Near(EnemyHealthAndDamage.Mult.Damage(-1f), 0f), "ranges clamped (health x100, damage x0)");
        Check(Near(EnemyHealthAndDamage.Mult.Health(float.NaN), 1f), "NaN setting falls back to x1");
    }

    static void LogicClock()
    {
        const string M = "TimeStopAndSkipMinute";
        Check(Near(TimeStopAndSkipMinute.Clock.SkipTarget(0f), Wrong(M, 59f)), "00:00 -> clock 00:59 (next tick = 01:00)");
        Check(Near(TimeStopAndSkipMinute.Clock.SkipTarget(125f), 179f), "02:05 -> 02:59");
        Check(Near(TimeStopAndSkipMinute.Clock.SkipTarget(179f), 239f), "already 02:59 -> 03:59 (a second press skips a full minute)");
        Check(Near(TimeStopAndSkipMinute.Clock.SkipTarget(179.6f), 239f), "fractions use whole seconds");
        Check(TimeStopAndSkipMinute.Clock.Minute(239f) == 3 && TimeStopAndSkipMinute.Clock.Minute(240f) == 4, "minute from seconds");
        Check(TimeStopAndSkipMinute.Clock.Mmss(1805f) == "30:05", "clock text");
        float t = TimeStopAndSkipMinute.Clock.SkipTarget(600f) + 1f;
        Check(t % 60f == 0f, "the game's own tick then lands exactly on a whole minute (CheckMinute fires)");
    }

    static void LogicAuto()
    {
        const string M = "AutoLevelUp";
        var offers = new List<AutoLevelUp.Offer> { new("HOLYBOOK", "King Bible"), new("WHIP", "Whip"), new("POWER", "Spinach"), new("MAGIC_MISSILE", "Magic Wand") };
        var rng = new Random(1);
        int i = AutoLevelUp.Chooser.Pick(offers, AutoLevelUp.Chooser.ParseList("Garlic, Spinach, Whip"), new(), AutoLevelUp.Fallback.Random, rng, out string why);
        Check(i == (_break == M ? 1 : 2) && why == "priority #2", $"priority order: Spinach before Whip (got {i}, {why})");
        i = AutoLevelUp.Chooser.Pick(offers, AutoLevelUp.Chooser.ParseList("magic_missile"), new(), AutoLevelUp.Fallback.Random, rng, out why);
        Check(i == 3, "internal names work (MAGIC_MISSILE = Magic Wand)");
        i = AutoLevelUp.Chooser.Pick(offers, AutoLevelUp.Chooser.ParseList("bible"), new(), AutoLevelUp.Fallback.Random, rng, out why);
        Check(i == 0 && why.Contains("partial"), "partial name (4+ letters) matches King Bible");
        i = AutoLevelUp.Chooser.Pick(offers, AutoLevelUp.Chooser.ParseList("Garlic"), new(), AutoLevelUp.Fallback.ShowScreen, rng, out why);
        Check(i == -1, "nothing from the list + ShowScreen = the level-up screen opens");
        i = AutoLevelUp.Chooser.Pick(offers, AutoLevelUp.Chooser.ParseList(""), AutoLevelUp.Chooser.ParseList("King Bible, Whip"), AutoLevelUp.Fallback.First, rng, out why);
        Check(i == 2, "avoid list skipped, First takes the first allowed (Spinach)");
        i = AutoLevelUp.Chooser.Pick(offers, AutoLevelUp.Chooser.ParseList("Whip"), AutoLevelUp.Chooser.ParseList("Whip, King Bible, Spinach, Magic Wand"), AutoLevelUp.Fallback.Random, rng, out why);
        Check(i == -1 && why.Contains("avoid"), "everything avoided = screen, even if it is on the priority list");
        var seen = new HashSet<int>();
        for (int k = 0; k < 200; k++) seen.Add(AutoLevelUp.Chooser.Pick(offers, new(), AutoLevelUp.Chooser.ParseList("Whip"), AutoLevelUp.Fallback.Random, rng, out why));
        Check(!seen.Contains(1) && seen.Count == 3, "random fallback never takes an avoided offer and covers the rest");
        Check(AutoLevelUp.Chooser.ParseFallback("ShowScreen") == AutoLevelUp.Fallback.ShowScreen && AutoLevelUp.Chooser.ParseFallback("junk") == AutoLevelUp.Fallback.Random, "fallback parsing (unknown = Random)");
        Check(AutoLevelUp.Chooser.ParseList("Whip, whip ; Hollow Heart").Count == 2, "list parsing drops duplicates");
        Check(AutoLevelUp.Chooser.Pick(new List<AutoLevelUp.Offer>(), new(), new(), AutoLevelUp.Fallback.Random, rng, out why) == -1, "no offers = screen");
    }

    static void LogicArcanas()
    {
        const string M = "MoreStartingArcanas";
        Check(MoreStartingArcanas.Picks.Extra(3, 10) == (int)Wrong(M, 2), "3 wanted, 10 unlocked = 2 extra");
        Check(MoreStartingArcanas.Picks.Extra(3, 2) == 1, "only 2 unlocked = 1 extra");
        Check(MoreStartingArcanas.Picks.Extra(3, 1) == 0, "1 unlocked = no extra");
        Check(MoreStartingArcanas.Picks.Extra(1, 10) == 0, "1 wanted = the game's normal pick only");
        Check(MoreStartingArcanas.Picks.Extra(99, -1) == 29, "unknown unlock count: capped at 30 total");
        Check(MoreStartingArcanas.Picks.Limit(3, 2, true) == 5 && MoreStartingArcanas.Picks.Limit(3, 2, false) == 3, "limit raised only when asked");
    }

    static void LogicRebind()
    {
        const string M = "RebindKeys";
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "W", "A", "S", "D", "Z", "Q", "E", "F", "J", "Space", "Return", "Alpha1", "Keypad5", "UpArrow", "Escape" };
        string Res(string n) { string a = RebindKeys.Remap.Alias(n); return known.FirstOrDefault(k => string.Equals(k, a, StringComparison.OrdinalIgnoreCase)); }
        var bad = new List<string>();
        var map = RebindKeys.Remap.Combine("AZERTY", "", Res, bad);
        Check(map.Count == 2 && map["W"] == "Z" && map["A"] == "Q", "AZERTY preset = W->Z, A->Q");
        map = RebindKeys.Remap.Combine("AZERTY", "A=J, Space -> E, 1=Num5, Foo=Z", Res, bad);
        Check(map["A"] == "J" && map["Space"] == "E" && map["Alpha1"] == "Keypad5" && bad.Count == 1 && bad[0] == "Foo=Z", "custom list wins over the preset, aliases, bad names reported");

        // A snapshot like the game's map: W/S on vertical (up = pole 0, down = pole 1), A/D horizontal, arrows, Space submit.
        var snap = new List<RebindKeys.Binding>
        {
            new(1, 10, 0, "W", 0), new(2, 10, 1, "S", 0), new(3, 11, 1, "A", 0), new(4, 11, 0, "D", 0),
            new(5, 10, 0, "UpArrow", 0), new(6, 20, 0, "Space", 0),
        };
        var ops = RebindKeys.Remap.Plan(snap, RebindKeys.Remap.Combine("AZERTY", "", Res, bad), keepOld: true);
        Check(ops.Count == 2 && ops.All(o => !o.Replace), "AZERTY + keep old keys = 2 keys added, nothing moved");
        ops = RebindKeys.Remap.Plan(snap, RebindKeys.Remap.Combine("ESDF", "", Res, bad), keepOld: true);
        var byKey = ops.ToDictionary(o => o.From.Key);
        Check(ops.Count == 4 && !byKey["W"].Replace && !byKey["A"].Replace && byKey["S"].Replace && byKey["D"].Replace,
              "ESDF: S and D are new keys of other actions, so they move; W and A are kept");
        Check(byKey["S"].NewKey == "D" && byKey["S"].From.ActionId == 10 && byKey["D"].NewKey == (_break == M ? "S" : "F"), "ESDF chain uses the original keys (S->D for down, D->F for right)");
        ops = RebindKeys.Remap.Plan(snap, RebindKeys.Remap.Combine("AZERTY", "", Res, bad), keepOld: false);
        Check(ops.Count == 2 && ops.All(o => o.Replace), "keep old keys off = moved");
        // second pass on an already changed snapshot (Add mode): nothing added twice
        var after = new List<RebindKeys.Binding>(snap) { new(7, 10, 0, "Z", 0), new(8, 11, 1, "Q", 0) };
        Check(RebindKeys.Remap.Plan(after, RebindKeys.Remap.Combine("AZERTY", "", Res, bad), true).Count == 0, "re-applying adds nothing twice");
        Check(RebindKeys.Remap.Fingerprint(snap) == RebindKeys.Remap.Fingerprint(snap.AsEnumerable().Reverse().ToList()), "fingerprint ignores order");
        Check(RebindKeys.Remap.Fingerprint(snap) != RebindKeys.Remap.Fingerprint(after), "fingerprint sees a changed map");
        Check(RebindKeys.Remap.Alias("up") == "UpArrow" && RebindKeys.Remap.Alias("Num7") == "Keypad7" && RebindKeys.Remap.Alias("Esc") == "Escape", "key name aliases");
    }

    // ---------------- patch targets ----------------
    sealed record Target(string Mod, string Type, string Method, string[] Args, string PatchClass, string[] PatchMethods, bool Required);

    static readonly Target[] Targets =
    {
        new("MovementSpeedControl", "VampireSurvivors.Objects.Characters.CharacterController", "PMoveSpeed", new string[0], "MovementSpeedControl.SpeedPatch", new[] { "Postfix" }, true),
        new("EnemyHealthAndDamage", "VampireSurvivors.Objects.Characters.EnemyController", "OnRecycleEnemy", new string[0], "EnemyHealthAndDamage.Patches", new[] { "AfterSpawn" }, false),
        new("EnemyHealthAndDamage", "VampireSurvivors.Objects.Characters.CharacterController", "GetDamaged", new[] { "System.Single" }, "EnemyHealthAndDamage.Patches", new[] { "BeforeDamage" }, false),
        new("EnemyHealthAndDamage", "VampireSurvivors.Objects.Characters.CharacterController", "GetDamagedByOwnWeapon", new[] { "System.Single" }, "EnemyHealthAndDamage.Patches", new[] { "OwnStart", "OwnEnd" }, false),
        new("AutoLevelUp", "VampireSurvivors.Framework.GameManager", "SwapToLevelUpScreen", null, "AutoLevelUp.LevelUpPatch", new[] { "Before" }, true),
        new("MoreStartingArcanas", "VampireSurvivors.Framework.GameManager", "PostStageInit", new string[0], "MoreStartingArcanas.Patches", new[] { "BeforeStart", "AfterStart" }, true),
        new("MoreStartingArcanas", "VampireSurvivors.Framework.GameManager", "QueueOpenArcana", null, "MoreStartingArcanas.Patches", new[] { "AfterQueue" }, true),
        new("MoreStartingArcanas", "VampireSurvivors.GameStateArcanaSelectionMain", "AddArcana", null, "MoreStartingArcanas.Patches", new[] { "AfterPick" }, false),
        new("RebindKeys", "VampireSurvivors.UI.MainMenuPage", "OnShowStart", null, "RebindKeys.MenuPatch", new[] { "After" }, false),
        new("RebindKeys", "VampireSurvivors.Objects.Characters.CharacterController", "AfterFullInitialization", null, "RebindKeys.RunPatch", new[] { "After" }, false),
    };

    static void PatchTargets()
    {
        var game = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(Refs, "interop", "VampireSurvivors.Runtime.dll"));
        var plugins = new Dictionary<string, Assembly>();
        foreach (var mod in Targets.Select(t => t.Mod).Distinct().Append("TimeStopAndSkipMinute"))
        {
            string dll = Path.Combine(Src, mod, "bin", mod + ".dll");
            if (!File.Exists(dll)) { Fail($"{mod}: {dll} not built (dotnet build {mod} -c Release first)"); continue; }
            plugins[mod] = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
            Check(true, $"{mod}: plugin DLL loads ({new FileInfo(dll).Length} bytes)");
        }
        foreach (var t0 in Targets)
        {
            var t = _break == t0.Mod && t0 == Targets.First(x => x.Mod == t0.Mod) ? t0 with { Method = t0.Method + "_Renamed" } : t0;
            string where = $"{t.Mod}: {ShortType(t.Type)}.{t.Method}";
            Type type = game.GetType(t.Type);
            if (type == null) { Fail($"{where} - class not found"); continue; }
            var cands = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                            .Where(m => m.Name == t.Method && (t.Args == null || m.GetParameters().Select(p => p.ParameterType.FullName).SequenceEqual(t.Args))).ToList();
            if (cands.Count != 1) { Fail($"{where} - {(cands.Count == 0 ? "not found (game update renamed it?)" : cands.Count + " overloads, ambiguous")}"); continue; }
            var target = cands[0];
            int callers = CallerCount(target);
            Check(true, $"{where}({string.Join(", ", target.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))}) exists, declared on the class{(callers >= 0 ? $", interop CallerCount {callers}" : "")}" +
                        (callers == 0 ? " (0 = only reached through the virtual-method table; Harmony's native detour still catches those calls)" : ""));
            if (!plugins.TryGetValue(t.Mod, out var asm)) continue;
            Type pc = asm.GetType(t.PatchClass);
            if (pc == null) { Fail($"{where} - patch class {t.PatchClass} missing in the plugin"); continue; }
            foreach (var pmName in t.PatchMethods)
            {
                var pm = pc.GetMethod(pmName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (pm == null) { Fail($"{where} - patch method {pmName} missing"); continue; }
                Check(Binds(pm, target, out string why), $"{where} <- {pc.Name}.{pmName}: parameters bind {why}");
            }
        }

        // Time Stop has no patches: it writes these game members directly (field-backed interop properties / methods).
        foreach (var (typeName, member) in new[] {
                     ("VampireSurvivors.Framework.GameManager", "_canRunTickerTimer"), ("VampireSurvivors.Framework.GameManager", "_SurvivedSeconds_k__BackingField"),
                     ("VampireSurvivors.Framework.GameManager", "SurvivedSeconds"), ("VampireSurvivors.Framework.GameManager", "MainPlayers"),
                     ("VampireSurvivors.Objects.Characters.CharacterController", "SetHealthToMax"), ("VampireSurvivors.Objects.Stage", "CurrentMinute") })
        {
            string m = _break == "TimeStopAndSkipMinute" && member == "_canRunTickerTimer" ? member + "_Renamed" : member;
            var ty = game.GetType(typeName);
            bool found = ty != null && ty.GetMember(m, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Length > 0;
            Check(found, $"TimeStopAndSkipMinute: {ShortType(typeName)}.{m} exists");
        }

        // Movement Speed Control also patches every override of PMoveSpeed (found by reflection, like the plugin does).
        var cc = game.GetType("VampireSurvivors.Objects.Characters.CharacterController");
        Type[] all;
        try { all = game.GetTypes(); } catch (ReflectionTypeLoadException e) { all = e.Types.Where(x => x != null).ToArray(); }
        var overrides = all.Where(x => x != cc && cc.IsAssignableFrom(x) &&
                                       x.GetMethod("PMoveSpeed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null) != null).ToList();
        Check(overrides.Count == 4, $"MovementSpeedControl: PMoveSpeed overrides found by reflection: {overrides.Count} ({string.Join(", ", overrides.Select(o => o.Name))}); the disassembly shows 4 that skip the base");
    }

    static string ShortType(string full) => full.Substring(full.LastIndexOf('.') + 1);

    static int CallerCount(MethodInfo m)
    {
        foreach (var a in m.CustomAttributes)
            if (a.AttributeType.Name == "CallerCountAttribute" && a.ConstructorArguments.Count == 1 && a.ConstructorArguments[0].Value is int n) return n;
        return -1;
    }

    // Harmony binds patch parameters by name: __instance / __result / __runOriginal / __state, or the target's own names.
    static bool Binds(MethodInfo patch, MethodInfo target, out string why)
    {
        var tps = target.GetParameters();
        var problems = new List<string>();
        var used = new List<string>();
        foreach (var p in patch.GetParameters())
        {
            Type pt = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
            switch (p.Name)
            {
                case "__instance":
                    if (target.IsStatic) problems.Add("__instance on a static method");
                    else if (!pt.IsAssignableFrom(target.DeclaringType)) problems.Add($"__instance type {pt.Name} vs {target.DeclaringType.Name}");
                    used.Add("__instance"); continue;
                case "__result":
                    if (pt.FullName != target.ReturnType.FullName) problems.Add($"__result type {pt.Name} vs {target.ReturnType.Name}");
                    used.Add("__result"); continue;
                case "__runOriginal":
                    if (pt != typeof(bool)) problems.Add("__runOriginal must be bool");
                    used.Add("__runOriginal"); continue;
                case "__state": used.Add("__state"); continue;
            }
            var tp = tps.FirstOrDefault(x => x.Name == p.Name);
            if (tp == null) { problems.Add($"'{p.Name}' is not a parameter of the game method"); continue; }
            Type tt = tp.ParameterType.IsByRef ? tp.ParameterType.GetElementType() : tp.ParameterType;
            if (pt.FullName != tt.FullName) problems.Add($"'{p.Name}' type {pt.Name} vs {tt.Name}");
            used.Add(p.Name);
        }
        why = problems.Count == 0 ? (used.Count == 0 ? "(no parameters)" : "(" + string.Join(", ", used) + ")") : "- " + string.Join("; ", problems);
        return problems.Count == 0;
    }
}
