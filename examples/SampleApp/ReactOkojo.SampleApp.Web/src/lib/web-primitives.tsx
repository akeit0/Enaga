import type { ComponentProps } from "react";
import {
  Image as NativeImage,
  Pressable as NativePressable,
  ScrollView as NativeScrollView,
  Text as NativeText,
  TextInput as NativeTextInput,
  View as NativeView,
} from "react-native";

type ClassNameProp = {
  className?: string;
};

export function View(props: ComponentProps<typeof NativeView> & ClassNameProp) {
  return <NativeView {...props} />;
}

export function Text(props: ComponentProps<typeof NativeText> & ClassNameProp) {
  return <NativeText {...props} />;
}

export function Pressable(props: ComponentProps<typeof NativePressable> & ClassNameProp) {
  return <NativePressable {...props} />;
}

export function ScrollView(props: ComponentProps<typeof NativeScrollView> & ClassNameProp) {
  return <NativeScrollView {...props} />;
}

export function Image(props: ComponentProps<typeof NativeImage> & ClassNameProp) {
  return <NativeImage {...props} />;
}

export function TextInput(props: ComponentProps<typeof NativeTextInput> & ClassNameProp) {
  return <NativeTextInput {...props} />;
}

