import React from "react";
import { Button, HostPanel, Label, Pane, StyleSheet, TextInput, useAttachRuntimeNode, useNodeRef, View } from "../../lib/react-okojo";
import { ToolTip } from "../../lib/react-okojo-tooltips";
import { catalogColors } from "../catalog-theme";
import { CatalogPage, PageHeader, SectionBodyColumn, SectionCard, sectionBodyWidth, useCatalogPageWidth } from "../catalog-ui";
import type { TextAlign, TextStyle } from "../../lib/react-okojo";

const wordLength = 5;
const previewGap = 10;

function normalizeGuess(value: string) {
  return value.toUpperCase().replace(/[^A-Z]/g, "").slice(0, wordLength);
}

function InfoPill({
  text,
  backgroundColor,
  color,
  style,
}: {
  text: string;
  backgroundColor: string;
  color: string;
  style?: { width?: number; flex?: number };
}) {
  return (
    <Pane
      style={[styles.infoPill, style, { backgroundColor, borderColor: backgroundColor }]}
    >
      <Label
        text={text}
        style={[styles.infoPillLabel, style ? { width: style.width } : undefined, { color }]}
      />
    </Pane>
  );
}

export function CommunicationPage() {
  const width = useCatalogPageWidth();
  const hostPanelRef = useNodeRef();
  const sectionWidth = sectionBodyWidth(width);
  const previewTileSize = 48;
  const [guess, setGuess] = React.useState(() => normalizeGuess(sampleHostPanel.currentGuess));

  const syncGuessFromHost = React.useCallback(() => {
    setGuess(normalizeGuess(sampleHostPanel.currentGuess));
  }, []);

  const handleGuessChange = React.useCallback((value: string) => {
    const nextGuess = normalizeGuess(value);
    setGuess(nextGuess);
    sampleHostPanel.currentGuess = nextGuess;
  }, []);

  const submitGuess = React.useCallback(() => {
    sampleHostPanel.currentGuess = normalizeGuess(guess);
    sampleHostPanel.submitGuess();
    syncGuessFromHost();
  }, [guess, syncGuessFromHost]);

  const resetRound = React.useCallback(() => {
    sampleHostPanel.resetGame();
    syncGuessFromHost();
  }, [syncGuessFromHost]);

  const attachHostPanel = React.useCallback((runtimeId: string) => {
    sampleHostPanel.attachHostNode(runtimeId);
  }, []);

  useAttachRuntimeNode(hostPanelRef, attachHostPanel);

  const previewLetters = Array.from({ length: wordLength }, (_, index) => guess[index] ?? "");
  const statusTone = sampleHostPanel.isSolved
    ? { background: "#123c2f", color: "#86efac" }
    : sampleHostPanel.isRoundComplete
      ? { background: "#3f1d1d", color: "#fda4af" }
      : { background: "#10233f", color: "#93c5fd" };

  return (
    <CatalogPage
      width={width}
      headerSpacing={10}
      header={(
        <PageHeader
          width={width}
          title="Word bridge"
          summary="React handles the current guess and round controls. C# evaluates each word, and low-level Skia renders the board inside HostPanel."
        />
      )}
    >
      <SectionCard
        title="Wordle-style JS <-> C# communication"
        subtitle="Type in React, submit to C#, and let the host-drawn board show the scored round."
      >
        <SectionBodyColumn style={styles.wordBridgeBody}>
          <View style={styles.controlsRow}>
            <TextInput
              value={guess}
              placeholder="Enter a five-letter guess"
              onChangeText={handleGuessChange}
              onSubmit={submitGuess}
              style={[styles.guessInput, { flex: 1, height: 40 }]}
            />

            <Button
              title="Guess"
              style={styles.guessButton}
              hoverStyle={styles.buttonHover}
              titleStyle={styles.buttonLabel}
              onPress={submitGuess}
            />
            <ToolTip content="Start the next answer after this round.">
              <Button
                title="New round"
                style={styles.resetButton}
                hoverStyle={styles.buttonHover}
                titleStyle={styles.buttonLabel}
                onPress={resetRound}
              />
            </ToolTip>
          </View>

          <View style={styles.metaRow}>
            <InfoPill
              text={sampleHostPanel.boardSummary}
              backgroundColor="#10233f"
              color="#93c5fd"
              style={{ width: 182 }}
            />
            <InfoPill
              text={sampleHostPanel.statusText}
              backgroundColor={statusTone.background}
              color={statusTone.color}
              style={{ flex: 1 }}
            />
          </View>

          <View style={styles.previewSection}>
            <Label text="JS-side guess preview" style={[styles.previewTitle, { width: sectionWidth }]} />
            <View style={styles.previewRow}>
              {previewLetters.map((letter, index) => (
                <Pane
                  key={index}
                  style={[
                    styles.previewTile,
                    {
                      width: previewTileSize,
                      height: previewTileSize,
                      backgroundColor: letter ? "#1e293b" : "#0f172a",
                      borderColor: letter ? "#60a5fa" : "#334155",
                      borderWidth: letter ? 2 : 1,
                    },
                  ]}
                >
                  <Label
                    text={letter}
                    style={[styles.previewLabel, { width: previewTileSize }]}
                  />
                </Pane>
              ))}
            </View>
          </View>

          <View style={styles.hostPanelFrame}>
            <HostPanel
              nodeRef={hostPanelRef}
              style={[styles.hostPanel, { left: 0, top: 0, width: sectionWidth, height: 392 }]}
            />
          </View>
        </SectionBodyColumn>
      </SectionCard>
    </CatalogPage>
  );
}

const styles = StyleSheet.create({
  guessInput: {
    backgroundColor: catalogColors.input,
    borderColor: catalogColors.border,
    activeBorderColor: catalogColors.activeInput,
    color: catalogColors.title,
  },
  guessButton: {
    backgroundColor: catalogColors.buttonOn,
  },
  resetButton: {
    backgroundColor: catalogColors.buttonOff,
  },
  buttonHover: {
    backgroundColor: "#475569",
    borderColor: "#93c5fd",
    borderWidth: 1,
  },
  buttonLabel: {
    color: "#f8fafc",
    fontSize: 18,
    fontWeight: 700,
    textAlign: "center" as TextAlign,
  },
  wordBridgeBody: {
    gap: 14,
  },
  controlsRow: {
    flexDirection: "row",
    height: 40,
    gap: 8,
    alignItems: "stretch",
  },
  metaRow: {
    flexDirection: "row",
    gap: 8,
    alignItems: "stretch",
  },
  infoPill: {
    height: 28,
    borderWidth: 0,
    borderRadius: 999,
  },
  infoPillLabel: {
    top: 7,
    fontSize: 12,
    fontWeight: 700,
    textAlign: "center" as TextAlign,
  },
  previewSection: {
    gap: 8,
  },
  previewTitle: {
    color: catalogColors.note,
    fontSize: 12,
    fontWeight: 700,
  },
  previewRow: {
    flexDirection: "row",
    gap: previewGap,
  },
  previewTile: {
    borderRadius: 10,
  },
  previewLabel: {
    color: catalogColors.title,
    fontSize: 20,
    fontWeight: 700,
    textAlign: "center",
    top: 12,
  },
  hostPanelFrame: {
    height: 392,
  },
  hostPanel: {
    backgroundColor: "#020617",
    borderColor: "#1e293b",
    borderWidth: 1,
    borderRadius: 18,
  },
});
