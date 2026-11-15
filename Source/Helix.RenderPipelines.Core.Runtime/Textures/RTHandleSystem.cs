using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using HelixEngine.Experimental.Rendering;
using HelixEngine.Rendering.Internal;

namespace HelixEngine.Rendering
{
    /// <summary>
    /// Render textures sized from a reference size (by a scale or a scale function): their targets have the largest
    /// reference size so far, so a smaller reference only changes the viewport; a larger one reallocates them. The
    /// native core's system by id.
    /// </summary>
    public unsafe class RTHandleSystem : IDisposable
    {
        internal readonly ulong m_Id;
        readonly bool m_Owned;
        readonly List<RTHandle> m_ScaledByFunction = new List<RTHandle>();

        public RTHandleSystem()
        {
            m_Id = TexturesNative.CreateSystem();
            m_Owned = true;
        }

        internal RTHandleSystem(ulong id) => m_Id = id;

        ~RTHandleSystem()
        {
            if (m_Owned && m_Id != 0)
                TexturesNative.DestroySystem(m_Id);
        }

        public RTHandleProperties rtHandleProperties => TexturesNative.Properties(m_Id);

        /// <summary>True when render textures are made with dynamic scale (no device of the engine scales them: kept for the pipelines).</summary>
        public bool hardwareDynamicResRequested => TexturesNative.HardwareDynamicResRequested(m_Id);

        public void Initialize(int width, int height) => TexturesNative.Initialize(m_Id, width, height);

        public void Dispose()
        {
            TexturesNative.Dispose(m_Id);
            foreach (RTHandle h in m_ScaledByFunction)
                h.Forget();
            m_ScaledByFunction.Clear();
        }

        public void Release(RTHandle rth) => rth?.Release();

        public int GetMaxWidth() => TexturesNative.MaxWidth(m_Id);
        public int GetMaxHeight() => TexturesNative.MaxHeight(m_Id);

        public void SetHardwareDynamicResolutionState(bool enableHWDynamicRes) => TexturesNative.SetHardwareDynamicResolutionState(m_Id, enableHWDynamicRes);

        public void ResetReferenceSize(int width, int height) => SetReferenceSize(width, height, true);
        public void SetReferenceSize(int width, int height) => SetReferenceSize(width, height, false);

        /// <summary>The size this frame renders at: the scaled targets grow to it when it is larger than before (or with reset, become its size).</summary>
        public void SetReferenceSize(int width, int height, bool reset) => TexturesNative.SetReferenceSize(m_Id, width, height, reset);

        // ------------------------------------------------------------------------------------------------- alloc

        static AllocData Data(int slices, DepthBits depthBufferBits, GraphicsFormat colorFormat, FilterMode filterMode, TextureWrapMode wrapMode,
                              TextureDimension dimension, bool enableRandomWrite, bool useMipMap, bool autoGenerateMips, bool isShadowMap, int anisoLevel,
                              float mipMapBias, MSAASamples msaaSamples, bool bindTextureMS, bool useDynamicScale, bool useDynamicScaleExplicit,
                              RenderTextureMemoryless memoryless, VRTextureUsage vrUsage) => new AllocData
        {
            slices = slices,
            depthBufferBits = (int)depthBufferBits,
            format = (int)colorFormat,
            filterMode = (int)filterMode,
            wrapMode = (int)wrapMode,
            dimension = (int)dimension,
            flags = (enableRandomWrite ? 1 : 0) | (useMipMap ? 2 : 0) | (autoGenerateMips ? 4 : 0) | (isShadowMap ? 8 : 0) | (bindTextureMS ? 16 : 0) |
                    (useDynamicScale ? 32 : 0) | (useDynamicScaleExplicit ? 64 : 0),
            anisoLevel = anisoLevel,
            mipMapBias = mipMapBias,
            msaaSamples = (int)msaaSamples,
            memoryless = (int)memoryless,
            vrUsage = (int)vrUsage,
        };

