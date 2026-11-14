using System;

namespace HelixEngine.Rendering
{
    /// <summary>
    /// Copies with the core's blit shaders: a full screen triangle (or a quad) reading _BlitTexture scaled and offset by
    /// _BlitScaleBias at _BlitMipLevel. The pipelines give Initialize their blit shaders (Hidden/Core/Blit and
    /// Hidden/Core/BlitColorAndDepth are the core's).
    /// </summary>
    public static class Blitter
    {
        static Material s_Blit;
        static Material s_BlitColorAndDepth;
        static MaterialPropertyBlock s_PropertyBlock = new MaterialPropertyBlock();

        static class BlitShaderIDs
        {
            public static readonly int _BlitTexture = Shader.PropertyToID("_BlitTexture");
            public static readonly int _BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
            public static readonly int _BlitScaleBiasRt = Shader.PropertyToID("_BlitScaleBiasRt");
            public static readonly int _BlitMipLevel = Shader.PropertyToID("_BlitMipLevel");
            public static readonly int _InputDepth = Shader.PropertyToID("_InputDepthTexture");
        }

        // The passes of the blit shader, in its order.
        const int kNearest = 0, kBilinear = 1, kNearestQuad = 2, kBilinearQuad = 3;

        public static void Initialize(Shader blitPS, Shader blitColorAndDepthPS)
        {
            if (s_Blit != null)
                throw new Exception("Blitter is already initialized. Please only initialize the blitter once or you will leak engine resources. If you need to re-initialize the blitter with different shaders destroy & recreate it.");
            s_Blit = CoreUtils.CreateEngineMaterial(blitPS);
            s_BlitColorAndDepth = CoreUtils.CreateEngineMaterial(blitColorAndDepthPS);
            s_PropertyBlock = new MaterialPropertyBlock();
        }

        public static void Cleanup()
        {
            CoreUtils.Destroy(s_Blit);
            s_Blit = null;
            CoreUtils.Destroy(s_BlitColorAndDepth);
            s_BlitColorAndDepth = null;
        }

        /// <summary>The material of 2D blits (the engine's devices of this milestone blit one slice of the other kinds).</summary>
        public static Material GetBlitMaterial(TextureDimension dimension, bool singleSlice = false) => s_Blit;

        static Material Blit
        {
            get
            {
                if (s_Blit == null)
                    throw new InvalidOperationException("Blitter.Initialize was not called.");
                return s_Blit;
            }
        }

        static void DrawTriangle(CommandBuffer cmd, Material material, int shaderPass) =>
            cmd.DrawProcedural(Matrix4x4.identity, material, shaderPass, MeshTopology.Triangles, 3, 1, s_PropertyBlock);

        static void DrawQuad(CommandBuffer cmd, Material material, int shaderPass) =>
            cmd.DrawProcedural(Matrix4x4.identity, material, shaderPass, MeshTopology.Triangles, 6, 1, s_PropertyBlock);

        static void SetSource(CommandBuffer cmd, RenderTargetIdentifier source, Vector4 scaleBias, float mipLevel)
        {
            cmd.SetGlobalTexture(BlitShaderIDs._BlitTexture, source);
            s_PropertyBlock.SetVector(BlitShaderIDs._BlitScaleBias, scaleBias);
            s_PropertyBlock.SetFloat(BlitShaderIDs._BlitMipLevel, mipLevel);
        }

        // ------------------------------------------------------------------------------------- into the bound target

        public static void BlitTexture(CommandBuffer cmd, RTHandle source, Vector4 scaleBias, float mipLevel, bool bilinear)
        {
            s_PropertyBlock.Clear();
            SetSource(cmd, source, scaleBias, mipLevel);
            DrawTriangle(cmd, Blit, bilinear ? kBilinear : kNearest);
        }

        public static void BlitTexture2D(CommandBuffer cmd, RTHandle source, Vector4 scaleBias, float mipLevel, bool bilinear) =>
            BlitTexture(cmd, source, scaleBias, mipLevel, bilinear);

        public static void BlitTexture(CommandBuffer cmd, RTHandle source, Vector4 scaleBias, Material material, int pass) =>
            BlitTexture(cmd, (RenderTargetIdentifier)source, scaleBias, material, pass);

        public static void BlitTexture(CommandBuffer cmd, RenderTargetIdentifier source, Vector4 scaleBias, Material material, int pass)
        {
            s_PropertyBlock.Clear();
            SetSource(cmd, source, scaleBias, 0);
            DrawTriangle(cmd, material, pass);
        }

