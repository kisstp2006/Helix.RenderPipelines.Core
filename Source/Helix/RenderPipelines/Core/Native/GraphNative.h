#pragma once

#include <Helix/RenderPipelines/Core/Native/TexturesNative.h>
#include <Helix/RenderPipelines/Core/RenderGraph/RenderGraph.h>

// What C#'s RenderGraph calls (internal): graphs by id, builders by pointer, texture and list handles packed as
// (execution << 32 | index). Calls that can fail answer an ErrorKind (0 when they worked) and leave the message.
namespace helix::rendering
{
    // A TextureDesc as plain values (flags: AllocData's bits, 128 clear buffer, 256 discard buffer).
    HX_STRUCT(Namespace = "HelixEngine.Rendering.Internal", Internal)
    struct TextureDescData
    {
        HX_FIELD() i32 sizeMode = 0;
        HX_FIELD() i32 width = 1;
        HX_FIELD() i32 height = 1;
        HX_FIELD() i32 slices = 1;
        HX_FIELD() Vector2 scale = Vector2(1, 1);
        HX_FIELD() i32 depthBufferBits = 0;
        HX_FIELD() i32 format = 0;
        HX_FIELD() i32 filterMode = 0;
        HX_FIELD() i32 wrapMode = 0;
        HX_FIELD() i32 dimension = 2;
        HX_FIELD() i32 flags = 4;
        HX_FIELD() i32 anisoLevel = 1;
        HX_FIELD() f32 mipMapBias = 0;
        HX_FIELD() i32 msaaSamples = 1;
        HX_FIELD() i32 memoryless = 0;
        HX_FIELD() i32 vrUsage = 0;
        HX_FIELD() Color clearColor = Color(0, 0, 0, 1);
    };

    HX_CLASS(Namespace = "HelixEngine.Rendering.Internal", Static, Internal)
    class GraphNative
    {
    public:
        HX_FUNCTION() static u64 Create(StringView name) { return (new RenderGraph(name))->Id(); }
        HX_FUNCTION() static void Destroy(u64 graph) { delete RenderGraph::Find(graph); }
        HX_FUNCTION() static void SetNativeRenderPasses(u64 graph, bool enabled);
        HX_FUNCTION() static bool NativeRenderPasses(u64 graph);
        // The message and kind of the graph's last error.
        HX_FUNCTION() static String Error(u64 graph);

        HX_FUNCTION() static i32 BeginRecording(u64 graph, u64 context, u64 buffer, i32 frameIndex, bool rendererListCulling, StringView executionName);
        HX_FUNCTION() static i32 EndRecordingAndExecute(u64 graph);
        HX_FUNCTION() static u32 Execution(u64 graph);
        // A graph native code records (a C++ pipeline's): its name, whether it records, its frame's command buffer and context, and
        // the graph executing on this thread (0: none).
        HX_FUNCTION() static String Name(u64 graph);
        HX_FUNCTION() static bool Recording(u64 graph);
        HX_FUNCTION() static u64 CommandBufferOf(u64 graph);
        // The context of its frame (0: none).
        HX_FUNCTION() static u64 ContextOf(u64 graph);
        HX_FUNCTION() static u64 ExecutingGraph();
        HX_FUNCTION() static u64 DefaultTexture(u64 graph, i32 which);

        HX_FUNCTION() static u64 ImportTexture(u64 graph, u64 handle, bool clearOnFirstUse, Color clearColor, bool discardOnLastUse);
        HX_FUNCTION() static u64 ImportBackbuffer(u64 graph, TargetData target, i32 width, i32 height, i32 volumeDepth, i32 format, bool clearOnFirstUse,
                                                  Color clearColor, bool discardOnLastUse);
        // A scale function: an unmanaged function of (user, width, height, out width, out height); the caller keeps its user for the frame.
        HX_FUNCTION() static u64 CreateTexture(u64 graph, TextureDescData desc, StringView name, u64 function, u64 user);
        HX_FUNCTION() static TextureDescData GetTextureDesc(u64 graph, u64 texture);
        HX_FUNCTION() static String TextureName(u64 graph, u64 texture);
        // The handle an imported texture came from (0 for the graph's own).
        HX_FUNCTION() static u64 ImportedHandle(u64 graph, u64 texture);
        HX_FUNCTION() static u64 ImportRendererList(u64 graph, u64 list);

        // A pass: its builder until DisposeBuilder (which answers the error of a pass without a render function).
        HX_FUNCTION() static u64 AddPass(u64 graph, StringView name, i32 kind);
        HX_FUNCTION() static i32 DisposeBuilder(u64 builder);
        HX_FUNCTION() static String BuilderError(u64 builder);
        HX_FUNCTION() static i32 UseTexture(u64 builder, u64 texture, i32 flags);
        HX_FUNCTION() static void UseGlobalTexture(u64 builder, i32 propertyId, i32 flags);
        HX_FUNCTION() static void UseAllGlobalTextures(u64 builder, bool enable);
        HX_FUNCTION() static i32 UseRendererList(u64 builder, u64 list);
        HX_FUNCTION() static void AllowPassCulling(u64 builder, bool value);
        HX_FUNCTION() static void AllowGlobalStateModification(u64 builder, bool value);
        HX_FUNCTION() static i32 SetGlobalTextureAfterPass(u64 builder, u64 texture, i32 propertyId);
        HX_FUNCTION() static u64 CreateTransientTexture(u64 builder, TextureDescData desc, StringView name, u64 function, u64 user);
        HX_FUNCTION() static i32 SetRenderAttachment(u64 builder, u64 texture, i32 index, i32 flags, i32 mipLevel, i32 depthSlice);
        HX_FUNCTION() static i32 SetInputAttachment(u64 builder, u64 texture, i32 index, i32 flags);
        HX_FUNCTION() static i32 SetRenderAttachmentDepth(u64 builder, u64 texture, i32 flags, i32 mipLevel, i32 depthSlice);
        // A C# render function: an unmanaged function of (user, command buffer id, pass kind).
        HX_FUNCTION() static void SetRenderFunc(u64 builder, u64 function, u64 user);

        HX_FUNCTION() static void EndFrame(u64 graph);
        HX_FUNCTION() static void Cleanup(u64 graph);
        HX_FUNCTION() static RenderGraphStatistics Statistics(u64 graph);

        // While a graph executes on this thread: the handle id of a texture, the engine id of a list (0: not executing or not allocated).
        HX_FUNCTION() static bool Executing(u32 execution);
        HX_FUNCTION() static u64 Resolve(u64 texture);
        HX_FUNCTION() static u64 ResolveList(u64 list);

        static u64 Pack(TextureHandle h) { return u64(h.execution) << 32 | h.index; }
        static TextureHandle Unpack(u64 v) { return {u32(v), u32(v >> 32)}; }
        static TextureDesc DescOf(RenderGraph& graph, const TextureDescData& data, StringView name, u64 function, u64 user);
        static TextureDescData DataOf(const TextureDesc& desc);
    };
}
