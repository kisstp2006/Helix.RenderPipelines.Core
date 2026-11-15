using HelixEngine.Rendering.Internal;

namespace HelixEngine.Rendering
{
    /// <summary>
    /// Copies with the core's blit shaders: a full screen triangle (or a quad) reading _BlitTexture (a global the
    /// Blitter sets) scaled and offset by _BlitScaleBias at _BlitMipLevel; the native core draws them. The pipelines
    /// give Initialize their blit shaders (Hidden/Core/Blit and Hidden/Core/BlitColorAndDepth are the core's).
    /// </summary>
    public static class Blitter
    {
        public static void Initialize(Shader blitPS, Shader blitColorAndDepthPS)
        {
            if (TexturesNative.BlitterInitialized())
                throw new System.Exception("Blitter is already initialized. Please only initialize the blitter once or you will leak engine resources. If you need to re-initialize the blitter with different shaders destroy & recreate it.");
            TexturesNative.BlitterInitialize(blitPS, blitColorAndDepthPS);
        }

        public static void Cleanup() => TexturesNative.BlitterCleanup();

        /// <summary>The material of 2D blits (the engine's devices of this milestone blit one slice of the other kinds).</summary>
        public static Material GetBlitMaterial(TextureDimension dimension, bool singleSlice = false) => TexturesNative.BlitMaterial();

        // ------------------------------------------------------------------------------------- into the bound target

        public static void BlitTexture(CommandBuffer cmd, RTHandle source, Vector4 scaleBias, float mipLevel, bool bilinear) =>
            TexturesNative.BlitTexture(cmd.Id, Targets.Data(source), scaleBias, mipLevel, bilinear);

        public static void BlitTexture2D(CommandBuffer cmd, RTHandle source, Vector4 scaleBias, float mipLevel, bool bilinear) =>
            BlitTexture(cmd, source, scaleBias, mipLevel, bilinear);

        public static void BlitTexture(CommandBuffer cmd, RTHandle source, Vector4 scaleBias, Material material, int pass) =>
            BlitTexture(cmd, (RenderTargetIdentifier)source, scaleBias, material, pass);

        public static void BlitTexture(CommandBuffer cmd, RenderTargetIdentifier source, Vector4 scaleBias, Material material, int pass) =>
            TexturesNative.BlitTextureMaterial(cmd.Id, Targets.Data(source), scaleBias, material, pass);

        /// <summary>A material's full screen pass without a source (its own inputs).</summary>
        public static void BlitTexture(CommandBuffer cmd, Vector4 scaleBias, Material material, int pass) => TexturesNative.BlitNoSource(cmd.Id, scaleBias, material, pass);

        // ------------------------------------------------------------------------------------------- into a target

        public static void BlitTexture(CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier destination, Material material, int pass) =>
            TexturesNative.BlitTextureInto(cmd.Id, Targets.Data(source), Targets.Data(destination), -1, -1, material, pass);

        public static void BlitTexture(CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier destination, RenderBufferLoadAction loadAction,
                                       RenderBufferStoreAction storeAction, Material material, int pass) =>
            TexturesNative.BlitTextureInto(cmd.Id, Targets.Data(source), Targets.Data(destination), (int)loadAction, (int)storeAction, material, pass);

        /// <summary>The used part of a handle into another (its viewport set).</summary>
        public static void BlitCameraTexture(CommandBuffer cmd, RTHandle source, RTHandle destination, float mipLevel = 0.0f, bool bilinear = false) =>
            TexturesNative.BlitCameraTexture(cmd.Id, source?.m_Id ?? 0, destination?.m_Id ?? 0, default, true, mipLevel, bilinear);

        public static void BlitCameraTexture(CommandBuffer cmd, RTHandle source, RTHandle destination, Vector4 scaleBias, float mipLevel = 0.0f, bool bilinear = false) =>
            TexturesNative.BlitCameraTexture(cmd.Id, source?.m_Id ?? 0, destination?.m_Id ?? 0, scaleBias, false, mipLevel, bilinear);

