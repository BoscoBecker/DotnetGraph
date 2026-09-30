using System.Security.Cryptography;
using System.Text;
using DotnetGraph.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotnetGraph.Core.Analysis;

public sealed class TypeGraphAnalyzer
{
    private const int MaxTypes = 220;
    private const int MaxEdges = 600;

    public TypeGraphResult Analyze(ProjectNode project)
    {
        MsBuildRegistration.EnsureRegistered();

        if (project.Language != ProjectLanguage.CSharp || !File.Exists(project.ProjectPath))
        {
            return Empty(project, GraphMessageKeys.TypeGraphCSharpOnly);
        }

        using var projectCache = new MsBuildProjectCache();
        var compilation = CSharpCompilationFactory.TryCreate(project.ProjectPath, projectCache);
        if (compilation is null)
        {
            return Empty(project, GraphMessageKeys.TypeGraphCompileFailed);
        }

        var typeSymbols = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var typeSyntax in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(typeSyntax) is not INamedTypeSymbol typeSymbol
                    || string.IsNullOrWhiteSpace(typeSymbol.Name)
                    || typeSymbol.Name.StartsWith("<", StringComparison.Ordinal))
                {
                    continue;
                }

                var id = CreateTypeId(typeSymbol);
                typeSymbols[id] = typeSymbol;
                if (typeSymbols.Count >= MaxTypes)
                {
                    break;
                }
            }

            if (typeSymbols.Count >= MaxTypes)
            {
                break;
            }
        }

        if (typeSymbols.Count == 0)
        {
            return Empty(project, GraphMessageKeys.TypeGraphNoTypes);
        }

        var nodes = typeSymbols
            .Select(kvp => ToNode(kvp.Key, kvp.Value))
            .OrderBy(n => n.Label, StringComparer.Ordinal)
            .ToList();

        var edges = new List<TypeGraphEdge>();
        var edgeKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kvp in typeSymbols)
        {
            var sourceId = kvp.Key;
            var sourceType = kvp.Value;
            foreach (var referenced in TypeReferenceWalker.GetReferencedTypes(sourceType))
            {
                var targetId = CreateTypeId(referenced);
                if (!typeSymbols.ContainsKey(targetId))
                {
                    continue;
                }

                var key = sourceId + "->" + targetId;
                if (!edgeKeys.Add(key) || edges.Count >= MaxEdges)
                {
                    continue;
                }

                edges.Add(new TypeGraphEdge
                {
                    SourceId = sourceId,
                    TargetId = targetId,
                    Kind = "uses"
                });
            }
        }

        return new TypeGraphResult
        {
            ProjectName = project.Name,
            TargetFramework = project.TargetFramework,
            ProjectLanguage = (int)project.Language,
            Nodes = nodes,
            Edges = edges
        };
    }

    private static TypeGraphNode ToNode(string id, INamedTypeSymbol typeSymbol)
    {
        var ns = typeSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        Location? location = null;
        foreach (var loc in typeSymbol.Locations)
        {
            if (loc.IsInSource)
            {
                location = loc;
                break;
            }
        }

        var line = 1;
        string? filePath = null;
        if (location is not null)
        {
            filePath = location.SourceTree?.FilePath;
            line = location.GetLineSpan().StartLinePosition.Line + 1;
        }

        return new TypeGraphNode
        {
            Id = id,
            Label = typeSymbol.Name,
            Subtitle = string.IsNullOrWhiteSpace(ns) ? typeSymbol.Name : ns,
            Accessibility = MapAccessibility(typeSymbol.DeclaredAccessibility),
            SourceFilePath = filePath,
            SourceLine = line
        };
    }

    private static string CreateTypeId(INamedTypeSymbol typeSymbol)
    {
        var key = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
        var hex = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        return hex.Length >= 12 ? hex.Substring(0, 12) : hex;
    }

    private static string MapAccessibility(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Private => "private",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedOrInternal => "protectedInternal",
        Accessibility.ProtectedAndInternal => "protectedInternal",
        _ => "unknown"
    };

    private static TypeGraphResult Empty(ProjectNode project, string message) =>
        new()
        {
            ProjectName = project.Name,
            TargetFramework = project.TargetFramework,
            ProjectLanguage = (int)project.Language,
            Nodes = new[]
            {
                new TypeGraphNode
                {
                    Id = "info",
                    Label = message,
                    Subtitle = project.Name
                }
            }
        };
}

internal static class TypeReferenceWalker
{
    public static IEnumerable<INamedTypeSymbol> GetReferencedTypes(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.BaseType is not null && typeSymbol.BaseType.SpecialType == SpecialType.None)
        {
            yield return typeSymbol.BaseType;
        }

        foreach (var iface in typeSymbol.Interfaces)
        {
            if (iface.SpecialType == SpecialType.None)
            {
                yield return iface;
            }
        }

        foreach (var member in typeSymbol.GetMembers())
        {
            switch (member)
            {
                case IMethodSymbol method when method.MethodKind == MethodKind.Ordinary:
                    var returnType = ExtractType(method.ReturnType);
                    if (returnType is not null)
                    {
                        yield return returnType;
                    }

                    foreach (var parameter in method.Parameters)
                    {
                        var parameterType = ExtractType(parameter.Type);
                        if (parameterType is not null)
                        {
                            yield return parameterType;
                        }
                    }

                    break;
                case IPropertySymbol property:
                    var propertyType = ExtractType(property.Type);
                    if (propertyType is not null)
                    {
                        yield return propertyType;
                    }

                    break;
                case IFieldSymbol field:
                    var fieldType = ExtractType(field.Type);
                    if (fieldType is not null)
                    {
                        yield return fieldType;
                    }

                    break;
            }
        }
    }

    private static INamedTypeSymbol? ExtractType(ITypeSymbol type) =>
        type switch
        {
            INamedTypeSymbol named => named,
            IArrayTypeSymbol array => ExtractType(array.ElementType),
            IPointerTypeSymbol pointer => ExtractType(pointer.PointedAtType),
            _ => null
        };
}
