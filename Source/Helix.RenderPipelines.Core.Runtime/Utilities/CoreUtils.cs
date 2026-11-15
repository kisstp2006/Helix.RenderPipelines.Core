using System;
using HelixEngine.Experimental.Rendering;
using HelixEngine.Rendering.Internal;

namespace HelixEngine.Rendering
{
    /// <summary>Helpers of the pipelines: targets set and cleared, engine materials, keywords, full screen draws, names.</summary>
    public static class CoreUtils
    {
        public static Texture2D whiteTexture => Texture2D.whiteTexture;
        public static Texture2D blackTexture => Texture2D.blackTexture;

        // ------------------------------------------------------------------------------------------------ targets

        public static void ClearRenderTarget(CommandBuffer cmd, ClearFlag clearFlag, Color clearColor)
        {
            if (clearFlag != ClearFlag.None)
                cmd.ClearRenderTarget((RTClearFlags)(int)clearFlag, clearColor, 1.0f, 0);
        }

        public static void SetRenderTarget(CommandBuffer cmd, RenderTargetIdentifier buffer, ClearFlag clearFlag, Color clearColor, int miplevel = 0,
                                           CubemapFace cubemapFace = CubemapFace.Unknown, int depthSlice = -1)
        {
            cmd.SetRenderTarget(buffer, miplevel, cubemapFace, depthSlice);
            ClearRenderTarget(cmd, clearFlag, clearColor);
        }

        public static void SetRenderTarget(CommandBuffer cmd, RenderTargetIdentifier buffer, ClearFlag clearFlag = ClearFlag.None, int miplevel = 0,
                                           CubemapFace cubemapFace = CubemapFace.Unknown, int depthSlice = -1) =>
            SetRenderTarget(cmd, buffer, clearFlag, Color.clear, miplevel, cubemapFace, depthSlice);

        public static void SetRenderTarget(CommandBuffer cmd, RenderTargetIdentifier colorBuffer, RenderTargetIdentifier depthBuffer, ClearFlag clearFlag,
                                           Color clearColor, int miplevel = 0, CubemapFace cubemapFace = CubemapFace.Unknown, int depthSlice = -1)
        {
            cmd.SetRenderTarget(colorBuffer, depthBuffer, miplevel, cubemapFace, depthSlice);
            ClearRenderTarget(cmd, clearFlag, clearColor);
        }

        public static void SetRenderTarget(CommandBuffer cmd, RenderTargetIdentifier colorBuffer, RenderTargetIdentifier depthBuffer, ClearFlag clearFlag = ClearFlag.None,
                                           int miplevel = 0, CubemapFace cubemapFace = CubemapFace.Unknown, int depthSlice = -1) =>
            SetRenderTarget(cmd, colorBuffer, depthBuffer, clearFlag, Color.clear, miplevel, cubemapFace, depthSlice);

        public static void SetRenderTarget(CommandBuffer cmd, RenderTargetIdentifier[] colorBuffers, RenderTargetIdentifier depthBuffer, ClearFlag clearFlag = ClearFlag.None) =>
            SetRenderTarget(cmd, colorBuffers, depthBuffer, clearFlag, Color.clear);

        public static void SetRenderTarget(CommandBuffer cmd, RenderTargetIdentifier[] colorBuffers, RenderTargetIdentifier depthBuffer, ClearFlag clearFlag, Color clearColor)
        {
            cmd.SetRenderTarget(colorBuffers, depthBuffer);
            ClearRenderTarget(cmd, clearFlag, clearColor);
        }

        public static void SetRenderTarget(CommandBuffer cmd, RenderTargetIdentifier buffer, RenderBufferLoadAction loadAction, RenderBufferStoreAction storeAction,
                                           ClearFlag clearFlag, Color clearColor)
        {
            cmd.SetRenderTarget(buffer, loadAction, storeAction);
            ClearRenderTarget(cmd, clearFlag, clearColor);
        }

        public static void SetRenderTarget(CommandBuffer cmd, RenderTargetIdentifier buffer, RenderBufferLoadAction loadAction, RenderBufferStoreAction storeAction,
                                           ClearFlag clearFlag) => SetRenderTarget(cmd, buffer, loadAction, storeAction, clearFlag, Color.clear);

        public static void SetRenderTarget(CommandBuffer cmd, RTHandle buffer, ClearFlag clearFlag, Color clearColor, int miplevel = 0,
                                           CubemapFace cubemapFace = CubemapFace.Unknown, int depthSlice = -1) =>
            TexturesNative.SetHandleTarget(cmd.Id, buffer?.m_Id ?? 0, 0, (int)clearFlag, clearColor, miplevel, (int)cubemapFace, depthSlice);

        public static void SetRenderTarget(CommandBuffer cmd, RTHandle buffer, ClearFlag clearFlag = ClearFlag.None, int miplevel = 0,
                                           CubemapFace cubemapFace = CubemapFace.Unknown, int depthSlice = -1) =>
            SetRenderTarget(cmd, buffer, clearFlag, Color.clear, miplevel, cubemapFace, depthSlice);

        public static void SetRenderTarget(CommandBuffer cmd, RTHandle colorBuffer, RTHandle depthBuffer, ClearFlag clearFlag, Color clearColor, int miplevel = 0,
                                           CubemapFace cubemapFace = CubemapFace.Unknown, int depthSlice = -1) =>
            TexturesNative.SetHandleTarget(cmd.Id, colorBuffer?.m_Id ?? 0, depthBuffer?.m_Id ?? 0, (int)clearFlag, clearColor, miplevel, (int)cubemapFace, depthSlice);

