using Godot;
using Okojo;
using Okojo.Annotations;
using Enaga.Hosting;
using Enaga.Overlay.Windows;
using Enaga.Rendering;
using Enaga.Rendering.Skia;
using Enaga.React.OkojoRuntime;
using Enaga.React.OkojoRuntime.Skia;
using System;
using System.IO;
using System.Text;
public sealed partial class OverlayGameRoot : Node3D
{
	private readonly GodotOverlayBridge bridge = new();
	private readonly JsValue[] hudSnapshotArgs = new JsValue[11];
	private WindowsDirectCompositionOverlayHost? overlay;
	private SkiaRuntimeSceneHost? overlaySource;
	private Camera3D? mainCamera;
	private Node3D? player;
	private Node3D? target;
	private MeshInstance3D? playerBody;
	private MeshInstance3D? targetCore;
	private MeshInstance3D? targetRing;
	private MeshInstance3D? ground;
	private MeshInstance3D? beaconA;
	private MeshInstance3D? beaconB;
	private MeshInstance3D? beaconC;
	private double elapsedSeconds;
	private Vector3 moveTarget = new(-4f, 0.7f, 5f);
	private Vector3 targetAnchor = new(6f, 0.75f, -4f);
	private bool overlayPointerCaptured;
	private int score;
	private int combo;
	private int crystalsCollected;
	private float energy = 0.78f;

	public override void _Ready()
	{
		mainCamera = GetNode<Camera3D>("CameraRig/Camera3D");
		player = GetNode<Node3D>("Player");
		target = GetNode<Node3D>("Target");
		playerBody = GetNode<MeshInstance3D>("Player/Body");
		targetCore = GetNode<MeshInstance3D>("Target/Core");
		targetRing = GetNode<MeshInstance3D>("Target/Ring");
		ground = GetNode<MeshInstance3D>("Ground");
		beaconA = GetNode<MeshInstance3D>("BeaconA");
		beaconB = GetNode<MeshInstance3D>("BeaconB");
		beaconC = GetNode<MeshInstance3D>("BeaconC");

		if (!OperatingSystem.IsWindows())
		{
			GD.PushError("The Godot overlay sample currently requires Windows because it uses DirectComposition.");
			return;
		}

		var hwnd = (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, 0);
		if (hwnd == 0)
		{
			GD.PushError("Godot did not return a valid HWND for the overlay sample.");
			return;
		}

		overlaySource = CreateOverlaySource();
		var viewportSize = GetViewport().GetVisibleRect().Size;
		overlay = new WindowsDirectCompositionOverlayHost(
			new SceneRenderRoot(overlaySource, requiresFullFramePresentation: true),
			new WindowsDirectCompositionOverlayOptions
			{
				TargetWindowHandle = hwnd,
				Width = Math.Max(1, (int)viewportSize.X),
				Height = Math.Max(1, (int)viewportSize.Y)
			});

		if (player is not null)
			moveTarget = player.Position;
		if (target is not null)
			targetAnchor = target.Position;
		UpdateBridge(forceRender: true);
	}

	public override void _Process(double delta)
	{
		if (overlay is null || player is null || target is null || mainCamera is null)
			return;

		elapsedSeconds += delta;
		SyncOverlayBounds();
		UpdateOverlayPointer();
		UpdateWorld((float)delta);
		UpdateBridge();
		overlay.Tick(TimeSpan.FromSeconds(elapsedSeconds));
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (overlay is null)
			return;

		if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
		{
			var buttons = GetMouseButtonsMask();
			if (mouseButton.Pressed)
			{
				if (overlay.HitTestOverlayInput(mouseButton.Position.X, mouseButton.Position.Y))
				{
					overlayPointerCaptured = true;
					overlay.PointerDown(0, buttons);
					GetViewport().SetInputAsHandled();
					return;
				}

				if (TryResolveGroundPoint(mouseButton.Position, out var worldPoint))
				{
					moveTarget = worldPoint;
					combo = 0;
					bridge.SetObjective("Move to the next crystal and keep the overlay combo alive.");
					GetViewport().SetInputAsHandled();
				}
			}
			else if (overlayPointerCaptured)
			{
				overlay.PointerUp(0, buttons);
				overlayPointerCaptured = false;
				GetViewport().SetInputAsHandled();
			}
		}
	}

