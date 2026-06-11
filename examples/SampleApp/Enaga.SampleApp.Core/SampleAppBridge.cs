using Enaga.Rendering;
using Enaga.Rendering.Skia;
using Enaga.React.OkojoRuntime.Skia;
using Okojo.Objects;
using Okojo.Runtime;
using Enaga.SampleApp.SyntaxHighlighting;
using SkiaSharp;

namespace Enaga.SampleApp;

public sealed class SampleAppBridge
{
    private readonly SampleHostPanel panel = new();
    private readonly SampleSyntaxHighlighter syntaxHighlighter = new();

    public void RegisterRenderers(SkiaRuntimeSceneHost source)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.RegisterLowLevelSkiaLayer(panel);
        panel.AttachRepaintRequester(request => source.RequestLowLevelSkiaRepaint(panel, request));
        panel.AttachBoundsResolver(nodeId => source.RuntimeHost.TryGetNodeScreenRect(nodeId, out var bounds) ? bounds : null);
        panel.AttachVisibleBoundsResolver(nodeId => source.RuntimeHost.TryGetNodeVisibleScreenRect(nodeId, out var bounds) ? bounds : null);
    }

    public void Install(JsGlobalInstaller installer)
    {
        installer.Value("sampleHostPanel", realm => JsValue.FromObject(SampleHostPanel.ToJsObject(realm, panel)));
        installer.Function("sampleHighlightJsx", 1, info =>
        {
            var source = info.GetArgumentOrDefault(0, JsValue.FromString(string.Empty)).AsString();
            return JsValue.FromObject(syntaxHighlighter.BuildHighlightedLineArrays(info.Realm, source));
        });
    }
}
