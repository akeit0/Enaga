import { mkdir, readFile, readdir, rm } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import * as babel from "@babel/core";
import * as esbuild from "esbuild";
import reactRefreshBabel from "react-refresh/babel";

const args = new Set(process.argv.slice(2));
const fastRefresh = args.has("--fast-refresh");
const watch = args.has("--watch");

const scriptPath = fileURLToPath(import.meta.url);
const scriptDirectory = path.dirname(scriptPath);
const projectDirectory = path.resolve(scriptDirectory, "..");
const repoRootDirectory = path.resolve(projectDirectory, "..", "..");
const sourceDirectory = path.join(projectDirectory, "src");
const reactRuntimeSourceDirectory = path.join(repoRootDirectory, "lib", "Enaga.React", "src");
const stableEntryPoint = path.join(sourceDirectory, "index.tsx");
const fastRefreshEntryPoint = path.join(sourceDirectory, "fast-refresh-entry.ts");
const stableOutputFile = path.join(projectDirectory, "dist", "react-entry.mjs");
const fastRefreshOutputDirectory = path.join(projectDirectory, "dist", "fast-refresh");
const fastRefreshEntryOutputFile = path.join(
  fastRefreshOutputDirectory,
  path.relative(repoRootDirectory, fastRefreshEntryPoint).replace(/\.[cm]?[jt]sx?$/i, ".mjs"),
);
const refreshTransformPlugin = {
  name: "sample-react-fast-refresh",
  setup(build) {
    if (!fastRefresh) {
      return;
    }

    build.onLoad({ filter: /\.[cm]?[jt]sx?$/ }, async (args) => {
      if (!isFastRefreshSourcePath(args.path)) {
        return null;
      }

      const source = await readFile(args.path, "utf8");
      const transformed = await babel.transformAsync(source, {
        filename: args.path,
        babelrc: false,
        configFile: false,
        sourceMaps: false,
        parserOpts: {
          sourceType: "module",
          plugins: ["jsx", "typescript", "importAttributes"],
        },
        plugins: [[reactRefreshBabel, { skipEnvCheck: true }]],
      });

      const moduleId = path.relative(repoRootDirectory, args.path).replaceAll("\\", "/");
      const preamble = [
        `const $RefreshReg$ = globalThis.__nativeCreateRefreshRegister?.(${JSON.stringify(moduleId)}) ?? (() => {});`,
        "const $RefreshSig$ = globalThis.__nativeCreateRefreshSignature ?? (() => ((type) => type));",
      ].join("\n");

      return {
        contents: `${preamble}\n${transformed?.code ?? source}`,
        loader: resolveEsbuildLoader(args.path),
        resolveDir: path.dirname(args.path),
      };
    });
  },
};

const commonBuildOptions = {
  absWorkingDir: projectDirectory,
  format: "esm",
  platform: "node",
  target: "es2022",
  jsxFactory: "React.createElement",
  jsxFragment: "React.Fragment",
  plugins: [refreshTransformPlugin],
  define: {
    "process.env.NODE_ENV": JSON.stringify(fastRefresh ? "development" : "production"),
  },
  logLevel: "info",
};

const buildOptions = fastRefresh
  ? {
    ...commonBuildOptions,
    entryPoints: await collectSourceEntryPoints([sourceDirectory, reactRuntimeSourceDirectory]),
    outbase: repoRootDirectory,
    outdir: fastRefreshOutputDirectory,
    outExtension: { ".js": ".mjs" },
    bundle: false,
  }
  : {
    ...commonBuildOptions,
    entryPoints: [stableEntryPoint],
    outfile: stableOutputFile,
    bundle: true,
    packages: "external",
    external: [
      "react",
      "react/*",
      "react-reconciler",
      "react-reconciler/*",
      "react-refresh",
      "react-refresh/*",
      "scheduler",
      "scheduler/*",
    ],
  };

if (fastRefresh) {
  await rm(fastRefreshOutputDirectory, { recursive: true, force: true });
  await mkdir(fastRefreshOutputDirectory, { recursive: true });
}

if (watch) {
  const context = await esbuild.context(buildOptions);
  await context.watch();
  console.log(`Watching React bundle (${fastRefresh ? "fast-refresh" : "standard"}) -> ${fastRefresh ? fastRefreshEntryOutputFile : stableOutputFile}`);
  process.on("SIGINT", async () => {
    await context.dispose();
    process.exit(0);
  });
  process.on("SIGTERM", async () => {
    await context.dispose();
    process.exit(0);
  });
  await new Promise(() => {});
} else {
  await esbuild.build(buildOptions);
}

function resolveEsbuildLoader(filePath) {
  const extension = path.extname(filePath).toLowerCase();
  return extension === ".ts" ? "ts"
    : extension === ".tsx" ? "tsx"
      : extension === ".jsx" ? "jsx"
        : "js";
}

function isFastRefreshSourcePath(filePath) {
  return filePath.startsWith(sourceDirectory) || filePath.startsWith(reactRuntimeSourceDirectory);
}

async function collectSourceEntryPoints(directories) {
  const entries = [];
  for (const directory of directories) {
    const directoryEntries = await readdir(directory, { withFileTypes: true });
    for (const entry of directoryEntries) {
      const fullPath = path.join(directory, entry.name);
      if (entry.isDirectory()) {
        entries.push(...await collectSourceEntryPoints([fullPath]));
        continue;
      }

      if (!/\.[cm]?[jt]sx?$/i.test(entry.name) || /\.d\.[cm]?[jt]s$/i.test(entry.name)) {
        continue;
      }

      entries.push(fullPath);
    }
  }

  return entries.sort((left, right) => left.localeCompare(right));
}
