using Enaga.Layout;
using Enaga.Rendering;

namespace Enaga.Html;

public sealed record HtmlDocument(string Html, string? StyleSheet = null, string? BasePath = null);

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
