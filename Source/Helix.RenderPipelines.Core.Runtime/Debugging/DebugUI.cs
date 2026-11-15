using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using HelixEngine.Rendering.Internal;

namespace HelixEngine.Rendering
{
    /// <summary>A list that tells when items come and go (the debug display's containers).</summary>
    public class ObservableList<T> : IList<T>
    {
        readonly List<T> m_List;

        public event Action<ObservableList<T>, ListChangedEventArgs<T>> ItemAdded;
        public event Action<ObservableList<T>, ListChangedEventArgs<T>> ItemRemoved;

        public ObservableList() : this(0) { }
        public ObservableList(int capacity) => m_List = new List<T>(capacity);
        public ObservableList(IEnumerable<T> collection) => m_List = new List<T>(collection);

        public T this[int index]
        {
            get => m_List[index];
            set
            {
                OnRemoved(index, m_List[index]);
                m_List[index] = value;
                OnAdded(index, value);
            }
        }

        public int Count => m_List.Count;
        public bool IsReadOnly => false;

        void OnAdded(int index, T item) => ItemAdded?.Invoke(this, new ListChangedEventArgs<T>(index, item));
        void OnRemoved(int index, T item) => ItemRemoved?.Invoke(this, new ListChangedEventArgs<T>(index, item));

        public bool Contains(T item) => m_List.Contains(item);
        public int IndexOf(T item) => m_List.IndexOf(item);

        public void Add(T item)
        {
            m_List.Add(item);
            OnAdded(m_List.Count - 1, item);
        }

        public void Add(params T[] items)
        {
            foreach (T item in items)
                Add(item);
        }

        public void Insert(int index, T item)
        {
            m_List.Insert(index, item);
            OnAdded(index, item);
        }

        public bool Remove(T item)
        {
            int index = m_List.IndexOf(item);
            if (index < 0)
                return false;
            RemoveAt(index);
            return true;
        }

        public int Remove(params T[] items)
        {
            int count = 0;
            foreach (T item in items)
                count += Remove(item) ? 1 : 0;
            return count;
        }

        public void RemoveAt(int index)
        {
            T item = m_List[index];
            m_List.RemoveAt(index);
            OnRemoved(index, item);
        }

        public void Clear()
        {
            while (m_List.Count > 0)
                RemoveAt(m_List.Count - 1);
        }

        // The items of the native list, without telling (a wrapper's view).
        internal void Reload(IEnumerable<T> items)
        {
            m_List.Clear();
            m_List.AddRange(items);
        }

        public void CopyTo(T[] array, int arrayIndex) => m_List.CopyTo(array, arrayIndex);
        public IEnumerator<T> GetEnumerator() => m_List.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class ListChangedEventArgs<T> : EventArgs
    {
        public readonly int index;
        public readonly T item;

        public ListChangedEventArgs(int index, T item)
        {
            this.index = index;
            this.item = item;
        }
    }

    /// <summary>What a debug data block resets.</summary>
    public interface IDebugData
    {
        Action GetReset();
    }

    /// <summary>
    /// The widgets of the debug display: panels of values and fields bound to getters and setters. The native core holds
    /// the panels and the tree: a C# widget keeps its settings and delegates, and while it is in a panel a native widget
    /// stands for it; a C++ widget is seen as a wrapper of the C# class of its kind (its settings read when it is
    /// wrapped, its value, name and flags the native ones).
    /// </summary>
    public partial class DebugUI
    {
        [Flags]
        public enum Flags
        {
            None = 0,
            EditorOnly = 1 << 1,
            RuntimeOnly = 1 << 2,
            EditorForceUpdate = 1 << 3,
            FrequentlyUsed = 1 << 4,
        }

        public interface IContainer
        {
            ObservableList<Widget> children { get; }
            string displayName { get; set; }
            string queryPath { get; }
        }

        public interface IValueField
        {
            object GetValue();
            void SetValue(object value);
            object ValidateValue(object value);
        }

        public abstract class Widget
        {
            protected Panel m_Panel;
            protected IContainer m_Parent;
            // The native widget: the C++ widget this wraps, or the one standing for this C# widget while it is in a tree
            // of native widgets (0 otherwise).
            internal ulong m_Native;
            internal bool m_Wraps;
            GCHandle m_Self;
            Flags m_Flags;
            string m_DisplayName = "";
            string m_Tooltip;
            string m_QueryPath = "";

