using System;
using HelixEngine.Experimental.Rendering;
using HelixEngine.Rendering.RendererUtils;

namespace HelixEngine.Rendering.RenderGraphModule
{
    /// <summary>How a pass uses a resource. Write keeps what was there (it is loaded); WriteAll replaces all of it.</summary>
    [Flags]
    public enum AccessFlags
    {
        None = 0,
        Read = 1 << 0,
        Write = 1 << 1,
        Discard = 1 << 2,
        WriteAll = Write | Discard,
        ReadWrite = Read | Write,
    }

    /// <summary>How the size of a texture of the graph is given.</summary>
    public enum TextureSizeMode
    {
        Explicit,
        Scale,
        Functor,
    }

    /// <summary>A texture of a render graph: valid while the graph records and executes the frame it was made in.</summary>
    public struct TextureHandle : IEquatable<TextureHandle>
    {
        internal int m_Index;
        internal int m_Graph;

        internal TextureHandle(int index, int graph)
        {
            m_Index = index;
            m_Graph = graph;
        }

        static readonly TextureHandle s_NullHandle = default;

        public static TextureHandle nullHandle => s_NullHandle;

        public bool IsValid() => m_Index != 0;

        /// <summary>The texture of the handle; only in the render function of a pass that uses it.</summary>
        public static implicit operator RTHandle(TextureHandle texture) => texture.IsValid() ? RenderGraph.Resolve(texture) : null;

        public static implicit operator RenderTargetIdentifier(TextureHandle texture)
        {
            RTHandle h = texture;
            return h != null ? h.nameID : new RenderTargetIdentifier(BuiltinRenderTextureType.None);
        }

        public static implicit operator Texture(TextureHandle texture)
        {
            RTHandle h = texture;
            return h;
        }

        public bool Equals(TextureHandle other) => m_Index == other.m_Index && m_Graph == other.m_Graph;
        public override bool Equals(object obj) => obj is TextureHandle other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(m_Index, m_Graph);
        public static bool operator ==(TextureHandle a, TextureHandle b) => a.Equals(b);
        public static bool operator !=(TextureHandle a, TextureHandle b) => !a.Equals(b);
    }

    /// <summary>A renderer list of a render graph, made when the pass that uses it runs.</summary>
    public struct RendererListHandle : IEquatable<RendererListHandle>
    {
        internal int m_Index;
        internal int m_Graph;

        internal RendererListHandle(int index, int graph)
        {
            m_Index = index;
            m_Graph = graph;
        }

        public bool IsValid() => m_Index != 0;

        public static implicit operator RendererList(RendererListHandle handle) => handle.IsValid() ? RenderGraph.ResolveList(handle) : RendererList.nullRendererList;

        public bool Equals(RendererListHandle other) => m_Index == other.m_Index && m_Graph == other.m_Graph;
        public override bool Equals(object obj) => obj is RendererListHandle other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(m_Index, m_Graph);
        public static bool operator ==(RendererListHandle a, RendererListHandle b) => a.Equals(b);
        public static bool operator !=(RendererListHandle a, RendererListHandle b) => !a.Equals(b);
    }

    /// <summary>A texture the graph makes: its size (explicit, a scale of the reference size or a function of it) and format.</summary>
    public struct TextureDesc
    {
        public TextureSizeMode sizeMode;
        public int width;
        public int height;
        public int slices;
        public Vector2 scale;
        public ScaleFunc func;
        public DepthBits depthBufferBits;
        public GraphicsFormat colorFormat;
        public FilterMode filterMode;
        public TextureWrapMode wrapMode;
        public TextureDimension dimension;
        public bool enableRandomWrite;
        public bool useMipMap;
        public bool autoGenerateMips;
        public bool isShadowMap;
        public int anisoLevel;
        public float mipMapBias;
        public MSAASamples msaaSamples;
        public bool bindTextureMS;
        public bool useDynamicScale;
        public RenderTextureMemoryless memoryless;
        public VRTextureUsage vrUsage;
        public string name;
        /// <summary>Cleared to clearColor at its first use in a frame.</summary>
        public bool clearBuffer;
        public Color clearColor;
        /// <summary>Its contents are not kept after its last use (always so for textures the graph makes).</summary>
        public bool discardBuffer;

        void InitDefaultValues(bool dynamicResolution)
        {
            slices = 1;
            dimension = TextureDimension.Tex2D;
            colorFormat = GraphicsFormat.R8G8B8A8_SRGB;
            filterMode = FilterMode.Point;
            wrapMode = TextureWrapMode.Repeat;
            msaaSamples = MSAASamples.None;
            autoGenerateMips = true;
            anisoLevel = 1;
            useDynamicScale = dynamicResolution;
            clearColor = Color.black;
            name = "";
        }

        public TextureDesc(int width, int height, bool dynamicResolution = false, bool xrReady = false) : this()
        {
            InitDefaultValues(dynamicResolution);
            sizeMode = TextureSizeMode.Explicit;
            this.width = width;
            this.height = height;
        }

        public TextureDesc(Vector2 scale, bool dynamicResolution = false, bool xrReady = false) : this()
        {
            InitDefaultValues(dynamicResolution);
            sizeMode = TextureSizeMode.Scale;
            this.scale = scale;
        }

        public TextureDesc(ScaleFunc func, bool dynamicResolution = false, bool xrReady = false) : this()
        {
            InitDefaultValues(dynamicResolution);
            sizeMode = TextureSizeMode.Functor;
            this.func = func;
        }

        public TextureDesc(TextureDesc input) => this = input;

