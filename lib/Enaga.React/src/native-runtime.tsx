import React, { type ComponentType, type ReactNode } from "react";
import Reconciler from "react-reconciler";
import { DefaultEventPriority, LegacyRoot } from "react-reconciler/constants";
import type {
  ButtonLabelStyle,
  ButtonProps,
  ButtonStyle,
  HostPanelProps,
  HostState,
  ImageHostProps,
  ImageStyle,
  LayoutStyle,
  NodeHandle,
  NodeRef,
  PaneProps,
  PressHandler,
  RuntimeShader,
  SceneProps,
  ScrollViewProps,
  SpacerProps,
  StackAlignSelf,
  StyleProp,
  StyleRecord,
  TextInputHostProps,
  TextInputProps,
  TextInputStyle,
  TextProps,
  TextStyle,
  ViewProps,
  RadialGradient,
  LinearGradient,
} from "./public-types";
import type {
  HostChild,
  HostContainer,
  HostNode,
  HostNodeType,
  HostTextNode,
  GenericHostProps,
  HostStateMaskValue,
  HostStateSubscriber,
  HoverStateSubscriber,
  LayoutOffset,
  NativeHostStoreState,
  NativeRendererLike,
  NativeRendererRuntimeState,
} from "./native-runtime-types";

let nextId = 1;
const runtimeShaderSourceIds = new Map<string, string>();
let nextRuntimeShaderSourceId = 1;
const nativeLayoutGlobals = globalThis as unknown as NativeHostGlobals;

const nativeCreateHostNode = nativeLayoutGlobals.nativeCreateHostNode;
const nativeCreateTextNode = nativeLayoutGlobals.nativeCreateTextNode;
const nativeResetAfterCommit = nativeLayoutGlobals.nativeResetAfterCommit;
const nativeHasLayoutAffectingHostPropChange = nativeLayoutGlobals.nativeHasLayoutAffectingHostPropChange;
const nativeCommitHostUpdate = nativeLayoutGlobals.nativeCommitHostUpdate;
const nativeCommitTextUpdate = nativeLayoutGlobals.nativeCommitTextUpdate;
const nativeSetNodeHidden = nativeLayoutGlobals.nativeSetNodeHidden;
const nativeAppendChild = nativeLayoutGlobals.nativeAppendChild;
const nativeInsertChildBefore = nativeLayoutGlobals.nativeInsertChildBefore;
const nativeRemoveChild = nativeLayoutGlobals.nativeRemoveChild;
const nativeClearChildren = nativeLayoutGlobals.nativeClearChildren;
const nativeGetParentRuntimeId = nativeLayoutGlobals.nativeGetParentRuntimeId;
const nativeResolveContainerLayout = nativeLayoutGlobals.nativeResolveContainerLayout;
const nativeMeasureTextWidth = nativeLayoutGlobals.nativeMeasureTextWidth;
const nativeConfigureFonts = nativeLayoutGlobals.configureFonts;
const nativeRegisterFont = nativeLayoutGlobals.registerFont;
const nativeMeasureTextHeight = nativeLayoutGlobals.nativeMeasureTextHeight;
const nativeSetAnimationEnabled = nativeLayoutGlobals.setAnimationEnabled;
const nativeSetShaderAnimationEnabled = nativeLayoutGlobals.setShaderAnimationEnabled;
const nativeRuntimeStateGlobal = globalThis as typeof globalThis & {
  __nativeFastRefreshEnabled?: boolean;
  __nativeReactRendererState?: NativeRendererRuntimeState;
  __nativeReactHostStoreState?: NativeHostStoreState;
};
function createNativeRendererRuntimeState(): NativeRendererRuntimeState {
  return {
    currentUpdatePriority: DefaultEventPriority,
    hostNodesByRuntimeId: new Map<string, HostNode>(),
    container: { children: [] },
    renderer: null,
    root: null,
    appComponent: null,
    appMounted: false,
    rendererInjectedIntoDevTools: false,
  };
}

function createNativeHostStoreState(): NativeHostStoreState {
  return {
    hostState: {
      width: 1280,
      height: 800,
      frame: 0,
      elapsedMs: 0,
      mouseX: 0,
      mouseY: 0,
      mouseButtons: 0,
      pointerDownSeq: 0,
      lastWheelDeltaX: 0,
      lastWheelDeltaY: 0,
      lastInputSynthetic: false,
      lastKey: "",
      keyModifiers: 0,
      keyRepeat: false,
      keyDownSeq: 0,
      keyUpSeq: 0,
      lastTextInput: "",
      textInputSeq: 0,
      textInputEventSeq: 0,
      textInputEventId: "",
      textInputEventKind: "",
      textInputEventValue: "",
      textInputEventCaretIndex: 0,
      textInputEventFocused: false,
      imageEventSeq: 0,
      imageEventId: "",
      imageEventKind: "",
      imageEventSource: "",
      imageEventDetail: "",
      scrollX: 0,
      scrollY: 0,
      animationEnabled: false,
      hoveredId: "",
      hoverTargetLeft: 0,
      hoverTargetTop: 0,
      hoverTargetWidth: 0,
      hoverTargetHeight: 0,
    },
    hostStateSubscribers: new Set<HostStateSubscriber>(),
    hoverStateSubscribers: new Set<HoverStateSubscriber>(),
    hostStateRevision: { value: 0 },
    animationClaimCount: { value: 0 },
    shaderAnimationClaimCount: { value: 0 },
  };
}

const nativeRendererRuntimeState = nativeRuntimeStateGlobal.__nativeFastRefreshEnabled === true
  ? (nativeRuntimeStateGlobal.__nativeReactRendererState ??= createNativeRendererRuntimeState())
  : createNativeRendererRuntimeState();

const nativeHostStoreState = nativeRuntimeStateGlobal.__nativeFastRefreshEnabled === true
  ? (nativeRuntimeStateGlobal.__nativeReactHostStoreState ??= createNativeHostStoreState())
  : createNativeHostStoreState();

export const hostNodesByRuntimeId = nativeRendererRuntimeState.hostNodesByRuntimeId;
type NamedStyles<T extends Record<string, StyleRecord>> = {
  readonly [K in keyof T]: Readonly<T[K]>;
};

function createHostComponent<P extends object>(tag: HostNodeType): ComponentType<P> {
  return function HostComponent(props: P) {
    return React.createElement(tag, props);
  };
}

const SceneHost = createHostComponent<SceneProps>("Scene");
const ViewHost = createHostComponent<ViewProps>("View");
const ScrollViewHost = createHostComponent<ScrollViewProps>("ScrollView");
const TextHost = createHostComponent<TextProps>("Text");
const TextInputHost = createHostComponent<TextInputHostProps>("TextInput");
const ImageHost = createHostComponent<ImageHostProps>("Image");
const SpacerHost = createHostComponent<SpacerProps>("Spacer");
const hostContext: Record<string, never> = {};
const LayoutOffsetContext = React.createContext<LayoutOffset>({ left: 0, top: 0 });
const LayoutFrameContext = React.createContext<{ width: number; height: number }>({ width: 0, height: 0 });

export const KeyModifiers = Object.freeze({
  Shift: 1,
  Control: 2,
  Alt: 4,
  Meta: 8,
});

export const HostStateMask = Object.freeze({
  Layout: 1 << 0,
  PointerPosition: 1 << 1,
  PointerPress: 1 << 2,
  HoverTarget: 1 << 3,
  HoverBounds: 1 << 4,
  HoverTooltip: 1 << 5,
  Scroll: 1 << 6,
  Animation: 1 << 7,
  Keyboard: 1 << 8,
  TextInput: 1 << 9,
  Image: 1 << 10,
  All: (1 << 11) - 1,
});

const hostState = nativeHostStoreState.hostState;
const hostStateSubscribers = nativeHostStoreState.hostStateSubscribers;
const hoverStateSubscribers = nativeHostStoreState.hoverStateSubscribers;
const hostStateRevision = nativeHostStoreState.hostStateRevision;

function subscribeHostState(mask: HostStateMaskValue, notify: () => void) {
  const subscriber: HostStateSubscriber = { mask, notify };
  hostStateSubscribers.add(subscriber);
  return () => {
    hostStateSubscribers.delete(subscriber);
  };
}

function subscribeHoverState(runtimeId: string, notify: () => void) {
  const subscriber: HoverStateSubscriber = {
    runtimeId,
    hovered: runtimeId.length > 0 && hostState.hoveredId === runtimeId,
    notify,
  };
  hoverStateSubscribers.add(subscriber);
  return () => {
    hoverStateSubscribers.delete(subscriber);
  };
}

