using HelixEngine.Shaders;
using static HelixEngine.Shaders.ShaderMath;

namespace HelixEngine.Rendering.ShaderLibrary;

/// <summary>
/// The ACES tonemapper as the pipelines use it: linear sRGB into ACES 2065-1 (AP0), the reference rendering transform's
/// glow, red modifier and desaturation, a rational fit of its tone scale with the 100 nit dim surround output transform,
/// the output's desaturation and the AP1 to Rec. 709 conversion through XYZ (D60 to D65).
/// </summary>
public static class ACES
{
    const float RRT_GLOW_GAIN = 0.05f;
    const float RRT_GLOW_MID = 0.08f;
    const float RRT_RED_SCALE = 0.82f;
    const float RRT_RED_PIVOT = 0.03f;
    const float RRT_RED_HUE = 0.0f;
    const float RRT_RED_WIDTH = 135.0f;
    const float RRT_SAT_FACTOR = 0.96f;
    const float ODT_SAT_FACTOR = 0.93f;
    const float DIM_SURROUND_GAMMA = 0.9811f;

    static float3 Mul3(float3 r0, float3 r1, float3 r2, float3 v) => new float3(Dot(r0, v), Dot(r1, v), Dot(r2, v));

    /// <summary>Linear sRGB (Rec. 709, D65) into ACES 2065-1.</summary>
    public static float3 UnityToACES(float3 x) =>
        Mul3(new float3(0.4397010f, 0.3829780f, 0.1773350f), new float3(0.0897923f, 0.8134230f, 0.0967616f), new float3(0.0175440f, 0.1115440f, 0.8707040f), x);

    static float3 ACESToACEScg(float3 x) =>
        Mul3(new float3(1.4514393161f, -0.2365107469f, -0.2149285693f), new float3(-0.0765537734f, 1.1762296998f, -0.0996759264f),
             new float3(0.0083161484f, -0.0060324498f, 0.9977163014f), x);

    static float3 AP1ToXYZ(float3 x) =>
        Mul3(new float3(0.6624541811f, 0.1340042065f, 0.1561876870f), new float3(0.2722287168f, 0.6740817658f, 0.0536895174f),
             new float3(-0.0055746495f, 0.0040607335f, 1.0103391003f), x);

    static float3 XYZToAP1(float3 x) =>
        Mul3(new float3(1.6410233797f, -0.3248032942f, -0.2364246952f), new float3(-0.6636628587f, 1.6153315917f, 0.0167563477f),
             new float3(0.0117218943f, -0.0082844420f, 0.9883948585f), x);

    static float3 D60ToD65(float3 x) =>
        Mul3(new float3(0.98722400f, -0.00611327f, 0.0159533f), new float3(-0.00759836f, 1.00186000f, 0.0053302f), new float3(0.00307257f, -0.00509595f, 1.0816800f), x);

    static float3 XYZToRec709(float3 x) =>
        Mul3(new float3(3.2409699419f, -1.5373831776f, -0.4986107603f), new float3(-0.9692436363f, 1.8759675015f, 0.0415550574f),
             new float3(0.0556300797f, -0.2039769589f, 1.0569715142f), x);

    /// <summary>The saturation matrix of a factor around AP1's luminance: (1 - s) * Y + s * rgb.</summary>
    static float3 Saturation(float3 rgb, float s)
    {
        float y = Dot(new float3(0.2722287168f, 0.6740817658f, 0.0536895174f), rgb);
        return new float3(y * (1.0f - s)) + rgb * s;
    }

    static float RgbToSaturation(float3 rgb)
    {
        float mi = Min(Min(rgb.x, rgb.y), rgb.z), ma = Max(Max(rgb.x, rgb.y), rgb.z);
        return (Max(ma, 1e-4f) - Max(mi, 1e-4f)) / Max(ma, 1e-2f);
    }

    static float RgbToYc(float3 rgb)
    {
        const float ycRadiusWeight = 1.75f;
        float r = rgb.x, g = rgb.y, b = rgb.z;
        float chroma = Sqrt(Max(b * (b - g) + g * (g - r) + r * (r - b), 0.0f));
        return (b + g + r + ycRadiusWeight * chroma) / 3.0f;
    }

