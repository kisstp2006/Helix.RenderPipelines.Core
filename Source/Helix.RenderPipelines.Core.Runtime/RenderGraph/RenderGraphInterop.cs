using HelixEngine.Rendering;
using HelixEngine.Rendering.RenderGraphModule;

namespace Helix.Interop
{
    /// <summary>
    /// The core's render graph objects by their native ids, for the C# sides of pipelines written in C++: the C# graph
    /// over a graph the pipeline's native code made (C# passes record into its frames; native code records, executes and
    /// cleans it up), and its handles as the native side packs them.
    /// </summary>
    public static class RenderGraphInterop
    {
        /// <summary>The C# graph over a native one (0: null).</summary>
        public static RenderGraph GraphOf(ulong graph) => RenderGraph.Borrow(graph);

        public static ulong IdOf(RenderGraph graph) => graph?.id ?? 0;

        public static TextureHandle TextureOf(ulong packed) => TextureHandle.Unpack(packed);

        public static ulong PackedOf(TextureHandle texture) => texture.packed;

        public static RendererListHandle RendererListOf(ulong packed) => RendererListHandle.Unpack(packed);

        public static ulong PackedOf(RendererListHandle list) => list.packed;

        /// <summary>The C# object of a native render texture handle (0: null).</summary>
        public static RTHandle HandleOf(ulong handle) => RTHandle.Of(handle);

        public static ulong IdOf(RTHandle handle) => handle?.m_Id ?? 0;

        /// <summary>The C# object of a native frame data container (0: null).</summary>
        public static ContextContainer ContainerOf(ulong container) => ContextContainer.Borrow(container);

        public static ulong IdOf(ContextContainer container) => container?.m_Id ?? 0;

        /// <summary>The command buffer a pass's command buffer records into (the graph's).</summary>
        public static CommandBuffer CommandBufferOf(BaseCommandBuffer cmd) => cmd?.m_WrappedCommandBuffer;
    }
}
