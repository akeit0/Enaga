import { View, StyleSheet, useShaderAnimation } from "../../lib/react-okojo";
import { shaderNotes } from "../catalog-data";
import { createPlasmaShader, createRadialPulseShader, createScanlineShader } from "../shaders/runtime-shaders";
import { Badge, CatalogPage, NotesSectionCard, PageHeader, SectionCard, SectionHeroCopy, SwatchStrip, SwatchTile, useCatalogPageWidth } from "../catalog-ui";

const shadersTitle = "Shaders";
const shadersSummary = "Skia runtime effects now render directly into scene backgrounds through app-side shader helpers.";

export function ShadersPage() {
  const width = useCatalogPageWidth();
  useShaderAnimation(true);

  return (
    <CatalogPage
      width={width}
      headerSpacing="tight"
      spacing={22}
      header={<PageHeader width={width} title={shadersTitle} summary={shadersSummary} badges={<Badge text="runtime effects" />} />}
    >
      <View style={styles.pageSectionGroup}>
        <SectionCard
          title="Plasma shader"
          subtitle="This panel binds size, time, and palette uniforms before the painter draws the card."
          style={styles.heroCard}
          backgroundShader={createPlasmaShader(0, "#38bdf8", "#0f172a")}
        >
          <SectionHeroCopy
            title="Runtime shader preview"
            summary="Shader source stays on the app side while the host just compiles and binds the provided spec."
            summaryStyle={styles.heroSummary}
          />
        </SectionCard>
        <SwatchStrip style={styles.swatchStrip}>
          <SwatchTile style={[styles.swatchTile, { backgroundShader: createPlasmaShader(0, "#38bdf8", "#082f49") }]} />
          <SwatchTile style={[styles.swatchTile, { backgroundShader: createScanlineShader(0, "#5eead4", "#082f49") }]} />
          <SwatchTile style={[styles.swatchTile, { backgroundShader: createRadialPulseShader(0, "#c084fc", "#111827") }]} />
        </SwatchStrip>
      </View>
      <NotesSectionCard title="Shader notes" subtitle="How runtime-effect shaders fit the current host architecture." notes={shaderNotes} />
    </CatalogPage>
  );
}

const styles = StyleSheet.create({
  pageSectionGroup: {
    gap: 24,
  },
  heroCard: {
    height: 230,
  },
  heroSummary: {
    color: "#dbeafe",
  },
  swatchStrip: {
    height: 166,
  },
  swatchTile: {
    height: 130,
  },
});
