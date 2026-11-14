using HelixEngine.Builtin;
using HelixEngine.Shaders;

namespace HelixEngine.Rendering.ShaderLibrary;

/// <summary>Full screen triangles and quads made from the vertex id, for draws without a mesh (Blitter, CoreUtils.DrawFullScreen).</summary>
public static class Sampling
{
    /// <summary>The texture coordinate of vertex 0, 1 or 2 of the full screen triangle: (0, 0), (2, 0) and (0, 2).</summary>
    public static float2 GetFullScreenTriangleTexCoord(uint vertexID) =>
        new float2((vertexID & 1u) != 0u ? 2.0f : 0.0f, (vertexID & 2u) != 0u ? 2.0f : 0.0f);

    /// <summary>The clip position of vertex 0, 1 or 2 of the full screen triangle at depth z, for the bound target (flipped for a texture).</summary>
    public static float4 GetFullScreenTriangleVertexPosition(uint vertexID, float z)
    {
        float2 uv = GetFullScreenTriangleTexCoord(vertexID);
        return new float4(uv.x * 2.0f - 1.0f, (uv.y * 2.0f - 1.0f) * ShaderVariables.camera.helix_ProjectionParams.x, z, 1.0f);
    }

    public static float4 GetFullScreenTriangleVertexPosition(uint vertexID) => GetFullScreenTriangleVertexPosition(vertexID, 0.5f);

    /// <summary>The corner of vertex 0 to 5 of a quad drawn as two triangles: (0, 0), (1, 0), (1, 1), (0, 0), (1, 1), (0, 1).</summary>
    public static float2 GetQuadTexCoord(uint vertexID)
    {
        float x = vertexID == 1u || vertexID == 2u || vertexID == 4u ? 1.0f : 0.0f;
        float y = vertexID == 2u || vertexID == 4u || vertexID == 5u ? 1.0f : 0.0f;
        return new float2(x, y);
    }

    /// <summary>The clip position of a point of the target (0 to 1 across it), for the bound target.</summary>
    public static float4 TargetToClip(float2 at, float z) =>
        new float4(at.x * 2.0f - 1.0f, (at.y * 2.0f - 1.0f) * ShaderVariables.camera.helix_ProjectionParams.x, z, 1.0f);
}
