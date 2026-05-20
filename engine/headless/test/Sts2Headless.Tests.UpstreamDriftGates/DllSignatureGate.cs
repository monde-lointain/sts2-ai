using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Sts2Headless.Tests.UpstreamDriftGates.Helpers;
using Xunit;

namespace Sts2Headless.Tests.UpstreamDriftGates;

/// <summary>
/// A.1 drift gate: cross-checks every reflection-call target in
/// <c>UpstreamDriver.cs</c> against the live <c>sts2.dll</c>.
///
/// <para>
/// <b>Expected state on current main (274beca):</b> FAIL. The
/// <c>upstream-pin.json</c> pins v0.103.2 (buildid 22823976) but the live
/// Steam install is v0.105.1 (buildid 23156356). Some reflection targets in
/// <c>UpstreamDriver.cs</c> — notably the 15-param <c>Player</c> ctor — do
/// not resolve against the v0.105.1 DLL. That mismatch is the exact failure
/// mode that derailed Wave 3.5, and this gate is designed to catch it.
/// </para>
///
/// <para>
/// Failure message format:
/// <code>
/// DLL SIGNATURE DRIFT DETECTED (3 missing targets)
///
/// MISSING:
///   GetConstructors(MegaCrit.Sts2.Core.Entities.Players.Player).Single(len==15)
///     Expected: 1 ctor with 15 params
///     Found:    ctors with param counts [10, 12]
///
/// HASH CHECK: OK  sha256=ab571bed... (matches pin)
/// </code>
/// </para>
/// </summary>
public sealed class DllSignatureGate
{
    /// <summary>
    /// Enumerate all reflection targets in <c>UpstreamDriver.cs</c> via Roslyn
    /// AST parsing, then verify each resolves against the live sts2.dll
    /// (hash-matched to <c>upstream-pin.json:pinned_dll_sha256</c>).
    ///
    /// <para><b>EXPECTED: FAIL on current main.</b> See class docs.</para>
    /// </summary>
    [Fact]
    public void ReflectionTargets_AllResolveInLiveDll()
    {
        PinFile pin = PinFile.Load();

        string? dllPath = DllLocator.TryGetDllPath();
        if (dllPath is null)
        {
            // Steam not present — skip with a clear message rather than silently pass.
            // We use Assert.Skip so the test runner marks it SKIPPED not PASSED.
            // (xUnit 2.9+ supports Assert.Skip.)
            Assert.Fail(
                "Steam install not found; cannot run DllSignatureGate. "
                    + "Set STEAM_STS2_DIR or install Slay the Spire 2 via Steam. "
                    + "If running on a GHA runner without Steam, this gate is expected to skip — "
                    + "use [Fact(Skip=...)] variant or DRIFT_GATES_SKIP_NO_STEAM=1."
            );
        }

        // 1. Hash check — gate must fail here if hash drifts, before any reflection.
        string actualSha = DllLocator.ComputeSha256(dllPath);
        bool hashMatch = string.Equals(
            actualSha,
            pin.PinnedDllSha256,
            StringComparison.OrdinalIgnoreCase
        );

        // 2. Install AssemblyResolve hook so sts2.dll's references (GodotSharp.dll,
        //    0Harmony.dll, etc.) can be located in the Steam install dir. Without
        //    this hook, GetType("...Players.Player") returns null because the
        //    type's base/interface classes can't be loaded — the gate then
        //    misreports every type as "not found". Mirrors UpstreamDriver's
        //    ResolveFromSteamDir.
        string steamDir = Path.GetDirectoryName(dllPath) ?? "";
        AppDomain.CurrentDomain.AssemblyResolve += (object? _, ResolveEventArgs args) =>
        {
            string asmFile = new AssemblyName(args.Name).Name + ".dll";
            string candidate = Path.Combine(steamDir, asmFile);
            if (File.Exists(candidate))
            {
                try
                {
                    return Assembly.LoadFrom(candidate);
                }
                catch
                {
                    return null;
                }
            }
            return null;
        };

        // 3. Load assembly via LoadFrom so the resolve hook can locate
        //    dependencies on probing.
        Assembly sts2 = Assembly.LoadFrom(dllPath);

        // 4. Extract reflection targets from UpstreamDriver.cs via Roslyn AST
        IReadOnlyList<ReflectionCallExtractor.ReflectionTarget> targets =
            ReflectionCallExtractor.ExtractFromUpstreamDriver();

        // 5. Verify each target
        var missing = new List<string>();
        foreach (var target in targets)
        {
            string? failure = VerifyTarget(sts2, target);
            if (failure is not null)
                missing.Add(failure);
        }

        // 6. Build structured failure report
        if (!hashMatch || missing.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("DLL SIGNATURE DRIFT DETECTED");
            sb.AppendLine();

            if (!hashMatch)
            {
                sb.AppendLine("HASH MISMATCH (DLL has been updated since pin was written):");
                sb.AppendLine($"  Expected: {pin.PinnedDllSha256}");
                sb.AppendLine($"  Actual:   {actualSha}");
                sb.AppendLine($"  Pin version: {pin.PinnedVersion} (buildid {pin.PinnedBuildId})");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine($"HASH CHECK: OK  sha256={pin.PinnedDllSha256[..16]}...");
                sb.AppendLine();
            }

            if (missing.Count > 0)
            {
                sb.AppendLine($"MISSING TARGETS ({missing.Count} of {targets.Count} failed):");
                foreach (string m in missing)
                {
                    sb.AppendLine(m);
                }
                sb.AppendLine();
            }

            sb.AppendLine(
                "CONTEXT: upstream-pin.json pins v0.103.2 (22823976); "
                    + "live DLL is v0.105.1 (23156356). Bridge in progress per ADR-026. "
                    + "This FAIL is expected until Phase B completes."
            );

            Assert.Fail(sb.ToString());
        }
    }

