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
    /// <inheritdoc />
    public string AnalyzerVersion => "roslyn-syntax:2";

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
                    typeKind,
                    typeFqn,
                    type.Identifier.Text,
                    containingType,
                    signature: null,
                    type,
                    isTest: false,
                    testFramework: null,
                    BaseTypes(type)));
                foreach (var member in type.Members)
                {
                    Walk(member, namespaceName, typeFqn, symbols);
                }

                return;
            case EnumDeclarationSyntax enumDecl:
                var enumFqn = Qualify(namespaceName, containingType, enumDecl.Identifier.Text);
                symbols.Add(Create(
                    "enum",
                    enumFqn,
                    enumDecl.Identifier.Text,
                    containingType,
                    null,
                    enumDecl,
                    false,
                    null,
                    []));
                foreach (var member in enumDecl.Members)
                {
                    symbols.Add(Create(
                        "enum_member",
                        enumFqn + "." + member.Identifier.Text,
                        member.Identifier.Text,
                        enumFqn,
                        null,
                        member,
                        false,
                        null,
                        []));
                }

                return;
            case MethodDeclarationSyntax method:
                var (isTest, framework) = DetectTest(method);
                symbols.Add(Create(
                    "method",
                    Qualify(namespaceName, containingType, method.Identifier.Text) + method.ParameterList.ToString(),
                    method.Identifier.Text,
                    containingType,
                    method.Identifier.Text + method.ParameterList,
                    method,
                    isTest,
                    framework,
                    []));
                return;
            case PropertyDeclarationSyntax property:
                symbols.Add(Create(
                    "property",
                    Qualify(namespaceName, containingType, property.Identifier.Text),
                    property.Identifier.Text,
                    containingType,
                    property.Type + " " + property.Identifier.Text,
                    property,
                    false,
                    null,
                    []));
                return;
            case EventDeclarationSyntax evt:
                symbols.Add(Create(
                    "event",
                    Qualify(namespaceName, containingType, evt.Identifier.Text),
                    evt.Identifier.Text,
                    containingType,
                    null,
                    evt,
                    false,
                    null,
                    []));
                return;
        }

        foreach (var child in node.ChildNodes())
        {
            Walk(child, namespaceName, containingType, symbols);
        }
    }

    private static ParsedSymbol Create(
        string kind,
        string fqn,
        string name,
        string? containing,
        string? signature,
        SyntaxNode node,
        bool isTest,
        string? testFramework,
        IReadOnlyList<string> baseTypeNames)
    {
        var span = node.GetLocation().GetLineSpan();
        return new ParsedSymbol(
            kind,
            fqn,
            name,
            containing,
            signature,
            AccessibilityOf(node),
            span.StartLinePosition.Line + 1,
            span.EndLinePosition.Line + 1,
            XmlDoc(node),
            isTest,
            testFramework,
            baseTypeNames);
    }

    private static IReadOnlyList<string> BaseTypes(TypeDeclarationSyntax type)
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

    private static string? XmlDoc(SyntaxNode node)
    {
        var trivia = node.GetLeadingTrivia()
            .Select(t => t.GetStructure())
            .OfType<DocumentationCommentTriviaSyntax>()
            .FirstOrDefault();
        return trivia?.ToString().Trim();
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
