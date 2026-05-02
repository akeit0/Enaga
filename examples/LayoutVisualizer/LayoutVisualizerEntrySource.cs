using System.Diagnostics;
using Enaga.React.OkojoRuntime;

namespace Enaga.LayoutVisualizer;

internal sealed class LayoutVisualizerEntrySource : IReactAppEntrySource
{
    private readonly string sourcePath;
    private readonly string runtimeImportPath;
    private readonly string workingDirectory;
    private readonly string generatedDirectory;
    private readonly string generatedSourcePath;
    private readonly string generatedEntryPath;
    private readonly string esbuildExecutablePath;
    private readonly bool debugVisuals;
    private DateTime lastCompiledWriteTimeUtc;

    public LayoutVisualizerEntrySource(string sourcePath, string runtimeImportPath, string workingDirectory, bool debugVisuals = false)
    {
        this.sourcePath = Path.GetFullPath(sourcePath);
        this.runtimeImportPath = Path.GetFullPath(runtimeImportPath);
        this.workingDirectory = Path.GetFullPath(workingDirectory);
        this.debugVisuals = debugVisuals;
        generatedDirectory = Path.Combine(this.workingDirectory, "obj", "layout-visualizer");
        generatedSourcePath = Path.Combine(generatedDirectory, "layout-wrapper.jsx");
        generatedEntryPath = Path.Combine(generatedDirectory, "react-entry.mjs");
        esbuildExecutablePath = ResolveEsbuildExecutablePath(this.workingDirectory);
    }

    public string DisplayPath => sourcePath;

    public string AssetBasePath => sourcePath;

    public IEnumerable<string> EnumerateWatchPaths()
    {
        yield return sourcePath;
    }

    public string PrepareEntryPath()
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Layout visualizer source file was not found.", sourcePath);

        var sourceWriteTimeUtc = File.GetLastWriteTimeUtc(sourcePath);
        if (File.Exists(generatedEntryPath) && sourceWriteTimeUtc <= lastCompiledWriteTimeUtc)
            return generatedEntryPath;

