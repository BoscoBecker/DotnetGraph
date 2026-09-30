using System.Collections.Generic;

namespace DotnetGraph.Extension.ToolWindows;

internal sealed class DetailCycleItem
{
    public int Index { get; set; }

    public IReadOnlyList<string> ProjectNames { get; set; } = new List<string>();

    public string PathDisplay => FormatCyclePath(ProjectNames);

    public static string FormatCyclePath(IReadOnlyList<string> names)
    {
        if (names.Count == 0)
        {
            return string.Empty;
        }

        var lines = new List<string> { names[0] };
        for (var i = 1; i < names.Count; i++)
        {
            lines.Add("→ " + names[i]);
        }

        return string.Join("\n", lines);
    }
}
