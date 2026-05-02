# enaga

Experimental C# project for rendering Html/Css or ReactNative directly.

See `docs/repository-refinement-suggestions.md` for a detailed repository/project cleanup direction, including the current Fast Refresh model and recommended ownership boundaries.

See `docs/browser.md` for the current native browser-layer functionality and TODOs.

The active implementation combines:

- local `okojo` sources for JS execution
- a small reusable browser layer in `src/Enaga.Browser`
- a reusable React runtime library in `lib/Enaga.React`
- a sample React app in `examples/SampleApp`
- layout visualizer examples in `examples/LayoutVisualizer`
- `SkiaSharp` rendering in a native window

## Current features

- React-driven native scene rendering
- HTML/CSS rendering and browser functionality with limited features

## Important paths

| Path | Purpose |
| --- | --- |
| `examples/SampleApp/Enaga.SampleApp/Program.cs` | cross-platform sample entry |
| `src/Enaga.Core/Enaga.Core.csproj` | reusable C# host library |
| `src/Enaga.Browser/Enaga.Browser.csproj` | reusable browser-adjacent JS/DOM runtime used by SampleBrowser |
| `src/Enaga.Html.Loader/Enaga.Html.Loader.csproj` | local/HTTP HTML document and stylesheet loading |
| `src/Enaga.Platforms.Windows/Enaga.Platforms.Windows.csproj` | Windows-specific injected host integration |
| `src/Enaga/Rendering/SceneRenderRoot.cs` | reusable scene-root adapter for custom C# sources |
| `tests/Enaga.Tests/Enaga.Tests.csproj` | library test project |
| `examples/SampleApp/Enaga.SampleApp.Windows/Program.cs` | sample Windows-only launcher |
| `examples/SampleApp/Enaga.SampleApp.Core/SampleAppRuntime.cs` | sample C# core bootstrap |
| `examples/SampleApp/Enaga.SampleApp.Core/SampleAppBridge.cs` | sample C# app-side <-> JS bridge |
| `lib/Enaga.React/src/native-runtime.tsx` | reusable React/runtime bridge |
| `lib/Enaga.React/src/react-okojo-fast-refresh.ts` | reusable dev-only React Fast Refresh bootstrap |
| `examples/SampleApp/src/app/sample-app.tsx` | sample UI |
| `examples/SampleApp/src/fast-refresh-entry.ts` | Fast Refresh entry |
| `examples/SampleApp/assets/` | sample-local image assets |
| `examples/LayoutVisualizer/layout.jsx` | single-file native visualizer source |
| `examples/LayoutVisualizer/Program.cs` | native visualizer host with source reload |
| `examples/LayoutVisualizer/Enaga.LayoutVisualizer.Web/src/App.jsx` | minimal browser visualizer |

## Prerequisites

- .NET SDK
- Node.js
- `pnpm`
- `dnrelay` (optional. replace dotnet to dnrelay if possible.)

## Build Submodule Okojo

This is needed on first (after clone or modify) build.

```sh
cd okojo
dotnet build . -c Release
```

## Build dotnet solution

dotnet build . -c Release

## Run browser

dotnet run -c Release --project ./examples/SampleBrowser

## Build React

```sh
cd examples/SampleApp
pnpm install
pnpm run build:react
```

The default native run expects that stable bundle to exist first.

For native React Fast Refresh during app-side development:

```sh
cd examples/SampleApp
pnpm install
pnpm run watch:react:fast-refresh

cd ../..
dotnet run --project ./examples/SampleApp/Enaga.SampleApp/Enaga.SampleApp.csproj -- --fast-refresh
```

`build:react` writes the stable default bundle to `dist/react-entry.mjs`.
`watch:react:fast-refresh` writes a module-preserving dev graph under `dist/fast-refresh/...`, with the sample entry at `dist/fast-refresh/examples/SampleApp/src/fast-refresh-entry.mjs`.
`--react-debug` now means host debug features/logging only; combine `--fast-refresh --react-debug` if you want both.

The split Fast Refresh graph is intentional. It preserves module boundaries for native React Fast Refresh instead of rebuilding one monolithic dev bundle on every edit.

## Build host

```sh
dotnet build ./examples/SampleApp/Enaga.SampleApp/Enaga.SampleApp.csproj
```

Native layout visualizer:

```sh
cd examples/LayoutVisualizer
pnpm install
dotnet run ./Enaga.LayoutVisualizer.csproj
```

