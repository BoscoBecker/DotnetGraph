using System;
using System.Threading;
using Microsoft.Build.Locator;

namespace DotnetGraph.Core.Analysis;

public static class MsBuildRegistration
{
    private static int _registerAttempted;

    public static void EnsureRegistered()
    {
        if (Interlocked.CompareExchange(ref _registerAttempted, 1, 0) != 0)
        {
            return;
        }

        if (MSBuildLocator.IsRegistered)
        {
            return;
        }

        // Visual Studio (e devenv) já carrega Microsoft.Build 15.x/17.x — não registrar de novo.
        if (IsMsBuildAlreadyLoaded())
        {
            return;
        }

        try
        {
            MSBuildLocator.RegisterDefaults();
        }
        catch (InvalidOperationException)
        {
            // Host já inicializou MSBuild antes do Locator.
        }
    }

    private static bool IsMsBuildAlreadyLoaded()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(assembly.GetName().Name, "Microsoft.Build", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
