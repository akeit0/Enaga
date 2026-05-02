import type React from "react";
import type {
  HostState,
  NodeRef,
  StyleProp,
  StyleRecord,
} from "./public-types";

export type HostNodeType = "Scene" | "View" | "ScrollView" | "Text" | "TextInput" | "Image" | "Spacer";

export type GenericHostProps = {
  id?: string;
  nodeRef?: NodeRef;
  style?: StyleProp<StyleRecord>;
  children?: React.ReactNode;
  hidden?: boolean;
  [key: string]: any;
};

export type HostChild = HostNode | HostTextNode;
export type HostParent = HostContainer | HostNode;

export type HostNode = {
  runtimeId: string;
  publicId?: string;
  nodeRef?: NodeRef;
  parent: HostParent | null;
  type: HostNodeType;
  props: GenericHostProps;
  children: HostChild[];
  hidden: boolean;
};

export type HostTextNode = {
  parent: HostNode | null;
  runtimeId: string;
  type: "__text__";
  text: string;
  children: HostChild[];
  hidden: boolean;
};

export type HostContainer = {
  children: HostChild[];
};

export type NativeRendererRuntimeState = {
  currentUpdatePriority: number;
  hostNodesByRuntimeId: Map<string, HostNode>;
  container: HostContainer;
  renderer: NativeRendererLike | null;
  root: unknown | null;
  appComponent: React.ComponentType | null;
  appMounted: boolean;
  rendererInjectedIntoDevTools: boolean;
};

export type NativeHostStoreState = {
  hostState: HostState;
  hostStateSubscribers: Set<HostStateSubscriber>;
  hoverStateSubscribers: Set<HoverStateSubscriber>;
  hostStateRevision: { value: number };
  animationClaimCount: { value: number };
  shaderAnimationClaimCount: { value: number };
};

export type LayoutOffset = {
  left: number;
  top: number;
};

export type LayoutFrame = {
  width: number;
  height: number;
};

export type LayoutInsets = {
  left: number;
  top: number;
  right: number;
  bottom: number;
};

export type NativeResolvedContainerLayout = {
  padding: LayoutInsets;
  contentFrame: LayoutFrame;
  contentOffset: LayoutOffset;
};

export type NativeCreateHostNodeFunction = (
  type: HostNodeType,
  runtimeId: string,
  publicId: string | undefined,
  props: GenericHostProps,
) => HostNode;

export type NativeCreateTextNodeFunction = (
  runtimeId: string,
  text: string,
) => HostTextNode;

export type NativeMutateTreeFunction = (
  parent: unknown,
  child: unknown,
) => boolean;

export type NativeRendererLike = {
  createContainer: (...args: unknown[]) => unknown;
  updateContainerSync: (element: React.ReactNode, container: unknown, parentComponent: unknown, callback: unknown) => void;
  flushSyncWork: () => void;
  injectIntoDevTools?: () => boolean;
};

export type HostStateMaskValue = number;

export type HostStateSubscriber = {
  mask: HostStateMaskValue;
  notify: () => void;
};

export type HoverStateSubscriber = {
  runtimeId: string;
  hovered: boolean;
  notify: () => void;
};
