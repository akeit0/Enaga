import React from "react";
import {
  View,
  Divider,
  HostStateMask,
  Label,
  Pane,
  Scene,
  ScrollView,
  StyleSheet,
  createNodeRef,
  useHostState,
  type StackAlign,
} from "../lib/react-okojo";
import { ToolTip, ToolTipOverlay } from "../lib/react-okojo-tooltips";
import { catalogTabs } from "./catalog-data";
import { catalogColors, clamp } from "./catalog-theme";
import type { CatalogTabId } from "./catalog-types";
import { CatalogPageWidthProvider } from "./catalog-ui";
import { AnimationPage } from "./pages/animation-page";
import { CommunicationPage } from "./pages/communication-page";
import { ComponentsPage } from "./pages/components-page";
import { GradientsPage } from "./pages/gradients-page";
import { InputsPage } from "./pages/inputs-page";
import { MinimumPage } from "./pages/minimum-page";
import { OverviewPage } from "./pages/overview-page";
import { RenderingPage } from "./pages/rendering-page";
import { ShadersPage } from "./pages/shaders-page";

let activeTab: CatalogTabId = "overview";
const catalogPages = {
  overview: OverviewPage,
  minimum: MinimumPage,
  inputs: InputsPage,
  rendering: RenderingPage,
  gradients: GradientsPage,
  shaders: ShadersPage,
  animation: AnimationPage,
  components: ComponentsPage,
  communication: CommunicationPage,
} satisfies Record<CatalogTabId, React.ComponentType>;

const pageScrollRefs = {
  overview: createNodeRef(),
  minimum: createNodeRef(),
  inputs: createNodeRef(),
  rendering: createNodeRef(),
  gradients: createNodeRef(),
  shaders: createNodeRef(),
  animation: createNodeRef(),
  components: createNodeRef(),
  communication: createNodeRef(),
};

const CatalogTabButton = React.memo(function CatalogTabButton({
  active,
  tabHeight,
  tab,
  onSelect,
}: {
  active: boolean;
  tabHeight: number;
  tab: typeof catalogTabs[number];
  onSelect: (tabId: CatalogTabId) => void;
}) {
  const handlePress = React.useCallback(() => {
    onSelect(tab.id);
  }, [onSelect, tab.id]);

  return (
    <ToolTip content={`Open ${tab.label}`}>
      <Pane
        onPress={handlePress}
        hoverStyle={active ? undefined : styles.tabPaneHover}
        style={[
          styles.tabPane,
          { height: tabHeight, hoverable: true },
          active
            ? styles.tabPaneActive
            : styles.tabPaneIdle,
        ]}
      >
        <Label text={tab.label} style={styles.tabLabel} />
        <Label
          text={tab.subtitle}
          style={[styles.tabSubtitle, active ? styles.tabSubtitleActive : styles.tabSubtitleIdle]}
        />
      </Pane>
    </ToolTip>
  );
});

const CatalogPageViewport = React.memo(function CatalogPageViewport({
  activeTab,
  contentHeight,
  contentWidth,
  pageScrollBottomInset,
  pageScrollInset,
  pageWidth,
}: {
  activeTab: CatalogTabId;
  contentHeight: number;
  contentWidth: number;
  pageScrollBottomInset: number;
  pageScrollInset: number;
  pageWidth: number;
}) {
  const ActivePage = catalogPages[activeTab];

  return (
    <ScrollView
      key={activeTab}
      nodeRef={pageScrollRefs[activeTab]}
      contentContainerStyle={{ width: pageWidth, alignItems: "stretch" }}
      style={[styles.contentScroll, {
        width: contentWidth,
        height: contentHeight,
        flex: 1,
        paddingLeft: pageScrollInset,
        paddingTop: pageScrollInset,
        paddingRight: pageScrollInset,
        paddingBottom: pageScrollBottomInset,
      }]}
    >
      <CatalogPageWidthProvider width={pageWidth}>
        <ActivePage />
      </CatalogPageWidthProvider>
    </ScrollView>
  );
});

