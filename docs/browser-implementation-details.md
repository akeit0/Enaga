# Browser Implementation Details

This document records implementation-level mechanics, compatibility shims, and deliberate workarounds in Enaga's browser stack. It is meant to answer **how specific browser-like behavior is implemented today**, not to describe the project at a high level.

## Scope

Primary implementation points:

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

### Browser-like request headers

Requests are shaped to look browser-originated, not just generic `HttpClient` traffic. Depending on the resource type, Enaga adds combinations of:

- `Accept`
- `Accept-Language`
- `Referer`
- `Sec-Fetch-Dest`
- `Sec-Fetch-Mode`
- `Sec-Fetch-Site`

This is used to reduce server-side branching onto bot/non-browser code paths.

### Cookie-gate retry

`HtmlDocumentLoader` includes a targeted one-hop retry for cookie-gated pages:

1. load the initial document
2. observe `Set-Cookie`
3. if the response effectively points to a same-document gate link, follow it once

This is not a full browser navigation model. It is a narrow workaround for simple cookie-wall and interstitial patterns.

## DOM mutation strategy

### Mutations rebuild document HTML snapshots

Current DOM mutation is snapshot-oriented:

1. JavaScript mutates the custom DOM model
2. the DOM is serialized back to HTML
3. a fresh `HtmlDocument` snapshot becomes the renderer input
4. the host updates `HtmlSceneFrameSource`

This is intentionally conservative. It favors correctness and consistency across script-visible DOM and renderer state over fine-grained incremental invalidation.

### `innerHTML` uses fragment parsing, not ad-hoc string splitting

`innerHTML` assignment is implemented through HTML fragment parsing and DOM replacement, not manual token splitting. This was necessary because mixed/nested fragments were otherwise rendered as literal text or detached incorrectly.

Current shape:

- parse fragment with `HtmlDocumentParser.ParseFragment(...)`
- import parsed children into `HtmlDomDocument`
- rebuild node indexing so later JS access still works

## Event dispatch and click handling

### Click dispatch is host-driven

The renderer decides which `HtmlDomElement` was hit. The script runtime then:

1. resolves the current DOM element
2. creates a minimal event object
3. invokes `onclick`
4. invokes registered `"click"` listeners while walking ancestors

This is not a full DOM event system. It is a targeted implementation for the current pointer/click path.

### Anchor activation is preserved separately from control handling

There is explicit logic to avoid clearing pending link activation just because pointer-up occurred on a descendant or nearby interactive path. This was added after anchor navigation regressed while form-control handling expanded.

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

### External scripts are loaded in document order

The current browser runtime loads and executes discovered classic scripts in source order. This is intentionally simpler than the full browser algorithm for `async`, `defer`, parser blocking, and preload interactions.

## Timers

### Browser timers override generic web-runtime timer globals

Enaga installs its own `setTimeout`, `clearTimeout`, `setInterval`, and `clearInterval` wrappers instead of relying only on default web-runtime globals. This allows timer scheduling to stay aligned with the browser host scheduler and Enaga event-loop pumping model.

Implementation shape:

1. capture callback + delay + extra arguments
2. schedule delayed work on `BrowserHostTaskScheduler`
3. queue a host task back into the realm
4. invoke the callback from that host task
5. reschedule if it is an interval and still active

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

## Worker implementation

### Workers are enabled through Okojo's built-in worker-agent support

Enaga does not implement a separate worker VM. It wires Okojo's existing worker support into the browser runtime by enabling:

- worker globals
- worker message queue
- web worker constructor
- worker host/background host

### Worker script loading uses a browser-specific module loader

`BrowserWorkerModuleLoader` exists so worker entry/module resolution follows browser-document rules instead of generic process-relative rules.

Current behavior:

- local file workers resolve relative to document path/base path
- HTTP(S) workers resolve relative to document URL/base URL
- worker module imports resolve relative to the worker module referrer
- remote worker requests use browser-shaped headers

### Worker environment is intentionally small

Worker globals currently depend on Okojo worker support plus Enaga's extra setup:

- `self`
- `onmessage`
- `postMessage`
- console
- Okojo `SharedArrayBuffer`
- Okojo `Atomics`

This is enough for dedicated-worker message flows, but not a full browser worker runtime.

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

## Resource loading details

### Images

Remote image loading is asynchronous and cache-backed:

1. create a pending cache entry
2. fetch/decode in the background
3. update cache state to ready/failed
4. request repaint when the resource resolves

Pending-image placeholders are intentionally suppressed when there is no explicit placeholder to avoid large dummy rectangles during normal loading.

### Fonts

Remote fonts are cached separately from document/script requests. This is a renderer resource cache, not a full browser font subsystem.

## URL handling details

### Special schemes are preserved before generic resolution

URL resolution keeps certain schemes and special forms intact instead of normalizing them as ordinary relative URLs:

- `javascript:`
- `mailto:`
- `tel:`
- fragments

This is necessary because those paths are dispatched by host/browser logic rather than fetched as documents.

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
