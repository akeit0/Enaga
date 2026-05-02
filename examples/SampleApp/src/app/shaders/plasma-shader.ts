export const plasmaShaderSource = `
uniform float2 resolution;
uniform float2 origin;
uniform float time;
uniform half4 accentColor;
uniform half4 backgroundColor;

half4 main(float2 coord)
{
  float2 localCoord = coord - origin;
  float2 uv = localCoord / max(resolution, float2(1.0, 1.0));
  float wave =
    sin((uv.x + time * 0.22) * 10.0) +
    sin((uv.y - time * 0.17) * 12.0) +
    sin((uv.x + uv.y + time * 0.11) * 8.0);
  float t = 0.5 + 0.5 * sin(wave);
  return mix(backgroundColor, accentColor, half(t));
}
`;
