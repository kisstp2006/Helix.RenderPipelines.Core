#include <Helix/RenderPipelines/Core/Native/DebugNative.h>

#include <bit>

namespace helix::rendering
{
    namespace
    {
        template <class F>
        F As(u64 f)
        {
            return std::bit_cast<F>(uptr(f));
        }

        struct ManagerCallbacks
        {
            void (*dirty)(bool) = nullptr;
            void (*reset)() = nullptr;
            void (*display)(bool) = nullptr;
            void (*panelDirty)(u64) = nullptr;
            void (*dataReset)(u64) = nullptr;
        } gManager;

        // A C# data block: C# resets it.
        class ManagedDebugData final : public IDebugData
        {
        public:
            explicit ManagedDebugData(u64 h) : handle(h) {}
            Function<void()> GetReset() override
            {
                u64 h = handle;
                return [h] {
                    if (gManager.dataReset)
                        gManager.dataReset(h);
                };
            }
            u64 handle;
        };

        Array<ManagedDebugData*>& ManagedData()
        {
            static Array<ManagedDebugData*> data;
            return data;
        }

        Array<u64>& Items()
        {
            static Array<u64> items;
            return items;
        }

        DebugUI::Widget* W(u64 id) { return DebugUI::Widget::Find(id); }

        DebugUI::Container* C(u64 id)
        {
            DebugUI::Widget* w = W(id);
            return w ? w->AsContainer() : nullptr;
        }

        // A widget that is a panel (by its kind).
        DebugUI::Panel* P(u64 id)
        {
            DebugUI::Widget* w = W(id);
            return w && w->kind() == StringView("Panel") ? static_cast<DebugUI::Panel*>(w) : nullptr;
        }

        void OnDirty(void*, bool full)
        {
            if (gManager.dirty)
                gManager.dirty(full);
        }

        void OnReset(void*)
        {
            if (gManager.reset)
                gManager.reset();
        }

        void OnDisplay(void*, bool shown)
        {
            if (gManager.display)
                gManager.display(shown);
        }

        void OnPanelDirty(void*, DebugUI::Panel* panel)
        {
            if (gManager.panelDirty)
                gManager.panelDirty(panel->Id());
        }
    }

    void DebugNative::SetWidgetCallbacks(u64 text, u64 hidden, u64 open, u64 free)
    {
        DebugUI::ManagedCallbacks c;
        c.text = As<decltype(c.text)>(text);
        c.hidden = As<decltype(c.hidden)>(hidden);
        c.open = As<decltype(c.open)>(open);
        c.free = As<decltype(c.free)>(free);
        DebugUI::SetManagedCallbacks(c);
    }

    void DebugNative::SetManagerCallbacks(u64 dirty, u64 reset, u64 display, u64 panelDirty, u64 dataReset)
    {
        bool first = !gManager.dirty;
        gManager.dirty = As<decltype(gManager.dirty)>(dirty);
        gManager.reset = As<decltype(gManager.reset)>(reset);
        gManager.display = As<decltype(gManager.display)>(display);
        gManager.panelDirty = As<decltype(gManager.panelDirty)>(panelDirty);
        gManager.dataReset = As<decltype(gManager.dataReset)>(dataReset);
        if (!first)
            return;
        DebugManager& m = DebugManager::instance();
        m.onSetDirty().Add(&OnDirty);
        m.resetData().Add(&OnReset);
        m.onDisplayRuntimeUIChanged().Add(&OnDisplay);
    }

    void DebugNative::Destroy(u64 widget)
    {
        DebugUI::Widget* w = W(widget);
        if (!w)
            return;
        if (DebugUI::Container* parent = w->parent())
            parent->Remove(w);
        delete w;
    }

    String DebugNative::Kind(u64 widget)
    {
        DebugUI::Widget* w = W(widget);
        return w ? String(w->kind()) : String();
    }

    u64 DebugNative::ManagedHandle(u64 widget)
    {
        DebugUI::Widget* w = W(widget);
        return w ? w->managedHandle() : 0;
    }

    String DebugNative::DisplayName(u64 widget)
    {
        DebugUI::Widget* w = W(widget);
        return w ? String(w->displayName()) : String();
    }

    void DebugNative::SetDisplayName(u64 widget, StringView name)
    {
        if (DebugUI::Widget* w = W(widget))
            w->SetDisplayName(name);
    }

    String DebugNative::Tooltip(u64 widget)
    {
        DebugUI::Widget* w = W(widget);
        return w ? String(w->tooltip()) : String();
    }

    void DebugNative::SetTooltip(u64 widget, StringView tooltip)
    {
        if (DebugUI::Widget* w = W(widget))
            w->SetTooltip(tooltip);
    }

    i32 DebugNative::Flags(u64 widget)
    {
        DebugUI::Widget* w = W(widget);
        return w ? i32(w->flags) : 0;
    }

    void DebugNative::SetFlags(u64 widget, i32 flags)
    {
        if (DebugUI::Widget* w = W(widget))
            w->flags = DebugUI::Flags(flags);
    }

    String DebugNative::QueryPath(u64 widget)
    {
        DebugUI::Widget* w = W(widget);
        return w ? String(w->queryPath()) : String();
    }

    bool DebugNative::IsHidden(u64 widget)
    {
        DebugUI::Widget* w = W(widget);
        return w && w->isHidden();
    }

    bool DebugNative::HasValueText(u64 widget)
    {
        String text;
        DebugUI::Widget* w = W(widget);
        return w && w->ValueText(text);
    }

    String DebugNative::ValueText(u64 widget)
    {
        String text;
        if (DebugUI::Widget* w = W(widget))
            w->ValueText(text);
        return text;
    }

