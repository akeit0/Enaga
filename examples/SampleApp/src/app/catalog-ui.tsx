import React from "react";
import { View, Label, Pane, Spacer, StyleSheet, measureTextHeight, measureTextWidth, type BoxShadow, type Gradient, type ReactStackStyle, type ReactTextStyle, type RuntimeShader, type StyleProp, type ViewStyle } from "../lib/react-okojo";
import { catalogColors } from "./catalog-theme";

const pageHeaderGap = 6;
const pageHeaderCopyGap = 4;
const pageHeaderTitleFontSize = 20;
const pageHeaderSummaryFontSize = 13;
const pageHeaderBadgeHeight = 26;
const pageHeaderSummaryMaxWidth = 760;
const sectionCardHeaderInsetX = 16;
const sectionCardHeaderTop = 14;
const sectionCardBodyGapWithSubtitle = 12;
const sectionCardBodyGapWithoutSubtitle = 28;
const swatchTileDefaults = {
  flex: 1,
  minWidth: 96,
  borderRadius: 12,
} satisfies ViewStyle;
const CatalogPageWidthContext = React.createContext<number | undefined>(undefined);
const sectionBodyInsetX = 18;
const sectionBodyBottom = 18;

function flattenSlotChildren(node: React.ReactNode): React.ReactNode[] {
  const result: React.ReactNode[] = [];
  React.Children.forEach(node, (child) => {
    if (React.isValidElement<{ children?: React.ReactNode }>(child) && child.type === React.Fragment) {
      result.push(...flattenSlotChildren(child.props.children));
      return;
    }

    result.push(child);
  });
  return result;
}

function resolveBadgeWidth(text: string) {
  return Math.max(32, measureTextWidth(text, { fontSize: 12, fontWeight: 700 }) + 24);
}

export function insetWidth(width: number, left: number, right = left) {
  return Math.max(0, width - left - right);
}

export function sectionBodyWidth(width: number) {
  return insetWidth(width, sectionBodyInsetX);
}

export function measureWrappedLabelHeight(text: string, width: number, fontSize: number, fontWeight = 400) {
  return measureTextHeight(text, width, { fontSize, fontWeight, wrap: true });
}

function createPageHeaderChildren(title: string, summary: string, badges?: React.ReactNode | boolean, width?: number) {
  const badgeItems = typeof badges === "boolean"
    ? []
    : flattenSlotChildren(badges);
  const resolvedTextWidth = typeof width === "number" && width > 0
    ? Math.min(pageHeaderSummaryMaxWidth, width)
    : pageHeaderSummaryMaxWidth;
  const titleHeight = measureWrappedLabelHeight(title, resolvedTextWidth, pageHeaderTitleFontSize, 700);
  const summaryHeight = measureWrappedLabelHeight(summary, resolvedTextWidth, pageHeaderSummaryFontSize);
  return [
    <View key="copy" style={[styles.pageHeaderCopy, { width: resolvedTextWidth }]}>
      <Label text={title} style={[styles.pageHeaderTitle, { width: resolvedTextWidth, height: titleHeight, wrap: true }]} />
      <Label text={summary} style={[styles.pageHeaderSummary, { width: resolvedTextWidth, height: summaryHeight, wrap: true }]} />
    </View>,
    badgeItems.length > 0 || badges === true ? (
      <View key="badges" style={[styles.pageHeaderBadges, { height: pageHeaderBadgeHeight }]}>
        {badgeItems}
      </View>
    ) : null,
  ];
}

export function CatalogPageWidthProvider({
  width,
  children,
}: {
  width: number;
  children?: React.ReactNode;
}) {
  return (
    <CatalogPageWidthContext.Provider value={width}>
      {children}
    </CatalogPageWidthContext.Provider>
  );
}

export function useCatalogPageWidth() {
  const width = React.useContext(CatalogPageWidthContext);
  if (width == null) {
    throw new Error("Catalog page width is not available outside CatalogPageWidthProvider.");
  }

  return width;
}

function Card(props: React.ComponentProps<typeof Pane>) {
  return <Pane {...props} style={StyleSheet.compose({ borderRadius: 18 }, props.style)} />;
}

export function SectionCard({
  id,
  title,
  subtitle,
  backgroundGradient,
  backgroundShader,
  shadow,
  style,
  children,
}: {
  id?: string;
  title: string;
  subtitle?: string;
  backgroundGradient?: Gradient;
  backgroundShader?: RuntimeShader;
  shadow?: BoxShadow | readonly BoxShadow[];
  style?: StyleProp<ViewStyle>;
  children?: React.ReactNode;
}) {
  return (
    <View
      id={id}
      style={[
        styles.sectionCard,
        subtitle ? styles.sectionCardWithSubtitle : styles.sectionCardWithoutSubtitle,
        style,
        { backgroundGradient, backgroundShader, shadow },
      ]}
    >
      <View style={styles.sectionHeader}>
        <Label text={title} style={[styles.sectionTitle, { wrap: true }]} />
        {subtitle ? (
          <Label text={subtitle} style={[styles.sectionSubtitle, { wrap: true }]} />
        ) : null}
      </View>
      {children}
    </View>
  );
}

