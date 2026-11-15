#pragma once

#include <Helix/RenderPipelines/Core/Debugging/DebugManager.h>

// What C#'s debug display calls (internal): widgets by id (C++ widgets, and the native widgets standing for C# ones
// while they are in a panel), the C# widgets' and the manager's callbacks, the manager, the text view.
namespace helix::rendering
{
    HX_CLASS(Namespace = "HelixEngine.Rendering.Internal", Static, Internal)
    class DebugNative
    {
    public:
        // C# widgets' functions of their GC handles: text (answering through ReturnText), hidden, open, free.
        HX_FUNCTION() static void SetWidgetCallbacks(u64 text, u64 hidden, u64 open, u64 free);
        HX_FUNCTION() static void ReturnText(StringView text) { DebugUI::SetManagedText(text); }
        // The manager's events to C#: dirty(bool), reset(), display(bool), panelDirty(panel id); and C# data blocks'
        // reset(GC handle).
        HX_FUNCTION() static void SetManagerCallbacks(u64 dirty, u64 reset, u64 display, u64 panelDirty, u64 dataReset);

        // Widgets.
        HX_FUNCTION() static u64 CreateManaged(u64 handle, bool container) { return DebugUI::NewManaged(handle, container)->Id(); }
        // The widget goes (out of its container first); a container's widgets with it.
        HX_FUNCTION() static void Destroy(u64 widget);
        HX_FUNCTION() static bool IsValid(u64 widget) { return DebugUI::Widget::Find(widget) != nullptr; }
        HX_FUNCTION() static String Kind(u64 widget);
        HX_FUNCTION() static u64 ManagedHandle(u64 widget);
        HX_FUNCTION() static String DisplayName(u64 widget);
        HX_FUNCTION() static void SetDisplayName(u64 widget, StringView name);
        HX_FUNCTION() static String Tooltip(u64 widget);
        HX_FUNCTION() static void SetTooltip(u64 widget, StringView tooltip);
        HX_FUNCTION() static i32 Flags(u64 widget);
        HX_FUNCTION() static void SetFlags(u64 widget, i32 flags);
        HX_FUNCTION() static String QueryPath(u64 widget);
        HX_FUNCTION() static bool IsHidden(u64 widget);
        HX_FUNCTION() static bool HasValueText(u64 widget);
        HX_FUNCTION() static String ValueText(u64 widget);
        HX_FUNCTION() static u64 Parent(u64 widget);
        HX_FUNCTION() static u64 PanelOf(u64 widget);
        HX_FUNCTION() static DebugValue ReadValue(u64 widget);
        HX_FUNCTION() static void WriteValue(u64 widget, DebugValue value);
        HX_FUNCTION() static void Press(u64 widget);
        // An enum field's names and values.
        HX_FUNCTION() static Array<String> EnumNames(u64 widget);
        HX_FUNCTION() static Array<i32> EnumValues(u64 widget);
        HX_FUNCTION() static i32 Hash(u64 widget);

        // Containers (panels too).
        HX_FUNCTION() static i32 ChildCount(u64 container);
        HX_FUNCTION() static u64 ChildAt(u64 container, i32 index);
        HX_FUNCTION() static u32 Version(u64 container);
        HX_FUNCTION() static void Insert(u64 container, i32 index, u64 widget);
        HX_FUNCTION() static bool Remove(u64 container, u64 widget);

        // Panels.
        HX_FUNCTION() static u64 NewPanel() { return (new DebugUI::Panel())->Id(); }
        HX_FUNCTION() static i32 GroupIndex(u64 panel);
        HX_FUNCTION() static void SetGroupIndex(u64 panel, i32 group);
        HX_FUNCTION() static void SetDirty(u64 panel);
        // The panel's dirty event reaches C# (panelDirty) from now on.
        HX_FUNCTION() static void WatchPanel(u64 panel);
        HX_FUNCTION() static bool Contains(u64 panel, StringView queryPath);

        // The manager.
        HX_FUNCTION() static i32 PanelCount() { return i32(DebugManager::instance().panels().Size()); }
        HX_FUNCTION() static u64 PanelAt(i32 index);
        HX_FUNCTION() static u32 PanelsVersion() { return DebugManager::instance().version(); }
        HX_FUNCTION() static u64 GetPanel(StringView name, bool createIfNull, i32 groupIndex, bool overrideIfExist);
        HX_FUNCTION() static i32 PanelIndex(StringView name) { return DebugManager::instance().PanelIndex(name); }
        HX_FUNCTION() static void RemovePanel(u64 panel);
        // The widgets of the flags, kept for ItemAt.
        HX_FUNCTION() static i32 GetItems(i32 flags);
        HX_FUNCTION() static u64 ItemAt(i32 index);
        HX_FUNCTION() static u64 GetItem(StringView queryPath);
        HX_FUNCTION() static bool DisplayRuntimeUI() { return DebugManager::instance().displayRuntimeUI(); }
        HX_FUNCTION() static void SetDisplayRuntimeUI(bool value) { DebugManager::instance().SetDisplayRuntimeUI(value); }
        HX_FUNCTION() static i32 GetState() { return DebugManager::instance().GetState(); }
        HX_FUNCTION() static void Reset() { DebugManager::instance().Reset(); }
        HX_FUNCTION() static void RefreshEditor() { DebugManager::instance().RefreshEditor(); }
        // A C# data block (its GC handle, freed when it is unregistered): an id.
        HX_FUNCTION() static u64 RegisterManagedData(u64 handle);
        HX_FUNCTION() static void UnregisterManagedData(u64 data);

        HX_FUNCTION() static String TextView() { return DebugUITextView::Render(); }
        HX_FUNCTION() static String TextViewOf(u64 panel);
    };
}
