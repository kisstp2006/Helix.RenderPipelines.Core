#pragma once

#include <Helix/Core/Containers/Array.h>
#include <Helix/Core/Strings/StringView.h>
#include <Helix/Scripting/Bindings/Interop.h>
#include <Helix/Scripting/Bindings/Tags.h>
#include <Helix/Scripting/Types/Type.h>

#include <memory>

// The data of a frame by its type: items a pipeline makes once and fills again each frame (the camera's, the lights',
// the resources'), and items of passes. C++ items derive from ContextItem; C# items derive from its C# class (their
// objects are native ones with the C# overrides).
namespace helix::rendering
{
    HX_CLASS(Root, Namespace = "HelixEngine.Rendering")
    class ContextItem
    {
    public:
        HX_FUNCTION(Constructor) ContextItem();
        virtual ~ContextItem();
        ContextItem(const ContextItem&) = delete;
        ContextItem& operator=(const ContextItem&) = delete;

        // Back to the state of a new item (the container calls it when the frame ends).
        HX_FUNCTION() virtual void Reset() = 0;

        u64 Id() const { return id_; }
        u64& Wrapper() { return wrapper_; }
        static ContextItem* Find(u64 id);
        virtual scripting::TypeId HelixType() const;

    private:
        u64 id_ = 0;
        u64 wrapper_ = 0;
    };

    // Items by type: each made once and kept; Dispose resets the items that were made (they are made again, reused, by
    // the next GetOrCreate or Create).
    class ContextContainer
    {
    public:
        ContextContainer();
        ~ContextContainer();
        ContextContainer(const ContextContainer&) = delete;
        ContextContainer& operator=(const ContextContainer&) = delete;

        // The key of a type: the FNV-1a hash of its full C# name for types bound to C# (C# computes the same), else of
        // its C++ name.
        template <class T>
        static u64 KeyOf()
        {
            scripting::TypeId t = scripting::TypeOf<T>();
            return KeyOfName(t.IsValid() ? scripting::GetType(t)->fullName : StringView(scripting::detail::NativeKey<T>()));
        }
        static u64 KeyOfName(StringView fullName);

        // The item of the type made in this frame (an error and null when it was not).
        template <class T>
        T* Get()
        {
            return static_cast<T*>(Get(KeyOf<T>(), TypeName<T>()));
        }
        // The item, made (or reused) when it was not in this frame.
        template <class T>
        T& GetOrCreate()
        {
            u64 key = KeyOf<T>();
            if (ContextItem* item = Find(key))
                return *static_cast<T*>(item);
            if (ContextItem* kept = Kept(key))
            {
                Mark(key);
                return *static_cast<T*>(kept);
            }
            T* made = new T();
            Add(key, made);
            return *made;
        }
        // The item made for this frame (an error, and the item, when it was made already).
        template <class T>
        T& Create()
        {
            u64 key = KeyOf<T>();
            if (Find(key))
                ReportCreatedTwice(TypeName<T>());
            return GetOrCreate<T>();
        }
        template <class T>
        bool Contains() const
        {
            return Find(KeyOf<T>()) != nullptr;
        }
        // The frame ends: the items made in it are reset.
        void Dispose();

        // By key (what C# calls): the item made in this frame, one kept from an earlier frame, an item taken (the
        // container owns it then), one marked as made again.
        ContextItem* Find(u64 key) const;
        ContextItem* Kept(u64 key) const;
        void Add(u64 key, ContextItem* item);
        void Mark(u64 key);
        ContextItem* Get(u64 key, StringView typeName);

        u64 Id() const { return id_; }
        static ContextContainer* FindContainer(u64 id);

    private:
        template <class T>
        static StringView TypeName()
        {
            scripting::TypeId t = scripting::TypeOf<T>();
            return t.IsValid() ? scripting::GetType(t)->fullName : StringView("The item");
        }
        static void ReportCreatedTwice(StringView typeName);

        struct Entry
        {
            u64 key = 0;
            std::unique_ptr<ContextItem> item;
            bool made = false;
        };
        Array<Entry> entries_ = Array<Entry>();
        u64 id_ = 0;
    };

    // What C#'s ContextContainer calls (containers and items by id).
    HX_CLASS(Namespace = "HelixEngine.Rendering", Static, Internal)
    class ContextNative
    {
    public:
        HX_FUNCTION() static u64 Create();
        HX_FUNCTION() static void Destroy(u64 container);
        HX_FUNCTION() static ContextItem* Find(u64 container, u64 key);
        HX_FUNCTION() static ContextItem* Kept(u64 container, u64 key);
        HX_FUNCTION() static void Add(u64 container, u64 key, ContextItem* item);
        HX_FUNCTION() static void Mark(u64 container, u64 key);
        HX_FUNCTION() static void Dispose(u64 container);
        HX_FUNCTION() static u64 KeyOfName(StringView fullName) { return ContextContainer::KeyOfName(fullName); }
    };
}

template <>
struct helix::scripting::ObjectHandles<helix::rendering::ContextItem>
{
    static u64 ToId(const rendering::ContextItem* item) { return item->Id(); }
    static rendering::ContextItem* FromId(u64 id) { return rendering::ContextItem::Find(id); }
    static TypeId TypeOf(const rendering::ContextItem* item) { return item->HelixType(); }
    static u64& WrapperOf(rendering::ContextItem* item) { return item->Wrapper(); }
};
