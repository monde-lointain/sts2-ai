using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;
using UpstreamAnalyzer = Sts2Headless.UpstreamCommentAnalyzer.UpstreamCommentAnalyzer;

namespace Sts2Headless.Tests.UpstreamDriftGates;

/// <summary>
/// Gate: verifies STS2_UPSTREAM_001 Roslyn analyzer fires correctly.
///
/// <para>Red-team target: <c>CalcifiedCultist</c> (Phase-1 monster,
/// <c>Sts2Headless.Domain.Content.Monsters</c> namespace). FatGremlin.cs
/// excluded per brief (may not exist post-wave-28). CalcifiedCultist is
/// present and has no <c>upstream-source:</c> comment on its public methods.</para>
///
/// <para>Four assertions:
/// <list type="number">
///   <item>Fires on in-scope method lacking <c>upstream-source:</c> comment.</item>
///   <item>Does NOT fire when leading comment with token is present.</item>
///   <item>Does NOT fire when body comment with token is present.</item>
///   <item>Does NOT fire on an out-of-scope namespace.</item>
/// </list></para>
/// </summary>
public sealed class UpstreamCommentAnalyzerTests
{
    private const string STS2Upstream001 = "STS2_UPSTREAM_001";

    // Minimal in-scope source — Monsters namespace matches H10 scope check.
    // Models CalcifiedCultist's public ApplyIncantation method without comment.
    private const string NoCommentSource = """
        namespace Sts2Headless.Domain.Content.Monsters
        {
            public class CalcifiedCultistStub
            {
                public void ApplyIncantation()
                {
                }
            }
        }
        """;

    // Same method with upstream-source: in leading trivia — must suppress warning.
    private const string WithLeadingCommentSource = """
        namespace Sts2Headless.Domain.Content.Monsters
        {
            public class CalcifiedCultistWithLeadingComment
            {
                // upstream-source: Core/Models/Monsters/CalcifiedCultist.cs
                public void ApplyIncantation()
                {
                }
            }
        }
        """;

    // Same method with upstream-source: inside body — must suppress warning.
    private const string WithBodyCommentSource = """
        namespace Sts2Headless.Domain.Content.Monsters
        {
            public class CalcifiedCultistWithBodyComment
            {
                public void ApplyIncantation()
                {
                    // upstream-source: Core/Models/Monsters/CalcifiedCultist.cs
                }
            }
        }
        """;

    // Out-of-scope namespace (Domain.Combat, not Domain.Content.*) — must not fire.
    private const string OutOfScopeSource = """
        namespace Sts2Headless.Domain.Combat
        {
            public class SomeCombatHelper
            {
                public void DoSomething()
                {
                }
            }
        }
        """;

    [Fact]
    public async Task Analyzer_FiresOn_InScopeMethodWithoutComment()
    {
        IReadOnlyList<Diagnostic> diagnostics = await RunAnalyzerAsync(NoCommentSource);

        Diagnostic? d = diagnostics.FirstOrDefault(x =>
            x.Id == STS2Upstream001 && x.GetMessage().Contains("ApplyIncantation")
        );
        Assert.True(
            d is not null,
            $"Expected STS2_UPSTREAM_001 on 'ApplyIncantation' in "
                + $"Sts2Headless.Domain.Content.Monsters, but got: "
                + $"[{string.Join("; ", diagnostics.Select(x => x.ToString()))}]"
        );
        Assert.Equal(DiagnosticSeverity.Warning, d!.Severity);
    }

    [Fact]
    public async Task Analyzer_DoesNotFire_WhenLeadingCommentPresent()
    {
        IReadOnlyList<Diagnostic> diagnostics = await RunAnalyzerAsync(WithLeadingCommentSource);

        bool hasSts2Warning = diagnostics.Any(x => x.Id == STS2Upstream001);
        Assert.False(
            hasSts2Warning,
            $"Expected NO STS2_UPSTREAM_001 when leading upstream-source: comment present, "
                + $"but got: [{string.Join("; ", diagnostics.Where(x => x.Id == STS2Upstream001).Select(x => x.ToString()))}]"
        );
    }

    [Fact]
    public async Task Analyzer_DoesNotFire_WhenBodyCommentPresent()
    {
        IReadOnlyList<Diagnostic> diagnostics = await RunAnalyzerAsync(WithBodyCommentSource);

        bool hasSts2Warning = diagnostics.Any(x => x.Id == STS2Upstream001);
        Assert.False(
            hasSts2Warning,
            $"Expected NO STS2_UPSTREAM_001 when body upstream-source: comment present, "
                + $"but got: [{string.Join("; ", diagnostics.Where(x => x.Id == STS2Upstream001).Select(x => x.ToString()))}]"
        );
    }

    [Fact]
    public async Task Analyzer_DoesNotFire_OnOutOfScopeNamespace()
    {
        IReadOnlyList<Diagnostic> diagnostics = await RunAnalyzerAsync(OutOfScopeSource);

        bool hasSts2Warning = diagnostics.Any(x => x.Id == STS2Upstream001);
        Assert.False(
            hasSts2Warning,
            $"Expected NO STS2_UPSTREAM_001 on out-of-scope namespace (Domain.Combat), "
                + $"but got: [{string.Join("; ", diagnostics.Where(x => x.Id == STS2Upstream001).Select(x => x.ToString()))}]"
        );
    }

    /// <summary>
    /// Creates an in-memory Roslyn compilation from <paramref name="source"/>,
    /// attaches <see cref="UpstreamAnalyzer"/>, and returns all Warning+ diagnostics.
    /// </summary>
    private static async Task<IReadOnlyList<Diagnostic>> RunAnalyzerAsync(string source)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source);

        MetadataReference corlibRef = MetadataReference.CreateFromFile(
            typeof(object).Assembly.Location
        );

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "AnalyzerTestAssembly",
            syntaxTrees: new[] { tree },
            references: new[] { corlibRef },
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var analyzer = new UpstreamAnalyzer();
        CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer)
        );

        ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAllDiagnosticsAsync(
            CancellationToken.None
        );

        // Return only warning-or-worse diagnostics to exclude AD0001 meta-noise.
        return diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).ToList();
    }
}
