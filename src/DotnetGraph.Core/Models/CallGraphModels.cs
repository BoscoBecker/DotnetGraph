namespace DotnetGraph.Core.Models;

public sealed class CallGraphResult
{
    public string View { get; set; } = "callGraph";
    public string Title { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string EntryMethod { get; set; } = string.Empty;
    public string TargetFramework { get; set; } = string.Empty;
    public int ProjectLanguage { get; set; }
    public IReadOnlyList<CallGraphNode> Nodes { get; set; } = Array.Empty<CallGraphNode>();
    public IReadOnlyList<CallGraphEdge> Edges { get; set; } = Array.Empty<CallGraphEdge>();
}

public sealed class CallGraphNode
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public int Depth { get; set; }
    public string Accessibility { get; set; } = "unknown";
    public string? SourceFilePath { get; set; }
    public int SourceLine { get; set; }
}

public sealed class CallGraphEdge
{
    public string SourceId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
}
