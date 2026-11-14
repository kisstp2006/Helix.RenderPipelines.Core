using HelixEngine.Experimental.Rendering;

namespace HelixEngine.Rendering
{
    /// <summary>A size from the reference size of a handle system (its scaled size).</summary>
    public delegate Vector2Int ScaleFunc(Vector2Int size);

    /// <summary>The reference sizes of a handle system this frame and the one before, and the scale of the viewport in the targets.</summary>
    public struct RTHandleProperties
    {
        public Vector2Int previousViewportSize;
        public Vector2Int previousRenderTargetSize;
        public Vector2Int currentViewportSize;
        public Vector2Int currentRenderTargetSize;
        /// <summary>xy: the current viewport over the current target size, zw: the previous one over the previous size.</summary>
        public Vector4 rtHandleScale;
    }

    /// <summary>The depth bits of a render target.</summary>
    public enum DepthBits
    {
        None = 0,
        Depth8 = 8,
        Depth16 = 16,
        Depth24 = 24,
        Depth32 = 32,
    }

    /// <summary>The samples of a multisampled render target.</summary>
    public enum MSAASamples
    {
        None = 1,
        MSAA2x = 2,
        MSAA4x = 4,
        MSAA8x = 8,
    }

    /// <summary>
    /// A render texture, a texture or a target identifier of a handle system. A handle sized by a scale or a scale
    /// function follows the system's reference size: it is reallocated when the reference grows past the largest one.
    /// </summary>
    public class RTHandle
    {
        internal RTHandleSystem m_Owner;
        internal RenderTexture m_RT;
        internal Texture m_ExternalTexture;
        internal RenderTargetIdentifier m_NameID;
        internal bool m_EnableMSAA;
        internal bool m_EnableRandomWrite;
        internal bool m_EnableHWDynamicScale;
        internal string m_Name;
        internal bool m_UseCustomHandleScales;
        internal RTHandleProperties m_CustomHandleProperties;
        internal ScaleFunc scaleFunc;

        internal RTHandle(RTHandleSystem owner) => m_Owner = owner;

        /// <summary>The scale of the reference size (useScaling with a scale factor).</summary>
        public Vector2 scaleFactor { get; internal set; }
        /// <summary>True when the size follows the reference size.</summary>
        public bool useScaling { get; internal set; }
        /// <summary>The reference size the handle was last sized for (its own size when it does not scale).</summary>
        public Vector2Int referenceSize { get; internal set; }
        public RTHandleProperties rtHandleProperties => m_UseCustomHandleScales ? m_CustomHandleProperties : m_Owner != null ? m_Owner.rtHandleProperties : default;
        public RenderTexture rt => m_RT;
        public RenderTargetIdentifier nameID => m_NameID;
        public string name => m_Name;
        public bool isMSAAEnabled => m_EnableMSAA;
        /// <summary>The texture of a handle made from one (null for render textures and identifiers).</summary>
        public Texture externalTexture => m_ExternalTexture;

        public static implicit operator RenderTargetIdentifier(RTHandle handle) => handle?.nameID ?? default;
        public static implicit operator Texture(RTHandle handle) => handle == null ? null : handle.rt != null ? handle.rt : handle.m_ExternalTexture;
        public static implicit operator RenderTexture(RTHandle handle) => handle?.rt;

        public void SetCustomHandleProperties(in RTHandleProperties properties)
        {
            m_UseCustomHandleScales = true;
            m_CustomHandleProperties = properties;
        }

        public void ClearCustomHandleProperties() => m_UseCustomHandleScales = false;

        /// <summary>The handle takes a render texture (not owned: Release does not destroy it).</summary>
        public void SetRenderTexture(RenderTexture rt)
        {
            m_RT = rt;
            m_ExternalTexture = null;
            m_NameID = rt != null ? new RenderTargetIdentifier(rt) : new RenderTargetIdentifier(BuiltinRenderTextureType.None);
        }

        public void SetTexture(Texture tex)
        {
            m_RT = null;
            m_ExternalTexture = tex;
            m_NameID = tex != null ? new RenderTargetIdentifier(tex) : new RenderTargetIdentifier(BuiltinRenderTextureType.None);
        }

        public void SetTexture(RenderTargetIdentifier tex)
        {
            m_RT = null;
            m_ExternalTexture = null;
            m_NameID = tex;
        }

        /// <summary>The size of the handle for a reference size.</summary>
        public Vector2Int GetScaledSize(Vector2Int refSize)
        {
            if (!useScaling)
                return refSize;
            if (scaleFunc != null)
                return scaleFunc(refSize);
            return new Vector2Int(Mathf.Max(Mathf.RoundToInt(scaleFactor.x * refSize.x), 1), Mathf.Max(Mathf.RoundToInt(scaleFactor.y * refSize.y), 1));
        }

        /// <summary>The size of the handle: for its reference size when it scales, else its own size.</summary>
        public Vector2Int GetScaledSize() => useScaling ? GetScaledSize(referenceSize) : referenceSize;

        /// <summary>Destroys the render texture the system made, and leaves the system.</summary>
        public void Release()
        {
            m_Owner?.Remove(this);
            if (m_RT != null && m_OwnsTexture)
                CoreUtils.Destroy(m_RT);
            m_NameID = new RenderTargetIdentifier(BuiltinRenderTextureType.None);
            m_RT = null;
            m_ExternalTexture = null;
        }

        internal bool m_OwnsTexture;

        // The render texture for a size: made again when the size changed.
        internal void Resize(Vector2Int size)
        {
            if (m_RT == null || (m_RT.width == size.x && m_RT.height == size.y))
                return;
            RenderTextureDescriptor desc = m_RT.descriptor;
            desc.width = size.x;
            desc.height = size.y;
            FilterMode filter = m_RT.filterMode;
            TextureWrapMode wrap = m_RT.wrapMode;
            int aniso = m_RT.anisoLevel;
            float bias = m_RT.mipMapBias;
            m_RT.Release();
            m_RT.descriptor = desc;
            m_RT.filterMode = filter;
            m_RT.wrapMode = wrap;
            m_RT.anisoLevel = aniso;
            m_RT.mipMapBias = bias;
            m_RT.name = CoreUtils.GetRenderTargetAutoName(size.x, size.y, desc.volumeDepth, desc.graphicsFormat, desc.dimension, m_Name, desc.useMipMap,
                                                          m_EnableMSAA, (MSAASamples)desc.msaaSamples);
            m_RT.Create();
        }
    }
}
