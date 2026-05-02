import React from "react";
import { Text, View, StyleSheet, measureTextWidth, type ReactStackStyle, type ReactTextStyle, type StyleProp, type ViewStyle, TextAlign } from "../lib/react-okojo";
import { catalogColors } from "./catalog-theme";

type CodeSegment = {
  text: string;
  kind: string;
};

type MeasuredCodeSegment = CodeSegment & {
  width: number;
};

type CachedCodeBlock = {
  lines: readonly (readonly MeasuredCodeSegment[])[];
  width: number;
  height: number;
};

const highlightedLineCache = new Map<string, CachedCodeBlock>();

declare global {
  interface GlobalThis {
    sampleHighlightJsx?: (source: string) => readonly (readonly [string, string][])[] | undefined;
  }
}

const codeTextStyle = {
  fontSize: 13,
  wrap: false,
} satisfies ReactTextStyle;

const codePalette = {
  plain: "#dbe4f0",
  keyword: "#c084fc",
  string: "#ffe7b2",
  number: "#d6ffd4",
  identifier: "#c8e0ff",
  "jsx-tag": "#22ff98",
  operator: "#a4b1fd",
  punctuation: "#feffab",
} as const satisfies Record<string, string>;

function resolveHighlightedLines(source: string) {
  const normalizedSource: string = source.replace(/\r\n/g, "\n");
  const highlight: ((source: string) => readonly (readonly [string, string])[] | undefined) | undefined = globalThis.sampleHighlightJsx;


  if (typeof highlight !== "function") {
    return normalizedSource.split("\n").map((line) => [{ text: line, kind: "plain" } satisfies CodeSegment]);
  }

  return Array.from(highlight(normalizedSource) ?? []).map((line) =>
    Array.from(line).map(([text, kind]) => ({
      text,
      kind,
    } satisfies CodeSegment)),
  );
}

function resolveMeasuredHighlightedLines(source: string, lineHeightMultiplier = 1.15) {
  const cached = highlightedLineCache.get(source);
  if (cached) {
    return cached;
  }

  const lines = resolveHighlightedLines(source).map((line) =>
    line.map((segment) => ({
      ...segment,
      width: Math.max(1, measureTextWidth(segment.text, codeTextStyle)),
    } satisfies MeasuredCodeSegment)),
  );
  const contentWidth = lines.reduce((maxWidth, line) => {
    const lineWidth = line.reduce((sum, segment) => sum + segment.width, 0);
    return Math.max(maxWidth, lineWidth);
  }, 0);
  const lineHeight = Math.ceil(codeTextStyle.fontSize * lineHeightMultiplier);
  const result = {
    lines,
    width: 22 + 10 + contentWidth + 28,
    height: (lines.length * lineHeight) + (Math.max(0, lines.length - 1) * 4) + 28,
  } satisfies CachedCodeBlock;
  highlightedLineCache.set(source, result);
  return result;
}

export const SyntaxCodeBlock = React.memo(function SyntaxCodeBlock({
  code,
  style,
}: {
  code: string;
  style?: StyleProp<ViewStyle>;
}) {
  const block = React.useMemo(() => resolveMeasuredHighlightedLines(code, 1.15), [code]);
  return (
    <View style={[styles.block, { width: block.width, height: block.height }, style]}>
      {block.lines.map((segments, lineIndex) => (
        <View key={lineIndex} style={styles.lineRow}>
          <Text
            style={styles.lineNumber}
          >{`${lineIndex + 1}`}</Text>
          <View style={styles.lineContent}>
            {segments.length > 0 ? segments.map((segment, segmentIndex) => (
              <Text
                key={`${lineIndex}-${segmentIndex}`}
                style={[
                  styles.segment,
                  {
                    width: segment.width,
                    color: codePalette[segment.kind as keyof typeof codePalette] ?? codePalette.plain,
                  },
                ]}
              >{segment.text}</Text>
            )) : (
              <Text style={[styles.segment, styles.emptySegment]}> </Text>
            )}
          </View>
        </View>
      ))}
    </View>
  );
});

const styles = StyleSheet.create({
  block: {
    backgroundColor: "#08101c",
    borderColor: "#1e293b",
    borderWidth: 1,
    borderRadius: 12,
    padding: 14,
    gap: 4,
    alignItems: "stretch",
  },
  lineRow: {
    flexDirection: "row",
    gap: 10,
    height: Math.ceil(codeTextStyle.fontSize * 1.15),
    alignItems: "start",
  } satisfies ReactStackStyle,
  lineNumber: {
    width: 22,
    fontSize: 12,
    color: catalogColors.muted,
    textAlign: "right" as TextAlign,
  },
  lineContent: {
    flexDirection: "row",
    gap: 0,
    alignItems: "start",
  } satisfies ReactStackStyle,
  segment: {
    ...codeTextStyle,
  },
  emptySegment: {
    color: "transparent",
  },
});
