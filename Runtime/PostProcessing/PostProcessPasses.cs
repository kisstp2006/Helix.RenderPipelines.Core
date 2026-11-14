using System;
using System.Collections.Generic;
using HelixEngine.Rendering.RenderGraphModule;

namespace HelixEngine.Rendering
{
    /// <summary>Where in a pipeline's frame post process components run.</summary>
    public enum PostProcessInjectionPoint
    {
        AfterOpaqueAndSky,
        BeforePostProcess,
        AfterPostProcess,
    }

    /// <summary>A volume component of an effect: active when its parameters make it do something.</summary>
    public interface IPostProcessComponent
    {
        bool IsActive();
    }

    /// <summary>
    /// An effect driven by a volume component: the stack's blended instance renders it, from the frame's color into the
    /// bound target, at its injection point.
    /// </summary>
    public abstract class PostProcessVolumeComponent : VolumeComponent, IPostProcessComponent
    {
        bool m_SetUp;

        public abstract bool IsActive();

        public virtual PostProcessInjectionPoint injectionPoint => PostProcessInjectionPoint.AfterPostProcess;

        /// <summary>Once before the first Render (materials, look up tables).</summary>
        public virtual void Setup() { }

        /// <summary>The effect from source into the bound target (a full screen draw).</summary>
        public abstract void Render(RasterCommandBuffer cmd, RTHandle source);

        /// <summary>What Setup made goes.</summary>
        public virtual void Cleanup() { }

        internal void EnsureSetup()
        {
            if (m_SetUp)
                return;
            Setup();
            m_SetUp = true;
        }

        internal void CleanupIfSetUp()
        {
            if (!m_SetUp)
                return;
            Cleanup();
            m_SetUp = false;
        }

        protected override void OnDisable()
        {
            CleanupIfSetUp();
            base.OnDisable();
        }
    }

    /// <summary>
    /// The post process components of a stack recorded into a render graph: at a point, each active one in order as a
    /// raster pass reading the previous result, alternating between two textures of the color's descriptor.
    /// </summary>
    public sealed class PostProcessPasses : IDisposable
    {
        readonly List<Type> m_Order = new List<Type>();
        readonly HashSet<PostProcessVolumeComponent> m_SetUp = new HashSet<PostProcessVolumeComponent>();

        class PassData
        {
            public PostProcessVolumeComponent component;
            public TextureHandle source;
        }

        /// <summary>The component types in the order they run (types not listed run after them, by name).</summary>
        public List<Type> order => m_Order;

        // The components of a point that run, in order.
        public List<PostProcessVolumeComponent> ActiveComponents(VolumeStack stack, PostProcessInjectionPoint point)
        {
            var list = new List<PostProcessVolumeComponent>();
            if (stack == null)
                return list;
            foreach (VolumeComponent c in stack.components.Values)
                if (c is PostProcessVolumeComponent p && p.injectionPoint == point && p.active && p.IsActive())
                    list.Add(p);
            list.Sort((a, b) =>
            {
                int ia = m_Order.IndexOf(a.GetType()), ib = m_Order.IndexOf(b.GetType());
                ia = ia < 0 ? int.MaxValue : ia;
                ib = ib < 0 ? int.MaxValue : ib;
                return ia != ib ? ia.CompareTo(ib) : string.CompareOrdinal(a.GetType().FullName, b.GetType().FullName);
            });
            return list;
        }

        /// <summary>The passes of a point; the texture holding the result (color itself when no component runs).</summary>
        public TextureHandle Record(RenderGraph renderGraph, VolumeStack stack, PostProcessInjectionPoint point, TextureHandle color)
        {
            List<PostProcessVolumeComponent> components = ActiveComponents(stack, point);
            if (components.Count == 0)
                return color;
            TextureDesc desc = renderGraph.GetTextureDesc(color);
            desc.clearBuffer = false;
            desc.depthBufferBits = DepthBits.None;
            desc.name = "PostProcess";
            TextureHandle[] targets = { renderGraph.CreateTexture(desc), renderGraph.CreateTexture(desc) };
            TextureHandle source = color;
            for (int i = 0; i < components.Count; i++)
            {
                PostProcessVolumeComponent component = components[i];
                component.EnsureSetup();
                m_SetUp.Add(component);
                TextureHandle target = targets[i & 1];
                using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(component.GetType().Name, out PassData data))
                {
                    data.component = component;
                    data.source = source;
                    builder.UseTexture(source);
                    builder.SetRenderAttachment(target, 0, AccessFlags.WriteAll);
                    builder.SetRenderFunc((PassData d, RasterGraphContext ctx) => d.component.Render(ctx.cmd, d.source));
                }
                source = target;
            }
            return source;
        }

        public void Dispose()
        {
            foreach (PostProcessVolumeComponent c in m_SetUp)
                if (c != null)
                    c.CleanupIfSetUp();
            m_SetUp.Clear();
        }
    }
}
