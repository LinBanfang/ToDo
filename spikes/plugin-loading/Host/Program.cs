using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Windows;
using System.Windows.Controls;
using Spike.Contract;

namespace Spike.Host;

static class Program
{
    private static int _failures;
    private static string? _only;

    [STAThread]
    static int Main(string[] args)
    {
        _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        var sourceDir = Path.GetFullPath(args[0]);
        _only = args.Length > 1 ? args[1] : null;
        Console.WriteLine($"plugin publish dir = {sourceDir}");
        Console.WriteLine($"only = {_only ?? "all"}");

        Run("U1", () => TestU1_ContractSingleLoad(sourceDir));
        Run("U5", () => TestU5_MinimalUnload(sourceDir));
        Run("U2", () => TestU2_UnloadWithResolverAndInstance(sourceDir));
        Run("U3a", () => TestU3a_CompiledXamlViaAlc(sourceDir));
        Run("U3d", () => TestU3d_CompiledXamlViaAlcWithResolveHook(sourceDir));
        Run("U3b", () => TestU3b_CodeOnlyViaAlc(sourceDir));
        Run("U3c", () => TestU3c_DefaultContextCompiledXaml(sourceDir));
        Run("U4", () => TestU4_UnloadAfterWpfInstantiation(sourceDir));
        Run("U6", () => TestU6_TwoCopiesAmbiguity(sourceDir));

        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "ALL PASS" : $"{_failures} FAILURE(S)");
        return _failures == 0 ? 0 : 1;
    }

    static void Run(string name, Action body)
    {
        if (_only != null && !string.Equals(_only, name, StringComparison.OrdinalIgnoreCase)) return;
        body();
    }

    static void TestU1_ContractSingleLoad(string sourceDir)
    {
        var dir = Stage(sourceDir, "u1");
        Check("U1 contract single-load (plugin is IPlugin)", () =>
        {
            var (asm, _) = LoadViaResolver(dir, null);
            var inst = (IPlugin)Activator.CreateInstance(asm.GetType("Spike.Plugin.SimplePlugin", throwOnError: true)!)!;
            Console.WriteLine($"  -> plugin is IPlugin == {inst is IPlugin} (id={inst.Id})");
        });
    }

    static void TestU5_MinimalUnload(string sourceDir)
    {
        var dir = Stage(sourceDir, "u5");
        var (asmAlive, ctxAlive, deletable) = MeasureUnload(dir, null, useResolver: false, makeView: false);
        Console.WriteLine("--- U5 minimal ALC (no resolver, no instance) ---");
        Console.WriteLine($"  asmAlive={asmAlive} ctxAlive={ctxAlive} dirDeletable={deletable}");
    }

    static void TestU2_UnloadWithResolverAndInstance(string sourceDir)
    {
        var dir = Stage(sourceDir, "u2");
        var (asmAlive, ctxAlive, deletable) = MeasureUnload(dir, "Spike.Plugin.SimplePlugin", useResolver: true, makeView: false);
        Console.WriteLine("--- U2 resolver + SimplePlugin instance ---");
        Console.WriteLine($"  asmAlive={asmAlive} ctxAlive={ctxAlive} dirDeletable={deletable}");
        if (!deletable) { _failures++; Console.WriteLine("  FAIL: directory still locked"); }
        else Console.WriteLine("  PASS");
    }

    static void TestU3a_CompiledXamlViaAlc(string sourceDir)
    {
        var dir = Stage(sourceDir, "u3a");
        Console.WriteLine("--- U3a compiled-XAML UserControl via collectible ALC (no hook) ---");
        try
        {
            var (asm, _) = LoadViaResolver(dir, null);
            var view = CreateWidgetView(asm);
            Console.WriteLine($"  -> SUCCESS: {view?.GetType().FullName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  -> FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    static void TestU3d_CompiledXamlViaAlcWithResolveHook(string sourceDir)
    {
        var dir = Stage(sourceDir, "u3d");
        Check("U3d compiled-XAML via ALC + AppDomain.AssemblyResolve hook", () =>
        {
            var (asm, _) = LoadViaResolver(dir, null);
            ResolveEventHandler hook = (s, e) =>
                new AssemblyName(e.Name).Name == "Plugin" ? asm : null;
            AppDomain.CurrentDomain.AssemblyResolve += hook;
            try
            {
                var view = CreateWidgetView(asm);
                Console.WriteLine($"  -> {view?.GetType().FullName} created with resolve hook");
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= hook;
            }
        });
    }

    static void TestU3b_CodeOnlyViaAlc(string sourceDir)
    {
        var dir = Stage(sourceDir, "u3b");
        Check("U3b code-only UserControl via collectible ALC", () =>
        {
            var (asm, _) = LoadViaResolver(dir, null);
            var inst = (IWidgetPlugin)Activator.CreateInstance(
                asm.GetType("Spike.Plugin.CodeWidgetPlugin", throwOnError: true)!)!;
            var view = inst.CreateView();
            if (view is not UserControl) throw new Exception($"view is {view?.GetType().FullName}");
            Console.WriteLine($"  -> {view.GetType().FullName} created from external dir");
        });
    }

    static void TestU3c_DefaultContextCompiledXaml(string sourceDir)
    {
        var dir = Stage(sourceDir, "u3c");
        Check("U3c compiled-XAML via default context (no unload)", () =>
        {
            var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(dir, "Plugin.dll"));
            var view = CreateWidgetView(asm);
            Console.WriteLine($"  -> {view?.GetType().FullName} created (pack URI resolved in default context)");
        });
    }

    static void TestU4_UnloadAfterWpfInstantiation(string sourceDir)
    {
        var dir = Stage(sourceDir, "u4");
        var (asmAlive, ctxAlive, deletable) = MeasureUnload(dir, "Spike.Plugin.CodeWidgetPlugin", useResolver: true, makeView: true);
        Console.WriteLine("--- U4 unload after instantiating a code-only WPF view ---");
        Console.WriteLine($"  asmAlive={asmAlive} ctxAlive={ctxAlive} dirDeletable={deletable}");
    }

    static void TestU6_TwoCopiesAmbiguity(string sourceDir)
    {
        var dir = Stage(sourceDir, "u6");
        // Load the SAME plugin (simple name "Plugin") into two live ALCs, simulating
        // a hot-reload window where v1 and v2 coexist.
        var (asm1, ctx1) = LoadViaResolver(dir, null);
        var (asm2, ctx2) = LoadViaResolver(dir, null);
        GC.KeepAlive(ctx1);
        GC.KeepAlive(ctx2);
        GC.KeepAlive(asm1);
        Console.WriteLine("--- U6 compiled XAML with TWO loaded 'Plugin' copies ---");
        try
        {
            var view = CreateWidgetView(asm2);
            Console.WriteLine($"  -> SUCCESS: {view?.GetType().FullName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  -> FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ─── helpers ────────────────────────────────────────────────────────

    static object? CreateWidgetView(Assembly asm)
    {
        var inst = (IWidgetPlugin)Activator.CreateInstance(
            asm.GetType("Spike.Plugin.WidgetPlugin", throwOnError: true)!)!;
        return inst.CreateView();
    }

    static (Assembly asm, PluginLoadContext ctx) LoadViaResolver(string dir, string? instantiate)
    {
        var ctx = new PluginLoadContext(dir, Path.Combine(dir, "Plugin.dll"));
        var asm = ctx.LoadFromAssemblyPath(Path.Combine(dir, "Plugin.dll"));
        if (instantiate != null)
            Activator.CreateInstance(asm.GetType(instantiate, throwOnError: true)!);
        return (asm, ctx);
    }

    /// <summary>Loads + unloads in a non-inlinable method and returns only WeakReferences,
    /// so no local can keep the assembly/ALC alive when the CALLER forces the GC.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    static (WeakReference asm, WeakReference ctx) LoadAndUnload(
        string dir, string? typeName, bool useResolver, bool makeView)
    {
        AssemblyLoadContext ctx = useResolver
            ? new PluginLoadContext(dir, Path.Combine(dir, "Plugin.dll"))
            : new MinimalAlc();
        var asm = ctx.LoadFromAssemblyPath(Path.Combine(dir, "Plugin.dll"));

        if (typeName != null)
        {
            var inst = Activator.CreateInstance(asm.GetType(typeName, throwOnError: true)!);
            if (makeView && inst is IWidgetPlugin w) _ = w.CreateView();
        }

        var wasm = new WeakReference(asm, trackResurrection: true);
        var wctx = new WeakReference(ctx, trackResurrection: true);
        ctx.Unload();
        return (wasm, wctx);
    }

    static (bool asmAlive, bool ctxAlive, bool deletable) MeasureUnload(
        string dir, string? typeName, bool useResolver, bool makeView)
    {
        var (wasm, wctx) = LoadAndUnload(dir, typeName, useResolver, makeView);
        ForceGc();
        return (wasm.IsAlive, wctx.IsAlive, TryDelete(dir));
    }

    static void Check(string label, Action body)
    {
        Console.WriteLine($"--- {label} ---");
        try { body(); Console.WriteLine("  PASS"); }
        catch (Exception ex) { _failures++; Console.WriteLine($"  FAIL: {ex.GetType().Name}: {ex.Message}"); }
    }

    static string Stage(string sourceDir, string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "spike-plugins", name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(sourceDir))
            File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
        return dir;
    }

    static bool TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); return true; }
        catch { return false; }
    }

    static void ForceGc()
    {
        for (var i = 0; i < 5; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
        }
    }
}

sealed class MinimalAlc : AssemblyLoadContext
{
    public MinimalAlc() : base(isCollectible: true) { }
    protected override Assembly? Load(AssemblyName name) => null;
}
