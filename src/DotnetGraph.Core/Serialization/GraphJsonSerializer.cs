using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetGraph.Core.Models;

namespace DotnetGraph.Core.Serialization;

public static class GraphJsonSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static string SerializeSolutionGraph(SolutionGraph graph, CircularDependencyReport? circularDependencies = null) =>
        JsonSerializer.Serialize(
            new
            {
                view = "architecture",
                projects = graph.Projects,
                references = graph.References,
                circularDependencies = circularDependencies?.HasCycles == true
                    ? circularDependencies.Cycles.Select(c => new
                    {
                        projectIds = c.ProjectIds,
                        projectNames = c.ProjectNames,
                        edges = c.Edges
                    })
                    : null
            },
            Options);

    public static string SerializeProjectDetail(ProjectDetail detail) =>
        JsonSerializer.Serialize(detail, Options);

    public static string SerializeCallGraph(CallGraphResult graph) =>
        JsonSerializer.Serialize(graph, Options);

    public static string SerializeNamespaceMap(NamespaceMapResult graph) =>
        JsonSerializer.Serialize(graph, Options);
}
