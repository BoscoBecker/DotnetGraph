using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace DotnetGraph.Extension.ToolWindows;

[Guid(ToolWindowGuidString)]
public sealed class DependencyGraphToolWindow : ToolWindowPane
{
    public const string ToolWindowGuidString = "f1a2b3c4-d5e6-7890-abcd-ef1234567890";

    public DependencyGraphToolWindow() : base(null)
    {
        Caption = "Dotnet Graph";
        Content = new DependencyGraphControl();
    }
}