        static AllocData Data(in RenderTextureDescriptor d, FilterMode filterMode, TextureWrapMode wrapMode, bool isShadowMap, int anisoLevel, float mipMapBias) =>
            Data(d.volumeDepth, (DepthBits)d.depthBufferBits, d.graphicsFormat, filterMode, wrapMode, d.dimension, d.enableRandomWrite, d.useMipMap,
                 d.autoGenerateMips, isShadowMap, anisoLevel, mipMapBias, (MSAASamples)Math.Max(d.msaaSamples, 1), d.bindMS, d.useDynamicScale, false,
                 d.memoryless, d.vrUsage);

        public RTHandle Alloc(int width, int height, int slices = 1, DepthBits depthBufferBits = DepthBits.None, GraphicsFormat colorFormat = GraphicsFormat.R8G8B8A8_SRGB,
                              FilterMode filterMode = FilterMode.Point, TextureWrapMode wrapMode = TextureWrapMode.Repeat, TextureDimension dimension = TextureDimension.Tex2D,
                              bool enableRandomWrite = false, bool useMipMap = false, bool autoGenerateMips = true, bool isShadowMap = false, int anisoLevel = 1,
                              float mipMapBias = 0f, MSAASamples msaaSamples = MSAASamples.None, bool bindTextureMS = false, bool useDynamicScale = false,
                              bool useDynamicScaleExplicit = false, RenderTextureMemoryless memoryless = RenderTextureMemoryless.None,
                              VRTextureUsage vrUsage = VRTextureUsage.None, string name = "") =>
            RTHandle.Of(TexturesNative.Alloc(m_Id, width, height,
                                             Data(slices, depthBufferBits, colorFormat, filterMode, wrapMode, dimension, enableRandomWrite, useMipMap, autoGenerateMips,
                                                  isShadowMap, anisoLevel, mipMapBias, msaaSamples, bindTextureMS, useDynamicScale, useDynamicScaleExplicit, memoryless,
                                                  vrUsage),
                                             name ?? ""));

        public RTHandle Alloc(in RenderTextureDescriptor descriptor, FilterMode filterMode = FilterMode.Point, TextureWrapMode wrapMode = TextureWrapMode.Repeat,
                              bool isShadowMap = false, int anisoLevel = 1, float mipMapBias = 0f, string name = "") =>
            RTHandle.Of(TexturesNative.Alloc(m_Id, descriptor.width, descriptor.height, Data(descriptor, filterMode, wrapMode, isShadowMap, anisoLevel, mipMapBias),
                                             name ?? ""));

        public RTHandle Alloc(Vector2 scaleFactor, int slices = 1, DepthBits depthBufferBits = DepthBits.None, GraphicsFormat colorFormat = GraphicsFormat.R8G8B8A8_SRGB,
                              FilterMode filterMode = FilterMode.Point, TextureWrapMode wrapMode = TextureWrapMode.Repeat, TextureDimension dimension = TextureDimension.Tex2D,
                              bool enableRandomWrite = false, bool useMipMap = false, bool autoGenerateMips = true, bool isShadowMap = false, int anisoLevel = 1,
                              float mipMapBias = 0f, MSAASamples msaaSamples = MSAASamples.None, bool bindTextureMS = false, bool useDynamicScale = false,
                              bool useDynamicScaleExplicit = false, RenderTextureMemoryless memoryless = RenderTextureMemoryless.None,
                              VRTextureUsage vrUsage = VRTextureUsage.None, string name = "") =>
            RTHandle.Of(TexturesNative.AllocScaled(m_Id, scaleFactor,
                                                   Data(slices, depthBufferBits, colorFormat, filterMode, wrapMode, dimension, enableRandomWrite, useMipMap,
                                                        autoGenerateMips, isShadowMap, anisoLevel, mipMapBias, msaaSamples, bindTextureMS, useDynamicScale,
                                                        useDynamicScaleExplicit, memoryless, vrUsage),
                                                   name ?? ""));

