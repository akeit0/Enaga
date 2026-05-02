import type { ComponentProps } from "react";
import { HostStateMask, Label, Pane, StyleSheet, createLinearGradient, useAnimationLoop, useHostState } from "../../lib/react-okojo";
import { animationNotes } from "../catalog-data";
import { catalogColors } from "../catalog-theme";
import { Badge, CatalogPage, NotesSectionCard, PageHeader, SectionBodyColumn, SectionCard, useCatalogPageWidth } from "../catalog-ui";

const animationTitle = "Animation";
const animationSummary = "This page is a direct sample of the opt-in animation path rather than a general effects catch-all.";
const trackFillGradient = createLinearGradient(["#38bdf8", "#22c55e"], { startX: 0, startY: 0, endX: 1, endY: 0 });

type AnimationMotionState = {
  elapsedMs: number;
  orbitX: number;
  orbitY: number;
  progressPercent: number;
  progressWidth: number;
};

export function AnimationPage() {
  const width = useCatalogPageWidth();

  return (
    <CatalogPage
      width={width}
      spacing="section"
      header={<PageHeader width={width} title={animationTitle} summary={animationSummary} badges={<Badge text="opt-in loop" />} />}
    >
      <AnimationMotionSection width={width} style={styles.motionCard} />
      <NotesSectionCard title="Animation notes" subtitle="Why the runtime loop is claimed from the app side." notes={animationNotes} />
    </CatalogPage>
  );
}

type AnimationMotionSectionProps = {
  width: number;
  style?: ComponentProps<typeof SectionCard>["style"];
};

function resolveAnimationMotionState(elapsedMs: number, width: number): AnimationMotionState {
  const time = elapsedMs * 0.001;
  const motionPhase = (Math.sin(time * 2.2) + 1) * 0.5;
  const orbitPhase = time * 1.4;

  return {
    elapsedMs,
    orbitX: Math.floor((width - 96) * (0.5 + Math.cos(orbitPhase) * 0.36)),
    orbitY: Math.floor(16 + Math.sin(orbitPhase * 1.35) * 10),
    progressPercent: Math.round(motionPhase * 100),
    progressWidth: Math.max(36, Math.floor((width - 72) * motionPhase)),
  };
}

function AnimationMotionSection(props: AnimationMotionSectionProps) {
  return (
    <SectionCard title="Animation sample" subtitle="The page claims animation explicitly with useAnimationLoop(true)." style={props.style}>
      <AnimationMotionContent width={props.width} style={styles.motionBody} />
    </SectionCard>
  );
}

type AnimationMotionContentProps = {
  width: number;
  style?: ComponentProps<typeof SectionBodyColumn>["style"];
};

function AnimationMotionContent(props: AnimationMotionContentProps) {
  const host = useHostState(HostStateMask.Animation);
  useAnimationLoop(true);
  const motion = resolveAnimationMotionState(host.elapsedMs, props.width);

  return (
    <SectionBodyColumn style={props.style}>
      <Pane style={styles.track}>
        <Pane style={[styles.trackFill, { width: motion.progressWidth, backgroundGradient: trackFillGradient }]} />
      </Pane>
      <Pane style={styles.stage}>
        <Pane style={[styles.orbit, { left: motion.orbitX, top: motion.orbitY }]} />
      </Pane>
      <Label
        text={`elapsed ${(motion.elapsedMs / 1000).toFixed(2)}s  progress ${motion.progressPercent}%`}
        style={[styles.motionText, styles.motionReadout]}
      />
    </SectionBodyColumn>
  );
}

const styles = StyleSheet.create({
  motionCard: {
    height: 186,
  },
  motionBody: {
    height: 102,
    gap: 10,
  },
  track: {
    height: 18,
    backgroundColor: "#0f172a",
    borderColor: "#1e293b",
    borderWidth: 1,
    borderRadius: 999,
  },
  trackFill: {
    left: 0,
    top: 0,
    height: 18,
    borderRadius: 999,
  },
  stage: {
    height: 58,
    backgroundColor: "#0b1220",
    borderColor: "#1e293b",
    borderWidth: 1,
    borderRadius: 14,
  },
  orbit: {
    width: 24,
    height: 24,
    backgroundColor: "#c084fc",
    borderColor: "#e9d5ff",
    borderWidth: 1,
    borderRadius: 999,
  },
  motionText: {
    color: catalogColors.note,
    fontSize: 13,
  },
  motionReadout: {
    height: 16,
  },
});
