using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Shell;

namespace DotnetGraph.Extension.ToolWindows;

[Guid(ToolWindowGuidString)]
public sealed class DependencyGraphToolWindow : ToolWindowPane
{
    public const string ToolWindowGuidString = "f1a2b3c4-d5e6-7890-abcd-ef1234567890";

    private static readonly Guid GraphCommandImageGuid = new("f2c8a1b3-6d4e-4f90-9a2b-1c3d4e5f6071");

    public DependencyGraphToolWindow() : base(null)
    {
        Caption = "Dotnet Graph";
        BitmapImageMoniker = new ImageMoniker
        {
            Guid = GraphCommandImageGuid,
            Id = 1
        };
        Content = new DependencyGraphControl();
    }
}
