using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace HelixEngine.Rendering
{
    /// <summary>The panels of the debug display, the data blocks they reset, and whether the runtime display shows.</summary>
    public sealed partial class DebugManager
    {
        static readonly Lazy<DebugManager> s_Instance = new Lazy<DebugManager>(() => new DebugManager());

        public static DebugManager instance => s_Instance.Value;

        readonly List<DebugUI.Panel> m_Panels = new List<DebugUI.Panel>();
        readonly List<IDebugData> m_Data = new List<IDebugData>();
        ReadOnlyCollection<DebugUI.Panel> m_ReadOnlyPanels;
        bool m_DisplayRuntimeUI;

        DebugManager() { }

        public ReadOnlyCollection<DebugUI.Panel> panels => m_ReadOnlyPanels ??= m_Panels.AsReadOnly();

        public event Action<bool> onDisplayRuntimeUIChanged = delegate { };
        public event Action resetData = delegate { };
        public event Action<bool> onSetDirty = delegate { };

        /// <summary>The runtime display shows (its view comes with the UI; the text view reads the same panels).</summary>
        public bool displayRuntimeUI
        {
            get => m_DisplayRuntimeUI;
            set
            {
                if (m_DisplayRuntimeUI == value)
                    return;
                m_DisplayRuntimeUI = value;
                onDisplayRuntimeUIChanged(value);
            }
        }

        public bool displayEditorUI => false;

        public bool isAnyDebugUIActive => displayRuntimeUI;

        public int GetState()
        {
            int hash = 17;
            foreach (DebugUI.Panel p in m_Panels)
                hash = hash * 23 + p.GetHashCode();
            return hash;
        }

        /// <summary>The panel of a name; made (in its group order) when createIfNull.</summary>
        public DebugUI.Panel GetPanel(string displayName, bool createIfNull = false, int groupIndex = 0, bool overrideIfExist = false)
        {
            int index = PanelIndex(displayName);
            DebugUI.Panel p = index >= 0 ? m_Panels[index] : null;
            if (p != null && overrideIfExist)
            {
                RemovePanel(p);
                p = null;
            }
            if (p == null && createIfNull)
            {
                p = new DebugUI.Panel { displayName = displayName, groupIndex = groupIndex };
                p.onSetDirty += OnPanelDirty;
                m_Panels.Add(p);
                // Stable by group.
                var sorted = new List<DebugUI.Panel>(m_Panels);
                sorted.Sort((a, b) => a.groupIndex != b.groupIndex ? a.groupIndex.CompareTo(b.groupIndex) : m_Panels.IndexOf(a).CompareTo(m_Panels.IndexOf(b)));
                m_Panels.Clear();
                m_Panels.AddRange(sorted);
                onSetDirty(true);
            }
            return p;
        }

        public int PanelIndex(string displayName)
        {
            displayName = displayName ?? "";
            for (int i = 0; i < m_Panels.Count; i++)
                if (string.Equals(m_Panels[i].displayName, displayName, StringComparison.InvariantCultureIgnoreCase))
                    return i;
            return -1;
        }

        public void RemovePanel(string displayName)
        {
            int index = PanelIndex(displayName);
            if (index >= 0)
                RemovePanel(m_Panels[index]);
        }

        public void RemovePanel(DebugUI.Panel panel)
        {
            if (panel == null || !m_Panels.Remove(panel))
                return;
            panel.onSetDirty -= OnPanelDirty;
            onSetDirty(true);
        }

        void OnPanelDirty(DebugUI.Panel panel) => onSetDirty(false);

        /// <summary>Every widget (in containers too) that has all of the flags.</summary>
        public DebugUI.Widget[] GetItems(DebugUI.Flags flags)
        {
            var list = new List<DebugUI.Widget>();
            foreach (DebugUI.Panel p in m_Panels)
                Collect(p.children, flags, list);
            return list.ToArray();
        }

        static void Collect(IEnumerable<DebugUI.Widget> widgets, DebugUI.Flags flags, List<DebugUI.Widget> list)
        {
            foreach (DebugUI.Widget w in widgets)
            {
                if ((w.flags & flags) == flags)
                    list.Add(w);
                if (w is DebugUI.IContainer c)
                    Collect(c.children, flags, list);
            }
        }

        public DebugUI.Widget GetItem(string queryPath)
        {
            foreach (DebugUI.Panel p in m_Panels)
                if (DebugUI.Panel.Find(p.children, queryPath) is DebugUI.Widget w)
                    return w;
            return null;
        }

        public void RegisterData(IDebugData data)
        {
            if (data != null && !m_Data.Contains(data))
                m_Data.Add(data);
        }

        public void UnregisterData(IDebugData data) => m_Data.Remove(data);

        /// <summary>Every registered data block back to its defaults.</summary>
        public void Reset()
        {
            resetData();
            foreach (IDebugData d in m_Data)
                d.GetReset()?.Invoke();
            onSetDirty(true);
        }

        public void RefreshEditor() => onSetDirty(true);
    }

    /// <summary>The debug display as text, a line per widget indented by depth: for logs and tests until the display's UI comes.</summary>
    public static class DebugUITextView
    {
        public static string Render()
        {
            var sb = new StringBuilder();
            foreach (DebugUI.Panel p in DebugManager.instance.panels)
                Render(p, sb);
            return sb.ToString();
        }

        public static string Render(DebugUI.Panel panel)
        {
            var sb = new StringBuilder();
            Render(panel, sb);
            return sb.ToString();
        }

        static void Render(DebugUI.Panel panel, StringBuilder sb)
        {
            sb.Append('[').Append(panel.displayName).Append("]\n");
            Render(panel.children, 1, sb);
        }

        static void Render(IEnumerable<DebugUI.Widget> widgets, int depth, StringBuilder sb)
        {
            foreach (DebugUI.Widget w in widgets)
            {
                if (w.isHidden || w.isEditorOnly)
                    continue;
                sb.Append(' ', depth * 2).Append(w.displayName);
                string value = ValueOf(w);
                if (value != null)
                    sb.Append(": ").Append(value);
                sb.Append('\n');
                if (w is DebugUI.Foldout f && !f.opened)
                    continue;
                if (w is DebugUI.IContainer c)
                    Render(c.children, depth + 1, sb);
            }
        }

        static string ValueOf(DebugUI.Widget w)
        {
            switch (w)
            {
            case DebugUI.Value v:
                return v.FormatString(v.GetValue());
            case DebugUI.EnumField e:
                int i = e.currentIndex;
                return i >= 0 && e.enumNames != null && i < e.enumNames.Length ? e.enumNames[i] : e.GetValue().ToString(CultureInfo.InvariantCulture);
            case DebugUI.FloatField f:
                return f.GetValue().ToString("F" + f.decimals, CultureInfo.InvariantCulture);
            case DebugUI.ColorField c:
                Color col = c.GetValue();
                return string.Format(CultureInfo.InvariantCulture, "({0:F3}, {1:F3}, {2:F3}, {3:F3})", col.r, col.g, col.b, col.a);
            case DebugUI.Foldout f:
                return f.opened ? "open" : "closed";
            case DebugUI.Button:
                return null;
            case DebugUI.IValueField field:
                return Convert.ToString(field.GetValue(), CultureInfo.InvariantCulture);
            default:
                return null;
            }
        }
    }
}
