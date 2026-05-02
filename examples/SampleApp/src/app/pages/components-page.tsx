import { View, HostStateMask, Label, Pane, ScrollView, StyleSheet, TextInput, createLinearGradient, createRadialGradient, type ReactStackStyle, useHostState } from "../../lib/react-okojo";
import { componentExamples } from "../catalog-data";
import { catalogColors } from "../catalog-theme";
import { Badge, CatalogPage, MetricTile, PageHeader, SectionBodyColumn, SectionBodyRow, SectionCard, useCatalogPageWidth } from "../catalog-ui";

const outerScrollNotes = [
  "Outer notes keep their own scroll offset.",
  "The inner editor can scroll and keep focus without the outer region stealing wheel input.",
];
const innerScrollInputHint = "Try scrolling here first, then press Tab between the nested inputs.";
const scrollFooterNote = "This demo sits inside the page-level content scroll too, so it exercises nested routing through multiple host layers.";
const horizontalScrollItems = ["Layout", "Inputs", "Images", "Shaders", "Gradients", "Tooltips", "Native panels", "IME"];
const decorationLinearGradient = createLinearGradient(["#1d4ed8", "#7c3aed"], { startX: 0, startY: 0, endX: 1, endY: 1 });
const decorationRadialGradient = createRadialGradient(["#0f172a", "#1e293b", "#38bdf8"], { centerX: 0.35, centerY: 0.25, radius: 0.95 });
const nestedScrollGradient = createLinearGradient(["#111827", "#0f172a"], { startX: 0, startY: 0, endX: 1, endY: 1 });
const decorationShadow = [
  { color: "#111111", offsetX: 10, offsetY: 14, blur: 14, spread: 1 },
];
const outerScrollShadow = [
  { color: "#02061755", offsetY: 8, blur: 14 },
  { color: "#93c5fd1f", offsetY: 1, blur: 18, spread: 1 },
];
const componentsTitle = "Component patterns";
const componentsSummary = "This tab shows practical app-level compositions, plus the newer decoration and nested-input host capabilities.";

export function ComponentsPage() {
  const width = useCatalogPageWidth();
  return (
    <CatalogPage
      width={width}
      headerSpacing={14}
      header={<PageHeader width={width} title={componentsTitle} summary={componentsSummary} />}
    >
      <View style={styles.sectionGroup}>
        <SectionCard title="Reusable patterns" subtitle="These are app-side building blocks, not hardcoded renderer primitives.">
          <SectionBodyColumn style={styles.patternsBody}>
            <SectionBodyRow style={styles.patternBadgesRow}>
              <Badge text="status pill" />
              <Badge text="success state" tone="success" />
              <Badge text="warning state" tone="warning" />
            </SectionBodyRow>
            <SectionBodyColumn style={styles.patternNotes}>
              {componentExamples.map((note, index) => (
                <Label
                  key={index}
                  text={`• ${note}`}
                  style={styles.patternNote}
                />
              ))}
            </SectionBodyColumn>
          </SectionBodyColumn>
        </SectionCard>
        <View style={styles.featureSectionGroup}>
          <SectionCard
            title="Decoration primitives"
            subtitle="The light stage makes elevation easier to read, and the shadows are biased toward the lower-right."
          >
            <SectionBodyRow style={styles.decorationStageRow}>
              <Pane
                style={{
                  ...styles.decorationPane,
                  backgroundGradient: decorationLinearGradient,
                  shadow: decorationShadow,
                  borderColor: "#312e81",
                }}
              >
                <View style={styles.decorationCopy}>
                  <Label text="linear + shadow" style={styles.linearDecorationTitle} />
                  <Label text="Good for elevated cards, panes, and callouts." style={styles.linearDecorationSummary} />
                </View>
              </Pane>
              <Pane
                style={{
                  ...styles.decorationPane,
                  backgroundGradient: decorationRadialGradient,
                  borderColor: "#164e63",
                }}
              >
                <View style={styles.decorationCopy}>
                  <Label text="radial surface" style={styles.radialDecorationTitle} />
                  <Label text="Useful for spotlights, badges, and hero treatment." style={styles.radialDecorationSummary} />
                </View>
              </Pane>
            </SectionBodyRow>
          </SectionCard>
          <SectionCard title="Nested scroll + focus" subtitle="Wheel routing prefers the deepest scroll region that can still move, and Tab cycles native text input focus.">
            <SectionBodyColumn>
              <ScrollView
                style={styles.outerScrollView}
                contentContainerStyle={styles.outerScrollContent}
              >
                {outerScrollNotes.map((note, index) => (
                  <Label
                    key={index}
                    text={note}
                    style={styles.outerScrollNote}
                  />
                ))}
                <ScrollView
                  style={[styles.innerScrollView, { backgroundGradient: nestedScrollGradient }]}
                  contentContainerStyle={styles.innerScrollContent}
                >
                  <Label text={innerScrollInputHint} style={styles.innerScrollHint} />
                  <ScrollView
                    style={styles.horizontalScrollView}
                    contentContainerAxis="row"
                    contentContainerStyle={styles.horizontalScrollContent}
                  >
                    {horizontalScrollItems.map((item) => (
                      <Pane key={item} style={styles.horizontalScrollChip}>
                        <Label text={item} style={styles.horizontalScrollChipText} />
                      </Pane>
                    ))}
                  </ScrollView>
                  <TextInput
                    value="Nested title"
                    style={styles.nestedTitleInput}
                  />
                  <TextInput
                    value={"Arrow Up / Down now keeps column.\nShift + Tab moves focus backward."}
                    style={styles.nestedBodyInput}
                  />
                </ScrollView>
                <Label text={scrollFooterNote} style={styles.outerScrollNote} />
              </ScrollView>
            </SectionBodyColumn>
          </SectionCard>
        </View>
      </View>
    </CatalogPage>
  );
}

