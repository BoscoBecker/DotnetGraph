namespace DotnetGraph.Core.Models;

public sealed class ImpactAnalysisResult
{
    public string View { get; set; } = "impactGraph";
    public string SymbolLabel { get; set; } = string.Empty;
    public string DefinitionProjectId { get; set; } = string.Empty;
    public string DefinitionProjectName { get; set; } = string.Empty;
    public IReadOnlyList<ImpactProjectNode> Projects { get; set; } = Array.Empty<ImpactProjectNode>();
    public IReadOnlyList<ImpactProjectEdge> References { get; set; } = Array.Empty<ImpactProjectEdge>();
    public IReadOnlyList<SymbolUsageEntry> Usages { get; set; } = Array.Empty<SymbolUsageEntry>();
    public string? Message { get; set; }
    public string? MessageFormatArg { get; set; }
}

public sealed class ImpactProjectNode
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Language { get; set; }
    public string TargetFramework { get; set; } = string.Empty;
    public int UsageCount { get; set; }
    public bool IsDefinition { get; set; }
}

public sealed class ImpactProjectEdge
{
    public string SourceProjectId { get; set; } = string.Empty;
    public string TargetProjectId { get; set; } = string.Empty;
}

public sealed class SymbolUsageEntry
{
    public string ProjectName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int Line { get; set; }
    public string Context { get; set; } = string.Empty;
}
