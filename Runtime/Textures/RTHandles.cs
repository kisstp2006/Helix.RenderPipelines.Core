using HelixEngine.Experimental.Rendering;

namespace HelixEngine.Rendering
{
    /// <summary>The default handle system, shared by the pipelines.</summary>
    public static class RTHandles
    {
        static readonly RTHandleSystem s_DefaultInstance = new RTHandleSystem();

        public static int maxWidth => s_DefaultInstance.GetMaxWidth();
        public static int maxHeight => s_DefaultInstance.GetMaxHeight();
        public static RTHandleProperties rtHandleProperties => s_DefaultInstance.rtHandleProperties;

        public static void Initialize(int width, int height) => s_DefaultInstance.Initialize(width, height);

        public static void Release(RTHandle rth) => s_DefaultInstance.Release(rth);

        public static void SetHardwareDynamicResolutionState(bool hwDynamicResRequested) => s_DefaultInstance.SetHardwareDynamicResolutionState(hwDynamicResRequested);

        public static void SetReferenceSize(int width, int height) => s_DefaultInstance.SetReferenceSize(width, height);

        public static void ResetReferenceSize(int width, int height) => s_DefaultInstance.ResetReferenceSize(width, height);

        public static Vector2Int CalculateDimensions(Vector2 scaleFactor) =>
            new Vector2Int(Mathf.Max(Mathf.RoundToInt(scaleFactor.x * maxWidth), 1), Mathf.Max(Mathf.RoundToInt(scaleFactor.y * maxHeight), 1));

        public static Vector2Int CalculateDimensions(ScaleFunc scaleFunc)
        {
            Vector2Int size = scaleFunc(new Vector2Int(maxWidth, maxHeight));
            return new Vector2Int(Mathf.Max(size.x, 1), Mathf.Max(size.y, 1));
        }

        public static RTHandle Alloc(int width, int height, int slices = 1, DepthBits depthBufferBits = DepthBits.None, GraphicsFormat colorFormat = GraphicsFormat.R8G8B8A8_SRGB,
                                     FilterMode filterMode = FilterMode.Point, TextureWrapMode wrapMode = TextureWrapMode.Repeat, TextureDimension dimension = TextureDimension.Tex2D,
                                     bool enableRandomWrite = false, bool useMipMap = false, bool autoGenerateMips = true, bool isShadowMap = false, int anisoLevel = 1,
                                     float mipMapBias = 0f, MSAASamples msaaSamples = MSAASamples.None, bool bindTextureMS = false, bool useDynamicScale = false,
                                     bool useDynamicScaleExplicit = false, RenderTextureMemoryless memoryless = RenderTextureMemoryless.None,
                                     VRTextureUsage vrUsage = VRTextureUsage.None, string name = "") =>
            s_DefaultInstance.Alloc(width, height, slices, depthBufferBits, colorFormat, filterMode, wrapMode, dimension, enableRandomWrite, useMipMap, autoGenerateMips, isShadowMap,
                                    anisoLevel, mipMapBias, msaaSamples, bindTextureMS, useDynamicScale, useDynamicScaleExplicit, memoryless, vrUsage, name);

        public static RTHandle Alloc(in RenderTextureDescriptor descriptor, FilterMode filterMode = FilterMode.Point, TextureWrapMode wrapMode = TextureWrapMode.Repeat,
                                     bool isShadowMap = false, int anisoLevel = 1, float mipMapBias = 0f, string name = "") =>
            s_DefaultInstance.Alloc(descriptor, filterMode, wrapMode, isShadowMap, anisoLevel, mipMapBias, name);

