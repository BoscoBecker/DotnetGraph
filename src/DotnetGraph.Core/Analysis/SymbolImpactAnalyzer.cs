using DotnetGraph.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotnetGraph.Core.Analysis;

public sealed class SymbolImpactAnalyzer
{
    private const int MaxUsages = 400;
    private const int MaxProjectsToScan = 40;

    public ImpactAnalysisResult Analyze(
        SolutionGraph graph,
        ProjectNode definitionProject,
        string typeFullName,
        string? memberName = null)
    {
        MsBuildRegistration.EnsureRegistered();

        if (definitionProject.Language != ProjectLanguage.CSharp
            || string.IsNullOrWhiteSpace(typeFullName)
            || !File.Exists(definitionProject.ProjectPath))
        {
            return Empty(definitionProject, GraphMessageKeys.ImpactCSharpOnly);
        }

        using var projectCache = new MsBuildProjectCache();
        var definitionCompilation = CSharpCompilationFactory.TryCreateWithSolutionReferences(
            definitionProject.ProjectPath,
            projectCache,
            graph);
        if (definitionCompilation is null)
        {
            return Empty(definitionProject, GraphMessageKeys.ImpactAnalyzeFailed);
        }

        if (!TryResolveTargetSymbol(definitionCompilation, typeFullName, memberName, out var targetSymbol, out var symbolLabel))
        {
            return Empty(definitionProject, GraphMessageKeys.ImpactSymbolNotFound, typeFullName);
        }

        var usages = new List<SymbolUsageEntry>();
        var usageCountByProjectId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var csharpProjects = graph.Projects
            .Where(p => p.Language == ProjectLanguage.CSharp && File.Exists(p.ProjectPath))
            .Take(MaxProjectsToScan)
            .ToList();

        foreach (var project in csharpProjects)
        {
            var compilation = CSharpCompilationFactory.TryCreateWithSolutionReferences(
                project.ProjectPath,
                projectCache,
                graph);
            if (compilation is null)
            {
                continue;
            }

            foreach (var tree in compilation.SyntaxTrees)
            {
                var model = compilation.GetSemanticModel(tree);
                foreach (var node in tree.GetRoot().DescendantNodes())
                {
                    if (usages.Count >= MaxUsages)
                    {
                        break;
                    }

                    if (!TryGetReferencedSymbol(node, model, out var referenced) || referenced is null)
                    {
                        continue;
                    }

                    if (!SymbolsMatch(targetSymbol, referenced))
                    {
                        continue;
                    }

                    var lineSpan = node.GetLocation().GetLineSpan();
                    usages.Add(new SymbolUsageEntry
                    {
                        ProjectName = project.Name,
                        FilePath = tree.FilePath ?? string.Empty,
                        Line = lineSpan.StartLinePosition.Line + 1,
                        Context = node.ToString().Trim().Replace('\r', ' ').Replace('\n', ' ')
                    });

                    usageCountByProjectId.TryGetValue(project.Id, out var count);
                    usageCountByProjectId[project.Id] = count + 1;
                }

                if (usages.Count >= MaxUsages)
                {
                    break;
                }
            }
        }

        var involvedProjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { definitionProject.Id };
        foreach (var id in usageCountByProjectId.Keys)
        {
            involvedProjectIds.Add(id);
        }

        var reverseRefConsumers = graph.References
            .Where(r => string.Equals(r.TargetProjectId, definitionProject.Id, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.SourceProjectId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var consumerId in reverseRefConsumers)
        {
            involvedProjectIds.Add(consumerId);
        }

        var nodes = graph.Projects
            .Where(p => involvedProjectIds.Contains(p.Id))
            .Select(p => new ImpactProjectNode
            {
                Id = p.Id,
                Name = p.Name,
                Language = (int)p.Language,
                TargetFramework = p.TargetFramework,
                UsageCount = usageCountByProjectId.TryGetValue(p.Id, out var usageCount) ? usageCount : 0,
                IsDefinition = string.Equals(p.Id, definitionProject.Id, StringComparison.OrdinalIgnoreCase)
            })
            .OrderByDescending(n => n.IsDefinition)
            .ThenByDescending(n => n.UsageCount)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var edges = graph.References
            .Where(r => involvedProjectIds.Contains(r.SourceProjectId) && involvedProjectIds.Contains(r.TargetProjectId))
            .Select(r => new ImpactProjectEdge
            {
                SourceProjectId = r.SourceProjectId,
                TargetProjectId = r.TargetProjectId
            })
            .ToList();

        return new ImpactAnalysisResult
        {
            SymbolLabel = symbolLabel,
            DefinitionProjectId = definitionProject.Id,
            DefinitionProjectName = definitionProject.Name,
            Projects = nodes,
            References = edges,
            Usages = usages
        };
    }

    private static bool TryResolveTargetSymbol(
        Compilation compilation,
        string typeFullName,
        string? memberName,
        out ISymbol targetSymbol,
        out string symbolLabel)
    {
        targetSymbol = null!;
        symbolLabel = typeFullName;

        INamedTypeSymbol? typeSymbol = null;
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var typeSyntax in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(typeSyntax) is not INamedTypeSymbol declared)
                {
                    continue;
                }

                if (string.Equals(declared.ToDisplayString(), typeFullName, StringComparison.Ordinal)
                    || string.Equals(declared.Name, typeFullName, StringComparison.Ordinal))
                {
                    typeSymbol = declared;
                    break;
                }
            }

            if (typeSymbol is not null)
            {
                break;
            }
        }

        if (typeSymbol is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(memberName))
        {
            targetSymbol = typeSymbol;
            symbolLabel = typeSymbol.ToDisplayString();
            return true;
        }

        foreach (var member in typeSymbol.GetMembers())
        {
            if (string.Equals(member.Name, memberName, StringComparison.Ordinal))
            {
                targetSymbol = member;
                symbolLabel = member.ToDisplayString();
                return true;
            }
        }

        return false;
    }

    private static bool TryGetReferencedSymbol(SyntaxNode node, SemanticModel model, out ISymbol? symbol)
    {
        symbol = null;
        switch (node)
        {
            case IdentifierNameSyntax:
            case GenericNameSyntax:
            case QualifiedNameSyntax:
            case MemberAccessExpressionSyntax:
            case ObjectCreationExpressionSyntax:
            case AttributeSyntax:
                var symbolInfo = model.GetSymbolInfo(node);
                symbol = symbolInfo.Symbol;
                if (symbol is null && symbolInfo.CandidateSymbols.Length > 0)
                {
                    symbol = symbolInfo.CandidateSymbols[0];
                }

                return symbol is not null;
            default:
                return false;
        }
    }

    private static bool SymbolsMatch(ISymbol target, ISymbol candidate)
    {
        if (SymbolEqualityComparer.Default.Equals(target, candidate))
        {
            return true;
        }

        return string.Equals(
            target.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            candidate.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            StringComparison.Ordinal);
    }

    private static ImpactAnalysisResult Empty(ProjectNode project, string messageKey, string? formatArg = null) =>
        new()
        {
            DefinitionProjectId = project.Id,
            DefinitionProjectName = project.Name,
            SymbolLabel = messageKey,
            Message = messageKey,
            MessageFormatArg = formatArg,
            Projects = new[]
            {
                new ImpactProjectNode
                {
                    Id = project.Id,
                    Name = project.Name,
                    Language = (int)project.Language,
                    TargetFramework = project.TargetFramework,
                    IsDefinition = true
                }
            }
        };
}
