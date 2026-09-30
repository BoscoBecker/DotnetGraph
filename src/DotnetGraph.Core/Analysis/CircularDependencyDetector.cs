using DotnetGraph.Core.Models;

namespace DotnetGraph.Core.Analysis;

public static class CircularDependencyDetector
{
    public static CircularDependencyReport Analyze(SolutionGraph graph)
    {
        if (graph.Projects.Count == 0 || graph.References.Count == 0)
        {
            return new CircularDependencyReport();
        }

        var idToName = graph.Projects.ToDictionary(p => p.Id, p => p.Name, StringComparer.Ordinal);
        var adjacency = BuildAdjacency(graph.References);
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowLinks = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var cycles = new List<ProjectReferenceCycle>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var indexCounter = 0;

        foreach (var project in graph.Projects)
        {
            if (!indices.ContainsKey(project.Id))
            {
                StrongConnect(
                    project.Id,
                    adjacency,
                    ref indexCounter,
                    indices,
                    lowLinks,
                    stack,
                    onStack,
                    idToName,
                    cycles,
                    seenKeys);
            }
        }

        return new CircularDependencyReport
        {
            Cycles = cycles
                .OrderBy(c => c.ProjectNames.FirstOrDefault(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.ProjectNames.Count)
                .ToList()
        };
    }

    private static Dictionary<string, List<string>> BuildAdjacency(IReadOnlyList<ProjectReferenceEdge> references)
    {
        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var edge in references)
        {
            if (string.IsNullOrWhiteSpace(edge.SourceProjectId)
                || string.IsNullOrWhiteSpace(edge.TargetProjectId))
            {
                continue;
            }

            if (!adjacency.TryGetValue(edge.SourceProjectId, out var targets))
            {
                targets = new List<string>();
                adjacency[edge.SourceProjectId] = targets;
            }

            if (!targets.Any(t => string.Equals(t, edge.TargetProjectId, StringComparison.Ordinal)))
            {
                targets.Add(edge.TargetProjectId);
            }
        }

        return adjacency;
    }

    private static void StrongConnect(
        string nodeId,
        IReadOnlyDictionary<string, List<string>> adjacency,
        ref int indexCounter,
        Dictionary<string, int> indices,
        Dictionary<string, int> lowLinks,
        Stack<string> stack,
        HashSet<string> onStack,
        IReadOnlyDictionary<string, string> idToName,
        List<ProjectReferenceCycle> cycles,
        HashSet<string> seenKeys)
    {
        indices[nodeId] = indexCounter;
        lowLinks[nodeId] = indexCounter;
        indexCounter++;
        stack.Push(nodeId);
        onStack.Add(nodeId);

        if (adjacency.TryGetValue(nodeId, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (!indices.ContainsKey(neighbor))
                {
                    StrongConnect(
                        neighbor,
                        adjacency,
                        ref indexCounter,
                        indices,
                        lowLinks,
                        stack,
                        onStack,
                        idToName,
                        cycles,
                        seenKeys);
                    lowLinks[nodeId] = Math.Min(lowLinks[nodeId], lowLinks[neighbor]);
                }
                else if (onStack.Contains(neighbor))
                {
                    lowLinks[nodeId] = Math.Min(lowLinks[nodeId], indices[neighbor]);
                }
            }
        }

        if (lowLinks[nodeId] != indices[nodeId])
        {
            return;
        }

        var component = new List<string>();
        while (stack.Count > 0)
        {
            var w = stack.Pop();
            onStack.Remove(w);
            component.Add(w);
            if (string.Equals(w, nodeId, StringComparison.Ordinal))
            {
                break;
            }
        }

        if (component.Count <= 1)
        {
            if (component.Count == 1
                && adjacency.TryGetValue(component[0], out var selfNeighbors)
                && selfNeighbors.Any(n => string.Equals(n, component[0], StringComparison.Ordinal)))
            {
                TryAddCycle(
                    new[] { component[0], component[0] },
                    idToName,
                    cycles,
                    seenKeys);
            }

            return;
        }

        var cyclePath = ExtractCyclePath(component, adjacency);
        if (cyclePath.Count >= 2)
        {
            TryAddCycle(cyclePath, idToName, cycles, seenKeys);
        }
    }

    private static List<string> ExtractCyclePath(
        IReadOnlyList<string> component,
        IReadOnlyDictionary<string, List<string>> adjacency)
    {
        var componentSet = new HashSet<string>(component, StringComparer.Ordinal);
        var start = component.OrderBy(id => id, StringComparer.Ordinal).First();

        var queue = new Queue<List<string>>();
        queue.Enqueue(new List<string> { start });

        while (queue.Count > 0)
        {
            var path = queue.Dequeue();
            var current = path[path.Count - 1];

            if (!adjacency.TryGetValue(current, out var neighbors))
            {
                continue;
            }

            foreach (var neighbor in neighbors.OrderBy(n => n, StringComparer.Ordinal))
            {
                if (!componentSet.Contains(neighbor))
                {
                    continue;
                }

                if (string.Equals(neighbor, start, StringComparison.Ordinal) && path.Count >= 2)
                {
                    return new List<string>(path) { start };
                }

                if (path.Any(p => string.Equals(p, neighbor, StringComparison.Ordinal)))
                {
                    continue;
                }

                queue.Enqueue(new List<string>(path) { neighbor });
            }
        }

        return component.Count >= 2
            ? new List<string>(component) { component[0] }
            : new List<string>();
    }

    private static void TryAddCycle(
        IReadOnlyList<string> pathWithClosure,
        IReadOnlyDictionary<string, string> idToName,
        List<ProjectReferenceCycle> cycles,
        HashSet<string> seenKeys)
    {
        if (pathWithClosure.Count < 2)
        {
            return;
        }

        var normalizedKey = NormalizeCycleKey(pathWithClosure);
        if (string.IsNullOrEmpty(normalizedKey) || !seenKeys.Add(normalizedKey))
        {
            return;
        }

        var names = pathWithClosure
            .Select(id => idToName.TryGetValue(id, out var name) ? name : id)
            .ToList();

        var edges = new List<ProjectReferenceEdge>();
        for (var i = 0; i < pathWithClosure.Count - 1; i++)
        {
            edges.Add(new ProjectReferenceEdge
            {
                SourceProjectId = pathWithClosure[i],
                TargetProjectId = pathWithClosure[i + 1]
            });
        }

        cycles.Add(new ProjectReferenceCycle
        {
            ProjectIds = pathWithClosure.ToList(),
            ProjectNames = names,
            Edges = edges
        });
    }

    private static string NormalizeCycleKey(IReadOnlyList<string> pathWithClosure)
    {
        var nodes = pathWithClosure.Count > 0
            && string.Equals(pathWithClosure[0], pathWithClosure[pathWithClosure.Count - 1], StringComparison.Ordinal)
            ? pathWithClosure.Take(pathWithClosure.Count - 1).ToList()
            : pathWithClosure.ToList();

        if (nodes.Count == 0)
        {
            return string.Empty;
        }

        var rotations = new List<string>();
        for (var i = 0; i < nodes.Count; i++)
        {
            rotations.Add(string.Join("|", nodes.Skip(i).Concat(nodes.Take(i))));
        }

        return rotations.OrderBy(r => r, StringComparer.Ordinal).First();
    }
}
