import type { ReactNode } from "react";

export type TextAlign = "left" | "center" | "right";
export type ImageFit = "contain" | "cover" | "fill";
export type LayoutScalar = number | string;

export type StackAxis = "row" | "column";
export type LayoutDirection = "ltr" | "rtl";
export type FlexDirection = StackAxis | "row-reverse" | "column-reverse";
export type FlexWrap = "nowrap" | "wrap";
export type Position = "absolute" | "relative" | "static";
export type BoxSizing = "border-box" | "content-box";
export type StackAlign = "start" | "center" | "end" | "stretch";
export type StackAlignSelf = "auto" | StackAlign;
export type StackJustify = "start" | "center" | "end" | "space-between" | "space-around";

export type StyleRecord = Record<string, unknown>;
export type StyleFalsy = false | null | undefined;
export type StyleProp<T extends StyleRecord> = T | readonly unknown[] | StyleFalsy;

export type LinearGradient = {
  type?: "linear";
  colors: string[];
  stops?: number[];
  startX?: number;
  startY?: number;
  endX?: number;
  endY?: number;
};

export type RadialGradient = {
  type: "radial";
  colors: string[];
  stops?: number[];
  centerX?: number;
  centerY?: number;
  radius?: number;
};

export type Gradient = LinearGradient | RadialGradient;

export type BoxShadow = {
  color?: string;
  offsetX?: number;
  offsetY?: number;
  blur?: number;
  spread?: number;
};

export type RuntimeShader = {
  sourceId?: string;
  source: string;
  uniforms?: Record<string, number | string | readonly number[]>;
  hostTime?: boolean;
};

export type LayoutStyle = {
  left?: LayoutScalar;
  top?: LayoutScalar;
  right?: LayoutScalar;
  bottom?: LayoutScalar;
  width?: LayoutScalar;
  height?: LayoutScalar;
  minWidth?: LayoutScalar;
  maxWidth?: LayoutScalar;
  minHeight?: LayoutScalar;
  maxHeight?: LayoutScalar;
  flex?: number;
  flexBasis?: LayoutScalar;
  flexGrow?: number;
  flexShrink?: number;
  alignSelf?: StackAlignSelf;
  backgroundColor?: string;
  backgroundGradient?: Gradient;
  backgroundShader?: RuntimeShader;
  shadow?: BoxShadow | readonly BoxShadow[];
  borderColor?: string;
  borderWidth?: number;
  borderRadius?: number;
  boxSizing?: BoxSizing;
  overflow?: "visible" | "hidden";
  margin?: LayoutScalar;
  marginHorizontal?: LayoutScalar;
  marginVertical?: LayoutScalar;
  marginLeft?: LayoutScalar;
  marginTop?: LayoutScalar;
  marginRight?: LayoutScalar;
  marginBottom?: LayoutScalar;
  padding?: LayoutScalar;
  paddingHorizontal?: LayoutScalar;
  paddingVertical?: LayoutScalar;
  paddingLeft?: LayoutScalar;
  paddingTop?: LayoutScalar;
  paddingRight?: LayoutScalar;
  paddingBottom?: LayoutScalar;
  direction?: LayoutDirection;
  flexDirection?: FlexDirection;
  flexWrap?: FlexWrap;
  position?: Position;
  gap?: number;
  alignItems?: StackAlign;
  justifyContent?: StackJustify;
  hoverable?: boolean;
  tooltip?: string;
};

export type ViewStyle = LayoutStyle;

export type ScrollViewStyle = LayoutStyle & {
  contentWidth?: number;
  contentHeight?: number;
  scrollX?: number;
  scrollY?: number;
};
export type ReactScrollViewStyle = ScrollViewStyle;

export type TextStyle = LayoutStyle & {
  fontSize?: number;
  color?: string;
  fontFamily?: string;
  fontWeight?: number;
  textAlign?: TextAlign;
  wrap?: boolean;
};
export type ReactTextStyle = TextStyle;

export type TextInputStyle = TextStyle & {
  padding?: number;
  paddingHorizontal?: number;
  paddingVertical?: number;
  paddingLeft?: number;
  paddingTop?: number;
  paddingRight?: number;
  paddingBottom?: number;
  multiline?: boolean;
  lineHeight?: number;
  activeBorderColor?: string;
  placeholderColor?: string;
  compositionUnderlineColor?: string;
  compositionSelectionUnderlineColor?: string;
};
export type ReactTextInputStyle = TextInputStyle;

export type ImageStyle = LayoutStyle & {
  fit?: ImageFit;
};
export type ReactImageStyle = ImageStyle;

export type StackLayoutStyle = LayoutStyle & {
  flexDirection?: FlexDirection;
  gap?: number;
  padding?: number;
  paddingHorizontal?: number;
  paddingVertical?: number;
  paddingLeft?: number;
  paddingTop?: number;
  paddingRight?: number;
  paddingBottom?: number;
  alignItems?: StackAlign;
  justifyContent?: StackJustify;
};
export type ReactStackStyle = StackLayoutStyle;

export type ButtonStyle = LayoutStyle & {
  backgroundColor?: string;
  borderColor?: string;
  borderWidth?: number;
  borderRadius?: number;
  hoverable?: boolean;
  tooltip?: string;
};

export type ButtonLabelStyle = TextStyle;

