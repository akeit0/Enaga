import React from "react";
import { HostStateMask, Label, Pane, Scene, StyleSheet, View, createStore, useHostState } from "./lib/react-okojo";
import type { FlexDirection, StackAlign, Position, StackJustify } from "./lib/react-okojo";

declare function godotSelectAction(action: string): void;
declare const godotSelectedAction: string;
declare const godotScore: number;
declare const godotCombo: number;
declare const godotCrystalsCollected: number;
declare const godotEnergy: number;
declare const godotDistanceToTarget: number;
declare const godotPlayerX: number;
declare const godotPlayerZ: number;
declare const godotTargetX: number;
declare const godotTargetZ: number;
declare const godotObjective: string;
declare const gdprint: (message: string) => void;
type ActionName = "Strike" | "Guard Break" | "Blink Step";
type ActionMeta = {
  accent: string;
  summary: string;
  hint: string;
};

type CommandDefinition = {
  title: ActionName;
  key: string;
  accent: string;
  detail: string;
};

type HudStoreState = {
  selectedAction: ActionName;
  score: number;
  combo: number;
  crystals: number;
  energy: number;
  distanceToTarget: number;
  playerX: number;
  playerZ: number;
  targetX: number;
  targetZ: number;
  objective: string;
};

const COMMANDS: readonly CommandDefinition[] = [
  { title: "Strike", key: "1", accent: "#ef6f51", detail: "Balanced" },
  { title: "Guard Break", key: "2", accent: "#f5c451", detail: "Burst" },
  { title: "Blink Step", key: "3", accent: "#7dd3fc", detail: "Sprint" },
];

const METER_TRACK_WIDTH = 180;

const hudStore = createStore<HudStoreState>(readRawHudSnapshot());
const STATUS_FIELDS = ["selectedAction", "score", "combo", "crystals", "energy"] as const;
const TELEMETRY_FIELDS = ["energy", "distanceToTarget", "playerX", "playerZ", "targetX", "targetZ"] as const;
const OBJECTIVE_FIELDS = ["selectedAction", "objective"] as const;
const ACTION_FIELDS = ["selectedAction"] as const;

type GodotHudGlobals = {
  __godotOverlayHudUpdate?: (
    selectedAction: string,
    score: number,
    combo: number,
    crystals: number,
    energy: number,
    distanceToTarget: number,
    playerX: number,
    playerZ: number,
    targetX: number,
    targetZ: number,
    objective: string,
  ) => void;
};

const godotHudGlobals = globalThis as GodotHudGlobals;

godotHudGlobals.__godotOverlayHudUpdate = (
  selectedActionValue,
  score,
  combo,
  crystals,
  energy,
  distanceToTarget,
  playerX,
  playerZ,
  targetX,
  targetZ,
  objective,
) => {
  hudStore.batch(() => {
    hudStore.setField("selectedAction", normalizeAction(selectedActionValue));
    hudStore.setField("score", score);
    hudStore.setField("combo", combo);
    hudStore.setField("crystals", crystals);
    hudStore.setField("energy", clamp01(energy));
    hudStore.setField("distanceToTarget", Math.max(0, distanceToTarget));
    hudStore.setField("playerX", playerX);
    hudStore.setField("playerZ", playerZ);
    hudStore.setField("targetX", targetX);
    hudStore.setField("targetZ", targetZ);
    hudStore.setField("objective", normalizeObjective(objective));
  });
};

export function GodotOverlayHud() {
  const host = useHostState(HostStateMask.Layout);
  const handleSelectAction = React.useCallback((action: ActionName) => {
    godotSelectAction(action);
  }, []);

  return (
    <Scene backgroundColor="#00000000">
      <View style={[styles.root, { width: host.width, height: host.height }]}>
        <StatusPanel />
        <TelemetryPanel />
        <ObjectivePanel />
        <CommandDock
          onSelectAction={handleSelectAction}
        />
      </View>
    </Scene>
  );
}

function readRawHudSnapshot(): HudStoreState {
  const objectiveRaw = godotObjective;
  return createRawHudSnapshot(
    godotSelectedAction,
    godotScore,
    godotCombo,
    godotCrystalsCollected,
    godotEnergy,
    godotDistanceToTarget,
    godotPlayerX,
    godotPlayerZ,
    godotTargetX,
    godotTargetZ,
    objectiveRaw,
  );
}

