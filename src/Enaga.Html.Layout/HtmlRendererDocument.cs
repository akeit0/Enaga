using Enaga.Layout;
using Enaga.Rendering;
using Enaga.Html.Dom;

namespace Enaga.Html;

public sealed class HtmlDocument
{
    private readonly string? html;

    public HtmlDocument(string Html, string? StyleSheet = null, string? BasePath = null)
    {
        html = Html ?? string.Empty;
        this.StyleSheet = StyleSheet;
        this.BasePath = BasePath;
    }

    public HtmlDocument(HtmlDomDocument DomDocument, string? StyleSheet = null, string? BasePath = null)
    {
        this.DomDocument = DomDocument ?? throw new ArgumentNullException(nameof(DomDocument));
        this.StyleSheet = StyleSheet;
        this.BasePath = BasePath ?? DomDocument.BasePath;
    }

    public string Html => DomDocument is null ? html ?? string.Empty : DomDocument.ToHtml();

    public string? StyleSheet { get; }

    public string? BasePath { get; }

    public HtmlDomDocument? DomDocument { get; }
}

public sealed record HtmlOptions(
    RuntimeBackendServices? BackendServices = null,
    string RootId = "root",
    float DefaultFontSize = 16,
    int DefaultFontWeight = 400,
    string? DefaultFontFamily = null,
    string DefaultTextColor = "#111827",
    string? DefaultBackgroundColor = "#ffffff",
    LayoutEngineConfig? LayoutConfig = null,
    TimeProvider? TimeProvider = null);
