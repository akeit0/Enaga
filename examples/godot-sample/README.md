# Enaga Godot Sample

This sample turns the stock `examples/godot-sample` project into a small 3D arena scene with a Enaga overlay UI.

- Godot renders the 3D world (`Node3D`, camera, lights, meshes)
- Enaga renders a Windows DirectComposition overlay on top of the Godot window
- generated globals bridge the overlay buttons and the Godot game state in both directions

## Setup

From the repository root:

```sh
cd examples/godot-sample
pnpm install
pnpm run build:react
dotnet build
```

Then open `examples/godot-sample` in Godot 4.6+ and run the main scene.

## What it does

- click the arena floor to move the player orb
- collect floating crystals to raise the score
- switch between **Strike**, **Guard Break**, and **Blink Step** from the overlay
- the selected overlay action changes movement speed, scene accents, and score behavior in the Godot world

Currently Windows-only because the overlay host uses DirectComposition/D3D12.

No need to run test after local change.