function createRawHudSnapshot(
  selectedActionValue: string,
  score: number,
  combo: number,
  crystals: number,
  energyValue: number,
  distanceToTarget: number,
  playerX: number,
  playerZ: number,
  targetX: number,
  targetZ: number,
  objective: string,
): HudStoreState {
  const selectedAction = normalizeAction(selectedActionValue);
  const energy = clamp01(energyValue);
  const distance = Math.max(0, distanceToTarget);

  return {
    selectedAction,
    score,
    combo,
    crystals,
    energy,
    distanceToTarget: distance,
    playerX,
    playerZ,
    targetX,
    targetZ,
    objective: normalizeObjective(objective),
  };
}

function formatScore(value: number) {
  return Math.max(0, Math.round(value)).toString();
}

function formatCombo(value: number) {
  return `x${Math.round(value)}`;
}

function formatCrystals(value: number) {
  return Math.max(0, Math.round(value)).toString();
}

function formatEnergyText(value: number) {
  return `${Math.round(clamp01(value) * 100)}%`;
}

function formatDistanceMeter(value: number) {
  return clamp01(1 - Math.max(0, value) / 18);
}

function formatDistanceText(value: number) {
  return `${Math.round(Math.max(0, value) * 10) / 10}m`;
}

function formatCoordText(x: number, z: number) {
  return `${Math.round(x)}, ${Math.round(z)}`;
}

function normalizeObjective(objective: string) {
  return objective || "Click the arena floor to move. Use the overlay to switch actions.";
}

function StatusPanel() {
  const { selectedAction, score, combo, crystals, energy } = hudStore.useFields(STATUS_FIELDS);
  const actionMeta = getActionMeta(selectedAction);
  gdprint(`StatusPanel render: action=${selectedAction}, score=${score}, combo=${combo}, crystals=${crystals}, energy=${energy}`);

  return (
    <View style={[styles.panel, styles.statusPanel]}>
      <View style={styles.panelHeader}>
        <View style={[styles.liveDot, { backgroundColor: actionMeta.accent }]} />
        <Label text="RUN" style={styles.headerTag} />
      </View>
      <Label text={formatScore(score)} style={styles.scoreValue} />
      <Label text={actionMeta.summary} style={[styles.modeSummary, { color: actionMeta.accent }]} />
      <View style={styles.statRow}>
        <StatChip title="Combo" value={formatCombo(combo)} />
        <StatChip title="Crystal" value={formatCrystals(crystals)} />
        <StatChip title="Energy" value={formatEnergyText(energy)} />
      </View>
    </View>
  );
}

function TelemetryPanel() {
  const { energy, distanceToTarget, playerX, playerZ, targetX, targetZ } = hudStore.useFields(TELEMETRY_FIELDS);

  return (
    <View style={[styles.panel, styles.telemetryPanel]}>
      <Label text="TARGET" style={styles.headerTag} />
      <View style={styles.coordRow}>
        <CoordChip label="P" value={formatCoordText(playerX, playerZ)} />
        <CoordChip label="T" value={formatCoordText(targetX, targetZ)} />
      </View>
      <Meter label="Energy" value={energy} color="#38bdf8" />
      <Meter label="Lock" value={formatDistanceMeter(distanceToTarget)} color="#f59e0b" />
      <Label text={`Vector ${formatDistanceText(distanceToTarget)}`} style={styles.helperText} />
    </View>
  );
};

function ObjectivePanel() {
  const selectedAction = hudStore.useField("selectedAction");
  const objective = hudStore.useField("objective");
  const actionMeta = getActionMeta(selectedAction);
  gdprint(`ObjectivePanel render: action=${selectedAction}, objective=${objective}`);

  return (
    <View style={[styles.panel, styles.objectivePanel]}>
      <Label text="OBJECTIVE" style={styles.objectiveTag} />
      <Label text={objective} style={styles.objectiveText} />
      <Label text={actionMeta.hint} style={[styles.objectiveHint, { color: actionMeta.accent }]} />
    </View>
  );
};

