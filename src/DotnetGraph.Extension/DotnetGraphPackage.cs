using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DotnetGraph.Extension.Commands;
using DotnetGraph.Extension.Services;
using DotnetGraph.Extension.ToolWindows;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace DotnetGraph.Extension;

/// <summary>
/// Loads on demand (no startup AutoLoad). Settings I/O runs off the UI thread during <see cref="InitializeAsync"/>.
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(PackageGuids.PackageString)]
[ProvideMenuResource("Menus.ctmenu", 1000)]
[ProvideToolWindow(typeof(DependencyGraphToolWindow), Style = VsDockStyle.Tabbed, Window = "DocumentWell")]
public sealed class DotnetGraphPackage : AsyncPackage
{
    public static DotnetGraphPackage? Instance { get; private set; }

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        try
        {
            await Task.Run(UserGraphSettings.Load, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ActivityLog.LogError(GetType().Name, ex.ToString());
        }

        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        Instance = this;

        await ShowGraphCommand.InitializeAsync(this).ConfigureAwait(true);
        await base.InitializeAsync(cancellationToken, progress).ConfigureAwait(true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Instance = null;
        }

        base.Dispose(disposing);
    }
}

public static class PackageGuids
{
    public const string PackageString = "c8f4e2a1-9b3d-4f6e-a5c7-1d2e3f4a5b6c";
    public static readonly Guid Package = new(PackageString);
}

public static class CommandIds
{
    public const int ShowDependencyGraph = 0x0100;
}

public static class CommandSetGuids
{
    public static readonly Guid DotnetGraphCommandSet = new("d9a5f3b2-0c4e-5a7f-b6d8-2e3f4a5b6c7d");
}
