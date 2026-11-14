using System;
using System.Collections.Generic;
using HelixEngine.Experimental.Rendering;

namespace HelixEngine.Rendering
{
    /// <summary>
    /// Render textures sized from a reference size (by a scale or a scale function): their targets have the largest
    /// reference size so far, so a smaller reference only changes the viewport; a larger one reallocates them.
    /// </summary>
    public class RTHandleSystem : IDisposable
    {
        readonly HashSet<RTHandle> m_AutoSizedRTs = new HashSet<RTHandle>();
        int m_MaxWidths;
        int m_MaxHeights;
        bool m_HardwareDynamicResRequested;
        RTHandleProperties m_RTHandleProperties;

        public RTHandleProperties rtHandleProperties => m_RTHandleProperties;

        /// <summary>True when render textures are made with dynamic scale (no device of the engine scales them: kept for the pipelines).</summary>
        public bool hardwareDynamicResRequested => m_HardwareDynamicResRequested;

        public void Initialize(int width, int height)
        {
            if (m_AutoSizedRTs.Count != 0)
            {
                var names = new List<string>();
                foreach (RTHandle h in m_AutoSizedRTs)
                    names.Add(h.name);
                Debug.LogWarning($"RTHandle.Initialize: There are uncleared RTHandles: {string.Join(", ", names)}. Please release them first.");
            }
            m_MaxWidths = width;
            m_MaxHeights = height;
            m_HardwareDynamicResRequested = false;
        }

        public void Dispose()
        {
            foreach (RTHandle h in new List<RTHandle>(m_AutoSizedRTs))
                h.Release();
            m_AutoSizedRTs.Clear();
        }

        public void Release(RTHandle rth) => rth?.Release();

        internal void Remove(RTHandle rth) => m_AutoSizedRTs.Remove(rth);

        public int GetMaxWidth() => m_MaxWidths;
        public int GetMaxHeight() => m_MaxHeights;

        public void SetHardwareDynamicResolutionState(bool enableHWDynamicRes) => m_HardwareDynamicResRequested = enableHWDynamicRes;

        public void ResetReferenceSize(int width, int height) => SetReferenceSize(width, height, true);

        public void SetReferenceSize(int width, int height) => SetReferenceSize(width, height, false);

        /// <summary>The size this frame renders at: the scaled targets grow to it when it is larger than before (or with reset, become its size).</summary>
        public void SetReferenceSize(int width, int height, bool reset)
        {
            m_RTHandleProperties.previousViewportSize = m_RTHandleProperties.currentViewportSize;
            m_RTHandleProperties.previousRenderTargetSize = m_RTHandleProperties.currentRenderTargetSize;
            Vector2 lastFrameMaxSize = new Vector2(GetMaxWidth(), GetMaxHeight());
            width = Mathf.Max(width, 1);
            height = Mathf.Max(height, 1);
            bool sizeChanged = width > GetMaxWidth() || height > GetMaxHeight() || reset;
            if (sizeChanged)
                Resize(width, height, reset);
            m_RTHandleProperties.currentViewportSize = new Vector2Int(width, height);
            m_RTHandleProperties.currentRenderTargetSize = new Vector2Int(GetMaxWidth(), GetMaxHeight());
            Vector2 maxSize = new Vector2(GetMaxWidth(), GetMaxHeight());
            Vector2 scaleCurrent = new Vector2(m_RTHandleProperties.currentViewportSize.x / maxSize.x, m_RTHandleProperties.currentViewportSize.y / maxSize.y);
            Vector2 scalePrevious = new Vector2(m_RTHandleProperties.previousViewportSize.x / lastFrameMaxSize.x,
                                                m_RTHandleProperties.previousViewportSize.y / lastFrameMaxSize.y);
            m_RTHandleProperties.rtHandleScale = new Vector4(scaleCurrent.x, scaleCurrent.y, scalePrevious.x, scalePrevious.y);
        }

