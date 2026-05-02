# Enaga MonoGame Overlay Sample

This sample renders Enaga as a Windows DirectComposition overlay on a MonoGame `GameWindow`.

The sample uses the DirectComposition/D3D12 overlay host in manual-tick mode. MonoGame owns the main loop, and each `Game.Update(...)` syncs the overlay size to the MonoGame client area and ticks the overlay renderer.

This imitates WebView-style overlay composition without WebView2: Enaga renders into a GPU-backed DXGI composition swapchain attached to the game window. It does not copy pixels through CPU memory and does not create a selectable overlay top-level window.

Run from the repository root:

```powershell
pnpm --dir examples/MonoGameOverlay build:react
dnrelay run .\examples\MonoGameOverlay\Enaga.MonoGameOverlay.Sample\Enaga.MonoGameOverlay.Sample.csproj -- --config .\examples\SampleApp\sample-appsettings.json
```

The overlay is not a Silk/GLFW `IWindow`; it is a DirectComposition visual attached to the MonoGame HWND.

MonoGame remains the input owner. The sample forwards MonoGame's `Mouse.GetState()` into Enaga and decides game blocking in code with `HitTestOverlayInput(...)`: Enaga `hoverable` UI regions consume the game click path, while empty transparent overlay space continues to drive the MonoGame target marker.

Currently Windows DirectComposition/D3D12 only.
