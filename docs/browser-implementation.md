# Browser Implementation Architecture

This document describes how Enaga implements browser-like behavior for loading, scripting, layout, rendering, and native presentation. It is intended for readers who already understand HTML, CSS, JavaScript, and parsing concepts, so it focuses on Enaga-specific architecture, technology choices, integration boundaries, and known gaps.

For implementation-level mechanics and compatibility workarounds, see `docs\browser-implementation-details.md`.

Enaga is not a wrapper around an existing browser engine. It is a custom renderer and browser-adjacent runtime built on .NET, with selected third-party libraries for parsing, graphics, and native windowing.

## Scope

The browser implementation currently exists as a set of reusable libraries plus `examples\SampleBrowser`, which is a native sample shell for loading and interacting with documents. The core responsibilities are split across:

| Area | Main project(s) | Responsibility |
| --- | --- | --- |
| Document loading | `src\Enaga.Html.Loader` | Local/HTTP document loading, stylesheet loading, source resolution, text decoding. |
| DOM representation | `src\Enaga.Html.Dom` | DOM snapshot model, id/node indexing, mutation helpers, serialization. |
| CSS parsing and style resolution | `src\Enaga.Html.Css`, `src\Enaga.Html.Layout` | CSS parsing, selector matching, cascade, computed style, pseudo-state snapshots. |
| HTML-to-scene conversion | `src\Enaga.Html`, `src\Enaga.Html.Layout` | Converts DOM/style data into Enaga scene/layout nodes. |
| Layout | `src\Enaga.Layout`, `src\Enaga.Html.Layout` | Custom layout engine and HTML-specific layout mapping. |
| Rendering | `src\Enaga.Rendering`, `src\Enaga.Rendering.Skia` | Scene painting, image/font resource loading, Skia drawing. |
| Native presentation | `src\Enaga.Windowing.Silk`, platform projects | Native window, GPU surface, input, frame pump. |
| Script runtime | `src\Enaga.Browser`, `okojo` | Okojo-backed JavaScript execution, DOM bindings, timers, fetch, script-driven mutation. |
| Sample browser UI | `examples\SampleBrowser` | Toolbar, history, navigation, file watching, browser-shell wiring. |

## Technologies used

### Runtime and host platform

- **.NET 10 / C#** is the implementation language and runtime.
- **Native windows** are provided through Enaga platform/windowing layers, primarily `Enaga.Windowing.Silk`.
- **GPU-backed presentation** uses Silk.NET OpenGL/Vulkan-facing infrastructure and Skia surfaces.
- **A custom scene graph** (`Enaga.Scene`) is the intermediate representation between document layout and painting.

### Rendering stack

- **SkiaSharp** is used for 2D drawing and text/image painting.
- **Svg.Skia** is used for SVG decoding/rasterization paths.
- **Silk.NET** is used for window creation, input plumbing, and graphics API integration.

### Parsing and scripting

