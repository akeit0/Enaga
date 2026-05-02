export const catalogTabs = [
  { id: "overview", label: "Overview", subtitle: "project summary" },
  { id: "inputs", label: "Inputs", subtitle: "text editing" },
  { id: "rendering", label: "Rendering", subtitle: "images and caching" },
  { id: "gradients", label: "Gradients", subtitle: "color ramps" },
  { id: "shaders", label: "Shaders", subtitle: "effect-style fills" },
  { id: "animation", label: "Animation", subtitle: "opt-in motion samples" },
  { id: "components", label: "Components", subtitle: "layout patterns" },
] as const;

export type CatalogTabId = (typeof catalogTabs)[number]["id"];

export const overviewBullets = [
  "React Native Web primitives render into the browser so the UI can be compared directly against the native renderer sample.",
  "The original sample is host-owned in C# with SkiaSharp rasterization and OpenGL texture presentation.",
  "This web mirror intentionally keeps the visuals close while using standard browser hosting and modern React component patterns.",
];

export const inputHints = [
  "Compare browser selection, IME, and clipboard behavior against the native host-owned input path.",
  "Double-click selection, Shift + arrows, and multiline editing now come from the browser instead of custom host logic.",
  "This page is useful for spotting which input APIs in the native sample are implementation details versus reusable UI concepts.",
];

export const renderingNotes = [
  "The scene painter now reuses a recorded picture when the commit object has not changed.",
  "Remote images load asynchronously and show loading or error states before decode completes.",
  "Cached files are evicted from the web image cache by age and file-count limits.",
];

export const componentExamples = [
  "Section cards for grouped settings or docs.",
  "Shadowed tiles and gradient surfaces for richer depth without inventing new browser primitives.",
  "Nested scroll regions and focusable inputs that still route correctly through the surrounding app shell.",
];

export const effectNotes = [
  "Gradients are now normal decorations on views and cards.",
  "Animation is opt-in through the runtime hook, so non-animated pages do not need frame-driven updates.",
  "Shader-style backgrounds can share app-side helpers without leaking into reusable runtime layers.",
];

export const gradientNotes = [
  "Gradients are ordinary scene backgrounds, so they work on panes, cards, and scroll containers.",
  "Linear and radial variants are configured from the app side without new host node types.",
  "Static gradients do not need the animation loop at all.",
];

export const shaderNotes = [
  "Runtime shaders are authored on the app side and serialized to the host as Skia runtime-effect specs in the native app.",
  "This browser mirror uses CSS-based approximations so the visual output can be compared without copying the runtime implementation.",
  "Animated shader pages opt into frame updates explicitly with the same overall product surface as the native sample.",
];

export const animationNotes = [
  "Animation is claimed from JS, so only pages that need time-based motion stay frame-driven.",
  "The host still redraws on input and reload events even when animation is disabled.",
  "Simple motion can mix ordinary panes, gradients, and shader-backed cards in the same scene tree.",
];
