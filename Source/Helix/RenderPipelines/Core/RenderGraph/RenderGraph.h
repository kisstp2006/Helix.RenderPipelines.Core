#pragma once

#include <Helix/Core/Containers/Array.h>
#include <Helix/Core/Strings/String.h>
#include <Helix/Engine/Rendering/GraphicsBuffer.h>
#include <Helix/Engine/Rendering/Pipeline/CommandBuffer.h>
#include <Helix/Engine/Rendering/Pipeline/RendererList.h>
#include <Helix/Engine/Rendering/Pipeline/ScriptableRenderContext.h>
#include <Helix/RenderPipelines/Core/Textures/RTHandle.h>

#include <type_traits>
#include <utility>

// A frame's passes recorded with the textures, buffers and renderer lists they use, then compiled and executed: passes
// whose results nothing uses are culled, the textures and buffers the graph makes live from their first use to their
// last and share pooled ones of the same kind, consecutive raster passes with the same attachments run as one render pass, and
// every pass records into the frame's command buffer.
namespace helix::rendering
{
    using engine::CommandBuffer;
    using engine::ScriptableRenderContext;

    // How a pass uses a resource. Write keeps what was there (it is loaded); WriteAll replaces all of it.
    HX_ENUM(Namespace = "HelixEngine.Rendering.RenderGraphModule", Attributes = "[System.Flags]")
    enum class AccessFlags : i32
    {
        None = 0,
        Read = 1,
        Write = 2,
        Discard = 4,
        WriteAll = 6,
        ReadWrite = 3,
    };
    constexpr AccessFlags operator|(AccessFlags a, AccessFlags b) { return AccessFlags(i32(a) | i32(b)); }
    constexpr bool Has(AccessFlags flags, AccessFlags bit) { return (i32(flags) & i32(bit)) != 0; }

    HX_ENUM(Namespace = "HelixEngine.Rendering.RenderGraphModule")
    enum class TextureSizeMode : i32
    {
        Explicit,
        Scale,
        Functor,
    };

    // A texture of a frame of a graph (the index of the frame's textures and the execution it was made in).
    struct TextureHandle
    {
        u32 index = 0;
        u32 execution = 0;
        bool IsValid() const { return index != 0; }
        bool operator==(const TextureHandle&) const = default;
        static TextureHandle nullHandle() { return {}; }
    };

    // A graphics buffer of a frame of a graph.
    struct BufferHandle
    {
        u32 index = 0;
        u32 execution = 0;
        bool IsValid() const { return index != 0; }
        bool operator==(const BufferHandle&) const = default;
        static BufferHandle nullHandle() { return {}; }
    };

    // A buffer the graph makes: count elements of stride bytes for a target.
    struct BufferDesc
    {
        i32 count = 1, stride = 4;
        engine::GraphicsBufferTarget target = engine::GraphicsBufferTarget::Structured;
        i32 usageFlags = 0;
        String name = String();

        BufferDesc() = default;
        BufferDesc(i32 c, i32 s) : count(c), stride(s) {}
        BufferDesc(i32 c, i32 s, engine::GraphicsBufferTarget t) : count(c), stride(s), target(t) {}
    };

    struct RendererListHandle
    {
        u32 index = 0;
        u32 execution = 0;
        bool IsValid() const { return index != 0; }
        bool operator==(const RendererListHandle&) const = default;
    };

    // A texture the graph makes: its size (explicit, a scale of the reference size or a function of it) and format.
    struct TextureDesc
    {
        TextureSizeMode sizeMode = TextureSizeMode::Explicit;
        i32 width = 1, height = 1, slices = 1;
        Vector2 scale = Vector2(1, 1);
        ScaleFunc func = nullptr;
        void* funcUser = nullptr;
        DepthBits depthBufferBits = DepthBits::None;
        GraphicsFormat colorFormat = GraphicsFormat::R8G8B8A8_SRGB;
        engine::FilterMode filterMode = engine::FilterMode::Point;
        engine::TextureWrapMode wrapMode = engine::TextureWrapMode::Repeat;
        engine::TextureDimension dimension = engine::TextureDimension::Tex2D;
        bool enableRandomWrite = false, useMipMap = false, autoGenerateMips = true, isShadowMap = false;
        i32 anisoLevel = 1;
        f32 mipMapBias = 0;
        MSAASamples msaaSamples = MSAASamples::None;
        bool bindTextureMS = false, useDynamicScale = false;
        engine::RenderTextureMemoryless memoryless = engine::RenderTextureMemoryless::None;
        engine::VRTextureUsage vrUsage = engine::VRTextureUsage::None;
        String name = String();
        // Cleared to clearColor at its first use in a frame.
        bool clearBuffer = false;
        Color clearColor = Color(0, 0, 0, 1);
        bool discardBuffer = false;