        void Resize(int width, int height, bool reset)
        {
            m_MaxWidths = reset ? width : Math.Max(width, m_MaxWidths);
            m_MaxHeights = reset ? height : Math.Max(height, m_MaxHeights);
            var maxSize = new Vector2Int(m_MaxWidths, m_MaxHeights);
            foreach (RTHandle h in m_AutoSizedRTs)
            {
                h.referenceSize = maxSize;
                h.Resize(h.GetScaledSize(maxSize));
            }
        }

        // ------------------------------------------------------------------------------------------------- alloc

        public RTHandle Alloc(int width, int height, int slices = 1, DepthBits depthBufferBits = DepthBits.None, GraphicsFormat colorFormat = GraphicsFormat.R8G8B8A8_SRGB,
                              FilterMode filterMode = FilterMode.Point, TextureWrapMode wrapMode = TextureWrapMode.Repeat, TextureDimension dimension = TextureDimension.Tex2D,
                              bool enableRandomWrite = false, bool useMipMap = false, bool autoGenerateMips = true, bool isShadowMap = false, int anisoLevel = 1,
                              float mipMapBias = 0f, MSAASamples msaaSamples = MSAASamples.None, bool bindTextureMS = false, bool useDynamicScale = false,
                              bool useDynamicScaleExplicit = false, RenderTextureMemoryless memoryless = RenderTextureMemoryless.None,
                              VRTextureUsage vrUsage = VRTextureUsage.None, string name = "")
        {
            RenderTexture rt = Create(width, height, slices, depthBufferBits, colorFormat, filterMode, wrapMode, dimension, enableRandomWrite, useMipMap, autoGenerateMips,
                                      isShadowMap, anisoLevel, mipMapBias, msaaSamples, bindTextureMS, useDynamicScale || useDynamicScaleExplicit, memoryless, vrUsage, name);
            var h = new RTHandle(this) { m_OwnsTexture = true };
            h.SetRenderTexture(rt);
            h.useScaling = false;
            h.m_EnableRandomWrite = enableRandomWrite;
            h.m_EnableMSAA = msaaSamples != MSAASamples.None;
            h.m_EnableHWDynamicScale = useDynamicScale;
            h.m_Name = name;
            h.referenceSize = new Vector2Int(width, height);
            return h;
        }

        public RTHandle Alloc(in RenderTextureDescriptor descriptor, FilterMode filterMode = FilterMode.Point, TextureWrapMode wrapMode = TextureWrapMode.Repeat,
                              bool isShadowMap = false, int anisoLevel = 1, float mipMapBias = 0f, string name = "")
        {
            RenderTextureDescriptor d = descriptor;
            RTHandle h = Alloc(d.width, d.height, d.volumeDepth, (DepthBits)d.depthBufferBits, d.graphicsFormat, filterMode, wrapMode, d.dimension, d.enableRandomWrite, d.useMipMap,
                               d.autoGenerateMips, isShadowMap, anisoLevel, mipMapBias, (MSAASamples)d.msaaSamples, d.bindMS, d.useDynamicScale, false, d.memoryless,
                               d.vrUsage, name);
            return h;
        }

