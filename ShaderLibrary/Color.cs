using HelixEngine.Shaders;
using static HelixEngine.Shaders.ShaderMath;

namespace HelixEngine.Rendering.ShaderLibrary;

/// <summary>
/// Color spaces and grading: ARRI LogC (EI 800, the space of HDR grading look up tables), the fast sRGB curves, the
/// neutral tonemapper, hue rotation and the 2D strip look up tables (lutSize^2 x lutSize, blue picks the slice).
/// </summary>
public static class ColorGrading
{
    // LogC (EI 800) parameters.
    const float LogCCut = 0.011361f;
    const float LogCA = 5.555556f;
    const float LogCB = 0.047996f;
    const float LogCC = 0.244161f;
    const float LogCD = 0.386036f;
    const float LogCE = 5.301883f;
    const float LogCF = 0.092819f;
    const float Log2Of10 = 3.32192809f;

    /// <summary>The middle gray of ACEScc: contrast pivots around it in log space.</summary>
    public const float ACEScc_MIDGRAY = 0.4135884f;

    public static float LinearToLogC(float x) => x > LogCCut ? LogCC * (Log2(LogCA * x + LogCB) / Log2Of10) + LogCD : LogCE * x + LogCF;
    public static float3 LinearToLogC(float3 x) => new float3(LinearToLogC(x.x), LinearToLogC(x.y), LinearToLogC(x.z));
    public static float LogCToLinear(float x) => x > LogCE * LogCCut + LogCF ? (Exp2((x - LogCD) / LogCC * Log2Of10) - LogCB) / LogCA : (x - LogCF) / LogCE;
    public static float3 LogCToLinear(float3 x) => new float3(LogCToLinear(x.x), LogCToLinear(x.y), LogCToLinear(x.z));

    /// <summary>The sRGB curves approximated by polynomials (the pipelines' fast conversion).</summary>
    public static float3 FastSRGBToLinear(float3 c) => c * (c * (c * 0.305306011f + new float3(0.682171111f)) + new float3(0.012522878f));
    public static float3 FastLinearToSRGB(float3 c) => Max(Pow(Max(c, new float3(0.0f)), new float3(0.416666667f)) * 1.055f - new float3(0.055f), new float3(0.0f));

    static float3 NeutralCurve(float3 x, float a, float b, float c, float d, float e, float f) =>
        (x * (x * a + new float3(c * b)) + new float3(d * e)) / (x * (x * a + new float3(b)) + new float3(d * f)) - new float3(e / f);

    /// <summary>The neutral tonemapper: a curve that keeps hues and reaches white at 5.3.</summary>
    public static float3 NeutralTonemap(float3 x)
    {
        const float a = 0.2f, b = 0.29f, c = 0.24f, d = 0.272f, e = 0.02f, f = 0.3f;
        const float whiteLevel = 5.3f, whiteClip = 1.0f;
        float3 whiteScale = new float3(1.0f) / NeutralCurve(new float3(whiteLevel), a, b, c, d, e, f);
        x = NeutralCurve(x * whiteScale, a, b, c, d, e, f);
        x *= whiteScale;
        return x / whiteClip;
    }

    /// <summary>A hue (0 to 1) wrapped into [low, high).</summary>
    public static float RotateHue(float value, float low, float high) => value < low ? value + high : value > high ? value - high : value;

    /// <summary>The color of a texel of a strip look up table being built (params: lutHeight, 0.5 / lutWidth,
    /// 0.5 / lutHeight, lutHeight / (lutHeight - 1)).</summary>
    public static float3 GetLutStripValue(float2 uv, float4 parameters)
    {
        uv -= parameters.yz;
        float r = Frac(uv.x * parameters.x);
        float3 color = new float3(r, uv.y, uv.x - r / parameters.x);
        return color * parameters.w;
    }

    /// <summary>Where a strip look up table is read for a color (0 to 1 per channel; scaleOffset: 1 / lutWidth,
    /// 1 / lutHeight, lutHeight - 1): the two slices' coordinates (xy, zw) and the blend between them (the result's x of
    /// LutBlend).</summary>
    public static float4 LutCoordinates(float3 uvw, float3 scaleOffset)
    {
        float z = uvw.z * scaleOffset.z;
        float shift = Floor(z);
        float2 uv = uvw.xy * scaleOffset.z * scaleOffset.xy + scaleOffset.xy * 0.5f;
        uv.x += shift * scaleOffset.y;
        return new float4(uv, uv + new float2(scaleOffset.y, 0.0f));
    }

    public static float LutBlend(float3 uvw, float3 scaleOffset)
    {
        float z = uvw.z * scaleOffset.z;
        return z - Floor(z);
    }
}

/// <summary>Filtering helpers: the cubic B-spline of a texture from four bilinear taps.</summary>
public static class Filtering
{
    /// <summary>The four bilinear taps of a bicubic B-spline lookup: positions (xy of the first and second tap in
    /// each axis: the result's xy and zw are the x and y of tap 0 and tap 1) and the axis weights g0 and g1 (the
    /// weights' xy for x, zw for y).</summary>
    public static float4 BicubicPositions(float2 uv, float2 size)
    {
        float2 xy = uv * size - new float2(0.5f);
        float2 ic = Floor(xy);
        float2 f = xy - ic;
        float2 f2 = f * f, f3 = f2 * f;
        float2 w0 = (new float2(1.0f) - f) * (new float2(1.0f) - f) * (new float2(1.0f) - f) / 6.0f;
        float2 w1 = (new float2(4.0f) - f2 * 6.0f + f3 * 3.0f) / 6.0f;
        float2 w2 = (new float2(1.0f) + f * 3.0f + f2 * 3.0f - f3 * 3.0f) / 6.0f;
        float2 w3 = f3 / 6.0f;
        float2 g0 = w0 + w1, g1 = w2 + w3;
        float2 h0 = ic - new float2(1.0f) + w1 / g0 + new float2(0.5f);
        float2 h1 = ic + new float2(1.0f) + w3 / g1 + new float2(0.5f);
        return new float4(h0 / size, h1 / size);
    }

    public static float4 BicubicWeights(float2 uv, float2 size)
    {
        float2 xy = uv * size - new float2(0.5f);
        float2 f = xy - Floor(xy);
        float2 f2 = f * f, f3 = f2 * f;
        float2 w0 = (new float2(1.0f) - f) * (new float2(1.0f) - f) * (new float2(1.0f) - f) / 6.0f;
        float2 w1 = (new float2(4.0f) - f2 * 6.0f + f3 * 3.0f) / 6.0f;
        return new float4(w0 + w1, new float2(1.0f) - (w0 + w1));
    }
}