	public override void _ExitTree()
	{
		overlay?.Dispose();
		overlay = null;
		base._ExitTree();
	}

	private void UpdateWorld(float delta)
	{
		if (player is null || target is null || ground is null)
			return;

		var action = bridge.CurrentAction;
		var speed = action switch
		{
			GodotOverlayAction.BlinkStep => 11.4f,
			GodotOverlayAction.GuardBreak => 4.5f,
			_ => 6.4f
		};

		var toTarget = moveTarget - player.Position;
		var movement = toTarget with { Y = 0 };
		if (movement.LengthSquared() > 0.0025f)
		{
			player.Position += movement.Normalized() * speed * delta;
			var facing = new Vector3(movement.X, 0, movement.Z).Normalized();
			player.Rotation = new Vector3(player.Rotation.X, Mathf.LerpAngle(player.Rotation.Y, Mathf.Atan2(-facing.X, -facing.Z), delta * 8f), player.Rotation.Z);
		}

		ApplyActionPresentation(action, delta);

		var beaconMultiplier = action switch
		{
			GodotOverlayAction.BlinkStep => 1.45f,
			GodotOverlayAction.GuardBreak => 0.72f,
			_ => 1f
		};
		RotateBeacon(beaconA, delta, 1.1f * beaconMultiplier, 0.35f);
		RotateBeacon(beaconB, delta, -1.3f * beaconMultiplier, 0.6f);
		RotateBeacon(beaconC, delta, 0.9f * beaconMultiplier, 0.8f);
		AnimateGround();

		var pickupRadius = action switch
		{
			GodotOverlayAction.GuardBreak => 2.05f,
			GodotOverlayAction.BlinkStep => 1.12f,
			_ => 1.5f
		};
		if (player.Position.DistanceTo(target.Position) < pickupRadius)
			CollectCrystal();
	}

