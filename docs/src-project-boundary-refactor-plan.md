# src project boundary refactor plan

## Goal

The current `src` tree mixes reusable scene/runtime code, Skia-backed measurement, React/Okojo host behavior, and HTML-specific document behavior. The goal is to make the project boundaries explicit so the native React renderer and the HTML renderer share the same low-level runtime infrastructure without forcing either path through the other's assumptions.

Backward compatibility is not a constraint for this cleanup. Prefer the cleaner boundary when APIs need to move or change.

## Current boundaries

- `Enaga.Core`
  - Owns scene records/store, backend service interfaces, and diagnostics contracts.
  - Should remain free of Skia, HTML, Okojo, and windowing dependencies.
- `Enaga.Layout`
  - Owns backend-neutral layout primitives and the flex/stack layout calculator.
  - Depends on `Enaga.Core` service contracts only.
- `Enaga.Input`
  - Owns input sinks and shared scene interaction policy: wheel latching, smooth scroll, screen geometry, scroll metrics, and scrollbar dragging.
  - Depends on `Enaga.Core` scene records only.
- `Enaga.Rendering`
  - Owns renderer-facing backend-neutral contracts and utilities: frame source, damage tracking/estimation, dirty regions, diagnostics, wake events, and low-level repaint matching.
  - Depends on `Enaga.Core` only.
- `Enaga.Rendering.Skia`
  - Owns painting, image decoding/cache, font catalog, resolved Skia font data, and Skia text measurement.
  - Exposes runtime backend services and should keep Skia-specific measurement, font fallback, and image resolution out of core/layout projects.
- `Enaga.React.OkojoRuntime`
  - Owns JS/Okojo integration and React host state.
  - It still has text-input logic that overlaps with HTML host behavior.
- `Enaga.Html`
  - Owns HTML parsing, CSS cascade, DOM/style/layout adaptation, HTML input behavior, and HTML scene frame source.
  - It still contains reusable text-input behavior that should move down.

## Problems

1. `IRuntimeTextServices` is both a high-level text-input service and a low-level measurement service. Span width/break APIs exist and styles now carry a backend-neutral `SceneFont`, but measurement and caret/editing APIs still remain coupled.
2. `Enaga.Html` and `Enaga.React.OkojoRuntime` still duplicate text input editing, composition, and caret movement policies around the same `SceneLayoutBox` model.
3. `HtmlDocumentSceneBuilder` owns parsing, cascade, layout request creation, layout measurement, and scene emission. This makes it hard to cache or benchmark each phase.
4. Font identity must stay explicit at scene/layout boundaries. `SceneTextStyle` should carry a `SceneFont` descriptor; Skia resolves that descriptor to cached platform font data (`SKTypeface`, `SKFont`, and metrics), but core/layout must not depend on Skia types or string-only family lookup.

## Target structure

The dependency direction is now:

```text
Enaga.Core (scene records + backend-neutral service contracts)
    <- Enaga.Layout (layout primitives/calculator)
    <- Enaga.Html.Layout (HTML layout/cache)
Enaga.Core
    <- Enaga.Input (input sinks + shared scene scroll interaction)
Enaga.Core
    <- Enaga.Rendering (frame/damage/rendering contracts)
        <- Enaga.Rendering.Skia
    <- Enaga.Html scene emission / frame source / app host
```

The layout engine should be usable without a renderer. Rendering consumes a layout commit; it should not own DOM style resolution, block/inline/table layout, scroll policy, or text measurement caches.

### Enaga.Core

Keep only backend-neutral scene/runtime contracts that other layers can share:

- `Scene/*`
  - Scene records, scene store, mutations, paint data, `SceneFont`, text styles, and layout commit records.
- `Rendering/RuntimeBackendServices`
  - Backend-neutral text/image service contracts used by layout, input, Skia, HTML, and Okojo.
- `Hosting/RuntimeDiagnostics`
  - Shared diagnostics sink contracts.

### Enaga.Layout

- Owns shared layout primitives and algorithms that are not HTML-specific.
- Exposes `LayoutChildRequest`, flex/position enums, `LayoutEngineConfig`, and `LayoutCalculator`.
- Depends on `Enaga.Core` service contracts only; it does not depend on Skia, HTML, Okojo, input, or windowing.

### Enaga.Input

