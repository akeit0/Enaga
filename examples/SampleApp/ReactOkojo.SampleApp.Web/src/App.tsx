import { useEffect, useMemo, useState, type CSSProperties, type ReactNode } from "react";
import { StyleSheet } from "react-native";
import {
  Image,
  Pressable,
  ScrollView,
  Text,
  TextInput,
  View,
} from "./lib/web-primitives";
import {
  animationNotes,
  catalogTabs,
  componentExamples,
  effectNotes,
  gradientNotes,
  inputHints,
  overviewBullets,
  renderingNotes,
  shaderNotes,
  type CatalogTabId,
} from "./catalog-data";

type ReactNativeStyle = CSSProperties;
type ReactNativeStyleInput =
  | ReactNativeStyle
  | readonly (ReactNativeStyle | false | null | undefined)[];

type HostState = {
  width: number;
  height: number;
  frame: number;
  elapsedMs: number;
  mouseX: number;
  mouseY: number;
  lastKey: string;
  lastTextInput: string;
};

type PageProps = {
  host: HostState;
  onTextInput: (value: string) => void;
};

const localAssetText =
  "This image stays fully local in the web build and mirrors the same rendering panel from the native sample.";

const outerScrollNoteA = "Outer notes keep their own scroll offset.";
const outerScrollNoteB = "The inner editor can scroll and keep focus without the outer region stealing wheel input.";
const innerScrollNote = "Try scrolling here first, then press Tab between the nested inputs.";
const scrollFooterNote =
  "This demo still sits inside the page-level content scroll, so it shows nested browser scrolling and focus behavior clearly.";

