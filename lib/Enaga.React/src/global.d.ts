import type { HostNode, HostTextNode, NativeResolvedContainerLayout } from "./native-runtime-types";

declare global {
  interface NativeLayoutOffset {
    left: number;
    top: number;
  }

  interface NativeLayoutSize {
    width: number;
    height: number;
  }

  interface NativeLayoutInsets {
    left: number;
    top: number;
    right: number;
    bottom: number;
  }

  interface NativeResolvedContainerLayout {
    padding: NativeLayoutInsets;
    contentFrame: NativeLayoutSize;
    contentOffset: NativeLayoutOffset;
  }

  interface NativeHostGlobals {
    setAnimationEnabled(enabled: boolean): void;
    setShaderAnimationEnabled(enabled: boolean): void;
    configureFonts(defaultFamily?: string, fallbackFamilies?: string[]): void;
    registerFont(family: string, source: string): void;
    nativeCreateHostNode(type: string, runtimeId: string, publicId: string | undefined, props: object): HostNode;
    nativeCreateTextNode(runtimeId: string, text: string): HostTextNode;
    nativeMarkFullSceneFlush(): void;
    nativeResetAfterCommit(rootChildren: unknown, backgroundColor?: string): void;
    nativeHasLayoutAffectingHostPropChange(oldProps: object, newProps: object): boolean;
    nativeCommitHostUpdate(instance: unknown, props: object, publicId: string | undefined, layoutAffected: boolean): void;
    nativeCommitTextUpdate(textInstance: unknown, oldText: string, newText: string): void;
    nativeSetNodeHidden(instance: unknown, hidden: boolean): void;
    nativeAppendChild(parent: unknown, child: unknown): boolean;
    nativeInsertChildBefore(parent: unknown, child: unknown, beforeChild: unknown): boolean;
    nativeRemoveChild(parent: unknown, child: unknown): boolean;
    nativeClearChildren(parent: unknown): void;
    nativeGetParentRuntimeId(runtimeId: string): string | undefined;
    nativeResolveContainerLayout(
      style: object,
      parentWidth: number,
      parentHeight: number,
      parentOffsetLeft: number,
      parentOffsetTop: number,
    ): NativeResolvedContainerLayout;
    nativeMeasureTextHeight(text: string, width: number, style?: object): number;
    nativeMeasureTextWidth(text: string, style?: object): number;
    nativeHostLog(message: string): void;
  }

  interface GlobalThis {
    nativeDebugEnabled?: boolean;
    width?: number;
    height?: number;
    hoveredId?: string;
    nativeHostLog?: (message: string) => void;
  }
}

export {};
