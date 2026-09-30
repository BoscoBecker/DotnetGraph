using System;
using System.ComponentModel.Design;
using System.Threading;
using System.Threading.Tasks;
using DotnetGraph.Extension.ToolWindows;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace DotnetGraph.Extension.Commands;

internal sealed class ShowGraphCommand
{
    private readonly AsyncPackage _package;

    private ShowGraphCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;
        var menuCommandId = new CommandID(CommandSetGuids.DotnetGraphCommandSet, CommandIds.ShowDependencyGraph);
        var menuItem = new OleMenuCommand(Execute, menuCommandId);
        commandService.AddCommand(menuItem);
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService
            ?? throw new InvalidOperationException("OleMenuCommandService não disponível.");
        _ = new ShowGraphCommand(package, commandService);
    }

    private void Execute(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            var window = await _package.ShowToolWindowAsync(
                typeof(DependencyGraphToolWindow),
                0,
                create: true,
                cancellationToken: CancellationToken.None);

            if (window?.Frame is IVsWindowFrame frame)
            {
                frame.Show();
            }

            GraphToolWindowHost.RequestRefresh();
        });
    }
}