function useActiveTab(): [CatalogTabId, (tab: CatalogTabId) => void] {
  const getInitialTab = (): CatalogTabId => {
    const hash = window.location.hash.replace(/^#/, "");
    return catalogTabs.some((tab) => tab.id === hash) ? (hash as CatalogTabId) : "overview";
  };

  const [activeTab, setActiveTab] = useState<CatalogTabId>(getInitialTab);

  useEffect(() => {
    const handleHashChange = () => setActiveTab(getInitialTab());
    window.addEventListener("hashchange", handleHashChange);
    return () => window.removeEventListener("hashchange", handleHashChange);
  }, []);

  const selectTab = (tab: CatalogTabId) => {
    window.history.replaceState(null, "", `#${tab}`);
    setActiveTab(tab);
  };

  return [activeTab, selectTab];
}

function useHostState(lastTextInput: string): HostState {
  const [viewport, setViewport] = useState(() => ({
    width: window.innerWidth,
    height: window.innerHeight,
  }));
  const [pointer, setPointer] = useState(() => ({
    x: Math.round(window.innerWidth * 0.5),
    y: Math.round(window.innerHeight * 0.5),
  }));
  const [lastKey, setLastKey] = useState("");
  const [frame, setFrame] = useState(0);
  const [elapsedMs, setElapsedMs] = useState(0);

  useEffect(() => {
    const handleResize = () => {
      setViewport({
        width: window.innerWidth,
        height: window.innerHeight,
      });
    };

    const handlePointerMove = (event: PointerEvent) => {
      setPointer({
        x: Math.round(event.clientX),
        y: Math.round(event.clientY),
      });
    };

    const handleKeyDown = (event: KeyboardEvent) => {
      setLastKey(event.key);
    };

    const startedAt = performance.now();
    let animationFrameId = 0;

    const tick = (now: number) => {
      setElapsedMs(now - startedAt);
      setFrame((currentFrame) => currentFrame + 1);
      animationFrameId = window.requestAnimationFrame(tick);
    };

    window.addEventListener("resize", handleResize);
    window.addEventListener("pointermove", handlePointerMove);
    window.addEventListener("keydown", handleKeyDown);
    animationFrameId = window.requestAnimationFrame(tick);

    return () => {
      window.removeEventListener("resize", handleResize);
      window.removeEventListener("pointermove", handlePointerMove);
      window.removeEventListener("keydown", handleKeyDown);
      window.cancelAnimationFrame(animationFrameId);
    };
  }, []);

  return {
    width: viewport.width,
    height: viewport.height,
    mouseX: pointer.x,
    mouseY: pointer.y,
    lastKey,
    frame,
    elapsedMs,
    lastTextInput,
  };
}

function Badge({
  children,
  tone = "accent",
}: {
  children: ReactNode;
  tone?: "accent" | "success" | "warning";
}) {
  const toneStyle = tone === "success" ? styles.badgeSuccess : tone === "warning" ? styles.badgeWarning : styles.badgeAccent;
  const toneTextStyle =
    tone === "success" ? styles.badgeTextSuccess : tone === "warning" ? styles.badgeTextWarning : styles.badgeTextAccent;

  return (
    <View style={[styles.badge, toneStyle]}>
      <Text style={[styles.badgeText, toneTextStyle]}>{children}</Text>
    </View>
  );
}

function MetricTile({
  label,
  value,
}: {
  label: string;
  value: string;
}) {
  return (
    <View style={styles.metricTile}>
      <Text style={styles.metricTileLabel}>{label}</Text>
      <Text style={styles.metricTileValue}>{value}</Text>
    </View>
  );
}

function SectionCard({
  title,
  subtitle,
  style,
  children,
}: {
  title: string;
  subtitle?: string;
  style?: ReactNativeStyleInput;
  children?: ReactNode;
}) {
  return (
    <View style={[styles.sectionCard, style]}>
      <View style={styles.sectionCardHeader}>
        <Text style={styles.sectionCardTitle}>{title}</Text>
        {subtitle ? <Text style={styles.sectionCardSubtitle}>{subtitle}</Text> : null}
      </View>
      {children}
    </View>
  );
}

function NoteList({ notes }: { notes: readonly string[] }) {
  return (
    <View style={styles.noteList}>
      {notes.map((note) => (
        <View key={note} style={styles.noteListItem}>
          <Text style={styles.noteListBullet}>-</Text>
          <Text style={styles.noteListText}>{note}</Text>
        </View>
      ))}
    </View>
  );
}

function PageHeader({
  title,
  summary,
  badges,
}: {
  title: string;
  summary: string;
  badges?: ReactNode;
}) {
  return (
    <View style={styles.pageHeader}>
      <View style={styles.pageHeaderCopy}>
        <Text style={styles.pageHeaderTitle}>{title}</Text>
        <Text style={styles.pageHeaderSummary}>{summary}</Text>
      </View>
      {badges ? <View style={styles.pageHeaderBadges}>{badges}</View> : null}
    </View>
  );
}

function OverviewPage({ host }: PageProps) {
  const isNarrow = host.width <= 820;

  return (
    <View style={styles.pageStack}>
      <PageHeader
        title="Native renderer catalog"
        summary="Enaga.Browser reference version of the native sample, rebuilt with React Native Web primitives and regular browser hosting."
        badges={
          <>
            <Badge>React Native Web</Badge>
            <Badge tone="success">Enaga.Browser input</Badge>
            <Badge tone="warning">Vite+ sample</Badge>
          </>
        }
      />

      <View style={[styles.metricGrid, styles.metricGridThree, isNarrow && styles.singleColumnGrid]}>
        <MetricTile label="Viewport" value={`${host.width} x ${host.height}`} />
        <MetricTile label="Input path" value="browser live" />
        <MetricTile label="Last key" value={host.lastKey || "none"} />
      </View>

      <SectionCard
        title="What this catalog covers"
        subtitle="The UI mirrors the native sample output while keeping the code close to React Native-style primitives."
      >
        <NoteList notes={overviewBullets} />
      </SectionCard>
    </View>
  );
}

function InputsPage({ onTextInput }: PageProps) {
  const [title, setTitle] = useState("Desktop-native note");
  const [draft, setDraft] = useState(
    [
      "This web sample keeps the same catalog layout, but uses regular browser inputs wrapped by React Native Web primitives.",
      "The purpose is to compare surface output and spot native-only APIs or missing features.",
    ].join("\n"),
  );

  return (
    <View style={styles.pageStack}>
      <PageHeader
        title="Input surface"
        summary="Standard browser text behavior wrapped through React Native Web so the UI stays close to the native sample structure."
      />

      <SectionCard title="Single-line field" subtitle="Good for search, labels, or command bars.">
        <TextInput
          style={styles.textField}
          value={title}
          placeholder="Catalog title"
          placeholderTextColor="#64748b"
          onChangeText={(value) => {
            setTitle(value);
            onTextInput(value);
          }}
        />
      </SectionCard>

      <SectionCard title="Multiline editor" subtitle="Enaga.Browser textarea version of the native editing sample.">
        <TextInput
          style={[styles.textField, styles.textArea]}
          value={draft}
          multiline
          placeholder="Type a longer note"
          placeholderTextColor="#64748b"
          onChangeText={(value) => {
            setDraft(value);
            onTextInput(value.slice(-1) || value);
          }}
        />
      </SectionCard>

      <SectionCard title="Native shortcuts in the original sample" subtitle={title}>
        <NoteList notes={inputHints} />
      </SectionCard>
    </View>
  );
}

function RenderingPage({ host }: PageProps) {
  const [remoteStatus, setRemoteStatus] = useState("loading");
  const isNarrow = host.width <= 820;

  return (
    <View style={styles.pageStack}>
      <PageHeader
        title="Rendering path"
        summary="The native sample paints a scene graph into an offscreen bitmap. Here the same catalog panel is rendered with browser-backed Image views."
        badges={<Badge>{`Frame ${host.frame}`}</Badge>}
      />

      <SectionCard title="Remote image" subtitle={`Status: ${remoteStatus}`}>
        <Image
          style={styles.heroImage}
          source={{ uri: "https://picsum.photos/960/520" }}
          accessibilityLabel="Remote sample"
          onLoad={() => setRemoteStatus("loaded")}
          onError={() => setRemoteStatus("error")}
        />
      </SectionCard>

      <SectionCard title="Local asset" subtitle="Local files load through Vite's static asset pipeline.">
        <View style={[styles.mediaRow, isNarrow && styles.singleColumnGrid]}>
          <Image
            style={styles.localImage}
            source={{ uri: "/demo.jpg" }}
            accessibilityLabel="Local demo asset"
          />
          <Text style={styles.supportingCopy}>{localAssetText}</Text>
        </View>
      </SectionCard>

      <SectionCard title="Render improvements" subtitle="These notes come straight from the original catalog.">
        <NoteList notes={renderingNotes} />
      </SectionCard>
    </View>
  );
}

function GradientsPage({ host }: PageProps) {
  const isNarrow = host.width <= 820;
  const extraGradientNotes = useMemo(
    () => [
      ...gradientNotes,
      "Radial gradients are useful for badges, hero cards, and soft vignette treatments without dropping to shader code.",
    ],
    [],
  );

  return (
    <View style={styles.pageStack}>
      <PageHeader
        title="Gradients"
        summary="The browser version uses layered web gradients while the component tree still reads like React Native-style layout code."
        badges={<Badge>no animation needed</Badge>}
      />

      <SectionCard
        title="Gradient hero panel"
        subtitle="Direction, stop placement, and palette stay on the app side."
        style={[styles.surfaceCard, styles.gradientSurface]}
      >
        <View style={styles.surfaceCopy}>
          <Text style={styles.surfaceTitle}>Linear gradients</Text>
          <Text style={styles.surfaceText}>
            These backgrounds are just regular web surfaces in the browser reference app.
          </Text>
        </View>
      </SectionCard>

      <SectionCard title="Gradient swatches" subtitle="Linear and radial examples.">
        <View style={[styles.threeColumnGrid, isNarrow && styles.singleColumnGrid]}>
          <View style={[styles.swatch, styles.gradientSwatchA]} />
          <View style={[styles.swatch, styles.gradientSwatchB]} />
          <View style={[styles.swatch, styles.gradientSwatchC]} />
        </View>
      </SectionCard>

      <SectionCard title="Gradient notes" subtitle="What makes gradients a good default decoration primitive.">
        <NoteList notes={extraGradientNotes} />
      </SectionCard>
    </View>
  );
}

function ShadersPage({ host }: PageProps) {
  const isNarrow = host.width <= 820;
  const phase = host.elapsedMs * 0.001;
  const heroStyle: ReactNativeStyle = {
    backgroundImage: [
      `radial-gradient(circle at ${30 + Math.sin(phase * 0.9) * 10}% ${32 + Math.cos(phase * 1.1) * 8}%, rgba(56, 189, 248, 0.9), transparent 34%)`,
      `radial-gradient(circle at ${72 - Math.sin(phase * 1.2) * 8}% ${42 - Math.cos(phase * 0.8) * 6}%, rgba(168, 85, 247, 0.85), transparent 28%)`,
      "linear-gradient(135deg, #082f49 0%, #0f172a 45%, #111827 100%)",
    ].join(", "),
  };
  const shaderSwatchA: ReactNativeStyle = { filter: `hue-rotate(${phase * 18}deg)` };
  const shaderSwatchB: ReactNativeStyle = { transform: `translateY(${Math.sin(phase * 1.4) * 2}px)` };
  const shaderSwatchC: ReactNativeStyle = { filter: `saturate(${1.1 + Math.sin(phase * 1.8) * 0.25})` };

  return (
    <View style={styles.pageStack}>
      <PageHeader
        title="Shaders"
        summary="This page uses animated web backgrounds to mirror the look of the native shader catalog without copying the runtime implementation."
        badges={<Badge>runtime effects look</Badge>}
      />

      <SectionCard
        title="Plasma-style panel"
        subtitle="Visual approximation of the shader sample using standard web backgrounds."
        style={[styles.surfaceCard, styles.shaderSurface]}
      >
        <View style={[styles.surfaceCopy, styles.shaderSurfaceCopy, heroStyle]}>
          <Text style={styles.surfaceTitle}>Shader-style preview</Text>
          <Text style={styles.surfaceText}>
            The browser sample aims to match the visual output while keeping the code ordinary React code over RN-like primitives.
          </Text>
        </View>
      </SectionCard>

      <SectionCard title="Animated swatches" subtitle="Three different shader-like surface treatments.">
        <View style={[styles.threeColumnGrid, isNarrow && styles.singleColumnGrid]}>
          <View style={[styles.swatch, styles.shaderSwatchBase, styles.shaderSwatchA, shaderSwatchA]} />
          <View style={[styles.swatch, styles.shaderSwatchBase, styles.shaderSwatchB, shaderSwatchB]} />
          <View style={[styles.swatch, styles.shaderSwatchBase, styles.shaderSwatchC, shaderSwatchC]} />
        </View>
      </SectionCard>

      <SectionCard title="Shader notes" subtitle="How the native shader page maps to a browser reference app.">
        <NoteList notes={shaderNotes} />
      </SectionCard>
    </View>
  );
}

function AnimationPage({ host }: PageProps) {
  const time = host.elapsedMs * 0.001;
  const motionPhase = (Math.sin(time * 2.2) + 1) * 0.5;
  const orbitX = 50 + Math.cos(time * 1.4) * 36;
  const orbitY = 50 + Math.sin(time * 1.9) * 22;
  const progressFillStyle: ReactNativeStyle = { width: `${12 + motionPhase * 88}%` };
  const orbitDotStyle: ReactNativeStyle = { left: `${orbitX}%`, top: `${orbitY}%` };

  return (
    <View style={styles.pageStack}>
      <PageHeader
        title="Animation"
        summary="Direct browser version of the opt-in motion sample using requestAnimationFrame-driven state."
        badges={<Badge>opt-in loop</Badge>}
      />

      <SectionCard title="Animation sample" subtitle="Progress and orbit positions update from a shared frame clock.">
        <View style={styles.progressTrack}>
          <View style={[styles.progressFill, progressFillStyle]} />
        </View>
        <View style={styles.orbitStage}>
          <View style={[styles.orbitDot, orbitDotStyle]} />
        </View>
        <Text style={styles.supportingCopy}>
          elapsed {(host.elapsedMs / 1000).toFixed(2)}s · progress {Math.round(motionPhase * 100)}%
        </Text>
      </SectionCard>

      <SectionCard title="Animation notes" subtitle="Why the native runtime loop is claimed from JS.">
        <NoteList notes={animationNotes} />
      </SectionCard>
    </View>
  );
}

function ComponentsPage({ host, onTextInput }: PageProps) {
  const isNarrow = host.width <= 820;
  const [nestedTitle, setNestedTitle] = useState("Nested title");
  const [nestedDraft, setNestedDraft] = useState(
    "Arrow Up / Down now keeps column.\nShift + Tab moves focus backward.",
  );

  return (
    <View style={styles.pageStack}>
      <PageHeader
        title="Component patterns"
        summary="Practical app-level compositions, decoration primitives, and nested browser scrolling."
      />

      <View style={[styles.metricGrid, styles.metricGridTwo, isNarrow && styles.singleColumnGrid]}>
        <MetricTile label="Pointer" value={`${host.mouseX}, ${host.mouseY}`} />
        <MetricTile label="Focused typing" value={host.lastTextInput || "none"} />
      </View>

      <SectionCard title="Reusable patterns" subtitle="These stay at the application layer rather than becoming renderer primitives.">
        <View style={styles.badgeRow}>
          <Badge>status pill</Badge>
          <Badge tone="success">success state</Badge>
          <Badge tone="warning">warning state</Badge>
        </View>
        <NoteList notes={componentExamples} />
      </SectionCard>

      <SectionCard title="Decoration primitives" subtitle={effectNotes[0]}>
        <View style={[styles.threeColumnGrid, isNarrow && styles.singleColumnGrid]}>
          <View style={[styles.decorCard, styles.decorCardLinear]}>
            <Text style={styles.decorCardTitle}>linear + shadow</Text>
            <Text style={styles.decorCardText}>Good for elevated cards, panes, and callouts.</Text>
          </View>
          <View style={[styles.decorCard, styles.decorCardRadial]}>
            <Text style={styles.decorCardTitle}>radial surface</Text>
            <Text style={styles.decorCardText}>Useful for spotlights, badges, and hero treatment.</Text>
          </View>
        </View>
      </SectionCard>

      <SectionCard title="Nested scroll + focus" subtitle="Enaga.Browser version of the nested host scrolling demo.">
        <ScrollView style={styles.outerScroll} contentContainerStyle={styles.nestedScroll}>
          <Text style={styles.supportingCopy}>{outerScrollNoteA}</Text>
          <Text style={styles.supportingCopy}>{outerScrollNoteB}</Text>
          <ScrollView style={styles.innerScroll} contentContainerStyle={styles.nestedScroll}>
            <Text style={styles.supportingCopy}>{innerScrollNote}</Text>
            <TextInput
              style={styles.textField}
              value={nestedTitle}
              placeholderTextColor="#64748b"
              onChangeText={(value) => {
                setNestedTitle(value);
                onTextInput(value);
              }}
            />
            <TextInput
              style={[styles.textField, styles.textArea, styles.textAreaCompact]}
              value={nestedDraft}
              multiline
              placeholderTextColor="#64748b"
              onChangeText={(value) => {
                setNestedDraft(value);
                onTextInput(value.slice(-1) || value);
              }}
            />
          </ScrollView>
          <Text style={styles.supportingCopy}>{scrollFooterNote}</Text>
        </ScrollView>
      </SectionCard>
    </View>
  );
}

function App() {
  const [activeTab, setActiveTab] = useActiveTab();
  const [lastTextInput, setLastTextInput] = useState("");
  const host = useHostState(lastTextInput);
  const isStacked = host.width <= 1080;
  const isNarrow = host.width <= 820;

  const activePage = useMemo(() => {
    switch (activeTab) {
      case "overview":
        return <OverviewPage host={host} onTextInput={setLastTextInput} />;
      case "inputs":
        return <InputsPage host={host} onTextInput={setLastTextInput} />;
      case "rendering":
        return <RenderingPage host={host} onTextInput={setLastTextInput} />;
      case "gradients":
        return <GradientsPage host={host} onTextInput={setLastTextInput} />;
      case "shaders":
        return <ShadersPage host={host} onTextInput={setLastTextInput} />;
      case "animation":
        return <AnimationPage host={host} onTextInput={setLastTextInput} />;
      case "components":
        return <ComponentsPage host={host} onTextInput={setLastTextInput} />;
    }
  }, [activeTab, host]);

  return (
    <View style={[styles.catalogApp, isNarrow && styles.catalogAppNarrow]}>
      <View style={[styles.comparisonBanner, isNarrow && styles.comparisonBannerNarrow]}>
        <Text style={styles.comparisonBannerTitle}>Web reference app</Text>
        <Text style={styles.comparisonBannerText}>
          React Native Web-flavored version of the native sample UI, built to expose API differences and missing features.
        </Text>
      </View>

      <View style={[styles.catalogFrame, isStacked && styles.catalogFrameStacked]}>
        <View
          style={[
            styles.catalogSidebar,
            isStacked && styles.catalogSidebarStacked,
            isNarrow && styles.sidePaddingNarrow,
          ]}
        >
          <View style={styles.catalogSidebarHeader}>
            <Text style={styles.catalogSidebarTitle}>Catalog</Text>
            <Text style={styles.catalogSidebarText}>{catalogTabs.length} tabs · browser metrics update live</Text>
          </View>
          <View style={styles.catalogTabList}>
            {catalogTabs.map((tab) => (
              <Pressable
                key={tab.id}
                onPress={() => setActiveTab(tab.id)}
                style={({ hovered, pressed }) => [
                  styles.catalogTab,
                  tab.id === activeTab && styles.catalogTabActive,
                  hovered && tab.id !== activeTab && styles.catalogTabHover,
                  pressed && styles.catalogTabPressed,
                ]}
              >
                <Text style={styles.catalogTabLabel}>{tab.label}</Text>
                <Text style={styles.catalogTabSubtitle}>{tab.subtitle}</Text>
              </Pressable>
            ))}
          </View>
        </View>

        <View style={styles.catalogContent}>
          <View
            style={[
              styles.catalogContentIntro,
              isNarrow && styles.sidePaddingNarrow,
            ]}
          >
            <Text style={styles.catalogContentIntroTitle}>Renderer catalog sample</Text>
            <Text style={styles.catalogContentIntroText}>
              Same product-style output as the native sample, rebuilt as a browser app with React Native Web-style primitives.
            </Text>
          </View>
          <ScrollView
            style={[styles.catalogContentBody, isStacked && styles.catalogContentBodyStacked]}
            contentContainerStyle={[styles.catalogContentBodyContent, isNarrow && styles.sidePaddingNarrow]}
          >
            {activePage}
          </ScrollView>
        </View>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  catalogApp: {
    width: "100%",
    maxWidth: 1440,
    alignSelf: "center",
    padding: 24,
  },
  catalogAppNarrow: {
    padding: 16,
  },
  comparisonBanner: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: 12,
    marginBottom: 18,
    paddingVertical: 12,
    paddingHorizontal: 16,
    backgroundColor: "rgba(17, 24, 39, 0.85)",
    borderWidth: 1,
    borderColor: "#1e293b",
    borderRadius: 16,
  },
  comparisonBannerNarrow: {
    alignItems: "flex-start",
    flexDirection: "column",
  },
  comparisonBannerTitle: {
    color: "#f8fafc",
    fontSize: 18,
    fontWeight: "700",
  },
  comparisonBannerText: {
    color: "#cbd5e1",
    fontSize: 14,
    lineHeight: 22,
    flexShrink: 1,
  },
  catalogFrame: {
    display: "grid",
    gridTemplateColumns: "290px minmax(0, 1fr)",
    minHeight: "calc(100vh - 114px)",
    backgroundColor: "#111827",
    borderWidth: 1,
    borderColor: "#334155",
    borderRadius: 26,
    overflow: "hidden",
    boxShadow: "0 24px 60px rgba(2, 6, 23, 0.45)",
  },
  catalogFrameStacked: {
    gridTemplateColumns: "1fr",
    minHeight: "auto",
  },
  catalogSidebar: {
    display: "flex",
    flexDirection: "column",
    gap: 20,
    paddingVertical: 28,
    paddingHorizontal: 22,
    backgroundColor: "#0f172a",
    borderRightWidth: 1,
    borderRightColor: "#334155",
  },
  catalogSidebarStacked: {
    borderRightWidth: 0,
    borderBottomWidth: 1,
    borderBottomColor: "#334155",
  },
  sidePaddingNarrow: {
    paddingLeft: 18,
    paddingRight: 18,
  },
  catalogSidebarHeader: {
    display: "grid",
    gap: 4,
  },
  catalogSidebarTitle: {
    color: "#f8fafc",
    fontSize: 28,
    fontWeight: "700",
    lineHeight: 32,
  },
  catalogSidebarText: {
    color: "#94a3b8",
    fontSize: 14,
    lineHeight: 20,
  },
  catalogTabList: {
    display: "grid",
    gap: 10,
  },
  catalogTab: {
    width: "100%",
    display: "flex",
    flexDirection: "column",
    alignItems: "flex-start",
    gap: 2,
    paddingVertical: 14,
    paddingHorizontal: 16,
    backgroundColor: "#334155",
    borderWidth: 1,
    borderColor: "transparent",
    borderRadius: 14,
    textAlign: "left",
  },
  catalogTabHover: {
    backgroundColor: "#3b485c",
    borderColor: "#475569",
    transform: "translateY(-1px)",
  },
  catalogTabPressed: {
    opacity: 0.96,
  },
  catalogTabActive: {
    backgroundColor: "#2563eb",
    borderColor: "#3b82f6",
  },
  catalogTabLabel: {
    color: "#f8fafc",
    fontSize: 16,
    fontWeight: "700",
  },
  catalogTabSubtitle: {
    color: "#dbeafe",
    fontSize: 12,
    lineHeight: 18,
  },
  catalogContent: {
    display: "flex",
    flexDirection: "column",
    minWidth: 0,
  },
  catalogContentIntro: {
    paddingTop: 28,
    paddingBottom: 18,
    paddingHorizontal: 32,
    borderBottomWidth: 1,
    borderBottomColor: "#334155",
  },
  catalogContentIntroTitle: {
    color: "#f8fafc",
    fontSize: "clamp(2rem, 4vw, 2.5rem)",
    lineHeight: 1.08,
    fontWeight: "700",
    marginBottom: 8,
  },
  catalogContentIntroText: {
    color: "#94a3b8",
    fontSize: 15,
    lineHeight: 24,
  },
  catalogContentBody: {
    minHeight: 0,
    flexGrow: 1,
  },
  catalogContentBodyStacked: {
    overflow: "visible",
  },
  catalogContentBodyContent: {
    paddingTop: 24,
    paddingBottom: 28,
    paddingHorizontal: 28,
  },
  pageStack: {
    display: "grid",
    gap: 18,
  },
  pageHeader: {
    display: "grid",
    gap: 10,
  },
  pageHeaderCopy: {
    display: "grid",
    gap: 6,
  },
  pageHeaderTitle: {
    color: "#f8fafc",
    fontSize: "clamp(2rem, 4vw, 2.5rem)",
    lineHeight: 1.08,
    fontWeight: "700",
  },
  pageHeaderSummary: {
    color: "#94a3b8",
    fontSize: 15,
    lineHeight: 24,
    maxWidth: 760,
  },
  pageHeaderBadges: {
    display: "flex",
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 10,
  },
  badgeRow: {
    display: "flex",
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 10,
  },
  badge: {
    minHeight: 28,
    paddingHorizontal: 14,
    borderRadius: 999,
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
  },
  badgeAccent: {
    backgroundColor: "#10233f",
  },
  badgeSuccess: {
    backgroundColor: "#123c2f",
  },
  badgeWarning: {
    backgroundColor: "#3f2b12",
  },
  badgeText: {
    fontSize: 12,
    fontWeight: "700",
    letterSpacing: 0.3,
  },
  badgeTextAccent: {
    color: "#93c5fd",
  },
  badgeTextSuccess: {
    color: "#86efac",
  },
  badgeTextWarning: {
    color: "#fcd34d",
  },
  metricGrid: {
    display: "grid",
    gap: 12,
  },
  metricGridThree: {
    gridTemplateColumns: "repeat(3, minmax(0, 1fr))",
  },
  metricGridTwo: {
    gridTemplateColumns: "repeat(2, minmax(0, 1fr))",
  },
  singleColumnGrid: {
    gridTemplateColumns: "1fr",
  },
  metricTile: {
    display: "grid",
    gap: 6,
    padding: 16,
    backgroundColor: "#101826",
    borderWidth: 1,
    borderColor: "#1e293b",
    borderRadius: 14,
  },
  metricTileLabel: {
    color: "#94a3b8",
    fontSize: 12,
    fontWeight: "700",
    textTransform: "uppercase",
    letterSpacing: 1,
  },
  metricTileValue: {
    color: "#f8fafc",
    fontSize: "clamp(1.2rem, 2vw, 1.55rem)",
    fontWeight: "700",
  },
  sectionCard: {
    display: "grid",
    gap: 16,
    padding: 18,
    backgroundColor: "#0f172a",
    borderWidth: 1,
    borderColor: "#1e293b",
    borderRadius: 18,
  },
  sectionCardHeader: {
    display: "grid",
    gap: 4,
  },
  sectionCardTitle: {
    color: "#f8fafc",
    fontSize: 18,
    fontWeight: "700",
  },
  sectionCardSubtitle: {
    color: "#94a3b8",
    fontSize: 14,
    lineHeight: 22,
  },
  noteList: {
    display: "grid",
    gap: 10,
  },
  noteListItem: {
    display: "flex",
    flexDirection: "row",
    alignItems: "flex-start",
    gap: 10,
  },
  noteListBullet: {
    color: "#93c5fd",
    fontSize: 16,
    lineHeight: 22,
  },
  noteListText: {
    color: "#e2e8f0",
    fontSize: 14,
    lineHeight: 22,
    flexShrink: 1,
  },
  textField: {
    width: "100%",
    paddingVertical: 13,
    paddingHorizontal: 15,
    borderWidth: 1,
    borderColor: "#1e293b",
    borderRadius: 14,
    backgroundColor: "#09101d",
    color: "#f8fafc",
    fontSize: 15,
    lineHeight: 22,
    outlineStyle: "none",
  },
  textArea: {
    minHeight: 180,
    resize: "vertical",
    textAlignVertical: "top",
  },
  textAreaCompact: {
    minHeight: 110,
  },
  heroImage: {
    width: "100%",
    aspectRatio: 16 / 8.6,
    borderRadius: 14,
    backgroundColor: "#09101d",
  },
  mediaRow: {
    display: "grid",
    gridTemplateColumns: "220px minmax(0, 1fr)",
    gap: 16,
    alignItems: "start",
  },
  localImage: {
    width: "100%",
    height: 132,
    borderRadius: 14,
  },
  supportingCopy: {
    color: "#94a3b8",
    fontSize: 14,
    lineHeight: 22,
  },
  threeColumnGrid: {
    display: "grid",
    gap: 14,
    gridTemplateColumns: "repeat(3, minmax(0, 1fr))",
  },
  swatch: {
    minHeight: 152,
    borderWidth: 1,
    borderColor: "rgba(255, 255, 255, 0.08)",
    borderRadius: 16,
  },
  gradientSwatchA: {
    backgroundImage: "linear-gradient(135deg, #2563eb, #7c3aed)",
  },
  gradientSwatchB: {
    backgroundImage: "linear-gradient(135deg, #0f766e, #22c55e 58%, #fde047)",
  },
  gradientSwatchC: {
    backgroundImage: "radial-gradient(circle at 50% 40%, #1d4ed8 0%, #0f172a 60%, #93c5fd 100%)",
  },
  surfaceCard: {
    overflow: "hidden",
  },
  gradientSurface: {
    backgroundImage: "linear-gradient(135deg, rgba(37, 99, 235, 0.98), rgba(124, 58, 237, 0.96) 52%, rgba(236, 72, 153, 0.9))",
  },
  shaderSurface: {
    minHeight: 250,
  },
  surfaceCopy: {
    display: "grid",
    gap: 10,
    paddingTop: 70,
  },
  shaderSurfaceCopy: {
    minHeight: 178,
    borderRadius: 14,
    paddingHorizontal: 18,
    paddingBottom: 18,
  },
  surfaceTitle: {
    color: "#f8fafc",
    fontSize: 24,
    fontWeight: "700",
  },
  surfaceText: {
    color: "#dbeafe",
    fontSize: 14,
    lineHeight: 22,
    maxWidth: "52ch",
  },
  shaderSwatchBase: {
    backgroundSize: "180% 180%",
  },
  shaderSwatchA: {
    backgroundImage: "radial-gradient(circle at 24% 24%, rgba(56, 189, 248, 0.9), transparent 28%), linear-gradient(135deg, #082f49, #0f172a, #111827)",
  },
  shaderSwatchB: {
    backgroundImage: "repeating-linear-gradient(180deg, rgba(94, 234, 212, 0.16), rgba(94, 234, 212, 0.16) 6px, transparent 6px, transparent 12px), linear-gradient(135deg, #082f49, #0f172a)",
  },
  shaderSwatchC: {
    backgroundImage: "radial-gradient(circle at 50% 45%, rgba(192, 132, 252, 0.9), transparent 26%), radial-gradient(circle at 30% 25%, rgba(59, 130, 246, 0.55), transparent 24%), linear-gradient(135deg, #111827, #0f172a)",
  },
  progressTrack: {
    width: "100%",
    height: 18,
    overflow: "hidden",
    backgroundColor: "#09101d",
    borderWidth: 1,
    borderColor: "#1e293b",
    borderRadius: 999,
  },
  progressFill: {
    height: "100%",
    borderRadius: 999,
    backgroundImage: "linear-gradient(90deg, #38bdf8, #22c55e)",
  },
  orbitStage: {
    position: "relative",
    minHeight: 112,
    backgroundColor: "#0b1220",
    borderWidth: 1,
    borderColor: "#1e293b",
    borderRadius: 16,
  },
  orbitDot: {
    position: "absolute",
    width: 24,
    height: 24,
    marginLeft: -12,
    marginTop: -12,
    borderRadius: 999,
    backgroundColor: "#c084fc",
    borderWidth: 1,
    borderColor: "#e9d5ff",
  },
  decorCard: {
    minHeight: 166,
    padding: 18,
    borderRadius: 16,
    borderWidth: 1,
    borderColor: "rgba(255, 255, 255, 0.08)",
    display: "grid",
    gap: 8,
  },
  decorCardLinear: {
    backgroundImage: "linear-gradient(135deg, #1d4ed8, #7c3aed)",
    boxShadow: "10px 14px 28px rgba(0, 0, 0, 0.32)",
  },
  decorCardRadial: {
    backgroundImage: "radial-gradient(circle at 35% 25%, #38bdf8, #1e293b 58%, #0f172a)",
  },
  decorCardTitle: {
    color: "#e0e7ff",
    fontSize: 18,
    fontWeight: "700",
  },
  decorCardText: {
    color: "#e0e7ff",
    fontSize: 14,
    lineHeight: 22,
  },
  nestedScroll: {
    display: "grid",
    gap: 12,
  },
  outerScroll: {
    maxHeight: 340,
    padding: 14,
    backgroundColor: "#101826",
    borderWidth: 1,
    borderColor: "#1e293b",
    borderRadius: 16,
  },
  innerScroll: {
    maxHeight: 220,
    padding: 14,
    backgroundImage: "linear-gradient(135deg, #111827, #0f172a)",
    borderWidth: 1,
    borderColor: "#1e293b",
    borderRadius: 14,
  },
});

export default App;
