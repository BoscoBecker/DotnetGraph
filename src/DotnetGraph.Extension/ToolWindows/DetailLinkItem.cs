namespace DotnetGraph.Extension.ToolWindows;

internal sealed class DetailLinkItem
{
    public string DisplayText { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public int Line { get; set; }
    public string? TypeFullName { get; set; }
    public bool IsNavigable => !string.IsNullOrWhiteSpace(FilePath);
}
