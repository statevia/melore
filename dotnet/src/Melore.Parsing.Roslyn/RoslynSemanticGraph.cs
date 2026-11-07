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
        => compilation.SyntaxTrees
            .SelectMany(tree =>
            {
                var model = compilation.GetSemanticModel(tree);
                var root = tree.GetRoot();
                return Implements(model, root).Concat(Calls(model, root));
            })
            .DistinctBy(edge => edge.FromFqn + "\t" + edge.ToFqn + "\t" + edge.RelType)
            .ToList();

    private static IEnumerable<SemanticRelation> Implements(SemanticModel model, SyntaxNode root)
        => root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Select(syntax => model.GetDeclaredSymbol(syntax))
            .OfType<INamedTypeSymbol>()
            .SelectMany(type => Fqn(type) is { } from
                ? type.Interfaces
                    .Select(Fqn)
                    .OfType<string>()
                    .Select(to => new SemanticRelation(from, to, "implements"))
                : []);

    private static IEnumerable<SemanticRelation> Calls(SemanticModel model, SyntaxNode root)
        => root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(invocation => ToCall(model, invocation))
            .OfType<SemanticRelation>();

    /// <summary>
    /// メソッド本体の中の呼び出しを辺にする。解決できない呼び出しと自己呼び出しは null。
    /// </summary>
    private static SemanticRelation? ToCall(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol target ||
            model.GetEnclosingSymbol(invocation.SpanStart) is not IMethodSymbol caller)
        {
            return null;
        }

        var from = Fqn(caller);
        var to = Fqn(target);
        return from is null || to is null || string.Equals(from, to, StringComparison.Ordinal)
            ? null
            : new SemanticRelation(from, to, "calls");
    }

    private static void EnsureMsBuild()
    {
        if (Interlocked.Exchange(ref _msbuildRegistered, 1) == 0)
        {
            MSBuildLocator.RegisterDefaults();
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