export function SampleApp() {
  const host = useHostState(HostStateMask.Layout);
  const [activeTabState, setActiveTab] = React.useState<CatalogTabId>(activeTab);
  const outerMargin = clamp(Math.floor(Math.min(host.width, host.height) * 0.035), 16, 34);
  const panelPadding = clamp(Math.floor(host.width * 0.016), 16, 24);
  const frame = {
    left: outerMargin,
    top: outerMargin,
    width: Math.max(420, host.width - outerMargin * 2),
    height: Math.max(320, host.height - outerMargin * 2),
  };
  const sidebarWidth = clamp(Math.floor(frame.width * 0.24), 220, 310);
  const bodyGap = 10;
  const dividerWidth = 1;
  const contentHeight = frame.height - 110;
  const contentWidth = Math.max(220, frame.width - panelPadding * 2 - sidebarWidth - dividerWidth - bodyGap * 2);
  const pageScrollInset = 20;
  const pageScrollBottomInset = 28;
  const tabHeight = 54;
  const pageWidth = Math.max(220, contentWidth - pageScrollInset * 2);
  const handleSelectTab = React.useCallback((tabId: CatalogTabId) => {
    activeTab = tabId;
    setActiveTab(tabId);
  }, []);

  return (
    <Scene backgroundColor={catalogColors.scene}>
      <View
        style={[styles.catalogRoot, {
          left: frame.left,
          top: frame.top,
          width: frame.width,
          height: frame.height,
          paddingLeft: panelPadding,
          paddingTop: 20,
          paddingRight: panelPadding,
          paddingBottom: 24,
        }]}
      >
        <View style={styles.catalogHeader}>
          <Label text="Native renderer catalog" style={styles.catalogTitle} />
          <Label text="Practical multi-file sample app with reusable C# core, JS/React UI, and host-owned interaction." style={styles.catalogSubtitle} />
        </View>

        <View style={[styles.catalogBody, { height: contentHeight }]}>
          <View
            style={[styles.sidebarPane, {
              width: sidebarWidth,
              height: contentHeight,
            }]}
          >
            <Label text="Catalog" style={styles.sidebarTitle} />
            <Label text={`${catalogTabs.length} tabs · live host metrics on every page`} style={styles.sidebarNote} />
             <ScrollView
               contentContainerStyle={styles.tabList}
               style={styles.tabScroll}
             >
               {catalogTabs.map((tab) => (
                 <CatalogTabButton
                   key={tab.id}
                   active={activeTabState === tab.id}
                   tabHeight={tabHeight}
                   tab={tab}
                   onSelect={handleSelectTab}
                 />
               ))}
             </ScrollView>
           </View>

          <Divider top={10} length={contentHeight - 20} color={catalogColors.divider} />

          <CatalogPageViewport
            activeTab={activeTabState}
            contentHeight={contentHeight}
            contentWidth={contentWidth}
            pageScrollBottomInset={pageScrollBottomInset}
            pageScrollInset={pageScrollInset}
            pageWidth={pageWidth}
          />
        </View>
      </View>
      <ToolTipOverlay />
    </Scene>
  );
}

const styles = StyleSheet.create({
  catalogRoot: {
    padding: 24,
    backgroundColor: catalogColors.panel,
    borderColor: catalogColors.divider,
    borderWidth: 1,
    borderRadius: 22,
    gap: 18,
  },
  catalogHeader: {
    gap: 6,
    alignItems: "stretch" as StackAlign,
  },
  catalogTitle: {
    color: catalogColors.title,
    fontSize: 28,
    fontWeight: 700,
    wrap: true,
  },
  catalogSubtitle: {
    color: catalogColors.text,
    fontSize: 14,
    wrap: true,
  },
  fastRefreshBadge: {
    alignSelf: "start" as StackAlign,
    borderWidth: 0,
    borderRadius: 999,
    backgroundColor: "#123c2f",
    borderColor: "#123c2f",
    paddingLeft: 10,
    paddingTop: 4,
    paddingRight: 10,
    paddingBottom: 4,
  },
  fastRefreshBadgeLabel: {
    color: "#86efac",
    fontSize: 12,
    fontWeight: 700,
  },
  catalogBody: {
    flexDirection: "row",
    gap: 10,
    alignItems: "stretch" as StackAlign,
  },
  sidebarPane: {
    backgroundColor: catalogColors.pane,
    borderColor: catalogColors.border,
    borderWidth: 1,
    borderRadius: 16,
    overflow: "hidden",
    paddingLeft: 18,
    paddingTop: 16,
    paddingRight: 18,
    paddingBottom: 16,
    gap: 8,
  },
  sidebarTitle: {
    color: catalogColors.accent,
    fontSize: 18,
    fontWeight: 700,
    wrap: true,
  },
  sidebarNote: {
    color: catalogColors.muted,
    fontSize: 13,
    wrap: true,
  },
  tabList: {
    gap: 8,
    alignItems: "stretch" as StackAlign,
    margin :10
  },
  tabScroll: {
    flex: 1,
    paddingRight: 10,
  },
  tabPane: {
    borderWidth: 0,
    borderRadius: 12,
    overflow: "hidden",
    paddingLeft: 16,
    paddingTop: 10,
    paddingRight: 16,
    paddingBottom: 10,
    gap: 4,
  },
  tabLabel: {
    color: catalogColors.title,
    fontSize: 15,
    fontWeight: 700,
    wrap: true,
  },
  tabSubtitle: {
    fontSize: 11,
    wrap: true,
  },
  tabPaneIdle: {
    backgroundColor: catalogColors.buttonOff,
    borderColor: catalogColors.buttonOff,
  },
  tabPaneHover: {
    backgroundColor: "#475569",
    borderColor: "#475569",
  },
  tabPaneActive: {
    backgroundColor: catalogColors.buttonOn,
    borderColor: catalogColors.buttonOn,
  },
  tabSubtitleIdle: {
    color: catalogColors.muted,
  },
  tabSubtitleActive: {
    color: "#dbeafe",
  },
  contentScroll: {
    backgroundColor: catalogColors.pane,
    borderColor: catalogColors.border,
    borderWidth: 1,
    borderRadius: 16,
  },
});
