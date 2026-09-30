using DotnetGraph.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotnetGraph.Core.Analysis;

public sealed class NamespaceMapAnalyzer
{
    private const int MaxSamplesPerCluster = 6;

    public NamespaceMapResult Analyze(ProjectNode project)
    {
        MsBuildRegistration.EnsureRegistered();

        if (project.Language != ProjectLanguage.CSharp || !File.Exists(project.ProjectPath))
        {
            return Empty(project, GraphMessageKeys.NamespaceCSharpOnly);
        }

        using var projectCache = new MsBuildProjectCache();
        var compilation = CSharpCompilationFactory.TryCreate(project.ProjectPath, projectCache);
        if (compilation is null)
        {
            return Empty(project, GraphMessageKeys.NamespaceAnalyzeFailed);
        }

        var typesByCluster = new Dictionary<string, List<INamedTypeSymbol>>(StringComparer.Ordinal);
        var typeToCluster = new Dictionary<INamedTypeSymbol, string>(SymbolEqualityComparer.Default);

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

                var cluster = ResolveClusterLabel(typeSymbol);
                if (!typesByCluster.TryGetValue(cluster, out var list))
                {
                    list = new List<INamedTypeSymbol>();
                    typesByCluster[cluster] = list;
                }

                list.Add(typeSymbol);
                typeToCluster[typeSymbol] = cluster;
            }
        }

        if (typesByCluster.Count == 0)
        {
            return Empty(project, GraphMessageKeys.NamespaceNoTypes);
        }

        var msbuildProject = projectCache.GetProject(project.ProjectPath);
        var rootNamespace = msbuildProject.GetPropertyValue("RootNamespace");
        if (string.IsNullOrWhiteSpace(rootNamespace))
        {
            rootNamespace = InferRootNamespaceFromTypes(typesByCluster.Values.SelectMany(v => v));
        }
        var clusters = typesByCluster
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp => new NamespaceClusterNode
            {
                Id = kvp.Key,
                Label = GetClusterShortLabel(kvp.Key),
                TypeCount = kvp.Value.Count,
                Samples = kvp.Value
                    .Select(t => t.Name)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .Take(MaxSamplesPerCluster)
                    .ToList(),
                SourceFiles = BuildSourceFileNames(kvp.Value),
                SourceFileLinks = BuildSourceFileLinks(kvp.Value),
                Types = kvp.Value
                    .Select(t => new NamespaceTypeEntry
                    {
                        Name = t.Name,
                        Accessibility = MapAccessibility(t.DeclaredAccessibility)
                    })
                    .OrderBy(t => t.Name, StringComparer.Ordinal)
                    .ToList()
            })
            .ToList();

        var clusterEdges = BuildClusterEdges(compilation, typeToCluster);

        return new NamespaceMapResult
        {
            View = "namespaceMap",
            ProjectName = project.Name,
            RootNamespace = rootNamespace,
            TargetFramework = project.TargetFramework,
            ProjectLanguage = (int)project.Language,
            Clusters = clusters,
            ClusterEdges = clusterEdges
        };
    }

    private static List<string> BuildSourceFileNames(IReadOnlyList<INamedTypeSymbol> types) =>
        BuildSourceFileLinks(types).Select(f => f.FileName).ToList();

    private static List<NamespaceClusterSourceFile> BuildSourceFileLinks(IReadOnlyList<INamedTypeSymbol> types)
    {
        var links = new Dictionary<string, NamespaceClusterSourceFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types)
        {
            SyntaxTree? sourceTree = null;
            var sourceLocation = default(Location);
            var hasSource = false;
            foreach (var location in type.Locations)
            {
                if (!location.IsInSource)
                {
                    continue;
                }

                var tree = location.SourceTree;
                if (tree is null || string.IsNullOrWhiteSpace(tree.FilePath))
                {
                    continue;
                }

                sourceTree = tree;
                sourceLocation = location;
                hasSource = true;
                break;
            }

            if (!hasSource || sourceTree is null)
            {
                continue;
            }

            var path = sourceTree.FilePath;
            if (links.ContainsKey(path))
            {
                continue;
            }

            var lineSpan = sourceLocation.GetLineSpan();
            links[path] = new NamespaceClusterSourceFile
            {
                FilePath = path,
                FileName = Path.GetFileName(path),
                Line = lineSpan.StartLinePosition.Line + 1
            };
        }

        return links.Values.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).ToList();
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

    private static string InferRootNamespaceFromTypes(IEnumerable<INamedTypeSymbol> types)
    {
        var namespaces = types
            .Select(t => t.ContainingNamespace?.ToDisplayString())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Cast<string>()
            .ToList();

        if (namespaces.Count == 0)
        {
            return "Project";
        }

        var split = namespaces.Select(n => n.Split('.')).ToList();
        var prefix = new List<string>();
        for (var i = 0; ; i++)
        {
            if (split.Any(parts => parts.Length <= i))
            {
                break;
            }

            var segment = split[0][i];
            if (split.Any(parts => !string.Equals(parts[i], segment, StringComparison.Ordinal)))
            {
                break;
            }

            prefix.Add(segment);
        }

        return prefix.Count > 0 ? string.Join(".", prefix) : namespaces[0];
    }

    private static string ResolveClusterLabel(INamedTypeSymbol typeSymbol)
    {
        var ns = typeSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return string.IsNullOrWhiteSpace(ns) ? "Global" : ns;
    }

    private static string GetClusterShortLabel(string clusterId)
    {
        if (string.Equals(clusterId, "Global", StringComparison.Ordinal))
        {
            return clusterId;
        }

        var parts = clusterId.Split('.');
        return parts.Length <= 1 ? parts[0] : parts[parts.Length - 1];
    }

    private static List<NamespaceClusterEdge> BuildClusterEdges(
        Compilation compilation,
        Dictionary<INamedTypeSymbol, string> typeToCluster)
    {
        var edges = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<NamespaceClusterEdge>();

        foreach (var entry in typeToCluster)
        {
            var sourceType = entry.Key;
            var sourceCluster = entry.Value;
            foreach (var referenced in GetReferencedTypes(sourceType))
            {
                if (!typeToCluster.TryGetValue(referenced, out var targetCluster)
                    || string.Equals(sourceCluster, targetCluster, StringComparison.Ordinal))
                {
                    continue;
                }

                var key = sourceCluster + "->" + targetCluster;
                if (!edges.Add(key))
                {
                    continue;
                }

                result.Add(new NamespaceClusterEdge
                {
                    SourceId = sourceCluster,
                    TargetId = targetCluster
                });
            }
        }

        return result.OrderBy(e => e.SourceId, StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<INamedTypeSymbol> GetReferencedTypes(INamedTypeSymbol typeSymbol)
    {
        foreach (var member in typeSymbol.GetMembers())
        {
            switch (member)
            {
                case IMethodSymbol method:
                    if (method.MethodKind != MethodKind.Ordinary)
                    {
                        continue;
                    }

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

        if (typeSymbol.BaseType is not null)
        {
            yield return typeSymbol.BaseType;
        }

        foreach (var iface in typeSymbol.Interfaces)
        {
            yield return iface;
        }
    }

    private static INamedTypeSymbol? ExtractType(ITypeSymbol type)
    {
        return type switch
        {
            INamedTypeSymbol named => named,
            IArrayTypeSymbol array => ExtractType(array.ElementType),
            IPointerTypeSymbol pointer => ExtractType(pointer.PointedAtType),
            _ => null
        };
    }

    private static NamespaceMapResult Empty(ProjectNode project, string message)
    {
        return new NamespaceMapResult
        {
            ProjectName = project.Name,
            RootNamespace = project.Name,
            Clusters = new[]
            {
                new NamespaceClusterNode
                {
                    Id = "info",
                    Label = message,
                    TypeCount = 0,
                    Samples = Array.Empty<string>()
                }
            }
        };
    }
}
