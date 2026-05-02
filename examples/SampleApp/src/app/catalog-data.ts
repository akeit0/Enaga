import type { CatalogTabDefinition } from "./catalog-types";

export const catalogTabs: CatalogTabDefinition[] = [
  { id: "overview", label: "Overview", subtitle: "project summary" },
  { id: "minimum", label: "Minimum", subtitle: "tiny React basics" },
  { id: "inputs", label: "Inputs", subtitle: "native text editing" },
  { id: "rendering", label: "Rendering", subtitle: "images and caching" },
  { id: "gradients", label: "Gradients", subtitle: "native color ramps" },
  { id: "shaders", label: "Shaders", subtitle: "runtime effect fills" },
  { id: "animation", label: "Animation", subtitle: "opt-in motion samples" },
  { id: "components", label: "Components", subtitle: "layout patterns" },
  { id: "communication", label: "Communication", subtitle: "C# core <-> JS" },
];

export const overviewBullets = [
  "React renders into a native scene graph instead of the browser DOM.",
  "The window path is host-owned in C# with SkiaSharp rasterization and Vulkan/OpenGL texture presentation.",
  "Text input, selection, repeat, clipboard, and most interaction stay on the native side.",
];

export const inputHints = [
  "Try Shift + arrows or Home/End to extend selection, including line-aware Up/Down movement in multiline fields.",
  "Double-click a word to select it.",
  "Ctrl + C / X / V route through the native clipboard service, and Tab / Shift+Tab now step focus between native inputs.",
];

export const renderingNotes = [
    "The scene painter now reuses a recorded picture when the commit object has not changed.",
    "Remote and local jpg/png/svg images all flow through the same native image path, including file:// local URIs.",
    "Cached files are evicted from the web image cache by age and file-count limits.",
];

export const componentExamples = [
  "Section cards for grouped settings or docs.",
  "Shadowed tiles and gradient surfaces for richer depth without new host node types.",
  "Nested scroll regions and focusable inputs that still route correctly through the native host.",
];

export const effectNotes = [
  "Gradients are now native scene decorations on normal views and cards.",
  "Animation is opt-in through the runtime hook, so non-animated pages do not need frame-driven updates.",
  "Skia runtime-effect shaders can now render directly into scene backgrounds with reusable app-side shader helpers.",
];

export const gradientNotes = [
  "Gradients are ordinary scene backgrounds, so they work on panes, cards, and scroll containers.",
  "Linear and radial variants are configured from the app side without new host node types.",
  "Static gradients do not need the animation loop at all.",
];

export const shaderNotes = [
  "Runtime shaders are authored on the app side and serialized to the host as Skia runtime-effect specs.",
  "Shader helpers can share color/default logic without leaking app-specific code into the reusable runtime layer.",
  "Animated shader pages opt into frame updates explicitly with useAnimationLoop(true).",
];

export const animationNotes = [
  "Animation is claimed from JS, so only pages that need time-based motion stay frame-driven.",
  "The host still redraws on input and reload events even when animation is disabled.",
  "Simple motion can mix ordinary panes, gradients, and shader-backed cards in the same scene tree.",
];
