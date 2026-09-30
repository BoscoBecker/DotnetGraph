namespace DotnetGraph.Core.Models;

public sealed class NamespaceMapResult
{
    public string View { get; set; } = "namespaceMap";
    public string ProjectName { get; set; } = string.Empty;
    public string RootNamespace { get; set; } = string.Empty;
    public string TargetFramework { get; set; } = string.Empty;
    public int ProjectLanguage { get; set; }
    public IReadOnlyList<NamespaceClusterNode> Clusters { get; set; } = Array.Empty<NamespaceClusterNode>();
    public IReadOnlyList<NamespaceClusterEdge> ClusterEdges { get; set; } = Array.Empty<NamespaceClusterEdge>();
}

public sealed class NamespaceClusterNode
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int TypeCount { get; set; }
    public IReadOnlyList<string> Samples { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> SourceFiles { get; set; } = Array.Empty<string>();
    public IReadOnlyList<NamespaceClusterSourceFile> SourceFileLinks { get; set; } = Array.Empty<NamespaceClusterSourceFile>();
    public IReadOnlyList<NamespaceTypeEntry> Types { get; set; } = Array.Empty<NamespaceTypeEntry>();
}

public sealed class NamespaceClusterSourceFile
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int Line { get; set; } = 1;
}

public sealed class NamespaceTypeEntry
{
    public string Name { get; set; } = string.Empty;
    public string Accessibility { get; set; } = "unknown";
}

public sealed class NamespaceClusterEdge
{
    public string SourceId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
}
