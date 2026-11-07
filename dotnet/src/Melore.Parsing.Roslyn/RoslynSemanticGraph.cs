using Melore.Core.Models;
using Melore.Core.Ports;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

namespace Melore.Parsing.Roslyn;

/// <summary>
/// 同一ソリューションで解決できた calls と implements を取る。
/// </summary>
public sealed class RoslynSemanticGraph : ISemanticGraphExtractor
{
    private static int _msbuildRegistered;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SemanticRelation>> ExtractAsync(
        string solutionPath,
        CancellationToken cancellationToken)
    {
        EnsureMsBuild();
        using var workspace = MSBuildWorkspace.Create();
        var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var edges = new List<SemanticRelation>();
        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
            {
                continue;
            }

            edges.AddRange(FromCompilation(compilation));
        }

        return edges;
    }

    /// <summary>
    /// コンパイル結果から解決済みの辺を集める。ソースに宣言が無い相手は捨てる。
    /// </summary>
    public static IReadOnlyList<SemanticRelation> FromCompilation(Compilation compilation)
    {
        var edges = new List<SemanticRelation>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            foreach (var typeSyntax in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(typeSyntax) is not INamedTypeSymbol type)
                {
                    continue;
                }

                var from = Fqn(type);
                if (from is null)
                {
                    continue;
                }

                foreach (var iface in type.Interfaces)
                {
                    var to = Fqn(iface);
                    if (to is null)
                    {
                        continue;
                    }

                    Add(edges, seen, new SemanticRelation(from, to, "implements"));
                }
            }

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol target)
                {
                    continue;
                }

                if (model.GetEnclosingSymbol(invocation.SpanStart) is not IMethodSymbol caller)
                {
                    continue;
                }

                var from = Fqn(caller);
                var to = Fqn(target);
                if (from is null || to is null || string.Equals(from, to, StringComparison.Ordinal))
                {
                    continue;
                }

                Add(edges, seen, new SemanticRelation(from, to, "calls"));
            }
        }

        return edges;
    }

    private static void EnsureMsBuild()
    {
        if (Interlocked.Exchange(ref _msbuildRegistered, 1) == 0)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }

    private static void Add(List<SemanticRelation> edges, HashSet<string> seen, SemanticRelation edge)
    {
        if (seen.Add(edge.FromFqn + "\t" + edge.ToFqn + "\t" + edge.RelType))
        {
            edges.Add(edge);
        }
    }

    private static string? Fqn(ISymbol symbol)
    {
        foreach (var reference in symbol.OriginalDefinition.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is SyntaxNode node && TryFqn(node) is { } fqn)
            {
                return fqn;
            }
        }

        return null;
    }

    private static string? TryFqn(SyntaxNode node)
    {
        string name;
        switch (node)
        {
            case MethodDeclarationSyntax method:
                name = method.Identifier.Text + method.ParameterList.ToString();
                break;
            case TypeDeclarationSyntax type:
                name = type.Identifier.Text;
                break;
            default:
                return null;
        }

        var parts = new List<string>();
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            switch (parent)
            {
                case TypeDeclarationSyntax type:
                    parts.Add(type.Identifier.Text);
                    break;
                case BaseNamespaceDeclarationSyntax ns:
                    parts.Add(ns.Name.ToString());
                    break;
            }
        }

        parts.Reverse();
        parts.Add(name);
        return string.Join('.', parts);
    }
}
