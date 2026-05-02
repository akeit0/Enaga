import React, { type ReactNode } from "react";

const toolTipRegistry = new Map<string, ReactNode>();
const toolTipRegistrySubscribers = new Set<() => void>();
let toolTipRegistryRevision = 0;

function subscribeToolTipRegistry(notify: () => void) {
  toolTipRegistrySubscribers.add(notify);
  return () => {
    toolTipRegistrySubscribers.delete(notify);
  };
}

function publishToolTipRegistry() {
  toolTipRegistryRevision += 1;
  for (const notify of toolTipRegistrySubscribers) {
    notify();
  }
}

export function registerToolTip(anchorId: string, content: ReactNode) {
  toolTipRegistry.set(anchorId, content);
  publishToolTipRegistry();
}

export function unregisterToolTip(anchorId: string) {
  if (toolTipRegistry.delete(anchorId)) {
    publishToolTipRegistry();
  }
}

export function useToolTipRegistryRevision() {
  React.useSyncExternalStore(
    subscribeToolTipRegistry,
    () => toolTipRegistryRevision,
    () => toolTipRegistryRevision,
  );
}

export function resolveRegisteredToolTipContent(
  hoveredId: string,
  resolveParentId: (runtimeId: string) => string | undefined,
): ReactNode | undefined {
  let currentId: string | undefined = hoveredId;
  while (currentId) {
    const direct = toolTipRegistry.get(currentId);
    if (direct !== undefined) {
      return direct;
    }

    currentId = resolveParentId(currentId);
  }

  return undefined;
}