    static float SigmoidShaper(float x)
    {
        float t = Max(1.0f - Abs(x / 2.0f), 0.0f);
        float y = 1.0f + Sign(x) * (1.0f - t * t);
        return y / 2.0f;
    }

    static float GlowForward(float ycIn, float glowGainIn, float glowMid)
    {
        if (ycIn <= 2.0f / 3.0f * glowMid)
            return glowGainIn;
        if (ycIn >= 2.0f * glowMid)
            return 0.0f;
        return glowGainIn * (glowMid / ycIn - 0.5f);
    }

    static float RgbToHue(float3 rgb)
    {
        if (rgb.x == rgb.y && rgb.y == rgb.z)
            return 0.0f;
        float hue = (180.0f / Common.PI) * Atan2(Sqrt(3.0f) * (rgb.y - rgb.z), 2.0f * rgb.x - rgb.y - rgb.z);
        return hue < 0.0f ? hue + 360.0f : hue;
    }

    static float CenterHue(float hue, float centerH)
    {
        float h = hue - centerH;
        if (h < -180.0f)
            h += 360.0f;
        else if (h > 180.0f)
            h -= 360.0f;
        return h;
    }

    static float3 XYZToxyY(float3 xyz)
    {
        float divisor = Max(xyz.x + xyz.y + xyz.z, 1e-4f);
        return new float3(xyz.x / divisor, xyz.y / divisor, xyz.y);
    }

    static float3 xyYToXYZ(float3 xyY)
    {
        float y = Max(xyY.y, 1e-4f);
        return new float3(xyY.x * xyY.z / y, xyY.z, (1.0f - xyY.x - xyY.y) * xyY.z / y);
    }

    /// <summary>The output transform's gamma for a dim surround (the tone scale is for a dark one).</summary>
    static float3 DarkSurroundToDimSurround(float3 linearCV)
    {
        float3 xyY = XYZToxyY(AP1ToXYZ(linearCV));
        xyY.z = Pow(Clamp(xyY.z, 0.0f, Common.HALF_MAX), DIM_SURROUND_GAMMA);
        return XYZToAP1(xyYToXYZ(xyY));
    }

    /// <summary>An ACES 2065-1 color tonemapped into linear Rec. 709 (0 to 1 for the display).</summary>
    public static float3 AcesTonemap(float3 aces)
    {
        // The glow module.
        float saturation = RgbToSaturation(aces);
        float ycIn = RgbToYc(aces);
        float s = SigmoidShaper((saturation - 0.4f) / 0.2f);
        aces *= 1.0f + GlowForward(ycIn, RRT_GLOW_GAIN * s, RRT_GLOW_MID);
        // The red modifier.
        float centeredHue = CenterHue(RgbToHue(aces), RRT_RED_HUE);
        float hueWeight = Smoothstep(0.0f, 1.0f, 1.0f - Abs(2.0f * centeredHue / RRT_RED_WIDTH));
        hueWeight *= hueWeight;
        aces.x += hueWeight * saturation * (RRT_RED_PIVOT - aces.x) * (1.0f - RRT_RED_SCALE);
        // Into the rendering space, desaturated.
        float3 acescg = Saturation(Max(ACESToACEScg(aces), new float3(0.0f)), RRT_SAT_FACTOR);
        // The tone scale (a fit of the reference rendering transform with the 100 nit output transform).
        const float a = 278.5085f, b = 10.7772f, c = 293.6045f, d = 88.7122f, e = 80.6889f;
        float3 x = acescg;
        float3 rgbPost = x * (x * a + new float3(b)) / (x * (x * c + new float3(d)) + new float3(e));
        float3 linearCV = Saturation(DarkSurroundToDimSurround(rgbPost), ODT_SAT_FACTOR);
        return XYZToRec709(D60ToD65(AP1ToXYZ(linearCV)));
    }
}