        public TextureDesc(RenderTextureDescriptor input) : this(input.width, input.height, input.useDynamicScale)
        {
            slices = input.volumeDepth;
            dimension = input.dimension;
            colorFormat = input.graphicsFormat;
            depthBufferBits = (DepthBits)input.depthBufferBits;
            enableRandomWrite = input.enableRandomWrite;
            useMipMap = input.useMipMap;
            autoGenerateMips = input.autoGenerateMips;
            msaaSamples = (MSAASamples)Mathf.Max(input.msaaSamples, 1);
            bindTextureMS = input.bindMS;
            memoryless = input.memoryless;
            vrUsage = input.vrUsage;
        }

        public TextureDesc(RTHandle input) : this(input.rt != null ? input.rt.descriptor : new RenderTextureDescriptor(1, 1))
        {
            if (input.useScaling)
            {
                sizeMode = input.scaleFunc != null ? TextureSizeMode.Functor : TextureSizeMode.Scale;
                scale = input.scaleFactor;
                func = input.scaleFunc;
            }
            if (input.rt != null)
            {
                filterMode = input.rt.filterMode;
                wrapMode = input.rt.wrapMode;
                anisoLevel = input.rt.anisoLevel;
                mipMapBias = input.rt.mipMapBias;
            }
            name = input.name;
        }

        /// <summary>The size for a reference size.</summary>
        internal Vector2Int Size(Vector2Int reference) => sizeMode switch
        {
            TextureSizeMode.Scale => new Vector2Int(Mathf.Max(Mathf.RoundToInt(scale.x * reference.x), 1), Mathf.Max(Mathf.RoundToInt(scale.y * reference.y), 1)),
            TextureSizeMode.Functor => func != null ? Vector2Int.Max(func(reference), Vector2Int.one) : reference,
            _ => new Vector2Int(Mathf.Max(width, 1), Mathf.Max(height, 1)),
        };

        public override int GetHashCode() =>
            HashCode.Combine(HashCode.Combine(sizeMode, width, height, slices, scale, depthBufferBits, colorFormat, filterMode),
                             HashCode.Combine(wrapMode, dimension, enableRandomWrite, useMipMap, autoGenerateMips, isShadowMap, msaaSamples, bindTextureMS));
    }

    /// <summary>What the graph does with an imported texture at its first and last use.</summary>
    public struct ImportResourceParams
    {
        public bool clearOnFirstUse;
        public Color clearColor;
        public bool discardOnLastUse;
    }

    /// <summary>The size and format of a target the graph imports without a texture (the backbuffer).</summary>
    public struct RenderTargetInfo
    {
        public int width;
        public int height;
        public int volumeDepth;
        public int msaaSamples;
        public GraphicsFormat format;
        public bool bindMS;
    }

    /// <summary>Textures every graph has: imported anew each frame.</summary>
    public class RenderGraphDefaultResources
    {
        public TextureHandle blackTexture { get; private set; }
        public TextureHandle whiteTexture { get; private set; }
        public TextureHandle clearTextureXR { get; private set; }
        public TextureHandle magentaTextureXR { get; private set; }
        public TextureHandle blackTextureXR { get; private set; }
        public TextureHandle blackTextureArrayXR { get; private set; }
        public TextureHandle blackUIntTextureXR { get; private set; }
        public TextureHandle blackTexture3DXR { get; private set; }
        public TextureHandle whiteTextureXR { get; private set; }
        public TextureHandle defaultShadowTexture { get; private set; }

        RTHandle m_Black, m_White, m_Clear, m_Magenta;
        Texture2D m_ClearTexture, m_MagentaTexture;

        internal void Import(RenderGraph graph)
        {
            if (m_Black == null)
            {
                m_ClearTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { name = "Clear", hideFlags = HideFlags.HideAndDontSave };
                m_ClearTexture.SetPixel(0, 0, Color.clear);
                m_ClearTexture.Apply(false);
                m_MagentaTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { name = "Magenta", hideFlags = HideFlags.HideAndDontSave };
                m_MagentaTexture.SetPixel(0, 0, Color.magenta);
                m_MagentaTexture.Apply(false);
                m_Black = RTHandles.Alloc(Texture2D.blackTexture);
                m_White = RTHandles.Alloc(Texture2D.whiteTexture);
                m_Clear = RTHandles.Alloc(m_ClearTexture);
                m_Magenta = RTHandles.Alloc(m_MagentaTexture);
            }
            blackTexture = graph.ImportTexture(m_Black);
            whiteTexture = graph.ImportTexture(m_White);
            clearTextureXR = graph.ImportTexture(m_Clear);
            magentaTextureXR = graph.ImportTexture(m_Magenta);
            blackTextureXR = blackTexture;
            blackTextureArrayXR = blackTexture;
            blackUIntTextureXR = blackTexture;
            blackTexture3DXR = blackTexture;
            whiteTextureXR = whiteTexture;
            defaultShadowTexture = whiteTexture;
        }

        internal void Cleanup()
        {
            m_Black?.Release();
            m_White?.Release();
            m_Clear?.Release();
            m_Magenta?.Release();
            m_Black = m_White = m_Clear = m_Magenta = null;
            CoreUtils.Destroy(m_ClearTexture, m_MagentaTexture);
            m_ClearTexture = m_MagentaTexture = null;
        }
    }

    /// <summary>What a frame of a graph renders with.</summary>
    public struct RenderGraphParameters
    {
        public string executionName;
        public int currentFrameIndex;
        /// <summary>Passes whose renderer lists draw nothing may be culled (lists are made at once here: none is known empty).</summary>
        public bool rendererListCulling;
        public ScriptableRenderContext scriptableRenderContext;
        public CommandBuffer commandBuffer;
        public bool invalidContextForTesting;
        public bool generateDebugData;
    }
}