        public RTHandle Alloc(Vector2 scaleFactor, in RenderTextureDescriptor descriptor, FilterMode filterMode = FilterMode.Point,
                              TextureWrapMode wrapMode = TextureWrapMode.Repeat, bool isShadowMap = false, int anisoLevel = 1, float mipMapBias = 0f, string name = "") =>
            RTHandle.Of(TexturesNative.AllocScaled(m_Id, scaleFactor, Data(descriptor, filterMode, wrapMode, isShadowMap, anisoLevel, mipMapBias), name ?? ""));

        public RTHandle Alloc(ScaleFunc scaleFunc, int slices = 1, DepthBits depthBufferBits = DepthBits.None, GraphicsFormat colorFormat = GraphicsFormat.R8G8B8A8_SRGB,
                              FilterMode filterMode = FilterMode.Point, TextureWrapMode wrapMode = TextureWrapMode.Repeat, TextureDimension dimension = TextureDimension.Tex2D,
                              bool enableRandomWrite = false, bool useMipMap = false, bool autoGenerateMips = true, bool isShadowMap = false, int anisoLevel = 1,
                              float mipMapBias = 0f, MSAASamples msaaSamples = MSAASamples.None, bool bindTextureMS = false, bool useDynamicScale = false,
                              bool useDynamicScaleExplicit = false, RenderTextureMemoryless memoryless = RenderTextureMemoryless.None,
                              VRTextureUsage vrUsage = VRTextureUsage.None, string name = "") =>
            ByFunction(scaleFunc, Data(slices, depthBufferBits, colorFormat, filterMode, wrapMode, dimension, enableRandomWrite, useMipMap, autoGenerateMips, isShadowMap,
                                       anisoLevel, mipMapBias, msaaSamples, bindTextureMS, useDynamicScale, useDynamicScaleExplicit, memoryless, vrUsage),
                       name);

        public RTHandle Alloc(ScaleFunc scaleFunc, in RenderTextureDescriptor descriptor, FilterMode filterMode = FilterMode.Point,
                              TextureWrapMode wrapMode = TextureWrapMode.Repeat, bool isShadowMap = false, int anisoLevel = 1, float mipMapBias = 0f, string name = "") =>
            ByFunction(scaleFunc, Data(descriptor, filterMode, wrapMode, isShadowMap, anisoLevel, mipMapBias), name);

        RTHandle ByFunction(ScaleFunc scaleFunc, AllocData data, string name)
        {
            if (scaleFunc == null)
                throw new ArgumentNullException(nameof(scaleFunc));
            GCHandle handle = GCHandle.Alloc(scaleFunc);
            ulong id = TexturesNative.AllocFunc(m_Id, (ulong)(delegate* unmanaged[Cdecl]<ulong, int, int, int*, int*, void>)&Scale, (ulong)GCHandle.ToIntPtr(handle),
                                                data, name ?? "");
            RTHandle h = RTHandle.Of(id);
            h.scaleFunc = scaleFunc;
            h.m_ScaleHandle = handle;
            m_ScaledByFunction.Add(h);
            return h;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        internal static void Scale(ulong user, int width, int height, int* outWidth, int* outHeight)
        {
            try
            {
                var f = (ScaleFunc)GCHandle.FromIntPtr((IntPtr)user).Target;
                Vector2Int size = f(new Vector2Int(width, height));
                *outWidth = size.x;
                *outHeight = size.y;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>A handle of a texture (not owned).</summary>
        public RTHandle Alloc(Texture texture) => RTHandle.Of(TexturesNative.AllocTexture(m_Id, texture));

        /// <summary>A handle of a render texture; with transferOwnership Release destroys it.</summary>
        public RTHandle Alloc(RenderTexture texture, bool transferOwnership = false) => RTHandle.Of(TexturesNative.AllocRenderTexture(m_Id, texture, transferOwnership));

        public RTHandle Alloc(RenderTargetIdentifier texture) => Alloc(texture, "");

        public RTHandle Alloc(RenderTargetIdentifier texture, string name) => RTHandle.Of(TexturesNative.AllocTarget(m_Id, Targets.Data(texture), name ?? ""));
    }
}
