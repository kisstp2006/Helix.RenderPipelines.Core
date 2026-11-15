#pragma once

#include <Helix/Core/Basics/Function.h>
#include <Helix/Core/Basics/Tags.h>
#include <Helix/Core/Containers/Array.h>
#include <Helix/Core/Strings/String.h>
#include <Helix/Engine/Event/Event.h>
#include <Helix/Math/Color.h>
#include <Helix/Math/Vector.h>

#include <type_traits>

// The widgets of the debug display: panels of values and fields bound to getters and setters, in containers. A
// container owns its widgets (they go with it); Remove gives a widget back to the caller. Widgets are found by id (C#
// reaches them so).
namespace helix::rendering
{
    // A widget's value as C# reads and writes a C++ widget's: floats for vectors and colors, i for whole numbers and bools.
    HX_STRUCT(Namespace = "HelixEngine.Rendering.Internal", Internal)
    struct DebugValue
    {
        HX_FIELD() f32 x = 0;
        HX_FIELD() f32 y = 0;
        HX_FIELD() f32 z = 0;
        HX_FIELD() f32 w = 0;
        HX_FIELD() i64 i = 0;
    };

    struct DebugUI
    {
        enum class Flags : i32
        {
            None = 0,
            EditorOnly = 1 << 1,
            RuntimeOnly = 1 << 2,
            EditorForceUpdate = 1 << 3,
            FrequentlyUsed = 1 << 4,
        };

        class Container;
        class Panel;

        class Widget
        {
        public:
            Widget();
            virtual ~Widget();
            Widget(const Widget&) = delete;
            Widget& operator=(const Widget&) = delete;

            static Widget* Find(u64 id);
            u64 Id() const { return id_; }

            // The name of its kind (the C# class C# sees it as).
            virtual StringView kind() const = 0;

            StringView displayName() const { return displayName_; }
            void SetDisplayName(StringView name);
            StringView tooltip() const { return tooltip_; }
            void SetTooltip(StringView tooltip) { tooltip_ = String(tooltip); }
            Flags flags = Flags::None;
            bool isEditorOnly() const { return (i32(flags) & i32(Flags::EditorOnly)) != 0; }
            bool isRuntimeOnly() const { return (i32(flags) & i32(Flags::RuntimeOnly)) != 0; }

            // The names of the containers above it and its own, joined by " -> ".
            StringView queryPath() const { return queryPath_; }
            Container* parent() const { return parent_; }
            Panel* panel() const { return panel_; }

            Function<bool()> isHiddenCallback;
            virtual bool isHidden() const { return isHiddenCallback && isHiddenCallback(); }

            // Its value as the text view shows it (false: it has none).
            virtual bool ValueText(String& out) const
            {
                (void)out;
                return false;
            }
            // The text view shows its widgets (a closed foldout's are left out).
            virtual bool IsOpen() const { return true; }
            virtual Container* AsContainer() { return nullptr; }
            virtual u32 Hash() const;
            // The GC handle of the C# object of a widget of C# (0: a C++ widget).
            virtual u64 managedHandle() const { return 0; }
            // A field's value (false: it has none) and a validated set of it; a button's action.
            virtual bool ReadValue(DebugValue& out) const
            {
                (void)out;
                return false;
            }
            virtual void WriteValue(const DebugValue& value) { (void)value; }
            virtual void Press() {}

        protected:
            friend class Container;
            friend class Panel;
            virtual void GenerateQueryPath();
            virtual void SetPanel(Panel* panel) { panel_ = panel; }

            String displayName_ = String();
            String tooltip_ = String();
            String queryPath_ = String();
            Container* parent_ = nullptr;
            Panel* panel_ = nullptr;
            u64 id_ = 0;
        };

        class Button : public Widget
        {
        public:
            StringView kind() const override { return "Button"; }
            Function<void()> action;
            void Press() override
            {
                if (action)
                    action();
            }
        };

        // A read only value (its text), refreshed refreshRate seconds apart.
        class Value : public Widget
        {
        public:
            StringView kind() const override { return "Value"; }
            Function<String()> getter;
            f32 refreshRate = 0.1f;
            bool ValueText(String& out) const override;
        };

