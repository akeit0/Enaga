using System.Globalization;
using System.Text.Json;

namespace Enaga.Scene;

public enum SceneGradientKind
{
    Linear,
    Radial,
}

public enum SceneRuntimeShaderUniformKind
{
    Int,
    Float,
    Color,
    FloatArray,
}

public sealed record SceneGradient(
    SceneGradientKind Kind,
    string[] Colors,
    float[]? Stops = null,
    float StartX = 0,
    float StartY = 0,
    float EndX = 1,
    float EndY = 1,
    float CenterX = 0.5f,
    float CenterY = 0.5f,
    float Radius = 0.5f
)
{
    public bool Equals(SceneGradient? other)
    {
        return other is not null
            && Kind == other.Kind
            && Colors.AsSpan().SequenceEqual(other.Colors)
            && ScenePaintEquality.ArraysEqual(Stops, other.Stops)
            && StartX == other.StartX
            && StartY == other.StartY
            && EndX == other.EndX
            && EndY == other.EndY
            && CenterX == other.CenterX
            && CenterY == other.CenterY
            && Radius == other.Radius;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        ScenePaintEquality.AddArrayHash(ref hash, Colors);
        ScenePaintEquality.AddArrayHash(ref hash, Stops);
        hash.Add(StartX);
        hash.Add(StartY);
        hash.Add(EndX);
        hash.Add(EndY);
        hash.Add(CenterX);
        hash.Add(CenterY);
        hash.Add(Radius);
        return hash.ToHashCode();
    }
}

public sealed record SceneBoxShadow(
    string? Color = null,
    float OffsetX = 0,
    float OffsetY = 8,
    float Blur = 18,
    float Spread = 0
);

public sealed record SceneRuntimeShaderUniform(
    string Name,
    SceneRuntimeShaderUniformKind Kind,
    int IntValue = 0,
    float FloatValue = 0,
    string? ColorValue = null,
    float[]? FloatArrayValue = null
)
{
    public bool Equals(SceneRuntimeShaderUniform? other)
    {
        return other is not null
            && string.Equals(Name, other.Name, StringComparison.Ordinal)
            && Kind == other.Kind
            && IntValue == other.IntValue
            && FloatValue == other.FloatValue
            && string.Equals(ColorValue, other.ColorValue, StringComparison.Ordinal)
            && ScenePaintEquality.ArraysEqual(FloatArrayValue, other.FloatArrayValue);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name, StringComparer.Ordinal);
        hash.Add(Kind);
        hash.Add(IntValue);
        hash.Add(FloatValue);
        hash.Add(ColorValue, StringComparer.Ordinal);
        ScenePaintEquality.AddArrayHash(ref hash, FloatArrayValue);
        return hash.ToHashCode();
    }
}

public sealed record SceneRuntimeShader(
    string? SourceId,
    string Source,
    bool HostTime = false,
    SceneRuntimeShaderUniform[]? Uniforms = null
)
{
    public bool Equals(SceneRuntimeShader? other)
    {
        return other is not null
            && string.Equals(SourceId, other.SourceId, StringComparison.Ordinal)
            && string.Equals(Source, other.Source, StringComparison.Ordinal)
            && HostTime == other.HostTime
            && ScenePaintEquality.ArraysEqual(Uniforms, other.Uniforms);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SourceId, StringComparer.Ordinal);
        hash.Add(Source, StringComparer.Ordinal);
        hash.Add(HostTime);
        ScenePaintEquality.AddArrayHash(ref hash, Uniforms);
        return hash.ToHashCode();
    }
}

internal static class ScenePaintEquality
{
    public static bool ArraysEqual<T>(T[]? left, T[]? right)
        where T : IEquatable<T>
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Length != right.Length)
            return false;

        return left.AsSpan().SequenceEqual(right);
    }

    public static void AddArrayHash<T>(ref HashCode hash, T[]? values)
    {
        if (values is null)
        {
            hash.Add(0);
            return;
        }

        hash.Add(values.Length);
        for (var index = 0; index < values.Length; index++)
            hash.Add(values[index]);
    }
}
