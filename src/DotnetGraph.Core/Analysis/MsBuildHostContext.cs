using System;
using System.Diagnostics;

namespace DotnetGraph.Core.Analysis;

internal static class MsBuildHostContext
{
    public static bool IsVisualStudioDevenv
    {
        get
        {
            try
            {
                return string.Equals(
                    Process.GetCurrentProcess().ProcessName,
                    "devenv",
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
