import React from "react";
import { Button, HostStateMask, Image, Label, StyleSheet, TextInput, View, type ReactImageStyle, useHostState, TextStyle, TextAlign } from "../../lib/react-okojo";
import { renderingNotes } from "../catalog-data";
import { catalogColors } from "../catalog-theme";
import { Badge, CatalogPage, NotesSectionCard, PageHeader, SectionBodyColumn, SectionBodyRow, SectionCard, useCatalogPageWidth } from "../catalog-ui";
import type { StackAlign } from "../../lib/react-okojo";
const remoteJpgSource = "https://picsum.photos/480/260";
const localJpgSource = "assets/demo.jpg";
const localSvgSource = "assets/demo.svg";
const loadingPlaceholderSource = "assets/demo-loading.svg";
const transparencyCheckerboardSource = "assets/transparency-checkerboard.png";
const renderingTitle = "Rendering path";
const renderingSummary = "The renderer paints a scene graph into an offscreen bitmap, caches remote files, and now decodes raster or SVG sources on the native side.";
const previewSize = 300;

export function RenderingPage() {
  const width = useCatalogPageWidth();
  const [draftSource, setDraftSource] = React.useState(remoteJpgSource);
  const [imageSource, setImageSource] = React.useState(remoteJpgSource);
  const [imageStatus, setImageStatus] = React.useState("loading");

  const applySource = React.useCallback((nextSource?: string) => {
    const resolvedSource = (nextSource ?? draftSource).trim();
    if (resolvedSource.length === 0) {
      setImageStatus("error: image source is empty");
      return;
    }

    setDraftSource(resolvedSource);
    setImageSource(resolvedSource);
    setImageStatus("loading");
  }, [draftSource]);

  const loadRemoteJpg = React.useCallback(() => applySource(remoteJpgSource), [applySource]);
  const loadLocalJpg = React.useCallback(() => applySource(localJpgSource), [applySource]);
  const loadLocalSvg = React.useCallback(() => applySource(localSvgSource), [applySource]);

  return (
    <CatalogPage
      width={width}
      headerSpacing="tight"
      spacing="relaxed"
      header={<PageHeader width={width} title={renderingTitle} summary={renderingSummary} badges={<FrameBadge />} />}
    >
      <SectionCard title="Image source playground" subtitle={`Status: ${imageStatus}`}>
        <SectionBodyColumn>
          <View style={styles.sourceRow}>
            <TextInput
              value={draftSource}
            placeholder="Paste https://..., file:///C:/.../image.svg, or assets/demo.jpg"
              onChangeText={setDraftSource}
              onSubmit={applySource}
              style={[styles.sourceInput, { flex: 1, height: 40 }]}
            />
            <Button
              title="Load"
              style={styles.actionButton}
              hoverStyle={styles.actionButtonHover}
              titleStyle={styles.actionButtonLabel}
              onPress={() => applySource()}
            />
          </View>
          <View style={styles.presetRow}>
            <Button
              title="Remote JPG"
              style={styles.presetButton}
              hoverStyle={styles.actionButtonHover}
              titleStyle={styles.presetButtonLabel}
              onPress={loadRemoteJpg}
            />
            <Button
              title="Local JPG"
              style={styles.presetButton}
              hoverStyle={styles.actionButtonHover}
              titleStyle={styles.presetButtonLabel}
              onPress={loadLocalJpg}
            />
            <Button
              title="Local SVG"
              style={styles.presetButton}
              hoverStyle={styles.actionButtonHover}
              labelStyle={styles.presetButtonLabel}
              onPress={loadLocalSvg}
            />
          </View>
          <TransparencyPreview
            source={imageSource}
            placeholderSource={loadingPlaceholderSource}
            onLoad={() => setImageStatus("loaded")}
            onError={(_, detail) => {setImageStatus(`error: ${detail}`); console.error(`Failed to load image from source ${imageSource}: ${detail}`);}}
          />
          <Label
            text="Paste a remote URL, a local file:// URI, or a sample asset path like assets/demo.jpg. The same native path now covers jpg, png, and svg."
            style={styles.helperText}
          />
        </SectionBodyColumn>
      </SectionCard>
      <SectionCard title="Resolved local asset paths" subtitle="Sample assets resolve from the configured React asset base.">
        <SectionBodyRow style={styles.localBody}>
          <Image
            source={localSvgSource}
            style={styles.localImage}
          />
          <Label
            text="The same Image component can now point at a local SVG asset with a normal assets/... path. If you prefer an absolute local URI, paste file:///... into the field above."
            style={styles.localText}
          />
        </SectionBodyRow>
      </SectionCard>
      <NotesSectionCard title="Render improvements" subtitle="Current work completed under TODO 1." notes={renderingNotes} />
    </CatalogPage>
  );
}

function FrameBadge() {
  const host = useHostState(HostStateMask.Animation);
  return <Badge text={`Frame ${host.frame}`} />;
}

function TransparencyPreview({
  source,
  placeholderSource,
  onLoad,
  onError,
}: {
  source: string;
  placeholderSource?: string;
  onLoad?: () => void;
  onError?: (source: string, detail: string) => void;
}) {
  return (
    <View style={styles.previewFrame}>
      <Image
        source={transparencyCheckerboardSource}
        style={styles.previewBackdropImage}
      />
      <Image
        source={source}
        placeholderSource={placeholderSource}
        style={styles.previewImage}
        onLoad={onLoad}
        onError={onError}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  sourceRow: {
    gap: 10,
    alignItems: "stretch" as StackAlign,
    flexDirection: "row",
  },
  sourceInput: {
    backgroundColor: catalogColors.input,
    borderColor: catalogColors.border,
    activeBorderColor: catalogColors.activeInput,
    color: catalogColors.title,
  },
  actionButton: {
    width: 88,
    backgroundColor: catalogColors.buttonOn,
  },
  actionButtonHover: {
    backgroundColor: "#1d4ed8",
    borderColor: "#93c5fd",
    borderWidth: 1,
  },
  actionButtonLabel: {
    color: "#f8fafc",
    fontSize: 15,
    fontWeight: 700,
    textAlign: "center" as TextAlign,
  },
  presetRow: {
    gap: 10,
    flexDirection: "row",
  },
  presetButton: {
    flex: 1,
    minWidth: 110,
    backgroundColor: catalogColors.buttonOff,
  },
  presetButtonLabel: {
    color: "#f8fafc",
    fontSize: 13,
    fontWeight: 700,
    textAlign: "center" as TextAlign,
  },
  previewFrame: {
    width: previewSize + 28,
    height: previewSize + 28,
    borderRadius: 12,
    overflow: "hidden" as "hidden" | "visible",
    borderColor: catalogColors.border,
    borderWidth: 1,
  },
  previewBackdropImage: {
    margin: 14,
    height: previewSize,
    width: previewSize,
    fit: "cover",
  } satisfies ReactImageStyle,
  previewImage: {
    margin: 14,
    top:-previewSize - 28,
    height: previewSize,
    width: previewSize,
    fit: "cover",
    borderRadius: 12,
  } satisfies ReactImageStyle,
  helperText: {
    color: catalogColors.note,
    fontSize: 13,
    wrap: true,
  },
  localBody: {
    gap: 16,
  },
  localImage: {
    width: 180,
    height: 108,
    fit: "cover",
    borderRadius: 12,
  } satisfies ReactImageStyle,
  localText: {
    flex: 1,
    color: catalogColors.note,
    fontSize: 14,
    wrap: true,
  },
});
