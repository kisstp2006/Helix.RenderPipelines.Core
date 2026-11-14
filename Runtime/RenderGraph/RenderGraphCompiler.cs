using System.Collections.Generic;
using Helix.Collections;

namespace HelixEngine.Rendering.RenderGraphModule
{
    public partial class RenderGraph
    {
        // A use that reads what was there: reads, and writes that keep it (Write without Discard loads it).
        static bool Reads(AccessFlags flags) => (flags & AccessFlags.Read) != 0 || ((flags & AccessFlags.Write) != 0 && (flags & AccessFlags.Discard) == 0);
        static bool Writes(AccessFlags flags) => (flags & AccessFlags.Write) != 0;

        void Compile()
        {
            var stats = new RenderGraphStatistics { passes = m_Passes.Count };
            int count = m_Passes.Count;

            // Culling, from the last pass back: a pass runs when it may not be culled, changes global state, or writes
            // what a later running pass reads, an imported texture keeps, or a global a later pass reads.
            var needed = new bool[m_Textures.Count];
            for (int t = 1; t < m_Textures.Count; t++)
                needed[t] = m_Textures[t].isImported && !m_Textures[t].import.discardOnLastUse;
            var neededGlobals = new HashSet<int>();
            bool allGlobals = false;
            for (int p = count - 1; p >= 0; p--)
            {
                GraphPass pass = m_Passes[p];
                bool alive = !pass.allowCulling || pass.globalState;
                foreach (var use in pass.All())
                    alive = alive || (Writes(use.flags) && needed[use.texture]);
                foreach (var g in pass.globalsAfter)
                    alive = alive || allGlobals || neededGlobals.Contains(g.propertyId);
                pass.culled = !alive;
                if (!alive)
                {
                    stats.culledPasses++;
                    continue;
                }
                foreach (var g in pass.globalsAfter)
                {
                    neededGlobals.Remove(g.propertyId);
                    needed[g.texture] = true;
                }
                foreach (var use in pass.All())
                    if (Writes(use.flags))
                        needed[use.texture] = false;
                foreach (var use in pass.All())
                    if (Reads(use.flags))
                        needed[use.texture] = true;
                foreach (int id in pass.globalReads)
                    neededGlobals.Add(id);
                allGlobals = allGlobals || pass.allGlobals;
            }

            // Lifetimes over the passes that run.
            for (int p = 0; p < count; p++)
            {
                GraphPass pass = m_Passes[p];
                if (pass.culled)
                    continue;
                foreach (var use in pass.All())
                {
                    GraphTexture t = m_Textures[use.texture];
                    if (t.firstPass < 0)
                        t.firstPass = p;
                    t.lastPass = p;
                }
                foreach (var g in pass.globalsAfter)
                    m_Textures[g.texture].lastPass = count;
            }
            for (int t = 1; t < m_Textures.Count; t++)
                if (!m_Textures[t].isImported && m_Textures[t].firstPass >= 0)
                    stats.textures++;

            // Render passes: consecutive raster passes with the same attachments, none reading one of them as a texture.
            RenderPassGroup group = null;
            for (int p = 0; p < count; p++)
            {
                GraphPass pass = m_Passes[p];
                if (pass.culled)
                    continue;
                bool joins = group != null && group.raster && pass.kind == PassKind.Raster && SameAttachments(m_Passes[group.passes[0]], pass) &&
                             !ReadsAttachmentOf(pass, m_Passes[group.passes[0]]);
                if (!joins)
                {
                    group = new RenderPassGroup { raster = pass.kind == PassKind.Raster };
                    m_Groups.Add(group);
                    if (group.raster)
                        stats.renderPasses++;
                }
                else
                    stats.mergedPasses++;
                pass.renderPass = m_Groups.Count - 1;
                group.passes.Add(p);
            }
            statistics = stats;
        }

        static bool SameAttachments(GraphPass a, GraphPass b)
        {
            if (a.colorCount != b.colorCount || a.depth.texture != b.depth.texture || a.depth.mipLevel != b.depth.mipLevel || a.depth.depthSlice != b.depth.depthSlice)
                return false;
            for (int i = 0; i < a.colorCount; i++)
                if (a.colors[i].texture != b.colors[i].texture || a.colors[i].mipLevel != b.colors[i].mipLevel || a.colors[i].depthSlice != b.colors[i].depthSlice)
                    return false;
            return a.colorCount > 0 || a.depth.texture != 0;
        }

