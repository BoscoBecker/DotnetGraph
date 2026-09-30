using Microsoft.Build.Evaluation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotnetGraph.Core.Analysis;

internal static class CSharpCompilationFactory
{
    public static CSharpCompilation? TryCreate(string projectPath, MsBuildProjectCache projectCache)
    {
        if (!projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var sources = GetCompileFiles(projectPath, projectCache);
        if (sources.Count == 0)
        {
            return null;
        }

        var trees = sources
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path))
            .Cast<SyntaxTree>()
            .ToList();

        var msbuildProject = projectCache.GetProject(projectPath);
        var references = CollectMetadataReferences(msbuildProject, projectPath);

        return CSharpCompilation.Create(
            Path.GetFileNameWithoutExtension(projectPath),
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static List<string> GetCompileFiles(string projectPath, MsBuildProjectCache projectCache)
    {
        var directory = Path.GetDirectoryName(projectPath)!;
        var msbuildProject = projectCache.GetProject(projectPath);
        var fromProject = msbuildProject.GetItems("Compile")
            .Select(i => i.EvaluatedInclude)
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Select(i => Path.GetFullPath(Path.Combine(directory, i)))
            .Where(File.Exists)
            .ToList();

        if (fromProject.Count > 0)
        {
            return fromProject;
        }

        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories).ToList()
            : new List<string>();
    }

    private static List<MetadataReference> CollectMetadataReferences(Project msbuildProject, string projectPath)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projectDir = Path.GetDirectoryName(projectPath)!;

        foreach (var item in msbuildProject.GetItems("Reference"))
        {
            var hint = item.GetMetadataValue("HintPath");
            if (!string.IsNullOrWhiteSpace(hint))
            {
                var path = Path.GetFullPath(Path.Combine(projectDir, hint));
                if (File.Exists(path))
                {
                    references.Add(path);
                }
            }
        }

        foreach (var item in msbuildProject.GetItems("ReferencePath"))
        {
            if (!string.IsNullOrWhiteSpace(item.EvaluatedInclude) && File.Exists(item.EvaluatedInclude))
            {
                references.Add(item.EvaluatedInclude);
            }
        }

        var frameworkDir = ResolveFrameworkReferenceDirectory(msbuildProject);
        if (!string.IsNullOrWhiteSpace(frameworkDir) && Directory.Exists(frameworkDir))
        {
            foreach (var dll in Directory.GetFiles(frameworkDir, "*.dll"))
            {
                references.Add(dll);
            }
        }

        return references.Select(path => MetadataReference.CreateFromFile(path)).Cast<MetadataReference>().ToList();
    }

    private static string? ResolveFrameworkReferenceDirectory(Project project)
    {
        var targetFramework = project.GetPropertyValue("TargetFramework");
        if (string.IsNullOrWhiteSpace(targetFramework))
        {
            targetFramework = project.GetPropertyValue("TargetFrameworkVersion");
            if (!string.IsNullOrWhiteSpace(targetFramework)
                && targetFramework.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                targetFramework = "net" + targetFramework.TrimStart('v').Replace(".", "");
            }
        }

        if (string.IsNullOrWhiteSpace(targetFramework))
        {
            return null;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var refRoot = Path.Combine(programFiles, "Reference Assemblies", "Microsoft", "Framework");
        if (targetFramework.StartsWith("net4", StringComparison.OrdinalIgnoreCase))
        {
            var folder = targetFramework switch
            {
                "net472" or "net47" => Path.Combine(refRoot, ".NETFramework", "v4.7.2"),
                "net48" => Path.Combine(refRoot, ".NETFramework", "v4.8"),
                _ => Path.Combine(refRoot, ".NETFramework", "v4.7.2")
            };
            if (Directory.Exists(folder))
            {
                return folder;
            }
        }

        return null;
    }
}