        static DebugValue Pack(bool v) { return {0, 0, 0, 0, v ? 1 : 0}; }
        static DebugValue Pack(i32 v) { return {0, 0, 0, 0, v}; }
        static DebugValue Pack(u32 v) { return {0, 0, 0, 0, i64(v)}; }
        static DebugValue Pack(f32 v) { return {v, 0, 0, 0, 0}; }
        static DebugValue Pack(Vector2 v) { return {v.x, v.y, 0, 0, 0}; }
        static DebugValue Pack(Vector3 v) { return {v.x, v.y, v.z, 0, 0}; }
        static DebugValue Pack(Vector4 v) { return {v.x, v.y, v.z, v.w, 0}; }
        static DebugValue Pack(Color v) { return {v.r, v.g, v.b, v.a, 0}; }
        template <class T>
        static T Unpack(const DebugValue& v)
        {
            if constexpr (std::is_same_v<T, bool>)
                return v.i != 0;
            else if constexpr (std::is_same_v<T, i32> || std::is_same_v<T, u32>)
                return T(v.i);
            else if constexpr (std::is_same_v<T, f32>)
                return v.x;
            else if constexpr (std::is_same_v<T, Vector2>)
                return Vector2(v.x, v.y);
            else if constexpr (std::is_same_v<T, Vector3>)
                return Vector3(v.x, v.y, v.z);
            else if constexpr (std::is_same_v<T, Vector4>)
                return Vector4(v.x, v.y, v.z, v.w);
            else
                return Color(v.x, v.y, v.z, v.w);
        }

        // A value bound to a getter and a setter; set values are validated and reported once they differ.
        template <class T>
        class Field : public Widget
        {
        public:
            bool ReadValue(DebugValue& out) const override
            {
                out = Pack(GetValue());
                return true;
            }
            void WriteValue(const DebugValue& value) override { SetValue(Unpack<T>(value)); }

            Function<T()> getter;
            Function<void(T)> setter;
            Function<void(Field&, T)> onValueChanged;

            virtual T ValidateValue(T value) const { return value; }
            T GetValue() const { return getter ? getter() : T(); }
            void SetValue(T value)
            {
                T v = ValidateValue(value);
                if (!getter || !(v == getter()))
                {
                    if (setter)
                        setter(v);
                    if (onValueChanged)
                        onValueChanged(*this, v);
                }
            }
        };

        class BoolField : public Field<bool>
        {
        public:
            StringView kind() const override { return "BoolField"; }
            bool ValueText(String& out) const override;
        };

        class IntField : public Field<i32>
        {
        public:
            StringView kind() const override { return "IntField"; }
            Function<i32()> min, max;
            i32 incStep = 1;
            i32 intStepMult = 10;
            i32 ValidateValue(i32 value) const override;
            bool ValueText(String& out) const override;
        };

        class UIntField : public Field<u32>
        {
        public:
            StringView kind() const override { return "UIntField"; }
            Function<u32()> min, max;
            u32 incStep = 1;
            u32 intStepMult = 10;
            u32 ValidateValue(u32 value) const override;
            bool ValueText(String& out) const override;
        };

        class FloatField : public Field<f32>
        {
        public:
            StringView kind() const override { return "FloatField"; }
            Function<f32()> min, max;
            f32 incStep = 0.1f;
            f32 incStepMult = 10;
            i32 decimals = 3;
            f32 ValidateValue(f32 value) const override;
            bool ValueText(String& out) const override;
        };

        // An index into named values.
        class EnumField : public Field<i32>
        {
        public:
            StringView kind() const override { return "EnumField"; }
            Array<String> enumNames = Array<String>();
            Array<i32> enumValues = Array<i32>();
            void SetEnum(Span<const StringView> names, Span<const i32> values);
            // The index of the value among enumValues (-1: not there).
            i32 currentIndex() const;
            i32 ValidateValue(i32 value) const override;
            bool ValueText(String& out) const override;
        };