        public static void BlitCameraTexture(CommandBuffer cmd, RTHandle source, RTHandle destination, Rect destViewport, float mipLevel = 0.0f, bool bilinear = false) =>
            TexturesNative.BlitCameraTextureViewport(cmd.Id, source?.m_Id ?? 0, destination?.m_Id ?? 0, destViewport, mipLevel, bilinear);

        public static void BlitCameraTexture(CommandBuffer cmd, RTHandle source, RTHandle destination, Material material, int pass) =>
            TexturesNative.BlitCameraTextureMaterial(cmd.Id, source?.m_Id ?? 0, destination?.m_Id ?? 0, -1, -1, material, pass);

        public static void BlitCameraTexture(CommandBuffer cmd, RTHandle source, RTHandle destination, RenderBufferLoadAction loadAction, RenderBufferStoreAction storeAction,
                                             Material material, int pass) =>
            TexturesNative.BlitCameraTextureMaterial(cmd.Id, source?.m_Id ?? 0, destination?.m_Id ?? 0, (int)loadAction, (int)storeAction, material, pass);

        // ----------------------------------------------------------------------------------- render graph passes

        public static void BlitTexture(RasterCommandBuffer cmd, RTHandle source, Vector4 scaleBias, float mipLevel, bool bilinear) =>
            BlitTexture(cmd.m_WrappedCommandBuffer, source, scaleBias, mipLevel, bilinear);

        public static void BlitTexture2D(RasterCommandBuffer cmd, RTHandle source, Vector4 scaleBias, float mipLevel, bool bilinear) =>
            BlitTexture(cmd.m_WrappedCommandBuffer, source, scaleBias, mipLevel, bilinear);

        public static void BlitTexture(RasterCommandBuffer cmd, RTHandle source, Vector4 scaleBias, Material material, int pass) =>
            BlitTexture(cmd.m_WrappedCommandBuffer, source, scaleBias, material, pass);

        public static void BlitTexture(RasterCommandBuffer cmd, RenderTargetIdentifier source, Vector4 scaleBias, Material material, int pass) =>
            BlitTexture(cmd.m_WrappedCommandBuffer, source, scaleBias, material, pass);

        public static void BlitTexture(RasterCommandBuffer cmd, Vector4 scaleBias, Material material, int pass) =>
            BlitTexture(cmd.m_WrappedCommandBuffer, scaleBias, material, pass);

        public static void BlitQuad(RasterCommandBuffer cmd, Texture source, Vector4 scaleBiasTex, Vector4 scaleBiasRT, int mipLevelTex, bool bilinear) =>
            BlitQuad(cmd.m_WrappedCommandBuffer, source, scaleBiasTex, scaleBiasRT, mipLevelTex, bilinear);

        public static void BlitCameraTexture(UnsafeCommandBuffer cmd, RTHandle source, RTHandle destination, float mipLevel = 0.0f, bool bilinear = false) =>
            BlitCameraTexture(cmd.m_WrappedCommandBuffer, source, destination, mipLevel, bilinear);

        public static void BlitCameraTexture(UnsafeCommandBuffer cmd, RTHandle source, RTHandle destination, Material material, int pass) =>
            BlitCameraTexture(cmd.m_WrappedCommandBuffer, source, destination, material, pass);

        // ------------------------------------------------------------------------------------------------- quads

        /// <summary>The source (scaleBiasTex of it) into the part of the target scaleBiasRT gives (xy size, zw offset, 0 to 1).</summary>
        public static void BlitQuad(CommandBuffer cmd, Texture source, Vector4 scaleBiasTex, Vector4 scaleBiasRT, int mipLevelTex, bool bilinear) =>
            TexturesNative.BlitQuad(cmd.Id, source, scaleBiasTex, scaleBiasRT, mipLevelTex, bilinear);

        /// <summary>A color texture into the bound target, and its depth from a depth texture when blitDepth.</summary>
        public static void BlitColorAndDepth(CommandBuffer cmd, Texture sourceColor, RenderTexture sourceDepth, Vector4 scaleBias, float mipLevel, bool blitDepth) =>
            TexturesNative.BlitColorAndDepth(cmd.Id, sourceColor, sourceDepth, scaleBias, mipLevel, blitDepth);
    }
}