- Owns input-facing contracts and shared scene interaction policy.
- Contains `IInputSink`, pointer cursor/text-composition contracts, wheel target latching, smooth scroll state, scroll clamping, screen bounds resolution, scrollbar metrics, and scrollbar drag state.
- Works only with `SceneLayoutCommit`, `SceneLayoutBox`, and small mutable state interfaces.

### Enaga.Rendering

- Owns rendering contracts and frame/damage utilities that are backend-neutral but renderer-facing.
- Contains `ISceneFrameSource`, render wake/diagnostics contracts, dirty region tracking, damage estimation, and low-level repaint request matching.
- Depends on `Enaga.Core`, not Skia, HTML, Okojo, input, or windowing.

### Enaga.Rendering.Skia

- Resolve `SceneFont` descriptors to `SKTypeface`/`SKFont` inside the Skia boundary.
- Treat `SceneFont` as the browser-style font description: family/source/identity, weight, italic, and size all participate in selecting resolved font data.
- Cache resolved Skia font data separately from text layout. The cache owns the long-lived `SKTypeface`, `SKFont`, and metrics; callers lease font data for lifetime-safe access without per-measurement `SKFont` allocation or shared-font serialization locks.
- Implement text width and breaking with `SKFont.MeasureText(ReadOnlySpan<char>)` and `SKFont.BreakText(ReadOnlySpan<char>, ...)`.
- Implement text height as a measurement-only path using resolved Skia font metrics, explicit line break counting, and width-constrained wrap counting.
- Keep font fallback inside Skia. Core should not know about typefaces.
- Keep text layout caching here until a backend-neutral layout abstraction is needed.

### Enaga.Html

Split into smaller internal modules before moving projects:

- `HtmlDomParser`: AngleSharp adapter into renderer-owned DOM.
- `HtmlCssParser` / `HtmlStyleSheet`: CSS parse and selector storage.
- `HtmlCascade`: selector matching and computed style.
- `HtmlLayoutAdapter`: DOM/style tree to shared layout requests.
- `HtmlSceneEmitter`: `SceneStore`/`SceneLayoutCommit` emission.
- `HtmlFrameSource`: host state, resource invalidation, input routing.

After these boundaries are clear, `Enaga.Html` can become a reusable library rather than a viewer spike.

### HtmlLayout

Move HTML-specific layout into its own layer once the internal split is stable:

- DOM/style to layout tree adaptation.
- HTML block/inline/table/list/form layout behavior.
- CSS unit and percentage resolution that needs HTML computed style context.
- HTML layout caches, including selector/cascade dependencies and wrapped inline text caches.

This layer may depend on `Enaga.Layout` and `IRuntimeTextServices`, but it should not depend on Skia, the native window host, Okojo, or sample viewer code.

### Renderer/host layer

The renderer layer should be left with:

- Resource loading and invalidation.
- Scene emission from resolved layout output.
- Input routing to shared interaction controllers.
- Cursor, link activation, focus, and host integration.

Scrollbar geometry, wheel latching, smooth scrolling, screen bounds resolution, and scrollbar drag state now live in `Enaga.Input` because they operate on scene data and are shared by both HTML and React/Okojo paths.

## Immediate execution order

1. Separate `IRuntimeTextServices` into lower-level measurement and higher-level editing/caret services.
   - `Enaga.Layout` should depend only on an `IRuntimeTextMeasurer`-style contract: `SceneFont` line height, span width, break count, and text height.
   - Caret hit testing, vertical movement, composition, and editing policy should move to a text-input layer used by HTML and React/Okojo hosts.
2. Keep shrinking `HtmlLayoutBuilder` by extracting a pure frame resolver from the traversal coordinator.
3. Split `Enaga.Html.DomCss` after DOM/CSS/parser types have stable public inputs and outputs.
4. Move host-specific input notification and text-editing policy into a dedicated input/text layer once React/Okojo and HTML share the same behavior.

## Completed refactor slices

- Span-first text measurement is available through `IRuntimeTextServices`, with Skia implementations using span-based `SKFont` APIs.
- `SceneTextStyle` now carries a backend-neutral `SceneFont`; legacy family/weight/italic accessors are derived from that descriptor, and layout requests can carry `SceneFont` through to measurement.
- Skia text height measurement now uses font metrics and a measurement-only wrap/line-break counter instead of constructing full caret layout data through `TextInputMetrics.CreateLayout`.
- Skia font lifetime now follows a browser-like split: `SceneFont` is the requested font description, `TextFontCatalog` resolves platform typefaces, and `SkiaFontCollection` caches resolved font data plus metrics. Weight and italic are part of the description because they affect font selection, not because of any borrowing policy.
- Wheel target latching, screen bounds resolution, smooth scroll state, scrollbar metrics, and scrollbar drag state now live in `Enaga.Input`.
- `HtmlDocumentSceneBuilder` now has separated cache and measurement slices:
  - `HtmlDocumentSceneBuilder.Cache.cs`
  - `HtmlDocumentSceneBuilder.Measurement.cs`
  - `HtmlDocumentSceneBuilder.Units.cs`