	private void ApplyActionPresentation(GodotOverlayAction action, float delta)
	{
		if (target is null)
			return;

		var pulse = bridge.EffectPulse;
		var hover = 0.2f + Mathf.Sin((float)elapsedSeconds * 2.4f) * 0.14f;
		var offset = action switch
		{
			GodotOverlayAction.GuardBreak => new Vector3(Mathf.Cos((float)elapsedSeconds * 0.9f) * 0.12f, 0, Mathf.Sin((float)elapsedSeconds * 1.3f) * 0.22f),
			GodotOverlayAction.BlinkStep => new Vector3(Mathf.Cos((float)elapsedSeconds * 4.2f) * 0.95f, 0, Mathf.Sin((float)elapsedSeconds * 3.8f) * 0.78f),
			_ => new Vector3(Mathf.Cos((float)elapsedSeconds * 2.1f) * 0.36f, 0, Mathf.Sin((float)elapsedSeconds * 2.5f) * 0.3f)
		};
		target.Position = targetAnchor + offset + new Vector3(0, hover, 0);
		target.RotateY(delta * (action == GodotOverlayAction.GuardBreak ? 0.95f : action == GodotOverlayAction.BlinkStep ? 4.2f : 2.1f));

		if (playerBody is not null)
		{
			playerBody.Scale = action switch
			{
				GodotOverlayAction.GuardBreak => new Vector3(1.18f + pulse * 0.22f, 0.92f, 1.18f + pulse * 0.22f),
				GodotOverlayAction.BlinkStep => new Vector3(0.84f, 1.0f + pulse * 0.38f, 0.84f),
				_ => new Vector3(1f + pulse * 0.1f, 1f, 1f + pulse * 0.1f)
			};

			if (playerBody.MaterialOverride is StandardMaterial3D bodyMaterial)
			{
				bodyMaterial.AlbedoColor = action switch
				{
					GodotOverlayAction.GuardBreak => new Color(0.97f, 0.69f + pulse * 0.12f, 0.32f, 1f),
					GodotOverlayAction.BlinkStep => new Color(0.58f, 0.56f + pulse * 0.18f, 1f, 1f),
					_ => new Color(0.28f + pulse * 0.08f, 0.78f, 0.9f, 1f)
				};
				bodyMaterial.Emission = action switch
				{
					GodotOverlayAction.GuardBreak => new Color(0.88f, 0.43f, 0.12f, 1f),
					GodotOverlayAction.BlinkStep => new Color(0.36f, 0.34f, 0.95f, 1f),
					_ => new Color(0.12f, 0.72f, 0.82f, 1f)
				};
				bodyMaterial.EmissionEnergyMultiplier = action switch
				{
					GodotOverlayAction.GuardBreak => 2.2f + pulse * 0.8f,
					GodotOverlayAction.BlinkStep => 1.7f + pulse * 0.5f,
					_ => 1.3f + pulse * 0.35f
				};
			}
		}

		if (targetCore is not null)
		{
			targetCore.Scale = action switch
			{
				GodotOverlayAction.GuardBreak => Vector3.One * (1.05f + pulse * 0.45f),
				GodotOverlayAction.BlinkStep => new Vector3(0.82f + pulse * 0.16f, 1.1f, 0.82f + pulse * 0.16f),
				_ => Vector3.One * (0.96f + pulse * 0.18f)
			};

			if (targetCore.MaterialOverride is StandardMaterial3D coreMaterial)
			{
				coreMaterial.AlbedoColor = action switch
				{
					GodotOverlayAction.GuardBreak => new Color(1f, 0.82f, 0.35f, 1f),
					GodotOverlayAction.BlinkStep => new Color(0.82f, 0.72f + pulse * 0.1f, 1f, 1f),
					_ => new Color(0.98f, 0.56f + pulse * 0.06f, 0.42f, 1f)
				};
				coreMaterial.Emission = action switch
				{
					GodotOverlayAction.GuardBreak => new Color(0.98f, 0.63f, 0.08f, 1f),
					GodotOverlayAction.BlinkStep => new Color(0.48f, 0.34f, 1f, 1f),
					_ => new Color(0.96f, 0.32f, 0.24f, 1f)
				};
				coreMaterial.EmissionEnergyMultiplier = action switch
				{
					GodotOverlayAction.GuardBreak => 2.8f + pulse * 1.2f,
					GodotOverlayAction.BlinkStep => 2.0f + pulse * 0.8f,
					_ => 1.8f + pulse * 0.6f
				};
			}
		}

		if (targetRing is not null)
		{
			targetRing.Scale = action switch
			{
				GodotOverlayAction.GuardBreak => Vector3.One * (1.45f + pulse * 0.85f),
				GodotOverlayAction.BlinkStep => new Vector3(1.35f + pulse * 0.3f, 1.35f + pulse * 0.3f, 1.35f + pulse * 0.3f),
				_ => Vector3.One * (1.15f + pulse * 0.25f)
			};

			if (targetRing.MaterialOverride is StandardMaterial3D ringMaterial)
			{
				ringMaterial.AlbedoColor = action switch
				{
					GodotOverlayAction.GuardBreak => new Color(0.98f, 0.58f + pulse * 0.08f, 0.18f, 1f),
					GodotOverlayAction.BlinkStep => new Color(0.74f, 0.58f, 1f, 1f),
					_ => new Color(0.95f, 0.42f, 0.32f, 1f)
				};
				ringMaterial.Emission = action switch
				{
					GodotOverlayAction.GuardBreak => new Color(0.98f, 0.45f, 0.12f, 1f),
					GodotOverlayAction.BlinkStep => new Color(0.54f, 0.38f, 1f, 1f),
					_ => new Color(1f, 0.28f, 0.22f, 1f)
				};
				ringMaterial.EmissionEnergyMultiplier = action switch
				{
					GodotOverlayAction.GuardBreak => 3.1f + pulse * 1.6f,
					GodotOverlayAction.BlinkStep => 2.2f + pulse * 0.9f,
					_ => 1.9f + pulse * 0.6f
				};
			}
		}
	}

	private void RotateBeacon(Node3D? beacon, float delta, float spinRate, float phase)
	{
		if (beacon is null)
			return;

		beacon.RotateY(delta * spinRate);
		var origin = beacon.Position;
		beacon.Position = new Vector3(origin.X, 0.8f + Mathf.Sin((float)elapsedSeconds * 1.7f + phase) * 0.2f, origin.Z);
	}

