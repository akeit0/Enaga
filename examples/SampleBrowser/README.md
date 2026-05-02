# SampleBrowser

SampleBrowser renders an HTML document into a native Enaga window. It can load the bundled sample, a local HTML file, or a remote URL. The top navigation bar is a native scene overlay, so page HTML/CSS and viewport zoom do not affect its layout.

## Run

```powershell
dnrelay run .\examples\SampleBrowser\SampleBrowser.csproj
```

Render a local file:

```powershell
dnrelay run .\examples\SampleBrowser\SampleBrowser.csproj -- --html .\examples\SampleBrowser\sample.html --css .\examples\SampleBrowser\sample.css
```

Render a URL:

```powershell
dnrelay run .\examples\SampleBrowser\SampleBrowser.csproj -- --url https://example.com/
```

Try in-viewer navigation:

```powershell
dnrelay run .\examples\SampleBrowser\SampleBrowser.csproj -- --html .\examples\SampleBrowser\navigation-demo.html
```

## Options

| Option | Description |
| --- | --- |
| `--html <path>` | Render a local HTML file. If `--css` is not provided, only styles embedded or linked by the document are used. |
| `--url <url>` | Render an HTTP or HTTPS document. Linked remote stylesheets are loaded relative to the document URL. |
| `--css <path-or-url>` | Add an explicit stylesheet. Local paths and HTTP/HTTPS URLs are supported. |
| `--no-css` | Do not add the default sample stylesheet or an explicit stylesheet. |
| `--watch` | Reload local HTML/CSS files when they change. This is enabled by default for local sources. |
| `--no-watch` | Disable local file watching. URL sources are not watched. |
| `--url-bar` | Show the native navigation bar at the top. This is enabled by default. Type a URL and press Enter, use Back/Next history, or Refresh the current page. |
| `--no-url-bar` | Hide the native navigation bar. |
| `--js`, `--enable-js` | Run inline classic `<script>` blocks through `src\Enaga.Browser`. A small `window`, `document`, DOM mutation API, click listeners, and `console.log` / `warn` / `error` are available. |
| `--no-js` | Disable script execution. This is the default. |
| `--title <text>` | Set the native window title. |
| `--width <pixels>` / `--height <pixels>` | Set the initial native window size. |
| `--opengl`, `--vulkan`, `--metal` | Select the graphics backend. macOS defaults to Metal. |
| `--input-log` | Wrap the frame source with input diagnostics. |

## Loader library

Document loading lives in `src\Enaga.Html.Loader`, not in the example app. The loader handles local files, HTTP/HTTPS URLs, declared encodings, linked stylesheets for both URLs and saved local pages, cookies, browser-like request headers, and document base paths so relative links and image sources can resolve correctly during rendering.

The renderer has first-pass support for common saved-page form controls: hidden inputs are skipped, search/text inputs render as text inputs, selects show the selected option, and submit inputs render as button-like content. Form submission and dropdown interaction are not implemented yet.

Some pages set a cookie and return a one-link "click here" interstitial before serving the real document. When that link points back to the same document URL, the loader retries once with the received cookie so the viewer can show the real page immediately.

## Enaga.Browser layer

Reusable browser-like script and DOM behavior lives in `src\Enaga.Browser`; SampleBrowser only wires it to the native toolbar, navigation, and renderer. See `docs\browser.md` for the current API surface and TODOs.
