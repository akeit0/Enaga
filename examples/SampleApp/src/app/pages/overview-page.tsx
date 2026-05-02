import React from "react";
import { TextInput, HostStateMask, StyleSheet, useHostState, View } from "../../lib/react-okojo";
import { overviewBullets } from "../catalog-data";
import { SectionBodyColumn, Badge, CatalogPage, MetricTile, NotesSectionCard, PageHeader, SectionCard, useCatalogPageWidth } from "../catalog-ui";
import { catalogColors } from "../catalog-theme";

const overviewTitle = "Native React showcase catalog";
const overviewSummary = "A practical reference app for the desktop-native renderer, split into catalog pages and reusable modules.";

export function OverviewPage() {
  const host = useHostState(HostStateMask.Layout | HostStateMask.Keyboard);
  const width = useCatalogPageWidth();
  const [title, setTitle] = React.useState("Desktop-native note");
  const mountToken = React.useRef(Math.random().toString(36).slice(2, 8));
  const renderCount = React.useRef(0);
  renderCount.current += 1;
  return (
    <CatalogPage
      width={width}
      spacing="compact"
      header={(
        <PageHeader
          width={width}
          title={overviewTitle}
          summary={overviewSummary}
          badges={(
            <>
              <Badge text="SkiaSharp + Vulkan" />
              <Badge text="Host-owned input" tone="success" />
              <Badge text="TSX runtime" tone="warning" />
            </>
          )}
        />
      )}
    >
      <View style={styles.metricsRow}>
        <MetricTile label="Viewport" value={`${host.width} x ${host.height}`} style={styles.metricTileFlex} />
        <MetricTile label="Input path" value={host.lastInputSynthetic ? "synthetic" : "live"} style={styles.metricTileFlex} />
        <MetricTile label="Last key" value={host.lastKey || "none"} style={styles.metricTileFlex} />
        <MetricTile label="Mount token" value={mountToken.current} style={styles.metricTileFlex} />
        <MetricTile label="Render count" value={`${renderCount.current}`} style={styles.metricTileFlex} />
      </View>
      <SectionCard title="Single-line field" subtitle="Good for search, labels, or command bars.">
        <SectionBodyColumn>
          <TextInput
            value={title}
            onChangeText={setTitle}
            placeholder="Catalog title"
            style={styles.singleLineInput}
          />
        </SectionBodyColumn>
      </SectionCard>
      <NotesSectionCard title="What this catalog covers" subtitle="Each tab demonstrates a surface that should be practical in a real app." notes={overviewBullets} fontSize={15} rowGap={10} />
    </CatalogPage>
  );
}

const styles = StyleSheet.create({
  singleLineInput: {
    backgroundColor: catalogColors.input,
    borderColor: catalogColors.border,
    activeBorderColor: catalogColors.activeInput,
    color: catalogColors.title,
    height: 45,
  },
  metricsRow: {
    flexDirection: "row",
    height: 82,
    gap: 12,
  },
  metricTileFlex: {
    flex: 1,
  },
});
