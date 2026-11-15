using System;
using System.Runtime.InteropServices;
using HelixEngine.Experimental.Rendering;
using HelixEngine.Rendering.Internal;
using HelixEngine.Rendering.RendererUtils;

namespace HelixEngine.Rendering.RenderGraphModule
{
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

        // The native core's handle: (execution << 32 | index).
        internal ulong packed => (ulong)(uint)m_Graph << 32 | (uint)m_Index;
        internal static TextureHandle Unpack(ulong packed) => new TextureHandle((int)(uint)packed, (int)(uint)(packed >> 32));

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

    /// <summary>A renderer list of a render graph, made when it is created.</summary>
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

        internal ulong packed => (ulong)(uint)m_Graph << 32 | (uint)m_Index;
        internal static RendererListHandle Unpack(ulong packed) => new RendererListHandle((int)(uint)packed, (int)(uint)(packed >> 32));

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

        // The native core's values; a scale function goes as RTHandleSystem's callback with a GC handle the graph frees after the frame.
        internal unsafe TextureDescData Data(RenderGraph graph, out ulong function, out ulong user)
        {
            function = 0;
            user = 0;
            if (sizeMode == TextureSizeMode.Functor && func != null)
            {
                function = (ulong)(delegate* unmanaged[Cdecl]<ulong, int, int, int*, int*, void>)&RTHandleSystem.Scale;
                user = graph.KeepForFrame(func);
            }
            return new TextureDescData
            {
                sizeMode = (int)sizeMode,
                width = width,
                height = height,
                slices = slices,
                scale = scale,
                depthBufferBits = (int)depthBufferBits,
                format = (int)colorFormat,
                filterMode = (int)filterMode,
                wrapMode = (int)wrapMode,
                dimension = (int)dimension,
                flags = (enableRandomWrite ? 1 : 0) | (useMipMap ? 2 : 0) | (autoGenerateMips ? 4 : 0) | (isShadowMap ? 8 : 0) | (bindTextureMS ? 16 : 0) |
                        (useDynamicScale ? 32 : 0) | (clearBuffer ? 128 : 0) | (discardBuffer ? 256 : 0),
                anisoLevel = anisoLevel,
                mipMapBias = mipMapBias,
                msaaSamples = (int)msaaSamples,
                memoryless = (int)memoryless,
                vrUsage = (int)vrUsage,
                clearColor = clearColor,
            };
        }

        internal static TextureDesc Of(in TextureDescData d, string name) => new TextureDesc
        {
            sizeMode = (TextureSizeMode)d.sizeMode,
            width = d.width,
            height = d.height,
            slices = d.slices,
            scale = d.scale,
            depthBufferBits = (DepthBits)d.depthBufferBits,
            colorFormat = (GraphicsFormat)d.format,
            filterMode = (FilterMode)d.filterMode,
            wrapMode = (TextureWrapMode)d.wrapMode,
            dimension = (TextureDimension)d.dimension,
            enableRandomWrite = (d.flags & 1) != 0,
            useMipMap = (d.flags & 2) != 0,
            autoGenerateMips = (d.flags & 4) != 0,
            isShadowMap = (d.flags & 8) != 0,
            bindTextureMS = (d.flags & 16) != 0,
            useDynamicScale = (d.flags & 32) != 0,
            clearBuffer = (d.flags & 128) != 0,
            discardBuffer = (d.flags & 256) != 0,
            anisoLevel = d.anisoLevel,
            mipMapBias = d.mipMapBias,
            msaaSamples = (MSAASamples)d.msaaSamples,
            memoryless = (RenderTextureMemoryless)d.memoryless,
            vrUsage = (VRTextureUsage)d.vrUsage,
            clearColor = d.clearColor,
            name = name,
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

        // The graph's own textures, imported anew each frame by the native core.
        internal void Import(RenderGraph graph)
        {
            ulong id = graph.id;
            TextureHandle Of(int which) => TextureHandle.Unpack(GraphNative.DefaultTexture(id, which));
            blackTexture = Of(0);
            whiteTexture = Of(1);
            clearTextureXR = Of(2);
            magentaTextureXR = Of(3);
            blackTextureXR = Of(4);
            blackTextureArrayXR = Of(5);
            blackUIntTextureXR = Of(6);
            blackTexture3DXR = Of(7);
            whiteTextureXR = Of(8);
            defaultShadowTexture = Of(9);
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