function CommandDock({
  onSelectAction,
}: {
  onSelectAction: (action: ActionName) => void;
}) {
  const selectedAction = hudStore.useField("selectedAction");
  gdprint(`CommandDock render: selectedAction=${selectedAction}`);
  return (
    <View style={styles.commandDock}>
      {COMMANDS.map((command) => (
        <CommandCard
          command={command}
          active={selectedAction === command.title}
          onSelectAction={onSelectAction}
        />
      ))}
    </View>
  );
}

function CommandCard({
  command,
  active,
  onSelectAction,
}: {
  command: CommandDefinition;
  active: boolean;
  onSelectAction: (action: ActionName) => void;
}) {
  const handlePress = React.useCallback(() => onSelectAction(command.title), [command.title, onSelectAction]);
  return (
    <Pane
      id={`godot-command-${command.title}`}
      hoverable
      style={[
        styles.commandCard,
        active ? [styles.commandCardActive, { borderColor: command.accent }] : undefined,
      ]}
      hoverStyle={[styles.commandCardHover, { borderColor: command.accent }]}
      onPress={handlePress}
    >
      <View style={[styles.commandAccent, { backgroundColor: command.accent }]} />
      <View style={styles.commandTitleRow}>
        <Label text={command.key} style={[styles.commandKey, { color: command.accent }]} />
        <Label text={command.title} style={styles.commandTitle} />
      </View>
      <Label text={command.detail} style={styles.commandDetail} />
    </Pane>
  );
};

function CoordChip({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.coordChip}>
      <Label text={label} style={styles.coordChipTag} />
      <Label text={value} style={styles.coordChipValue} />
    </View>
  );
};

function StatChip({ title, value }: { title: string; value: string }) {
  return (
    <View style={styles.statChip}>
      <Label text={title} style={styles.statTitle} />
      <Label text={value} style={styles.statValue} />
    </View>
  );
};

function Meter({ label, value, color }: { label: string; value: number; color: string }) {
  const fill = Math.max(0, Math.min(METER_TRACK_WIDTH, Math.round(METER_TRACK_WIDTH * clamp01(value))));

  return (
    <View style={styles.meterRow}>
      <Label text={label} style={styles.meterLabel} />
      <View style={styles.meterTrack}>
        <View style={[styles.meterFill, { width: fill, backgroundColor: color }]} />
      </View>
    </View>
  );
}

function normalizeAction(action: string): ActionName {
  switch (action) {
    case "Guard Break":
    case "Blink Step":
      return action;
    default:
      return "Strike";
  }
}

function getActionMeta(action: ActionName): ActionMeta {
  switch (action) {
    case "Guard Break":
      return { accent: "#f5c451", summary: "Guard Break", hint: "Wide pickup radius, slower route." };
    case "Blink Step":
      return { accent: "#7dd3fc", summary: "Blink Step", hint: "Fast route, tighter pickup window." };
    default:
      return { accent: "#ef6f51", summary: "Strike", hint: "Balanced movement and scoring." };
  }
}

function clamp01(value: number) {
  return Math.max(0, Math.min(1, value));
}