export function SectionBodyColumn({
  id,
  style,
  children,
}: {
  id?: string;
  style?: StyleProp<ReactStackStyle>;
  children?: React.ReactNode;
}) {
  return (
    <View
      id={id}
      style={[
        styles.sectionBodyColumn,
        style,
      ]}
    >
      {children}
    </View>
  );
}

export function SectionBodyRow({
  id,
  style,
  children,
}: {
  id?: string;
  style?: StyleProp<ReactStackStyle>;
  children?: React.ReactNode;
}) {
  return (
    <View
      id={id}
      style={[
        styles.sectionBodyRow,
        style,
      ]}
    >
      {children}
    </View>
  );
}

export function NotesSectionCard({
  id,
  title,
  subtitle,
  notes,
  style,
  color = catalogColors.note,
  fontSize = 13,
  rowGap = 8,
}: {
  id?: string;
  title: string;
  subtitle?: string;
  notes: readonly string[];
  style?: StyleProp<ViewStyle>;
  color?: string;
  fontSize?: number;
  rowGap?: number;
}) {
  return (
    <SectionCard id={id} title={title} subtitle={subtitle} style={style}>
      <SectionBodyColumn style={{ gap: rowGap }}>
        {notes.map((note, index) => (
          <Label
            key={index}
            text={`• ${note}`}
            style={{ color, fontSize, wrap: true }}
          />
        ))}
      </SectionBodyColumn>
    </SectionCard>
  );
}

export function Badge({
  id,
  text,
  tone = "accent",
  style,
}: {
  id?: string;
  text: string;
  tone?: "accent" | "success" | "warning";
  style?: StyleProp<ViewStyle>;
}) {
  const resolvedStyle = StyleSheet.flatten(style) ?? {};
  const width = typeof resolvedStyle.width === "number"
    ? resolvedStyle.width
    : resolveBadgeWidth(text);
  const backgroundColor = tone === "success"
    ? "#123c2f"
    : tone === "warning"
      ? "#3f2b12"
      : "#10233f";
  const color = tone === "success"
    ? "#86efac"
    : tone === "warning"
      ? "#fcd34d"
      : catalogColors.accent;
  return (
    <Pane
      id={id}
      style={[styles.badge, style, { width, backgroundColor, borderColor: backgroundColor }]}
    >
      <Label text={text} style={[styles.badgeText, { width: width - 24, color }]} />
    </Pane>
  );
}

export function MetricTile({
  id,
  label,
  value,
  style,
}: {
  id?: string;
  label: string;
  value: string;
  style?: StyleProp<{ left?: number; top?: number; flex?: number; width?: number; minWidth?: number; maxWidth?: number; alignSelf?: "auto" | "start" | "center" | "end" | "stretch" }>;
}) {
  return (
    <Card
      id={id}
      style={[styles.metricCard, { height: 82 }, style]}
    >
      <View style={styles.metricContent}>
        <Label text={label} style={styles.metricLabel} />
        <Label text={value} style={styles.metricValue} />
      </View>
    </Card>
  );
}

export function PageHeader({
  id,
  title,
  summary,
  badges,
  width,
  style,
}: {
  id?: string;
  title: string;
  summary: string;
  badges?: React.ReactNode;
  width?: number;
  style?: StyleProp<ViewStyle>;
}) {
  return (
    <View
      id={id}
      style={[styles.pageHeader, style]}
    >
      {createPageHeaderChildren(title, summary, badges, width)}
    </View>
  );
}

export function CatalogPage({
  id,
  width,
  height,
  header,
  headerSpacing = "regular",
  spacing = "regular",
  style,
  children,
}: {
  id?: string;
  width: number;
  height?: number;
  header?: React.ReactNode;
  headerSpacing?: PageSpacerSize;
  spacing?: PageSpacerSize;
  style?: StyleProp<ReactStackStyle>;
  children?: React.ReactNode;
}) {
  const bodyItems = flattenSlotChildren(children);
  const resolvedSpacing = resolvePageSpacerSize(spacing);
  const resolvedHeaderSpacing = resolvePageSpacerSize(headerSpacing);
  return (
    <View id={id} style={[{ width, alignItems: "stretch" }, typeof height === "number" ? { height } : undefined, style]}>
      {header}
      {header != null && bodyItems.length > 0 ? <Spacer size={resolvedHeaderSpacing} flex={0} /> : null}
      <View style={{ width, alignItems: "stretch", gap: resolvedSpacing }}>
        {bodyItems}
      </View>
    </View>
  );
}