const styles = StyleSheet.create({
  metricsRow: {
    height: 82,
    gap: 12,
  },
  sectionGroup: {
    gap: 24,
  },
  featureSectionGroup: {
    gap: 26,
  },
  metricTile: {
    flex: 1,
    minWidth: 150,
  },
  patternsBody: {
    gap: 16,
  },
  patternBadgesRow: {
    height: 26,
    gap: 10,
  },
  patternNotes: {
    gap: 10,
  },
  patternNote: {
    color: catalogColors.note,
    fontSize: 14,
    wrap: true,
  },
  decorationStageRow: {
    gap: 18,
    marginLeft: 32,
    marginRight: 32,
    height: 130,
    padding: 16,
    backgroundColor: "#f8fafc",
    borderColor: "#dbeafe",
    borderWidth: 1,
    borderRadius: 16,
  },
  decorationPane: {
    flex: 1,
    height: 100,
    padding: 32,
    borderWidth: 1,
    borderRadius: 16,
    justifyContent: "end",
  },
  decorationCopy: {
    gap: 12,
    alignItems: "stretch",
  } satisfies ReactStackStyle,
  linearDecorationTitle: {
    color: "#e0e7ff",
    fontSize: 18,
    fontWeight: 700,
  },
  linearDecorationSummary: {
    color: "#cbd5e1",
    fontSize: 13,
  },
  radialDecorationTitle: {
    color: "#e0f2fe",
    fontSize: 18,
    fontWeight: 700,
  },
  radialDecorationSummary: {
    color: "#bae6fd",
    fontSize: 13,
  },
  outerScrollView: {
    height: 168,
    backgroundColor: catalogColors.paneAlt,
    borderColor: catalogColors.border,
    borderWidth: 1,
    borderRadius: 14,
    shadow: outerScrollShadow,
  },
  outerScrollContent: {
    padding: 16,
    paddingBottom: 20,
    gap: 14,
  },
  outerScrollNote: {
    color: catalogColors.note,
    fontSize: 13,
    wrap: true,
  },
  innerScrollView: {
    height: 156,
    borderColor: "#1e293b",
    borderWidth: 1,
    borderRadius: 12,
  },
  innerScrollContent: {
    padding: 14,
    gap: 12,
  },
  innerScrollHint: {
    color: "#cbd5e1",
    fontSize: 13,
    wrap: true,
  },
  horizontalScrollView: {
    height: 46,
    contentWidth: 760,
    backgroundColor: "#020617",
    borderColor: "#334155",
    borderWidth: 1,
    borderRadius: 12,
  },
  horizontalScrollContent: {
    flexDirection: "row",
    width: 760,
    padding: 8,
    gap: 8,
  },
  horizontalScrollChip: {
    width: 88,
    height: 28,
    backgroundColor: "#0f172a",
    borderColor: "#1e40af",
    borderWidth: 1,
    borderRadius: 8,
    paddingHorizontal: 10,
    justifyContent: "center",
  },
  horizontalScrollChipText: {
    color: "#bfdbfe",
    fontSize: 12,
    textAlign: "center",
  },
  nestedTitleInput: {
    height: 40,
    backgroundColor: "#020617",
    borderColor: "#334155",
    activeBorderColor: catalogColors.activeInput,
    color: catalogColors.title,
  },
  nestedBodyInput: {
    height: 82,
    multiline: true,
    lineHeight: 22,
    paddingTop: 12,
    backgroundColor: "#020617",
    borderColor: "#334155",
    activeBorderColor: catalogColors.activeInput,
    color: catalogColors.title,
    borderRadius: 12,
  },
});