        public static void SetRenderTarget(CommandBuffer cmd, RTHandle colorBuffer, RTHandle depthBuffer, ClearFlag clearFlag = ClearFlag.None, int miplevel = 0,
                                           CubemapFace cubemapFace = CubemapFace.Unknown, int depthSlice = -1) =>
            SetRenderTarget(cmd, colorBuffer, depthBuffer, clearFlag, Color.clear, miplevel, cubemapFace, depthSlice);

        public static void SetRenderTarget(CommandBuffer cmd, RTHandle buffer, RenderBufferLoadAction loadAction, RenderBufferStoreAction storeAction, ClearFlag clearFlag,
                                           Color clearColor) =>
            TexturesNative.SetHandleTargetActions(cmd.Id, buffer?.m_Id ?? 0, (int)loadAction, (int)storeAction, (int)clearFlag, clearColor);

        public static void SetViewport(CommandBuffer cmd, RTHandle target, int miplevel = 0) => TexturesNative.SetViewport(cmd.Id, target?.m_Id ?? 0, miplevel);

        // ---------------------------------------------------------------------------------------------- materials

        /// <summary>A material of a shader of the engine or a pipeline, kept out of scenes (null with an error when the shader is missing).</summary>
        public static Material CreateEngineMaterial(string shaderPath) => TexturesNative.CreateEngineMaterialByName(shaderPath ?? "");

        public static Material CreateEngineMaterial(Shader shader) => TexturesNative.CreateEngineMaterial(shader);

        /// <summary>Destroyed at once outside play mode, at the frame's end in it.</summary>
        public static void Destroy(Object obj)
        {
            if (obj != null)
                TexturesNative.Destroy(obj);
        }

        public static void Destroy(params Object[] objs)
        {
            if (objs == null)
                return;
            foreach (Object o in objs)
                Destroy(o);
        }

        // ----------------------------------------------------------------------------------------------- keywords

        public static void SetKeyword(CommandBuffer cmd, string keyword, bool state)
        {
            if (state)
                cmd.EnableShaderKeyword(keyword);
            else
                cmd.DisableShaderKeyword(keyword);
        }

        public static void SetKeyword(Material material, string keyword, bool state)
        {
            if (state)
                material.EnableKeyword(keyword);
            else
                material.DisableKeyword(keyword);
        }

        public static void SetKeyword(Material material, LocalKeyword keyword, bool state) => material.SetKeyword(keyword, state);

        // ------------------------------------------------------------------------------------------- full screen

        /// <summary>A full screen triangle: three vertices whose positions the shader makes from the vertex id.</summary>
        public static void DrawFullScreen(CommandBuffer commandBuffer, Material material, MaterialPropertyBlock properties = null, int shaderPassId = 0) =>
            commandBuffer.DrawProcedural(Matrix4x4.identity, material, shaderPassId, MeshTopology.Triangles, 3, 1, properties);

        public static void DrawFullScreen(CommandBuffer commandBuffer, Material material, RenderTargetIdentifier colorBuffer, MaterialPropertyBlock properties = null,
                                          int shaderPassId = 0)
        {
            commandBuffer.SetRenderTarget(colorBuffer, 0, CubemapFace.Unknown, -1);
            DrawFullScreen(commandBuffer, material, properties, shaderPassId);
        }

        public static void DrawFullScreen(CommandBuffer commandBuffer, Material material, RenderTargetIdentifier colorBuffer, RenderTargetIdentifier depthStencilBuffer,
                                          MaterialPropertyBlock properties = null, int shaderPassId = 0)
        {
            commandBuffer.SetRenderTarget(colorBuffer, depthStencilBuffer, 0, CubemapFace.Unknown, -1);
            DrawFullScreen(commandBuffer, material, properties, shaderPassId);
        }

        // -------------------------------------------------------------------------------------------------- names

        public static string GetRenderTargetAutoName(int width, int height, int depth, RenderTextureFormat format, string name, bool mips = false, bool enableMSAA = false,
                                                     MSAASamples msaaSamples = MSAASamples.None) =>
            TexturesNative.RenderTargetAutoName(width, height, depth, format.ToString(), (int)TextureDimension.None, name ?? "", mips, enableMSAA, (int)msaaSamples, false);

        public static string GetRenderTargetAutoName(int width, int height, int depth, GraphicsFormat format, string name, bool mips = false, bool enableMSAA = false,
                                                     MSAASamples msaaSamples = MSAASamples.None) =>
            TexturesNative.RenderTargetAutoName(width, height, depth, format.ToString(), (int)TextureDimension.None, name ?? "", mips, enableMSAA, (int)msaaSamples, false);

        public static string GetRenderTargetAutoName(int width, int height, int depth, GraphicsFormat format, TextureDimension dim, string name, bool mips = false,
                                                     bool enableMSAA = false, MSAASamples msaaSamples = MSAASamples.None, bool dynamicRes = false) =>
            TexturesNative.RenderTargetAutoName(width, height, depth, format.ToString(), (int)dim, name ?? "", mips, enableMSAA, (int)msaaSamples, dynamicRes);

        public static string GetTextureAutoName(int width, int height, TextureFormat format, TextureDimension dim = TextureDimension.None, string name = "", bool mips = false,
                                                int depth = 0) => TexturesNative.TextureAutoName(width, height, format.ToString(), (int)dim, name ?? "", mips, depth);

        public static string GetTextureAutoName(int width, int height, GraphicsFormat format, TextureDimension dim = TextureDimension.None, string name = "", bool mips = false,
                                                int depth = 0) => TexturesNative.TextureAutoName(width, height, format.ToString(), (int)dim, name ?? "", mips, depth);
    }
}
