import { mkdir } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import * as esbuild from "esbuild";

const scriptPath = fileURLToPath(import.meta.url);
const projectDirectory = path.resolve(path.dirname(scriptPath), "..");
const sourceDirectory = path.join(projectDirectory, "src");
const outputDirectory = path.join(projectDirectory, "dist");

await mkdir(outputDirectory, { recursive: true });
await esbuild.build({
  absWorkingDir: projectDirectory,
  entryPoints: [path.join(sourceDirectory, "index.tsx")],
  outfile: path.join(outputDirectory, "react-entry.mjs"),
  bundle: true,
  format: "esm",
  platform: "node",
  target: "es2022",
  jsxFactory: "React.createElement",
  jsxFragment: "React.Fragment",
  packages: "external",
  external: [
    "react",
    "react/*",
    "react-reconciler",
    "react-reconciler/*",
    "scheduler",
    "scheduler/*",
  ],
  define: {
    "process.env.NODE_ENV": JSON.stringify("production"),
  },
  logLevel: "info",
});