        public RTHandle Alloc(Vector2 scaleFactor, int slices = 1, DepthBits depthBufferBits = DepthBits.None, GraphicsFormat colorFormat = GraphicsFormat.R8G8B8A8_SRGB,
                              FilterMode filterMode = FilterMode.Point, TextureWrapMode wrapMode = TextureWrapMode.Repeat, TextureDimension dimension = TextureDimension.Tex2D,
                              bool enableRandomWrite = false, bool useMipMap = false, bool autoGenerateMips = true, bool isShadowMap = false, int anisoLevel = 1,
                              float mipMapBias = 0f, MSAASamples msaaSamples = MSAASamples.None, bool bindTextureMS = false, bool useDynamicScale = false,
                              bool useDynamicScaleExplicit = false, RenderTextureMemoryless memoryless = RenderTextureMemoryless.None,
                              VRTextureUsage vrUsage = VRTextureUsage.None, string name = "")
        {
            var maxSize = new Vector2Int(GetMaxWidth(), GetMaxHeight());
            var size = new Vector2Int(Mathf.Max(Mathf.RoundToInt(scaleFactor.x * maxSize.x), 1), Mathf.Max(Mathf.RoundToInt(scaleFactor.y * maxSize.y), 1));
            RTHandle h = Alloc(size.x, size.y, slices, depthBufferBits, colorFormat, filterMode, wrapMode, dimension, enableRandomWrite, useMipMap, autoGenerateMips, isShadowMap,
                               anisoLevel, mipMapBias, msaaSamples, bindTextureMS, useDynamicScale, useDynamicScaleExplicit, memoryless, vrUsage, name);
            return AutoSized(h, scaleFactor, null, maxSize);
        }

        public RTHandle Alloc(Vector2 scaleFactor, in RenderTextureDescriptor descriptor, FilterMode filterMode = FilterMode.Point,
                              TextureWrapMode wrapMode = TextureWrapMode.Repeat, bool isShadowMap = false, int anisoLevel = 1, float mipMapBias = 0f, string name = "")
        {
            RenderTextureDescriptor d = descriptor;
            return Alloc(scaleFactor, d.volumeDepth, (DepthBits)d.depthBufferBits, d.graphicsFormat, filterMode, wrapMode, d.dimension, d.enableRandomWrite, d.useMipMap,
                         d.autoGenerateMips, isShadowMap, anisoLevel, mipMapBias, (MSAASamples)d.msaaSamples, d.bindMS, d.useDynamicScale, false, d.memoryless, d.vrUsage, name);
        }

        public RTHandle Alloc(ScaleFunc scaleFunc, int slices = 1, DepthBits depthBufferBits = DepthBits.None, GraphicsFormat colorFormat = GraphicsFormat.R8G8B8A8_SRGB,
                              FilterMode filterMode = FilterMode.Point, TextureWrapMode wrapMode = TextureWrapMode.Repeat, TextureDimension dimension = TextureDimension.Tex2D,
                              bool enableRandomWrite = false, bool useMipMap = false, bool autoGenerateMips = true, bool isShadowMap = false, int anisoLevel = 1,
                              float mipMapBias = 0f, MSAASamples msaaSamples = MSAASamples.None, bool bindTextureMS = false, bool useDynamicScale = false,
                              bool useDynamicScaleExplicit = false, RenderTextureMemoryless memoryless = RenderTextureMemoryless.None,
                              VRTextureUsage vrUsage = VRTextureUsage.None, string name = "")
        {
            if (scaleFunc == null)
                throw new ArgumentNullException(nameof(scaleFunc));
            var maxSize = new Vector2Int(GetMaxWidth(), GetMaxHeight());
            Vector2Int size = scaleFunc(maxSize);
            size = new Vector2Int(Mathf.Max(size.x, 1), Mathf.Max(size.y, 1));
            RTHandle h = Alloc(size.x, size.y, slices, depthBufferBits, colorFormat, filterMode, wrapMode, dimension, enableRandomWrite, useMipMap, autoGenerateMips, isShadowMap,
                               anisoLevel, mipMapBias, msaaSamples, bindTextureMS, useDynamicScale, useDynamicScaleExplicit, memoryless, vrUsage, name);
            return AutoSized(h, Vector2.one, scaleFunc, maxSize);
        }

        public RTHandle Alloc(ScaleFunc scaleFunc, in RenderTextureDescriptor descriptor, FilterMode filterMode = FilterMode.Point,
                              TextureWrapMode wrapMode = TextureWrapMode.Repeat, bool isShadowMap = false, int anisoLevel = 1, float mipMapBias = 0f, string name = "")
        {
            RenderTextureDescriptor d = descriptor;
            return Alloc(scaleFunc, d.volumeDepth, (DepthBits)d.depthBufferBits, d.graphicsFormat, filterMode, wrapMode, d.dimension, d.enableRandomWrite, d.useMipMap,
                         d.autoGenerateMips, isShadowMap, anisoLevel, mipMapBias, (MSAASamples)d.msaaSamples, d.bindMS, d.useDynamicScale, false, d.memoryless, d.vrUsage, name);
        }

