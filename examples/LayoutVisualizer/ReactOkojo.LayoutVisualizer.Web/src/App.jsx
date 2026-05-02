import React from "react";

const surfaceStyle = {
  minHeight: "100vh",
  padding: 24,
  boxSizing: "border-box",
  background: "#0f172a",
  color: "#e2e8f0",
  fontFamily: "Segoe UI, sans-serif",
};

const panelStyle = {
  maxWidth: 980,
  margin: "0 auto",
  display: "grid",
  gap: 20,
};

const cardStyle = {
  background: "#111827",
  border: "1px solid #334155",
  borderRadius: 16,
  padding: 16,
  boxSizing: "border-box",
};

const codeStyle = {
  margin: 0,
  whiteSpace: "pre-wrap",
  fontSize: 13,
  lineHeight: 1.5,
  color: "#cbd5e1",
};

const demoSource = `<Node
  style={{
    width: 200,
    height: 250,
    padding: 10,
    flexWrap: "wrap",
    gap: 10,
  }}>
  <Node style={{ height: 50, width: 50 }} />
  <Node style={{ height: 50, width: 50 }} />
  <Node style={{ height: 50, width: 50 }} />
  <Node style={{ height: 50, width: 50 }} />
  <Node style={{ height: 50, width: 50 }} />
</Node>`;

function Node({ style, children, depth = 0 }) {
  const hue = 210 + depth * 18;
  return (
    <div
      style={{
        display: "flex",
        flexDirection: "row",
        alignItems: "flex-start",
        alignContent: "flex-start",
        position: "relative",
        boxSizing: "border-box",
        border: "1px solid rgba(148, 163, 184, 0.55)",
        borderRadius: 12,
        background: `hsla(${hue}, 70%, 55%, ${depth === 0 ? 0.16 : 0.24})`,
        boxShadow: depth === 0 ? "0 18px 40px rgba(15, 23, 42, 0.35)" : "none",
        minWidth: 0,
        minHeight: 0,
        ...style,
      }}
    >
      {React.Children.map(children, (child) =>
        React.isValidElement(child)
          ? React.cloneElement(child, { depth: depth + 1 })
          : child,
      )}
    </div>
  );
}

export function App() {
  return (
    <div style={surfaceStyle}>
      <div style={panelStyle}>
        <div style={cardStyle}>
          <h1 style={{ margin: 0, fontSize: 28 }}>Layout Visualizer</h1>
          <p style={{ margin: "8px 0 0", color: "#94a3b8" }}>
            Edit <code>src\\App.jsx</code> and save. <code>pnpm run dev</code> reloads the view immediately.
          </p>
        </div>

        <div style={{ ...cardStyle, display: "grid", gap: 16 }}>
          <Node
            style={{
              width: 200,
              height: 250,
              padding: 10,
              flexWrap: "wrap",
              gap: 10,
            }}
          >
            <Node style={{ height: 50, width: 50 }} />
            <Node style={{ height: 50, width: 50 }} />
            <Node style={{ height: 50, width: 50 }} />
            <Node style={{ height: 50, width: 50 }} />
            <Node style={{ height: 50, width: 50 }} />
          </Node>
        </div>

        <div style={cardStyle}>
          <pre style={codeStyle}>{demoSource}</pre>
        </div>
      </div>
    </div>
  );
}
