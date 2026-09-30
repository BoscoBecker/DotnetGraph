namespace DotnetGraph.Core.Models;

public sealed class TypeGraphResult
{
    public string View { get; set; } = "typeGraph";
    public string ProjectName { get; set; } = string.Empty;
    public string TargetFramework { get; set; } = string.Empty;
    public int ProjectLanguage { get; set; }
    public IReadOnlyList<TypeGraphNode> Nodes { get; set; } = Array.Empty<TypeGraphNode>();
    public IReadOnlyList<TypeGraphEdge> Edges { get; set; } = Array.Empty<TypeGraphEdge>();
}

public sealed class TypeGraphNode
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Accessibility { get; set; } = "unknown";
    public string? SourceFilePath { get; set; }
    public int SourceLine { get; set; }
}

public sealed class TypeGraphEdge
{
    public string SourceId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string Kind { get; set; } = "uses";
}