export type NodeHandle = {
  readonly runtimeId: string;
  readonly publicId?: string;
};

export type NodeRef<T extends NodeHandle = NodeHandle> = {
  readonly runtimeKey: string;
  current: T | null;
};

export type PressHandler = () => void;

export interface HostState {
  width: number;
  height: number;
  frame: number;
  elapsedMs: number;
  mouseX: number;
  mouseY: number;
  mouseButtons: number;
  pointerDownSeq: number;
  lastWheelDeltaX: number;
  lastWheelDeltaY: number;
  lastInputSynthetic: boolean;
  lastKey: string;
  keyModifiers: number;
  keyRepeat: boolean;
  keyDownSeq: number;
  keyUpSeq: number;
  lastTextInput: string;
  textInputSeq: number;
  textInputEventSeq: number;
  textInputEventId: string;
  textInputEventKind: string;
  textInputEventValue: string;
  textInputEventCaretIndex: number;
  textInputEventFocused: boolean;
  imageEventSeq: number;
  imageEventId: string;
  imageEventKind: string;
  imageEventSource: string;
  imageEventDetail: string;
  scrollX: number;
  scrollY: number;
  animationEnabled: boolean;
  hoveredId: string;
  hoverTargetLeft: number;
  hoverTargetTop: number;
  hoverTargetWidth: number;
  hoverTargetHeight: number;
}

export type IntrinsicLayoutContext = {
  axis: StackAxis;
  availableWidth: number;
  availableHeight: number;
  stretchWidth: number;
  stretchHeight: number;
};

export type IntrinsicLayoutResult = {
  width?: number;
  height?: number;
  minWidth?: number;
  maxWidth?: number;
  minHeight?: number;
  maxHeight?: number;
};

export type IntrinsicLayoutMeasure<P> = (props: Readonly<P>, context: IntrinsicLayoutContext) => IntrinsicLayoutResult | undefined;

export type LayoutDefaults<P> =
  Partial<LayoutStyle>
  | ((props: Readonly<P>) => Partial<LayoutStyle> | undefined);

export type SceneProps = {
  backgroundColor?: string;
  children?: ReactNode;
};

export type ViewProps = {
  id?: string;
  nodeRef?: NodeRef;
  style?: StyleProp<LayoutStyle>;
  children?: ReactNode;
};

export type ScrollViewProps = {
  id?: string;
  nodeRef?: NodeRef;
  style?: StyleProp<ScrollViewStyle>;
  contentContainerStyle?: StyleProp<StackLayoutStyle>;
  contentContainerAxis?: StackAxis;
  children?: ReactNode;
};

export type TextProps = {
  id?: string;
  nodeRef?: NodeRef;
  content?: string;
  style?: StyleProp<TextStyle>;
  children?: ReactNode;
};

export type TextInputHostProps = {
  id?: string;
  nodeRef?: NodeRef;
  value?: string;
  placeholder?: string;
  style?: StyleProp<TextInputStyle>;
};

export type ImageHostProps = {
  id?: string;
  nodeRef?: NodeRef;
  source: string;
  placeholderSource?: string;
  style?: StyleProp<ImageStyle>;
};

export type StackLayoutProps = {
  id?: string;
  nodeRef?: NodeRef;
  left?: number;
  top?: number;
  width?: number;
  height?: number;
  gap?: number;
  padding?: number;
  paddingHorizontal?: number;
  paddingVertical?: number;
  paddingLeft?: number;
  paddingTop?: number;
  paddingRight?: number;
  paddingBottom?: number;
  alignItems?: StackAlign;
  justifyContent?: StackJustify;
  backgroundColor?: string;
  backgroundGradient?: Gradient;
  backgroundShader?: RuntimeShader;
  shadow?: BoxShadow | readonly BoxShadow[];
  borderColor?: string;
  borderWidth?: number;
  borderRadius?: number;
  margin?: number;
  marginHorizontal?: number;
  marginVertical?: number;
  marginLeft?: number;
  marginTop?: number;
  marginRight?: number;
  marginBottom?: number;
  hoverable?: boolean;
  style?: StyleProp<StackLayoutStyle>;
  children?: ReactNode;
};

export type SpacerProps = {
  size?: number;
  flex?: number;
};

export type PaneProps = {
  id?: string;
  nodeRef?: NodeRef;
  style?: StyleProp<LayoutStyle>;
  hoverStyle?: StyleProp<LayoutStyle>;
  hoverable?: boolean;
  tooltip?: string;
  title?: string;
  onPress?: PressHandler;
  children?: ReactNode;
};

export type HostPanelProps = PaneProps & {
  onBoundsChange?: (left: number, top: number, width: number, height: number) => void;
};

export type ButtonProps = {
  id?: string;
  nodeRef?: NodeRef;
  title: string;
  active?: boolean;
  style?: StyleProp<ButtonStyle>;
  hoverStyle?: StyleProp<ButtonStyle>;
  titleStyle?: StyleProp<ButtonLabelStyle>;
  onPress?: PressHandler;
};

export type TextInputProps = {
  id?: string;
  nodeRef?: NodeRef;
  value?: string;
  placeholder?: string;
  style?: StyleProp<TextInputStyle>;
  onChangeText?: (value: string) => void;
  onSubmit?: (value: string) => void;
};
