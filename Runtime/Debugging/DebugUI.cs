using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

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

    /// <summary>The widgets of the debug display: panels of values and fields bound to getters and setters.</summary>
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

            public virtual Panel panel
            {
                get => m_Panel;
                internal set => m_Panel = value;
            }

            public virtual IContainer parent
            {
                get => m_Parent;
                internal set => m_Parent = value;
            }

            public Flags flags { get; set; }
            public string displayName { get; set; } = "";
            public string tooltip { get; set; }
            /// <summary>The names of the containers above it and its own, joined by " -> ".</summary>
            public string queryPath { get; private set; } = "";
            public bool isEditorOnly => (flags & Flags.EditorOnly) != 0;
            public bool isRuntimeOnly => (flags & Flags.RuntimeOnly) != 0;
            public Func<bool> isHiddenCallback;
            public bool isHidden => isHiddenCallback?.Invoke() ?? false;

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

            public override int GetHashCode() => (queryPath ?? "").GetHashCode() ^ isHidden.GetHashCode();
        }

        public class Button : Widget
        {
            public Action action { get; set; }
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
        }

        public class BoolField : Field<bool> { }

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
        }

        public class Vector2Field : Field<Vector2> { }
        public class Vector3Field : Field<Vector3> { }
        public class Vector4Field : Field<Vector4> { }

        /// <summary>Widgets under a name.</summary>
        public class Container : Widget, IContainer
        {
            public ObservableList<Widget> children { get; }

            public Container() : this("", new ObservableList<Widget>()) { }

            public Container(string displayName, ObservableList<Widget> children)
            {
                this.displayName = displayName;
                this.children = children;
                children.ItemAdded += OnItemAdded;
                children.ItemRemoved += OnItemRemoved;
                foreach (Widget w in children)
                    Adopt(w);
            }

            public override Panel panel
            {
                get => m_Panel;
                internal set
                {
                    m_Panel = value;
                    foreach (Widget w in children)
                        w.panel = value;
                }
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
                m_Panel?.SetDirty();
            }

            protected virtual void OnItemRemoved(ObservableList<Widget> sender, ListChangedEventArgs<Widget> e)
            {
                if (e.item != null)
                {
                    e.item.panel = null;
                    e.item.parent = null;
                }
                m_Panel?.SetDirty();
            }

            internal override void GenerateQueryPath()
            {
                base.GenerateQueryPath();
                foreach (Widget w in children)
                    w.GenerateQueryPath();
            }

            public override int GetHashCode()
            {
                int hash = base.GetHashCode();
                foreach (Widget w in children)
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

            public bool GetValue() => opened;
            object IValueField.GetValue() => GetValue();
            public void SetValue(object value) => SetValue((bool)value);
            public object ValidateValue(object value) => value;
            public void SetValue(bool value) => opened = value;
        }

        /// <summary>A page of the debug display.</summary>
        public class Panel : IContainer, IComparable<Panel>
        {
            public Flags flags { get; set; }
            public string displayName { get; set; }
            public int groupIndex { get; set; }
            public string queryPath => displayName;
            public bool isEditorOnly => (flags & Flags.EditorOnly) != 0;
            public bool isRuntimeOnly => (flags & Flags.RuntimeOnly) != 0;
            public ObservableList<Widget> children { get; } = new ObservableList<Widget>();
            public event Action<Panel> onSetDirty = delegate { };

            public Panel()
            {
                children.ItemAdded += OnItemAdded;
                children.ItemRemoved += OnItemRemoved;
            }

            protected virtual void OnItemAdded(ObservableList<Widget> sender, ListChangedEventArgs<Widget> e)
            {
                if (e.item != null)
                {
                    e.item.panel = this;
                    e.item.parent = this;
                    e.item.GenerateQueryPath();
                }
                SetDirty();
            }

            protected virtual void OnItemRemoved(ObservableList<Widget> sender, ListChangedEventArgs<Widget> e)
            {
                if (e.item != null)
                {
                    e.item.panel = null;
                    e.item.parent = null;
                }
                SetDirty();
            }

            public void SetDirty()
            {
                foreach (Widget w in children)
                    w.GenerateQueryPath();
                onSetDirty(this);
            }

            public bool Contains(string queryPath) => Find(children, queryPath) != null;

            internal static Widget Find(IEnumerable<Widget> widgets, string queryPath)
            {
                foreach (Widget w in widgets)
                {
                    if (w.queryPath == queryPath)
                        return w;
                    if (w is IContainer c && Find(c.children, queryPath) is Widget inner)
                        return inner;
                }
                return null;
            }

            public int CompareTo(Panel other) => other == null ? 1 : groupIndex.CompareTo(other.groupIndex);

            public override int GetHashCode()
            {
                int hash = 17;
                foreach (Widget w in children)
                    hash = hash * 23 + w.GetHashCode();
                return hash;
            }
        }
    }
}