Enaga.Browser layout visualizer:

```sh
cd examples/LayoutVisualizer/Enaga.LayoutVisualizer.Web
pnpm install
pnpm run dev
```

## Run host

```sh
dotnet run ./examples/SampleApp/Enaga.SampleApp/Enaga.SampleApp.csproj
```

## Test host

```sh
dotnet test tests/Enaga.Tests
```

## Check running

If usin dnrelay

```sh
dnrelay stats
```

## Extend from C #

The reusable C# side now centers on:

- `Enaga.Hosting.NativeWindowApp`
- `Enaga.Rendering.SceneRenderRoot`
- `Enaga.Rendering.ISceneFrameSource`
- `Enaga.Input.IInputSink`
- `Enaga.Scene.*` scene/store/mutation types

The practical sample now lives under `examples/SampleApp/`:

- `Enaga.SampleApp.Core` owns option parsing, the React entry selection, and a sample-specific generated globals bridge for C# app state.
- `Enaga.SampleApp` is the default cross-platform executable and uses the base-library platform integration on non-Windows hosts.
- `Enaga.SampleApp.Windows` is only the executable launcher and Windows platform injection.

App-side C# can provide its own `ISceneFrameSource` and optionally `IInputSink`, then run it through `SceneRenderRoot` and `NativeWindowApp`.

Platform-specific behavior is now meant to live outside the reusable host library. The default sample uses a minimal base-library integration on non-Windows hosts, while the Windows sample injects `Enaga.Platforms.Windows.WindowsNativeWindowPlatformIntegration` into `NativeWindowApp` for IME behavior and Windows text defaults.

## Image examples

Local file:

```tsx
<Image
  id="local-photo"
  source="assets/demo.jpg"
  left={20}
  top={20}
  width={240}
  height={160}
  fit="contain"
/>
```

Web URL:

```tsx
<Image
  id="remote-photo"
  source="https://picsum.photos/320/200"
  left={20}
  top={20}
  width={320}
  height={200}
  fit="cover"
  onLoad={(source) => console.log(`loaded ${source}`)}
  onError={(source, detail) => console.log(`failed ${source}: ${detail}`)}
/>
```

Remote images are cached under `%LocalAppData%/Enaga/image-cache`.

## Font examples

```tsx
configureFonts({
  defaultFamily: "Segoe UI",
  fallbackFamilies: ["Yu Gothic UI", "Meiryo", "Segoe UI Emoji", "Segoe UI Symbol"],
});

registerFont("Noto Sans JP", "https://example.com/fonts/NotoSansJP-Regular.ttf");

<Label
  text="こんにちは"
  left={20}
  top={20}
  width={240}
  fontFamily="Noto Sans JP"
/>
```

Remote fonts are cached under `%LocalAppData%/Enaga/font-cache`.

## Notes

- Relative asset paths resolve from the React entry or script directory.
- The reusable C# host library now lives under `src/Enaga/`.
- The React runtime library now lives under `lib/Enaga.React/`.
- The practical sample app now lives under `examples/SampleApp/`, split into a reusable C# core, a cross-platform entry, a Windows launcher, and the React sample app/files at the example root.
- Windows-only host behavior now lives in `Enaga.Platforms.Windows` and is injected into the Windows launcher instead of being hardcoded into the reusable core.
- The base library now includes a minimal default platform integration so non-Windows runs get sensible font defaults without the Windows package.
- The sample app is now organized under `examples/SampleApp/src/app/` as a small catalog with shared theme, shared UI, and per-page modules.
- The catalog now includes a communication page that shows sample-specific C# app globals and JS-to-host commands.
- Runtime animation is now opt-in from JS via `useAnimationLoop(...)`; non-animated apps can stay event-driven at the scene-source level.
- The shader source now lives on the TS side and is serialized into the native host as a runtime-effect spec, with reusable app-side shader helpers under `examples/SampleApp/src/app/shaders/`.
- Fast Refresh bootstrap now lives in `lib/Enaga.React/src/react-okojo-fast-refresh.ts`; the sample only opts into it through the debug entry and local `src/lib/` re-export shim.
- For a detailed repository cleanup and ownership proposal, see `docs/repository-refinement-suggestions.md`.
- For browser-layer functionality and TODOs, see `docs/browser.md`.
- If work needs `okojo` changes, coordinate that explicitly first.

## LICENSE

MIT
