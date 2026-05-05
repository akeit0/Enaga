# Browser Implementation Details

This document records implementation-level mechanics, compatibility shims, and deliberate workarounds in Enaga's browser stack. It is meant to answer **how specific browser-like behavior is implemented today**, not to describe the project at a high level.

## Scope

Primary implementation points:

- `src\Enaga.Browser\HtmlBrowserDocumentLoader.cs`
- `src\Enaga.Browser\HtmlBrowserDocumentLoadOptions.cs`
- `src\Enaga.Browser\HtmlBrowserScriptRuntime.cs`
- `src\Enaga.Browser\BrowserStorageArea.cs`
- `src\Enaga.Browser\BrowserStorageRegistry.cs`
- `src\Enaga.Browser\BrowserStorageJsBindings.cs`
- `src\Enaga.Browser\BrowserWorkerModuleLoader.cs`
- `src\Enaga.Html.Loader\*`
- `src\Enaga.Html.Dom\*`
- `src\Enaga.Html.Layout\*`
- `src\Enaga.Rendering.Skia\WebImageCache.cs`
- `src\Enaga.Rendering.Skia\WebFontCache.cs`

## Request compatibility shims

## Browser page loading boundary

### Page loading is now separate from JS runtime execution

`HtmlBrowserScriptRuntime` is meant to stay focused on:

- JS execution
- DOM bindings
- timers and host-task re-entry
- browser-style event-loop pumping

Document fetch/load policy now has a separate entry point:

- `HtmlBrowserDocumentLoader`

That loader owns the browser-facing "load a page, then optionally attach script runtime" flow:

1. normalize the requested source
2. load HTML and linked/explicit CSS through `HtmlDocumentLoader`
3. optionally create `HtmlBrowserScriptRuntime`
4. return `HtmlBrowserLoadedDocument`

The option split is deliberate:

- `HtmlDocumentHttpClientOptions` shapes main document and stylesheet HTTP requests
- `HtmlBrowserScriptRuntimeOptions` shapes runtime/network behavior after the document is loaded
- `HtmlBrowserDocumentLoadOptions` ties those together for browser-facing callers such as hosts and samples

This keeps sample applications from owning browser-page loading policy directly, while still leaving UI-only concerns such as toolbars, history presentation, and status documents outside the library.

### Browser-shaped User-Agent strings

Enaga intentionally uses browser-shaped User-Agent strings such as:

- `Mozilla/5.0 AppleWebKit/537.36 (KHTML, like Gecko) Enaga.Browser/1.0 Safari/537.36`

This is a compatibility workaround, not an identity claim. Many sites gate responses on UA sniffing and will serve reduced, blocked, or unusual content to unknown clients. A browser-shaped UA keeps Enaga on the common path more often.

Implementation notes:

- Keep an explicit `Enaga.Browser/...` token so logs and bug reports still identify Enaga traffic.
- Avoid claiming more specific browser brands unless the runtime is ready for the behavior that those tokens unlock.
- Different resource paths use different Accept headers, but the same general UA strategy.

Current locations:

- document/style loading
- external script loading
- `fetch`
- worker module loading
- image loading

The strings are intentionally browser-shaped but still Enaga-branded. That is an implementation compromise:

- browser-shaped enough to avoid obvious unknown-client code paths
- Enaga-branded enough that logs still show the traffic came from Enaga
- conservative enough not to claim a specific Chrome/Firefox/Edge version that would unlock behavior Enaga does not support yet

Current browser-runtime requests can override this through `HtmlBrowserScriptRuntimeOptions.RequestProfile`. The request profile currently carries the browser-facing identity/header defaults used consistently for:

- `navigator.userAgent`
- external script requests
- `fetch`
- worker module requests

`HtmlBrowserScriptRuntime` no longer owns those header defaults directly. The runtime delegates browser-style request shaping to `BrowserRequestProfile` + `BrowserNetworkSession`, which keeps request identity and cookie-bearing HTTP state separate from the JS/event-loop runner itself.

### Browser-like request headers

Requests are shaped to look browser-originated, not just generic `HttpClient` traffic. Depending on the resource type, Enaga adds combinations of:

- `Accept`
- `Accept-Language`
- `Referer`
- `Sec-Fetch-Dest`
- `Sec-Fetch-Mode`
- `Sec-Fetch-Site`

This is used to reduce server-side branching onto bot/non-browser code paths.

