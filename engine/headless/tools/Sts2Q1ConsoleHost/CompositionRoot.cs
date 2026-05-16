using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace Sts2Q1ConsoleHost;

/// <summary>
/// <para>
/// Foundational composition root for the Phase-1.5 console host. α scope:
/// </para>
/// <list type="number">
///   <item>Load upstream <c>sts2.dll</c> via <see cref="Assembly.LoadFile"/>
///         on an isolated <see cref="AssemblyLoadContext"/>.</item>
///   <item>Hook a Resolving handler that probes the directory containing
///         <c>sts2.dll</c> for runtime dependencies (<c>GodotSharp.dll</c>,
///         <c>0Harmony.dll</c>, <c>JetBrains.Annotations.dll</c>, etc.).</item>
///   <item>Reflect the <c>CombatManager</c> + <c>Player</c> types and assert
///         they exist.</item>
///   <item>Emit an <c>upstream_bound</c> sentinel JSON to stdout so the
///         smoke test + downstream sub-streams can confirm the load
///         succeeded.</item>
/// </list>
///
/// <para>
/// <b>Out of α:</b> singleton stub bodies (β), <see cref="ISceneTreeSingletons"/>
/// concrete implementations (β), the <c>Pinned&lt;TStub&gt;</c> harness (γ),
/// and the per-step probe driver that consumes the resolved types (δ).
/// </para>
///
/// <para>
/// <b>Determinism:</b> on the load path we only touch the local filesystem
/// + the supplied seed/encounter strings. No <c>DateTime.Now</c>, no
/// network I/O, no thread spawns (Q1-ADR-008 single-threaded discipline).
/// </para>
/// </summary>
public static class CompositionRoot
{
    /// <summary>
    /// Reflectively bind to upstream <c>sts2.dll</c> and emit the
    /// <c>upstream_bound</c> sentinel. The returned <see cref="UpstreamBinding"/>
    /// is the handoff surface for P-1.5-1.γ + P-1.5-1.δ.
    /// </summary>
    public static UpstreamBinding BindUpstream(CliArgs cli, TextWriter stdout)
    {
        ArgumentNullException.ThrowIfNull(cli);
        ArgumentNullException.ThrowIfNull(stdout);

        string dllAbsolutePath = Path.GetFullPath(cli.Sts2DllPath);
        string steamDir =
            Path.GetDirectoryName(dllAbsolutePath)
            ?? throw new CompositionRootException(
                $"--sts2-dll has no parent directory: {cli.Sts2DllPath}"
            );

        AssemblyLoadContext loadContext = new(
            $"sts2-q1-{cli.Seed}-{cli.EncounterId}",
            isCollectible: false
        );
        loadContext.Resolving += (ctx, name) => ResolveFromSteamDir(ctx, name, steamDir);

        Assembly sts2;
        try
        {
            sts2 = loadContext.LoadFromAssemblyPath(dllAbsolutePath);
        }
        catch (Exception ex)
        {
            throw new CompositionRootException(
                $"Assembly.LoadFromAssemblyPath('{dllAbsolutePath}') failed: {ex.GetType().Name}: {ex.Message}",
                ex
            );
        }

        Type combatManagerType = ResolveType(sts2, "MegaCrit.Sts2.Core.Combat.CombatManager");
        Type playerType = ResolveType(sts2, "MegaCrit.Sts2.Core.Entities.Players.Player");

        EmitUpstreamBound(stdout, dllAbsolutePath, sts2, combatManagerType, playerType);

        return new UpstreamBinding(
            Sts2Assembly: sts2,
            CombatManagerType: combatManagerType,
            PlayerType: playerType,
            SteamDir: steamDir
        );
    }

    /// <summary>
    /// AssemblyLoadContext resolving handler. Probes the Steam install dir
    /// for the standard upstream runtime dependencies. We list the known
    /// names defensively but fall back to a generic
    /// <c>&lt;assembly-name&gt;.dll</c> probe so MonoMod / Sentry / SmartFormat
    /// also resolve.
    /// </summary>
    private static Assembly? ResolveFromSteamDir(
        AssemblyLoadContext context,
        AssemblyName name,
        string steamDir
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(name);
        if (name.Name is null)
            return null;
        string candidate = Path.Combine(steamDir, name.Name + ".dll");
        if (File.Exists(candidate))
        {
            try
            {
                return context.LoadFromAssemblyPath(candidate);
            }
            catch (Exception)
            {
                // Fall through and let the runtime continue probing.
                return null;
            }
        }
        return null;
    }

    private static Type ResolveType(Assembly sts2, string fullName)
    {
        Type? t = sts2.GetType(fullName, throwOnError: false);
        if (t is null)
        {
            throw new CompositionRootException(
                $"upstream type '{fullName}' not found in sts2.dll at '{sts2.Location}'."
            );
        }
        return t;
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private static void EmitUpstreamBound(
        TextWriter stdout,
        string sts2DllPath,
        Assembly sts2,
        Type combatManagerType,
        Type playerType
    )
    {
        var doc = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["event"] = "upstream_bound",
            ["sts2_dll"] = sts2DllPath,
            ["assembly_name"] = sts2.GetName().Name,
            ["assembly_version"] = sts2.GetName().Version?.ToString(),
            ["combat_manager_type"] = combatManagerType.FullName,
            ["player_type"] = playerType.FullName,
        };
        stdout.WriteLine(JsonSerializer.Serialize(doc, JsonOpts));
        stdout.Flush();
    }
}

/// <summary>
/// Result of the α composition root. P-1.5-1.γ wraps the resolved upstream
/// surface with <c>Pinned&lt;TStub&gt;</c>; P-1.5-1.δ drives the per-step
/// probe through it.
/// </summary>
public sealed record UpstreamBinding(
    Assembly Sts2Assembly,
    Type CombatManagerType,
    Type PlayerType,
    string SteamDir
);

/// <summary>
/// Thrown when the composition root fails to bind upstream. Caller (the
/// <see cref="Program"/> entrypoint) translates this into exit code
/// <see cref="Program.ExitLoad"/>.
/// </summary>
public sealed class CompositionRootException : Exception
{
    public CompositionRootException() { }

    public CompositionRootException(string message)
        : base(message) { }

    public CompositionRootException(string message, Exception inner)
        : base(message, inner) { }
}