        static bool ReadsAttachmentOf(GraphPass pass, GraphPass first)
        {
            foreach (var use in pass.uses)
            {
                for (int i = 0; i < first.colorCount; i++)
                    if (first.colors[i].texture == use.texture)
                        return true;
                if (first.depth.texture == use.texture)
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------------------------------------------ execute

        Vector2Int ReferenceSize()
        {
            Vector2Int v = RTHandles.rtHandleProperties.currentViewportSize;
            return v.x > 0 && v.y > 0 ? v : new Vector2Int(Mathf.Max(RTHandles.maxWidth, 1), Mathf.Max(RTHandles.maxHeight, 1));
        }

        void Allocate(int first, int last)
        {
            Vector2Int reference = ReferenceSize();
            for (int t = 1; t < m_Textures.Count; t++)
            {
                GraphTexture tex = m_Textures[t];
                if (tex.isImported || tex.pooled != null || tex.firstPass < first || tex.firstPass > last)
                    continue;
                tex.pooled = m_Pool.Acquire(tex.desc, tex.desc.Size(reference), m_Frame, ref statistics);
            }
        }

        void Free(int first, int last)
        {
            for (int t = 1; t < m_Textures.Count; t++)
            {
                GraphTexture tex = m_Textures[t];
                if (tex.pooled != null && tex.lastPass >= first && tex.lastPass <= last)
                {
                    m_Pool.Release(tex.pooled);
                    tex.pooled = null;
                }
            }
        }

        // The load of an attachment at the start of its group: clear at its first use when asked, load when kept, else nothing.
        RenderBufferLoadAction LoadOf(int texture, AccessFlags flags, int pass)
        {
            GraphTexture t = m_Textures[texture];
            bool first = !t.hasContents;
            if (first && (t.isImported ? t.import.clearOnFirstUse : t.desc.clearBuffer))
                return RenderBufferLoadAction.Clear;
            if ((flags & AccessFlags.Discard) != 0)
                return RenderBufferLoadAction.DontCare;
            return t.isImported || !first ? RenderBufferLoadAction.Load : RenderBufferLoadAction.DontCare;
        }

        Color ClearColorOf(int texture)
        {
            GraphTexture t = m_Textures[texture];
            return t.isImported ? t.import.clearColor : t.desc.clearColor;
        }

        // Kept after the group when a later pass uses it or it is imported (unless discarded).
        RenderBufferStoreAction StoreOf(int texture, int lastPassOfGroup)
        {
            GraphTexture t = m_Textures[texture];
            if (t.isImported)
                return t.import.discardOnLastUse && t.lastPass <= lastPassOfGroup ? RenderBufferStoreAction.DontCare : RenderBufferStoreAction.Store;
            return t.lastPass > lastPassOfGroup ? RenderBufferStoreAction.Store : RenderBufferStoreAction.DontCare;
        }

        void Execute()
        {
            s_Current = this;
            m_Executing = true;
            m_Pool.BeginFrame();
            CommandBuffer cmd = m_Parameters.commandBuffer;
            ScriptableRenderContext context = m_Parameters.scriptableRenderContext;
            bool native = nativeRenderPassesEnabled && context != default;
            foreach (RenderPassGroup group in m_Groups)
            {
                int first = group.passes[0], last = group.passes[group.passes.Count - 1];
                Allocate(first, last);
                GraphPass head = m_Passes[first];
                if (group.raster && native)
                    RunNative(context, cmd, group, head, last);
                else
                {
                    if (group.raster)
                        Bind(cmd, head);
                    foreach (int p in group.passes)
                        Run(cmd, m_Passes[p]);
                }
                foreach (int p in group.passes)
                    foreach (var use in m_Passes[p].All())
                        if (Writes(use.flags))
                            m_Textures[use.texture].hasContents = true;
                Free(first, last);
            }
            statistics.pooledTextures = m_Pool.Count;
            m_Executing = false;
            s_Current = null;
        }

        // Clears of the textures a non raster pass first uses (raster passes clear by their load action).
        void ClearFirstUses(CommandBuffer cmd, GraphPass pass)
        {
            foreach (var use in pass.uses)
            {
                GraphTexture t = m_Textures[use.texture];
                if (!t.hasContents && Writes(use.flags) && (t.isImported ? t.import.clearOnFirstUse : t.desc.clearBuffer))
                {
                    cmd.SetRenderTarget(t.Handle);
                    cmd.ClearRenderTarget(RTClearFlags.All, ClearColorOf(use.texture), 1.0f, 0);
                    t.hasContents = true;
                }
            }
        }

        // The attachments of a group as the target, cleared where their first use asks.
        void Bind(CommandBuffer cmd, GraphPass head)
        {
            RTHandle depth = head.depth.texture != 0 ? m_Textures[head.depth.texture].Handle : null;
            bool clearColor = false, clearDepth = false;
            Color color = Color.clear;
            if (head.colorCount == 1)
            {
                RTHandle c = m_Textures[head.colors[0].texture].Handle;
                if (depth != null)
                    cmd.SetRenderTarget(c, depth, head.colors[0].mipLevel, CubemapFace.Unknown, head.colors[0].depthSlice);
                else
                    cmd.SetRenderTarget(c, head.colors[0].mipLevel, CubemapFace.Unknown, head.colors[0].depthSlice);
            }
            else if (head.colorCount > 1)
            {
                var colors = new RenderTargetIdentifier[head.colorCount];
                for (int i = 0; i < head.colorCount; i++)
                    colors[i] = head.colors[i].texture != 0 ? (RenderTargetIdentifier)m_Textures[head.colors[i].texture].Handle : new RenderTargetIdentifier(BuiltinRenderTextureType.None);
                cmd.SetRenderTarget(colors, depth != null ? (RenderTargetIdentifier)depth : new RenderTargetIdentifier(BuiltinRenderTextureType.None));
            }
            else if (depth != null)
                cmd.SetRenderTarget(depth, head.depth.mipLevel, CubemapFace.Unknown, head.depth.depthSlice);
            for (int i = 0; i < head.colorCount; i++)
                if (head.colors[i].texture != 0 && LoadOf(head.colors[i].texture, head.colors[i].flags, 0) == RenderBufferLoadAction.Clear)
                {
                    clearColor = true;
                    color = ClearColorOf(head.colors[i].texture);
                }
            if (head.depth.texture != 0 && LoadOf(head.depth.texture, head.depth.flags, 0) == RenderBufferLoadAction.Clear)
                clearDepth = true;
            if (clearColor || clearDepth)
                cmd.ClearRenderTarget(clearDepth, clearColor, color, 1.0f);
        }

        void Run(CommandBuffer cmd, GraphPass pass)
        {
            if (pass.kind != PassKind.Raster)
                ClearFirstUses(cmd, pass);
            else
                // Input attachments without native render passes: read as the textures the engine names for subpass inputs.
                foreach (var input in pass.inputs)
                    cmd.SetGlobalTexture("_HelixInput" + input.index, m_Textures[input.texture].Handle);
            cmd.BeginSample(pass.name);
            pass.run(this);
            cmd.EndSample(pass.name);
            foreach (var g in pass.globalsAfter)
                cmd.SetGlobalTexture(g.propertyId, m_Textures[g.texture].Handle);
        }

        // A group as one native render pass: an attachment per texture, a subpass per pass.
        void RunNative(ScriptableRenderContext context, CommandBuffer cmd, RenderPassGroup group, GraphPass head, int lastPass)
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            var textures = new List<int>();
            for (int i = 0; i < head.colorCount; i++)
                if (head.colors[i].texture != 0 && !textures.Contains(head.colors[i].texture))
                    textures.Add(head.colors[i].texture);
            int depthIndex = -1;
            if (head.depth.texture != 0)
            {
                depthIndex = textures.Count;
                textures.Add(head.depth.texture);
            }
            foreach (int p in group.passes)
                foreach (var input in m_Passes[p].inputs)
                    if (!textures.Contains(input.texture))
                        textures.Add(input.texture);
            var attachments = new NativeArray<AttachmentDescriptor>(textures.Count, Allocator.Temp);
            int width = 1, height = 1, samples = 1;
            for (int i = 0; i < textures.Count; i++)
            {
                GraphTexture t = m_Textures[textures[i]];
                RTHandle h = t.Handle;
                var a = new AttachmentDescriptor(h.rt != null ? (i == depthIndex ? h.rt.depthStencilFormat : h.rt.graphicsFormat) : t.desc.colorFormat);
                AccessFlags flags = i == depthIndex ? head.depth.flags : AccessFlags.Write;
                for (int c = 0; c < head.colorCount; c++)
                    if (head.colors[c].texture == textures[i])
                        flags = head.colors[c].flags;
                a.loadStoreTarget = h;
                a.loadAction = LoadOf(textures[i], flags, group.passes[0]);
                a.storeAction = StoreOf(textures[i], lastPass);
                if (a.loadAction == RenderBufferLoadAction.Clear)
                    a.ConfigureClear(ClearColorOf(textures[i]));
                attachments[i] = a;
                if (i == 0 && h.rt != null)
                {
                    width = h.rt.width;
                    height = h.rt.height;
                    samples = Mathf.Max(h.rt.antiAliasing, 1);
                }
            }
            context.BeginRenderPass(width, height, samples, attachments, depthIndex);
            foreach (int p in group.passes)
            {
                GraphPass pass = m_Passes[p];
                var colors = new NativeArray<int>(pass.colorCount, Allocator.Temp);
                for (int i = 0; i < pass.colorCount; i++)
                    colors[i] = textures.IndexOf(pass.colors[i].texture);
                var inputs = new NativeArray<int>(pass.inputs.Count, Allocator.Temp);
                for (int i = 0; i < pass.inputs.Count; i++)
                    inputs[i] = textures.IndexOf(pass.inputs[i].texture);
                context.BeginSubPass(colors, inputs, pass.depth.texture != 0 && !Writes(pass.depth.flags));
                cmd.BeginSample(pass.name);
                pass.run(this);
                cmd.EndSample(pass.name);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                context.EndSubPass();
                colors.Dispose();
                inputs.Dispose();
                foreach (var g in pass.globalsAfter)
                    cmd.SetGlobalTexture(g.propertyId, m_Textures[g.texture].Handle);
            }
            context.EndRenderPass();
            attachments.Dispose();
        }
    }
}