            public virtual Panel panel
            {
                get => m_Wraps ? NativeWidgets.Of(DebugNative.PanelOf(m_Native)) as Panel : m_Panel;
                internal set => m_Panel = value;
            }

            public virtual IContainer parent
            {
                get => m_Wraps ? NativeWidgets.Of(DebugNative.Parent(m_Native)) as IContainer : m_Parent;
                internal set => m_Parent = value;
            }

            public Flags flags
            {
                get => m_Wraps ? (Flags)DebugNative.Flags(m_Native) : m_Flags;
                set
                {
                    m_Flags = value;
                    if (m_Native != 0)
                        DebugNative.SetFlags(m_Native, (int)value);
                }
            }

            public string displayName
            {
                get => m_Wraps ? DebugNative.DisplayName(m_Native) : m_DisplayName;
                set
                {
                    m_DisplayName = value;
                    if (m_Native != 0)
                        DebugNative.SetDisplayName(m_Native, value ?? "");
                }
            }

            public string tooltip
            {
                get => m_Wraps ? DebugNative.Tooltip(m_Native) : m_Tooltip;
                set
                {
                    m_Tooltip = value;
                    if (m_Native != 0)
                        DebugNative.SetTooltip(m_Native, value ?? "");
                }
            }

            /// <summary>The names of the containers above it and its own, joined by " -> ".</summary>
            public string queryPath
            {
                get => m_Native != 0 ? DebugNative.QueryPath(m_Native) : m_QueryPath;
                private set => m_QueryPath = value;
            }

            public bool isEditorOnly => (flags & Flags.EditorOnly) != 0;
            public bool isRuntimeOnly => (flags & Flags.RuntimeOnly) != 0;
            public Func<bool> isHiddenCallback;
            public bool isHidden => m_Wraps ? DebugNative.IsHidden(m_Native) : isHiddenCallback?.Invoke() ?? false;

            public struct NameAndTooltip
            {
                public string name;
                public string tooltip;
            }

            public NameAndTooltip nameAndTooltip
            {
                set
                {
                    displayName = value.name;
                    tooltip = value.tooltip;
                }
            }

            internal virtual void GenerateQueryPath()
            {
                queryPath = (displayName ?? "").Trim();
                if (m_Parent != null && !string.IsNullOrEmpty(m_Parent.queryPath))
                    queryPath = m_Parent.queryPath + " -> " + queryPath;
            }

            public override int GetHashCode() => m_Wraps ? DebugNative.Hash(m_Native) : (queryPath ?? "").GetHashCode() ^ isHidden.GetHashCode();

            // The text view's value of a C# widget (null: none); a wrapper's is the native one.
            internal virtual string OwnText() => null;
            // The text view shows its widgets.
            internal virtual bool OwnOpen => true;

            // A wrapper of a C++ widget: its delegates reach the native widget.
            internal virtual void BindNative() { }

            // The native widget standing for it (made with its children's), or the C++ widget it wraps.
            internal ulong Attach()
            {
                if (m_Wraps || m_Native != 0)
                    return m_Native;
                DebugCallbacks.Ensure();
                m_Self = GCHandle.Alloc(this);
                m_Native = DebugNative.CreateManaged((ulong)GCHandle.ToIntPtr(m_Self), this is IContainer);
                DebugNative.SetDisplayName(m_Native, m_DisplayName ?? "");
                DebugNative.SetFlags(m_Native, (int)m_Flags);
                if (m_Tooltip != null)
                    DebugNative.SetTooltip(m_Native, m_Tooltip);
                AttachChildren();
                return m_Native;
            }

            internal virtual void AttachChildren() { }

            // Out of the native tree: its native widget goes (a C++ widget below it stays, out of the tree).
            internal void Detach()
            {
                if (m_Wraps || m_Native == 0)
                    return;
                ReleaseWrapped();
                DebugNative.Destroy(m_Native);
            }

            internal virtual void ReleaseWrapped() { }

            // Its native widget is gone.
            internal void Detached()
            {
                m_Native = 0;
                if (m_Self.IsAllocated)
                    m_Self.Free();
            }

            // A child that came or went: the native list follows (the C# widget gets its native widget or loses it).
            internal static void Inserted(ulong container, int index, Widget item)
            {
                if (container != 0 && item != null)
                    DebugNative.Insert(container, index, item.Attach());
            }

