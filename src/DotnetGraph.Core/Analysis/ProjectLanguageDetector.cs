using DotnetGraph.Core.Models;

namespace DotnetGraph.Core.Analysis;

internal static class ProjectLanguageDetector
{
    public static ProjectLanguage FromProjectPath(string projectPath)
    {
        var ext = Path.GetExtension(projectPath);
        return ext.ToLowerInvariant() switch
        {
            ".csproj" => ProjectLanguage.CSharp,
            ".fsproj" => ProjectLanguage.FSharp,
            ".vbproj" => ProjectLanguage.VisualBasic,
            _ => ProjectLanguage.Unknown
        };
    }
}