	private void AnimateGround()
	{
		if (ground?.MaterialOverride is not StandardMaterial3D material)
			return;

		var pulse = bridge.EffectPulse;
		material.AlbedoColor = bridge.CurrentAction switch
		{
			GodotOverlayAction.GuardBreak => new Color(0.17f + pulse * 0.05f, 0.13f + pulse * 0.04f, 0.08f, 1f),
			GodotOverlayAction.BlinkStep => new Color(0.08f, 0.10f + pulse * 0.05f, 0.18f + pulse * 0.08f, 1f),
			_ => new Color(0.09f + pulse * 0.05f, 0.08f, 0.08f + pulse * 0.03f, 1f)
		};
	}

	private void CollectCrystal()
	{
		crystalsCollected++;
		combo++;
		var points = bridge.CurrentAction switch
		{
			GodotOverlayAction.GuardBreak => 135,
			GodotOverlayAction.BlinkStep => 155,
			_ => 100
		};
		score += points + combo * 10;
		energy = bridge.CurrentAction switch
		{
			GodotOverlayAction.GuardBreak => Mathf.Clamp(energy + 0.22f, 0f, 1f),
			GodotOverlayAction.BlinkStep => Mathf.Clamp(energy - 0.12f, 0f, 1f),
			_ => Mathf.Clamp(energy + 0.04f, 0f, 1f)
		};
		bridge.SetObjective($"Crystal secured. Score {score}. Chain another pickup to grow the combo.");
		RelocateTarget();
	}

	private void RelocateTarget()
	{
		if (target is null)
			return;

		var radius = 4.5f + (crystalsCollected % 4) * 1.25f;
		var angle = 0.8f + crystalsCollected * 1.37f;
		var x = Mathf.Cos(angle) * radius;
		var z = Mathf.Sin(angle) * radius;
		targetAnchor = new Vector3(x, 0.75f, z);
		target.Position = targetAnchor;
	}

	private void UpdateBridge(bool forceRender = false)
	{
		if (player is null || target is null)
			return;

		var changed = bridge.UpdateFrame(
			player.Position,
			target.Position,
			score,
			combo,
			crystalsCollected,
			energy,
			elapsedSeconds);
		if (!forceRender && changed && TryPushHudSnapshotToReact())
			return;

		if (forceRender || changed)
			overlaySource?.RequestRender(SceneDamageReason.FullFrameFallback);
	}

	private bool TryPushHudSnapshotToReact()
	{
		if (overlaySource is null)
			return false;

		hudSnapshotArgs[0] = bridge.SelectedAction;
		hudSnapshotArgs[1] = bridge.Score;
		hudSnapshotArgs[2] = bridge.Combo;
		hudSnapshotArgs[3] = bridge.CrystalsCollected;
		hudSnapshotArgs[4] = bridge.Energy;
		hudSnapshotArgs[5] = bridge.DistanceToTarget;
		hudSnapshotArgs[6] = bridge.PlayerX;
		hudSnapshotArgs[7] = bridge.PlayerZ;
		hudSnapshotArgs[8] = bridge.TargetX;
		hudSnapshotArgs[9] = bridge.TargetZ;
		hudSnapshotArgs[10] = bridge.Objective;
		return overlaySource.TryInvokeGlobalFunctionWhenChanged("__godotOverlayHudUpdate", SceneDamageReason.FullFrameFallback, hudSnapshotArgs);
	}

	private void SyncOverlayBounds()
	{
		if (overlay is null)
			return;

		var viewportSize = GetViewport().GetVisibleRect().Size;
		overlay.Resize(Math.Max(1, (int)viewportSize.X), Math.Max(1, (int)viewportSize.Y));
	}

	private void UpdateOverlayPointer()
	{
		if (overlay is null)
			return;

		var mousePosition = GetViewport().GetMousePosition();
		overlay.PointerMove(mousePosition.X, mousePosition.Y, GetMouseButtonsMask());
	}

	private int GetMouseButtonsMask()
	{
		var mask = 0;
		if (Input.IsMouseButtonPressed(MouseButton.Left))
			mask |= 1;
		if (Input.IsMouseButtonPressed(MouseButton.Right))
			mask |= 1 << 1;
		if (Input.IsMouseButtonPressed(MouseButton.Middle))
			mask |= 1 << 2;
		return mask;
	}

