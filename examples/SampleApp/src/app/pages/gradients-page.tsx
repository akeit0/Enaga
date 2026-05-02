import { View, StyleSheet, createLinearGradient, createRadialGradient } from "../../lib/react-okojo";
import { gradientNotes } from "../catalog-data";
import { Badge, CatalogPage, NotesSectionCard, PageHeader, SectionCard, SectionHeroCopy, SwatchStrip, SwatchTile, useCatalogPageWidth } from "../catalog-ui";

const extraGradientNotes = [
  ...gradientNotes,
  "Radial gradients are useful for badges, hero cards, and soft vignette treatments without dropping to shader code.",
];
const gradientsTitle = "Gradients";
const gradientsSummary = "Static native gradients are just ordinary scene backgrounds, now including radial fills for softer spotlight-style surfaces.";

export function GradientsPage() {
  const width = useCatalogPageWidth();
  return (
    <CatalogPage
      width={width}
      headerSpacing="tight"
      spacing={22}
      header={<PageHeader width={width} title={gradientsTitle} summary={gradientsSummary} badges={<Badge text="no animation needed" />} />}
    >
      <View style={styles.pageSectionGroup}>
        <SectionCard
          title="Gradient hero panel"
          subtitle="Direction, stop placement, and palette all stay on the app side."
          style={styles.heroCard}
          backgroundGradient={createLinearGradient(["#2563eb", "#7c3aed", "#ec4899"], { stops: [0, 0.55, 1], startX: 0, startY: 0, endX: 1, endY: 1 })}
        >
          <SectionHeroCopy
            title="Native linear gradients"
            summary="These backgrounds render through the normal scene painter without introducing a special gradient host node."
            summaryStyle={styles.heroSummary}
          />
        </SectionCard>
        <SwatchStrip style={styles.swatchStrip}>
          <SwatchTile style={[styles.tallSwatch, {
            backgroundGradient: createLinearGradient(["#2563eb", "#7c3aed"],
               { startX: 0, startY: 0, endX: 1, endY: 1 })
          }]} />
          <SwatchTile style={[styles.tallSwatch, {
            backgroundGradient: createLinearGradient(["#0f766e", "#22c55e", "#fde047"],
               { stops: [0, 0.6, 1], startX: 0, startY: 1, endX: 1, endY: 0 })
          }]} />
          <SwatchTile style={[styles.tallSwatch, {
            backgroundGradient: createRadialGradient(["#0f172a", "#1d4ed8", "#93c5fd"],
               { stops: [0, 0.5, 1], centerX: 0.5, centerY: 0.4, radius: 0.8 })
          }]} />
        </SwatchStrip>
      </View>
      <NotesSectionCard title="Gradient notes" subtitle="What makes gradients a good default decoration primitive." notes={extraGradientNotes} />
    </CatalogPage>
  );
}

const styles = StyleSheet.create({
  pageSectionGroup: {
    gap: 24,
  },
  heroCard: {
    height: 214,
  },
  heroSummary: {
    color: "#e0e7ff",
  },
  swatchStrip: {
    height: 184,
  },
  tallSwatch: {
    height: 148,
  },
});