const styles = StyleSheet.create({
  root: {
    position: "relative",
    backgroundColor: "#00000000",
  },
  panel: {
    position: "absolute",
    borderRadius: 8,
    borderWidth: 1,
    padding: 12,
    gap: 7,
    backgroundColor: "#07111ee6",
    shadow: { color: "#0000006e", offsetY: 8, blur: 16 },
  },
  statusPanel: {
    left: 18,
    top: 18,
    width: 260,
    height: 164,
    borderColor: "#ef6f5170",
  },
  telemetryPanel: {
    right: 18,
    top: 18,
    width: 266,
    height: 148,
    borderColor: "#38bdf870",
  },
  objectivePanel: {
    left: 18,
    bottom: 124,
    width: 360,
    height: 100,
    borderColor: "#65a30d70",
    backgroundColor: "#0d2e198a",
  },
  panelHeader: {
    flexDirection: "row" as FlexDirection,
    alignItems: "center" as StackAlign,
    gap: 7,
    height: 14,
  },
  liveDot: {
    width: 7,
    height: 7,
    borderRadius: 999,
  },
  headerTag: {
    width: 78,
    height: 14,
    fontSize: 10,
    fontWeight: 800,
    color: "#aeb8c7",
  },
  scoreValue: {
    width: 236,
    height: 36,
    fontSize: 31,
    fontWeight: 800,
    color: "#fff7ed",
  },
  modeSummary: {
    width: 236,
    height: 16,
    fontSize: 12,
    fontWeight: 800,
  },
  statRow: {
    flexDirection: "row" as FlexDirection,
    gap: 8,
    height: 42,
  },
  statChip: {
    width: 70,
    height: 40,
    padding: 6,
    borderRadius: 6,
    backgroundColor: "#111827dc",
    borderColor: "#334155",
    borderWidth: 1,
  },
  statTitle: {
    width: 56,
    height: 12,
    fontSize: 9,
    fontWeight: 800,
    color: "#93a4b8",
  },
  statValue: {
    width: 56,
    height: 18,
    fontSize: 14,
    fontWeight: 800,
    color: "#f8fafc",
  },
  coordRow: {
    flexDirection: "row" as FlexDirection,
    gap: 8,
    height: 34,
  },
  coordChip: {
    flexDirection: "row" as FlexDirection,
    alignItems: "center" as StackAlign,
    gap: 7,
    width: 96,
    height: 32,
    padding: 7,
    borderRadius: 6,
    backgroundColor: "#0f172adc",
    borderColor: "#334155",
    borderWidth: 1,
  },
  coordChipTag: {
    width: 14,
    height: 14,
    fontSize: 10,
    fontWeight: 800,
    color: "#7dd3fc",
  },
  coordChipValue: {
    width: 62,
    height: 14,
    fontSize: 11,
    color: "#e2e8f0",
  },
  meterRow: {
    flexDirection: "row" as FlexDirection,
    alignItems: "center" as StackAlign,
    gap: 9,
    height: 20,
  },
  meterLabel: {
    width: 50,
    height: 14,
    fontSize: 11,
    fontWeight: 800,
    color: "#d6e2f1",
  },
  meterTrack: {
    width: METER_TRACK_WIDTH,
    height: 10,
    borderRadius: 999,
    backgroundColor: "#223044",
    overflow: "hidden" as "hidden" | "visible",
  },
  meterFill: {
    left: 0,
    top: 0,
    height: 10,
    borderRadius: 999,
  },
  helperText: {
    width: 120,
    height: 14,
    fontSize: 10,
    color: "#aeb8c7",
  },
  objectiveTag: {
    width: 88,
    height: 14,
    fontSize: 10,
    fontWeight: 800,
    color: "#bef264",
  },
  objectiveText: {
    width: 336,
    height: 38,
    fontSize: 12,
    wrap: true,
    color: "#e7f5d1",
  },
  objectiveHint: {
    width: 336,
    height: 16,
    fontSize: 11,
    fontWeight: 800,
  },
  commandDock: {
    position: "absolute" as Position,
    left: 0,
    bottom: 18,
    width: "100%",
    height: 76,
    flexDirection: "row" as FlexDirection,
    alignItems: "center" as StackAlign,
    justifyContent: "center" as StackJustify,
    gap: 8,
  },
  commandCard: {
    width: 126,
    height: 76,
    padding: 10,
    borderRadius: 8,
    backgroundColor: "#0d418593",
    borderColor: "#3b4758b8",
    borderWidth: 1,
    gap: 5,
    shadow: { color: "#0000005f", offsetY: 7, blur: 13 },
  },
  commandCardHover: {
    backgroundColor: "#ff7d04b2",
  },
  commandCardActive: {
    top: -12,
    width: 126 * 1.1,
    height: 76 * 1.2,
    backgroundColor: "#ff4a4ac5",
  },
  commandAccent: {
    width: 32,
    height: 3,
    borderRadius: 999,
  },
  commandTitleRow: {
    flexDirection: "row" as FlexDirection,
    alignItems: "center" as StackAlign,
    gap: 6,
    height: 20,
  },
  commandKey: {
    width: 16,
    height: 18,
    fontSize: 12,
    fontWeight: 800,
  },
  commandTitle: {
    width: 80,
    height: 18,
    fontSize: 13,
    fontWeight: 800,
    color: "#f8fafc",
  },
  commandDetail: {
    width: 104,
    height: 16,
    fontSize: 10,
    color: "#aeb8c7",
  },
});
