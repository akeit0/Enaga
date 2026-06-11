using Enaga.Rendering.Skia;
using Enaga.Rendering;
using Enaga.Hosting;
using Enaga.React.OkojoRuntime;
using Enaga.React.OkojoRuntime.Skia;

namespace Enaga.SampleApp;

public static class SampleAppRuntime
{
    public static SkiaRuntimeSceneHost CreateSource(SampleAppOptions options, IRuntimeDiagnosticsSink? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ReactEntryPath))
            throw new InvalidOperationException("SampleApp requires --react-entry <path-to-react-entry.mjs>.");

        var bridge = new SampleAppBridge();
        var backendServices = SkiaRuntimeBackendServices.Create();
        diagnostics ??= options.CreateDiagnosticsSink();
        diagnostics.Write(new RuntimeDiagnosticEvent(RuntimeDiagnosticArea.Configuration, nameof(SampleAppRuntime), options.Describe()));
        var host = new OkojoNodeReactHost(new OkojoReactHostOptions
        {
            EntrySource = options.CreateReactEntrySource(),
            BackendServices = backendServices,
            ConfigureAdditionalGlobals = bridge.Install,
            Reload = options.CreateReloadOptions(),
            AssetResolver = options.CreateAssetResolver(),
            Diagnostics = diagnostics,
            EnableDebugFeatures = options.EnableDebugFeatures
        });
        var source = new SkiaRuntimeSceneHost(host);
        bridge.RegisterRenderers(source);
        return source;
    }
}
