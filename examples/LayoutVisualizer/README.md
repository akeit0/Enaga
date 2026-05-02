# Native Layout Visualizer

Edit only `layout.jsx`.

The host compiles that JSX file to JS on demand, watches the source file, and reloads the app module without rebuilding the whole JS runtime.

The native layout path now supports `flexBasis`, `flexGrow`, `flexShrink`, `flexWrap: "wrap"`, and `position: "absolute" | "relative" | "static"` for quick layout experiments in this single-file workflow.

When `position` is omitted, the native host now defaults to **`relative`**. Host apps can switch that omitted-position default to CSS-style **`static`** with `defaultPositionMode: DefaultPositionMode.Static` when constructing `OkojoNodeReactHost`.

By default the visualizer runs in **normal** mode. Use `--debug` to draw the debug content-box overlay and right-side labels for each `Node`.

## Run

```sh
cd examples/LayoutVisualizer
pnpm install
dnrelay run ./Enaga.LayoutVisualizer.csproj
```

## Debug mode

```sh
dnrelay run ./Enaga.LayoutVisualizer.csproj -- --debug
```

## Custom source file

```sh
dnrelay run ./Enaga.LayoutVisualizer.csproj -- --layout-source ./my-layout.jsx
```

## Fallback bundled entry

```sh
dnrelay run ./Enaga.LayoutVisualizer.csproj -- --react-entry ./dist/react-entry.mjs
```
