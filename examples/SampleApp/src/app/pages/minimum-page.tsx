import React from "react";
import { Text, Button, Label, StyleSheet, View, StackAxis, type StackAlign, FlexDirection} from "../../lib/react-okojo";
import { Badge, CatalogPage, NotesSectionCard, PageHeader, SectionBodyColumn, SectionCard, useCatalogPageWidth } from "../catalog-ui";
import { SyntaxCodeBlock } from "../syntax-highlight";

const minimumTitle = "Minimum React samples";
const minimumSummary = "Short examples that map directly to the rendered UI: local state, button presses, ordinary views, StyleSheet.create, margin, padding, box shadow, and native syntax highlighting.";

const counterSnippet = `function CounterPreview() {
  const [count, setCount] = React.useState(0);
  return (
    <View style={counterStyles.row}>
      <Text style={{ color: "#000000", fontWeight: 800, fontSize: 30 }}>{\`count: \${count}\`}</Text>
      <Button label="+1" onPress={() => setCount((value) => value + 1)} style={counterStyles.button} />
    </View>
  );
}
const counterStyles = StyleSheet.create({
  row: { 
    flexDirection: "row", margin: 16, gap: 25,
     maxWidth: 250, padding: 12, borderRadius: 14, backgroundColor: "#a5ece2" 
  },
  button: {
    borderColor: "#92a5c5", borderWidth: 1,
    shadow: { color: "#020617", offsetX: 15, offsetY: 7, blur: 12, },
  }
});`;

const boxSnippet = `const boxPreviewStyles = StyleSheet.create({
  row: {
    flexDirection: "row",
    gap: 12 },
  card: {
    width: 132,
    padding: 14,
    marginRight: 12,
    backgroundColor: "#132033",
    borderColor: "#22314a",
    borderWidth: 1,
    borderRadius: 14,
    shadow: { color: "#020617", offsetY: 10, blur: 20 },
    flexDirection: "column",
    gap: 8,
  },
  cardAlt: {
    backgroundColor: "#14263c",
    borderColor: "#29425f",
    paddingTop: 18,
  },
  title: {
    color: "#f8fafc",
    fontSize: 13,
    fontWeight: 700,
  },
  body: {
    color: "#cbd5e1",
    fontSize: 12,
    wrap: true,
  },
});

function BoxPreview() {
  return (
    <View style={boxPreviewStyles.row}>
      <View style={boxPreviewStyles.card}>
        <Label text="Primary box" style={boxPreviewStyles.title} />
        <Label text={"padding: 14\\nmarginRight: 12\\nshadow enabled"} style={boxPreviewStyles.body} />
      </View>
      <View style={[boxPreviewStyles.card, boxPreviewStyles.cardAlt]}>
        <Label text="Second box" style={boxPreviewStyles.title} />
        <Label text={"Same View API,\\ndifferent style object."} style={boxPreviewStyles.body} />
      </View>
    </View>
  );
}`;

const minimumNotes = [
  "Each preview is intentionally tiny so the JSX and the rendered result stay easy to compare.",
  "Code blocks are syntax-highlighted through the sample host, not the reusable react-okojo runtime surface.",
  "The same View/Button/Label primitives stay enough for small stateful React UI without browser-only APIs.",
];

const demoStyles = StyleSheet.create({
  codeColumn: {
    padding: 15,
    gap: 14,
  },
});

const boxPreviewStyles = StyleSheet.create({
  row: {
    flexDirection: "row" as StackAxis,
    gap: 12,
  },
  card: {
    width: 132,
    padding: 14,
    marginRight: 12,
    backgroundColor: "#132033",
    borderColor: "#22314a",
    borderWidth: 1,
    borderRadius: 14,
    shadow: {
      color: "#020617",
      offsetY: 10,
      blur: 20,
    },
    flexDirection: "column" as FlexDirection,
    gap: 8,
  },
  cardAlt: {
    backgroundColor: "#14263c",
    borderColor: "#29425f",
    paddingTop: 18,
  },
  title: {
    color: "#f8fafc",
    fontSize: 13,
    fontWeight: 700,
  },
  body: {
    color: "#cbd5e1",
    fontSize: 12,
    wrap: true,
  },
});

export function MinimumPage() {
  const width = useCatalogPageWidth();
  return (
    <CatalogPage
      width={width}
      headerSpacing="tight"
      spacing="relaxed"
      header={(
        <PageHeader
          width={width}
          title={minimumTitle}
          summary={minimumSummary}
          badges={(
            <>
              <Badge text="useState" />
              <Badge text="StyleSheet.create" tone="success" />
              <Badge text="Acornima.Jsx" tone="warning" />
            </>
          )}
        />
      )}
    >
      <SectionCard title="Counter with local state" subtitle="A plain React state update driving native text.">
        <SectionBodyColumn style={demoStyles.codeColumn}>
          <CounterPreview />
          <SyntaxCodeBlock code={counterSnippet} />;
        </SectionBodyColumn>
      </SectionCard>

      <SectionCard title="View, margin, padding, and box shadow" subtitle="The same primitives stay readable when styles live in StyleSheet.create.">
        <SectionBodyColumn style={demoStyles.codeColumn}>
          <BoxPreview />
          <SyntaxCodeBlock code={boxSnippet} />;
        </SectionBodyColumn>
      </SectionCard>

      <NotesSectionCard title="Why this page exists" subtitle="Small enough to copy, short enough to compare with the live UI." notes={minimumNotes} />
    </CatalogPage>
  );
}

function CounterPreview() {
  const [count, setCount] = React.useState(0);
  
  return (
    <View style={counterStyles.row}>
      <Text style={{ color: "#000000", fontWeight: 800, fontSize: 30 }}>{`count: ${count}`}</Text>
      <Button title="+1" onPress={() => {
        setTimeout(() =>console.log("Incrementing count " + count), 1000);
        return setCount((value) => value + 1);
      }} style={counterStyles.button} />
    </View>
  );
}
const counterStyles = StyleSheet.create({
  row: { flexDirection: "row" as FlexDirection, margin: 16, gap: 25, maxWidth: 250, padding: 12, borderRadius: 14, backgroundColor: "#a5ece2" },
  button: {
    borderColor: "#92a5c5", borderWidth: 1,
    shadow: { color: "#020617", offsetX: 15, offsetY: 7, blur: 12, },
  }
});
function BoxPreview() {
  return (
    <View style={boxPreviewStyles.row}>
      <View style={boxPreviewStyles.card}>
        <Label text="Primary box" style={boxPreviewStyles.title} />
        <Label text={"padding: 14\nmarginRight: 12\nshadow enabled"} style={boxPreviewStyles.body} />
      </View>
      <View style={[boxPreviewStyles.card, boxPreviewStyles.cardAlt]}>
        <Label text="Second box" style={boxPreviewStyles.title} />
        <Label text={"Same View API,\ndifferent style object."} style={boxPreviewStyles.body} />
      </View>
    </View>
  );
}
