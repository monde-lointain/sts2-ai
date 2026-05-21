using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Sts2Headless.UpstreamCommentAnalyzer;

/// <summary>
/// STS2_UPSTREAM_001 — warn when a public/internal method under
/// <c>Sts2Headless.Domain.Content.{Cards,Monsters,Powers,Relics,Encounters}</c>
/// lacks an <c>upstream-source:</c> comment token.
///
/// <para>Severity: Warning (Phase 3a; ratchet to Error after 2 wave cycles green
/// per ADR-024 promotion-window precedent).</para>
///
/// <para>Exclusion predicate (H10):
/// <list type="bullet">
///   <item>Skips <see cref="MethodKind.PropertyGet"/>, <see cref="MethodKind.PropertySet"/>,
///         <see cref="MethodKind.EventAdd"/>, <see cref="MethodKind.EventRemove"/>.</item>
///   <item>Skips <see cref="IMethodSymbol.IsImplicitlyDeclared"/> — synthesized record
///         ctors, compiler-generated equals/hashcode, closure classes.</item>
/// </list></para>
///
/// <para>A method PASSES if its XML doc comment OR any trivia comment in the
/// method's syntax (leading/trailing on the declaration or body) contains the
/// literal token <c>upstream-source:</c> (case-sensitive).</para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UpstreamCommentAnalyzer : DiagnosticAnalyzer
{
    internal const string DiagnosticId = "STS2_UPSTREAM_001";
    private const string UpstreamToken = "upstream-source:";

    private static readonly string[] InScopeNamespaceSuffixes =
    {
        ".Domain.Content.Cards",
        ".Domain.Content.Monsters",
        ".Domain.Content.Powers",
        ".Domain.Content.Relics",
        ".Domain.Content.Encounters",
    };

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Missing upstream-source comment",
        messageFormat: "Method '{0}' in {1} lacks an 'upstream-source:' comment. "
            + "Add '// upstream-source: <path>' or a Q1-only exemption comment. "
            + "Warn-only Phase 3a per ADR-024.",
        category: "Sts2UpstreamDrift",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Public/internal methods under Domain.Content namespaces that mirror "
            + "upstream types must carry an 'upstream-source:' comment token so drift "
            + "can be localised during Phase-1.5 encounter ports."
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext ctx)
    {
        var methodDecl = (MethodDeclarationSyntax)ctx.Node;
        var cancellationToken = ctx.CancellationToken;

        // Semantic model — get the method symbol.
        IMethodSymbol? method = ctx.SemanticModel.GetDeclaredSymbol(methodDecl, cancellationToken);
        if (method is null)
            return;

        // H10 exclusion predicate — skip non-user-authored methods.
        if (IsExcluded(method))
            return;

        // Namespace scope check — only fire in the 5 in-scope Content sub-namespaces.
        if (!IsInScope(method))
            return;

        // Accessibility check — public or internal only.
        if (method.DeclaredAccessibility != Accessibility.Public
            && method.DeclaredAccessibility != Accessibility.Internal)
            return;

        // Comment presence check.
        if (HasUpstreamComment(methodDecl, method, cancellationToken))
            return;

        // Emit STS2_UPSTREAM_001.
        var diagnostic = Diagnostic.Create(
            Rule,
            methodDecl.Identifier.GetLocation(),
            method.Name,
            method.ContainingType.ToDisplayString()
        );
        ctx.ReportDiagnostic(diagnostic);
    }

    /// <summary>H10: returns true for methods the analyzer should NOT examine.</summary>
    private static bool IsExcluded(IMethodSymbol method)
    {
        // Skip synthesized members (record ctors, equals, hashcode, closures).
        if (method.IsImplicitlyDeclared)
            return true;

        // Skip property accessors and event accessors.
        return method.MethodKind switch
        {
            MethodKind.PropertyGet => true,
            MethodKind.PropertySet => true,
            MethodKind.EventAdd => true,
            MethodKind.EventRemove => true,
            _ => false,
        };
    }

    /// <summary>Returns true if the containing type is in one of the 5 in-scope namespaces.</summary>
    private static bool IsInScope(IMethodSymbol method)
    {
        INamedTypeSymbol? type = method.ContainingType;
        if (type is null)
            return false;

        // Walk up to the outermost containing type (handles nested types).
        while (type.ContainingType != null)
            type = type.ContainingType;

        string? ns = type.ContainingNamespace?.ToDisplayString();
        if (ns is null)
            return false;

        foreach (string suffix in InScopeNamespaceSuffixes)
        {
            if (ns.EndsWith(suffix, System.StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true if the method declaration has an <c>upstream-source:</c> token
    /// in its XML doc comment OR in any comment trivia on the declaration node tree.
    /// </summary>
    private static bool HasUpstreamComment(
        MethodDeclarationSyntax methodDecl,
        IMethodSymbol method,
        CancellationToken cancellationToken
    )
    {
        // 1. XML doc comment via structured trivia.
        foreach (var trivia in methodDecl.GetLeadingTrivia())
        {
            if (
                trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)
            )
            {
                if (trivia.ToString().Contains(UpstreamToken))
                    return true;
            }
        }

        // 2. Line/block comments anywhere in the declaration leading trivia.
        foreach (var trivia in methodDecl.GetLeadingTrivia())
        {
            if (
                trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
            )
            {
                if (trivia.ToString().Contains(UpstreamToken))
                    return true;
            }
        }

        // 3. Comments inside the method body (all descendant trivia).
        BlockSyntax? body = methodDecl.Body;
        if (body != null)
        {
            foreach (var trivia in body.DescendantTrivia())
            {
                if (
                    trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                    || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
                    || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                    || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)
                )
                {
                    if (trivia.ToString().Contains(UpstreamToken))
                        return true;
                }
            }
        }

        // 4. Expression-bodied method (arrow body) — trivia on the expression.
        ArrowExpressionClauseSyntax? arrowBody = methodDecl.ExpressionBody;
        if (arrowBody != null)
        {
            foreach (var trivia in arrowBody.DescendantTrivia())
            {
                if (
                    trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                    || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
                )
                {
                    if (trivia.ToString().Contains(UpstreamToken))
                        return true;
                }
            }
        }

        return false;
    }
}
