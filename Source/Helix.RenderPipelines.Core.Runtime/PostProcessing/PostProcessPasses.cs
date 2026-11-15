using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HelixEngine.Rendering.Internal;
using HelixEngine.Rendering.RenderGraphModule;

namespace HelixEngine.Rendering
{
    /// <summary>A volume component of an effect: active when its parameters make it do something.</summary>
    public interface IPostProcessComponent
    {
        bool IsActive();
    }

    /// <summary>
    /// An effect driven by a volume component: the stack's blended instance renders it, from the frame's color into the
    /// bound target, at its injection point. The native core's component: C# effects override IsActive, injectionPoint,
    /// Setup, Render and Cleanup; a C++ effect's Render is its C++ one.
    /// </summary>
    public partial class PostProcessVolumeComponent : IPostProcessComponent
    {
        static bool s_RenderSet;

        /// <summary>The effect from source into the bound target (a full screen draw).</summary>
        public virtual void Render(RasterCommandBuffer cmd, RTHandle source)
        {
            if (!((global::Helix.Interop.INativeObject)this).HxDerived && cmd != null)
                PostNative.Render(this, cmd.m_WrappedCommandBuffer.Id, source != null ? source.m_Id : 0);
        }

        protected override void OnDisable()
        {
            PostNative.CleanupIfSetUp(this);
            base.OnDisable();
        }

        internal static unsafe void EnsureRender()
        {
            if (s_RenderSet)
                return;
            s_RenderSet = true;
            PostNative.SetManagedRender((ulong)(delegate* unmanaged[Cdecl]<ulong, ulong, ulong, void>)&RenderManaged);
        }

        // The C++ passes' call of a C# component's Render: the raster commands of the C# graph executing the frame; an
        // exception waits for the graph's EndRecordingAndExecute.
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void RenderManaged(ulong wrapper, ulong commandBuffer, ulong source)
        {
            try
            {
                var component = (PostProcessVolumeComponent)GCHandle.FromIntPtr((IntPtr)wrapper).Target;
                RenderGraph graph = RenderGraph.s_Executing;
                RasterCommandBuffer cmd = graph?.m_RasterCommands;
                if (cmd == null || cmd.m_WrappedCommandBuffer.Id != commandBuffer)
                    throw new InvalidOperationException($"{component.GetType().Name}.Render: a C# post process component renders in the frame of a C# render graph");
                component.Render(cmd, RTHandle.Of(source));
            }
            catch (Exception e)
            {
                RenderGraph.s_Failure ??= e;
            }
        }
    }

    /// <summary>
    /// The post process components of a stack recorded into a render graph: at a point, each active one in order as a
    /// raster pass reading the previous result, alternating between two textures of the color's descriptor. The native
    /// core's passes by id.
    /// </summary>
    public sealed class PostProcessPasses : IDisposable
    {
        readonly List<Type> m_Order = new List<Type>();
        ulong m_Id;

        /// <summary>The component types in the order they run (types not listed run after them, by name).</summary>
        public List<Type> order => m_Order;

        ulong id
        {
            get
            {
                if (m_Id == 0)
                    m_Id = PostNative.CreatePasses();
                var types = new List<uint>(m_Order.Count);
                foreach (Type t in m_Order)
                    if (t != null)
                        types.Add(global::Helix.Interop.Types.Of(t));
                PostNative.SetOrder(m_Id, types.ToArray());
                return m_Id;
            }
        }

        // The components of a point that run, in order.
        public List<PostProcessVolumeComponent> ActiveComponents(VolumeStack stack, PostProcessInjectionPoint point)
        {
            var list = new List<PostProcessVolumeComponent>();
            if (stack == null)
                return list;
            int count = PostNative.ActiveComponents(id, stack.m_Id, (int)point);
            for (int i = 0; i < count; i++)
                list.Add(PostNative.ActiveAt(i));
            return list;
        }

        /// <summary>The passes of a point; the texture holding the result (color itself when no component runs).</summary>
        public TextureHandle Record(RenderGraph renderGraph, VolumeStack stack, PostProcessInjectionPoint point, TextureHandle color)
        {
            if (renderGraph == null)
                throw new ArgumentNullException(nameof(renderGraph));
            renderGraph.CheckRecording(nameof(Record));
            PostProcessVolumeComponent.EnsureRender();
            return TextureHandle.Unpack(PostNative.Record(id, renderGraph.id, stack != null ? stack.m_Id : 0, (int)point, color.packed));
        }

        /// <summary>The components it set up are cleaned up.</summary>
        public void Dispose()
        {
            if (m_Id == 0)
                return;
            PostNative.DestroyPasses(m_Id);
            m_Id = 0;
        }
    }
}