- **AngleSharp** is used where Enaga needs robust HTML parsing for DOM construction and fragment parsing.
- **Enaga.Html.Css** is a custom CSS parser/resolver layer for the supported CSS subset.
- **Okojo** is a custom-built JavaScript engine and host/runtime stack. It is part of this repository under `okojo\` and is not an external browser engine.
- **Okojo.Hosting** and **Okojo.WebPlatform** provide host task queues, promises/tasks integration, and selected web-platform primitives.

### Networking

- **System.Net.Http.HttpClient** with `SocketsHttpHandler` is used for document, stylesheet, script, fetch, image, and font requests.
- Browser-like request headers are added by Enaga for several resource types, including User-Agent, Accept, Accept-Language, Referrer, and selected `Sec-Fetch-*` headers.

## Custom-built components

The following are custom-built in this codebase:

- **Okojo**: JavaScript parser, compiler, VM/runtime, object model, host task scheduling integration, and selected web-platform APIs.
- **HTML renderer pipeline**:
  - DOM-to-scene tree mapping.
  - HTML-specific computed style resolution.
  - HTML layout tree generation.
  - Form control rendering and interaction state.
  - Link hit-testing and navigation dispatch.
- **CSS engine subset**:
  - Selector matching for the supported selector subset.
  - Cascading and computed style mapping.
  - CSS property parsing for the supported visual/layout subset.
- **Layout engine**:
  - Custom layout model with web-oriented defaults and HTML-specific formatting adaptations.
  - Fragment/hit-test data used by pointer interaction and invalidation.
- **Scene graph and rendering abstraction**:
  - `SceneLayoutCommit`, `SceneLayoutBox`, and scene node kinds.
  - Skia-backed painter for text, boxes, images, controls, scrollbars, and shadows.
- **Browser shell behavior**:
  - SampleBrowser navigation history.
  - URL entry and toolbar overlay.
  - Script runtime wiring and DOM mutation repaint integration.
  - Native input forwarding to renderer controls.

Third-party libraries are used for well-defined infrastructure tasks, but browser behavior is not delegated to Chromium, WebKit, Gecko, WebView2, or an embedded browser control.

## High-level loading and rendering flow

1. `SampleBrowser` asks `HtmlDocumentLoader` to load a local file or HTTP(S) document.
2. The loader resolves the base URL/path, decodes text, and loads linked stylesheets.
3. `HtmlDocumentParser` builds the DOM snapshot and extracts style/script metadata.
4. `HtmlStyleTraversal` resolves computed styles for DOM elements.
5. `HtmlSceneTreeBuilder` converts styled DOM nodes into Enaga HTML scene nodes.
6. `HtmlLayoutBuilder` computes layout and emits a `SceneLayoutCommit`.
7. `SceneCommitPainter` paints the commit through SkiaSharp.
8. `Enaga.Windowing.Silk` presents the rendered surface in a native window and forwards pointer/keyboard input back into `HtmlSceneFrameSource`.

Script-enabled documents add this loop:

1. `HtmlBrowserScriptRuntime` creates an Okojo realm.
2. Browser-like globals and DOM wrappers are installed.
3. Classic scripts are executed.
4. DOM mutations produce a new `HtmlDocument` snapshot.
5. The host updates `HtmlSceneFrameSource`, causing the renderer to rebuild/repaint.

## Native rendering model

Enaga renders into native surfaces, not into a web `<canvas>` and not through a DOM renderer. The browser-like document is converted into Enaga scene/layout data and then painted by Skia:

- Boxes, borders, backgrounds, shadows, text, images, controls, and scrollbars are represented as scene layout boxes.
- Images and fonts are resolved by rendering resource caches.
- Pointer hit testing uses layout/fragment data from the generated scene commit.
- The sample browser window is only a host shell; the reusable rendering pipeline lives below it.

This architecture also keeps window rendering and offscreen texture rendering as possible targets, because the document pipeline produces scene commits rather than platform-specific UI controls.

## JavaScript and DOM integration

`Enaga.Browser.HtmlBrowserScriptRuntime` embeds Okojo and exposes a minimal browser-like environment:

- `window`, `self`, `document`, `location`, `navigator`, `console`, `fetch`, timers, and selected DOM APIs.
- `localStorage` and `sessionStorage` with synchronous Web Storage-style `length`, `key(index)`, `getItem`, `setItem`, `removeItem`, and `clear`.
- `Worker` backed by Okojo worker agents, background hosts, worker message queues, `postMessage`, `onmessage`, module imports, `SharedArrayBuffer`, and `Atomics`.
- Classic inline and external scripts when script execution is enabled.
- `onclick` and `addEventListener("click", ...)` dispatch for host-reported clicks.
- DOM mutation APIs such as `textContent`, `innerText`, `innerHTML`, `value`, `setAttribute`, `removeAttribute`, `appendChild`, and `insertBefore`.
- `href="javascript:..."` execution through the Okojo host task path.
- Timers (`setTimeout`, `clearTimeout`, `setInterval`, `clearInterval`) through Enaga's host scheduler.

The DOM runtime is intentionally small. It is sufficient for many simple pages and saved-page scripts, but it is not a complete browser DOM implementation.

## HTTP loading and resource fetching

### Documents and stylesheets

`HtmlDocumentLoader` supports:

- Local files.
- HTTP and HTTPS documents.
- Relative resource resolution from the loaded document base.
- BOM and declared encoding handling.
- Inline styles and linked stylesheets.
- Auto redirects.
- GZip, Deflate, and Brotli decompression.
- Browser-like default headers:
  - User-Agent.
  - Accept.
  - Accept-Language derived from `CultureInfo.CurrentUICulture`.

The loader uses a shared `HttpClient` created with `SocketsHttpHandler`.

### Scripts and `fetch`

`HtmlBrowserScriptRuntime` has its own shared script/fetch `HttpClient`:

- External classic scripts are fetched with script-oriented Accept headers.
- `fetch` supports local file reads and HTTP(S) reads.
- `fetch` supports method, body, and object-style headers from the init object.
- The response object currently includes:
  - `ok`
  - `status`
  - `statusText`
  - `url`
  - `headers.get(name)`
  - `headers.has(name)`
  - `text()`
  - `json()`
  - `arrayBuffer()`

This is a pragmatic fetch subset. It does not yet implement the complete Fetch Standard.

### Images

`Enaga.Rendering.Skia.WebImageCache` loads remote images with:

- A browser-like User-Agent.
- Image-oriented Accept headers, including AVIF, WebP, SVG, and generic image fallbacks.
- GZip, Deflate, and Brotli decompression.
- Disk caching under the local application data directory.
- Asynchronous download and repaint invalidation when resources become available.

SVG images are decoded through the SVG/Skia path.

### Fonts

`WebFontCache` can download remote font resources and cache them on disk with an eviction policy. It is a renderer resource cache rather than a complete browser font loading implementation.

## Cookie support

Cookie support exists, but it is limited and should be understood as transport-level support rather than a full browser cookie subsystem.

Current behavior:

- Document loading uses `SocketsHttpHandler` with a `CookieContainer` and `UseCookies = true`.
- Script and `fetch` requests use a separate `SocketsHttpHandler` with its own `CookieContainer` and `UseCookies = true`.
- Redirects and normal `Set-Cookie` / `Cookie` handling are delegated to .NET's HTTP stack for those clients.
- `HtmlDocumentLoader` has a one-hop "cookie gate" retry path: if a document response sets cookies and returns a page containing a single same-document link, the loader can follow that link once.
- `navigator.cookieEnabled` is exposed as `true`.

Important limitations:

- There is no implemented `document.cookie` API.
- Cookies are not exposed to script as a cookie store.
- Cookies are not persisted as a browser profile across process lifetimes.
- Document loading and script/fetch have separate HTTP clients and therefore separate cookie containers.
- Image/font resource cookie behavior is not modeled as a browser-grade resource cookie policy.
- There is no storage partitioning, third-party cookie policy, SameSite enforcement layer, or user-facing cookie management UI beyond what .NET applies at request time.
- There is no full browser origin model around cookies.

## Storage support

Enaga currently supports a small, synchronous Web Storage subset:

- `localStorage`
- `sessionStorage`
- `length`
- `key(index)`
- `getItem(key)`
- `setItem(key, value)`
- `removeItem(key)`
- `clear()`

`sessionStorage` is scoped to one `HtmlBrowserScriptRuntime` instance. `localStorage` is shared in process by resolved origin, so separate runtime instances for the same HTTP(S) origin can see the same values.

Important limitations:

- Storage is in-memory only.
- Storage is not persisted to disk.
- There is no quota enforcement.
- There are no `storage` events.
- Direct property access such as `localStorage.name = "value"` is not modeled as storage mutation.
- File-document origin behavior is a pragmatic directory-based grouping, not a full browser origin model.

## Worker support

Enaga enables Okojo's Web Worker support in the browser runtime:

- `new Worker(url)` and `new Worker(url, { type: "module" })`.
- Document-relative local and HTTP(S) worker script loading.
- Worker module imports resolved relative to the worker module URL.
- Main-to-worker and worker-to-main `postMessage`.
- `onmessage` on both the `Worker` wrapper and worker global scope.
- Okojo's structured-clone subset for message payloads.
- `SharedArrayBuffer` and `Atomics` from Okojo's multi-agent runtime.
- Background worker hosts so worker agents can process messages independently from the main realm.

Important limitations:

- Worker scripts are evaluated through Okojo's module loader. Classic worker syntax that is also valid in modules works, but full classic-worker semantics are not implemented.
- Shared workers are not implemented.
- Service workers are not implemented.
- Worker `addEventListener`, `removeEventListener`, `MessageEvent` parity, `importScripts`, dedicated worker lifecycle events, and browser security policy checks are incomplete.
- Worker `fetch` and timers are inherited from the current runtime modules but are not yet a full browser worker environment.
- There is no CORS, CSP, origin isolation, COOP/COEP, or cross-origin worker policy enforcement.

## Navigation model

`SampleBrowserDocumentController` provides a simple navigation shell:

- URL entry.
- Link activation.
- Back, forward, and refresh.
- In-memory history list.
- `mailto:` and `tel:` external handoff.
- `javascript:` URL execution.
- `location.href` assignment and `location.replace(...)` navigation requests from scripts.

This is not yet equivalent to the browser History API. It is a host-side sample browser history stack.

## Current support boundaries

The project intentionally implements browser behavior incrementally. The following sections summarize notable unsupported or partial areas.

### Storage and persistence not yet supported

Not currently implemented:

- IndexedDB
- Cache API
- Origin-private file system
- Persistent browser profiles
- Persistent cookies as a user-visible browser profile
- Quota management

### Network and security not yet supported

Not currently implemented or incomplete:

- Complete Fetch Standard semantics.
- CORS enforcement.
- Referrer policy.
- Content Security Policy.
- Subresource Integrity.
- Mixed content blocking.
- Permissions Policy.
- Service workers.
- Shared workers.
- WebSocket.
- WebRTC.
- HTTP cache semantics.
- Download handling and MIME-sniffing policy.
- Full origin/site isolation model.

### DOM and events not yet complete

Partial or missing:

- Full selector engine for DOM APIs.
- Full event propagation and event phases.
- `preventDefault`, `stopPropagation`, and default action modeling beyond narrow cases.
- Keyboard/input/change/submit event parity.
- `MutationObserver`.
- Shadow DOM.
- Custom elements.
- Full HTML form submission algorithm.
- Full focus management.
- Full selection APIs.
- Full CSSOM and live style declarations.

### Layout, CSS, and rendering not yet complete

Partial or missing:

- Complete CSS selector support.
- Complete CSS cascade/inheritance/property coverage.
- Grid layout.
- Full table layout.
- Full inline formatting parity.
- Full font fallback/shaping parity with browsers.
- CSS animations and transitions.
- Canvas 2D and WebGL.
- Video/audio elements.
- Accessibility tree.
- Printing.

### JavaScript runtime compatibility

Okojo is custom-built and actively evolving. It supports enough modern JavaScript for the current browser experiments, but it is not yet at full browser-engine parity. Work continues in:

- Parser compatibility for modern syntax.
- VM/runtime correctness.
- Host task/event-loop behavior.
- Web-platform API coverage.
- Node-like compatibility for local React/runtime work.

## Relationship to React support

The repository also contains React/Okojo runtime projects. The architectural direction is to render React into Enaga's native scene/runtime model rather than through a web DOM/canvas path. The browser-like HTML renderer and the React runtime share lower-level concepts such as scene layout, input, rendering, and Okojo integration, but they are separate surfaces.

## Practical expectations

For external consumers, the current browser work should be treated as:

- A native HTML/CSS/JS rendering experiment.
- A reusable set of document loading, layout, rendering, and scripting components.
- A sample browser shell for exercising those components.
- Not a complete secure browser engine.
- Not a replacement for Chromium/WebKit/Gecko for arbitrary web compatibility.

The most mature parts are native scene rendering, Skia presentation, document loading, partial CSS/layout, basic form controls, image/SVG rendering, and Okojo-backed script execution for simple pages. The least mature parts are browser security policy, persistent storage, full DOM/event parity, and long-tail web-platform APIs.
