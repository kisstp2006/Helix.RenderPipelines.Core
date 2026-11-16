using System;
using HelixEngine.Rendering.Internal;

namespace HelixEngine.Rendering.RenderGraphModule
{
    /// <summary>A graphics buffer of a render graph's frame.</summary>
    public struct BufferHandle : IEquatable<BufferHandle>
    {
        internal int m_Index;
        internal int m_Graph;

        internal BufferHandle(int index, int graph)
        {
            m_Index = index;
            m_Graph = graph;
        }

        internal ulong packed => (ulong)(uint)m_Graph << 32 | (uint)m_Index;
        internal static BufferHandle Unpack(ulong packed) => new BufferHandle((int)(uint)packed, (int)(uint)(packed >> 32));

        public static BufferHandle nullHandle => default;

        public bool IsValid() => m_Index != 0;

        /// <summary>The buffer of the handle; only in the render function of a pass that uses it.</summary>
        public static implicit operator GraphicsBuffer(BufferHandle buffer) => buffer.IsValid() ? RenderGraph.Resolve(buffer) : null;

        public bool Equals(BufferHandle other) => m_Index == other.m_Index && m_Graph == other.m_Graph;
        public override bool Equals(object obj) => obj is BufferHandle other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(m_Index, m_Graph);
        public static bool operator ==(BufferHandle a, BufferHandle b) => a.Equals(b);
        public static bool operator !=(BufferHandle a, BufferHandle b) => !a.Equals(b);
    }

    /// <summary>A buffer the graph makes: count elements of stride bytes for a target.</summary>
    public struct BufferDesc
    {
        public int count;
        public int stride;
        public string name;
        public GraphicsBuffer.Target target;
        public GraphicsBuffer.UsageFlags usageFlags;

        public BufferDesc(int count, int stride)
        {
            this = default;
            this.count = count;
            this.stride = stride;
            target = GraphicsBuffer.Target.Structured;
            usageFlags = GraphicsBuffer.UsageFlags.None;
        }

        public BufferDesc(int count, int stride, GraphicsBuffer.Target target)
        {
            this = default;
            this.count = count;
            this.stride = stride;
            this.target = target;
            usageFlags = GraphicsBuffer.UsageFlags.None;
        }

        internal BufferDescData Data() => new BufferDescData { count = count, stride = stride, target = (int)target, usageFlags = (int)usageFlags };

        internal static BufferDesc Of(BufferDescData d, string name) =>
            new BufferDesc(d.count, d.stride, (GraphicsBuffer.Target)d.target) { usageFlags = (GraphicsBuffer.UsageFlags)d.usageFlags, name = name };
    }

    public partial class RenderGraph
    {
        /// <summary>A buffer the caller keeps (it outlives the frame).</summary>
        public BufferHandle ImportBuffer(GraphicsBuffer graphicsBuffer, bool forceRelease = false)
        {
            CheckRecording(nameof(ImportBuffer));
            if (graphicsBuffer == null)
                throw new ArgumentNullException(nameof(graphicsBuffer));
            return BufferHandle.Unpack(GraphNative.ImportBuffer(id, global::Helix.Interop.RenderingInterop.IdOf(graphicsBuffer)));
        }

        public BufferHandle CreateBuffer(in BufferDesc desc)
        {
            CheckRecording(nameof(CreateBuffer));
            if (desc.count <= 0 || desc.stride <= 0)
                throw new ArgumentException($"RenderGraph.CreateBuffer: count ({desc.count}) and stride ({desc.stride}) must be greater than 0.");
            return BufferHandle.Unpack(GraphNative.CreateBuffer(id, desc.Data(), desc.name ?? ""));
        }

        public BufferHandle CreateBuffer(in BufferHandle graphicsBuffer) => CreateBuffer(GetBufferDesc(graphicsBuffer));

        public BufferDesc GetBufferDesc(in BufferHandle graphicsBuffer)
        {
            CheckBuffer(graphicsBuffer, "GetBufferDesc");
            return BufferDesc.Of(GraphNative.GetBufferDesc(id, graphicsBuffer.packed), GraphNative.BufferName(id, graphicsBuffer.packed));
        }

        internal void CheckBuffer(in BufferHandle handle, string pass)
        {
            if (!handle.IsValid() || handle.m_Graph != m_Execution)
                throw new ArgumentException($"Pass {pass}: the buffer handle is not valid (null, or made in another frame or graph).");
        }

        internal static GraphicsBuffer Resolve(BufferHandle handle)
        {
            ulong b = GraphNative.ResolveBuffer(handle.packed);
            if (b == 0)
            {
                if (!GraphNative.Executing((uint)handle.m_Graph))
                    throw new InvalidOperationException("A render graph buffer can only be read in the render function of a pass of its frame.");
                throw new InvalidOperationException("The render graph buffer is not allocated: only the passes that declared it may read it.");
            }
            return global::Helix.Interop.RenderingInterop.BorrowGraphicsBuffer(b);
        }
    }

    public partial interface IBaseRenderGraphBuilder
    {
        BufferHandle UseBuffer(in BufferHandle input, AccessFlags flags = AccessFlags.Read);
        /// <summary>A buffer only this pass uses.</summary>
        BufferHandle CreateTransientBuffer(in BufferDesc desc);
        BufferHandle CreateTransientBuffer(in BufferHandle computebuffer);
    }

    internal sealed partial class RenderGraphBuilder
    {
        public BufferHandle UseBuffer(in BufferHandle input, AccessFlags flags = AccessFlags.Read)
        {
            CheckOpen();
            m_Graph.CheckBuffer(input, passName);
            Check(GraphNative.UseBuffer(m_Ptr, input.packed, (int)flags));
            return input;
        }

        public BufferHandle CreateTransientBuffer(in BufferDesc desc)
        {
            CheckOpen();
            if (desc.count <= 0 || desc.stride <= 0)
                throw new ArgumentException($"Pass {passName}: a buffer's count ({desc.count}) and stride ({desc.stride}) must be greater than 0.");
            return BufferHandle.Unpack(GraphNative.CreateTransientBuffer(m_Ptr, desc.Data(), desc.name ?? ""));
        }

        public BufferHandle CreateTransientBuffer(in BufferHandle computebuffer) => CreateTransientBuffer(m_Graph.GetBufferDesc(computebuffer));
    }
}
