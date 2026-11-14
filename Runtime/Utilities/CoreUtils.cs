using System;
using HelixEngine.Experimental.Rendering;

namespace HelixEngine.Rendering
{
    /// <summary>What a target is cleared of.</summary>
    [Flags]
    public enum ClearFlag
    {
        None = 0,
        Color = 1,
        Depth = 2,
        Stencil = 4,
        DepthStencil = Depth | Stencil,
        ColorStencil = Color | Stencil,
        All = Color | Depth | Stencil,
    }

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

        // A handle as the target: the viewport is its scaled size (the part of a larger target this frame uses).
        static void SetViewport(CommandBuffer cmd, RTHandle target)
        {
            if (target == null || !target.useScaling)
                return;
            Vector2Int size = target.GetScaledSize(target.rtHandleProperties.currentViewportSize);
            cmd.SetViewport(new Rect(0.0f, 0.0f, size.x, size.y));
        }

        public static void SetRenderTarget(CommandBuffer cmd, RTHandle buffer, ClearFlag clearFlag, Color clearColor, int miplevel = 0,
                                           CubemapFace cubemapFace = CubemapFace.Unknown, int depthSlice = -1)
        {
            cmd.SetRenderTarget(buffer, miplevel, cubemapFace, depthSlice);
            SetViewport(cmd, buffer);
            ClearRenderTarget(cmd, clearFlag, clearColor);
        }

        public static void SetRenderTarget(CommandBuffer cmd, RTHandle buffer, ClearFlag clearFlag = ClearFlag.None, int miplevel = 0,
                                           CubemapFace cubemapFace = CubemapFace.Unknown, int depthSlice = -1) =>
            SetRenderTarget(cmd, buffer, clearFlag, Color.clear, miplevel, cubemapFace, depthSlice);

        public static void SetRenderTarget(CommandBuffer cmd, RTHandle colorBuffer, RTHandle depthBuffer, ClearFlag clearFlag, Color clearColor, int miplevel = 0,
                                           CubemapFace cubemapFace = CubemapFace.Unknown, int depthSlice = -1)
        {
            cmd.SetRenderTarget(colorBuffer, depthBuffer, miplevel, cubemapFace, depthSlice);
            SetViewport(cmd, colorBuffer);
            ClearRenderTarget(cmd, clearFlag, clearColor);
        }

        public static void SetRenderTarget(CommandBuffer cmd, RTHandle colorBuffer, RTHandle depthBuffer, ClearFlag clearFlag = ClearFlag.None, int miplevel = 0,
                                           CubemapFace cubemapFace = CubemapFace.Unknown, int depthSlice = -1) =>
            SetRenderTarget(cmd, colorBuffer, depthBuffer, clearFlag, Color.clear, miplevel, cubemapFace, depthSlice);

        public static void SetRenderTarget(CommandBuffer cmd, RTHandle buffer, RenderBufferLoadAction loadAction, RenderBufferStoreAction storeAction, ClearFlag clearFlag,
                                           Color clearColor)
        {
            cmd.SetRenderTarget(buffer, loadAction, storeAction);
            SetViewport(cmd, buffer);
            ClearRenderTarget(cmd, clearFlag, clearColor);
        }

        public static void SetViewport(CommandBuffer cmd, RTHandle target, int miplevel = 0)
        {
            if (target == null || !target.useScaling)
                return;
            Vector2Int size = target.GetScaledSize(target.rtHandleProperties.currentViewportSize);
            cmd.SetViewport(new Rect(0.0f, 0.0f, Mathf.Max(size.x >> miplevel, 1), Mathf.Max(size.y >> miplevel, 1)));
        }

        // ---------------------------------------------------------------------------------------------- materials

        /// <summary>A material of a shader of the engine or a pipeline, kept out of scenes (null with an error when the shader is missing).</summary>
        public static Material CreateEngineMaterial(string shaderPath)
        {
            Shader shader = Shader.Find(shaderPath);
            if (shader == null)
            {
                Debug.LogError("Cannot create required material because shader " + shaderPath + " could not be found");
                return null;
            }
            return CreateEngineMaterial(shader);
        }

        public static Material CreateEngineMaterial(Shader shader)
        {
            if (shader == null)
            {
                Debug.LogError("Cannot create required material because shader is null");
                return null;
            }
            return new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        /// <summary>Destroyed at once outside play mode, at the frame's end in it.</summary>
        public static void Destroy(Object obj)
        {
            if (obj == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(obj);
            else
                Object.DestroyImmediate(obj);
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
            GetRenderTargetAutoName(width, height, depth, format.ToString(), TextureDimension.None, name, mips, enableMSAA, msaaSamples, false);

        public static string GetRenderTargetAutoName(int width, int height, int depth, GraphicsFormat format, string name, bool mips = false, bool enableMSAA = false,
                                                     MSAASamples msaaSamples = MSAASamples.None) =>
            GetRenderTargetAutoName(width, height, depth, format.ToString(), TextureDimension.None, name, mips, enableMSAA, msaaSamples, false);

        public static string GetRenderTargetAutoName(int width, int height, int depth, GraphicsFormat format, TextureDimension dim, string name, bool mips = false,
                                                     bool enableMSAA = false, MSAASamples msaaSamples = MSAASamples.None, bool dynamicRes = false) =>
            GetRenderTargetAutoName(width, height, depth, format.ToString(), dim, name, mips, enableMSAA, msaaSamples, dynamicRes);

        static string GetRenderTargetAutoName(int width, int height, int depth, string format, TextureDimension dim, string name, bool mips, bool enableMSAA,
                                              MSAASamples msaaSamples, bool dynamicRes)
        {
            string result = $"{name}_{width}x{height}";
            if (depth > 1)
                result = $"{result}x{depth}";
            if (mips)
                result = $"{result}_Mips";
            result = $"{result}_{format}";
            if (dim != TextureDimension.None)
                result = $"{result}_{dim}";
            if (enableMSAA)
                result = $"{result}_{msaaSamples}";
            if (dynamicRes)
                result = $"{result}_dynamic";
            return result;
        }

        public static string GetTextureAutoName(int width, int height, TextureFormat format, TextureDimension dim = TextureDimension.None, string name = "", bool mips = false,
                                                int depth = 0) => GetTextureAutoName(width, height, format.ToString(), dim, name, mips, depth);

        public static string GetTextureAutoName(int width, int height, GraphicsFormat format, TextureDimension dim = TextureDimension.None, string name = "", bool mips = false,
                                                int depth = 0) => GetTextureAutoName(width, height, format.ToString(), dim, name, mips, depth);

        static string GetTextureAutoName(int width, int height, string format, TextureDimension dim, string name, bool mips, int depth)
        {
            string temp = depth == 0 ? $"{width}x{height}{(mips ? "_Mips" : "")}_{format}" : $"{width}x{height}x{depth}{(mips ? "_Mips" : "")}_{format}";
            return $"Texture_{(string.IsNullOrEmpty(name) ? "" : name + "_")}{(dim == TextureDimension.None ? "" : dim.ToString())}{temp}";
        }
    }
}
