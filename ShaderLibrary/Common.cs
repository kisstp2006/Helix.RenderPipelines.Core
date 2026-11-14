using HelixEngine.Shaders;
using static HelixEngine.Shaders.ShaderMath;

namespace HelixEngine.Rendering.ShaderLibrary;

/// <summary>Math helpers of the pipelines' shaders.</summary>
public static class Common
{
    public const float PI = 3.14159265358979323846f;
    public const float TWO_PI = 6.28318530717958647693f;
    public const float HALF_PI = 1.57079632679489661923f;
    public const float INV_PI = 0.31830988618379067154f;
    public const float FLT_EPS = 5.960464478e-8f;
    public const float FLT_MIN = 1.175494351e-38f;
    public const float HALF_MAX = 65504.0f;

    public static float Sq(float x) => x * x;
    public static float3 Sq(float3 x) => x * x;
    public static float Pow4(float x) => x * x * (x * x);

    public static float Max3(float a, float b, float c) => Max(Max(a, b), c);
    public static float Min3(float a, float b, float c) => Min(Min(a, b), c);
    public static float3 Max3(float3 a, float3 b, float3 c) => Max(Max(a, b), c);
    public static float3 Min3(float3 a, float3 b, float3 c) => Min(Min(a, b), c);

    /// <summary>pow for a base that may be negative or zero (its absolute value, at least FLT_EPS).</summary>
    public static float PositivePow(float b, float e) => Pow(Max(Abs(b), FLT_EPS), e);
    public static float3 PositivePow(float3 b, float3 e) => Pow(Max(Abs(b), new float3(FLT_EPS)), e);

    /// <summary>Normalized, or zero for a zero vector (no NaN).</summary>
    public static float3 SafeNormalize(float3 v)
    {
        float lengthSq = Max(FLT_MIN, Dot(v, v));
        return v * Rsqrt(lengthSq);
    }

    public static float SafeDiv(float numer, float denom) => numer != denom ? numer / denom : 1.0f;

    /// <summary>A value from one range in another, clamped.</summary>
    public static float Remap(float value, float fromMin, float fromMax, float toMin, float toMax) =>
        toMin + Saturate((value - fromMin) / (fromMax - fromMin)) * (toMax - toMin);

    /// <summary>acos to about 1e-4 without the intrinsic's cost.</summary>
    public static float FastACos(float x)
    {
        float ax = Abs(x);
        float res = (-0.156583f * ax + HALF_PI) * Sqrt(1.0f - ax);
        return x >= 0.0f ? res : PI - res;
    }
}

/// <summary>Colors: the sRGB transfer curves, luminance, HSV.</summary>
public static class Color
{
    public static float SRGBToLinear(float c) => c <= 0.04045f ? c / 12.92f : Pow((c + 0.055f) / 1.055f, 2.4f);
    public static float3 SRGBToLinear(float3 c) => new float3(SRGBToLinear(c.x), SRGBToLinear(c.y), SRGBToLinear(c.z));
    public static float LinearToSRGB(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * Pow(c, 1.0f / 2.4f) - 0.055f;
    public static float3 LinearToSRGB(float3 c) => new float3(LinearToSRGB(c.x), LinearToSRGB(c.y), LinearToSRGB(c.z));

    /// <summary>The luminance of a linear Rec. 709 color.</summary>
    public static float Luminance(float3 linearRgb) => Dot(linearRgb, new float3(0.2126729f, 0.7151522f, 0.0721750f));

    public static float3 RgbToHsv(float3 c)
    {
        float maxC = Common.Max3(c.x, c.y, c.z), minC = Common.Min3(c.x, c.y, c.z);
        float delta = maxC - minC;
        float h = 0.0f;
        if (delta > 0.0f)
        {
            if (maxC == c.x)
                h = (c.y - c.z) / delta;
            else if (maxC == c.y)
                h = 2.0f + (c.z - c.x) / delta;
            else
                h = 4.0f + (c.x - c.y) / delta;
            h = Frac(h / 6.0f);
        }
        float s = maxC > 0.0f ? delta / maxC : 0.0f;
        return new float3(h, s, maxC);
    }

    public static float3 HsvToRgb(float3 c)
    {
        float3 p = Abs(Frac(new float3(c.x) + new float3(1.0f, 2.0f / 3.0f, 1.0f / 3.0f)) * 6.0f - new float3(3.0f));
        return c.z * Lerp(new float3(1.0f), Saturate(p - new float3(1.0f)), c.y);
    }
}

/// <summary>Packing of normals and values.</summary>
public static class Packing
{
    /// <summary>A unit vector in two values (-1 to 1) on the octahedron.</summary>
    public static float2 PackNormalOctQuadEncode(float3 n)
    {
        n *= 1.0f / Dot(Abs(n), new float3(1.0f));
        float t = Saturate(-n.z);
        float2 xy = n.xy;
        return new float2(xy.x + (xy.x >= 0.0f ? t : -t), xy.y + (xy.y >= 0.0f ? t : -t));
    }

    public static float3 UnpackNormalOctQuadEncode(float2 f)
    {
        float3 n = new float3(f.x, f.y, 1.0f - Abs(f.x) - Abs(f.y));
        float t = Max(-n.z, 0.0f);
        n = new float3(n.x + (n.x >= 0.0f ? -t : t), n.y + (n.y >= 0.0f ? -t : t), n.z);
        return Normalize(n);
    }

    /// <summary>A normal of a normal map stored as RGB (0 to 1).</summary>
    public static float3 UnpackNormalRGB(float4 packedNormal, float scale)
    {
        float3 n = packedNormal.xyz * 2.0f - new float3(1.0f);
        return new float3(n.x * scale, n.y * scale, n.z);
    }

    /// <summary>A normal of a normal map stored in alpha and green (two channel compression).</summary>
    public static float3 UnpackNormalAG(float4 packedNormal, float scale)
    {
        float2 xy = new float2(packedNormal.w * packedNormal.x, packedNormal.y) * 2.0f - new float2(1.0f);
        float z = Sqrt(Max(1.0e-16f, 1.0f - Saturate(Dot(xy, xy))));
        return new float3(xy.x * scale, xy.y * scale, z);
    }

    public static float3 UnpackNormalScale(float4 packedNormal, float bumpScale) => UnpackNormalAG(packedNormal, bumpScale);
}
