namespace DotnetGraph.Core.Models;

public sealed class SolutionGraph
{
    public string SolutionPath { get; set; } = string.Empty;
    public IReadOnlyList<ProjectNode> Projects { get; set; } = Array.Empty<ProjectNode>();
    public IReadOnlyList<ProjectReferenceEdge> References { get; set; } = Array.Empty<ProjectReferenceEdge>();
}

public sealed class ProjectNode
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public ProjectLanguage Language { get; set; }
    public string TargetFramework { get; set; } = string.Empty;
    public string OutputType { get; set; } = string.Empty;
    /// <summary>Solution Explorer folder path (e.g. "src\\Services"), empty for solution root.</summary>
    public string SolutionFolderPath { get; set; } = string.Empty;
}

public sealed class ProjectReferenceEdge
{
    public string SourceProjectId { get; set; } = string.Empty;
    public string TargetProjectId { get; set; } = string.Empty;
}

public sealed class ProjectDetail
{
    public ProjectNode Project { get; set; } = new ProjectNode();
    public IReadOnlyList<ReferencedProjectInfo> ProjectReferences { get; set; } = Array.Empty<ReferencedProjectInfo>();
    public IReadOnlyList<PackageReferenceInfo> PackageReferences { get; set; } = Array.Empty<PackageReferenceInfo>();
    public IReadOnlyList<TypeMemberInfo> Types { get; set; } = Array.Empty<TypeMemberInfo>();
}

public sealed class ReferencedProjectInfo
{
    public string Name { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
}

public sealed class PackageReferenceInfo
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}

public sealed class TypeMemberInfo
{
    public string FullName { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public IReadOnlyList<string> Members { get; set; } = Array.Empty<string>();
    public string? SourceFilePath { get; set; }
    public int SourceLine { get; set; }
}

public sealed class SolutionProjectInput
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string SolutionFolderPath { get; set; } = string.Empty;
}