	private bool TryResolveGroundPoint(Vector2 screenPosition, out Vector3 worldPoint)
	{
		worldPoint = default;
		if (mainCamera is null)
			return false;

		var rayOrigin = mainCamera.ProjectRayOrigin(screenPosition);
		var rayNormal = mainCamera.ProjectRayNormal(screenPosition);
		var plane = new Plane(Vector3.Up, 0f);
		var hit = plane.IntersectsRay(rayOrigin, rayNormal);
		if (hit is null)
			return false;

		var point = hit.Value;
		worldPoint = new Vector3(
			Mathf.Clamp(point.X, -11.5f, 11.5f),
			0.7f,
			Mathf.Clamp(point.Z, -11.5f, 11.5f));
		return true;
	}
	class GodotConsoleWriter : TextWriter
	{
		public override Encoding Encoding => Encoding.UTF8;
		StringBuilder currentLine = new();
		public override void Write(string? value)
		{
			currentLine.Append(value);
			if (value is not null && value.Contains('\n'))
			{
				GD.Print(currentLine.ToString());
				currentLine.Clear();
			}
		}
		public override void Flush()
		{
			base.Flush();
		}

		public override void WriteLine(string? value)
		{
			GD.Print(value);
		}

		public override void WriteLine(string format, params object?[] arg)
		{
			GD.Print(string.Format(format, arg));
		}
	}
	private SkiaRuntimeSceneHost CreateOverlaySource()
	{
		var (projectRoot, entryPath) = ResolveReactEntryPath();
		var host = new OkojoNodeReactHost(new OkojoReactHostOptions
		{
			EntrySource = new FileReactAppEntrySource(entryPath, watchPaths: [projectRoot], assetBasePath: projectRoot),
			BackendServices = SkiaRuntimeBackendServices.Create(),
			ConfigureAdditionalGlobals = bridge.InstallGeneratedGlobals,
			Reload = ReactRuntimeReloadOptions.Production,
			Diagnostics = RuntimeDiagnosticsSink.None,
			ConfigureTerminal = terminal => terminal.Stdout = new GodotConsoleWriter()
		});
		return new SkiaRuntimeSceneHost(host);
	}

	private static (string ProjectRoot, string EntryPath) ResolveReactEntryPath()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null)
		{
			var entryPath = Path.Combine(directory.FullName, "dist", "react-entry.mjs");
			if (File.Exists(entryPath))
				return (directory.FullName, entryPath);

			var projectRoot = Path.Combine(directory.FullName, "examples", "godot-sample");
			entryPath = Path.Combine(projectRoot, "dist", "react-entry.mjs");
			if (File.Exists(entryPath))
				return (projectRoot, entryPath);

			projectRoot = Path.Combine(directory.FullName, "godot-sample");
			entryPath = Path.Combine(projectRoot, "dist", "react-entry.mjs");
			if (File.Exists(entryPath))
				return (projectRoot, entryPath);

			directory = directory.Parent;
		}

		throw new InvalidOperationException("Could not locate examples\\godot-sample\\dist\\react-entry.mjs. Run `pnpm install` and `pnpm run build:react` in examples\\godot-sample first.");
	}
}

[GenerateJsGlobals]
public sealed partial class GodotOverlayBridge
{
	private string objective = "Click the arena floor to move. Collect crystals and drive the action from the overlay.";
	private int hudRevision;
	private int renderedHudRevision = -1;
	private int distanceDisplaySteps = -1;
	private int energyDisplayPercent = -1;

	[JsGlobalProperty("godotSelectedAction")]
	public string SelectedAction => CurrentAction switch
	{
		GodotOverlayAction.GuardBreak => "Guard Break",
		GodotOverlayAction.BlinkStep => "Blink Step",
		_ => "Strike"
	};

	[JsGlobalProperty("godotScore")]
	public int Score { get; private set; }

	[JsGlobalProperty("godotCombo")]
	public int Combo { get; private set; }

	[JsGlobalProperty("godotCrystalsCollected")]
	public int CrystalsCollected { get; private set; }

	[JsGlobalProperty("godotEnergy")]
	public float Energy { get; private set; }

	[JsGlobalProperty("godotDistanceToTarget")]
	public float DistanceToTarget { get; private set; }

	[JsGlobalProperty("godotPlayerX")]
	public int PlayerX { get; private set; }