            internal static void Removed(ulong container, Widget item)
            {
                if (container == 0 || item == null || item.m_Native == 0)
                    return;
                DebugNative.Remove(container, item.m_Native);
                item.Detach();
            }
        }

        public class Button : Widget
        {
            public Action action { get; set; }

            internal override void BindNative()
            {
                ulong id = m_Native;
                action = () => DebugNative.Press(id);
            }
        }

        /// <summary>A read only value, refreshed refreshRate seconds apart.</summary>
        public class Value : Widget
        {
            public Func<object> getter { get; set; }
            public float refreshRate = 0.1f;
            public string formatString;

            public virtual object GetValue() => getter?.Invoke();

            public virtual string FormatString(object value) =>
                string.IsNullOrEmpty(formatString) ? Convert.ToString(value, CultureInfo.InvariantCulture) : string.Format(CultureInfo.InvariantCulture, formatString, value);

            internal override string OwnText() => FormatString(GetValue());

            internal override void BindNative()
            {
                ulong id = m_Native;
                getter = () => DebugNative.ValueText(id);
            }
        }

        /// <summary>A value bound to a getter and a setter; set values are validated and reported once they differ.</summary>
        public abstract class Field<T> : Widget, IValueField
        {
            public Func<T> getter { get; set; }
            public Action<T> setter { get; set; }
            public Action<Field<T>, T> onValueChanged;

            object IValueField.GetValue() => GetValue();
            void IValueField.SetValue(object value) => SetValue((T)value);
            object IValueField.ValidateValue(object value) => ValidateValue((T)value);

            public virtual T ValidateValue(T value) => value;

            public T GetValue() => getter();

            public virtual void SetValue(T value)
            {
                T v = ValidateValue(value);
                if (v == null || !v.Equals(getter()))
                {
                    setter?.Invoke(v);
                    onValueChanged?.Invoke(this, v);
                }
            }

            internal override string OwnText() => Convert.ToString(GetValue(), CultureInfo.InvariantCulture);

            // A C++ field's value as the native core carries it.
            internal virtual T FromNative(DebugValue v) => throw new NotSupportedException($"{GetType().Name} of a C++ widget");
            internal virtual DebugValue ToNative(T value) => throw new NotSupportedException($"{GetType().Name} of a C++ widget");

            // A C++ field validates natively.
            internal override void BindNative()
            {
                ulong id = m_Native;
                getter = () => FromNative(DebugNative.ReadValue(id));
                setter = v => DebugNative.WriteValue(id, ToNative(v));
            }
        }

        public class BoolField : Field<bool>
        {
            internal override bool FromNative(DebugValue v) => v.i != 0;
            internal override DebugValue ToNative(bool value) => new DebugValue { i = value ? 1 : 0 };
        }

        public class IntField : Field<int>
        {
            public Func<int> min;
            public Func<int> max;
            public int incStep = 1;
            public int intStepMult = 10;

            public override int ValidateValue(int value)
            {
                if (min != null)
                    value = Math.Max(value, min());
                if (max != null)
                    value = Math.Min(value, max());
                return value;
            }

            internal override int FromNative(DebugValue v) => (int)v.i;
            internal override DebugValue ToNative(int value) => new DebugValue { i = value };
        }

        public class UIntField : Field<uint>
        {
            public Func<uint> min;
            public Func<uint> max;
            public uint incStep = 1u;
            public uint intStepMult = 10u;

            public override uint ValidateValue(uint value)
            {
                if (min != null)
                    value = Math.Max(value, min());
                if (max != null)
                    value = Math.Min(value, max());
                return value;
            }

            internal override uint FromNative(DebugValue v) => (uint)v.i;
            internal override DebugValue ToNative(uint value) => new DebugValue { i = value };
        }

        public class FloatField : Field<float>
        {
            public Func<float> min;
            public Func<float> max;
            public float incStep = 0.1f;
            public float incStepMult = 10f;
            public int decimals = 3;

            public override float ValidateValue(float value)
            {
                if (min != null)
                    value = Mathf.Max(value, min());
                if (max != null)
                    value = Mathf.Min(value, max());
                return value;
            }

            internal override string OwnText() => GetValue().ToString("F" + decimals, CultureInfo.InvariantCulture);
            internal override float FromNative(DebugValue v) => v.x;
            internal override DebugValue ToNative(float value) => new DebugValue { x = value };
        }

