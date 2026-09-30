using DotnetGraph.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.VisualBasic;

namespace DotnetGraph.Core.Analysis;

public sealed class ProjectCompositionAnalyzer
{
    public ProjectDetail Analyze(ProjectNode project)
    {
        MsBuildRegistration.EnsureRegistered();

        var projectReferences = new List<ReferencedProjectInfo>();
        var packageReferences = new List<PackageReferenceInfo>();
        var types = new List<TypeMemberInfo>();

        if (!File.Exists(project.ProjectPath))
        {
            return new ProjectDetail { Project = project };
        }

        using var projectCache = new MsBuildProjectCache();
        var msbuildProject = projectCache.GetProject(project.ProjectPath);
        foreach (var item in msbuildProject.GetItems("ProjectReference"))
        {
            var include = item.EvaluatedInclude;
            if (string.IsNullOrWhiteSpace(include))
            {
                continue;
            }

            var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project.ProjectPath)!, include));
            projectReferences.Add(new ReferencedProjectInfo
            {
                Name = Path.GetFileNameWithoutExtension(path),
                ProjectPath = path
            });
        }

        foreach (var item in msbuildProject.GetItems("PackageReference"))
        {
            packageReferences.Add(new PackageReferenceInfo
            {
                Name = item.EvaluatedInclude,
                Version = item.GetMetadataValue("Version")
            });
        }

        types.AddRange(project.Language switch
        {
            ProjectLanguage.CSharp => AnalyzeCSharp(project.ProjectPath, projectCache),
            ProjectLanguage.VisualBasic => AnalyzeVisualBasic(project.ProjectPath, projectCache),
            ProjectLanguage.FSharp => AnalyzeFSharp(project.ProjectPath, projectCache),
            _ => Array.Empty<TypeMemberInfo>()
        });

        return new ProjectDetail
        {
            Project = project,
            ProjectReferences = projectReferences,
            PackageReferences = packageReferences,
            Types = types
        };
    }

    private static IReadOnlyList<TypeMemberInfo> AnalyzeCSharp(string projectPath, MsBuildProjectCache projectCache)
    {
        var sources = GetCompileFiles(projectPath, "*.cs", projectCache);
        if (sources.Count == 0)
        {
            return Array.Empty<TypeMemberInfo>();
        }

        var trees = sources.Select(p => CSharpSyntaxTree.ParseText(File.ReadAllText(p), path: p)).Cast<SyntaxTree>();
        var compilation = CSharpCompilation.Create(
            Path.GetFileNameWithoutExtension(projectPath),
            trees,
            Array.Empty<MetadataReference>(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return ExtractFromSemanticModel(compilation);
    }

    private static IReadOnlyList<TypeMemberInfo> AnalyzeVisualBasic(string projectPath, MsBuildProjectCache projectCache)
    {
        var sources = GetCompileFiles(projectPath, "*.vb", projectCache);
        if (sources.Count == 0)
        {
            return Array.Empty<TypeMemberInfo>();
        }

        var trees = sources.Select(p => VisualBasicSyntaxTree.ParseText(File.ReadAllText(p), path: p)).Cast<SyntaxTree>();
        var compilation = VisualBasicCompilation.Create(
            Path.GetFileNameWithoutExtension(projectPath),
            trees,
            Array.Empty<MetadataReference>(),
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return ExtractFromSemanticModel(compilation);
    }

    private static IReadOnlyList<TypeMemberInfo> AnalyzeFSharp(string projectPath, MsBuildProjectCache projectCache)
    {
        var sources = GetCompileFiles(projectPath, "*.fs", projectCache);
        var types = new List<TypeMemberInfo>();

        foreach (var file in sources)
        {
            var lines = File.ReadAllLines(file);
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var trimmed = lines[lineIndex].TrimStart();
                if (!trimmed.StartsWith("type ", StringComparison.Ordinal))
                {
                    continue;
                }

                var name = trimmed.Substring(5).Split(new[] { ' ', '=', '<' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                types.Add(new TypeMemberInfo
                {
                    FullName = name,
                    Kind = "FSharpType",
                    Members = Array.Empty<string>(),
                    SourceFilePath = file,
                    SourceLine = lineIndex + 1
                });
            }

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var trimmed = lines[lineIndex].TrimStart();
                if (!trimmed.StartsWith("module ", StringComparison.Ordinal))
                {
                    continue;
                }

                var name = trimmed.Substring(7).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                types.Add(new TypeMemberInfo
                {
                    FullName = name,
                    Kind = "FSharpModule",
                    Members = Array.Empty<string>(),
                    SourceFilePath = file,
                    SourceLine = lineIndex + 1
                });
            }
        }

        return types
            .GroupBy(t => t.FullName, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<TypeMemberInfo> ExtractFromSemanticModel(Compilation compilation)
    {
        var types = new List<TypeMemberInfo>();

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(syntaxTree);
            VisitSyntaxNode(model, syntaxTree.GetRoot(), types);
        }

        return types
            .GroupBy(t => t.FullName, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();
    }

    private static void VisitSyntaxNode(SemanticModel model, SyntaxNode node, List<TypeMemberInfo> types)
    {
        INamedTypeSymbol? symbol = node switch
        {
            Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax cs => model.GetDeclaredSymbol(cs) as INamedTypeSymbol,
            Microsoft.CodeAnalysis.VisualBasic.Syntax.TypeBlockSyntax vb => model.GetDeclaredSymbol(vb) as INamedTypeSymbol,
            _ => null
        };

        if (symbol is not null && !symbol.Name.StartsWith("<", StringComparison.Ordinal))
        {
            var members = symbol.GetMembers()
                .Where(m => m.CanBeReferencedByName && m.DeclaredAccessibility == Accessibility.Public)
                .Select(m => m.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .Take(40)
                .ToList();

            var lineSpan = 0;
            string? filePath = null;
            foreach (var location in symbol.Locations)
            {
                if (!location.IsInSource)
                {
                    continue;
                }

                lineSpan = location.GetLineSpan().StartLinePosition.Line + 1;
                filePath = location.SourceTree?.FilePath;
                break;
            }

            types.Add(new TypeMemberInfo
            {
                FullName = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                Kind = symbol.TypeKind.ToString(),
                Members = members,
                SourceFilePath = filePath,
                SourceLine = lineSpan
            });
        }

        foreach (var child in node.ChildNodes())
        {
            VisitSyntaxNode(model, child, types);
        }
    }

    private static List<string> GetCompileFiles(string projectPath, string pattern, MsBuildProjectCache projectCache)
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
            ? Directory.GetFiles(directory, pattern, SearchOption.AllDirectories).ToList()
            : new List<string>();
    }
}