    u64 DebugNative::Parent(u64 widget)
    {
        DebugUI::Widget* w = W(widget);
        return w && w->parent() ? w->parent()->Id() : 0;
    }

    u64 DebugNative::PanelOf(u64 widget)
    {
        DebugUI::Widget* w = W(widget);
        return w && w->panel() ? w->panel()->Id() : 0;
    }

    DebugValue DebugNative::ReadValue(u64 widget)
    {
        DebugValue v;
        if (DebugUI::Widget* w = W(widget))
            w->ReadValue(v);
        return v;
    }

    void DebugNative::WriteValue(u64 widget, DebugValue value)
    {
        if (DebugUI::Widget* w = W(widget))
            w->WriteValue(value);
    }

    void DebugNative::Press(u64 widget)
    {
        if (DebugUI::Widget* w = W(widget))
            w->Press();
    }

    Array<String> DebugNative::EnumNames(u64 widget)
    {
        DebugUI::Widget* w = W(widget);
        if (!w || w->kind() != StringView("EnumField"))
            return Array<String>();
        return static_cast<DebugUI::EnumField*>(w)->enumNames;
    }

    Array<i32> DebugNative::EnumValues(u64 widget)
    {
        DebugUI::Widget* w = W(widget);
        if (!w || w->kind() != StringView("EnumField"))
            return Array<i32>();
        return static_cast<DebugUI::EnumField*>(w)->enumValues;
    }

    i32 DebugNative::Hash(u64 widget)
    {
        DebugUI::Widget* w = W(widget);
        return w ? i32(w->Hash()) : 0;
    }

    i32 DebugNative::ChildCount(u64 container)
    {
        DebugUI::Container* c = C(container);
        return c ? i32(c->children().Size()) : 0;
    }

    u64 DebugNative::ChildAt(u64 container, i32 index)
    {
        DebugUI::Container* c = C(container);
        return c && index >= 0 && usize(index) < c->children().Size() ? c->children()[usize(index)]->Id() : 0;
    }

    u32 DebugNative::Version(u64 container)
    {
        DebugUI::Container* c = C(container);
        return c ? c->version() : 0;
    }

    void DebugNative::Insert(u64 container, i32 index, u64 widget)
    {
        DebugUI::Container* c = C(container);
        DebugUI::Widget* w = W(widget);
        if (c && w)
            c->Insert(u32(index < 0 ? 0 : index), w);
    }

    bool DebugNative::Remove(u64 container, u64 widget)
    {
        DebugUI::Container* c = C(container);
        DebugUI::Widget* w = W(widget);
        return c && w && c->Remove(w);
    }

    i32 DebugNative::GroupIndex(u64 panel)
    {
        DebugUI::Panel* p = P(panel);
        return p ? p->groupIndex : 0;
    }

    void DebugNative::SetGroupIndex(u64 panel, i32 group)
    {
        if (DebugUI::Panel* p = P(panel))
            p->groupIndex = group;
    }

    void DebugNative::SetDirty(u64 panel)
    {
        if (DebugUI::Panel* p = P(panel))
            p->SetDirty();
    }

    void DebugNative::WatchPanel(u64 panel)
    {
        if (DebugUI::Panel* p = P(panel))
        {
            p->onSetDirty().Remove(&OnPanelDirty, nullptr);
            p->onSetDirty().Add(&OnPanelDirty);
        }
    }

    bool DebugNative::Contains(u64 panel, StringView queryPath)
    {
        DebugUI::Panel* p = P(panel);
        return p && p->Contains(queryPath);
    }

    u64 DebugNative::PanelAt(i32 index)
    {
        Span<DebugUI::Panel* const> panels = DebugManager::instance().panels();
        return index >= 0 && usize(index) < panels.Size() ? panels[usize(index)]->Id() : 0;
    }

    u64 DebugNative::GetPanel(StringView name, bool createIfNull, i32 groupIndex, bool overrideIfExist)
    {
        DebugUI::Panel* p = DebugManager::instance().GetPanel(name, createIfNull, groupIndex, overrideIfExist);
        return p ? p->Id() : 0;
    }

    void DebugNative::RemovePanel(u64 panel)
    {
        if (DebugUI::Panel* p = P(panel))
            DebugManager::instance().RemovePanel(p);
    }

    i32 DebugNative::GetItems(i32 flags)
    {
        Array<DebugUI::Widget*> items;
        DebugManager::instance().GetItems(DebugUI::Flags(flags), items);
        Items().Clear();
        for (DebugUI::Widget* w : items)
            Items().Push(w->Id());
        return i32(Items().Size());
    }

    u64 DebugNative::ItemAt(i32 index) { return index >= 0 && usize(index) < Items().Size() ? Items()[usize(index)] : 0; }

    u64 DebugNative::GetItem(StringView queryPath)
    {
        DebugUI::Widget* w = DebugManager::instance().GetItem(queryPath);
        return w ? w->Id() : 0;
    }

    u64 DebugNative::RegisterManagedData(u64 handle)
    {
        auto* d = new ManagedDebugData(handle);
        ManagedData().Push(d);
        DebugManager::instance().RegisterData(d);
        return u64(uptr(d));
    }

    void DebugNative::UnregisterManagedData(u64 data)
    {
        for (usize i = 0; i < ManagedData().Size(); i++)
            if (u64(uptr(ManagedData()[i])) == data)
            {
                DebugManager::instance().UnregisterData(ManagedData()[i]);
                delete ManagedData()[i];
                ManagedData().RemoveAt(i);
                return;
            }
    }

    String DebugNative::TextViewOf(u64 panel)
    {
        DebugUI::Panel* p = P(panel);
        return p ? DebugUITextView::Render(*p) : String();
    }
}
