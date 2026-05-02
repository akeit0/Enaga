import React, { type ReactNode } from "react";
import {
  HostStateMask,
  Label,
  View,
  measureTextHeight,
  measureTextWidth,
  mergeDefinedStyle,
  normalizeStyle,
  resolveParentRuntimeId,
  resolveNodeRuntimeId,
  useComponentNodeRef,
  useHostState,
  useLayoutOffset,
} from "./native-runtime";
import type {NodeRef,StyleProp,ViewStyle} from "./public-types";

import {
  registerToolTip,
  resolveRegisteredToolTipContent,
  unregisterToolTip,
  useToolTipRegistryRevision,
} from "./tooltip-registry";

export function ToolTipOverlay(): ReactNode {
  const host = useHostState(HostStateMask.HoverTarget | HostStateMask.HoverBounds);
  useToolTipRegistryRevision();
  const parentOffset = useLayoutOffset();
  const content = host.hoveredId
    ? resolveRegisteredToolTipContent(host.hoveredId, (runtimeId) => resolveParentRuntimeId(runtimeId))
    : undefined;
  const stringContent = typeof content === "string" ? content : "";
  const hasStringContent = stringContent.length > 0;
  const customContent = typeof content === "string" || content === undefined ? null : content;
  const overlayVisible = hasStringContent || customContent !== null;

  return (
    <View
      id="native-hover-tooltip"
      style={{
        left: overlayVisible ? host.hoverTargetLeft - parentOffset.left : 0,
        top: overlayVisible ? host.hoverTargetTop + host.hoverTargetHeight + 8 - parentOffset.top : 0,
      }}
    >
      <DefaultToolTipBubble text={stringContent} visible={hasStringContent} />
      {customContent}
    </View>
  );
}

export const HoverTooltipOverlay = ToolTipOverlay;

export function DefaultToolTipBubble({ text, visible = true }: { text: string; visible?: boolean }) {
  const maxTextWidth = 280;
  const horizontalPadding = 10;
  const verticalPadding = 6;
  const fontSize = 13;
  const fontWeight = 500;
  const measuredTextWidth = measureTextWidth(text, { fontSize, fontWeight });
  const labelWidth = visible ? Math.max(1, Math.min(maxTextWidth, measuredTextWidth)) : 0;
  const bubbleWidth = visible ? labelWidth + horizontalPadding * 2 : 0;
  const labelHeight = visible ? measureTextHeight(text, labelWidth, { fontSize, fontWeight, wrap: true }) : 0;
  const bubbleHeight = visible ? labelHeight + verticalPadding * 2 : 0;

  return (
    <View
      style={{
        width: bubbleWidth,
        height: bubbleHeight,
        backgroundColor: visible ? "#020617" : undefined,
        borderColor: visible ? "#334155" : undefined,
        borderWidth: visible ? 1 : 0,
        borderRadius: visible ? 10 : 0,
        paddingLeft: visible ? horizontalPadding : 0,
        paddingTop: visible ? verticalPadding : 0,
        paddingRight: visible ? horizontalPadding : 0,
        paddingBottom: visible ? verticalPadding : 0,
        shadow: visible
          ? [
              { color: "#02061788", offsetY: 10, blur: 18, spread: 1 },
            ]
          : undefined,
      }}
    >
      <Label
        text={text}
        style={{
          width: labelWidth,
          height: labelHeight,
          fontSize,
          color: "#e2e8f0",
          fontWeight,
          wrap: true,
        }}
      />
    </View>
  );
}

export function ToolTip({
  id,
  nodeRef,
  content,
  style,
  children,
}: {
  id?: string;
  nodeRef?: NodeRef;
  content: ReactNode;
  style?: StyleProp<ViewStyle>;
  children?: ReactNode;
}) {
  const toolTipRef = useComponentNodeRef(nodeRef, id, "tooltip");
  const anchorId = resolveNodeRuntimeId(toolTipRef) ?? toolTipRef.runtimeKey;
  const wrapperStyle = mergeDefinedStyle(normalizeStyle<ViewStyle>(style), {
    flexDirection: "column",
    hoverable: true,
  });

  React.useLayoutEffect(() => {
    if (!anchorId) {
      return;
    }

    registerToolTip(anchorId, content);
    return () => unregisterToolTip(anchorId);
  }, [anchorId, content]);

  return (
    <View
      id={id}
      nodeRef={toolTipRef}
      style={wrapperStyle}
    >
      {children}
    </View>
  );
}