        /// <summary>A material's full screen pass without a source (its own inputs).</summary>
        public static void BlitTexture(CommandBuffer cmd, Vector4 scaleBias, Material material, int pass)
        {
            s_PropertyBlock.Clear();
            s_PropertyBlock.SetVector(BlitShaderIDs._BlitScaleBias, scaleBias);
            DrawTriangle(cmd, material, pass);
        }

        // ------------------------------------------------------------------------------------------- into a target

        public static void BlitTexture(CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier destination, Material material, int pass)
        {
            cmd.SetRenderTarget(destination, 0, CubemapFace.Unknown, -1);
            BlitTexture(cmd, source, new Vector4(1, 1, 0, 0), material, pass);
        }

        public static void BlitTexture(CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier destination, RenderBufferLoadAction loadAction,
                                       RenderBufferStoreAction storeAction, Material material, int pass)
        {
            cmd.SetRenderTarget(destination, loadAction, storeAction);
            BlitTexture(cmd, source, new Vector4(1, 1, 0, 0), material, pass);
        }

        // The part of the source this frame uses: its scale when it follows the reference size.
        static Vector2 ViewportScale(RTHandle source) =>
            source.useScaling ? new Vector2(source.rtHandleProperties.rtHandleScale.x, source.rtHandleProperties.rtHandleScale.y) : Vector2.one;

        /// <summary>The used part of a handle into another (its viewport set).</summary>
        public static void BlitCameraTexture(CommandBuffer cmd, RTHandle source, RTHandle destination, float mipLevel = 0.0f, bool bilinear = false)
        {
            Vector2 scale = ViewportScale(source);
            CoreUtils.SetRenderTarget(cmd, destination);
            BlitTexture(cmd, source, new Vector4(scale.x, scale.y, 0, 0), mipLevel, bilinear);
        }

        public static void BlitCameraTexture(CommandBuffer cmd, RTHandle source, RTHandle destination, Vector4 scaleBias, float mipLevel = 0.0f, bool bilinear = false)
        {
            CoreUtils.SetRenderTarget(cmd, destination);
            BlitTexture(cmd, source, scaleBias, mipLevel, bilinear);
        }

        public static void BlitCameraTexture(CommandBuffer cmd, RTHandle source, RTHandle destination, Rect destViewport, float mipLevel = 0.0f, bool bilinear = false)
        {
            Vector2 scale = ViewportScale(source);
            CoreUtils.SetRenderTarget(cmd, destination);
            cmd.SetViewport(destViewport);
            BlitTexture(cmd, source, new Vector4(scale.x, scale.y, 0, 0), mipLevel, bilinear);
        }

        public static void BlitCameraTexture(CommandBuffer cmd, RTHandle source, RTHandle destination, Material material, int pass)
        {
            Vector2 scale = ViewportScale(source);
            CoreUtils.SetRenderTarget(cmd, destination);
            BlitTexture(cmd, source, new Vector4(scale.x, scale.y, 0, 0), material, pass);
        }

        public static void BlitCameraTexture(CommandBuffer cmd, RTHandle source, RTHandle destination, RenderBufferLoadAction loadAction, RenderBufferStoreAction storeAction,
                                             Material material, int pass)
        {
            Vector2 scale = ViewportScale(source);
            CoreUtils.SetRenderTarget(cmd, destination, loadAction, storeAction, ClearFlag.None, Color.clear);
            BlitTexture(cmd, source, new Vector4(scale.x, scale.y, 0, 0), material, pass);
        }

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
        public static void BlitQuad(CommandBuffer cmd, Texture source, Vector4 scaleBiasTex, Vector4 scaleBiasRT, int mipLevelTex, bool bilinear)
        {
            s_PropertyBlock.Clear();
            SetSource(cmd, new RenderTargetIdentifier(source), scaleBiasTex, mipLevelTex);
            s_PropertyBlock.SetVector(BlitShaderIDs._BlitScaleBiasRt, scaleBiasRT);
            DrawQuad(cmd, Blit, bilinear ? kBilinearQuad : kNearestQuad);
        }

        /// <summary>A color texture into the bound target, and its depth from a depth texture when blitDepth.</summary>
        public static void BlitColorAndDepth(CommandBuffer cmd, Texture sourceColor, RenderTexture sourceDepth, Vector4 scaleBias, float mipLevel, bool blitDepth)
        {
            if (s_BlitColorAndDepth == null)
                throw new InvalidOperationException("Blitter.Initialize was not called.");
            s_PropertyBlock.Clear();
            SetSource(cmd, new RenderTargetIdentifier(sourceColor), scaleBias, mipLevel);
            if (blitDepth)
                cmd.SetGlobalTexture(BlitShaderIDs._InputDepth, new RenderTargetIdentifier(sourceDepth));
            DrawTriangle(cmd, s_BlitColorAndDepth, blitDepth ? 1 : 0);
        }
    }
}