Inside the browser runtime, this shaping is now centralized instead of being spread across timer/DOM/runtime code:

- `BrowserRequestProfile` defines default browser-facing identity such as `User-Agent` and `Accept-Language`
- `BrowserHttpRequestFactory` applies those defaults to concrete requests
- `BrowserNetworkSession` owns the shared `HttpClient` and cookie container for script loading, `fetch`, and worker module loads

Implementation detail: these headers are **compatibility hints**, not the output of a full browser policy engine. In particular, `Sec-Fetch-*`, referrer handling, and same-origin/cross-origin behavior are still heuristic in several paths.

### Request clients are intentionally separate

Enaga does not currently route all network traffic through one unified browser network stack. Different areas use separate `HttpClient` instances and often separate cookie containers:

- document/style loading
- script loading and `fetch`
- worker module loading
- image loading
- font loading

This is simpler to evolve and test, but it means browser-like sharing behavior is incomplete. For example, a document request and an image request are not guaranteed to observe the same cookie jar or policy surface.

### Cookie-gate retry

`HtmlDocumentLoader` includes a targeted one-hop retry for cookie-gated pages:

1. load the initial document
2. observe `Set-Cookie`
3. if the response effectively points to a same-document gate link, follow it once

This is not a full browser navigation model. It is a narrow workaround for simple cookie-wall and interstitial patterns.

### Encoding handling favors "load something useful"

Document text decoding combines:

- BOM detection
- charset from HTTP headers
- charset declared in the document
- UTF-8 fallback

This is an implementation choice biased toward recovering a readable document rather than surfacing strict browser-parity decode states.

## DOM mutation strategy

### Mutations keep the DOM model as the renderer input

Current browser-runtime DOM mutation is DOM-model-oriented:

1. JavaScript mutates the custom DOM model
2. the runtime increments the DOM document version
3. `HtmlDocument` carries the live `HtmlDomDocument`
4. the host updates `HtmlSceneFrameSource` with that DOM-backed document
5. the renderer reparses style metadata from the DOM model and reuses layout/version state for dirty-node layout

`CurrentDocument.Html` still serializes from the DOM when callers ask for HTML text, which keeps tests/debugging and HTML export paths usable. Mutation notification itself no longer calls `HtmlDomDocument.ToHtml()` just to hand a new string snapshot to the renderer.

Practical consequence:

- JS-visible DOM and renderer input share the same node ids
- renderer updates caused by DOM mutation do not set the runtime-reload/full-document damage path
- existing scene/layout version stores can mark only affected nodes, ancestors, fragments, and hit-test data dirty
- serializing the whole document is now reserved for `HtmlDocument.Html` consumers, not the normal render wake path

### `innerHTML` uses fragment parsing, not ad-hoc string splitting

`innerHTML` assignment is implemented through HTML fragment parsing and DOM replacement, not manual token splitting. This was necessary because mixed/nested fragments were otherwise rendered as literal text or detached incorrectly.

Current shape:

- parse fragment with `HtmlDocumentParser.ParseFragment(...)`
- import parsed children into `HtmlDomDocument`
- rebuild node indexing so later JS access still works

This exists because string-based replacement broke real-world pages that use mixed text + nested tags inside `innerHTML`.

### JS wrapper identity is cached by DOM node id

`HtmlBrowserScriptRuntime` keeps a cache of JS wrapper objects keyed by `HtmlNodeId`. This is a practical identity layer so repeated DOM lookups usually return the same wrapper object for the same live node.

That cache follows the DOM node id lifetime:

- if a node survives mutations with the same node id, wrapper identity is preserved
- if a mutation replaces/imports nodes, new wrappers may be created

This works with the DOM-backed renderer path because wrapper cache keys and renderer node mapping both use `HtmlNodeId`.

## Event dispatch and click handling

### Click dispatch is host-driven

The renderer decides which `HtmlDomElement` was hit. The script runtime then:

1. resolves the current DOM element
2. creates a minimal event object
3. invokes `onclick`
4. invokes registered `"click"` listeners while walking ancestors

This is not a full DOM event system. It is a targeted implementation for the current pointer/click path.

Missing pieces by design:

- capture phase
- bubbling controls
- `preventDefault`
- `stopPropagation`
- composed paths
- rich pointer/keyboard event payloads

### Anchor activation is preserved separately from control handling

There is explicit logic to avoid clearing pending link activation just because pointer-up occurred on a descendant or nearby interactive path. This was added after anchor navigation regressed while form-control handling expanded.

