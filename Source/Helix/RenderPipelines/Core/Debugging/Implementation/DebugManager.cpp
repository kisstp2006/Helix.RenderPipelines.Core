#include <Helix/RenderPipelines/Core/Debugging/DebugManager.h>

#include <algorithm>

namespace helix::rendering
{
    namespace
    {
        char Lower(char c) { return c >= 'A' && c <= 'Z' ? char(c - 'A' + 'a') : c; }

        bool SameIgnoringCase(StringView a, StringView b)
        {
            if (a.Size() != b.Size())
                return false;
            for (usize i = 0; i < a.Size(); i++)
                if (Lower(a[i]) != Lower(b[i]))
                    return false;
            return true;
        }

        void Collect(Span<DebugUI::Widget* const> widgets, DebugUI::Flags flags, Array<DebugUI::Widget*>& out)
        {
            for (DebugUI::Widget* w : widgets)
            {
                if ((i32(w->flags) & i32(flags)) == i32(flags))
                    out.Push(w);
                if (DebugUI::Container* c = w->AsContainer())
                    Collect(c->children(), flags, out);
            }
        }

        void RenderWidgets(Span<DebugUI::Widget* const> widgets, i32 depth, String& out)
        {
            for (DebugUI::Widget* w : widgets)
            {
                if (w->isHidden() || w->isEditorOnly())
                    continue;
                for (i32 i = 0; i < depth * 2; i++)
                    out.Append(" ");
                out.Append(w->displayName());
                String value;
                if (w->ValueText(value))
                    out.Append(": ").Append(value);
                out.Append("\n");
                if (!w->IsOpen())
                    continue;
                if (DebugUI::Container* c = w->AsContainer())
                    RenderWidgets(c->children(), depth + 1, out);
            }
        }

        void RenderPanel(const DebugUI::Panel& panel, String& out)
        {
            out.Append("[").Append(panel.displayName()).Append("]\n");
            RenderWidgets(panel.children(), 1, out);
        }
    }

    DebugManager& DebugManager::instance()
    {
        static DebugManager manager;
        return manager;
    }

    void DebugManager::SetDisplayRuntimeUI(bool value)
    {
        if (displayRuntimeUI_ == value)
            return;
        displayRuntimeUI_ = value;
        onDisplayRuntimeUIChanged_.Invoke(value);
    }

    i32 DebugManager::GetState() const
    {
        u32 hash = 17;
        for (DebugUI::Panel* p : panels_)
            hash = hash * 23 + p->Hash();
        return i32(hash);
    }

    DebugUI::Panel* DebugManager::GetPanel(StringView displayName, bool createIfNull, i32 groupIndex, bool overrideIfExist)
    {
        i32 index = PanelIndex(displayName);
        DebugUI::Panel* p = index >= 0 ? panels_[usize(index)] : nullptr;
        if (p && overrideIfExist)
        {
            RemovePanel(p);
            p = nullptr;
        }
        if (!p && createIfNull)
        {
            p = new DebugUI::Panel();
            p->SetDisplayName(displayName);
            p->groupIndex = groupIndex;
            p->SetDirty();
            p->onSetDirty().Add(&OnPanelDirty, this);
            panels_.Push(p);
            // Stable by group.
            std::stable_sort(panels_.begin(), panels_.end(), [](const DebugUI::Panel* a, const DebugUI::Panel* b) { return a->groupIndex < b->groupIndex; });
            version_++;
            onSetDirty_.Invoke(true);
        }
        return p;
    }

    i32 DebugManager::PanelIndex(StringView displayName) const
    {
        for (usize i = 0; i < panels_.Size(); i++)
            if (SameIgnoringCase(panels_[i]->displayName(), displayName))
                return i32(i);
        return -1;
    }

    void DebugManager::RemovePanel(StringView displayName)
    {
        i32 index = PanelIndex(displayName);
        if (index >= 0)
            RemovePanel(panels_[usize(index)]);
    }

    void DebugManager::RemovePanel(DebugUI::Panel* panel)
    {
        for (usize i = 0; i < panels_.Size(); i++)
            if (panels_[i] == panel)
            {
                panels_.RemoveAt(i);
                panel->onSetDirty().Remove(&OnPanelDirty, this);
                version_++;
                delete panel;
                onSetDirty_.Invoke(true);
                return;
            }
    }

    void DebugManager::OnPanelDirty(void* self, DebugUI::Panel*) { static_cast<DebugManager*>(self)->onSetDirty_.Invoke(false); }

    void DebugManager::GetItems(DebugUI::Flags flags, Array<DebugUI::Widget*>& out) const
    {
        out.Clear();
        for (DebugUI::Panel* p : panels_)
            Collect(p->children(), flags, out);
    }

    DebugUI::Widget* DebugManager::GetItem(StringView queryPath) const
    {
        for (DebugUI::Panel* p : panels_)
            if (DebugUI::Widget* w = DebugUI::Find(p->children(), queryPath))
                return w;
        return nullptr;
    }

    void DebugManager::RegisterData(IDebugData* data)
    {
        if (!data)
            return;
        for (IDebugData* d : data_)
            if (d == data)
                return;
        data_.Push(data);
    }

    void DebugManager::UnregisterData(IDebugData* data)
    {
        for (usize i = 0; i < data_.Size(); i++)
            if (data_[i] == data)
            {
                data_.RemoveAt(i);
                return;
            }
    }

    void DebugManager::Reset()
    {
        resetData_.Invoke();
        Array<IDebugData*> data = data_;
        for (IDebugData* d : data)
            if (Function<void()> reset = d->GetReset())
                reset();
        onSetDirty_.Invoke(true);
    }

    String DebugUITextView::Render()
    {
        String out;
        for (DebugUI::Panel* p : DebugManager::instance().panels())
            RenderPanel(*p, out);
        return out;
    }

    String DebugUITextView::Render(const DebugUI::Panel& panel)
    {
        String out;
        RenderPanel(panel, out);
        return out;
    }
}
