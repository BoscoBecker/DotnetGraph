namespace DotnetGraph.Core.Analysis;

/// <summary>Stable keys localized in DotnetGraph.Extension (GraphLocalizer).</summary>
public static class GraphMessageKeys
{
    public const string CallGraphCSharpOnly = "MsgCallGraphCSharpOnly";
    public const string CallGraphCompileFailed = "MsgCallGraphCompileFailed";
    public const string CallGraphNoEntryType = "MsgCallGraphNoEntryType";
    public const string CallGraphNoEntryMethods = "MsgCallGraphNoEntryMethods";
    public const string CallGraphMethodNotFound = "MsgCallGraphMethodNotFound";

    public const string NamespaceCSharpOnly = "MsgNamespaceCSharpOnly";
    public const string NamespaceAnalyzeFailed = "MsgNamespaceAnalyzeFailed";
    public const string NamespaceNoTypes = "MsgNamespaceNoTypes";

    public const string TypeGraphCSharpOnly = "MsgTypeGraphCSharpOnly";
    public const string TypeGraphCompileFailed = "MsgTypeGraphCompileFailed";
    public const string TypeGraphNoTypes = "MsgTypeGraphNoTypes";

    public const string ImpactCSharpOnly = "MsgImpactCSharpOnly";
    public const string ImpactAnalyzeFailed = "MsgImpactAnalyzeFailed";
    public const string ImpactSymbolNotFound = "MsgImpactSymbolNotFound";
}
