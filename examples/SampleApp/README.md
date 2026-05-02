# SampleApp

Native sample app for the `Enaga` runtime host.

## Layout

- `assets\`, `src\`, `scripts\` at the example root are the React sample app.
- `Enaga.SampleApp\` is the cross-platform native host.
- `Enaga.SampleApp.Core\` contains shared sample bootstrap/config logic.
- `Enaga.SampleApp.Windows\` is the Windows launcher.
- `Enaga.SampleApp.SyntaxHighlighting\` and `Enaga.SampleApp.Web\` stay grouped under the same example root.

## Stable run

Initialize dependencies once first:

```sh
cd examples/SampleApp
pnpm install
```

The default native run uses the stable bundle under `dist\react-entry.mjs`, so build it first:

```sh
cd examples/SampleApp
pnpm run build:react

dnrelay run ./Enaga.SampleApp/Enaga.SampleApp.csproj
```

## Settings file

- `sample-appsettings.json` lives at the example root and is copied to the native app output on build.
- The sample auto-loads that copied file when `--config` is not supplied.
- Relative paths in the settings file resolve from the loaded config file location.
- Prefer relative paths or `/` separators in JSON settings so the same file works across platforms.
- Use `--config ./path/to/custom-settings.json` to point the sample at another settings file.

Example settings:

```jsonc
{
  "window": {
    "width": 1440,
    "height": 900
  },
  "react": {
    "diagnostics": {
      "areas": ["configuration", "reload"],
      "file": "logs/sample-host.log"
    }
  }
}
```

## React debug mode

```sh
dnrelay run ./examples/SampleApp/Enaga.SampleApp/Enaga.SampleApp.csproj -- --react-debug
```

- keeps using the stable bundle
- does not enable Fast Refresh by itself
- enables host debug features and narrow debug logs

## Fast Refresh

```sh
cd examples/SampleApp
pnpm install
pnpm run watch:react:fast-refresh

dnrelay run ./Enaga.SampleApp/Enaga.SampleApp.csproj -- --fast-refresh
```

If you want both Fast Refresh and debug features:

```sh
dnrelay run ./examples/SampleApp/Enaga.SampleApp/Enaga.SampleApp.csproj -- --fast-refresh --react-debug
```

The sample auto-loads `sample-appsettings.json` from the app output when `--config` is not supplied.

## Window size

- Settings file: `window.width`, `window.height`
- CLI overrides: `--width 1600 --height 900`

If neither is specified, the sample starts at `1280x800`.

## Diagnostics log target

- Console logging remains the default when diagnostics are enabled.
- Use `react.diagnostics.file` in the settings file or `--host-log-file <path>` to write host diagnostics to a file instead.
- When a log file target is set, diagnostics go to that file instead of stdout.
- A relative log file path such as `logs/sample-host.log` stays portable across Windows, macOS, and Linux.