	[JsGlobalProperty("godotPlayerZ")]
	public int PlayerZ { get; private set; }

	[JsGlobalProperty("godotTargetX")]
	public int TargetX { get; private set; }

	[JsGlobalProperty("godotTargetZ")]
	public int TargetZ { get; private set; }

	[JsGlobalProperty("godotPulse")]
	public float EffectPulse { get; private set; }

	[JsGlobalProperty("godotObjective")]
	public string Objective => objective;

	[JsGlobalProperty("godotHudRevision")]
	public int HudRevision => hudRevision;

	public GodotOverlayAction CurrentAction { get; private set; } = GodotOverlayAction.Strike;

	[JsGlobalFunction("godotSelectAction")]
	public void SelectAction(string action)
	{
		var nextAction = action.Trim() switch
		{
			"Guard Break" => GodotOverlayAction.GuardBreak,
			"Blink Step" => GodotOverlayAction.BlinkStep,
			_ => GodotOverlayAction.Strike
		};
		if (CurrentAction == nextAction)
			return;

		CurrentAction = nextAction;
		objective = CurrentAction switch
		{
			GodotOverlayAction.GuardBreak => "Guard Break boosts score bursts and shield gain, but slows movement.",
			GodotOverlayAction.BlinkStep => "Blink Step accelerates movement and rewards aggressive crystal routing.",
			_ => "Strike is the balanced route: steady speed, reliable score, clean chaining."
		};
		hudRevision++;
	}
	[JsGlobalFunction("gdprint")]
	public void GdPrint(string action)
	{
		GD.Print(action);
	}

	public void SetObjective(string nextObjective)
	{
		var next = string.IsNullOrWhiteSpace(nextObjective)
			? objective
			: nextObjective.Trim();
		if (!string.Equals(objective, next, StringComparison.Ordinal))
		{
			objective = next;
			hudRevision++;
		}
	}

	public bool UpdateFrame(
		Vector3 playerPosition,
		Vector3 targetPosition,
		int score,
		int combo,
		int crystalsCollected,
		float energy,
		double elapsedSeconds)
	{
		var nextDistance = playerPosition.DistanceTo(targetPosition);
		var nextDistanceDisplaySteps = Mathf.RoundToInt(nextDistance * 2f);
		var nextEnergyDisplayPercent = Mathf.RoundToInt(Mathf.Clamp(energy, 0f, 1f) * 100f);
		var nextPlayerX = Mathf.RoundToInt(playerPosition.X);
		var nextPlayerZ = Mathf.RoundToInt(playerPosition.Z);
		var nextTargetX = Mathf.RoundToInt(targetPosition.X);
		var nextTargetZ = Mathf.RoundToInt(targetPosition.Z);
		var changed =
			Score != score ||
			Combo != combo ||
			CrystalsCollected != crystalsCollected ||
			energyDisplayPercent != nextEnergyDisplayPercent ||
			distanceDisplaySteps != nextDistanceDisplaySteps ||
			renderedHudRevision != hudRevision ||
			PlayerX != nextPlayerX ||
			PlayerZ != nextPlayerZ ||
			TargetX != nextTargetX ||
			TargetZ != nextTargetZ;

		Score = score;
		Combo = combo;
		CrystalsCollected = crystalsCollected;
		Energy = nextEnergyDisplayPercent / 100f;
		DistanceToTarget = nextDistanceDisplaySteps / 2f;
		PlayerX = nextPlayerX;
		PlayerZ = nextPlayerZ;
		TargetX = nextTargetX;
		TargetZ = nextTargetZ;
		distanceDisplaySteps = nextDistanceDisplaySteps;
		energyDisplayPercent = nextEnergyDisplayPercent;
		var speed = CurrentAction switch
		{
			GodotOverlayAction.GuardBreak => 2.1f,
			GodotOverlayAction.BlinkStep => 4.8f,
			_ => 3.2f
		};
		EffectPulse = 0.5f + Mathf.Sin((float)elapsedSeconds * speed) * 0.5f;
		if (changed)
		{
			hudRevision++;
			renderedHudRevision = hudRevision;
		}
		return changed;
	}
}

public enum GodotOverlayAction : byte
{
	Strike = 0,
	GuardBreak = 1,
	BlinkStep = 2
}
