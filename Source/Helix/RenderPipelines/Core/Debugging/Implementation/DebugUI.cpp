#include <Helix/RenderPipelines/Core/Debugging/DebugUI.h>

#include <Helix/Core/Containers/HashMap.h>
#include <Helix/Core/Hashing/Hash.h>
#include <Helix/Core/Strings/Format.h>

namespace helix::rendering
{
    namespace
    {
        HashMap<u64, DebugUI::Widget*>& Widgets()
        {
            static HashMap<u64, DebugUI::Widget*> widgets;
            return widgets;
        }
        u64 gNextWidget = 0;

        DebugUI::ManagedCallbacks gCallbacks;
        thread_local String tManagedText;

        // A fixed number of decimals.
        String Fixed(f32 value, i32 decimals) { return Format(Format("{{:.{}f}}", decimals < 0 ? 0 : decimals), value); }

        // The C# side's string hash is its own; this one only has to be the same within the process.
        u32 HashText(StringView s) { return u32(Fnv1a64(s.Data(), s.Size())); }

        // A widget of C#: its C# object answers.
        class ManagedWidget final : public DebugUI::Widget
        {
        public:
            explicit ManagedWidget(u64 handle) : handle_(handle) {}
            ~ManagedWidget() override
            {
                if (gCallbacks.free)
                    gCallbacks.free(handle_);
            }
            StringView kind() const override { return "Managed"; }
            bool isHidden() const override { return gCallbacks.hidden && gCallbacks.hidden(handle_); }
            bool ValueText(String& out) const override
            {
                if (!gCallbacks.text || !gCallbacks.text(handle_))
                    return false;
                out = tManagedText;
                return true;
            }
            u64 managedHandle() const override { return handle_; }

        private:
            u64 handle_;
        };

        class ManagedContainer final : public DebugUI::Container
        {
        public:
            explicit ManagedContainer(u64 handle) : handle_(handle) {}
            ~ManagedContainer() override
            {
                if (gCallbacks.free)
                    gCallbacks.free(handle_);
            }
            StringView kind() const override { return "Managed"; }
            bool isHidden() const override { return gCallbacks.hidden && gCallbacks.hidden(handle_); }
            bool IsOpen() const override { return !gCallbacks.open || gCallbacks.open(handle_); }
            bool ValueText(String& out) const override
            {
                if (!gCallbacks.text || !gCallbacks.text(handle_))
                    return false;
                out = tManagedText;
                return true;
            }
            u64 managedHandle() const override { return handle_; }

        private:
            u64 handle_;
        };
    }

    // ------------------------------------------------------------------------------------------------ Widget

    DebugUI::Widget::Widget() : id_(++gNextWidget) { Widgets().Set(id_, this); }

    DebugUI::Widget::~Widget() { Widgets().Remove(id_); }

    DebugUI::Widget* DebugUI::Widget::Find(u64 id)
    {
        Widget** w = Widgets().Find(id);
        return w ? *w : nullptr;
    }

    void DebugUI::Widget::SetDisplayName(StringView name) { displayName_ = String(name); }

    void DebugUI::Widget::GenerateQueryPath()
    {
        queryPath_ = String(displayName_.View().Trimmed());
        if (parent_ && !parent_->queryPath().Empty())
            queryPath_ = Format("{} -> {}", parent_->queryPath(), queryPath_);
    }

    u32 DebugUI::Widget::Hash() const { return HashText(queryPath_) ^ (isHidden() ? 1u : 0u); }

    bool DebugUI::Value::ValueText(String& out) const
    {
        out = getter ? getter() : String();
        return true;
    }

    bool DebugUI::BoolField::ValueText(String& out) const
    {
        out = String(GetValue() ? "True" : "False");
        return true;
    }

    i32 DebugUI::IntField::ValidateValue(i32 value) const
    {
        if (min)
            value = Max(value, min());
        if (max)
            value = Min(value, max());
        return value;
    }

    bool DebugUI::IntField::ValueText(String& out) const
    {
        out = Format("{}", GetValue());
        return true;
    }

    u32 DebugUI::UIntField::ValidateValue(u32 value) const
    {
        if (min)
            value = Max(value, min());
        if (max)
            value = Min(value, max());
        return value;
    }

    bool DebugUI::UIntField::ValueText(String& out) const
    {
        out = Format("{}", GetValue());
        return true;
    }

    f32 DebugUI::FloatField::ValidateValue(f32 value) const
    {
        if (min)
            value = Max(value, min());
        if (max)
            value = Min(value, max());
        return value;
    }

    bool DebugUI::FloatField::ValueText(String& out) const
    {
        out = Fixed(GetValue(), decimals);
        return true;
    }

    void DebugUI::EnumField::SetEnum(Span<const StringView> names, Span<const i32> values)
    {
        enumNames.Clear();
        enumValues.Clear();
        for (StringView n : names)
            enumNames.Push(String(n));
        for (i32 v : values)
            enumValues.Push(v);
    }

    i32 DebugUI::EnumField::currentIndex() const
    {
        i32 value = GetValue();
        for (usize i = 0; i < enumValues.Size(); i++)
            if (enumValues[i] == value)
                return i32(i);
        return -1;
    }