- `Enaga.Html.Layout` now exists as the first library boundary for HTML document/style/layout work.
  - It owns linked DOM/CSS/computed-style/document-builder sources.
  - `Enaga.Html` references it and keeps frame source, input, clipboard, text input, resource invalidation, and viewer-facing host behavior.
- DOM/CSS/parser and intermediate layout models are now top-level internal types inside `Enaga.Html.Layout` instead of nested inside `HtmlDocumentSceneBuilder`.
- `HtmlSceneTreeBuilder` now owns styled-tree cache lookup and styled tree construction. The temporary `HtmlStyledSceneTreeFactory` indirection has been removed.
- Style resolution, URL resolution, and text normalization are now separate `Enaga.Html.Layout` services:
  - `HtmlStyleResolver`
  - `HtmlUrlResolver`
  - `HtmlTextNormalizer`
- `HtmlLayoutBuilder` now owns layout traversal, layout request creation, table/float measurement, and layout measurement caches.
- `HtmlDocumentSceneBuilder` is now a thin facade that coordinates parser output through `HtmlSceneTreeBuilder` and `HtmlLayoutBuilder`.
- `HtmlSceneEmitter` now owns `SceneStore` mutation and `HtmlSceneNode` to `SceneLayoutBox` conversion.
- `HtmlLayoutBuilder` now produces an explicit `HtmlLayoutResult`; `HtmlSceneEmitter` consumes that result and is no longer called during recursive layout traversal.
- Empty child arrays use C# `[]`, which lowers to `Array.Empty<T>()`.
- `HtmlLayoutBuilder` now uses a pass-local scratch arena for temporary `LayoutChildRequest` spans, frame spans, float buffers, and table row child buffers. The arena is rewound by layout-scope marks before the durable `HtmlLayoutResult` is emitted.
- Core src boundaries are now split at the csproj level:
  - `Enaga.Core`: scene records/store plus shared backend service and diagnostics contracts.
  - `Enaga.Layout`: backend-neutral layout primitives and calculator.
  - `Enaga.Input`: input sinks and shared scene scroll/scrollbar/screen-geometry interaction policy.
  - `Enaga.Rendering`: backend-neutral frame, damage, diagnostics, wake, and low-level repaint contracts.

## Next extraction slices

1. Split `HtmlLayoutBuilder` into a pure frame resolver and a traversal coordinator.
   - Current state still computes frames and records placed nodes recursively in one traversal.
   - The layout result boundary is explicit enough to make temporary buffer lifetimes safe, but frame resolving, table measurement, and recursive traversal are still bundled in one class.
2. Continue project-level splits around HTML once the layout traversal types are stable:
   - `Enaga.Html.DomCss`: DOM, CSS parser, selector/cascade.
   - `Enaga.Html.Layout`: HTML layout adapter/cache and text measurement use.
   - `Enaga.Html`: frame source, resource loading, interaction, viewer-facing API.
3. Move text measurement contracts out of `IRuntimeTextServices` now that text style/font identity is explicit.
   - Keep `IRuntimeTextMeasurer` backend-neutral and layout-friendly: all measurement inputs should use `SceneFont`/`SceneTextStyle`, never Skia types or raw family strings alone.
   - Keep Skia-specific font fallback and typeface/runs details inside `Enaga.Rendering.Skia`.
4. Move text-editing/caret contracts out of `IRuntimeTextServices` after HTML and React/Okojo text input state logic converge.

## Notes to revisit

- Span measurement with font fallback needs a richer result if a span contains mixed scripts. For now, the span API can use the resolved primary typeface; richer run-level measurement should stay behind the Skia boundary.
- `BreakText` now drives simple width-constrained height measurement. The next improvement is sharing a measurement-only wrap result with paint/text-input layout without leaking Skia types into `Enaga.Layout`.
- Scroll behavior should become state-machine based if more input modes are added; the current shared controllers already keep HTML and React/Okojo on the same scroll/scrollbar primitives.
