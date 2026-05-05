# Enaga.Browser layer

`src\Enaga.Browser` is the reusable browser-adjacent layer used by `examples\SampleBrowser`. It is intentionally small: rendering still belongs to `Enaga.Html`, native windows still belong to `Enaga.Windowing.Silk`, and `SampleBrowser` remains a temporary UI shell for navigation, toolbar, and smoke testing.

Implementation-level browser/runtime mechanics and workarounds are documented separately in `docs\browser-implementation-details.md`.

## Current functionality

### Script runtime

`Enaga.Enaga.Browser.HtmlBrowserScriptRuntime` runs classic inline `<script>` blocks discovered by `Enaga.Html.Dom.HtmlDocumentParser`.

Available globals:

| API | Current behavior |
| --- | --- |
| `window`, `self` | Same host object. |
| `document` | Thin wrapper over `HtmlDomDocument`. |
| `location` | Current document source string. |
| `console.log`, `console.warn`, `console.error` | Writes to the host console with a `Enaga.Browser` prefix. |

Script execution is optional. Hosts call `HtmlBrowserScriptRuntime.CreateAndRun(document, documentSource)` and subscribe to `DocumentMutated` to repaint after DOM changes.

### DOM APIs exposed to JavaScript

Document:

- `document.body`
- `document.getElementById(id)`
- `document.querySelector(selector)` for simple `#id`, `.class`, and tag selectors
- `document.getElementsByTagName(tagName)`
- `document.createElement(localName)`

Element:

- `id` getter/setter
- `className` getter/setter
- `localName`
- `tagName`
- `textContent` getter/setter
- `innerText` getter/setter
- `getAttribute(name)`
- `setAttribute(name, value)`
- `removeAttribute(name)`
- `appendChild(child)`
- `addEventListener("click", callback)`
- `removeEventListener("click", callback)`
- `onclick`

Click dispatch is host-driven. The renderer reports clicked `HtmlDomElement` identities, then `HtmlBrowserScriptRuntime.DispatchClick(...)` invokes `onclick` and `click` listeners while walking ancestors.

### DOM model and mutation

`src\Enaga.Html.Dom` owns the DOM-facing data model:

- `HtmlDocumentParser` parses HTML and exposes:
  - root element
  - linked/inline author styles
  - script metadata
  - executable inline classic script text
- `HtmlDomDocument` indexes elements by id and node id.
- Text and attribute mutations rebuild affected immutable element records.
- `HtmlDocument` can carry the live `HtmlDomDocument` so renderer updates do not need to serialize and parse the whole HTML document again.
- `HtmlDomDocument.ToHtml()` is still available for debug/export and `CurrentDocument.Html` consumers.

The current repaint path keeps the browser DOM model as renderer input: DOM mutations update the indexed DOM, then the host calls `HtmlSceneFrameSource.UpdateDocument(...)`. `HtmlSceneFrameSource` reuses node ids/version stores to route the change through layout dirty nodes and fragment damage instead of treating every mutation as a full runtime reload.

### Loading

`src\Enaga.Html.Loader` handles document fetch and source resolution:

- local files
- HTTP/HTTPS URLs
- declared encodings and BOMs
- linked stylesheets
- linked local stylesheets relative to saved HTML files
- explicit stylesheet source
- cookies
- one-hop cookie-gate retry
- browser-like request headers
- relative URL base paths

Basic renderer support for saved-page form controls currently includes hidden inputs being omitted, search/text inputs rendering as text inputs, `<select>` rendering the selected option text with click-to-open option selection, and submit/button/reset inputs rendering as button-like content. Form submission and full keyboard/change-event dropdown parity are still TODOs.

`HtmlDocumentLoader.CreateHttpClient(...)` is public and accepts `HtmlDocumentHttpClientOptions`. Defaults are generic and culture-derived: the User-Agent no longer hard-codes Windows or SampleBrowser, and Accept-Language is generated from `CultureInfo.CurrentUICulture`.

### SampleBrowser integration

`examples\SampleBrowser` currently provides:

- native toolbar overlay
- URL entry
- Back/Next/Refresh history
- link navigation
- local file watching
- optional script execution through `src\Enaga.Browser`

The viewer is not the long-term browser API surface. Move reusable behavior into `src\Enaga.Browser`, `src\Enaga.Html.Dom`, or `src\Enaga.Html.Loader`.

## Implemented in the current extraction pass

- Moved the Okojo-backed script runtime from `examples\SampleBrowser` to `src\Enaga.Browser`.
- Removed direct Okojo references from `SampleBrowser.csproj`; the example now consumes `Enaga.Browser`.
- Added `HtmlDocumentLoader.CreateHttpClient(...)` and `HtmlDocumentHttpClientOptions`.
- Replaced hard-coded Windows/Japanese loader headers with generic browser headers and culture-derived Accept-Language.
- Added writable element `id`, `className`, `textContent`, and `innerText`.
- Added `setAttribute`, `removeAttribute`, and `removeEventListener`.
- Added DOM-side attribute mutation tests and loader header tests.
- Fixed local saved-page linked CSS loading and added first-pass rendering for common search form controls.

## TODOs

### Enaga.Browser runtime API

- Replace string `location` with a small location object (`href`, `protocol`, `host`, `pathname`, `search`, `hash`) and a host navigation callback.
- Add a browser host/options object instead of hard-wiring console output, mutation behavior, and script error reporting.
- Add timer APIs (`setTimeout`, `clearTimeout`, `setInterval`, `clearInterval`) with host-owned scheduling.
- Decide whether `fetch` should come from `Okojo.WebPlatform`, `Enaga.Browser`, or a host-provided capability.
- Support external classic scripts after loader policy and same-origin/capability rules are defined.
- Decide module script support separately; do not treat `type="module"` as classic script.

### DOM API

- Add `querySelectorAll`.
- Add tree navigation (`parentNode`, `children`, `firstChild`, `nextSibling`) with stable JS object identity.
- Add `removeChild`, `replaceChild`, `insertBefore`, and safer detach/reattach behavior for existing nodes.
- Add `contains`, `matches`, and `closest`.
- Add `hasAttribute`.
- Add `dataset` and basic `style` object support.
- Add form/control APIs for input values when renderer-side form controls are stable.
- Make serialization more HTML-compatible for void elements and boolean attributes.

### Events

- Introduce an event object with `preventDefault`, `stopPropagation`, `bubbles`, `defaultPrevented`, and phase/current-target behavior.
- Add listener options (`once`, `capture`, `passive`) or explicitly reject unsupported options.
- Add input/change/submit events for text controls and forms.
- Add form submission/navigation behavior and full dropdown keyboard/change-event parity.
- Add keyboard and pointer events once host input dispatch has a stable DOM target path.

### Rendering and mutation

- Replace full HTML serialization/reparse on mutation with a DOM-to-scene invalidation path.
- Batch DOM mutations made during one script turn before repainting.
- Preserve runtime/listeners when renderer document updates are caused by DOM mutation.
- Define how script-created nodes map to renderer node ids across reparses.

### Loader and navigation

- Make loader options an instance-level service, not only static helpers.
- Expose redirect/final URL metadata to browser hosts.
- Add configurable cookie container lifetime.
- Add cache policy hooks.
- Add navigation policy hooks for unsupported schemes, downloads, and external browser handoff.

### Security and host policy

- Add a capability model for network, filesystem, timers, and external navigation.
- Add script error reporting callbacks.
- Decide default behavior for cross-origin external resources.
- Keep the default example permissive enough for demos, but make reusable APIs explicit about policy.