        class ColorField : public Field<Color>
        {
        public:
            StringView kind() const override { return "ColorField"; }
            bool hdr = false;
            bool showAlpha = true;
            bool showPicker = true;
            f32 incStep = 0.025f;
            f32 incStepMult = 5;
            i32 decimals = 3;
            Color ValidateValue(Color value) const override;
            bool ValueText(String& out) const override;
        };

        class Vector2Field : public Field<Vector2>
        {
        public:
            StringView kind() const override { return "Vector2Field"; }
            bool ValueText(String& out) const override;
        };

        class Vector3Field : public Field<Vector3>
        {
        public:
            StringView kind() const override { return "Vector3Field"; }
            bool ValueText(String& out) const override;
        };

        class Vector4Field : public Field<Vector4>
        {
        public:
            StringView kind() const override { return "Vector4Field"; }
            bool ValueText(String& out) const override;
        };

        // Widgets under a name.
        class Container : public Widget
        {
        public:
            Container() = default;
            explicit Container(StringView name) { SetDisplayName(name); }
            ~Container() override;

            StringView kind() const override { return "Container"; }
            Span<Widget* const> children() const { return Span<Widget* const>(children_.Data(), children_.Size()); }
            // Takes the widget (from the container that had it).
            void Add(Widget* widget) { Insert(u32(children_.Size()), widget); }
            void Insert(u32 index, Widget* widget);
            // Gives the widget back to the caller (false: it is not a child).
            bool Remove(Widget* widget);
            // Changes with every Insert and Remove (C#'s view of the list).
            u32 version() const { return version_; }

            Container* AsContainer() override { return this; }
            u32 Hash() const override;

        protected:
            void GenerateQueryPath() override;
            void SetPanel(Panel* panel) override;

        private:
            Array<Widget*> children_ = Array<Widget*>();
            u32 version_ = 0;
        };

        // A container that opens and closes.
        class Foldout : public Container
        {
        public:
            Foldout() = default;
            explicit Foldout(StringView name) : Container(name) {}
            StringView kind() const override { return "Foldout"; }
            bool opened = false;
            Array<String> columnLabels = Array<String>();
            bool IsOpen() const override { return opened; }
            bool ReadValue(DebugValue& out) const override
            {
                out = Pack(opened);
                return true;
            }
            void WriteValue(const DebugValue& value) override { opened = value.i != 0; }
            bool ValueText(String& out) const override;
        };

        // A page of the debug display.
        class Panel : public Container
        {
        public:
            Panel();
            StringView kind() const override { return "Panel"; }
            i32 groupIndex = 0;
            // Its widgets' query paths made again; the manager hears of it.
            void SetDirty();
            bool Contains(StringView queryPath) const;
            engine::Event<Panel*>& onSetDirty() { return onSetDirty_; }
            u32 Hash() const override;

        protected:
            void GenerateQueryPath() override;

        private:
            engine::Event<Panel*> onSetDirty_;
        };

        // A widget of C#: its C# object (a GC handle) answers its text, hidden state and whether it is open; C# sets its
        // name and flags. Destroyed when it leaves its container (C# hears of it).
        struct ManagedCallbacks
        {
            // Writes the text through DebugNative's ReturnText (false: none).
            bool (*text)(u64 handle) = nullptr;
            bool (*hidden)(u64 handle) = nullptr;
            bool (*open)(u64 handle) = nullptr;
            // The widget is gone: C# forgets it and frees the handle.
            void (*free)(u64 handle) = nullptr;
        };
        static void SetManagedCallbacks(const ManagedCallbacks& callbacks);
        static const ManagedCallbacks& managedCallbacks();
        // The text a ManagedCallbacks.text call gives (thread's last one).
        static void SetManagedText(StringView text);
        static Widget* NewManaged(u64 handle, bool container);

        // Finds a widget by query path among widgets and their children.
        static Widget* Find(Span<Widget* const> widgets, StringView queryPath);
    };

    inline DebugUI::Flags operator|(DebugUI::Flags a, DebugUI::Flags b) { return DebugUI::Flags(i32(a) | i32(b)); }
}