        public static RTHandle Alloc(Vector2 scaleFactor, int slices = 1, DepthBits depthBufferBits = DepthBits.None, GraphicsFormat colorFormat = GraphicsFormat.R8G8B8A8_SRGB,
                                     FilterMode filterMode = FilterMode.Point, TextureWrapMode wrapMode = TextureWrapMode.Repeat, TextureDimension dimension = TextureDimension.Tex2D,
                                     bool enableRandomWrite = false, bool useMipMap = false, bool autoGenerateMips = true, bool isShadowMap = false, int anisoLevel = 1,
                                     float mipMapBias = 0f, MSAASamples msaaSamples = MSAASamples.None, bool bindTextureMS = false, bool useDynamicScale = false,
                                     bool useDynamicScaleExplicit = false, RenderTextureMemoryless memoryless = RenderTextureMemoryless.None,
                                     VRTextureUsage vrUsage = VRTextureUsage.None, string name = "") =>
            s_DefaultInstance.Alloc(scaleFactor, slices, depthBufferBits, colorFormat, filterMode, wrapMode, dimension, enableRandomWrite, useMipMap, autoGenerateMips, isShadowMap,
                                    anisoLevel, mipMapBias, msaaSamples, bindTextureMS, useDynamicScale, useDynamicScaleExplicit, memoryless, vrUsage, name);

        public static RTHandle Alloc(Vector2 scaleFactor, in RenderTextureDescriptor descriptor, FilterMode filterMode = FilterMode.Point,
                                     TextureWrapMode wrapMode = TextureWrapMode.Repeat, bool isShadowMap = false, int anisoLevel = 1, float mipMapBias = 0f, string name = "") =>
            s_DefaultInstance.Alloc(scaleFactor, descriptor, filterMode, wrapMode, isShadowMap, anisoLevel, mipMapBias, name);

        public static RTHandle Alloc(ScaleFunc scaleFunc, int slices = 1, DepthBits depthBufferBits = DepthBits.None, GraphicsFormat colorFormat = GraphicsFormat.R8G8B8A8_SRGB,
                                     FilterMode filterMode = FilterMode.Point, TextureWrapMode wrapMode = TextureWrapMode.Repeat, TextureDimension dimension = TextureDimension.Tex2D,
                                     bool enableRandomWrite = false, bool useMipMap = false, bool autoGenerateMips = true, bool isShadowMap = false, int anisoLevel = 1,
                                     float mipMapBias = 0f, MSAASamples msaaSamples = MSAASamples.None, bool bindTextureMS = false, bool useDynamicScale = false,
                                     bool useDynamicScaleExplicit = false, RenderTextureMemoryless memoryless = RenderTextureMemoryless.None,
                                     VRTextureUsage vrUsage = VRTextureUsage.None, string name = "") =>
            s_DefaultInstance.Alloc(scaleFunc, slices, depthBufferBits, colorFormat, filterMode, wrapMode, dimension, enableRandomWrite, useMipMap, autoGenerateMips, isShadowMap,
                                    anisoLevel, mipMapBias, msaaSamples, bindTextureMS, useDynamicScale, useDynamicScaleExplicit, memoryless, vrUsage, name);

        public static RTHandle Alloc(ScaleFunc scaleFunc, in RenderTextureDescriptor descriptor, FilterMode filterMode = FilterMode.Point,
                                     TextureWrapMode wrapMode = TextureWrapMode.Repeat, bool isShadowMap = false, int anisoLevel = 1, float mipMapBias = 0f, string name = "") =>
            s_DefaultInstance.Alloc(scaleFunc, descriptor, filterMode, wrapMode, isShadowMap, anisoLevel, mipMapBias, name);

        public static RTHandle Alloc(Texture tex) => s_DefaultInstance.Alloc(tex);
        public static RTHandle Alloc(RenderTexture tex, bool transferOwnership = false) => s_DefaultInstance.Alloc(tex, transferOwnership);
        public static RTHandle Alloc(RenderTargetIdentifier tex) => s_DefaultInstance.Alloc(tex);
        public static RTHandle Alloc(RenderTargetIdentifier tex, string name) => s_DefaultInstance.Alloc(tex, name);
    }
}
