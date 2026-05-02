using Enaga.Hosting;
using Enaga.React.OkojoRuntime;
using Enaga.SampleApp;
using Xunit;

namespace Enaga.Tests;

public sealed class SampleAppOptionsTests
{
    [Fact]
    public void Parse_FastRefresh_EnablesWatchingForFastRefreshGraphRoot()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var entryDirectory = Path.Combine(rootDirectory, "dist", "fast-refresh", "examples", "SampleApp", "src");
        Directory.CreateDirectory(entryDirectory);
        var entryPath = Path.Combine(entryDirectory, "fast-refresh-entry.mjs");
        File.WriteAllText(entryPath, "export default null;");

        try
        {
            var options = SampleAppOptions.Parse(["--fast-refresh", "--react-entry", entryPath]);

            Assert.Equal(SampleAppRuntimeProfile.FastRefresh, options.RuntimeProfile);
            Assert.True(options.EnableFileWatching);
            Assert.Contains(Path.Combine(rootDirectory, "dist", "fast-refresh"), options.WatchPaths);
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    [Fact]
    public void Parse_ReactDebug_DoesNotEnableFastRefreshOrWatching()
    {
        var entryPath = "C:\\temp\\react-entry.mjs";

        var options = SampleAppOptions.Parse(["--react-debug", "--react-entry", entryPath]);

        Assert.Equal(SampleAppRuntimeProfile.Stable, options.RuntimeProfile);
        Assert.True(options.ReactDebug);
        Assert.True(options.EnableDebugFeatures);
        Assert.False(options.EnableFileWatching);
        Assert.Equal(entryPath, options.ReactEntryPath);
    }

    [Fact]
    public void Parse_WindowConfig_AppliesWindowSize()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(rootDirectory, "sample-appsettings.json");
        Directory.CreateDirectory(rootDirectory);
        File.WriteAllText(configPath, """
        {
          "window": {
            "width": 1440,
            "height": 900
          }
        }
        """);

        try
        {
            var options = SampleAppOptions.Parse(["--config", configPath, "--react-entry", "C:\\temp\\react-entry.mjs"]);

            Assert.Equal(1440, options.WindowWidth);
            Assert.Equal(900, options.WindowHeight);
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    [Fact]
    public void CreateDiagnosticsSink_FastRefresh_EnablesOnlyRefreshDiagnostics()
    {
        var options = SampleAppOptions.Parse(["--fast-refresh", "--react-entry", "C:\\temp\\fast-refresh-entry.mjs"]);

        var diagnostics = options.CreateDiagnosticsSink();

        Assert.True(diagnostics.IsEnabled(RuntimeDiagnosticArea.Configuration));
        Assert.True(diagnostics.IsEnabled(RuntimeDiagnosticArea.Reload));
        Assert.True(diagnostics.IsEnabled(RuntimeDiagnosticArea.ModuleInvalidation));
        Assert.False(diagnostics.IsEnabled(RuntimeDiagnosticArea.Rendering));
        Assert.False(diagnostics.IsEnabled(RuntimeDiagnosticArea.Assets));
    }

    [Fact]
    public void CreateDiagnosticsSink_ReactDebug_EnablesRuntimeLifecycleWithoutRefreshDiagnostics()
    {
        var options = SampleAppOptions.Parse(["--react-debug", "--react-entry", "C:\\temp\\react-entry.mjs"]);

        var diagnostics = options.CreateDiagnosticsSink();

        Assert.True(diagnostics.IsEnabled(RuntimeDiagnosticArea.Configuration));
        Assert.True(diagnostics.IsEnabled(RuntimeDiagnosticArea.RuntimeLifecycle));
        Assert.False(diagnostics.IsEnabled(RuntimeDiagnosticArea.Reload));
        Assert.False(diagnostics.IsEnabled(RuntimeDiagnosticArea.ModuleInvalidation));
    }

    [Fact]
    public void CreateDiagnosticsSink_HostLogFile_WritesToFileOnly()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var logPath = Path.Combine(rootDirectory, "logs", "sample.log");
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var options = SampleAppOptions.Parse([
                "--react-entry", "C:\\temp\\react-entry.mjs",
                "--host-diagnostics", "configuration",
                "--host-log-file", logPath
            ]);

            var diagnostics = options.CreateDiagnosticsSink();
            diagnostics.Write(new RuntimeDiagnosticEvent(RuntimeDiagnosticArea.Configuration, "SampleTest", "hello file"));

            Assert.True(File.Exists(logPath));
            Assert.Contains("hello file", File.ReadAllText(logPath));
            Assert.Equal(logPath, options.DiagnosticLogPath);
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    [Fact]
    public void Parse_ConfigWithDisabledDevelopmentSection_RemainsStable()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(rootDirectory, "sample-appsettings.json");
        Directory.CreateDirectory(rootDirectory);
        File.WriteAllText(configPath, """
        {
          "react": {
            "development": {
              "fastRefresh": false,
              "watch": false,
              "watchPaths": []
            }
          }
        }
        """);

        try
        {
            var options = SampleAppOptions.Parse(["--config", configPath, "--react-entry", "C:\\temp\\react-entry.mjs"]);

            Assert.Equal(SampleAppRuntimeProfile.Stable, options.RuntimeProfile);
            Assert.False(options.EnableDebugFeatures);
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    [Fact]
    public void CreateDiagnosticsSink_RenderStats_EnablesRenderingAndDamage()
    {
        var options = SampleAppOptions.Parse(["--render-stats", "--react-entry", "C:\\temp\\react-entry.mjs"]);

        var diagnostics = options.CreateDiagnosticsSink();

        Assert.True(diagnostics.IsEnabled(RuntimeDiagnosticArea.Rendering));
        Assert.True(diagnostics.IsEnabled(RuntimeDiagnosticArea.Damage));
    }

    [Fact]
    public void Parse_InfersAssetBaseFromReactAppLayout()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var distDirectory = Path.Combine(rootDirectory, "dist");
        var assetsDirectory = Path.Combine(rootDirectory, "assets");
        Directory.CreateDirectory(distDirectory);
        Directory.CreateDirectory(assetsDirectory);
        var entryPath = Path.Combine(distDirectory, "react-entry.mjs");
        File.WriteAllText(entryPath, "export default null;");

        try
        {
            var options = SampleAppOptions.Parse(["--react-entry", entryPath]);

            Assert.Equal(rootDirectory, options.ReactAssetBasePath);
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    [Fact]
    public void CreateAssetResolver_DefaultResolver_UsesConfiguredAssetBase()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(rootDirectory, "sample-appsettings.json");
        var reactDirectory = Path.Combine(rootDirectory, "ReactApp");
        var assetsDirectory = Path.Combine(reactDirectory, "assets");
        Directory.CreateDirectory(assetsDirectory);
        File.WriteAllText(Path.Combine(assetsDirectory, "demo.jpg"), "demo");
        File.WriteAllText(configPath, """
        {
          "react": {
            "assetBase": "ReactApp"
          }
        }
        """);

        try
        {
            var options = SampleAppOptions.Parse(["--config", configPath, "--react-entry", "C:\\temp\\react-entry.mjs"]);

            var resolved = options.CreateAssetResolver().Resolve(new RuntimeAssetRequest("assets/demo.jpg", options.ReactAssetBasePath));

            Assert.Equal(RuntimeAssetKind.LocalPath, resolved.Kind);
            Assert.Equal(Path.Combine(assetsDirectory, "demo.jpg"), resolved.LocalPath);
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }
}