        TextureDesc() = default;
        TextureDesc(i32 w, i32 h) : width(w), height(h) {}
        explicit TextureDesc(Vector2 s) : sizeMode(TextureSizeMode::Scale), scale(s) {}
        TextureDesc(ScaleFunc f, void* user) : sizeMode(TextureSizeMode::Functor), func(f), funcUser(user) {}
        explicit TextureDesc(const RTHandle& handle);

        // The size for a reference size.
        Vector2Int Size(Vector2Int reference) const;
    };

    // What the graph does with an imported texture at its first and last use.
    struct ImportResourceParams
    {
        bool clearOnFirstUse = false;
        Color clearColor = Color(0, 0, 0, 0);
        bool discardOnLastUse = false;
    };

    // The size and format of a target imported without a texture (the backbuffer).
    struct RenderTargetInfo
    {
        i32 width = 0, height = 0, volumeDepth = 1, msaaSamples = 1;
        GraphicsFormat format = GraphicsFormat::None;
        bool bindMS = false;
    };

    // What a frame of a graph renders with.
    struct RenderGraphParameters
    {
        String executionName = String();
        i32 currentFrameIndex = 0;
        // Passes whose renderer lists draw nothing may be culled (lists are made at once here: none is known empty).
        bool rendererListCulling = false;
        ScriptableRenderContext* scriptableRenderContext = nullptr;
        CommandBuffer* commandBuffer = nullptr;
        bool generateDebugData = false;
    };

    // The counts of a frame of a graph (the tests and the debug display read them).
    HX_STRUCT(Namespace = "HelixEngine.Rendering.RenderGraphModule", Internal)
    struct RenderGraphStatistics
    {
        HX_FIELD() i32 passes = 0;
        HX_FIELD() i32 culledPasses = 0;
        HX_FIELD() i32 renderPasses = 0;
        HX_FIELD() i32 mergedPasses = 0;
        HX_FIELD() i32 textures = 0;
        HX_FIELD() i32 pooledTexturesCreated = 0;
        HX_FIELD() i32 aliasedTextures = 0;
        HX_FIELD() i32 pooledTextures = 0;
        HX_FIELD() i32 buffers = 0;
        HX_FIELD() i32 pooledBuffersCreated = 0;
        HX_FIELD() i32 pooledBuffers = 0;
    };

    class RenderGraph;

    // What kind of misuse an error is (C# throws ArgumentException, InvalidOperationException, ArgumentOutOfRangeException).
    enum class ErrorKind : u8
    {
        None,
        Argument,
        InvalidOperation,
        OutOfRange,
    };

    // Textures every graph has: imported anew each frame.
    struct RenderGraphDefaultResources
    {
        TextureHandle blackTexture, whiteTexture, clearTextureXR, magentaTextureXR, blackTextureXR, blackTextureArrayXR, blackUIntTextureXR, blackTexture3DXR,
            whiteTextureXR, defaultShadowTexture;
    };

    enum class PassKind : u8
    {
        Raster,
        Compute,
        Unsafe,
    };

    // What a render function records into: the frame's command buffer and the graph's default textures.
    struct RasterGraphContext
    {
        CommandBuffer* cmd = nullptr;
        const RenderGraphDefaultResources* defaultResources = nullptr;
    };
    struct ComputeGraphContext
    {
        CommandBuffer* cmd = nullptr;
        const RenderGraphDefaultResources* defaultResources = nullptr;
    };
    struct UnsafeGraphContext
    {
        CommandBuffer* cmd = nullptr;
        const RenderGraphDefaultResources* defaultResources = nullptr;
    };

    namespace detail
    {
        // A pass's data and its render function, kept for the frame.
        struct PassCallable
        {
            virtual ~PassCallable() = default;
            virtual void Run(PassKind kind, CommandBuffer& cmd, const RenderGraphDefaultResources& resources) = 0;
        };

        // f(Data&, RasterGraphContext&), (Data&, ComputeGraphContext&) or (Data&, UnsafeGraphContext&).
        template <class Data, class F>
        struct TypedCallable final : PassCallable
        {
            Data* data;
            F f;
            TypedCallable(Data* d, F&& fn) : data(d), f(std::forward<F>(fn)) {}
            void Run(PassKind, CommandBuffer& cmd, const RenderGraphDefaultResources& resources) override
            {
                if constexpr (std::is_invocable_v<F&, Data&, RasterGraphContext&>)
                {
                    RasterGraphContext ctx{&cmd, &resources};
                    f(*data, ctx);
                }
                else if constexpr (std::is_invocable_v<F&, Data&, ComputeGraphContext&>)
                {
                    ComputeGraphContext ctx{&cmd, &resources};
                    f(*data, ctx);
                }
                else
                {
                    UnsafeGraphContext ctx{&cmd, &resources};
                    f(*data, ctx);
                }
            }
        };

