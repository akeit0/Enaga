# AGENTS

This file governs root-level work in `.` when a task spans both `okojo`.

## Goal

Primary goal: render React into a native window or offscreen texture, not the web DOM/canvas, using:

- `SkiaSharp` for native drawing and presentation
- local `okojo` sources for JavaScript and Node-like execution when unpublished packages are required

## Current architecture findings

- `okojo` already has a usable embedding API and a local Node-like stack (`src/Okojo.Node*`), but those Node-facing packages are not part of the published package wave yet.

## Planning rules

1. Extract host/platform interfaces before attempting a full native port.
2. Prove `SceneRuntime` -> `SkiaSharp` rendering first, before bringing in React or Okojo.
3. Keep window and texture targets as first-class outcomes; do not design only for one.
4. Use local `okojo` source projects for Node-like execution; do not assume unpublished pieces are available from NuGet or npm.
5. If work requires editing `okojo`, stop and tell the user before making those edits.
6. Prefer moving reusable logic deeper into `core-v2` instead of adding more browser assumptions in `react-v2`.

## Command rules

- Use the global `dnrelay` command for native host build, run, and test workflows.
- Prefer `dnrelay build`, `dnrelay run`, and `dnrelay test` over direct `dotnet build`, `dotnet run`, or `dotnet test` for this repo.

## Native host interop rules

- Do not use JSON only as a communication format between TypeScript and C# when direct interop is possible.
- Prefer direct params, direct `JsObject` property reads, and shared `JsArray`-style result paths back to JavaScript.
- Prefer `GenerateJsObjectAttribute` and `GenerateJsGlobalsAttribute` when defining reusable interop objects and globals.

## Recommended milestone order

1. **Presenter abstraction**: separate surface creation, frame pump, DPR, pointer input, cursor updates, and drawing from browser DOM types.
2. **Native renderer spike**: render a hard-coded or imperative `SceneRuntime` with `SkiaSharp` into a native window and an offscreen render target.
3. **Native host bridge**: own resize, frame scheduling, input forwarding, and texture presentation on the .NET side.
4. **Okojo bridge**: run the JS bundle on local `Okojo.Node` and expose host bindings needed by React and the renderer.
5. **React mount path**: mount a React tree into `SceneRuntime` without going through the web `<canvas>` path.
6. **Polish**: images, fonts, timers, module loading, pointer parity, and texture upload/performance work.

### `okojo`

- Follow `okojo\AGENTS.md`.
- Prefer local `src\Okojo.Node*` projects over assumptions about published Node packages.
- Use focused `.NET` validation commands already documented in that repo when changing it.
- Do not edit `okojo` without explicit user awareness.