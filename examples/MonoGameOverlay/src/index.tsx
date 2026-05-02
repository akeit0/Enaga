import React from "react";
import { HostStateMask, Label, Pane, Scene, StyleSheet, View, mountNativeApp, useAnimationLoop, useHostState } from "./lib/react-okojo";
import type { FlexDirection, StackAlign, TextAlign } from "./lib/react-okojo";

declare function overlaySelectAction(action: string): void;
declare const overlaySelectedAction: string;
declare const overlayUiCommandCount: number;
declare const overlayActionSummary: string;
declare const overlayActorX: number;
declare const overlayActorY: number;
declare const overlayTargetX: number;
declare const overlayTargetY: number;
declare const overlayDistanceToTarget: number;
declare const overlayEffectPulse: number;

function MonoGameOverlayHud() {
  useAnimationLoop(true);
  const host = useHostState(HostStateMask.Layout | HostStateMask.HoverTarget | HostStateMask.PointerPress | HostStateMask.Animation);
  const compact = host.width < 980;
  const hp = 0.68 + Math.sin(host.elapsedMs / 900) * 0.04;
  const guard = 0.42 + Math.cos(host.elapsedMs / 1200) * 0.05;
  const selectedAction = overlaySelectedAction;
  const commandCount = overlayUiCommandCount;
  const sync = Math.max(0, Math.min(1, 1 - overlayDistanceToTarget / Math.max(320, Math.min(host.width, host.height))));
  const effect = Math.max(0, Math.min(1, overlayEffectPulse));
  const rightPanelLeft = Math.max(16, host.width - 304);
  const choiceTop = Math.max(compact ? 440 : 220, host.height - 196);
  const noticeTop = compact ? Math.max(306, host.height - 318) : Math.max(208, host.height - 132);

  return (
    <Scene backgroundColor="#00000000">
      <View style={[styles.root, { width: host.width, height: host.height }]}>
        <Pane id="player-hud" hoverable style={[styles.playerHud, compact ? styles.playerHudCompact : undefined]}>
          <View style={styles.row}>
            <View style={styles.avatar}>
              <Label text="A" style={styles.avatarText} />
            </View>
            <View style={styles.column}>
              <Label text="Astra" style={styles.name} />
              <Label text={`Phase knight / ${selectedAction}`} style={styles.muted} />
            </View>
          </View>
          <Meter label="HP" value={hp} color="#e84f5f" />
          <Meter label="Guard" value={guard} color="#57b6ff" />
          <Meter label="Sync" value={sync} color="#f59e0b" />
        </Pane>

        <Pane
          id="target-card"
          hoverable
          style={[
            styles.targetCard,
            compact ? styles.targetCardCompact : { left: rightPanelLeft },
          ]}
        >
          <Label text={`Command ${commandCount}`} style={styles.sectionLabel} />
          <Label text={selectedAction} style={styles.targetName} />
          <Meter label="Drive" value={effect} color="#d7ba7d" />
          <Label text={overlayActionSummary} style={styles.targetCopy} />
        </Pane>

        <View
          style={[
            styles.choiceStack,
            compact ? styles.choiceStackCompact : { left: rightPanelLeft, top: choiceTop },
          ]}
        >
          <Choice title="Strike" detail="Fast hit / slash burst" selected={selectedAction === "Strike"} />
          <Choice title="Guard Break" detail="Heavy stance damage" selected={selectedAction === "Guard Break"} />
          <Choice title="Blink Step" detail="Evade and reposition" selected={selectedAction === "Blink Step"} />
        </View>

        <Pane id="notice" hoverable style={[styles.notice, compact ? styles.noticeCompact : { top: noticeTop }]}>
          <Label text="Game bridge" style={styles.noticeTitle} />
          <Label text={`Actor ${overlayActorX}, ${overlayActorY}`} style={styles.noticeCopy} />
          <Label text={`Target ${overlayTargetX}, ${overlayTargetY}`} style={styles.noticeCopy} />
          <Label text="Empty overlay space still passes input through to MonoGame." style={styles.noticeCopy} />
        </Pane>
      </View>
    </Scene>
  );
}

function Meter({ label, value, color }: { label: string; value: number; color: string }) {
  const width = 210;
  const fill = Math.max(0, Math.min(width, Math.round(width * value)));
  return (
    <View style={styles.meterRow}>
      <Label text={label} style={styles.meterLabel} />
      <View style={[styles.meterTrack, { width }]}>
        <View style={[styles.meterFill, { width: fill, backgroundColor: color }]} />
      </View>
    </View>
  );
}