This is one of several places where Enaga treats link activation and control activation as related but separate state machines.

## Script execution details

### Inline scripts and `javascript:` URLs run through Okojo host-task context

Directly invoking JS from arbitrary host callbacks caused Okojo context/runtime failures. The current workaround is:

1. create a host function driver
2. queue it onto the realm host task queue
3. run script execution from that queued context

This applies to:

- `javascript:` URL execution
- browser timer callbacks

The key rule is: **do not call back into JS from arbitrary scheduler callbacks without re-entering through Okojo's host-task path**.

### `window` is the browser global object

The main browser realm now uses Okojo's `GlobalObject` as the browser `window` object. Enaga defines:

- `window`
- `self`
- `globalThis`
- `top`
- `parent`

as the same object in the main realm. This means browser identity checks such as `window === globalThis` and `self === window` are true, and `window.foo = ...` writes to the same object that later bare global lookups can see.

Implementation detail: browser globals such as `fetch`, timers, `document`, `navigator`, and storage are written through the global binding surface as well as the global object property surface. This is necessary because Okojo's generic web runtime may install default globals first; the browser runtime must replace those bindings instead of merely adding a same-named ordinary object slot.

### External scripts are loaded in document order

The current browser runtime loads and executes discovered classic scripts in source order. This is intentionally simpler than the full browser algorithm for `async`, `defer`, parser blocking, and preload interactions.

Module scripts are still treated separately and are not folded into this classic-script path.

## Timers

### Browser timers override generic web-runtime timer globals

Enaga installs its own `setTimeout`, `clearTimeout`, `setInterval`, and `clearInterval` wrappers instead of relying only on default web-runtime globals. This allows timer scheduling to stay aligned with the browser host scheduler and Enaga event-loop pumping model.

Implementation shape:

1. capture callback + delay + extra arguments
2. schedule delayed work on `BrowserHostTaskScheduler`
3. queue a host task back into the realm
4. invoke the callback from that host task
5. reschedule if it is an interval and still active

Current implementation detail: interval activity is tracked separately from the delayed-operation dictionary so `clearInterval` can suppress rescheduling even if the callback has already been dequeued.

## Event loop pumping

### Browser-runtime work is pumped in a fixed queue order

The current preferred host queue order is:

1. timers
2. messages
3. network
4. default host tasks
5. rendering

This is a practical ordering chosen to keep browser-like async work progressing while still allowing host-driven repaint/update hooks to run afterward.

It is not yet claiming standards-level browser event loop parity.

## Storage implementation

### `localStorage`

Current implementation is:

- synchronous
- in-memory
- shared in-process by resolved origin

The backing store is `BrowserStorageRegistry` + `BrowserStorageArea`.

For file-backed documents, origin grouping is pragmatic and directory-based rather than standards-complete.

### `sessionStorage`

Current implementation is:

- synchronous
- in-memory
- scoped to one `HtmlBrowserScriptRuntime` instance

No persistence, quota system, or `storage` events are currently implemented.

### Storage JS bindings are explicit method shims

The JS layer is not a proxy-backed dynamic storage object. Enaga exposes:

- `length`
- `key(index)`
- `getItem`
- `setItem`
- `removeItem`
- `clear`

Direct property writes like `localStorage.foo = "x"` do not currently mutate the backing storage area.

That behavior is intentional because Enaga currently exposes storage as a small explicit API surface, not as a fully proxied Web Storage host object.

## Worker implementation

### Workers are enabled through Okojo's built-in worker-agent support

Enaga does not implement a separate worker VM. It wires Okojo's existing worker support into the browser runtime by enabling:

- worker globals
- worker message queue
- web worker constructor
- worker host/background host

### Worker script loading uses a browser-specific loader

`BrowserWorkerModuleLoader` exists so worker entry scripts, `importScripts(...)`, and module imports follow browser-document rules instead of generic process-relative rules.

Current behavior:

- local file workers resolve relative to document path/base path
- HTTP(S) workers resolve relative to document URL/base URL
- classic worker `importScripts(...)` resolves relative to the currently executing worker script
- worker module imports resolve relative to the worker module referrer
- remote worker requests use browser-shaped headers

Implementation detail: Okojo now carries worker script type through the worker host boundary. `new Worker("./worker.js")` creates a classic worker, while `new Worker("./worker.js", { type: "module" })` creates a module worker. Classic workers load script text through `IWorkerScriptSourceLoader`; module workers still use the module loader path.

