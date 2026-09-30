using System;
using System.Collections.Generic;
using System.IO;
using DotnetGraph.Core.Models;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace DotnetGraph.Extension.Services;

internal static class SolutionProjectCollector
{
    private const string SolutionFolderKind = "{2150E333-8FDC-42A3-9474-1A3956E46DE5}";

    public static IReadOnlyList<SolutionProjectInput> CollectFromDte(DTE dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var results = new List<SolutionProjectInput>();
        if (dte.Solution is null || !dte.Solution.IsOpen)
        {
            return results;
        }

        foreach (Project project in dte.Solution.Projects)
        {
            CollectProject(project, results, string.Empty);
        }

        return results;
    }

    private static void CollectProject(Project project, List<SolutionProjectInput> results, string folderPath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (string.Equals(project.Kind, SolutionFolderKind, StringComparison.OrdinalIgnoreCase))
        {
            if (project.ProjectItems is null)
            {
                return;
            }

            var folderName = project.Name ?? string.Empty;
            var nestedFolder = string.IsNullOrWhiteSpace(folderPath)
                ? folderName
                : string.IsNullOrWhiteSpace(folderName)
                    ? folderPath
                    : folderPath + "\\" + folderName;

            foreach (ProjectItem item in project.ProjectItems)
            {
                if (item.SubProject is not null)
                {
                    CollectProject(item.SubProject, results, nestedFolder);
                }
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(project.FullName))
        {
            return;
        }

        var ext = Path.GetExtension(project.FullName).ToLowerInvariant();
        if (ext is not (".csproj" or ".fsproj" or ".vbproj"))
        {
            return;
        }

        results.Add(new SolutionProjectInput
        {
            Name = project.Name ?? Path.GetFileNameWithoutExtension(project.FullName),
            FullPath = project.FullName,
            SolutionFolderPath = folderPath ?? string.Empty
        });
    }
}
