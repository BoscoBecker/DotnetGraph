namespace DotnetGraph.Core.Models;

public sealed class NamespaceMapResult
{
    public string View { get; set; } = "namespaceMap";
    public string ProjectName { get; set; } = string.Empty;
    public string RootNamespace { get; set; } = string.Empty;
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
}

public sealed class NamespaceClusterEdge
{
    public string SourceId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
}
