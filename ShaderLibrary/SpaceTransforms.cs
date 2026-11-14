using HelixEngine.Builtin;
using HelixEngine.Shaders;
using static HelixEngine.Shaders.ShaderMath;

namespace HelixEngine.Rendering.ShaderLibrary;

/// <summary>Positions and directions between object, world, view and clip space, over the engine's camera and draw blocks.</summary>
public static class SpaceTransforms
{
    public static float4x4 GetObjectToWorldMatrix() => Instancing.ObjectToWorldMatrix();
    public static float4x4 GetWorldToObjectMatrix() => Instancing.WorldToObjectMatrix();
    public static float4x4 GetWorldToViewMatrix() => ShaderVariables.camera.helix_MatrixV;
    public static float4x4 GetViewToHClipMatrix() => ShaderVariables.camera.helix_MatrixP;
    public static float4x4 GetWorldToHClipMatrix() => ShaderVariables.camera.helix_MatrixVP;

    public static float3 GetCameraPositionWS() => ShaderVariables.camera.helix_WorldSpaceCameraPos.xyz;

    public static float3 TransformObjectToWorld(float3 positionOS) => Mul(GetObjectToWorldMatrix(), new float4(positionOS, 1.0f)).xyz;
    public static float3 TransformWorldToObject(float3 positionWS) => Mul(GetWorldToObjectMatrix(), new float4(positionWS, 1.0f)).xyz;
    public static float3 TransformWorldToView(float3 positionWS) => Mul(GetWorldToViewMatrix(), new float4(positionWS, 1.0f)).xyz;
    public static float4 TransformObjectToHClip(float3 positionOS) => Mul(GetWorldToHClipMatrix(), new float4(TransformObjectToWorld(positionOS), 1.0f));
    public static float4 TransformWorldToHClip(float3 positionWS) => Mul(GetWorldToHClipMatrix(), new float4(positionWS, 1.0f));
    public static float4 TransformWViewToHClip(float3 positionVS) => Mul(GetViewToHClipMatrix(), new float4(positionVS, 1.0f));

    public static float3 TransformObjectToWorldDir(float3 dirOS) => SafeNormalizeDir(Mul(GetObjectToWorldMatrix(), new float4(dirOS, 0.0f)).xyz);
    public static float3 TransformWorldToObjectDir(float3 dirWS) => SafeNormalizeDir(Mul(GetWorldToObjectMatrix(), new float4(dirWS, 0.0f)).xyz);
    public static float3 TransformWorldToViewDir(float3 dirWS) => SafeNormalizeDir(Mul(GetWorldToViewMatrix(), new float4(dirWS, 0.0f)).xyz);
    public static float3 TransformWorldToHClipDir(float3 dirWS) => Mul(GetWorldToHClipMatrix(), new float4(dirWS, 0.0f)).xyz;

    /// <summary>A normal by the transpose of the world to object matrix (right for any scale).</summary>
    public static float3 TransformObjectToWorldNormal(float3 normalOS) => SafeNormalizeDir(Mul(Transpose(GetWorldToObjectMatrix()), new float4(normalOS, 0.0f)).xyz);

    /// <summary>From a world space position toward the camera (not normalized).</summary>
    public static float3 GetWorldSpaceViewDir(float3 positionWS) => GetCameraPositionWS() - positionWS;

    public static float3 GetWorldSpaceNormalizeViewDir(float3 positionWS) => SafeNormalizeDir(GetWorldSpaceViewDir(positionWS));

    static float3 SafeNormalizeDir(float3 v) => Common.SafeNormalize(v);
}

/// <summary>The object data of the draw, or of the instance in the INSTANCING_ON variants.</summary>
public static class Instancing
{
    /// <summary>True in the variants the renderer draws instanced (the shader declares ShaderVariables.INSTANCING_ON).</summary>
    public static bool IsInstanced() => ShaderVariables.INSTANCING_ON;

    /// <summary>The instance's index in the draw (0 when not instanced).</summary>
    public static uint GetInstanceID() => ShaderVariables.INSTANCING_ON ? InstanceId() : 0u;

    public static float4x4 ObjectToWorldMatrix() => ShaderVariables.ObjectToWorldMatrix();

    public static float4x4 WorldToObjectMatrix() =>
        ShaderVariables.INSTANCING_ON ? ShaderVariables.instances.helix_Instances[(int)InstanceId()].helix_WorldToObject : ShaderVariables.draw.helix_WorldToObject;
}