        Directory.CreateDirectory(generatedDirectory);
        File.WriteAllText(generatedSourcePath, BuildWrapperSource());
        RunEsbuild();
        lastCompiledWriteTimeUtc = sourceWriteTimeUtc;
        return generatedEntryPath;
    }

    public void Dispose()
    {
    }

    private string BuildWrapperSource()
    {
        var importSpecifier = ToModuleSpecifier(generatedDirectory, runtimeImportPath);
        var sourceText = File.ReadAllText(sourcePath);
        var nodeImplementation = debugVisuals
            ? """
            function Node({ style, children }) {
              const flatStyle = flattenStyle(style);
              const padding = readEdgeInsets(flatStyle, "padding");
              const borderWidth = typeof flatStyle?.borderWidth === "number" ? flatStyle.borderWidth : 0;
              const insetLeft = padding.left + borderWidth;
              const insetTop = padding.top + borderWidth;
              const insetRight = padding.right + borderWidth;
              const insetBottom = padding.bottom + borderWidth;
              const boxSizing = flatStyle?.boxSizing === "content-box" ? "content-box" : "border-box";
              const widthLabel = formatAxisLabel("w", flatStyle?.width, insetLeft, insetRight, boxSizing);
              const heightLabel = formatAxisLabel("h", flatStyle?.height, insetTop, insetBottom, boxSizing);
              const labelParts = [
                boxSizing === "content-box" ? "cb" : "bb",
                (insetLeft || insetTop || insetRight || insetBottom) ? `inset ${insetLeft}/${insetTop}/${insetRight}/${insetBottom}` : null,
                widthLabel,
                heightLabel,
              ].filter(Boolean);

              return (
                <View style={[nodeBaseStyle, style]}>
                  <View
                    style={[
                      contentOverlayStyle,
                      {
                        left: insetLeft,
                        top: insetTop,
                        right: insetRight,
                        bottom: insetBottom,
                      },
                    ]}
                  />
                  {children}
                  {labelParts.length > 0 ? <Text style={labelStyle}>{labelParts.join(" | ")}</Text> : null}
                </View>
              );
            }
            """
            : """
            function Node({ style, children }) {
              return <View style={[nodeBaseStyle, style]}>{children}</View>;
            }
            """;
        var wrapper =
            $$"""
            import React from "react";
            import { Scene, Text, View, mountNativeApp } from "{{importSpecifier}}";

            const nodeBaseStyle = {
              borderColor: "rgba(148, 163, 184, 0.55)",
              borderWidth: 1,
              borderRadius: 3,
              backgroundColor: "rgba(59, 130, 246, 0.16)",
            };

            const contentOverlayStyle = {
              position: "absolute",
              borderColor: "rgba(248, 250, 252, 0.75)",
              borderWidth: 1,
              borderRadius: 3,
              backgroundColor: "rgba(248, 250, 252, 0.06)",
            };

            const labelStyle = {
              position: "absolute",
              left: "100%",
              top: 0,
              marginLeft: 8,
              fontSize: 11,
              textAlign: "left",
              color: "#e2e8f0",
              backgroundColor: "rgba(15, 23, 42, 0.78)",
              paddingHorizontal: 4,
              paddingVertical: 2,
              borderRadius: 3,
            };

            function flattenStyle(style) {
              if (!style) {
                return null;
              }

              if (Array.isArray(style)) {
                const merged = {};
                for (const entry of style) {
                  const next = flattenStyle(entry);
                  if (!next) {
                    continue;
                  }

                  Object.assign(merged, next);
                }

                return merged;
              }

              return typeof style === "object" ? style : null;
            }

            function readEdgeInsets(style, baseName) {
              const base = typeof style?.[baseName] === "number" ? style[baseName] : 0;
              const horizontal = typeof style?.[`${baseName}Horizontal`] === "number" ? style[`${baseName}Horizontal`] : base;
              const vertical = typeof style?.[`${baseName}Vertical`] === "number" ? style[`${baseName}Vertical`] : base;

              return {
                left: typeof style?.[`${baseName}Left`] === "number" ? style[`${baseName}Left`] : horizontal,
                top: typeof style?.[`${baseName}Top`] === "number" ? style[`${baseName}Top`] : vertical,
                right: typeof style?.[`${baseName}Right`] === "number" ? style[`${baseName}Right`] : horizontal,
                bottom: typeof style?.[`${baseName}Bottom`] === "number" ? style[`${baseName}Bottom`] : vertical,
              };
            }

            function formatAxisLabel(axis, value, insetStart, insetEnd, boxSizing) {
              if (typeof value !== "number") {
                return null;
              }

              if (boxSizing === "content-box") {
                return `${axis}${value} -> outer${Math.max(value, insetStart + insetEnd)}`;
              }

              return `${axis}${value} -> content${Math.max(0, value - insetStart - insetEnd)}`;
            }

            __NODE_IMPLEMENTATION__

            function App() {
              return (
                <Scene backgroundColor="#0b1020">
                  <View style={__HOST_ROOT_STYLE__}>
            __LAYOUT_SOURCE__
                  </View>
                </Scene>
              );
            }

            mountNativeApp(App);
            """;
        return wrapper
            .Replace("__NODE_IMPLEMENTATION__", nodeImplementation, StringComparison.Ordinal)
            .Replace("__HOST_ROOT_STYLE__", "{ left: 0, top: 0, right: 0, bottom: 0, padding: 24 }", StringComparison.Ordinal)
            .Replace("__LAYOUT_SOURCE__", sourceText, StringComparison.Ordinal);
    }

    private void RunEsbuild()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = esbuildExecutablePath,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(generatedSourcePath);
        startInfo.ArgumentList.Add("--bundle");
        startInfo.ArgumentList.Add("--format=esm");
        startInfo.ArgumentList.Add("--platform=node");
        startInfo.ArgumentList.Add("--target=es2022");
        startInfo.ArgumentList.Add("--packages=external");
        startInfo.ArgumentList.Add($"--outfile={generatedEntryPath}");
        startInfo.ArgumentList.Add("--jsx-factory=React.createElement");
        startInfo.ArgumentList.Add("--jsx-fragment=React.Fragment");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start esbuild process.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"esbuild failed for '{sourcePath}'.\n{standardOutput}\n{standardError}".Trim());
    }

    private static string ToModuleSpecifier(string fromDirectory, string targetPath)
    {
        var relativePath = Path.GetRelativePath(fromDirectory, targetPath).Replace('\\', '/');
        return relativePath.StartsWith(".", StringComparison.Ordinal)
            ? relativePath
            : $"./{relativePath}";
    }

    private static string ResolveEsbuildExecutablePath(string rootDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            var windowsCommandPath = Path.Combine(rootDirectory, "node_modules", ".bin", "esbuild.CMD");
            if (File.Exists(windowsCommandPath))
                return windowsCommandPath;

            var windowsExecutablePath = Path.Combine(
                rootDirectory,
                "node_modules",
                ".pnpm",
                "@esbuild+win32-x64@0.28.0",
                "node_modules",
                "@esbuild",
                "win32-x64",
                "esbuild.exe");
            if (File.Exists(windowsExecutablePath))
                return windowsExecutablePath;
        }
        else
        {
            var unixCommandPath = Path.Combine(rootDirectory, "node_modules", ".bin", "esbuild");
            if (File.Exists(unixCommandPath))
                return unixCommandPath;
        }

        throw new FileNotFoundException("Could not find a local esbuild executable. Run 'pnpm install' in the layout visualizer project first.");
    }
}
