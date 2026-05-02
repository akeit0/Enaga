declare function resetScene(backgroundColor?: string): void;

declare function configureFonts(defaultFamily?: string, fallbackFamilies?: string[]): void;

declare function registerFont(family: string, source: string): void;

declare function nativeMeasureTextHeight(text: string, width: number, style?: object): number;

declare function nativeMeasureTextWidth(text: string, style?: object): number;

declare function textInput(parentId: string, id: string, left: number, top: number, width: number, height: number, value?: string, placeholder?: string, style?: object): void;

declare function nativeHostLog(message: string): void;

declare const width: number;

declare const height: number;

declare const frameCount: number;

declare const mouseX: number;

declare const mouseY: number;

declare const mouseButtons: number;

declare const lastWheelDeltaX: number;

declare const lastWheelDeltaY: number;

declare const lastInputSynthetic: boolean;

declare const lastKey: string;

declare const keyModifiers: number;

declare const keyRepeat: boolean;

declare const lastTextInput: string;

declare const nativeDebugEnabled: boolean;
