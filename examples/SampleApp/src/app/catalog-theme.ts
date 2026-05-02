export const catalogColors = {
  scene: "#0b12200F",
  panel: "#111827",
  pane: "#0f172a",
  paneAlt: "#101826",
  input: "#09101d",
  border: "#1e293b",
  divider: "#334155",
  title: "#f8fafc",
  accent: "#93c5fd",
  text: "#cbd5e1",
  note: "#e2e8f0",
  hint: "#a7f3d0",
  muted: "#94a3b8",
  buttonOn: "#2563eb",
  buttonOff: "#334155",
  activeInput: "#60a5fa",
  success: "#22c55e",
  warning: "#f59e0b",
};

export function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, value));
}
