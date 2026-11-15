using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HelixEngine.Rendering.Internal;

namespace HelixEngine.Rendering
{
    /// <summary>
    /// The panels of the debug display, the data blocks they reset, and whether the runtime display shows: the native
    /// core's manager (C++ pipelines' panels are in it too).
    /// </summary>
    public sealed partial class DebugManager
    {
        static readonly Lazy<DebugManager> s_Instance = new Lazy<DebugManager>(() => new DebugManager());

        public static DebugManager instance => s_Instance.Value;

        readonly List<DebugUI.Panel> m_Panels = new List<DebugUI.Panel>();
        readonly ReadOnlyCollection<DebugUI.Panel> m_ReadOnlyPanels;
        uint m_Version = uint.MaxValue;
        readonly Dictionary<IDebugData, (ulong id, GCHandle handle)> m_Data = new Dictionary<IDebugData, (ulong, GCHandle)>();

        DebugManager()
        {
            m_ReadOnlyPanels = m_Panels.AsReadOnly();
            DebugCallbacks.Ensure();
        }

        public ReadOnlyCollection<DebugUI.Panel> panels
        {
            get
            {
                uint version = DebugNative.PanelsVersion();
                if (version != m_Version)
                {
                    m_Version = version;
                    m_Panels.Clear();
                    int count = DebugNative.PanelCount();
                    for (int i = 0; i < count; i++)
                        if (NativeWidgets.Of(DebugNative.PanelAt(i)) is DebugUI.Panel p)
                            m_Panels.Add(p);
                }
                return m_ReadOnlyPanels;
            }
        }

        public event Action<bool> onDisplayRuntimeUIChanged = delegate { };
        public event Action resetData = delegate { };
        public event Action<bool> onSetDirty = delegate { };

        internal void RaiseDisplay(bool shown) => onDisplayRuntimeUIChanged(shown);
        internal void RaiseReset() => resetData();
        internal void RaiseDirty(bool full) => onSetDirty(full);

        /// <summary>The runtime display shows (its view comes with the UI; the text view reads the same panels).</summary>
        public bool displayRuntimeUI
        {
            get => DebugNative.DisplayRuntimeUI();
            set => DebugNative.SetDisplayRuntimeUI(value);
        }

        public bool displayEditorUI => false;

        public bool isAnyDebugUIActive => displayRuntimeUI;

        public int GetState() => DebugNative.GetState();

        /// <summary>The panel of a name; made (in its group order) when createIfNull.</summary>
        public DebugUI.Panel GetPanel(string displayName, bool createIfNull = false, int groupIndex = 0, bool overrideIfExist = false) =>
            NativeWidgets.Of(DebugNative.GetPanel(displayName ?? "", createIfNull, groupIndex, overrideIfExist)) as DebugUI.Panel;

        public int PanelIndex(string displayName) => DebugNative.PanelIndex(displayName ?? "");

        public void RemovePanel(string displayName)
        {
            int index = PanelIndex(displayName);
            if (index >= 0)
                DebugNative.RemovePanel(DebugNative.PanelAt(index));
        }

        public void RemovePanel(DebugUI.Panel panel)
        {
            if (panel != null)
                DebugNative.RemovePanel(panel.m_Native);
        }

        /// <summary>Every widget (in containers too) that has all of the flags.</summary>
        public DebugUI.Widget[] GetItems(DebugUI.Flags flags)
        {
            int count = DebugNative.GetItems((int)flags);
            var ids = new ulong[count];
            for (int i = 0; i < count; i++)
                ids[i] = DebugNative.ItemAt(i);
            var list = new List<DebugUI.Widget>(count);
            foreach (ulong id in ids)
                if (NativeWidgets.Of(id) is DebugUI.Widget w)
                    list.Add(w);
            return list.ToArray();
        }

        public DebugUI.Widget GetItem(string queryPath) => NativeWidgets.Of(DebugNative.GetItem(queryPath ?? "")) as DebugUI.Widget;

        public void RegisterData(IDebugData data)
        {
            if (data == null || m_Data.ContainsKey(data))
                return;
            GCHandle handle = GCHandle.Alloc(data);
            m_Data[data] = (DebugNative.RegisterManagedData((ulong)GCHandle.ToIntPtr(handle)), handle);
        }

        public void UnregisterData(IDebugData data)
        {
            if (data == null || !m_Data.TryGetValue(data, out (ulong id, GCHandle handle) entry))
                return;
            DebugNative.UnregisterManagedData(entry.id);
            entry.handle.Free();
            m_Data.Remove(data);
        }

        /// <summary>Every registered data block back to its defaults.</summary>
        public void Reset() => DebugNative.Reset();

        public void RefreshEditor() => DebugNative.RefreshEditor();
    }

    /// <summary>The debug display as text, a line per widget indented by depth: for logs and tests until the display's UI comes.</summary>
    public static class DebugUITextView
    {
        public static string Render() => DebugNative.TextView();

        public static string Render(DebugUI.Panel panel) => panel != null ? DebugNative.TextViewOf(panel.m_Native) : "";
    }

    // The native core's calls of C# widgets (their GC handles), of the manager's events and of C# data blocks.
    static unsafe class DebugCallbacks
    {
        static bool s_Set;

        internal static void Ensure()
        {
            if (s_Set)
                return;
            s_Set = true;
            DebugNative.SetWidgetCallbacks((ulong)(delegate* unmanaged[Cdecl]<ulong, byte>)&Text, (ulong)(delegate* unmanaged[Cdecl]<ulong, byte>)&Hidden,
                                           (ulong)(delegate* unmanaged[Cdecl]<ulong, byte>)&Open, (ulong)(delegate* unmanaged[Cdecl]<ulong, void>)&Free);
            DebugNative.SetManagerCallbacks((ulong)(delegate* unmanaged[Cdecl]<byte, void>)&Dirty, (ulong)(delegate* unmanaged[Cdecl]<void>)&Reset,
                                            (ulong)(delegate* unmanaged[Cdecl]<byte, void>)&Display, (ulong)(delegate* unmanaged[Cdecl]<ulong, void>)&PanelDirty,
                                            (ulong)(delegate* unmanaged[Cdecl]<ulong, void>)&DataReset);
        }

        static DebugUI.Widget W(ulong h) => (DebugUI.Widget)GCHandle.FromIntPtr((IntPtr)h).Target;

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static byte Text(ulong h)
        {
            try
            {
                string text = W(h).OwnText();
                if (text == null)
                    return 0;
                DebugNative.ReturnText(text);
                return 1;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return 0;
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static byte Hidden(ulong h)
        {
            try
            {
                return W(h).isHidden ? (byte)1 : (byte)0;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return 0;
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static byte Open(ulong h)
        {
            try
            {
                return W(h).OwnOpen ? (byte)1 : (byte)0;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return 1;
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void Free(ulong h)
        {
            try
            {
                W(h).Detached();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void Dirty(byte full)
        {
            try
            {
                DebugManager.instance.RaiseDirty(full != 0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void Reset()
        {
            try
            {
                DebugManager.instance.RaiseReset();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void Display(byte shown)
        {
            try
            {
                DebugManager.instance.RaiseDisplay(shown != 0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void PanelDirty(ulong panel)
        {
            try
            {
                (NativeWidgets.Of(panel) as DebugUI.Panel)?.RaiseDirty();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void DataReset(ulong h)
        {
            try
            {
                ((IDebugData)GCHandle.FromIntPtr((IntPtr)h).Target).GetReset()?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
