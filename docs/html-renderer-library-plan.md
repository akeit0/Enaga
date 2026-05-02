# HTML renderer library plan

## Purpose

`src/Enaga.Html` started as a viewer-oriented spike, but it is now large enough that the core should move toward a reusable native HTML/CSS rendering library. The project has two equal tracks:

- an efficient React renderer that is free to follow React Native-style primitives where that gives better native performance and host control;
- a web-compatible HTML path that can render practical HTML/CSS content and can later support React mounted through HTML-like elements.

The target is not a full browser engine in the short term. The target is a native scene renderer that accepts useful web content, builds an explicit DOM/style/layout model, and emits `SceneLayoutCommit` data for Skia-backed window and texture hosts.

## Current state

- HTML parsing uses AngleSharp and CSS parsing uses ExCSS.
- The renderer already maps a useful subset into `Enaga.Scene` nodes: blocks, flex rows/columns, text, images, scroll containers, inputs, textareas, hover, and basic form editing.
- Layout currently reuses `Enaga.Core` primitives, which keeps HTML and React rendering on the same scene/painter path.
- The initial implementation is still tightly coupled: parsing, style matching, layout tree creation, and scene emission mostly happen in `HtmlDocumentSceneBuilder`.
- React/Okojo already has shared scene primitives and scrollbar geometry; HTML should reuse those instead of adding a parallel widget model.

## Problems to solve

- Parse and build are coupled, so repeated viewport/hover rebuilds can redo work that should be cached.
- The current DOM shape is implicit in AngleSharp objects, which makes renderer-owned optimizations and mutation tracking difficult.
- Selector support is intentionally tiny and cannot scale cleanly while it stays embedded in scene building.
- CSS computed style is mutable and monolithic, which is convenient for the spike but hard to optimize or validate against web behavior.
- The public surface does not yet distinguish library input, parsed document state, style state, layout state, and scene output.

## Architecture direction

1. **DOM and resource model**
   - Parse HTML once into renderer-owned DOM nodes.
   - Preserve element names, attributes, text nodes, source order, and embedded `<style>` text.
   - Keep AngleSharp as an adapter, not as the internal document model.
   - Add a stable node identity strategy before supporting incremental document updates.
   - Preserve document base paths so `img src`, stylesheet URLs, and `a href` values resolve like authored HTML rather than process working-directory paths.

2. **CSS and cascade**
   - Split selector parsing/matching from computed style application.
   - Add support in small steps: descendant selectors, child selectors, attribute selectors, pseudo-classes needed by native interaction, then media/container-like host queries.
   - Track specificity and source order explicitly.
   - Keep unsupported declarations observable through diagnostics instead of silently hiding all gaps.

3. **Layout**
   - Keep using shared `Enaga.Core` layout primitives where they match the needed behavior.
   - Add HTML-specific layout adapters for block formatting, inline text flow, replaced elements, and form controls.
   - Avoid pushing browser-specific assumptions into shared core unless React and native scene layout both benefit.

4. **Rendering**
   - Continue emitting `SceneLayoutCommit` first.
   - Treat Skia painting as a separate backend concern.
   - Keep window and offscreen texture targets equally important.
   - Put reusable scene metadata, hit testing, scrollbar geometry, and asset URL resolution in shared projects when both React and HTML need them.

5. **Performance**
   - Cache parsed DOM and stylesheet data across layout rebuilds.
   - Cache selector match results and computed styles by document version and interaction state.
   - Add layout invalidation scopes before attempting broad incremental rendering.
   - Add benchmarks for parse, cascade, layout, and scene emission independently.

6. **Compliance and scope**
   - Define a documented supported subset rather than implying full browser parity.
   - Use focused web-platform-like fixtures for behavior that is intentionally supported.
   - Add visual/regression samples for controls, text wrapping, flex, images, scrolling, and forms.

## Milestones

1. Extract renderer-owned DOM and cache parsed documents.
2. Move selector/cascade code into dedicated types with diagnostics.
3. Split layout tree construction from scene commit emission.
4. Add parse/cascade/layout benchmarks.
5. Expand selector support to descendant and child combinators.
6. Add inline text runs and stronger block formatting behavior.
7. Add image sizing, font loading, and asset resolution hooks.
8. Stabilize public library APIs and keep `examples/SampleBrowser` as only one host.

## Near-term feature work

- **Images**: resolve relative `src` values against the HTML document, honor `width`/`height` attributes, keep `object-fit` routed to the shared Skia image path, and add image sizing fixtures.
- **Links**: preserve `href` on anchor elements and descendants, emit link metadata in scene boxes, expose activation from `HtmlSceneFrameSource`, and later route navigation through a host-provided policy.
- **Scrollbar drag**: reuse `SceneScrollBarLayout` from core so HTML scroll views can drag vertical and horizontal thumbs consistently with the React/Okojo path.
- **Shared extraction**: do not preserve current namespaces or project boundaries when they block shared React/HTML infrastructure. Prefer moving common scene input and geometry deeper into core over duplicating it.
- **Future React HTML path**: keep the DOM/cascade/layout work independent enough that a future React host can mount HTML-like elements into the same HTML renderer instead of only the React Native-style primitive set.

## Immediate work

- Introduce an internal DOM model inside `Enaga.Html`.
- Parse `Enaga.HtmlDocument` into that DOM once per document update.
- Let viewport, hover, and text-input rebuilds reuse the parsed document.
- Replace project-specific `sample.html` copy with unrelated fixture content.
- Add first-pass image URL resolution, anchor link activation, and scrollbar thumb dragging.
- Keep validation through existing `HtmlSceneFrameSourceTests` and `dnrelay test`.
