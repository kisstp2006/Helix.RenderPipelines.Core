using System;
using System.Collections.Generic;

namespace HelixEngine.Rendering
{
    /// <summary>
    /// The data of a frame by its type: items made once and filled again each frame. An item of a C# class derives from
    /// ContextItem (its object is a native one); a pipeline's own items (the frame data of the pipeline) are its native
    /// classes. Dispose resets the items made since the last Dispose.
    /// </summary>
    public sealed class ContextContainer : IDisposable
    {
        internal readonly ulong m_Id;
        readonly bool m_Owned;

        static readonly Dictionary<Type, ulong> s_Keys = new Dictionary<Type, ulong>();
        static readonly Dictionary<ulong, ContextContainer> s_Borrowed = new Dictionary<ulong, ContextContainer>();

        public ContextContainer()
        {
            m_Id = ContextNative.Create();
            m_Owned = true;
        }

        ContextContainer(ulong id)
        {
            m_Id = id;
            m_Owned = false;
        }

        // The container a native pipeline made (one C# object per native container).
        internal static ContextContainer Borrow(ulong id)
        {
            if (id == 0)
                return null;
            if (!s_Borrowed.TryGetValue(id, out ContextContainer c))
                s_Borrowed[id] = c = new ContextContainer(id);
            return c;
        }

        static ulong KeyOf(Type type)
        {
            if (!s_Keys.TryGetValue(type, out ulong key))
                s_Keys[type] = key = ContextNative.KeyOfName(type.FullName);
            return key;
        }

        /// <summary>The item of the type made in this frame (throws when it was not).</summary>
        public T Get<T>() where T : ContextItem, new()
        {
            Check();
            if (ContextNative.Find(m_Id, KeyOf(typeof(T))) is T item)
                return item;
            throw new InvalidOperationException($"Type {typeof(T).FullName} has not been created yet.");
        }

        /// <summary>The item, made (or reused) when it was not made in this frame.</summary>
        public T GetOrCreate<T>() where T : ContextItem, new()
        {
            Check();
            ulong key = KeyOf(typeof(T));
            if (ContextNative.Find(m_Id, key) is T made)
                return made;
            if (ContextNative.Kept(m_Id, key) is T kept)
            {
                ContextNative.Mark(m_Id, key);
                return kept;
            }
            var item = new T();
            ContextNative.Add(m_Id, key, item);
            return item;
        }

        /// <summary>The item made for this frame (throws when it was made already).</summary>
        public T Create<T>() where T : ContextItem, new()
        {
            Check();
            if (ContextNative.Find(m_Id, KeyOf(typeof(T))) != null)
                throw new InvalidOperationException($"Type {typeof(T).FullName} has already been created.");
            return GetOrCreate<T>();
        }

        public bool Contains<T>() where T : ContextItem, new() => ContextNative.Find(m_Id, KeyOf(typeof(T))) != null;

        /// <summary>The frame ends: the items made in it are reset (a container made in C# goes with its items at
        /// its finalization or when the native pipeline's frame data ends).</summary>
        public void Dispose() => ContextNative.Dispose(m_Id);

        ~ContextContainer()
        {
            if (m_Owned)
                DestroyLater(m_Id);
        }

        // Native objects go on the main thread (the finalizer runs on its own): at the next frame of the C# side.
        static readonly List<ulong> s_Pending = new List<ulong>();

        static void DestroyLater(ulong id)
        {
            lock (s_Pending)
                s_Pending.Add(id);
        }

        // The containers of finalized C# objects go first.
        static void Check()
        {
            if (s_Pending.Count > 0)
                lock (s_Pending)
                {
                    foreach (ulong id in s_Pending)
                        ContextNative.Destroy(id);
                    s_Pending.Clear();
                }
        }
    }
}
