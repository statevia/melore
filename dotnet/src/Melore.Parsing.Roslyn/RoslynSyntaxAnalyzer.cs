using Melore.Core.Models;
using Melore.Core.Ports;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Melore.Parsing.Roslyn;

/// <summary>
/// コンパイルなしで構文木から定義位置を取る。参照解決は後続 Phase。
/// </summary>
public sealed class RoslynSyntaxAnalyzer : ICodeAnalyzer
{
    /// <summary>シンボルの名前まわりの情報。</summary>
    /// <param name="Kind">シンボルの種別。</param>
    /// <param name="Fqn">完全修飾名。</param>
    /// <param name="Name">短い名前。</param>
    /// <param name="Containing">所属する型の FQN。</param>
    /// <param name="Signature">シグネチャ。無ければ null。</param>
    private readonly record struct SymbolHead(
        string Kind,
        string Fqn,
        string Name,
        string? Containing,
        string? Signature);

    /// <inheritdoc />
    public string AnalyzerVersion => "roslyn-syntax:3";

    /// <inheritdoc />
    public IReadOnlyList<ParsedSymbol> AnalyzeFile(string relativePath, string content)
    {
        _ = relativePath;
        var tree = CSharpSyntaxTree.ParseText(content);
        var root = tree.GetCompilationUnitRoot();
        var symbols = new List<ParsedSymbol>();
        Walk(root, namespaceName: "", containingType: null, symbols);
        return symbols;
    }

    private static void Walk(
        SyntaxNode node,
        string namespaceName,
        string? containingType,
        List<ParsedSymbol> symbols)
    {
        switch (node)
        {
            case BaseNamespaceDeclarationSyntax ns:
                var nextNs = ns.Name.ToString();
                foreach (var child in ns.Members)
                {
                    Walk(child, nextNs, containingType, symbols);
                }

                return;
            case TypeDeclarationSyntax type:
                var typeKind = type switch
                {
                    ClassDeclarationSyntax => "class",
                    InterfaceDeclarationSyntax => "interface",
                    StructDeclarationSyntax => "struct",
                    RecordDeclarationSyntax => "record",
                    _ => "class"
                };
                var typeFqn = Qualify(namespaceName, containingType, type.Identifier.Text);
                symbols.Add(Create(
                    new SymbolHead(typeKind, typeFqn, type.Identifier.Text, containingType, Signature: null),
                    type,
                    baseTypeNames: BaseTypes(type)));
                foreach (var member in type.Members)
                {
                    Walk(member, namespaceName, typeFqn, symbols);
                }

                return;
            case EnumDeclarationSyntax enumDecl:
                var enumFqn = Qualify(namespaceName, containingType, enumDecl.Identifier.Text);
                symbols.Add(Create(
                    new SymbolHead("enum", enumFqn, enumDecl.Identifier.Text, containingType, Signature: null),
                    enumDecl));
                symbols.AddRange(enumDecl.Members.Select(member => Create(
                    new SymbolHead(
                        "enum_member",
                        enumFqn + "." + member.Identifier.Text,
                        member.Identifier.Text,
                        enumFqn,
                        Signature: null),
                    member)));

                return;
            case MethodDeclarationSyntax method:
                var (isTest, framework) = DetectTest(method);
                symbols.Add(Create(
                    new SymbolHead(
                        "method",
                        Qualify(namespaceName, containingType, method.Identifier.Text) + method.ParameterList.ToString(),
                        method.Identifier.Text,
                        containingType,
                        method.Identifier.Text + method.ParameterList),
                    method,
                    isTest,
                    framework));
                return;
            case PropertyDeclarationSyntax property:
                symbols.Add(Create(
                    new SymbolHead(
                        "property",
                        Qualify(namespaceName, containingType, property.Identifier.Text),
                        property.Identifier.Text,
                        containingType,
                        property.Type + " " + property.Identifier.Text),
                    property));
                return;
            case EventDeclarationSyntax evt:
                symbols.Add(Create(
                    new SymbolHead(
                        "event",
                        Qualify(namespaceName, containingType, evt.Identifier.Text),
                        evt.Identifier.Text,
                        containingType,
                        Signature: null),
                    evt));
                return;
        }

        foreach (var child in node.ChildNodes())
        {
            Walk(child, namespaceName, containingType, symbols);
        }
    }

    private static ParsedSymbol Create(
        SymbolHead head,
        SyntaxNode node,
        bool isTest = false,
        string? testFramework = null,
        IReadOnlyList<string>? baseTypeNames = null)
    {
        var span = node.GetLocation().GetLineSpan();
        return new ParsedSymbol(
            head.Kind,
            head.Fqn,
            head.Name,
            head.Containing,
            head.Signature,
            AccessibilityOf(node),
            span.StartLinePosition.Line + 1,
            span.EndLinePosition.Line + 1,
            SummaryAndRemarks(node),
            isTest,
            testFramework,
            baseTypeNames ?? []);
    }

    private static List<string> BaseTypes(TypeDeclarationSyntax type)
    {
        if (type.BaseList is null)
        {
            return [];
        }

        return type.BaseList.Types
            .Select(t => t.Type.ToString())
            .Where(n => n.Length > 0)
            .ToList();
    }

    private static string Qualify(string ns, string? containing, string name)
    {
        if (!string.IsNullOrEmpty(containing))
        {
            return containing + "." + name;
        }

        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    private static string AccessibilityOf(SyntaxNode node)
    {
        var modifiers = node switch
        {
            MemberDeclarationSyntax member => member.Modifiers,
            _ => default
        };
        if (modifiers.Any(SyntaxKind.PublicKeyword))
        {
            return "public";
        }

        if (modifiers.Any(SyntaxKind.InternalKeyword))
        {
            return "internal";
        }

        if (modifiers.Any(SyntaxKind.ProtectedKeyword))
        {
            return "protected";
        }

        if (modifiers.Any(SyntaxKind.PrivateKeyword))
        {
            return "private";
        }

        return "private";
    }

    private static string? SummaryAndRemarks(SyntaxNode node)
    {
        var trivia = node.GetLeadingTrivia()
            .Select(t => t.GetStructure())
            .OfType<DocumentationCommentTriviaSyntax>()
            .FirstOrDefault();
        if (trivia is null)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var element in trivia.Content.OfType<XmlElementSyntax>())
        {
            var tag = element.StartTag.Name.ToString();
            if (tag is not ("summary" or "remarks"))
            {
                continue;
            }

            var text = string.Join(' ', element.Content.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (text.Length > 0)
            {
                parts.Add(text);
            }
        }

        return parts.Count == 0 ? null : string.Join('\n', parts);
    }

    private static (bool IsTest, string? Framework) DetectTest(MethodDeclarationSyntax method)
    {
        var isXunit = method.AttributeLists
            .SelectMany(list => list.Attributes)
            .Select(attribute => attribute.Name.ToString())
            .Any(name => name is "Fact" or "FactAttribute" or "Theory" or "TheoryAttribute");
        return isXunit ? (true, "xunit") : (false, null);
    }
}