For module loading, Okojo's module loader gives Enaga the **resolved id** when loading source, not the original requester. Because of that, `BrowserWorkerModuleLoader` keeps a best-effort map from resolved module id to the referrer/requester that produced it. This is used to recover:

- `Referer`
- `Sec-Fetch-Mode`
- `Sec-Fetch-Site`

This is still approximate. There is no full worker-origin policy engine yet.

### Worker environment is intentionally small

Worker globals currently depend on Okojo worker support plus Enaga's extra setup:

- `self`
- `onmessage`
- `postMessage`
- `importScripts`
- console
- Okojo `SharedArrayBuffer`
- Okojo `Atomics`

This is enough for dedicated-worker message flows and classic script inclusion, but not a full browser worker runtime. There is still no full worker-origin policy engine, structured clone implementation for arbitrary objects, worker lifecycle/event model parity, or Service Worker support.

## Fetch and network behavior

### `fetch` is a minimal Response wrapper

`fetch` currently supports:

- local files
- HTTP(S)
- method override
- text body
- object-style headers

The Response-like wrapper exposes:

- `ok`
- `status`
- `statusText`
- `url`
- `headers.get`
- `headers.has`
- `text()`
- `json()`
- `arrayBuffer()`

This is deliberately smaller than the standard Fetch API.

### Local fetch is a direct file read shortcut

If a fetch target resolves to a local file, Enaga bypasses HTTP and returns a Response-like object from direct file reads. Content type is inferred from file extension with a small explicit table.

That is intentionally practical rather than standards-complete.

## Resource loading details

### Images

Remote image loading is asynchronous and cache-backed:

1. create a pending cache entry
2. fetch/decode in the background
3. update cache state to ready/failed
4. request repaint when the resource resolves

Pending-image placeholders are intentionally suppressed when there is no explicit placeholder to avoid large dummy rectangles during normal loading.

There are therefore three meaningful image states in the renderer path:

- pending without placeholder
- ready
- failed with fallback/error presentation

### Fonts

Remote fonts are cached separately from document/script requests. This is a renderer resource cache, not a full browser font subsystem.

## Form state and value resolution

### Renderer state can override static DOM attributes

For some controls, Enaga resolves values from multiple sources in this order:

1. runtime-side live value cache
2. host-provided live text resolver
3. DOM text/attribute state

This exists because the renderer can hold a newer user-edited value than the original serialized DOM snapshot.

### `<select>` and radio behavior is partly enforced outside a full browser form engine

Some form semantics are implemented as targeted rules:

- selected option persistence is treated as live state
- same-name radio buttons are made exclusive
- GET form submission serializes current live values into a navigation URL

These rules were added to support real saved pages before a standards-complete form/control model exists.

## URL handling details

### Special schemes are preserved before generic resolution

URL resolution keeps certain schemes and special forms intact instead of normalizing them as ordinary relative URLs:

- `javascript:`
- `mailto:`
- `tel:`
- fragments

This is necessary because those paths are dispatched by host/browser logic rather than fetched as documents.

### Navigation requests are host requests, not immediate document replacement

`location.href`, `location.replace(...)`, clicked links, and form GET submits do not directly swap the renderer document inside the JS runtime. Instead they raise browser-host navigation intent which `SampleBrowser` or another host decides how to apply.

This separation keeps the reusable runtime/browser layer distinct from the example application's history and window shell.

## Form/control fidelity workarounds

Several form-control behaviors are implemented with renderer/runtime-specific shims instead of a full browser form engine:

- `<select>` selected state is preserved as live state, not repeatedly overwritten from original static markup
- submit/reset/button controls use default button-like styling when author CSS does not fully define appearance
- GET form submission builds navigation URLs from current live control values
- radio groups enforce same-name exclusivity in renderer/runtime logic

These choices exist to improve fidelity on real saved pages before a complete browser form algorithm exists.

## Known deliberate non-goals in the current implementation

The current code intentionally does **not** try to hide these shortcuts:

- full DOM event model
- full browser navigation/history model
- standards-complete storage behavior
- standards-complete classic worker semantics
- strict browser security policy enforcement
- full incremental DOM-to-layout invalidation

When adding new behavior, prefer documenting similar compatibility shims and narrow workarounds here rather than only in high-level architecture documents.
