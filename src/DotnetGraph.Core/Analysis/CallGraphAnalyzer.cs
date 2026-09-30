using System.Linq;
using DotnetGraph.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
namespace DotnetGraph.Core.Analysis;

public sealed class CallGraphAnalyzer
{
    private const int MaxDepth = 10;
    private const int MaxNodes = 800;

    public CallGraphResult Analyze(ProjectNode project, string? rootTypeName = null, string? rootMethodName = null)
    {
        MsBuildRegistration.EnsureRegistered();

        if (project.Language != ProjectLanguage.CSharp || !File.Exists(project.ProjectPath))
        {
            return Empty(project, GraphMessageKeys.CallGraphCSharpOnly);
        }

        using var projectCache = new MsBuildProjectCache();
        var compilation = CSharpCompilationFactory.TryCreate(project.ProjectPath, projectCache);
        if (compilation is null)
        {
            return Empty(project, GraphMessageKeys.CallGraphCompileFailed);
        }

        var types = CollectTypes(compilation);
        var entryType = ResolveEntryType(types, rootTypeName);
        if (entryType is null)
        {
            return Empty(project, GraphMessageKeys.CallGraphNoEntryType);
        }

        var nodes = new Dictionary<string, CallGraphNode>(StringComparer.Ordinal);
        var edges = new List<CallGraphEdge>();
        var expandedMethods = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in types)
        {
            foreach (var method in GetOrdinaryMethods(type))
            {
                if (nodes.Count >= MaxNodes)
                {
                    break;
                }

                var depth = string.Equals(type.Name, entryType.Name, StringComparison.Ordinal)
                    && string.Equals(type.ToDisplayString(), entryType.ToDisplayString(), StringComparison.Ordinal)
                    ? 0
                    : 1;
                AddMethodNode(nodes, method, depth, type.Name);
            }
        }

        var entryMethods = GetOrdinaryMethods(entryType);
        if (entryMethods.Count == 0)
        {
            return Empty(project, GraphMessageKeys.CallGraphNoEntryMethods);
        }

        if (!string.IsNullOrWhiteSpace(rootMethodName))
        {
            entryMethods = entryMethods
                .Where(m => string.Equals(m.Name, rootMethodName, StringComparison.Ordinal))
                .ToList();
            if (entryMethods.Count == 0)
            {
                return Empty(project, GraphMessageKeys.CallGraphMethodNotFound, rootMethodName);
            }
        }

        foreach (var method in entryMethods)
        {
            var key = GetMethodKey(method);
            if (nodes.TryGetValue(key, out var node))
            {
                node.Depth = 0;
            }
            else
            {
                AddMethodNode(nodes, method, 0, entryType.Name);
            }

            ExpandCalls(compilation, method, 0, nodes, edges, expandedMethods);
        }

        AddIntraProjectCallEdges(compilation, types, nodes, edges);

        return new CallGraphResult
        {
            View = "callGraph",
            Title = entryType.Name,
            ProjectName = project.Name,
            TargetFramework = project.TargetFramework,
            ProjectLanguage = (int)project.Language,
            EntryMethod = entryMethods.Count == 1
                ? entryMethods[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
                : $"{entryMethods.Count} métodos em {entryType.Name}",
            Nodes = nodes.Values.OrderBy(n => n.Depth).ThenBy(n => n.Label, StringComparer.Ordinal).ToList(),
            Edges = edges
        };
    }

    private static INamedTypeSymbol? ResolveEntryType(IReadOnlyList<INamedTypeSymbol> types, string? rootTypeName)
    {
        if (!string.IsNullOrWhiteSpace(rootTypeName))
        {
            var match = types.FirstOrDefault(t =>
                string.Equals(t.Name, rootTypeName, StringComparison.Ordinal)
                || string.Equals(t.ToDisplayString(), rootTypeName, StringComparison.Ordinal)
                || t.ToDisplayString().EndsWith("." + rootTypeName, StringComparison.Ordinal));
            if (match is not null)
            {
                return match;
            }
        }

        return types
                   .Where(t => t.Name.EndsWith("Controller", StringComparison.Ordinal))
                   .OrderBy(t => t.Name, StringComparer.Ordinal)
                   .FirstOrDefault()
               ?? types.OrderBy(t => t.Name, StringComparer.Ordinal).FirstOrDefault();
    }

