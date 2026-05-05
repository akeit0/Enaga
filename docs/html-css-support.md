# HTML and CSS support

This document lists the current Enaga HTML renderer and browser-runtime support. Enaga parses broader HTML through AngleSharp, but only the features below have renderer, loader, style, or script semantics.

## HTML document loading

| Area | Supported behavior |
| --- | --- |
| Sources | Local files and HTTP/HTTPS documents. |
| Encoding | BOM and declared encodings through `HtmlDocumentTextDecoder`. |
| Base paths | Relative resource paths are resolved from the loaded document base path. |
| Stylesheets | Inline `<style>` and local/HTTP linked `<link rel="stylesheet">`. |
| Scripts | Classic inline scripts and external classic scripts when `Enaga.Browser` script execution is enabled. Module scripts are not supported. |
| Images | `<img src>`, `width`, `height`, intrinsic image probing through backend services, and relative URL resolution. |

## Rendered HTML elements

| Elements | Current support |
| --- | --- |
| `body` | Scroll container root, default padding/background/text defaults. |
| Generic block elements such as `div`, `main`, `section`, `article`, `header`, `footer`, `nav` | Render as block/view nodes with CSS styling. `display: contents` flattens the element and renders its children in the parent formatting context. |
| `p`, `span` | Text and inline flow handling. |
| `a` | Inline/link styling, inherited link href for descendants, click activation. |
| `br` | Line break block inside inline flow. |
| `strong`, `b`, `em`, `i`, `u`, `small`, `font` | Inline text defaults for weight, italic, underline, font size/family/color. |
| `h1`, `h2`, `h3` | Heading font-weight/size and semantic spacing defaults. |
| `ul`, `ol`, `li` | Basic markers, ordered numeric markers, marker indentation, `list-style` marker suppression/type. |
| `table`, `tbody`, `thead`, `tfoot`, `tr`, `td`, `th` | Flex-based table approximation, table/cell spacing, `th` center/bold defaults, rowspan/colspan fields in scene nodes. |
| `center` | Centered flex column approximation. |
| `hr` | Horizontal rule sizing/background. |
| `img` | Image node with object fit and intrinsic aspect ratio behavior. |
| `input` | Text/search-like inputs as text input controls; hidden inputs omitted; `submit`, `button`, and `reset` as button-like views. |
| `textarea` | Multiline text input control; value from text content. |
| `select`, `option` | Select control display text from selected or first option; pointer selection support in `HtmlSceneFrameSource`. |
| `button` | Button-like view/control with default interaction colors. |
| `head`, `meta`, `link`, `style`, `script` | Parsed for metadata/resources/scripts but not rendered as scene nodes. |

## Browser-runtime DOM and JS APIs

| API | Current support |
| --- | --- |
| Globals | `window`, `self`, `document`, `location`, `console`, `fetch`. |
| Event loop | Okojo host tasks, timers, messages, network completions, and promise continuations are pumped by the browser/runtime bridge. |
| `location` | `href`, assignment navigation, `replace(url)`, and `toString()`. `replace` requests navigation without pushing history in SampleBrowser. |
| `fetch` | `fetch` and `window.fetch`; relative URL resolution; local file and HTTP(S) reads; minimal `Response` with `ok`, `status`, `statusText`, `url`, `headers.get/has`, `text()`, `json()`, and `arrayBuffer()`. |
| Document APIs | `body`, `getElementById`, `querySelector` for simple `#id`, `.class`, and tag selectors, `getElementsByTagName`, `createElement`. |
| Element APIs | `id`, `className`, `localName`, `tagName`, `textContent`, `innerText`, `value`, `getAttribute`, `setAttribute`, `removeAttribute`, `appendChild`, `onclick`, `addEventListener("click")`, `removeEventListener("click")`. |

## CSS selectors and at-rules

| Feature | Supported subset |
| --- | --- |
| Selector groups | Comma-separated selector lists. |
| Simple selectors | Type, universal, id, class, attribute selectors, and limited pseudo-class matching. |
| Combinators | Descendant and child (`>`). |
| Pseudo-classes | `:hover` and `:active` for renderer pointer state. |
| Attribute selectors | `[attr]`, `[attr=value]`, `[attr~=value]`, `[attr\|=value]`,`[attr^=value]`,`[attr$=value]`,`[attr*=value]`, and the ASCII case-insensitive`i` flag. |
| Unsupported selectors | Sibling combinators, most structural pseudo-classes, and pseudo-elements except scrollbar rewriting. |
| Media queries | `@media` with `screen`/`all`, `min-width`, `max-width`, `min-height`, and `max-height` in `px`, `em`, and `rem`. `print` and `not` queries do not match. |
| Scrollbar pseudo-elements | `::-webkit-scrollbar`, `::-webkit-scrollbar-track`, and `::-webkit-scrollbar-thumb` are rewritten to internal scrollbar style properties. |

## CSS properties

| Category | Supported properties and notes |
| --- | --- |
| Display/layout | `display` (`none`, `contents`, `block`, `flow-root`, `list-item`, `inline`, `inline-block`, `flex`, `inline-flex`), `position` (`static`, `relative`, `absolute`), `box-sizing`, `float`, `clear`, `overflow`, `contain`, `order`. |
| Flex/alignment | `flex-direction`, `flex-wrap`, `justify-content` (`start`, `center`, `end`, `flex-end`, `space-between`, `space-around`, `space-evenly`), `align-items`, `align-self`, `place-content` (maps justify side), `place-items` (maps align-items), `place-self` (maps align-self), `flex-grow`, `flex-shrink`, `flex-basis`, `flex`, `gap`, `row-gap`, `column-gap`. |
| Sizing/positioning | `width`, `height`, `min-width`, `max-width`, `min-height`, `max-height`, `left`, `top`, `right`, `bottom`, `aspect-ratio`. Lengths support `px`, `%`, `vw`, `vh`, `em`, `rem`, and numeric zero where applicable. |
| Spacing | `margin`, `margin-top/right/bottom/left`, `margin-inline`, `margin-inline-start/end`, `margin-block`, `margin-block-start/end`, `padding`, `padding-top/right/bottom/left`. |
| Borders | `border`, `border-top/right/bottom/left`, `border-width`, side border widths, `border-style`, side border styles, `border-color`, side border colors, `border-radius`, `border-collapse`, `border-spacing`. Border rendering supports solid and dotted scene styles. |
| Backgrounds | `background`, `background-color`, `background-image: url(...)`, `background-size` (`cover`, `contain`, fallback fill). |
| Shadows | `box-shadow`, `text-shadow` with non-inset length/color shadows. |
| Text/font | `color`, `font-size`, `font-family`, `font-weight`, `font-style`, `text-align`, `text-transform`, `text-decoration`, `white-space`, `text-overflow`, `line-height`. |
| Lists | `list-style`, `list-style-type` for `none`, `disc`, `circle`, and `square`. |
| Images | `object-fit`. |
| Scrollbars | Internal `scrollbar-width`, scrollbar track color, and scrollbar thumb color through scrollbar pseudo-element rewriting. |