function Choice({ title, detail, selected }: { title: string; detail: string; selected: boolean }) {
  return (
    <Pane
      id={`choice-${title}`}
      hoverable
      style={[styles.choice, selected ? styles.choiceSelected : undefined]}
      hoverStyle={styles.choiceHover}
      onPress={() => overlaySelectAction(title)}
    >
      <Label text={title} style={[styles.choiceTitle, selected ? styles.choiceTitleSelected : undefined]} />
      <Label text={detail} style={[styles.choiceDetail, selected ? styles.choiceDetailSelected : undefined]} />
    </Pane>
  );
}

const styles = StyleSheet.create({
  root: {
    position: "relative",
    backgroundColor: "#00000000",
  },
  row: {
    flexDirection: "row" as FlexDirection,
    alignItems: "center" as StackAlign,
    gap: 12,
    height: 48,
  },
  column: {
    flexDirection: "column" as FlexDirection,
    gap: 2,
  },
  playerHud: {
    position: "absolute",
    left: 24,
    top: 22,
    width: 330,
    height: 192,
    padding: 16,
    gap: 10,
    backgroundColor: "#111827d9",
    borderColor: "#8aa4c766",
    borderWidth: 1,
    borderRadius: 8,
    shadow: { color: "#00000070", offsetY: 8, blur: 18 },
  },
  playerHudCompact: {
    left: 14,
    top: 14,
  },
  avatar: {
    width: 44,
    height: 44,
    borderRadius: 8,
    backgroundColor: "#2f6f73",
    borderColor: "#78d1cf",
    borderWidth: 1,
  },
  avatarText: {
    left: 0,
    top: 9,
    width: 44,
    height: 24,
    textAlign: "center" as TextAlign,
    fontSize: 20,
    fontWeight: 800,
    color: "#ecfeff",
  },
  name: {
    width: 210,
    height: 24,
    fontSize: 20,
    fontWeight: 800,
    color: "#f8fafc",
  },
  muted: {
    width: 230,
    height: 18,
    fontSize: 12,
    color: "#b7c0ce",
  },
  meterRow: {
    flexDirection: "row" as FlexDirection,
    alignItems: "center" as StackAlign,
    gap: 10,
    height: 22,
  },
  meterLabel: {
    width: 48,
    height: 18,
    fontSize: 12,
    fontWeight: 800,
    color: "#d7e1ee",
  },
  meterTrack: {
    height: 12,
    borderRadius: 4,
    backgroundColor: "#243041",
    overflow: "hidden",
  },
  meterFill: {
    left: 0,
    top: 0,
    height: 12,
    borderRadius: 4,
  },
  targetCard: {
    position: "absolute",
    top: 24,
    width: 280,
    height: 144,
    padding: 14,
    gap: 8,
    backgroundColor: "#18131f",
    borderColor: "#d7ba7d66",
    borderWidth: 1,
    borderRadius: 8,
  },
  targetCardCompact: {
    top: 188,
    left: 14,
  },
  sectionLabel: {
    width: 120,
    height: 16,
    fontSize: 11,
    fontWeight: 800,
    color: "#d7ba7d",
  },
  targetName: {
    width: 230,
    height: 24,
    fontSize: 18,
    fontWeight: 800,
    color: "#fff7e2",
  },
  targetCopy: {
    width: 242,
    height: 28,
    fontSize: 11,
    color: "#d7d2c8",
  },
  choiceStack: {
    position: "absolute",
    width: 260,
    height: 158,
    gap: 10,
  },
  choiceStackCompact: {
    left: 16,
    top: 428,
  },
  choice: {
    width: 260,
    height: 46,
    padding: 8,
    gap: 2,
    borderRadius: 7,
    backgroundColor: "#1f2937dd",
    borderColor: "#6b7280aa",
    borderWidth: 1,
  },
  choiceHover: {
    backgroundColor: "#256f68ee",
    borderColor: "#7dd3c7",
  },
  choiceSelected: {
    backgroundColor: "#374151ee",
    borderColor: "#f8d57e",
  },
  choiceTitle: {
    width: 228,
    height: 17,
    fontSize: 15,
    fontWeight: 800,
    color: "#f8fafc",
  },
  choiceTitleSelected: {
    color: "#fff8dd",
  },
  choiceDetail: {
    width: 228,
    height: 14,
    fontSize: 11,
    color: "#b7c0ce",
  },
  choiceDetailSelected: {
    color: "#f3e8bf",
  },
  notice: {
    position: "absolute",
    left: 26,
    width: 330,
    height: 88,
    padding: 14,
    gap: 5,
    backgroundColor: "#112018d9",
    borderColor: "#6ee7a866",
    borderWidth: 1,
    borderRadius: 8,
  },
  noticeCompact: {
    left: 16,
    top: 362,
  },
  noticeTitle: {
    width: 280,
    height: 20,
    fontSize: 15,
    fontWeight: 800,
    color: "#dcfce7",
  },
  noticeCopy: {
    width: 290,
    height: 18,
    fontSize: 12,
    color: "#b8d8c5",
  },
});

mountNativeApp(MonoGameOverlayHud);