function publishHostState(mask: HostStateMaskValue) {
  if (mask === 0) {
    return;
  }

  if ((mask & HostStateMask.HoverTarget) !== 0) {
    for (const subscriber of hoverStateSubscribers) {
      const nextHovered = subscriber.runtimeId.length > 0 && hostState.hoveredId === subscriber.runtimeId;
      if (subscriber.hovered !== nextHovered) {
        subscriber.hovered = nextHovered;
        subscriber.notify();
      }
    }
  }

  hostStateRevision.value += 1;
  for (const subscriber of hostStateSubscribers) {
    if ((subscriber.mask & mask) !== 0) {
      subscriber.notify();
    }
  }
  NativeRenderer.flushSyncWork();
}

function normalizePublicId(value: unknown): string | undefined {
  return typeof value === "string" && value.length > 0
    ? value
    : undefined;
}

export function createNodeRef<T extends NodeHandle = NodeHandle>(): NodeRef<T> {
  return {
    runtimeKey: `ref-${nextId++}`,
    current: null,
  };
}

export function useNodeRef<T extends NodeHandle = NodeHandle>(): NodeRef<T> {
  return React.useMemo(() => createNodeRef<T>(), []);
}

export function useComponentNodeRef(nodeRef: NodeRef | undefined, explicitId: string | undefined, prefix: string): NodeRef {
  return React.useMemo(() => nodeRef ?? {
    runtimeKey: normalizePublicId(explicitId) ?? `${prefix}-${nextId++}`,
    current: null,
  }, [explicitId, nodeRef, prefix]);
}

function isDebugEnabled() {
  return (globalThis as typeof globalThis & { nativeDebugEnabled?: boolean }).nativeDebugEnabled === true;
}

function syncHostState() {
  const globals = globalThis as typeof globalThis & {
    width?: number;
    height?: number;
    hoveredId?: string;
    hoverTargetLeft?: number;
    hoverTargetTop?: number;
    hoverTargetWidth?: number;
    hoverTargetHeight?: number;
  };

  let changedMask = 0;

  if (typeof globals.width === "number" && hostState.width !== globals.width) {
    hostState.width = globals.width;
    changedMask |= HostStateMask.Layout;
  }
  if (typeof globals.height === "number" && hostState.height !== globals.height) {
    hostState.height = globals.height;
    changedMask |= HostStateMask.Layout;
  }
  if (typeof globals.hoveredId === "string" && hostState.hoveredId !== globals.hoveredId) {
    hostState.hoveredId = globals.hoveredId;
    changedMask |= HostStateMask.HoverTarget;
  }
  if (typeof globals.hoverTargetLeft === "number" && hostState.hoverTargetLeft !== globals.hoverTargetLeft) {
    hostState.hoverTargetLeft = globals.hoverTargetLeft;
    changedMask |= HostStateMask.HoverBounds;
  }
  if (typeof globals.hoverTargetTop === "number" && hostState.hoverTargetTop !== globals.hoverTargetTop) {
    hostState.hoverTargetTop = globals.hoverTargetTop;
    changedMask |= HostStateMask.HoverBounds;
  }
  if (typeof globals.hoverTargetWidth === "number" && hostState.hoverTargetWidth !== globals.hoverTargetWidth) {
    hostState.hoverTargetWidth = globals.hoverTargetWidth;
    changedMask |= HostStateMask.HoverBounds;
  }
  if (typeof globals.hoverTargetHeight === "number" && hostState.hoverTargetHeight !== globals.hoverTargetHeight) {
    hostState.hoverTargetHeight = globals.hoverTargetHeight;
    changedMask |= HostStateMask.HoverBounds;
  }

  return changedMask;
}
function hostLog(message: string) {
  if (!isDebugEnabled()) {
    return;
  }

  if (typeof nativeHostLog === "function") {
    nativeHostLog(message);
    return;
  }

  console.log(message);
}

function createHostNode(type: HostNodeType, props: GenericHostProps): HostNode {
  const instance = nativeCreateHostNode(
    type,
    props.nodeRef?.runtimeKey ?? `node-${nextId++}`,
    normalizePublicId(props.id),
    props,
  );
  instance.nodeRef = props.nodeRef;
  hostNodesByRuntimeId.set(instance.runtimeId, instance);
  bindNodeRef(props.nodeRef, instance);
  return instance;
}

function createTextNode(text: string): HostTextNode {
  return nativeCreateTextNode(`text-${nextId++}`, text);
}

function bindNodeRef(nodeRef: NodeRef | undefined, handle: NodeHandle | null) {
  if (nodeRef !== undefined) {
    nodeRef.current = handle;
  }
}

function releaseNodeRef(nodeRef: NodeRef | undefined, handle: NodeHandle) {
  if (nodeRef?.current === handle) {
    nodeRef.current = null;
  }
}

function detachChild(child: HostChild): void {
  child.parent = null;
  if (child.type === "__text__") {
    return;
  }

  hostNodesByRuntimeId.delete(child.runtimeId);

  for (const nestedChild of child.children) {
    detachChild(nestedChild);
  }

  releaseNodeRef(child.nodeRef, child);
}

type NodeTarget = string | NodeHandle | NodeRef | null | undefined;

function isNodeRef(target: NodeTarget): target is NodeRef {
  return typeof target === "object" && target !== null && "runtimeKey" in target && "current" in target;
}

function isNodeHandle(target: NodeTarget): target is NodeHandle {
  return typeof target === "object" && target !== null && "runtimeId" in target;
}

export function resolveNodeRuntimeId(target: NodeTarget): string | undefined {
  if (typeof target === "string" && target.length > 0) {
    return target;
  }

  if (isNodeHandle(target)) {
    return target.runtimeId;
  }

  if (isNodeRef(target)) {
    return target.current?.runtimeId ?? target.runtimeKey;
  }

  return undefined;
}

export function resolveNodeHandle<T extends NodeHandle = NodeHandle>(target: NodeRef<T> | T | null | undefined): T | null {
  if (isNodeRef(target)) {
    return target.current as T | null;
  }

  return (target ?? null) as T | null;
}

export function resolveParentRuntimeId(runtimeId: string): string | undefined {
  return runtimeId.length > 0 ? nativeGetParentRuntimeId(runtimeId) : undefined;
}

export function useAttachRuntimeNode(
  target: NodeRef | NodeHandle | null | undefined,
  attach?: (runtimeId: string) => void,
): void {
  const runtimeId = resolveNodeRuntimeId(target) ?? "";
  React.useEffect(() => {
    if (attach === undefined) {
      return;
    }

    attach(runtimeId);
    return () => attach("");
  }, [attach, runtimeId]);
}

function updateNodeRefBinding(instance: HostNode, previousNodeRef: NodeRef | undefined, nextNodeRef: NodeRef | undefined) {
  if (previousNodeRef === nextNodeRef) {
    return;
  }

  releaseNodeRef(previousNodeRef, instance);
  bindNodeRef(nextNodeRef, instance);
  instance.nodeRef = nextNodeRef;
}

function isHoveredTarget(target: NodeTarget, hoveredId = hostState.hoveredId): boolean {
  const runtimeId = resolveNodeRuntimeId(target);
  return runtimeId !== undefined && hoveredId === runtimeId;
}

function isHoveredRuntimeId(runtimeId: string, hoveredId = hostState.hoveredId): boolean {
  return runtimeId.length > 0 && hoveredId === runtimeId;
}

function appendChild(parent: HostContainer | HostNode, child: HostChild): void {
  nativeAppendChild(parent, child);
}

function removeChild(parent: HostContainer | HostNode, child: HostChild): void {
  if (nativeRemoveChild(parent, child)) {
    detachChild(child);
  }
}

function insertBefore(parent: HostContainer | HostNode, child: HostChild, beforeChild: HostChild): void {
  nativeInsertChildBefore(parent, child, beforeChild);
}