        /// <summary>A handle of a texture (not owned).</summary>
        public RTHandle Alloc(Texture texture)
        {
            var h = new RTHandle(this);
            h.SetTexture(texture);
            h.useScaling = false;
            h.m_Name = texture != null ? texture.name : "";
            h.referenceSize = texture != null ? new Vector2Int(texture.width, texture.height) : Vector2Int.zero;
            return h;
        }

        /// <summary>A handle of a render texture; with transferOwnership Release destroys it.</summary>
        public RTHandle Alloc(RenderTexture texture, bool transferOwnership = false)
        {
            var h = new RTHandle(this) { m_OwnsTexture = transferOwnership };
            h.SetRenderTexture(texture);
            h.useScaling = false;
            h.m_EnableMSAA = texture != null && texture.antiAliasing > 1;
            h.m_Name = texture != null ? texture.name : "";
            h.referenceSize = texture != null ? new Vector2Int(texture.width, texture.height) : Vector2Int.zero;
            return h;
        }

        public RTHandle Alloc(RenderTargetIdentifier texture) => Alloc(texture, "");

        public RTHandle Alloc(RenderTargetIdentifier texture, string name)
        {
            var h = new RTHandle(this);
            h.SetTexture(texture);
            h.useScaling = false;
            h.m_Name = name;
            return h;
        }

        RTHandle AutoSized(RTHandle h, Vector2 scaleFactor, ScaleFunc scaleFunc, Vector2Int referenceSize)
        {
            h.useScaling = true;
            h.scaleFactor = scaleFactor;
            h.scaleFunc = scaleFunc;
            h.referenceSize = referenceSize;
            m_AutoSizedRTs.Add(h);
            return h;
        }

        static RenderTexture Create(int width, int height, int slices, DepthBits depthBufferBits, GraphicsFormat colorFormat, FilterMode filterMode,
                                    TextureWrapMode wrapMode, TextureDimension dimension, bool enableRandomWrite, bool useMipMap, bool autoGenerateMips, bool isShadowMap,
                                    int anisoLevel, float mipMapBias, MSAASamples msaaSamples, bool bindTextureMS, bool useDynamicScale,
                                    RenderTextureMemoryless memoryless, VRTextureUsage vrUsage, string name)
        {
            bool depthOnly = isShadowMap || colorFormat == GraphicsFormat.None;
            DepthBits depth = depthOnly && depthBufferBits == DepthBits.None ? DepthBits.Depth32 : depthBufferBits;
            var desc = new RenderTextureDescriptor(width, height, depthOnly ? GraphicsFormat.None : colorFormat, (int)depth)
            {
                dimension = dimension,
                volumeDepth = Mathf.Max(slices, 1),
                enableRandomWrite = enableRandomWrite,
                useMipMap = useMipMap,
                autoGenerateMips = autoGenerateMips,
                msaaSamples = (int)msaaSamples,
                bindMS = bindTextureMS,
                useDynamicScale = useDynamicScale,
                memoryless = memoryless,
                vrUsage = vrUsage,
            };
            var rt = new RenderTexture(desc)
            {
                filterMode = filterMode,
                wrapMode = wrapMode,
                anisoLevel = anisoLevel,
                mipMapBias = mipMapBias,
                hideFlags = HideFlags.HideAndDontSave,
            };
            rt.name = CoreUtils.GetRenderTargetAutoName(width, height, desc.volumeDepth, colorFormat, dimension, name, useMipMap, msaaSamples != MSAASamples.None, msaaSamples);
            rt.Create();
            return rt;
        }
    }
}
