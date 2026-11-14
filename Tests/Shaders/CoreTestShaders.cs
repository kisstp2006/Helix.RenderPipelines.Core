using HelixEngine.Builtin;
using HelixEngine.Rendering.ShaderLibrary;
using HelixEngine.Shaders;
using static HelixEngine.Shaders.ShaderMath;
using LibraryColor = HelixEngine.Rendering.ShaderLibrary.Color;

namespace HelixEngine.Rendering.CoreTestShaders;

/// <summary>A post effect: _BlitTexture (scaled and offset by _BlitScaleBias) times _TintColor, both set by name.</summary>
[Shader("Tests/Core/Tint")]
public sealed class Tint : ShaderDefinition
{
    public struct TintData
    {
        public float4 _BlitScaleBias;
        public float4 _TintColor;
    }

    public struct Varyings
    {
        [Position] public float4 position;
        [Location(0)] public float2 uv;
    }

    [Uniform(0)] public static TintData tint;
    [Texture(0)] public static Texture2D<float4> _BlitTexture;
    [Sampler(0)] public static SamplerState blitSampler;

    [Pass("Tint", Cull = CullMode.Off, ZTest = CompareFunction.Always, ZWrite = false)]
    public static class TintPass
    {
        [VertexShader]
        public static Varyings Vertex([VertexId] uint id)
        {
            Varyings o;
            o.position = Sampling.GetFullScreenTriangleVertexPosition(id);
            o.uv = Sampling.GetFullScreenTriangleTexCoord(id) * tint._BlitScaleBias.xy + tint._BlitScaleBias.zw;
            return o;
        }

        [FragmentShader]
        [return: Target(0)]
        public static float4 Fragment(Varyings input) => _BlitTexture.SampleLevel(blitSampler, input.uv, 0.0f) * tint._TintColor;
    }
}

/// <summary>
/// Calls the whole shader library and draws white where its results are right: red the color functions, green packing
/// and the color spaces, blue the space transforms and the math helpers. Instanced too.
/// </summary>
[Shader("Tests/Core/Library")]
public sealed class Library : ShaderDefinition
{
    [Keyword(KeywordScope.Global)] public static readonly bool INSTANCING_ON;

    public struct Varyings
    {
        [Position] public float4 position;
        [Location(0)] public float transforms;
    }

    static float Near(float a, float b) => Abs(a - b) < 0.002f ? 1.0f : 0.0f;
    static float Near(float3 a, float3 b) => Length(a - b) < 0.004f ? 1.0f : 0.0f;
    static float Near(float4 a, float4 b) => Length(a - b) < 0.004f ? 1.0f : 0.0f;

    [Pass("Forward")]
    public static class Forward
    {
        [VertexShader]
        public static Varyings Vertex([Location(VertexInputs.Position)] float3 position)
        {
            Varyings o;
            o.position = SpaceTransforms.TransformObjectToHClip(position);
            float3 world = SpaceTransforms.TransformObjectToWorld(position);
            float ok = Near(o.position, ShaderVariables.ObjectToClip(position)) * Near(SpaceTransforms.TransformWorldToObject(world), position) *
                       Near(SpaceTransforms.TransformWorldToHClip(world), o.position) *
                       Near(SpaceTransforms.TransformWViewToHClip(SpaceTransforms.TransformWorldToView(world)), o.position) *
                       Near(SpaceTransforms.GetWorldSpaceViewDir(world), SpaceTransforms.GetCameraPositionWS() - world) *
                       Near(Length(SpaceTransforms.TransformObjectToWorldNormal(new float3(0.0f, 0.0f, -1.0f))), 1.0f) *
                       Near(Length(SpaceTransforms.GetWorldSpaceNormalizeViewDir(world)), 1.0f) *
                       Near(Length(SpaceTransforms.TransformObjectToWorldDir(new float3(1.0f, 0.0f, 0.0f))), 1.0f) *
                       (Instancing.GetInstanceID() < 128u ? 1.0f : 0.0f);
            o.transforms = ok;
            return o;
        }

        [FragmentShader]
        [return: Target(0)]
        public static float4 Fragment(Varyings input)
        {
            float red = Near(LibraryColor.SRGBToLinear(LibraryColor.LinearToSRGB(0.25f)), 0.25f) * Near(LibraryColor.SRGBToLinear(0.5f), 0.214041f) *
                        Near(LibraryColor.Luminance(new float3(1.0f)), 1.0f) *
                        Near(LibraryColor.HsvToRgb(LibraryColor.RgbToHsv(new float3(0.2f, 0.5f, 0.8f))), new float3(0.2f, 0.5f, 0.8f));
            float3 n = Normalize(new float3(0.3f, -0.5f, 0.8f));
            float3 back = Normalize(new float3(-0.2f, 0.4f, -0.9f));
            float green = Near(Packing.UnpackNormalOctQuadEncode(Packing.PackNormalOctQuadEncode(n)), n) *
                          Near(Packing.UnpackNormalOctQuadEncode(Packing.PackNormalOctQuadEncode(back)), back) *
                          Near(Packing.UnpackNormalRGB(new float4(0.5f, 0.5f, 1.0f, 1.0f), 1.0f), new float3(0.0f, 0.0f, 1.0f)) *
                          Near(Packing.UnpackNormalScale(new float4(1.0f, 0.5f, 0.0f, 0.5f), 1.0f), new float3(0.0f, 0.0f, 1.0f));
            float blue = input.transforms * Near(Common.Sq(3.0f), 9.0f) * Near(Common.Pow4(2.0f), 16.0f) * Near(Common.Max3(1.0f, 5.0f, 3.0f), 5.0f) *
                         Near(Common.Min3(4.0f, 2.0f, 3.0f), 2.0f) * Near(Common.PositivePow(-2.0f, 2.0f), 4.0f) *
                         Near(Length(Common.SafeNormalize(new float3(0.0f))), 0.0f) * Near(Common.Remap(5.0f, 0.0f, 10.0f, 0.0f, 1.0f), 0.5f) *
                         (Abs(Common.FastACos(0.5f) - 1.0471976f) < 0.01f ? 1.0f : 0.0f) * Near(Common.SafeDiv(1.0f, 1.0f), 1.0f);
            return new float4(red, green, blue, 1.0f);
        }
    }
}
