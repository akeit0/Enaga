import { createRuntimeShader } from "../../lib/native-runtime";
import { plasmaShaderSource } from "./plasma-shader";

const scanlineShaderSource = `
uniform float2 resolution;
uniform float2 origin;
uniform float time;
uniform half4 accentColor;
uniform half4 backgroundColor;

half4 main(float2 coord)
{
  float2 localCoord = coord - origin;
  float2 uv = localCoord / max(resolution, float2(1.0, 1.0));
  float sweep = 0.5 + 0.5 * sin((uv.x * 2.2 - time * 0.7) * 6.28318);
  float band = 0.5 + 0.5 * sin((uv.y * 14.0 - time * 2.4) * 6.28318);
  float scan = smoothstep(0.32, 0.9, band);
  float glow = 1.0 - smoothstep(0.0, 0.85, abs(uv.y - 0.5));
  float mixAmount = clamp(scan * 0.55 + glow * 0.25 + sweep * 0.2, 0.0, 1.0);
  return mix(backgroundColor, accentColor, half(mixAmount));
}
`;

const radialPulseShaderSource = `
uniform float2 resolution;
uniform float2 origin;
uniform float time;
uniform half4 accentColor;
uniform half4 backgroundColor;

half4 main(float2 coord)
{
  float2 localCoord = coord - origin;
  float2 size = max(resolution, float2(1.0, 1.0));
  float2 uv = localCoord / size - 0.5;
  uv.x *= size.x / size.y;
  float dist = length(uv);
  float ringRadius = 0.18 + 0.06 * sin(time * 1.7);
  float ring = 1.0 - smoothstep(0.0, 0.035, abs(dist - ringRadius));
  float core = 1.0 - smoothstep(0.0, 0.14, dist);
  float halo = 1.0 - smoothstep(0.08, 0.52, dist);
  float ripple = 0.5 + 0.5 * sin(dist * 20.0 - time * 4.0);
  float mixAmount = clamp(core * 0.9 + ring * 0.85 + halo * ripple * 0.35, 0.0, 1.0);
  return mix(backgroundColor, accentColor, half(mixAmount));
}
`;

export function createPlasmaShader(_time: number, accentColor = "#38bdf8", backgroundColor = "#0f172a") {
  return createRuntimeShader(plasmaShaderSource, { accentColor, backgroundColor }, { hostTime: true });
}

export function createScanlineShader(_time: number, accentColor = "#22d3ee", backgroundColor = "#111827") {
  return createRuntimeShader(scanlineShaderSource, { accentColor, backgroundColor }, { hostTime: true });
}

export function createRadialPulseShader(_time: number, accentColor = "#a78bfa", backgroundColor = "#111827") {
  return createRuntimeShader(radialPulseShaderSource, { accentColor, backgroundColor }, { hostTime: true });
}