function isStyleRecord(value: unknown): value is StyleRecord {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function flattenStyle<T extends StyleRecord>(style: StyleProp<T>): T {
  if (!style) {
    return {} as T;
  }

  if (Array.isArray(style)) {
    const result: StyleRecord = {};
    for (const entry of style) {
      const flattened = flattenStyle(entry as StyleProp<T>);
      Object.assign(result, flattened);
    }

    return result as T;
  }

  return (isStyleRecord(style) ? style : {}) as T;
}

export function normalizeStyle<T extends StyleRecord>(style: StyleProp<T>): T {
  return flattenStyle(style);
}

export const StyleSheet = Object.freeze({
  create<T extends Record<string, StyleRecord>>(styles: T): NamedStyles<T> {
    const entries = Object.entries(styles).map(([name, style]) => [name, Object.freeze({ ...style })]);
    return Object.freeze(Object.fromEntries(entries)) as NamedStyles<T>;
  },
  flatten<T extends StyleRecord>(style: StyleProp<T>): T {
    return flattenStyle(style);
  },
  compose<T extends StyleRecord>(first: StyleProp<T>, second: StyleProp<T>): StyleProp<T> {
    if (!first) {
      return second;
    }

    if (!second) {
      return first;
    }

    return [first, second];
  },
});

function numericStyleValue(value: unknown): number {
  return typeof value === "number" ? value : 0;
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function areComparableValuesEqual(left: unknown, right: unknown): boolean {
  if (Object.is(left, right)) {
    return true;
  }

  if (Array.isArray(left) && Array.isArray(right)) {
    if (left.length !== right.length) {
      return false;
    }

    for (let index = 0; index < left.length; index += 1) {
      if (!areComparableValuesEqual(left[index], right[index])) {
        return false;
      }
    }

    return true;
  }

  if (isPlainObject(left) && isPlainObject(right)) {
    const leftKeys = Object.keys(left);
    const rightKeys = Object.keys(right);
    if (leftKeys.length !== rightKeys.length) {
      return false;
    }

    for (const key of leftKeys) {
      if (!(key in right) || !areComparableValuesEqual(left[key], right[key])) {
        return false;
      }
    }

    return true;
  }

  return false;
}

function areHostPropsEqual(oldProps: Record<string, unknown>, newProps: Record<string, unknown>) {
  const oldKeys = Object.keys(oldProps).filter((key) => key !== "children");
  const newKeys = Object.keys(newProps).filter((key) => key !== "children");
  if (oldKeys.length !== newKeys.length) {
    return false;
  }

  for (const key of oldKeys) {
    if (!(key in newProps)) {
      return false;
    }

    if (key === "style") {
      const oldStyle = normalizeStyle(oldProps.style as StyleProp<StyleRecord>);
      const newStyle = normalizeStyle(newProps.style as StyleProp<StyleRecord>);
      if (!areComparableValuesEqual(oldStyle, newStyle)) {
        return false;
      }

      continue;
    }

    if (!areComparableValuesEqual(oldProps[key], newProps[key])) {
      return false;
    }
  }

  return true;
}

export function mergeDefinedStyle<T extends StyleRecord>(base: T, extra: Partial<T>): T {
  const result: StyleRecord = { ...base };
  for (const [key, value] of Object.entries(extra)) {
    if (value !== undefined) {
      result[key] = value;
    }
  }

  return result as T;
}

type TextMeasureOptions = {
  fontSize?: number;
  fontFamily?: string;
  fontWeight?: number;
  wrap?: boolean;
};

const textHeightMeasureCache = new Map<string, number>();
const textWidthMeasureCache = new Map<string, number>();

export function measureTextHeight(
  text: string,
  width: number,
  { fontSize = 18, fontFamily, fontWeight = 400, wrap = false }: TextMeasureOptions = {},
): number {
  const normalizedWidth = Number.isFinite(width) && width > 0 ? width : 0;
  const cacheKey = `${text}|${normalizedWidth}|${fontSize}|${fontWeight}|${fontFamily ?? ""}|${wrap ? 1 : 0}`;
  const cached = textHeightMeasureCache.get(cacheKey);
  if (cached !== undefined) {
    return cached;
  }

  const style = { fontSize, fontFamily, fontWeight, wrap };
  const measured = nativeMeasureTextHeight(text, normalizedWidth, style)
  const resolved = Math.max(Math.ceil(fontSize * 1.35), Math.ceil(measured));
  textHeightMeasureCache.set(cacheKey, resolved);
  return resolved;
}

export function measureTextWidth(
  text: string,
  { fontSize = 18, fontFamily, fontWeight = 400 }: Omit<TextMeasureOptions, "wrap"> = {},
): number {
  const cacheKey = `${text}|${fontSize}|${fontWeight}|${fontFamily ?? ""}`;
  const cached = textWidthMeasureCache.get(cacheKey);
  if (cached !== undefined) {
    return cached;
  }

  const style = { fontSize, fontFamily, fontWeight, wrap: false };
  const measured = typeof nativeMeasureTextWidth === "function"
    ? nativeMeasureTextWidth(text, style)
    : Math.ceil(text.length * fontSize * 0.62);
  const resolved = Math.max(0, Math.ceil(measured));
  textWidthMeasureCache.set(cacheKey, resolved);
  return resolved;
}

function readNumericProp(source: Record<string, any>, name: string): number | undefined {
  const prop = source[name];
  return typeof prop === "number" ? prop : undefined;
}

function getElementFrameProps(element: React.ReactElement<any>) {
  const props = (element.props ?? {}) as Record<string, any>;
  const style = normalizeStyle<Record<string, any>>(props.style);
  return {
    props,
    style,
    left: readNumericProp(props, "left") ?? readNumericProp(style, "left"),
    top: readNumericProp(props, "top") ?? readNumericProp(style, "top"),
    right: readNumericProp(props, "right") ?? readNumericProp(style, "right"),
    bottom: readNumericProp(props, "bottom") ?? readNumericProp(style, "bottom"),
    width: readNumericProp(props, "width") ?? readNumericProp(style, "width"),
    height: readNumericProp(props, "height") ?? readNumericProp(style, "height"),
    minWidth: readNumericProp(props, "minWidth") ?? readNumericProp(style, "minWidth"),
    maxWidth: readNumericProp(props, "maxWidth") ?? readNumericProp(style, "maxWidth"),
    minHeight: readNumericProp(props, "minHeight") ?? readNumericProp(style, "minHeight"),
    maxHeight: readNumericProp(props, "maxHeight") ?? readNumericProp(style, "maxHeight"),
    alignSelf: typeof props.alignSelf === "string"
      ? props.alignSelf as StackAlignSelf
      : typeof style.alignSelf === "string"
        ? style.alignSelf as StackAlignSelf
        : undefined,
  };
}

function resolveAxisFrame(
  start: number | undefined,
  end: number | undefined,
  size: number | undefined,
  parentSize: number,
  fallbackSize: number,
) {
  const resolvedSize = typeof size === "number"
    ? size
    : typeof end === "number"
      ? Math.max(0, parentSize - (start ?? 0) - end)
      : fallbackSize;
  const resolvedStart = typeof start === "number"
    ? start
    : typeof end === "number"
      ? Math.max(0, parentSize - resolvedSize - end)
      : 0;
  return { start: resolvedStart, size: resolvedSize };
}

export function resolveFrameMetrics(
  frame: Pick<ReturnType<typeof getElementFrameProps>, "left" | "top" | "right" | "bottom" | "width" | "height">,
  parentWidth: number,
  parentHeight: number,
  fallbackWidth = 0,
  fallbackHeight = 0,
  margin = { left: 0, top: 0, right: 0, bottom: 0 },
) {
  const resolvedX = resolveAxisFrame(frame.left, frame.right, frame.width, Math.max(0, parentWidth - margin.left - margin.right), fallbackWidth);
  const resolvedY = resolveAxisFrame(frame.top, frame.bottom, frame.height, Math.max(0, parentHeight - margin.top - margin.bottom), fallbackHeight);
  return {
    left: resolvedX.start + margin.left,
    top: resolvedY.start + margin.top,
    width: resolvedX.size,
    height: resolvedY.size,
  };
}

function clampMeasuredSize(value: number, minValue?: number, maxValue?: number) {
  let result = Number.isFinite(value) ? value : 0;
  if (typeof minValue === "number") {
    result = Math.max(result, Math.max(0, minValue));
  }

  if (typeof maxValue === "number") {
    const resolvedMax = Math.max(typeof minValue === "number" ? Math.max(0, minValue) : 0, maxValue);
    result = Math.min(result, resolvedMax);
  }

  return Math.max(0, result);
}

function getRuntimeShaderSourceId(source: string): string {
  const cached = runtimeShaderSourceIds.get(source);
  if (cached) {
    return cached;
  }

  const created = `shader-${nextRuntimeShaderSourceId++}`;
  runtimeShaderSourceIds.set(source, created);
  return created;
}

export function createLinearGradient(
  colors: string[],
  options?: Omit<LinearGradient, "type" | "colors">,
): LinearGradient {
  return {
    type: "linear",
    colors,
    ...options,
  };
}

export function createRadialGradient(
  colors: string[],
  options?: Omit<RadialGradient, "type" | "colors">,
): RadialGradient {
  return {
    type: "radial",
    colors,
    ...options,
  };
}

export function createRuntimeShader(
  source: string,
  uniforms?: RuntimeShader["uniforms"],
  options?: { hostTime?: boolean },
): RuntimeShader {
  return { sourceId: getRuntimeShaderSourceId(source), source, uniforms, hostTime: options?.hostTime === true };
}

export function configureFonts(options: { defaultFamily?: string; fallbackFamilies?: string[] }): void {
  nativeConfigureFonts(options.defaultFamily, options.fallbackFamilies);
}

export function registerFont(family: string, source: string): void {
  nativeRegisterFont(family, source);
}

const container = nativeRendererRuntimeState.container;

const hostConfig = {
  supportsMutation: true,
  supportsPersistence: false,
  supportsHydration: false,
  isPrimaryRenderer: true,
  noTimeout: -1,
  supportsMicrotasks: true,
  scheduleTimeout: setTimeout,
  cancelTimeout: clearTimeout,
  scheduleMicrotask: queueMicrotask,
  getInstanceFromNode() {
    return null;
  },
  setCurrentUpdatePriority(priority: number) {
    nativeRendererRuntimeState.currentUpdatePriority = priority;
  },
  getCurrentUpdatePriority() {
    return nativeRendererRuntimeState.currentUpdatePriority;
  },
  resolveUpdatePriority() {
    return nativeRendererRuntimeState.currentUpdatePriority;
  },
  trackSchedulerEvent() { },
  getCurrentEventPriority() {
    return DefaultEventPriority;
  },
  resolveEventType() {
    return null;
  },
  resolveEventTimeStamp() {
    return hostState.elapsedMs;
  },
  shouldAttemptEagerTransition() {
    return false;
  },
  getRootHostContext() {
    return hostContext;
  },
  getChildHostContext(parentHostContext: unknown) {
    return parentHostContext;
  },
  getPublicInstance(instance: unknown) {
    return instance;
  },
  prepareForCommit() {
    return null;
  },
  resetAfterCommit(rootContainer: unknown) {
    //let t = performance.now();
    nativeResetAfterCommit((rootContainer as any).children, "#08111f");
    //console.log(`nativeResetAfterCommit: ${performance.now() - t}ms`);
  },
  preparePortalMount() { },
  createInstance(type: HostNodeType, props: Record<string, unknown>) {
    return createHostNode(type, props);
  },
  createTextInstance(text: string) {
    return createTextNode(text);
  },
  appendInitialChild(parent: HostNode | HostContainer, child: HostChild) {
    appendChild(parent, child);
  },
  finalizeInitialChildren() {
    return false;
  },
  shouldSetTextContent() {
    return false;
  },
  prepareUpdate(instance: any, type: any, oldProps: Record<string, unknown>, newProps: Record<string, unknown>) {
    return areHostPropsEqual(
      oldProps as Record<string, unknown>,
      newProps as Record<string, unknown>,
    ) ? null : true;
  },
  appendChild,
  appendChildToContainer(parent: HostNode | HostContainer, child: HostChild) {
    appendChild(parent, child);
  },
  insertBefore,
  insertInContainerBefore(parent: HostNode | HostContainer, child: HostChild, beforeChild: HostChild) {
    insertBefore(parent, child, beforeChild);
  },
  removeChild,
  removeChildFromContainer(parent: HostNode | HostContainer, child: HostChild) {
    removeChild(parent, child);
  },
  commitUpdate(instance: any, type: any, oldProps: Record<string, NodeRef | undefined>, newProps: Record<string, NodeRef | undefined>) {
    updateNodeRefBinding(instance, oldProps.nodeRef, newProps.nodeRef);
    nativeCommitHostUpdate(
      instance,
      newProps as Record<string, unknown>,
      normalizePublicId(newProps.id),
      nativeHasLayoutAffectingHostPropChange(
        oldProps as Record<string, unknown>,
        newProps as Record<string, unknown>,
      ),
    );
  },
  commitTextUpdate(textInstance: unknown, oldText: string, newText: string) {
    nativeCommitTextUpdate(textInstance, oldText, newText);
  },
  resetTextContent() { },
  commitMount() { },
  hideInstance(instance: unknown) {
    nativeSetNodeHidden(instance, true);
  },
  hideTextInstance(instance: unknown) {
    nativeSetNodeHidden(instance, true);
  },
  unhideInstance(instance: unknown) {
    nativeSetNodeHidden(instance, false);
  },
  unhideTextInstance(instance: unknown) {
    nativeSetNodeHidden(instance, false);
  },
  clearContainer(rootContainer: HostContainer) {
    const removedChildren = [...rootContainer.children];
    nativeClearChildren(rootContainer);
    for (const child of removedChildren) {
      detachChild(child);
    }
  },
  detachDeletedInstance() { },
  maySuspendCommit() {
    return false;
  },
  maySuspendCommitOnUpdate() {
    return false;
  },
  maySuspendCommitInSyncRender() {
    return false;
  },
  preloadInstance() { },
  startSuspendingCommit() { },
  suspendInstance() { },
  waitForCommitToBeReady() {
    return null;
  },
  getSuspendedCommitReason() {
    return null;
  },
  beforeActiveInstanceBlur() { },
  afterActiveInstanceBlur() { },
  prepareScopeUpdate() { },
  getInstanceFromScope() {
    return null;
  },
  setCurrentUpdateLanePriority() { },
  getCurrentUpdateLanePriority() {
    return 0;
  },
  NotPendingTransition: null,
};

const NativeRenderer = nativeRendererRuntimeState.renderer
  ?? (nativeRendererRuntimeState.renderer = Reconciler(hostConfig as never) as unknown as NativeRendererLike);
if (nativeRuntimeStateGlobal.__nativeFastRefreshEnabled === true &&
  nativeRendererRuntimeState.rendererInjectedIntoDevTools !== true) {
  NativeRenderer.injectIntoDevTools?.();
  nativeRendererRuntimeState.rendererInjectedIntoDevTools = true;
}
const root = nativeRendererRuntimeState.root
  ?? (nativeRendererRuntimeState.root = NativeRenderer.createContainer(
    container,
    LegacyRoot,
    null,
    false,
    null,
    "",
    (error: unknown) => console.error(error),
    (error: unknown) => console.error(error),
    (error: unknown) => console.error(error),
    null,
  ));

function NativeAppRoot() {
  const AppComponent: ComponentType<{}> = nativeRendererRuntimeState.appComponent;
  return AppComponent ? <AppComponent /> : null;
}

export function useLayoutOffset(): LayoutOffset {
  return React.useContext(LayoutOffsetContext);
}

function useLayoutFrame() {
  return React.useContext(LayoutFrameContext);
}

function useAbsoluteBounds(left: number, top: number, width: number, height: number): LayoutOffset & { width: number; height: number } {
  const offset = useLayoutOffset();
  return React.useMemo(() => ({
    left: offset.left + left,
    top: offset.top + top,
    width,
    height,
  }), [height, left, offset.left, offset.top, top, width]);
}

export function Scene(props: SceneProps) {
  return (
    <LayoutFrameContext.Provider value={{ width: hostState.width, height: hostState.height }}>
      <LayoutOffsetContext.Provider value={{ left: 0, top: 0 }}>
        <SceneHost {...props} />
      </LayoutOffsetContext.Provider>
    </LayoutFrameContext.Provider>
  );
}

export function View(props: ViewProps) {
  const parentOffset = useLayoutOffset();
  const parentFrame = useLayoutFrame();
  const nodeRef = props.nodeRef;
  const children = props.children;
  const styleProp = props.style;
  const id = props.id;
  const effectiveNodeRef = useComponentNodeRef(nodeRef, id, "view");
  const style = normalizeStyle(styleProp);
  if (children === undefined || React.Children.count(children) === 0) {
    return <ViewHost id={id} nodeRef={effectiveNodeRef} style={style} />;
  }

  const layout = nativeResolveContainerLayout(
    style,
    parentFrame.width,
    parentFrame.height,
    parentOffset.left,
    parentOffset.top,
  );
  return (
    <LayoutFrameContext.Provider value={layout.contentFrame}>
      <LayoutOffsetContext.Provider value={layout.contentOffset}>
        <ViewHost id={id} nodeRef={effectiveNodeRef} style={style}>
          {children}
        </ViewHost>
      </LayoutOffsetContext.Provider>
    </LayoutFrameContext.Provider>
  );
}

export function ScrollView(props: ScrollViewProps) {
  const parentOffset = useLayoutOffset();
  const parentFrame = useLayoutFrame();
  const {
    nodeRef,
    contentContainerStyle,
    contentContainerAxis,
    children,
    ...hostProps
  } = props;
  const effectiveNodeRef = useComponentNodeRef(nodeRef, hostProps.id, "scroll");
  const style = normalizeStyle(hostProps.style);
  const layout = nativeResolveContainerLayout(
    style,
    parentFrame.width,
    parentFrame.height,
    parentOffset.left,
    parentOffset.top,
  );
  const innerWidth = layout.contentFrame.width > 0
    ? layout.contentFrame.width
    : Math.max(0, parentFrame.width - layout.padding.left - layout.padding.right);
  const innerHeight = layout.contentFrame.height > 0
    ? layout.contentFrame.height
    : Math.max(0, parentFrame.height - layout.padding.top - layout.padding.bottom);
  const contentAxis = contentContainerAxis ?? "column";
  const laidOutChildren = contentContainerStyle === undefined
    ? children
    : (
      <View
        style={[
          contentAxis === "column"
            ? {
              alignItems: "stretch",
              ...(layout.contentFrame.width > 0 ? { width: innerWidth } : {}),
            }
            : (layout.contentFrame.height > 0 ? { height: innerHeight, flexDirection: "row" } : { flexDirection: "row" }),
          contentContainerStyle,
        ]}
      >
        {children}
      </View>
    );

  return (
    <LayoutFrameContext.Provider value={layout.contentFrame}>
      <LayoutOffsetContext.Provider value={layout.contentOffset}>
        <ScrollViewHost {...hostProps} nodeRef={effectiveNodeRef} style={style}>
          {laidOutChildren}
        </ScrollViewHost>
      </LayoutOffsetContext.Provider>
    </LayoutFrameContext.Provider>
  );
}

export function Text(props: TextProps) {
  const effectiveNodeRef = useComponentNodeRef(props.nodeRef, props.id, "text");
  return <TextHost {...props} nodeRef={effectiveNodeRef} />;
}

export function Spacer(props: SpacerProps) {
  return <SpacerHost {...props} />;
}

export function Image({
  id,
  nodeRef,
  source,
  placeholderSource,
  style,
  onLoad,
  onError,
}: {
  id?: string;
  nodeRef?: NodeRef;
  source: string;
  placeholderSource?: string;
  style?: StyleProp<ImageStyle>;
  onLoad?: (source: string, detail: string) => void;
  onError?: (source: string, detail: string) => void;
}) {
  const imageRef = useComponentNodeRef(nodeRef, id, "image");
  const imageId = resolveNodeRuntimeId(imageRef) ?? id ?? "image";
  const host = useHostState(HostStateMask.Image);
  const handledEventSeq = React.useRef(host.imageEventSeq);

  React.useLayoutEffect(() => {
    if (host.imageEventSeq === handledEventSeq.current || host.imageEventId !== imageId) {
      return;
    }

    handledEventSeq.current = host.imageEventSeq;
    if (host.imageEventKind === "load" && typeof onLoad === "function") {
      onLoad(host.imageEventSource, host.imageEventDetail);
      return;
    }

    if (host.imageEventKind === "error" && typeof onError === "function") {
      onError(host.imageEventSource, host.imageEventDetail);
    }
  }, [host.imageEventDetail, host.imageEventId, host.imageEventKind, host.imageEventSeq, host.imageEventSource, imageId, onError, onLoad]);

  return (
    <ImageHost
      id={imageId}
      nodeRef={imageRef}
      source={source}
      placeholderSource={placeholderSource}
      style={normalizeStyle<ImageStyle>(style)}
    />
  );
}

export function Label({
  id,
  nodeRef,
  text,
  style,
}: {
  id?: string;
  nodeRef?: NodeRef;
  text: string;
  style?: StyleProp<TextStyle>;
}) {
  const mergedStyle = normalizeStyle<TextStyle>(style);

  return (
    <Text
      id={id}
      nodeRef={nodeRef}
      content={text}
      style={mergeDefinedStyle(mergedStyle, {
        fontSize: typeof mergedStyle.fontSize === "number" ? mergedStyle.fontSize : 18,
        color: typeof mergedStyle.color === "string" ? mergedStyle.color : "#cbd5e1",
        fontWeight: typeof mergedStyle.fontWeight === "number" ? mergedStyle.fontWeight : 400,
        wrap: mergedStyle.wrap === true,
      })}
    />
  );
}

function resolvePaneBaseStyle(
  style: Partial<LayoutStyle>,
  hoverStyle: StyleProp<LayoutStyle> | undefined,
  hoverable: boolean | undefined,
  onPress: PressHandler | undefined,
) {
  return mergeDefinedStyle(style, {
    backgroundColor: typeof style.backgroundColor === "string" ? style.backgroundColor : "#0f172a",
    borderColor: typeof style.borderColor === "string" ? style.borderColor : "#1e293b",
    borderWidth: typeof style.borderWidth === "number" ? style.borderWidth : 1,
    borderRadius: typeof style.borderRadius === "number" ? style.borderRadius : 16,
    hoverable: hoverable === true || style.hoverable === true || hoverStyle !== undefined || onPress !== undefined,
  });
}

function InteractivePane({
  id,
  paneRef,
  baseStyle,
  hoverStyle,
  onPress,
  children,
}: {
  id?: string;
  paneRef: NodeRef;
  baseStyle: Partial<LayoutStyle>;
  hoverStyle?: StyleProp<LayoutStyle>;
  onPress?: PressHandler;
  children?: ReactNode;
}) {
  const mergedHoverStyle = normalizeStyle<LayoutStyle>(hoverStyle);
  const hovered = useHover(paneRef);
  usePress(paneRef, onPress);
  const finalStyle = hovered ? mergeDefinedStyle(baseStyle, mergedHoverStyle) : baseStyle;

  return (
    <View
      id={id}
      nodeRef={paneRef}
      style={finalStyle}
    >
      {children}
    </View>
  );
}

export function Pane({
  id,
  nodeRef,
  style,
  hoverStyle,
  hoverable,
  onPress,
  children,
}: PaneProps) {
  const paneRef = useComponentNodeRef(nodeRef, id, "pane");
  const mergedStyle = normalizeStyle<LayoutStyle>(style);
  const baseStyle = resolvePaneBaseStyle(mergedStyle, hoverStyle, hoverable, onPress);
  if (hoverStyle !== undefined || onPress !== undefined) {
    return (
      <InteractivePane
        id={id}
        paneRef={paneRef}
        baseStyle={baseStyle}
        hoverStyle={hoverStyle}
        onPress={onPress}
      >
        {children}
      </InteractivePane>
    );
  }

  return (
    <View
      id={id}
      nodeRef={paneRef}
      style={baseStyle}
    >
      {children}
    </View>
  );
}


export function HostPanel({
  nodeRef,
  style,
  onBoundsChange,
  ...props
}: HostPanelProps) {
  const resolvedStyle = normalizeStyle<LayoutStyle>(style);
  const resolvedLeft = numericStyleValue(resolvedStyle.left);
  const resolvedTop = numericStyleValue(resolvedStyle.top);
  const resolvedWidth = numericStyleValue(resolvedStyle.width);
  const resolvedHeight = numericStyleValue(resolvedStyle.height);
  const bounds = useAbsoluteBounds(resolvedLeft, resolvedTop, resolvedWidth, resolvedHeight);

  React.useLayoutEffect(() => {
    onBoundsChange?.(bounds.left, bounds.top, bounds.width, bounds.height);
  }, [bounds.height, bounds.left, bounds.top, bounds.width, onBoundsChange]);

  React.useEffect(() => {
    return () => onBoundsChange?.(0, 0, 0, 0);
  }, [onBoundsChange]);

  return <Pane {...props} nodeRef={nodeRef} style={resolvedStyle} />;
}

export function Divider({
  id,
  left = 0,
  top = 0,
  length,
  thickness = 1,
  color = "#334155",
  vertical = true,
}: {
  id?: string;
  left?: number;
  top?: number;
  length: number;
  thickness?: number;
  color?: string;
  vertical?: boolean;
}) {
  return (
    <View
      id={id}
      style={{
        left,
        top,
        width: vertical ? thickness : length,
        height: vertical ? length : thickness,
        backgroundColor: color,
      }}
    />
  );
}

function resolveButtonSizing(
  label: string,
  style: StyleProp<ButtonStyle>,
  labelStyle: StyleProp<ButtonLabelStyle>,
  stretchWidth = 0,
  stretchHeight = 0,
) {
  const resolvedStyle = normalizeStyle<ButtonStyle>(style);
  const resolvedLabelStyle = normalizeStyle<ButtonLabelStyle>(labelStyle);
  const fontSize = typeof resolvedLabelStyle.fontSize === "number" ? resolvedLabelStyle.fontSize : 14;
  const fontWeight = typeof resolvedLabelStyle.fontWeight === "number" ? resolvedLabelStyle.fontWeight : 700;
  const fontFamily = typeof resolvedLabelStyle.fontFamily === "string" ? resolvedLabelStyle.fontFamily : undefined;
  const minWidth = typeof resolvedStyle.minWidth === "number" ? resolvedStyle.minWidth : undefined;
  const maxWidth = typeof resolvedStyle.maxWidth === "number" ? resolvedStyle.maxWidth : undefined;
  const minHeight = typeof resolvedStyle.minHeight === "number" ? resolvedStyle.minHeight : undefined;
  const maxHeight = typeof resolvedStyle.maxHeight === "number" ? resolvedStyle.maxHeight : undefined;
  const labelHeight = Math.max(18, Math.ceil(fontSize + 4));
  const measuredWidth = measureTextWidth(label, { fontSize, fontFamily, fontWeight });
  const resolvedWidth = typeof resolvedStyle.width === "number"
    ? resolvedStyle.width
    : clampMeasuredSize(stretchWidth > 0 ? stretchWidth : measuredWidth + 36, minWidth, maxWidth);
  const resolvedHeight = typeof resolvedStyle.height === "number"
    ? resolvedStyle.height
    : clampMeasuredSize(stretchHeight > 0 ? stretchHeight : Math.max(40, labelHeight + 16), minHeight, maxHeight);
  return {
    resolvedStyle,
    resolvedLabelStyle,
    resolvedWidth,
    resolvedHeight,
    resolvedFontSize: fontSize,
    resolvedLabelHeight: labelHeight,
  };
}

export function Button({
  id,
  nodeRef,
  title,
  active = false,
  style,
  hoverStyle,
  titleStyle,
  onPress,
}: ButtonProps) {
  const buttonRef = useComponentNodeRef(nodeRef, id, "button");
  const hovered = useHover(buttonRef);
  usePress(buttonRef, onPress);
  const {
    resolvedStyle,
    resolvedLabelStyle: baseLabelStyle,
    resolvedWidth,
    resolvedHeight,
    resolvedFontSize,
    resolvedLabelHeight,
  } = resolveButtonSizing(title, style, titleStyle);
  const basePaneStyle = mergeDefinedStyle(resolvedStyle, {
    width: resolvedWidth,
    height: resolvedHeight,
    borderRadius: typeof resolvedStyle.borderRadius === "number" ? resolvedStyle.borderRadius : 10,
    hoverable: true,
  });
  const interactiveHoverStyle = normalizeStyle<ButtonStyle>(hoverStyle);
  const baseBackgroundColor = basePaneStyle.backgroundColor ?? (active ? "#2563eb" : "#374151");
  const nextHoverBackgroundColor = interactiveHoverStyle.backgroundColor ?? (active ? baseBackgroundColor : "#475569");
  const nextBorderColor = basePaneStyle.borderColor ?? baseBackgroundColor;
  const nextHoverBorderColor = interactiveHoverStyle.borderColor ?? nextHoverBackgroundColor;
  const baseBorderWidth = typeof basePaneStyle.borderWidth === "number" ? basePaneStyle.borderWidth : 0;
  const nextHoverBorderWidth = typeof interactiveHoverStyle.borderWidth === "number" ? interactiveHoverStyle.borderWidth : baseBorderWidth;
  const resolvedLabelStyle = mergeDefinedStyle(baseLabelStyle, {
    fontSize: resolvedFontSize,
    color: typeof baseLabelStyle.color === "string" ? baseLabelStyle.color : "#f8fafc",
    fontWeight: typeof baseLabelStyle.fontWeight === "number" ? baseLabelStyle.fontWeight : 700,
  });
  const hoverLabelStyle = normalizeStyle<ButtonLabelStyle>(hoverStyle as StyleProp<ButtonLabelStyle>);
  const nextForegroundColor = hovered
    ? (typeof hoverLabelStyle.color === "string" ? hoverLabelStyle.color : resolvedLabelStyle.color)
    : resolvedLabelStyle.color;
  const centered = resolvedLabelStyle.textAlign === "center";
  const labelLeft = centered ? 0 : 18;
  const labelWidth = centered ? resolvedWidth : Math.max(0, resolvedWidth - 24);
  const labelTop = Math.max(0, Math.floor((resolvedHeight - resolvedLabelHeight) / 2));
  const finalPaneStyle = mergeDefinedStyle(basePaneStyle, hovered ? interactiveHoverStyle : {});
  return (
    <Pane
      id={id}
      nodeRef={buttonRef}
      style={mergeDefinedStyle(finalPaneStyle, {
        backgroundColor: hovered ? nextHoverBackgroundColor : baseBackgroundColor,
        borderColor: hovered ? nextHoverBorderColor : nextBorderColor,
        borderWidth: hovered ? nextHoverBorderWidth : baseBorderWidth,
      })}
    >
      <Label
        text={title}
        style={mergeDefinedStyle(resolvedLabelStyle, {
          left: labelLeft,
          top: labelTop,
          width: labelWidth,
          height: resolvedLabelHeight,
          color: nextForegroundColor,
        })}
      />
    </Pane>
  );
}


export function TextInput({
  id: idProp,
  nodeRef,
  value = "",
  placeholder = "",
  style,
  onChangeText,
  onSubmit,
}: TextInputProps) {
  const host = useHostState(HostStateMask.TextInput);
  const inputRef = useComponentNodeRef(nodeRef, idProp, "input");
  const id = resolveNodeRuntimeId(inputRef) ?? idProp ?? "input";
  const handledEventSeq = React.useRef(host.textInputEventSeq);
  const [renderValue, setRenderValue] = React.useState(value);
  const pendingHostValue = React.useRef<string | null>(null);
  const lastPropValue = React.useRef(value);
  const hasIncomingHostChange = host.textInputEventId === id && host.textInputEventKind === "change";
  const effectiveValue = hasIncomingHostChange ? host.textInputEventValue : renderValue;

  React.useLayoutEffect(() => {
    const previousPropValue = lastPropValue.current;
    lastPropValue.current = value;

    if (pendingHostValue.current !== null) {
      if (value === pendingHostValue.current) {
        pendingHostValue.current = null;
        setRenderValue(value);
        return;
      }

      if (value === previousPropValue) {
        return;
      }

      pendingHostValue.current = null;
    }

    setRenderValue(value);
  }, [value]);

  React.useLayoutEffect(() => {
    if (host.textInputEventSeq === handledEventSeq.current || host.textInputEventId !== id) {
      return;
    }

    handledEventSeq.current = host.textInputEventSeq;
    if (host.textInputEventKind === "change") {
      pendingHostValue.current = host.textInputEventValue;
      setRenderValue(host.textInputEventValue);
    }

    if (host.textInputEventKind === "change" && typeof onChangeText === "function") {
      onChangeText(host.textInputEventValue);
      return;
    }

    if (host.textInputEventKind === "submit" && typeof onSubmit === "function") {
      onSubmit(host.textInputEventValue);
    }
  }, [host.textInputEventId, host.textInputEventKind, host.textInputEventSeq, host.textInputEventValue, id, onChangeText, onSubmit]);

  return (
    <TextInputHost
      id={id}
      nodeRef={inputRef}
      value={effectiveValue}
      placeholder={placeholder}
      style={mergeDefinedStyle(normalizeStyle<TextInputStyle>(style), {
        height: typeof normalizeStyle<TextInputStyle>(style).height === "number"
          ? normalizeStyle<TextInputStyle>(style).height
          : 40,
      })}
    />
  );
}

function useHostStateRevision(mask: HostStateMaskValue) {
  return React.useSyncExternalStore(
    React.useCallback((notify: () => void) => subscribeHostState(mask, notify), [mask]),
    () => hostStateRevision.value,
    () => hostStateRevision.value,
  );
}

export function useHostState(mask: HostStateMaskValue = HostStateMask.All): HostState {
  useHostStateRevision(mask);
  return hostState;
}

type Listener = () => void;

type FieldEntry<V> = {
  subscribe: (notify: Listener) => () => void;
  getSnapshot: () => V;
};

type FieldsEntry<S> = {
  subscribe: (notify: Listener) => () => void;
  getSnapshot: () => S;
};

export interface Store<T extends Record<PropertyKey, unknown>> {
  getField<K extends keyof T>(key: K): T[K];
  setField<K extends keyof T>(key: K, value: T[K]): boolean;
  batch(update: () => void): void;

  subscribeField<K extends keyof T>(key: K, notify: Listener): () => void;
  subscribeFields<K extends keyof T>(
    keys: readonly K[],
    notify: Listener,
  ): () => void;

  useField<K extends keyof T>(key: K): T[K];
  useFields<K extends readonly (keyof T)[]>(
    keys: K,
  ): Pick<T, K[number]>;
}

export function createStore<T extends Record<PropertyKey, unknown>>(
  initialState: T,
): Store<T> {
  const state = { ...initialState } as T;

  const listeners = new Map<keyof T, Set<Listener>>();

  let pendingListeners = new Set<Listener>();
  let flushingListeners = new Set<Listener>();

  let batchDepth = 0;

  /**
   * field version
   *
   * useFields の snapshot 差分判定で Object.is を毎回全 field に対して
   * 実行しなくて済むように、field ごとの version を持つ。
   */
  const versions = new Map<keyof T, number>();

  /**
   * useField 用 cache
   *
   * key ごとに stable subscribe/getSnapshot を 1 回だけ作る。
   */
  const fieldEntries = new Map<keyof T, FieldEntry<unknown>>();

  /**
   * useFields 用 cache
   *
   * key-set ごとに stable subscribe/getSnapshot/snapshot cache を持つ。
   */
  const fieldsEntries = new Map<any, FieldsEntry<unknown>>();

  /**
   * PropertyKey -> internal id
   *
   * useFields(["a", "b"]) のような key array から cache key を作るために使う。
   */
  const keyIds = new Map<PropertyKey, string>();
  let nextKeyId = 0;

  function getVersion(key: keyof T): number {
    return versions.get(key) ?? 0;
  }

  function bumpVersion(key: keyof T): void {
    versions.set(key, getVersion(key) + 1);
  }

  function getOrCreateListeners(key: keyof T): Set<Listener> {
    let set = listeners.get(key);

    if (set === undefined) {
      set = new Set<Listener>();
      listeners.set(key, set);
    }

    return set;
  }

  function queueNotify(key: keyof T): void {
    const set = listeners.get(key);

    if (set === undefined) {
      return;
    }

    if (batchDepth > 0) {
      for (const listener of set) {
        pendingListeners.add(listener);
      }
      return;
    }

    for (const listener of set) {
      listener();
    }
  }

  function flushPendingListeners(): void {
    if (pendingListeners.size === 0) {
      return;
    }

    const toNotify = pendingListeners;

    pendingListeners = flushingListeners;
    flushingListeners = toNotify;

    pendingListeners.clear();

    try {
      for (const listener of toNotify) {
        listener();
      }
    } finally {
      toNotify.clear();
    }
  }
  function createFieldEntry<K extends keyof T>(key: K): FieldEntry<T[K]> {
    return {
      subscribe: (notify) => store.subscribeField(key, notify),
      getSnapshot: () => state[key],
    };
  }
  function createFieldsEntry(keys): FieldsEntry<Pick<T, typeof keys[number]>> {
    type K = typeof keys[number];
    type Snapshot = Pick<T, K>;
    let snapshot: Snapshot | null = null;
    let snapshotVersions: number[] | null = null;

    return {
      subscribe: (notify) => store.subscribeFields(keys, notify),

      getSnapshot: () => {
        if (snapshot === null || snapshotVersions === null) {
          const next = {} as Snapshot;
          const nextVersions = new Array<number>(keys.length);

          for (let i = 0; i < keys.length; i++) {
            const key = keys[i];

            next[key] = state[key] as T[K];
            nextVersions[i] = getVersion(key);
          }

          snapshot = next;
          snapshotVersions = nextVersions;

          return next;
        }

        for (let i = 0; i < keys.length; i++) {
          if (snapshotVersions[i] !== getVersion(keys[i])) {
            const next = {} as Snapshot;
            const nextVersions = new Array<number>(keys.length);

            for (let j = 0; j < keys.length; j++) {
              const key = keys[j];

              next[key] = state[key] as T[K];
              nextVersions[j] = getVersion(key);
            }

            snapshot = next;
            snapshotVersions = nextVersions;

            return next;
          }
        }

        return snapshot;
      },
    };
  }
  const store: Store<T> = {
    getField(key) {
      return state[key];
    },

    setField(key, value) {
      if (Object.is(state[key], value)) {
        return false;
      }

      state[key] = value;
      bumpVersion(key);
      queueNotify(key);

      return true;
    },

    batch(update) {
      batchDepth++;

      try {
        update();
      } finally {
        batchDepth--;

        if (batchDepth === 0) {
          flushPendingListeners();
        }
      }
    },

    subscribeField(key, notify) {
      const set = getOrCreateListeners(key);

      set.add(notify);

      return () => {
        set.delete(notify);

        if (set.size === 0) {
          listeners.delete(key);
        }
      };
    },

    subscribeFields(keys, notify) {
      const uniqueKeys: (keyof T)[] = [];

      outer: for (const key of keys) {
        for (let i = 0; i < uniqueKeys.length; i++) {
          if (uniqueKeys[i] === key) continue outer;
        }
        uniqueKeys.push(key);
      }

      for (const key of uniqueKeys) {
        getOrCreateListeners(key).add(notify);
      }

      return () => {
        for (const key of uniqueKeys) {
          const set = listeners.get(key);
          if (set === undefined) continue;

          set.delete(notify);

          if (set.size === 0) {
            listeners.delete(key);
          }
        }
      };
    },

    useField(key) {
      let entry = fieldEntries.get(key) as FieldEntry<T[typeof key]> | undefined;

      if (entry === undefined) {
        entry = createFieldEntry(key);

        fieldEntries.set(key, entry as FieldEntry<unknown>);
      }

      return React.useSyncExternalStore(
        entry.subscribe,
        entry.getSnapshot,
      );
    },

    useFields(keys) {
      type K = typeof keys[number];
      type Snapshot = Pick<T, K>;

      let entry = fieldsEntries.get(keys) as FieldsEntry<Snapshot> | undefined;

      if (entry === undefined) {

        entry = createFieldsEntry(keys);

        fieldsEntries.set(keys, entry as FieldsEntry<unknown>);
      }

      return React.useSyncExternalStore(
        entry.subscribe,
        entry.getSnapshot,
      );
    },
  };

  return store;
}

export function createHoverPressTracker(target: NodeTarget): (host: HostState) => boolean {
  let lastPointerDownSeq = 0;

  return function wasPressed(host) {
    if (host.pointerDownSeq === lastPointerDownSeq) {
      return false;
    }

    lastPointerDownSeq = host.pointerDownSeq;
    return isHovered(target, host.hoveredId);
  };
}

export function isHovered(target: NodeTarget, hoveredId = hostState.hoveredId): boolean {
  return isHoveredTarget(target, hoveredId);
}

export function useHover(target: NodeTarget): boolean {
  const runtimeId = resolveNodeRuntimeId(target) ?? "";
  return React.useSyncExternalStore(
    React.useCallback((notify: () => void) => subscribeHoverState(runtimeId, notify), [runtimeId]),
    () => isHoveredRuntimeId(runtimeId),
    () => isHoveredRuntimeId(runtimeId),
  );
}

export function usePress(target: NodeTarget, onPress?: PressHandler): boolean {
  useHostStateRevision(HostStateMask.PointerPress);
  const hovered = useHover(target);
  const pointerDownSeq = hostState.pointerDownSeq;
  const lastPointerDownSeq = React.useRef(pointerDownSeq);
  const pressed = pointerDownSeq !== 0 &&
    pointerDownSeq !== lastPointerDownSeq.current &&
    hovered;

  React.useLayoutEffect(() => {
    if (pointerDownSeq === 0 || pointerDownSeq === lastPointerDownSeq.current) {
      return;
    }

    const wasPressed = hovered;
    lastPointerDownSeq.current = pointerDownSeq;
    if (wasPressed) {
      onPress?.();
    }
  }, [hovered, onPress, pointerDownSeq]);

  return pressed;
}

const animationClaimCount = nativeHostStoreState.animationClaimCount;
const shaderAnimationClaimCount = nativeHostStoreState.shaderAnimationClaimCount;

function updateAnimationState() {
  const enabled = animationClaimCount.value > 0;
  if (hostState.animationEnabled === enabled) {
    return;
  }

  hostState.animationEnabled = enabled;
  nativeSetAnimationEnabled(enabled);

}

function updateShaderAnimationState() {
  const enabled = shaderAnimationClaimCount.value > 0;
  nativeSetShaderAnimationEnabled(enabled);
}

export function useAnimationLoop(enabled = true): void {
  React.useEffect(() => {
    if (!enabled) {
      return;
    }

    animationClaimCount.value += 1;
    updateAnimationState();
    return () => {
      animationClaimCount.value = Math.max(0, animationClaimCount.value - 1);
      updateAnimationState();
    };
  }, [enabled]);
}

export function useShaderAnimation(enabled = true): void {
  React.useEffect(() => {
    if (!enabled) {
      return;
    }

    shaderAnimationClaimCount.value += 1;
    updateShaderAnimationState();
    return () => {
      shaderAnimationClaimCount.value = Math.max(0, shaderAnimationClaimCount.value - 1);
      updateShaderAnimationState();
    };
  }, [enabled]);
}
export function mountNativeApp(AppComponent: ComponentType): void {
  let loggedRenderCalls = 0;
  let loggedFrameCallbacks = 0;
  let loggedPointerCalls = 0;
  const fastRefreshReevaluation = nativeRuntimeStateGlobal.__nativeFastRefreshEnabled === true &&
    nativeRendererRuntimeState.appMounted === true;
  nativeRendererRuntimeState.appComponent = AppComponent;

  function renderApp() {
    syncHostState();
    if (isDebugEnabled() && loggedRenderCalls < 5) {
      hostLog(`renderApp frame=${hostState.frame} mouse=(${hostState.mouseX},${hostState.mouseY})`);
    }

    NativeRenderer.updateContainerSync(<NativeAppRoot />, root, null, null);
    NativeRenderer.flushSyncWork();
    nativeRendererRuntimeState.appMounted = true;
    loggedRenderCalls += 1;
  }

  globalThis.__nativeRenderFrame = function renderFrame(elapsedMs: number, frame: number) {
    let changedMask = syncHostState();
    if (isDebugEnabled() && loggedFrameCallbacks < 5) {
      hostLog(`__nativeRenderFrame elapsed=${elapsedMs} frame=${frame}`);
    }
    if (hostState.elapsedMs !== elapsedMs) {
      hostState.elapsedMs = elapsedMs;
      changedMask |= HostStateMask.Animation;
    }
    if (hostState.frame !== frame) {
      hostState.frame = frame;
      changedMask |= HostStateMask.Animation;
    }
    publishHostState(changedMask);
    loggedFrameCallbacks += 1;
  };

  globalThis.__nativePointerMove = function pointerMove(x: number, y: number, buttons: number, synthetic: boolean) {
    if (isDebugEnabled() && loggedPointerCalls < 8) {
      hostLog(`pointerMove x=${x} y=${y} buttons=${buttons} synthetic=${synthetic}`);
    }
    let changedMask = syncHostState();
    if (hostState.mouseX !== x || hostState.mouseY !== y || hostState.mouseButtons !== buttons || hostState.lastInputSynthetic !== synthetic) {
      hostState.mouseX = x;
      hostState.mouseY = y;
      hostState.mouseButtons = buttons;
      hostState.lastInputSynthetic = synthetic;
      changedMask |= HostStateMask.PointerPosition;
    }
    publishHostState(changedMask);
    loggedPointerCalls += 1;
  };

  globalThis.__nativePointerDown = function pointerDown(button: number, buttons: number, synthetic: boolean) {
    if (isDebugEnabled() && loggedPointerCalls < 8) {
      hostLog(`pointerDown button=${button} buttons=${buttons} synthetic=${synthetic}`);
    }
    let changedMask = syncHostState();
    if (hostState.mouseButtons !== buttons || hostState.lastInputSynthetic !== synthetic) {
      hostState.mouseButtons = buttons;
      hostState.lastInputSynthetic = synthetic;
      changedMask |= HostStateMask.PointerPosition;
    }
    hostState.pointerDownSeq += 1;
    changedMask |= HostStateMask.PointerPress;
    publishHostState(changedMask);
    loggedPointerCalls += 1;
  };

  globalThis.__nativePointerUp = function pointerUp(button: number, buttons: number, synthetic: boolean) {
    if (isDebugEnabled() && loggedPointerCalls < 8) {
      hostLog(`pointerUp button=${button} buttons=${buttons} synthetic=${synthetic}`);
    }
    let changedMask = syncHostState();
    if (hostState.mouseButtons !== buttons || hostState.lastInputSynthetic !== synthetic) {
      hostState.mouseButtons = buttons;
      hostState.lastInputSynthetic = synthetic;
      changedMask |= HostStateMask.PointerPosition;
    }
    changedMask |= HostStateMask.PointerPress;
    publishHostState(changedMask);
    loggedPointerCalls += 1;
  };

  globalThis.__nativeWheel = function wheel(deltaX: number, deltaY: number, synthetic: boolean) {
    if (isDebugEnabled() && loggedPointerCalls < 8) {
      hostLog(`wheel dx=${deltaX} dy=${deltaY} synthetic=${synthetic}`);
    }
    let changedMask = syncHostState();
    hostState.lastWheelDeltaX = deltaX;
    hostState.lastWheelDeltaY = deltaY;
    hostState.lastInputSynthetic = synthetic;
    hostState.scrollX = Math.max(0, hostState.scrollX + deltaX * 12);
    hostState.scrollY = Math.max(0, hostState.scrollY + deltaY * 12);
    changedMask |= HostStateMask.Scroll;
    publishHostState(changedMask);
    loggedPointerCalls += 1;
  };

  globalThis.__nativeKeyDown = function keyDown(key: string, modifiers: number, repeat: boolean, synthetic: boolean) {
    let changedMask = syncHostState();
    hostState.lastKey = key;
    hostState.keyModifiers = modifiers;
    hostState.keyRepeat = repeat;
    hostState.lastInputSynthetic = synthetic;
    hostState.keyDownSeq += 1;
    changedMask |= HostStateMask.Keyboard;
    publishHostState(changedMask);
  };

  globalThis.__nativeKeyUp = function keyUp(key: string, modifiers: number, synthetic: boolean) {
    let changedMask = syncHostState();
    hostState.lastKey = key;
    hostState.keyModifiers = modifiers;
    hostState.keyRepeat = false;
    hostState.lastInputSynthetic = synthetic;
    hostState.keyUpSeq += 1;
    changedMask |= HostStateMask.Keyboard;
    publishHostState(changedMask);
  };

  globalThis.__nativeTextInput = function textInput(text: string, synthetic: boolean) {
    let changedMask = syncHostState();
    hostState.lastTextInput = text;
    hostState.lastInputSynthetic = synthetic;
    hostState.textInputSeq += 1;
    changedMask |= HostStateMask.TextInput;
    publishHostState(changedMask);
  };

  globalThis.__nativeTextInputEvent = function textInputEvent(id: string, kind: string, value: string, caretIndex: number, focused: boolean) {
    let changedMask = syncHostState();
    hostState.textInputEventId = id;
    hostState.textInputEventKind = kind;
    hostState.textInputEventValue = value;
    hostState.textInputEventCaretIndex = caretIndex;
    hostState.textInputEventFocused = focused;
    hostState.textInputEventSeq += 1;
    changedMask |= HostStateMask.TextInput;
    publishHostState(changedMask);
  };

  globalThis.__nativeImageEvent = function imageEvent(id: string, kind: string, source: string, detail: string) {
    let changedMask = syncHostState();
    hostState.imageEventId = id;
    hostState.imageEventKind = kind;
    hostState.imageEventSource = source;
    hostState.imageEventDetail = detail;
    hostState.imageEventSeq += 1;
    changedMask |= HostStateMask.Image;
    publishHostState(changedMask);
  };

  hostLog("react bundle evaluated");
  if (!fastRefreshReevaluation) {
    renderApp();
  }
  publishHostState(HostStateMask.All);
}