    private static string? VerifyTarget(
        Assembly sts2,
        ReflectionCallExtractor.ReflectionTarget target
    )
    {
        Type? type = sts2.GetType(target.TypeFullName);
        if (type is null)
        {
            return $"  {target}\n    Type not found: '{target.TypeFullName}'";
        }

        return target.Kind switch
        {
            ReflectionCallExtractor.ReflectionCallKind.TypeOrThrow => null, // type found = ok

            ReflectionCallExtractor.ReflectionCallKind.GetConstructors => VerifyConstructor(
                type,
                target
            ),

            ReflectionCallExtractor.ReflectionCallKind.GetMethod => VerifyMethod(type, target),

            ReflectionCallExtractor.ReflectionCallKind.GetProperty => VerifyProperty(type, target),

            _ => null,
        };
    }

    private static string? VerifyConstructor(
        Type type,
        ReflectionCallExtractor.ReflectionTarget target
    )
    {
        ConstructorInfo[] ctors = type.GetConstructors(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        );
        int expected = target.ParamCount;
        if (expected < 0)
            return null; // no length constraint extracted

        bool found = ctors.Any(c => c.GetParameters().Length == expected);
        if (!found)
        {
            string foundCounts = string.Join(
                ", ",
                ctors.Select(c => c.GetParameters().Length).OrderBy(n => n)
            );
            return $"  {target}\n"
                + $"    Expected: 1 ctor with {expected} params\n"
                + $"    Found:    ctors with param counts [{foundCounts}]";
        }
        return null;
    }

    private static string? VerifyMethod(Type type, ReflectionCallExtractor.ReflectionTarget target)
    {
        const BindingFlags All =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static;

        MethodInfo[] methods = type.GetMethods(All)
            .Where(m => m.Name == target.MemberName)
            .ToArray();

        if (methods.Length == 0)
        {
            // Collect all method names to help diagnose renames
            string[] allNames = type.GetMethods(All)
                .Select(m => m.Name)
                .Distinct()
                .OrderBy(n => n)
                .ToArray();
            return $"  {target}\n"
                + $"    Method '{target.MemberName}' not found on {type.FullName}\n"
                + $"    Available (sample): [{string.Join(", ", allNames.Take(10))}]";
        }
        return null;
    }

    private static string? VerifyProperty(
        Type type,
        ReflectionCallExtractor.ReflectionTarget target
    )
    {
        const BindingFlags All =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static;

        PropertyInfo? prop = type.GetProperty(target.MemberName!, All);
        if (prop is null)
        {
            string[] allNames = type.GetProperties(All)
                .Select(p => p.Name)
                .OrderBy(n => n)
                .ToArray();
            return $"  {target}\n"
                + $"    Property '{target.MemberName}' not found on {type.FullName}\n"
                + $"    Available (sample): [{string.Join(", ", allNames.Take(10))}]";
        }
        return null;
    }
}
