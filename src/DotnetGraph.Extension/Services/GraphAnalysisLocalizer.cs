using System.Linq;
using DotnetGraph.Core.Analysis;
using DotnetGraph.Core.Models;

namespace DotnetGraph.Extension.Services;

internal static class GraphAnalysisLocalizer
{
    public static CallGraphResult Localize(CallGraphResult result)
    {
        var info = result.Nodes.FirstOrDefault(n => string.Equals(n.Id, "info", StringComparison.Ordinal));
        if (info is null)
        {
            return result;
        }

        var formatArg = info.Label == GraphMessageKeys.CallGraphMethodNotFound
            ? result.EntryMethod
            : null;
        var text = FormatMessage(info.Label, formatArg);
        if (text == info.Label)
        {
            return result;
        }

        var nodes = result.Nodes.ToList();
        var index = nodes.FindIndex(n => string.Equals(n.Id, "info", StringComparison.Ordinal));
        if (index >= 0)
        {
            nodes[index] = new CallGraphNode
            {
                Id = info.Id,
                Label = text,
                Subtitle = info.Subtitle,
                Depth = info.Depth,
                Accessibility = info.Accessibility,
                SourceFilePath = info.SourceFilePath,
                SourceLine = info.SourceLine
            };
        }

        return new CallGraphResult
        {
            View = result.View,
            Title = text,
            ProjectName = result.ProjectName,
            EntryMethod = result.EntryMethod,
            TargetFramework = result.TargetFramework,
            ProjectLanguage = result.ProjectLanguage,
            Nodes = nodes,
            Edges = result.Edges
        };
    }

    public static NamespaceMapResult Localize(NamespaceMapResult result)
    {
        var info = result.Clusters.FirstOrDefault(c => string.Equals(c.Id, "info", StringComparison.Ordinal));
        if (info is null)
        {
            return result;
        }

        var text = FormatMessage(info.Label, null);
        if (text == info.Label)
        {
            return result;
        }

        var clusters = result.Clusters.ToList();
        var index = clusters.FindIndex(c => string.Equals(c.Id, "info", StringComparison.Ordinal));
        if (index >= 0)
        {
            clusters[index] = new NamespaceClusterNode
            {
                Id = info.Id,
                Label = text,
                TypeCount = info.TypeCount,
                Samples = info.Samples,
                SourceFiles = info.SourceFiles,
                SourceFileLinks = info.SourceFileLinks,
                Types = info.Types
            };
        }

        return new NamespaceMapResult
        {
            View = result.View,
            ProjectName = result.ProjectName,
            RootNamespace = result.RootNamespace,
            TargetFramework = result.TargetFramework,
            ProjectLanguage = result.ProjectLanguage,
            Clusters = clusters,
            ClusterEdges = result.ClusterEdges
        };
    }

    public static TypeGraphResult Localize(TypeGraphResult result)
    {
        var info = result.Nodes.FirstOrDefault(n => string.Equals(n.Id, "info", StringComparison.Ordinal));
        if (info is null)
        {
            return result;
        }

        var text = FormatMessage(info.Label, null);
        if (text == info.Label)
        {
            return result;
        }

        var nodes = result.Nodes.ToList();
        var index = nodes.FindIndex(n => string.Equals(n.Id, "info", StringComparison.Ordinal));
        if (index >= 0)
        {
            nodes[index] = new TypeGraphNode
            {
                Id = info.Id,
                Label = text,
                Subtitle = info.Subtitle,
                Accessibility = info.Accessibility,
                SourceFilePath = info.SourceFilePath,
                SourceLine = info.SourceLine
            };
        }

        return new TypeGraphResult
        {
            View = result.View,
            ProjectName = result.ProjectName,
            TargetFramework = result.TargetFramework,
            ProjectLanguage = result.ProjectLanguage,
            Nodes = nodes,
            Edges = result.Edges
        };
    }

    public static ImpactAnalysisResult Localize(ImpactAnalysisResult result)
    {
        if (!LooksLikeMessageKey(result.Message))
        {
            return result;
        }

        var text = FormatMessage(result.Message!, result.MessageFormatArg);
        return new ImpactAnalysisResult
        {
            View = result.View,
            SymbolLabel = text,
            DefinitionProjectId = result.DefinitionProjectId,
            DefinitionProjectName = result.DefinitionProjectName,
            Projects = result.Projects,
            References = result.References,
            Usages = result.Usages,
            Message = text,
            MessageFormatArg = result.MessageFormatArg
        };
    }

    private static bool LooksLikeMessageKey(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.StartsWith("Msg", StringComparison.Ordinal);

    private static string FormatMessage(string key, string? formatArg)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        if (!key.StartsWith("Msg", StringComparison.Ordinal))
        {
            return key;
        }

        return string.IsNullOrWhiteSpace(formatArg)
            ? GraphLocalizer.T(key)
            : GraphLocalizer.Format(key, formatArg);
    }
}