        // A render function of C#: an unmanaged function of (user, command buffer id, pass kind).
        struct ManagedCallable final : PassCallable
        {
            u64 function, user;
            ManagedCallable(u64 fn, u64 u) : function(fn), user(u) {}
            void Run(PassKind kind, CommandBuffer& cmd, const RenderGraphDefaultResources&) override
            {
                reinterpret_cast<void (*)(u64, u64, i32)>(function)(user, cmd.Id(), i32(kind));
            }
        };
    }

    // What every kind of pass declares; the pass is recorded when the builder goes (or EndPass).
    class RenderGraphBuilder
    {
    public:
        RenderGraphBuilder(RenderGraph& graph, u32 pass) : graph_(&graph), pass_(pass) {}
        RenderGraphBuilder(RenderGraphBuilder&& o) noexcept : graph_(o.graph_), pass_(o.pass_) { o.graph_ = nullptr; }
        RenderGraphBuilder(const RenderGraphBuilder&) = delete;
        RenderGraphBuilder& operator=(const RenderGraphBuilder&) = delete;
        ~RenderGraphBuilder() { Dispose(); }
        void Dispose();

        void UseTexture(TextureHandle texture, AccessFlags flags = AccessFlags::Read);
        void UseBuffer(BufferHandle buffer, AccessFlags flags = AccessFlags::Read);
        // A buffer of this pass only.
        BufferHandle CreateTransientBuffer(const BufferDesc& desc);
        BufferHandle CreateTransientBuffer(BufferHandle buffer);
        void UseGlobalTexture(i32 propertyId, AccessFlags flags = AccessFlags::Read);
        void UseAllGlobalTextures(bool enable);
        void UseRendererList(RendererListHandle list);
        // False: the pass runs even when nothing uses what it writes.
        void AllowPassCulling(bool value);
        // True: the pass may change global state (it is never culled).
        void AllowGlobalStateModification(bool value);
        void SetGlobalTextureAfterPass(TextureHandle texture, i32 propertyId);
        TextureHandle CreateTransientTexture(const TextureDesc& desc);
        TextureHandle CreateTransientTexture(TextureHandle texture);

        // Raster passes: the graph binds the attachments.
        void SetRenderAttachment(TextureHandle texture, i32 index, AccessFlags flags = AccessFlags::Write, i32 mipLevel = 0, i32 depthSlice = -1);
        void SetInputAttachment(TextureHandle texture, i32 index, AccessFlags flags = AccessFlags::Read);
        void SetRenderAttachmentDepth(TextureHandle texture, AccessFlags flags = AccessFlags::Write, i32 mipLevel = 0, i32 depthSlice = -1);
        // Compute passes: the devices have one queue (in order with the others).
        void EnableAsyncCompute(bool) {}

        // A C# render function.
        void SetManagedRenderFunc(u64 function, u64 user);

        // The error of the last call (empty when it worked), for C#.
        StringView error() const;
        ErrorKind errorKind() const;

    protected:
        void SetCallable(detail::PassCallable* callable);
        friend class RenderGraph;
        friend class GraphNative;
        RenderGraph* graph_;
        u32 pass_;
    };

    // The builder of a pass with data: its render function takes the data and the pass's context.
    template <class Data>
    class PassBuilder : public RenderGraphBuilder
    {
    public:
        PassBuilder(RenderGraph& graph, u32 pass, Data* data) : RenderGraphBuilder(graph, pass), data_(data) {}

        // f(Data&, RasterGraphContext&) (ComputeGraphContext, UnsafeGraphContext for those passes).
        template <class F>
        void SetRenderFunc(F&& f)
        {
            SetCallable(new detail::TypedCallable<Data, std::decay_t<F>>(data_, std::decay_t<F>(std::forward<F>(f))));
        }

    private:
        Data* data_;
    };

    class RenderGraph
    {
    public:
        static constexpr i32 kMaxMRTCount = 8;

        explicit RenderGraph(StringView name = "RenderGraph");
        ~RenderGraph();
        RenderGraph(const RenderGraph&) = delete;
        RenderGraph& operator=(const RenderGraph&) = delete;

        static RenderGraph* Find(u64 id);
        u64 Id() const { return id_; }

        StringView name() const { return name_; }
        // Consecutive raster passes with the same attachments run as one native render pass (subpasses); off: one
        // target bind per group.
        bool nativeRenderPassesEnabled = false;
        const RenderGraphDefaultResources& defaultResources() const { return defaults_; }
        // The viewer is editor work: never active in a player.
        static bool isRenderGraphViewerActive() { return false; }