export function SectionHeroCopy({
  title,
  summary,
  style,
  titleStyle,
  summaryStyle,
}: {
  title: string;
  summary: string;
  style?: StyleProp<ReactStackStyle>;
  titleStyle?: StyleProp<ReactTextStyle>;
  summaryStyle?: StyleProp<ReactTextStyle>;
}) {
  return (
    <SectionBodyColumn style={[styles.sectionHeroCopy, style]}>
      <Label text={title} style={[styles.sectionHeroTitle, titleStyle]} />
      <Label text={summary} style={[styles.sectionHeroSummary, summaryStyle]} />
    </SectionBodyColumn>
  );
}

export function SwatchStrip({
  style,
  children,
}: {
  style?: StyleProp<ReactStackStyle>;
  children?: React.ReactNode;
}) {
  return (
    <View style={[styles.swatchStrip, style]}>
      {children}
    </View>
  );
}

export function SwatchTile(props: React.ComponentProps<typeof Pane>) {
  return <Pane {...props} style={[styles.swatchTile, props.style]} />;
}

export const pageSpacerSizes = Object.freeze({
  tight: 8,
  compact: 12,
  regular: 16,
  relaxed: 20,
  section: 24,
});

export type PageSpacerSize = keyof typeof pageSpacerSizes | number;

export function resolvePageSpacerSize(size: PageSpacerSize = "regular") {
  return typeof size === "number" ? size : pageSpacerSizes[size];
}

const styles = StyleSheet.create({
  sectionCard: {
    backgroundColor: catalogColors.panel,
    borderColor: catalogColors.border,
    borderRadius: 14,
    paddingBottom: sectionBodyBottom,
    alignItems: "stretch",
  },
  sectionCardWithSubtitle: {
    gap: sectionCardBodyGapWithSubtitle,
  },
  sectionCardWithoutSubtitle: {
    gap: sectionCardBodyGapWithoutSubtitle,
  },
  sectionHeader: {
    paddingLeft: sectionCardHeaderInsetX,
    paddingTop: sectionCardHeaderTop,
    paddingRight: sectionCardHeaderInsetX,
    gap: 2,
    alignItems: "stretch",
  } satisfies ReactStackStyle,
  sectionBodyColumn: {
    paddingLeft: sectionBodyInsetX,
    paddingRight: sectionBodyInsetX,
    alignItems: "stretch",
  },
  sectionBodyRow: {
    flexDirection: "row",
    paddingLeft: sectionBodyInsetX,
    paddingRight: sectionBodyInsetX,
  },
  sectionTitle: {
    color: catalogColors.title,
    fontSize: 18,
    fontWeight: 700,
  },
  sectionSubtitle: {
    color: catalogColors.muted,
    fontSize: 13,
  },
  badge: {
    borderWidth: 0,
    borderRadius: 999,
    height: 26,
    alignItems: "center",
    padding: 3
  },
  badgeContent: {
    alignItems: "center",
  },
  badgeText: {
    fontSize: 12,
    fontWeight: 700,
    height: 14,
    textAlign: "center",
  },
  metricCard: {
    backgroundColor: catalogColors.paneAlt,
    borderColor: catalogColors.border,
    borderRadius: 12,
  },
  metricContent: {
    padding: 14,
    gap: 4,
    alignItems: "stretch",
  } satisfies ReactStackStyle,
  metricLabel: {
    color: catalogColors.muted,
    fontSize: 12,
    fontWeight: 700,
  },
  metricValue: {
    color: catalogColors.title,
    fontSize: 22,
    fontWeight: 700,
    wrap: true,
  },
  pageHeader: {
    gap: pageHeaderGap,
    alignItems: "stretch",
  },
  pageHeaderCopy: {
    gap: pageHeaderCopyGap,
    alignItems: "stretch",
  } satisfies ReactStackStyle,
  pageHeaderTitle: {
    color: catalogColors.title,
    fontSize: pageHeaderTitleFontSize,
    fontWeight: 700,
  },
  pageHeaderSummary: {
    color: catalogColors.text,
    fontSize: pageHeaderSummaryFontSize,
  },
  pageHeaderBadges: {
    flexDirection: "row",
    gap: 10,
  },
  sectionHeroCopy: {
    height: 54,
    gap: 10,
  },
  sectionHeroTitle: {
    color: "#f8fafc",
    fontSize: 24,
    fontWeight: 700,
  },
  sectionHeroSummary: {
    fontSize: 14,
  },
  swatchStrip: {
    flexDirection: "row",
    justifyContent: "space-between",
    gap: 9,
    padding: 18,
    backgroundColor: catalogColors.pane,
    borderColor: catalogColors.border,
    borderWidth: 1,
    borderRadius: 14,
  },
  swatchTile: {
    ...swatchTileDefaults,
  },
});