    i32 DebugUI::EnumField::ValidateValue(i32 value) const
    {
        if (enumValues.Empty())
            return value;
        for (i32 v : enumValues)
            if (v == value)
                return value;
        return enumValues[0];
    }

    bool DebugUI::EnumField::ValueText(String& out) const
    {
        i32 i = currentIndex();
        out = i >= 0 && usize(i) < enumNames.Size() ? enumNames[usize(i)] : Format("{}", GetValue());
        return true;
    }

    Color DebugUI::ColorField::ValidateValue(Color value) const
    {
        if (!hdr)
        {
            value.r = Clamp(value.r, 0.0f, 1.0f);
            value.g = Clamp(value.g, 0.0f, 1.0f);
            value.b = Clamp(value.b, 0.0f, 1.0f);
            value.a = Clamp(value.a, 0.0f, 1.0f);
        }
        return value;
    }

    bool DebugUI::ColorField::ValueText(String& out) const
    {
        Color c = GetValue();
        out = Format("({:.3f}, {:.3f}, {:.3f}, {:.3f})", c.r, c.g, c.b, c.a);
        return true;
    }

    bool DebugUI::Vector2Field::ValueText(String& out) const
    {
        Vector2 v = GetValue();
        out = Format("({:.2f}, {:.2f})", v.x, v.y);
        return true;
    }

    bool DebugUI::Vector3Field::ValueText(String& out) const
    {
        Vector3 v = GetValue();
        out = Format("({:.2f}, {:.2f}, {:.2f})", v.x, v.y, v.z);
        return true;
    }

    bool DebugUI::Vector4Field::ValueText(String& out) const
    {
        Vector4 v = GetValue();
        out = Format("({:.2f}, {:.2f}, {:.2f}, {:.2f})", v.x, v.y, v.z, v.w);
        return true;
    }

    // ------------------------------------------------------------------------------------------------ containers

    DebugUI::Container::~Container()
    {
        Array<Widget*> children = Move(children_);
        for (Widget* w : children)
        {
            w->parent_ = nullptr;
            delete w;
        }
    }

    void DebugUI::Container::Insert(u32 index, Widget* widget)
    {
        if (!widget || widget == this)
            return;
        if (widget->parent_)
            widget->parent_->Remove(widget);
        index = Min(index, u32(children_.Size()));
        children_.Insert(index, widget);
        widget->parent_ = this;
        widget->SetPanel(panel_);
        widget->GenerateQueryPath();
        version_++;
        if (panel_)
            panel_->SetDirty();
    }

    bool DebugUI::Container::Remove(Widget* widget)
    {
        for (usize i = 0; i < children_.Size(); i++)
            if (children_[i] == widget)
            {
                children_.RemoveAt(i);
                widget->parent_ = nullptr;
                widget->SetPanel(nullptr);
                widget->GenerateQueryPath();
                version_++;
                if (panel_)
                    panel_->SetDirty();
                return true;
            }
        return false;
    }

    void DebugUI::Container::GenerateQueryPath()
    {
        Widget::GenerateQueryPath();
        for (Widget* w : children_)
            w->GenerateQueryPath();
    }

    void DebugUI::Container::SetPanel(Panel* panel)
    {
        panel_ = panel;
        for (Widget* w : children_)
            w->SetPanel(panel);
    }

    u32 DebugUI::Container::Hash() const
    {
        u32 hash = Widget::Hash();
        for (Widget* w : children_)
            hash = hash * 23 + w->Hash();
        return hash;
    }

    bool DebugUI::Foldout::ValueText(String& out) const
    {
        out = String(opened ? "open" : "closed");
        return true;
    }

    DebugUI::Panel::Panel() { panel_ = this; }

    void DebugUI::Panel::GenerateQueryPath()
    {
        queryPath_ = displayName_;
        for (Widget* w : children())
            w->GenerateQueryPath();
    }

    void DebugUI::Panel::SetDirty()
    {
        GenerateQueryPath();
        onSetDirty_.Invoke(this);
    }

    bool DebugUI::Panel::Contains(StringView queryPath) const { return DebugUI::Find(children(), queryPath) != nullptr; }

    u32 DebugUI::Panel::Hash() const
    {
        u32 hash = 17;
        for (Widget* w : children())
            hash = hash * 23 + w->Hash();
        return hash;
    }

    // ------------------------------------------------------------------------------------------------ C# widgets

    void DebugUI::SetManagedCallbacks(const ManagedCallbacks& callbacks) { gCallbacks = callbacks; }

    const DebugUI::ManagedCallbacks& DebugUI::managedCallbacks() { return gCallbacks; }

    void DebugUI::SetManagedText(StringView text) { tManagedText = String(text); }

    DebugUI::Widget* DebugUI::NewManaged(u64 handle, bool container)
    {
        if (container)
            return new ManagedContainer(handle);
        return new ManagedWidget(handle);
    }

    DebugUI::Widget* DebugUI::Find(Span<Widget* const> widgets, StringView queryPath)
    {
        for (Widget* w : widgets)
        {
            if (w->queryPath() == queryPath)
                return w;
            if (Container* c = w->AsContainer())
                if (Widget* inner = Find(c->children(), queryPath))
                    return inner;
        }
        return nullptr;
    }
}
