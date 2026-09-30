namespace DotnetGraph.Core.Models;

public sealed class CircularDependencyReport
{
    public IReadOnlyList<ProjectReferenceCycle> Cycles { get; set; } = Array.Empty<ProjectReferenceCycle>();

    public bool HasCycles => Cycles.Count > 0;
}

public sealed class ProjectReferenceCycle
{
    public IReadOnlyList<string> ProjectIds { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> ProjectNames { get; set; } = Array.Empty<string>();

    public IReadOnlyList<ProjectReferenceEdge> Edges { get; set; } = Array.Empty<ProjectReferenceEdge>();
}
