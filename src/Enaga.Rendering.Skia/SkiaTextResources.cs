namespace Enaga.Rendering.Skia;

internal sealed class SkiaTextResources : IDisposable
{
    public SkiaTextResources()
    {
        FontCatalog = new TextFontCatalog();
        FontCollection = new SkiaFontCollection(FontCatalog);
        InputMetrics = new TextInputMetrics(FontCatalog);
        TextMeasurer = new SkiaTextMeasurer(FontCatalog, FontCollection);
    }

    public TextFontCatalog FontCatalog { get; }

    public SkiaFontCollection FontCollection { get; }

    public TextInputMetrics InputMetrics { get; }

    public SkiaTextMeasurer TextMeasurer { get; }

    public void Dispose()
    {
        InputMetrics.Dispose();
        FontCollection.Dispose();
        FontCatalog.Dispose();
    }
}