        // False with the error when the parameters cannot record (no command buffer, already recording).
        bool BeginRecording(const RenderGraphParameters& parameters);
        bool EndRecordingAndExecute();
        bool recording() const { return recording_; }
        // The parameters of the frame being recorded or executed.
        const RenderGraphParameters& parameters() const;
        u32 execution() const { return execution_; }

        TextureHandle ImportTexture(RTHandle* handle, const ImportResourceParams& params = {});
        TextureHandle ImportBackbuffer(const RenderTargetIdentifier& target, const RenderTargetInfo& info = {}, const ImportResourceParams& params = {});
        TextureHandle CreateTexture(const TextureDesc& desc);
        TextureHandle CreateTexture(TextureHandle texture) { return CreateTexture(GetTextureDesc(texture)); }
        const TextureDesc& GetTextureDesc(TextureHandle texture) const;
        // A buffer kept by the caller (it outlives the frame), or one the graph makes for the passes that use it.
        BufferHandle ImportBuffer(engine::GraphicsBuffer* buffer);
        BufferHandle CreateBuffer(const BufferDesc& desc);
        BufferHandle CreateBuffer(BufferHandle buffer) { return CreateBuffer(GetBufferDesc(buffer)); }
        const BufferDesc& GetBufferDesc(BufferHandle buffer) const;
        RendererListHandle CreateRendererList(const engine::RendererUtils::RendererListDesc& desc);
        RendererListHandle CreateRendererList(const engine::RendererListParams& desc);
        // The skybox of RenderSettings around the camera (an empty list without one).
        RendererListHandle CreateSkyboxRendererList(engine::Camera* camera);
        // The shadow casters of a visible light's split (an empty list before CullShadowCasters).
        RendererListHandle CreateShadowRendererList(const engine::ShadowDrawingSettings& settings);
        // A list the context already made (C#'s).
        RendererListHandle ImportRendererList(engine::RendererUtils::RendererList list);

        template <class Data>
        PassBuilder<Data> AddRasterRenderPass(StringView name, Data*& data) { return AddTyped(name, PassKind::Raster, data); }
        template <class Data>
        PassBuilder<Data> AddComputePass(StringView name, Data*& data) { return AddTyped(name, PassKind::Compute, data); }
        template <class Data>
        PassBuilder<Data> AddUnsafePass(StringView name, Data*& data) { return AddTyped(name, PassKind::Unsafe, data); }
        // A pass whose data the caller keeps (C#'s).
        RenderGraphBuilder AddPass(StringView name, PassKind kind);

        // Pooled textures unused for some frames are released.
        void EndFrame();
        void Cleanup();

        // While executing: the texture of a handle and the list of a handle (null when not executing or not allocated).
        RTHandle* Resolve(TextureHandle texture) const;
        engine::GraphicsBuffer* Resolve(BufferHandle buffer) const;
        engine::RendererUtils::RendererList ResolveList(RendererListHandle list) const;
        bool executing() const { return executing_; }
        // The graph executing on this thread (null outside EndRecordingAndExecute).
        static RenderGraph* Executing();
        usize TextureCount() const;
        // The handle an imported texture of this frame came from.
        RTHandle* ImportedOf(TextureHandle texture) const;

        const RenderGraphStatistics& statistics() const { return stats_; }
        // The error of the last call that failed (empty after one that worked).
        StringView error() const { return error_; }
        ErrorKind errorKind() const { return errorKind_; }

        struct Impl;

    private:
        friend class RenderGraphBuilder;
        friend class GraphNative;
        template <class Data>
        PassBuilder<Data> AddTyped(StringView name, PassKind kind, Data*& data);
        u32 BeginPass(StringView name, PassKind kind);
        void KeepData(void* data, void (*destroy)(void*));
        u32 CheckTexture(TextureHandle texture);
        u32 CheckBuffer(BufferHandle buffer);
        void ImportDefaults();
        void SetError(ErrorKind kind, String message)
        {
            errorKind_ = kind;
            error_ = std::move(message);
        }
        template <class R>
        R Fail(ErrorKind kind, String message, R result)
        {
            SetError(kind, std::move(message));
            return result;
        }

        u64 id_ = 0;
        String name_;
        Impl* impl_;
        RenderGraphDefaultResources defaults_{};
        RenderGraphStatistics stats_{};
        bool recording_ = false, executing_ = false;
        u32 execution_ = 0;
        String error_ = String();
        ErrorKind errorKind_ = ErrorKind::None;
    };

    template <class Data>
    PassBuilder<Data> RenderGraph::AddTyped(StringView name, PassKind kind, Data*& data)
    {
        data = new Data();
        KeepData(data, [](void* p) { delete static_cast<Data*>(p); });
        return PassBuilder<Data>(*this, BeginPass(name, kind), data);
    }
}