        /// <summary>An index into named values (names as text until the UI's content type comes with the UI).</summary>
        public class EnumField : Field<int>
        {
            public string[] enumNames;
            public int[] enumValues;

            /// <summary>The names and values of an enum type.</summary>
            public Type autoEnum
            {
                set
                {
                    enumNames = Enum.GetNames(value);
                    enumValues = Enum.GetValues(value).Cast<object>().Select(Convert.ToInt32).ToArray();
                }
            }

            public int currentIndex => enumValues == null ? -1 : Array.IndexOf(enumValues, GetValue());

            public override int ValidateValue(int value) => enumValues != null && enumValues.Length > 0 && Array.IndexOf(enumValues, value) < 0 ? enumValues[0] : value;

            internal override string OwnText()
            {
                int i = currentIndex;
                return i >= 0 && enumNames != null && i < enumNames.Length ? enumNames[i] : GetValue().ToString(CultureInfo.InvariantCulture);
            }

            internal override int FromNative(DebugValue v) => (int)v.i;
            internal override DebugValue ToNative(int value) => new DebugValue { i = value };

            internal override void BindNative()
            {
                base.BindNative();
                enumNames = DebugNative.EnumNames(m_Native);
                enumValues = DebugNative.EnumValues(m_Native);
            }
        }

        public class ColorField : Field<Color>
        {
            public bool hdr;
            public bool showAlpha = true;
            public bool showPicker = true;
            public float incStep = 0.025f;
            public float incStepMult = 5f;
            public int decimals = 3;

            public override Color ValidateValue(Color value)
            {
                if (!hdr)
                {
                    value.r = Mathf.Clamp01(value.r);
                    value.g = Mathf.Clamp01(value.g);
                    value.b = Mathf.Clamp01(value.b);
                    value.a = Mathf.Clamp01(value.a);
                }
                return value;
            }

            internal override string OwnText()
            {
                Color c = GetValue();
                return string.Format(CultureInfo.InvariantCulture, "({0:F3}, {1:F3}, {2:F3}, {3:F3})", c.r, c.g, c.b, c.a);
            }

            internal override Color FromNative(DebugValue v) => new Color(v.x, v.y, v.z, v.w);
            internal override DebugValue ToNative(Color value) => new DebugValue { x = value.r, y = value.g, z = value.b, w = value.a };
        }

        public class Vector2Field : Field<Vector2>
        {
            internal override Vector2 FromNative(DebugValue v) => new Vector2(v.x, v.y);
            internal override DebugValue ToNative(Vector2 value) => new DebugValue { x = value.x, y = value.y };
        }

        public class Vector3Field : Field<Vector3>
        {
            internal override Vector3 FromNative(DebugValue v) => new Vector3(v.x, v.y, v.z);
            internal override DebugValue ToNative(Vector3 value) => new DebugValue { x = value.x, y = value.y, z = value.z };
        }

        public class Vector4Field : Field<Vector4>
        {
            internal override Vector4 FromNative(DebugValue v) => new Vector4(v.x, v.y, v.z, v.w);
            internal override DebugValue ToNative(Vector4 value) => new DebugValue { x = value.x, y = value.y, z = value.z, w = value.w };
        }

        /// <summary>Widgets under a name.</summary>
        public class Container : Widget, IContainer
        {
            readonly ObservableList<Widget> m_Children;
            uint m_Version = uint.MaxValue;

            public ObservableList<Widget> children
            {
                get
                {
                    if (m_Wraps)
                        Reload();
                    return m_Children;
                }
            }

            public Container() : this("", new ObservableList<Widget>()) { }

            public Container(string displayName, ObservableList<Widget> children)
            {
                this.displayName = displayName;
                m_Children = children;
                children.ItemAdded += OnItemAdded;
                children.ItemRemoved += OnItemRemoved;
                foreach (Widget w in children)
                    Adopt(w);
            }

            public override Panel panel
            {
                get => base.panel;
                internal set
                {
                    m_Panel = value;
                    foreach (Widget w in m_Children)
                        w.panel = value;
                }
            }

            // A C++ container's widgets as the native list has them.
            void Reload()
            {
                uint version = DebugNative.Version(m_Native);
                if (version == m_Version)
                    return;
                m_Version = version;
                m_Children.Reload(NativeWidgets.ChildrenOf(m_Native));
            }

