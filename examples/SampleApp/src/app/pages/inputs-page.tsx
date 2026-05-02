import React from "react";
import { View, StyleSheet, TextInput } from "../../lib/react-okojo";
import { inputHints } from "../catalog-data";
import { catalogColors } from "../catalog-theme";
import { CatalogPage, NotesSectionCard, PageHeader, SectionBodyColumn, SectionCard, useCatalogPageWidth } from "../catalog-ui";

const initialDraft = [
  "Native text input keeps caret math, selection, repeat, and clipboard on the C# side.",
  "This field is multiline and uses explicit newlines rather than JS-side layout heuristics.",
].join("\n");
const inputsTitle = "Input surface";
const inputsSummary = "Host-owned text editing with selection, clipboard shortcuts, multiline caret navigation, and native focus routing.";

export function InputsPage() {
  const width = useCatalogPageWidth();
  const [title, setTitle] = React.useState("Desktop-native note");
  const [draft, setDraft] = React.useState(initialDraft);

  return (
    <CatalogPage
      width={width}
      headerSpacing="compact"
      header={<PageHeader width={width} title={inputsTitle} summary={inputsSummary} />}
    >
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
      <View style={styles.secondarySectionGroup}>
        <SectionCard title="Multiline editor" subtitle="Enter inserts a line break; selection and clipboard stay native.">
          <SectionBodyColumn>
            <TextInput
              value={draft}
              onChangeText={setDraft}
              placeholder="Type a longer note"
              style={styles.editorInput}
            />
          </SectionBodyColumn>
        </SectionCard>
        <NotesSectionCard title="Native shortcuts" subtitle={title} notes={inputHints} fontSize={14} rowGap={10} />
      </View>
    </CatalogPage>
  );
}

const styles = StyleSheet.create({
  singleLineInput: {
    backgroundColor: catalogColors.input,
    borderColor: catalogColors.border,
    activeBorderColor: catalogColors.activeInput,
    color: catalogColors.title,
    height: 42,
  },
  secondarySectionGroup: {
    gap: 54,
  },
  editorInput: {
    backgroundColor: catalogColors.input,
    borderColor: catalogColors.border,
    activeBorderColor: catalogColors.activeInput,
    color: catalogColors.title,
    height: 200,
    multiline: true,
    lineHeight: 24,
    paddingTop: 14,
  },
});
