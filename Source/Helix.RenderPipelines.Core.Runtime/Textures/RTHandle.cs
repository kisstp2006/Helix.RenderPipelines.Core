using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HelixEngine.Rendering.Internal;

namespace HelixEngine.Rendering
{
    /// <summary>A size from the reference size of a handle system (its scaled size).</summary>
    public delegate Vector2Int ScaleFunc(Vector2Int size);

    /// <summary>
    /// A render texture, a texture or a target identifier of a handle system (the native core's handle by id). A
    /// handle sized by a scale or a scale function follows the system's reference size.
    /// </summary>
    public class RTHandle
    {
        internal readonly ulong m_Id;
        internal ScaleFunc scaleFunc;
        internal System.Runtime.InteropServices.GCHandle m_ScaleHandle;

        static readonly Dictionary<ulong, RTHandle> s_Handles = new Dictionary<ulong, RTHandle>();
        static int s_PruneAt = 64;

        RTHandle(ulong id) => m_Id = id;

        // The one wrapper of a native handle (null for 0).
        internal static RTHandle Of(ulong id)
        {
            if (id == 0)
                return null;
            if (!s_Handles.TryGetValue(id, out RTHandle h))
            {
                // Handles released on the native side (a render graph's pooled ones) leave the cache as it grows.
                if (s_Handles.Count >= s_PruneAt)
                {
                    var dead = new List<ulong>();
                    foreach (ulong key in s_Handles.Keys)
                        if (!TexturesNative.IsValid(key))
                            dead.Add(key);
                    foreach (ulong key in dead)
                        s_Handles.Remove(key);
                    s_PruneAt = Math.Max(64, s_Handles.Count * 2);
                }
                s_Handles[id] = h = new RTHandle(id);
            }
            return h;
        }

        public Vector2 scaleFactor => TexturesNative.ScaleFactor(m_Id);
        public bool useScaling => TexturesNative.UseScaling(m_Id);
        public Vector2Int referenceSize => TexturesNative.ReferenceSize(m_Id);
        public RTHandleProperties rtHandleProperties => TexturesNative.HandleProperties(m_Id);
        public RenderTexture rt => TexturesNative.RenderTextureOf(m_Id);
        public RenderTargetIdentifier nameID => Targets.Identifier(TexturesNative.NameID(m_Id));
        public string name => TexturesNative.Name(m_Id);
        public bool isMSAAEnabled => TexturesNative.IsMSAAEnabled(m_Id);
        public Texture externalTexture => TexturesNative.ExternalTexture(m_Id);

        public static implicit operator RenderTargetIdentifier(RTHandle handle) => handle?.nameID ?? default;
        public static implicit operator Texture(RTHandle handle) => handle == null ? null : handle.rt != null ? handle.rt : handle.externalTexture;
        public static implicit operator RenderTexture(RTHandle handle) => handle?.rt;

        public void SetCustomHandleProperties(in RTHandleProperties properties) => TexturesNative.SetCustomHandleProperties(m_Id, properties);
        public void ClearCustomHandleProperties() => TexturesNative.ClearCustomHandleProperties(m_Id);
        public void SetRenderTexture(RenderTexture rt) => TexturesNative.SetRenderTexture(m_Id, rt);
        public void SetTexture(Texture tex) => TexturesNative.SetTexture(m_Id, tex);
        public void SetTexture(RenderTargetIdentifier tex) => TexturesNative.SetTarget(m_Id, Targets.Data(tex));

        /// <summary>The size for a reference size (its own size when it does not scale).</summary>
        public Vector2Int GetScaledSize(Vector2Int refSize) => TexturesNative.ScaledSize(m_Id, refSize);

        public Vector2Int GetScaledSize() => TexturesNative.OwnScaledSize(m_Id);

        /// <summary>Destroys the render texture the system made and leaves the system.</summary>
        public void Release()
        {
            TexturesNative.Release(m_Id);
            Forget();
        }

        internal void Forget()
        {
            s_Handles.Remove(m_Id);
            if (m_ScaleHandle.IsAllocated)
                m_ScaleHandle.Free();
        }
    }

    // RenderTargetIdentifier and the native TargetData have one layout.
    static class Targets
    {
        public static TargetData Data(RenderTargetIdentifier id) => Unsafe.As<RenderTargetIdentifier, TargetData>(ref id);
        public static RenderTargetIdentifier Identifier(TargetData data) => Unsafe.As<TargetData, RenderTargetIdentifier>(ref data);
    }
}
