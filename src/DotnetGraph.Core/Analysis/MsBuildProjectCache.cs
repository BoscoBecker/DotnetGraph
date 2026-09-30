using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Build.Evaluation;

namespace DotnetGraph.Core.Analysis;

internal sealed class MsBuildProjectCache : IDisposable
{
    private readonly ProjectCollection _collection;
    private readonly bool _ownsCollection;
    private readonly Dictionary<string, Project> _projects = new Dictionary<string, Project>(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public MsBuildProjectCache()
    {
        if (MsBuildHostContext.IsVisualStudioDevenv)
        {
            _collection = ProjectCollection.GlobalProjectCollection;
            _ownsCollection = false;
        }
        else
        {
            _collection = new ProjectCollection();
            _ownsCollection = true;
        }
    }

    public Project GetProject(string projectPath)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MsBuildProjectCache));
        }

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            throw new ArgumentException("Caminho de projeto inválido.", nameof(projectPath));
        }

        var fullPath = Path.GetFullPath(projectPath);
        if (_projects.TryGetValue(fullPath, out var cached))
        {
            return cached;
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Projeto não encontrado.", fullPath);
        }

        var project = FindLoadedProject(fullPath) ?? new Project(fullPath, null, null, _collection);
        _projects[fullPath] = project;
        return project;
    }

    public bool TryGetProject(string projectPath, out Project? project)
    {
        project = null;
        if (_disposed || string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
        {
            return false;
        }

        try
        {
            project = GetProject(projectPath);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private Project? FindLoadedProject(string fullPath)
    {
        if (_ownsCollection)
        {
            return null;
        }

        foreach (var loaded in _collection.LoadedProjects)
        {
            if (string.Equals(loaded.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return loaded;
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_ownsCollection)
        {
            foreach (var project in _projects.Values)
            {
                _collection.UnloadProject(project);
            }

            _collection.Dispose();
        }

        _projects.Clear();
        _disposed = true;
    }
}
