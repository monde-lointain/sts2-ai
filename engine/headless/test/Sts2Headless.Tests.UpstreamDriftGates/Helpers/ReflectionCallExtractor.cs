using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sts2Headless.Tests.UpstreamDriftGates.Helpers;

/// <summary>
/// Parses <c>UpstreamDriver.cs</c> via the Roslyn CSharp syntax APIs and
/// enumerates every reflection-call target. The extracted targets drive
/// <see cref="DllSignatureGate"/> — if any target disappears from the live
/// sts2.dll the gate fails with a structured diff.
/// </summary>
internal static class ReflectionCallExtractor
{
    /// <summary>
    /// A single reflection call target extracted from <c>UpstreamDriver.cs</c>.
    /// </summary>
    internal sealed record ReflectionTarget(
        ReflectionCallKind Kind,
        string TypeFullName,
        string? MemberName,
        /// <summary>Parameter count for constructors; -1 = unspecified / not applicable.</summary>
        int ParamCount
    )
    {
        public override string ToString() =>
            Kind switch
            {
                ReflectionCallKind.TypeOrThrow => $"TypeOrThrow({TypeFullName})",
                ReflectionCallKind.GetConstructors =>
                    $"GetConstructors({TypeFullName}).Single(len=={ParamCount})",
                ReflectionCallKind.GetMethod => $"GetMethod({TypeFullName}.{MemberName})",
                ReflectionCallKind.GetProperty => $"GetProperty({TypeFullName}.{MemberName})",
                _ => $"{Kind}({TypeFullName}.{MemberName ?? "?"})",
            };
    }

    internal enum ReflectionCallKind
    {
        TypeOrThrow,
        GetConstructors,
        GetMethod,
        GetProperty,
    }

    /// <summary>
    /// Locates <c>UpstreamDriver.cs</c> relative to the repo root and extracts
    /// all reflection targets via Roslyn syntax-only analysis (no compilation).
    /// </summary>
    public static IReadOnlyList<ReflectionTarget> ExtractFromUpstreamDriver()
    {
        string sourcePath = LocateUpstreamDriver();
        string src = File.ReadAllText(sourcePath);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(src);
        SyntaxNode root = tree.GetRoot();

        var targets = new List<ReflectionTarget>();

        // Collect TypeOrThrow("...") calls.
        foreach (
            InvocationExpressionSyntax inv in root.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
        )
        {
            if (inv.Expression is not IdentifierNameSyntax id)
                continue;

            if (id.Identifier.Text == "TypeOrThrow")
            {
                if (TryGetStringArg(inv, 0) is string typeName)
                {
                    targets.Add(
                        new ReflectionTarget(ReflectionCallKind.TypeOrThrow, typeName, null, -1)
                    );
                }
            }
        }

        // Collect member accesses: .GetMethod("X"), .GetProperty("Y"), .GetField("Z")
        // and .GetConstructors(...).FirstOrDefault(c => c.GetParameters().Length == N)
        // or .Single(c => c.GetParameters().Length == N).
        foreach (
            InvocationExpressionSyntax inv in root.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
        )
        {
            if (inv.Expression is not MemberAccessExpressionSyntax ma)
                continue;

            string method = ma.Name.Identifier.Text;

            if (method is "GetMethod" or "GetProperty")
            {
                if (TryGetStringArg(inv, 0) is not string memberName)
                    continue;

                // Walk up to find the type: usually someLocalVar.GetMethod(...)
                // We resolve the variable name to its TypeOrThrow assignment.
                string? typeName = TryResolveReceiverTypeName(ma.Expression, root);
                if (typeName is null)
                    continue;

                var kind =
                    method == "GetMethod"
                        ? ReflectionCallKind.GetMethod
                        : ReflectionCallKind.GetProperty;
                targets.Add(new ReflectionTarget(kind, typeName, memberName, -1));
            }

            // GetConstructors(...).FirstOrDefault(c => c.GetParameters().Length == N)
            // or .Single(c => ...)
            if (method is "FirstOrDefault" or "Single")
            {
                // Is the receiver a .GetConstructors() call?
                if (
                    ma.Expression is InvocationExpressionSyntax innerInv
                    && innerInv.Expression is MemberAccessExpressionSyntax innerMa
                    && innerMa.Name.Identifier.Text == "GetConstructors"
                )
                {
                    string? typeName = TryResolveReceiverTypeName(innerMa.Expression, root);
                    if (typeName is null)
                        continue;

                    int paramCount = TryExtractParameterCountFromLambda(inv) ?? -1;
                    targets.Add(
                        new ReflectionTarget(
                            ReflectionCallKind.GetConstructors,
                            typeName,
                            null,
                            paramCount
                        )
                    );
                }
            }
        }

        return targets.GroupBy(t => t.ToString()).Select(g => g.First()).ToList();
    }

    private static string LocateUpstreamDriver()
    {
        // Walk up from test assembly to repo root, then down to the known path.
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(
                dir,
                "engine",
                "headless",
                "test",
                "determinism-probe-upstream-capture",
                "src",
                "UpstreamDriver.cs"
            );
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException(
            "Could not locate engine/headless/test/determinism-probe-upstream-capture/src/UpstreamDriver.cs"
        );
    }

    private static string? TryGetStringArg(InvocationExpressionSyntax inv, int argIndex)
    {
        if (inv.ArgumentList.Arguments.Count <= argIndex)
            return null;
        ArgumentSyntax arg = inv.ArgumentList.Arguments[argIndex];
        if (
            arg.Expression is LiteralExpressionSyntax lit
            && lit.IsKind(SyntaxKind.StringLiteralExpression)
        )
        {
            return lit.Token.ValueText;
        }
        return null;
    }

    /// <summary>
    /// Given the receiver expression of a .GetMethod/.GetProperty call, tries to
    /// find the variable it was assigned from a TypeOrThrow("...") call in the
    /// same scope.
    /// </summary>
    private static string? TryResolveReceiverTypeName(
        ExpressionSyntax receiverExpr,
        SyntaxNode root
    )
    {
        if (receiverExpr is not IdentifierNameSyntax receiverIdent)
            return null;

        string varName = receiverIdent.Identifier.Text;

        // Find all: Type <varName> = TypeOrThrow("...") or TypeOrThrow(...)
        foreach (
            VariableDeclaratorSyntax decl in root.DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
        )
        {
            if (decl.Identifier.Text != varName)
                continue;
            if (decl.Initializer?.Value is not InvocationExpressionSyntax initInv)
                continue;
            if (initInv.Expression is not IdentifierNameSyntax initId)
                continue;
            if (initId.Identifier.Text != "TypeOrThrow")
                continue;
            if (TryGetStringArg(initInv, 0) is string typeName)
                return typeName;
        }

        return null;
    }

    private static int? TryExtractParameterCountFromLambda(InvocationExpressionSyntax inv)
    {
        if (inv.ArgumentList.Arguments.Count == 0)
            return null;
        ExpressionSyntax argExpr = inv.ArgumentList.Arguments[0].Expression;
        // Expect: c => c.GetParameters().Length == N
        if (argExpr is SimpleLambdaExpressionSyntax lambda)
        {
            // Body: c.GetParameters().Length == N
            if (
                lambda.Body is BinaryExpressionSyntax bin
                && bin.IsKind(SyntaxKind.EqualsExpression)
                && bin.Right is LiteralExpressionSyntax lit
                && lit.IsKind(SyntaxKind.NumericLiteralExpression)
            )
            {
                if (int.TryParse(lit.Token.ValueText, out int n))
                    return n;
            }
        }
        return null;
    }
}
