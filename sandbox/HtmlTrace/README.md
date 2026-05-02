# HtmlTrace

Small HTML renderer trace harness for `dotnet-trace`.

Run a long resize-layout loop:

```powershell
dnrelay run sandbox\HtmlTrace\HtmlTrace.csproj -c Release -- --case stress --mode resize --seconds 60 --warmup 3 --report-every 100 --skia-text
```

Wait before the loop so `dotnet-trace` can attach:

```powershell
dnrelay run sandbox\HtmlTrace\HtmlTrace.csproj -c Release -- --case iana --mode resize --seconds 60 --wait
```

Then collect in another terminal using the printed PID:

```powershell
dotnet-trace collect -p <pid> --providers Microsoft-DotNETCore-SampleProfiler,Microsoft-Windows-DotNETRuntime:0x1C000080018:5,Enaga-HtmlTrace
```

Useful switches:

- `--case iana|legacy|stress`
- `--mode cold|resize|cached|all`
- `--seconds N`
- `--warmup N`
- `--dummy-text` or `--skia-text`
- `--width N --height N`
- `--wait`
- `--report-every N`

`cold` recreates `HtmlSceneFrameSource` each iteration. `resize` reuses the source and alternates viewport widths to force relayout. `cached` measures the no-damage path.
