using Enaga.Hosting;
using Enaga.Rendering;
using Okojo.Hosting;
using Okojo.Node;

namespace Enaga.React.OkojoRuntime;

public sealed class OkojoReactHostOptions
{
    public required IReactAppEntrySource EntrySource { get; init; }

    public RuntimeBackendServices BackendServices { get; init; } = RuntimeBackendServices.Missing;

    public Action<JsGlobalInstaller>? ConfigureAdditionalGlobals { get; init; }

    public DefaultPositionMode DefaultPositionMode { get; init; } = DefaultPositionMode.Relative;

    public ReactRuntimeReloadOptions Reload { get; init; } = ReactRuntimeReloadOptions.Production;

    public IRuntimeAssetResolver AssetResolver { get; init; } =
        RuntimeAssetResolver.FileSystemRelativeToEntry;

    public IRuntimeDiagnosticsSink Diagnostics { get; init; } = RuntimeDiagnosticsSink.None;

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    public bool EnableDebugFeatures { get; init; }

    public Action<JsRuntimeBuilder>? ConfigureRuntime { get; init; }

    public Action<NodeTerminalOptions>? ConfigureTerminal { get; init; }
}