    private static List<IMethodSymbol> GetOrdinaryMethods(INamedTypeSymbol type) =>
        type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary)
            .Where(m => !m.IsImplicitlyDeclared)
            .Where(m => m.AssociatedSymbol is null)
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

    private static List<INamedTypeSymbol> CollectTypes(CSharpCompilation compilation)
    {
        var types = new List<INamedTypeSymbol>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var typeSyntax in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(typeSyntax) is INamedTypeSymbol typeSymbol)
                {
                    types.Add(typeSymbol);
                }
            }
        }

        return types;
    }

    private static void ExpandCalls(
        CSharpCompilation compilation,
        IMethodSymbol method,
        int depth,
        Dictionary<string, CallGraphNode> nodes,
        List<CallGraphEdge> edges,
        HashSet<string> expandedMethods)
    {
        if (depth >= MaxDepth || nodes.Count >= MaxNodes)
        {
            return;
        }

        var methodKey = GetMethodKey(method);
        if (!expandedMethods.Add(methodKey))
        {
            return;
        }

        foreach (var callee in CollectCallees(compilation, method))
        {
            if (nodes.Count >= MaxNodes)
            {
                break;
            }

            if (!IsDefinedInProject(callee, compilation))
            {
                continue;
            }

            var calleeDepth = depth + 1;
            var calleeKey = GetMethodKey(callee);
            AddMethodNode(nodes, callee, calleeDepth, callee.ContainingType?.Name ?? "?");
            edges.Add(new CallGraphEdge { SourceId = methodKey, TargetId = calleeKey });
            ExpandCalls(compilation, callee, calleeDepth, nodes, edges, expandedMethods);
        }
    }

    private static void AddIntraProjectCallEdges(
        CSharpCompilation compilation,
        IReadOnlyList<INamedTypeSymbol> types,
        Dictionary<string, CallGraphNode> nodes,
        List<CallGraphEdge> edges)
    {
        var edgeKeys = new HashSet<string>(
            edges.Select(e => e.SourceId + "\0" + e.TargetId),
            StringComparer.Ordinal);

        foreach (var type in types)
        {
            foreach (var method in GetOrdinaryMethods(type))
            {
                var sourceKey = GetMethodKey(method);
                if (!nodes.ContainsKey(sourceKey))
                {
                    continue;
                }

                foreach (var callee in CollectCallees(compilation, method))
                {
                    if (!IsDefinedInProject(callee, compilation))
                    {
                        continue;
                    }

                    var targetKey = GetMethodKey(callee);
                    if (!nodes.ContainsKey(targetKey))
                    {
                        continue;
                    }

                    var edgeKey = sourceKey + "\0" + targetKey;
                    if (!edgeKeys.Add(edgeKey))
                    {
                        continue;
                    }

                    edges.Add(new CallGraphEdge { SourceId = sourceKey, TargetId = targetKey });
                }
            }
        }
    }

    private static bool IsDefinedInProject(IMethodSymbol method, CSharpCompilation compilation) =>
        method.Locations.Any(static l => l.IsInSource)
        && SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly);

    private static List<IMethodSymbol> CollectCallees(CSharpCompilation compilation, IMethodSymbol method)
    {
        var callees = new List<IMethodSymbol>();
        foreach (var syntaxRef in method.DeclaringSyntaxReferences)
        {
            var root = syntaxRef.GetSyntax();
            var tree = root.SyntaxTree;
            var model = compilation.GetSemanticModel(tree);

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetSymbolInfo(invocation).Symbol is IMethodSymbol callee
                    && !callee.IsImplicitlyDeclared)
                {
                    callees.Add(callee);
                }
            }
        }

        return callees
            .GroupBy(c => GetMethodKey(c))
            .Select(g => g.First())
            .OrderBy(c => c.ContainingType?.Name, StringComparer.Ordinal)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static void AddMethodNode(
        Dictionary<string, CallGraphNode> nodes,
        IMethodSymbol method,
        int depth,
        string typeName)
    {
        var id = GetMethodKey(method);
        if (nodes.TryGetValue(id, out var existing))
        {
            if (depth < existing.Depth)
            {
                existing.Depth = depth;
            }

            return;
        }

        var (filePath, line) = GetPrimarySourceLocation(method);
        nodes[id] = new CallGraphNode
        {
            Id = id,
            Label = method.Name + "()",
            Subtitle = method.ContainingType?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? typeName,
            Depth = depth,
            Accessibility = MapAccessibility(method.DeclaredAccessibility),
            SourceFilePath = filePath,
            SourceLine = line
        };
    }

    private static (string? FilePath, int Line) GetPrimarySourceLocation(IMethodSymbol method)
    {
        foreach (var location in method.Locations)
        {
            if (!location.IsInSource)
            {
                continue;
            }

            var tree = location.SourceTree;
            if (tree is null)
            {
                continue;
            }

            var lineSpan = location.GetLineSpan();
            return (tree.FilePath, lineSpan.StartLinePosition.Line + 1);
        }

        return (null, 0);
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

    private static string GetMethodKey(IMethodSymbol method) =>
        method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

    private static CallGraphResult Empty(ProjectNode project, string messageKey, string? formatArg = null)
    {
        return new CallGraphResult
        {
            View = "callGraph",
            Title = messageKey,
            ProjectName = project.Name,
            EntryMethod = formatArg ?? string.Empty,
            TargetFramework = project.TargetFramework,
            ProjectLanguage = (int)project.Language,
            Nodes = new[]
            {
                new CallGraphNode
                {
                    Id = "info",
                    Label = messageKey,
                    Subtitle = project.Name,
                    Depth = 0,
                    Accessibility = "unknown"
                }
            }
        };
    }
}
