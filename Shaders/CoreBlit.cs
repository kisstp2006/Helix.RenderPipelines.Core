using HelixEngine.Rendering.ShaderLibrary;
using HelixEngine.Shaders;
using static HelixEngine.Shaders.ShaderMath;

namespace HelixEngine.Rendering.CoreShaders;

/// <summary>
/// The Blitter's shader: _BlitTexture (a global) scaled and offset by _BlitScaleBias at _BlitMipLevel, over the bound
/// target (passes 0 and 1) or over the part _BlitScaleBiasRt gives (passes 2 and 3). Nearest passes read the texel, the
/// bilinear ones sample with the texture's own sampler.
/// </summary>
[Shader("Hidden/Core/Blit")]
public sealed class CoreBlit : ShaderDefinition
{
    public struct BlitData
    {
        public float4 _BlitScaleBias;
        public float4 _BlitScaleBiasRt;
        public float _BlitMipLevel;
    }

    public struct Varyings
    {
        [Position] public float4 position;
        [Location(0)] public float2 uv;
    }

    [Uniform(0)] public static BlitData blit;
    [Texture(0)] public static Texture2D<float4> _BlitTexture;
    [Sampler(0)] public static SamplerState blitSampler;

    static Varyings Triangle(uint id)
    {
        Varyings o;
        o.position = Sampling.GetFullScreenTriangleVertexPosition(id);
        o.uv = Sampling.GetFullScreenTriangleTexCoord(id) * blit._BlitScaleBias.xy + blit._BlitScaleBias.zw;
        return o;
    }

    static Varyings Quad(uint id)
    {
        Varyings o;
        float2 corner = Sampling.GetQuadTexCoord(id);
        o.position = Sampling.TargetToClip(corner * blit._BlitScaleBiasRt.xy + blit._BlitScaleBiasRt.zw, 0.5f);
        o.uv = corner * blit._BlitScaleBias.xy + blit._BlitScaleBias.zw;
        return o;
    }

    static float4 Nearest(float2 uv)
    {
        int mip = (int)blit._BlitMipLevel;
        int2 size = _BlitTexture.Size;
        int w = Max(size.x >> mip, 1), h = Max(size.y >> mip, 1);
        int2 texel = new int2(Clamp((int)Floor(uv.x * (float)w), 0, w - 1), Clamp((int)Floor(uv.y * (float)h), 0, h - 1));
        return _BlitTexture.Load(texel, mip);
    }

    [Pass("Nearest", Cull = CullMode.Off, ZTest = CompareFunction.Always, ZWrite = false)]
    public static class NearestPass
    {
        [VertexShader]
        public static Varyings Vertex([VertexId] uint id) => Triangle(id);

        [FragmentShader]
        [return: Target(0)]
        public static float4 Fragment(Varyings input) => Nearest(input.uv);
    }

    [Pass("Bilinear", Cull = CullMode.Off, ZTest = CompareFunction.Always, ZWrite = false)]
    public static class BilinearPass
    {
        [VertexShader]
        public static Varyings Vertex([VertexId] uint id) => Triangle(id);

        [FragmentShader]
        [return: Target(0)]
        public static float4 Fragment(Varyings input) => _BlitTexture.SampleLevel(blitSampler, input.uv, blit._BlitMipLevel);
    }

    [Pass("NearestQuad", Cull = CullMode.Off, ZTest = CompareFunction.Always, ZWrite = false)]
    public static class NearestQuadPass
    {
        [VertexShader]
        public static Varyings Vertex([VertexId] uint id) => Quad(id);

        [FragmentShader]
        [return: Target(0)]
        public static float4 Fragment(Varyings input) => Nearest(input.uv);
    }

    [Pass("BilinearQuad", Cull = CullMode.Off, ZTest = CompareFunction.Always, ZWrite = false)]
    public static class BilinearQuadPass
    {
        [VertexShader]
        public static Varyings Vertex([VertexId] uint id) => Quad(id);

        [FragmentShader]
        [return: Target(0)]
        public static float4 Fragment(Varyings input) => _BlitTexture.SampleLevel(blitSampler, input.uv, blit._BlitMipLevel);
    }
}

/// <summary>A color texture into the bound target (pass 0), with its depth from _InputDepthTexture (pass 1).</summary>
[Shader("Hidden/Core/BlitColorAndDepth")]
public sealed class CoreBlitColorAndDepth : ShaderDefinition
{
    public struct BlitData
    {
        public float4 _BlitScaleBias;
        public float _BlitMipLevel;
    }

    public struct Varyings
    {
        [Position] public float4 position;
        [Location(0)] public float2 uv;
    }

    public struct ColorAndDepth
    {
        [Target(0)] public float4 color;
        [Depth] public float depth;
    }

    [Uniform(0)] public static BlitData blit;
    [Texture(0)] public static Texture2D<float4> _BlitTexture;
    [Texture(1)] public static Texture2D<float> _InputDepthTexture;
    [Sampler(0)] public static SamplerState blitSampler;
    [Sampler(1)] public static SamplerState depthSampler;

    static Varyings Triangle(uint id)
    {
        Varyings o;
        o.position = Sampling.GetFullScreenTriangleVertexPosition(id);
        o.uv = Sampling.GetFullScreenTriangleTexCoord(id) * blit._BlitScaleBias.xy + blit._BlitScaleBias.zw;
        return o;
    }

    [Pass("ColorOnly", Cull = CullMode.Off, ZTest = CompareFunction.Always, ZWrite = false)]
    public static class ColorOnlyPass
    {
        [VertexShader]
        public static Varyings Vertex([VertexId] uint id) => Triangle(id);

        [FragmentShader]
        [return: Target(0)]
        public static float4 Fragment(Varyings input) => _BlitTexture.SampleLevel(blitSampler, input.uv, blit._BlitMipLevel);
    }

    [Pass("ColorAndDepth", Cull = CullMode.Off, ZTest = CompareFunction.Always, ZWrite = true)]
    public static class ColorAndDepthPass
    {
        [VertexShader]
        public static Varyings Vertex([VertexId] uint id) => Triangle(id);

        [FragmentShader]
        public static ColorAndDepth Fragment(Varyings input)
        {
            ColorAndDepth o;
            o.color = _BlitTexture.SampleLevel(blitSampler, input.uv, blit._BlitMipLevel);
            o.depth = _InputDepthTexture.SampleLevel(depthSampler, input.uv, 0.0f);
            return o;
        }
    }
}
