using System;

namespace DotnetGraph.Extension.ToolWindows;

internal static class GraphToolWindowHost
{
    private static DependencyGraphControl? _control;

    public static void Register(DependencyGraphControl control) => _control = control;

    public static void Unregister(DependencyGraphControl control)
    {
        if (ReferenceEquals(_control, control))
        {
            _control = null;
        }
    }

    public static void RequestRefresh()
    {
        _control?.RequestRefresh();
    }
}