            void Adopt(Widget w)
            {
                if (w == null)
                    return;
                w.panel = m_Panel;
                w.parent = this;
                w.GenerateQueryPath();
            }

            protected virtual void OnItemAdded(ObservableList<Widget> sender, ListChangedEventArgs<Widget> e)
            {
                Adopt(e.item);
                if (m_Native != 0)
                {
                    Inserted(m_Native, e.index, e.item);
                    if (m_Wraps)
                        m_Version = DebugNative.Version(m_Native);
                }
                else
                    m_Panel?.SetDirty();
            }

            protected virtual void OnItemRemoved(ObservableList<Widget> sender, ListChangedEventArgs<Widget> e)
            {
                if (e.item != null)
                {
                    e.item.panel = null;
                    e.item.parent = null;
                }
                if (m_Native != 0)
                {
                    Removed(m_Native, e.item);
                    if (m_Wraps)
                        m_Version = DebugNative.Version(m_Native);
                }
                else
                    m_Panel?.SetDirty();
            }

            internal override void AttachChildren()
            {
                for (int i = 0; i < m_Children.Count; i++)
                    Inserted(m_Native, i, m_Children[i]);
            }

            internal override void ReleaseWrapped()
            {
                foreach (Widget w in m_Children)
                    if (w != null)
                    {
                        if (w.m_Wraps)
                            DebugNative.Remove(m_Native, w.m_Native);
                        else
                            w.ReleaseWrapped();
                    }
            }

            internal override void GenerateQueryPath()
            {
                base.GenerateQueryPath();
                foreach (Widget w in m_Children)
                    w.GenerateQueryPath();
            }

            public override int GetHashCode()
            {
                if (m_Wraps)
                    return base.GetHashCode();
                int hash = base.GetHashCode();
                foreach (Widget w in m_Children)
                    hash = hash * 23 + w.GetHashCode();
                return hash;
            }
        }

        /// <summary>A container that opens and closes.</summary>
        public class Foldout : Container, IValueField
        {
            public bool opened;
            public bool isReadOnly => false;
            public string[] columnLabels;

            public Foldout() { }
            public Foldout(string displayName, ObservableList<Widget> children, string[] columnLabels = null) : base(displayName, children) => this.columnLabels = columnLabels;

            public bool GetValue() => m_Wraps ? DebugNative.ReadValue(m_Native).i != 0 : opened;
            object IValueField.GetValue() => GetValue();
            public void SetValue(object value) => SetValue((bool)value);
            public object ValidateValue(object value) => value;

            public void SetValue(bool value)
            {
                opened = value;
                if (m_Wraps)
                    DebugNative.WriteValue(m_Native, new DebugValue { i = value ? 1 : 0 });
            }

            internal override string OwnText() => opened ? "open" : "closed";
            internal override bool OwnOpen => opened;

            internal override void BindNative() => opened = DebugNative.ReadValue(m_Native).i != 0;
        }

        /// <summary>A page of the debug display (the native core's panel).</summary>
        public class Panel : IContainer, IComparable<Panel>
        {
            internal readonly ulong m_Native;
            readonly ObservableList<Widget> m_Children = new ObservableList<Widget>();
            uint m_Version = uint.MaxValue;

            public Panel() : this(DebugNative.NewPanel()) { }

            internal Panel(ulong id)
            {
                DebugCallbacks.Ensure();
                m_Native = id;
                NativeWidgets.Remember(id, this);
                DebugNative.WatchPanel(id);
                m_Children.ItemAdded += OnItemAdded;
                m_Children.ItemRemoved += OnItemRemoved;
            }

            public Flags flags
            {
                get => (Flags)DebugNative.Flags(m_Native);
                set => DebugNative.SetFlags(m_Native, (int)value);
            }

            public string displayName
            {
                get => DebugNative.DisplayName(m_Native);
                set => DebugNative.SetDisplayName(m_Native, value ?? "");
            }

            public int groupIndex
            {
                get => DebugNative.GroupIndex(m_Native);
                set => DebugNative.SetGroupIndex(m_Native, value);
            }

            public string queryPath => displayName;
            public bool isEditorOnly => (flags & Flags.EditorOnly) != 0;
            public bool isRuntimeOnly => (flags & Flags.RuntimeOnly) != 0;

