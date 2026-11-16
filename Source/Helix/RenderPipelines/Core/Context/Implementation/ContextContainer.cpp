#include <Helix/RenderPipelines/Core/Context/ContextContainer.h>

#include <Helix/Core/Containers/HashMap.h>
#include <Helix/Core/Logging/Log.h>

namespace helix::rendering
{
    namespace
    {
        LogChannel ContextLog{"Rendering"};

        struct Registry
        {
            HashMap<u64, ContextItem*> items;
            HashMap<u64, ContextContainer*> containers;
            u64 next = 0;
        };

        Registry& R()
        {
            static Registry r;
            return r;
        }
    }

    // ---------------------------------------------------------------------------------------------- items

    ContextItem::ContextItem() : id_(++R().next) { R().items.Set(id_, this); }

    ContextItem::~ContextItem()
    {
        scripting::ReleaseWrapper(wrapper_);
        R().items.Remove(id_);
    }

    ContextItem* ContextItem::Find(u64 id)
    {
        ContextItem* const* item = R().items.Find(id);
        return item ? *item : nullptr;
    }

    scripting::TypeId ContextItem::HelixType() const { return scripting::TypeOf<ContextItem>(); }

    // ---------------------------------------------------------------------------------------------- containers

    ContextContainer::ContextContainer() : id_(++R().next) { R().containers.Set(id_, this); }

    ContextContainer::~ContextContainer() { R().containers.Remove(id_); }

    ContextContainer* ContextContainer::FindContainer(u64 id)
    {
        ContextContainer* const* c = R().containers.Find(id);
        return c ? *c : nullptr;
    }

    u64 ContextContainer::KeyOfName(StringView fullName)
    {
        u64 h = 14695981039346656037ull;
        for (char c : fullName)
        {
            h ^= u8(c);
            h *= 1099511628211ull;
        }
        return h;
    }

    ContextItem* ContextContainer::Find(u64 key) const
    {
        for (const Entry& e : entries_)
            if (e.key == key)
                return e.made ? e.item.get() : nullptr;
        return nullptr;
    }

    ContextItem* ContextContainer::Kept(u64 key) const
    {
        for (const Entry& e : entries_)
            if (e.key == key)
                return e.item.get();
        return nullptr;
    }

    void ContextContainer::Add(u64 key, ContextItem* item)
    {
        for (Entry& e : entries_)
            if (e.key == key)
            {
                if (e.item.get() != item)
                    e.item.reset(item);
                e.made = true;
                return;
            }
        Entry& e = entries_.Emplace();
        e.key = key;
        e.item.reset(item);
        e.made = true;
    }

    void ContextContainer::Mark(u64 key)
    {
        for (Entry& e : entries_)
            if (e.key == key)
                e.made = true;
    }

    ContextItem* ContextContainer::Get(u64 key, StringView typeName)
    {
        ContextItem* item = Find(key);
        if (!item)
            HX_LOG_ERROR(ContextLog, "ContextContainer: {} has not been made in this frame (GetOrCreate or Create makes it).", typeName);
        return item;
    }

    void ContextContainer::ReportCreatedTwice(StringView typeName)
    {
        HX_LOG_ERROR(ContextLog, "ContextContainer: {} was made already in this frame (Create makes an item once; Get or GetOrCreate takes it).", typeName);
    }

    void ContextContainer::Dispose()
    {
        for (Entry& e : entries_)
            if (e.made)
            {
                e.item->Reset();
                e.made = false;
            }
    }

    // ---------------------------------------------------------------------------------------------- what C# calls

    u64 ContextNative::Create() { return (new ContextContainer())->Id(); }

    void ContextNative::Destroy(u64 container) { delete ContextContainer::FindContainer(container); }

    ContextItem* ContextNative::Find(u64 container, u64 key)
    {
        ContextContainer* c = ContextContainer::FindContainer(container);
        return c ? c->Find(key) : nullptr;
    }

    ContextItem* ContextNative::Kept(u64 container, u64 key)
    {
        ContextContainer* c = ContextContainer::FindContainer(container);
        return c ? c->Kept(key) : nullptr;
    }

    void ContextNative::Add(u64 container, u64 key, ContextItem* item)
    {
        if (ContextContainer* c = ContextContainer::FindContainer(container); c && item)
            c->Add(key, item);
    }

    void ContextNative::Mark(u64 container, u64 key)
    {
        if (ContextContainer* c = ContextContainer::FindContainer(container))
            c->Mark(key);
    }

    void ContextNative::Dispose(u64 container)
    {
        if (ContextContainer* c = ContextContainer::FindContainer(container))
            c->Dispose();
    }
}
