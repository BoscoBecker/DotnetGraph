using System.Security.Cryptography;
using System.Text;
using DotnetGraph.Core.Models;
using Microsoft.Build.Construction;
namespace DotnetGraph.Core.Analysis;

public sealed class SolutionGraphService
{
    public SolutionGraph BuildFromSolutionFile(string solutionPath)
    {
        MsBuildRegistration.EnsureRegistered();

        if (!File.Exists(solutionPath))
        {
            throw new FileNotFoundException("Arquivo de solução não encontrado.", solutionPath);
        }

        var solution = SolutionFile.Parse(solutionPath);
        var projects = solution.ProjectsInOrder
            .Where(p => p.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat)
            .Select(p => new SolutionProjectInput
            {
                Name = Path.GetFileNameWithoutExtension(p.AbsolutePath),
                FullPath = Path.GetFullPath(p.AbsolutePath)
            })
            .ToList();

        return BuildFromProjects(solutionPath, projects);
    }

    public SolutionGraph BuildFromProjects(string? solutionPath, IReadOnlyList<SolutionProjectInput> projects)
    {
        MsBuildRegistration.EnsureRegistered();

        using var projectCache = new MsBuildProjectCache();

        var nodes = new List<ProjectNode>();
        var edges = new List<ProjectReferenceEdge>();
        var pathToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var input in projects)
        {
            if (!File.Exists(input.FullPath))
            {
                continue;
            }

            var id = CreateStableId(input.FullPath);
            pathToId[input.FullPath] = id;

            var msbuildProject = projectCache.GetProject(input.FullPath);

            nodes.Add(new ProjectNode
            {
                Id = id,
                Name = input.Name,
                ProjectPath = input.FullPath,
                Language = ProjectLanguageDetector.FromProjectPath(input.FullPath),
                TargetFramework = msbuildProject.GetPropertyValue("TargetFramework")
                    ?? msbuildProject.GetPropertyValue("TargetFrameworks")
                    ?? string.Empty,
                OutputType = msbuildProject.GetPropertyValue("OutputType") ?? string.Empty
            });
        }

        foreach (var input in projects)
        {
            if (!pathToId.TryGetValue(input.FullPath, out var sourceId))
            {
                continue;
            }

            if (!projectCache.TryGetProject(input.FullPath, out var msbuildProject) || msbuildProject is null)
            {
                continue;
            }

            foreach (var reference in msbuildProject.GetItems("ProjectReference"))
            {
                var include = reference.EvaluatedInclude;
                if (string.IsNullOrWhiteSpace(include))
                {
                    continue;
                }

                var referencedPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(input.FullPath)!, include));
                if (!pathToId.TryGetValue(referencedPath, out var targetId))
                {
                    targetId = CreateStableId(referencedPath);
                    pathToId[referencedPath] = targetId;

                    string targetFramework = string.Empty;
                    string outputType = string.Empty;
                    if (projectCache.TryGetProject(referencedPath, out var referencedProject) && referencedProject is not null)
                    {
                        targetFramework = referencedProject.GetPropertyValue("TargetFramework")
                            ?? referencedProject.GetPropertyValue("TargetFrameworks")
                            ?? string.Empty;
                        outputType = referencedProject.GetPropertyValue("OutputType") ?? string.Empty;
                    }

                    nodes.Add(new ProjectNode
                    {
                        Id = targetId,
                        Name = Path.GetFileNameWithoutExtension(referencedPath),
                        ProjectPath = referencedPath,
                        Language = ProjectLanguageDetector.FromProjectPath(referencedPath),
                        TargetFramework = targetFramework,
                        OutputType = outputType
                    });
                }

                edges.Add(new ProjectReferenceEdge
                {
                    SourceProjectId = sourceId,
                    TargetProjectId = targetId
                });
            }
        }

        return new SolutionGraph
        {
            SolutionPath = solutionPath ?? string.Empty,
            Projects = nodes,
            References = edges
        };
    }

    private static string CreateStableId(string fullPath)
    {
        var normalized = Path.GetFullPath(fullPath).ToLowerInvariant();
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        var hex = BitConverter.ToString(hash).Replace("-", string.Empty);
        return hex.Substring(0, 12).ToLowerInvariant();
    }
}
