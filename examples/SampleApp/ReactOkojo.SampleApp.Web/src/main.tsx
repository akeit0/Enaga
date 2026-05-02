import { StrictMode } from "react";
import { AppRegistry } from "react-native";
import "./index.css";
import App from "./App.tsx";

const appName = "App";
const rootTag = document.getElementById("root");

if (rootTag === null) {
  throw new Error("Missing #root mount node.");
}

AppRegistry.registerComponent(appName, () => function RootApp() {
  return (
    <StrictMode>
      <App />
    </StrictMode>
  );
});

AppRegistry.runApplication(appName, { rootTag });