            public ObservableList<Widget> children
            {
                get
                {
                    uint version = DebugNative.Version(m_Native);
                    if (version != m_Version)
                    {
                        m_Version = version;
                        m_Children.Reload(NativeWidgets.ChildrenOf(m_Native));
                    }
                    return m_Children;
                }
            }

            public event Action<Panel> onSetDirty = delegate { };

            internal void RaiseDirty() => onSetDirty(this);

            protected virtual void OnItemAdded(ObservableList<Widget> sender, ListChangedEventArgs<Widget> e)
            {
                if (e.item != null)
                {
                    e.item.panel = this;
                    e.item.parent = this;
                    e.item.GenerateQueryPath();
                }
                Widget.Inserted(m_Native, e.index, e.item);
                m_Version = DebugNative.Version(m_Native);
            }

            protected virtual void OnItemRemoved(ObservableList<Widget> sender, ListChangedEventArgs<Widget> e)
            {
                if (e.item != null)
                {
                    e.item.panel = null;
                    e.item.parent = null;
                }
                Widget.Removed(m_Native, e.item);
                m_Version = DebugNative.Version(m_Native);
            }

            public void SetDirty() => DebugNative.SetDirty(m_Native);

            public bool Contains(string queryPath) => DebugNative.Contains(m_Native, queryPath ?? "");

            public int CompareTo(Panel other) => other == null ? 1 : groupIndex.CompareTo(other.groupIndex);

            public override int GetHashCode() => DebugNative.Hash(m_Native);
        }
    }

    // The C# objects of native widgets: a C# widget's own object, a C++ widget's wrapper of the C# class of its kind.
    static class NativeWidgets
    {
        static readonly Dictionary<string, Func<object>> s_Kinds = new Dictionary<string, Func<object>>
        {
            ["Button"] = () => new DebugUI.Button(),
            ["Value"] = () => new DebugUI.Value(),
            ["BoolField"] = () => new DebugUI.BoolField(),
            ["IntField"] = () => new DebugUI.IntField(),
            ["UIntField"] = () => new DebugUI.UIntField(),
            ["FloatField"] = () => new DebugUI.FloatField(),
            ["EnumField"] = () => new DebugUI.EnumField(),
            ["ColorField"] = () => new DebugUI.ColorField(),
            ["Vector2Field"] = () => new DebugUI.Vector2Field(),
            ["Vector3Field"] = () => new DebugUI.Vector3Field(),
            ["Vector4Field"] = () => new DebugUI.Vector4Field(),
            ["Container"] = () => new DebugUI.Container(),
            ["Foldout"] = () => new DebugUI.Foldout(),
        };

        static readonly Dictionary<ulong, object> s_Wrappers = new Dictionary<ulong, object>();
        static int s_PruneAt = 64;

        internal static void Remember(ulong id, object wrapper) => s_Wrappers[id] = wrapper;

        internal static object Of(ulong id)
        {
            if (id == 0)
                return null;
            ulong handle = DebugNative.ManagedHandle(id);
            if (handle != 0)
                return GCHandle.FromIntPtr((IntPtr)handle).Target;
            if (s_Wrappers.TryGetValue(id, out object known))
                return known;
            if (!DebugNative.IsValid(id))
                return null;
            Prune();
            string kind = DebugNative.Kind(id);
            if (kind == "Panel")
                return new DebugUI.Panel(id);
            if (!s_Kinds.TryGetValue(kind, out Func<object> make))
                return null;
            var w = (DebugUI.Widget)make();
            w.m_Native = id;
            w.m_Wraps = true;
            w.BindNative();
            s_Wrappers[id] = w;
            return w;
        }

        internal static IEnumerable<DebugUI.Widget> ChildrenOf(ulong container)
        {
            int count = DebugNative.ChildCount(container);
            var list = new List<DebugUI.Widget>(count);
            for (int i = 0; i < count; i++)
                if (Of(DebugNative.ChildAt(container, i)) is DebugUI.Widget w)
                    list.Add(w);
            return list;
        }

        // Wrappers of widgets the native side destroyed leave as the cache grows.
        static void Prune()
        {
            if (s_Wrappers.Count < s_PruneAt)
                return;
            var dead = new List<ulong>();
            foreach (ulong id in s_Wrappers.Keys)
                if (!DebugNative.IsValid(id))
                    dead.Add(id);
            foreach (ulong id in dead)
                s_Wrappers.Remove(id);
            s_PruneAt = Math.Max(64, s_Wrappers.Count * 2);
        }
    }
}
