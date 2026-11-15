#pragma once

#include <Helix/RenderPipelines/Core/Debugging/DebugUI.h>

// The panels of the debug display, the data blocks they reset, whether the runtime display shows, and the display as
// text.
namespace helix::rendering
{
    // What a debug data block resets.
    class IDebugData
    {
    public:
        virtual ~IDebugData() = default;
        virtual Function<void()> GetReset() = 0;
    };

    class DebugManager
    {
    public:
        static DebugManager& instance();

        Span<DebugUI::Panel* const> panels() const { return Span<DebugUI::Panel* const>(panels_.Data(), panels_.Size()); }
        // Changes when panels come and go (C#'s view of the list).
        u32 version() const { return version_; }

        engine::Event<bool>& onDisplayRuntimeUIChanged() { return onDisplayRuntimeUIChanged_; }
        engine::Event<>& resetData() { return resetData_; }
        engine::Event<bool>& onSetDirty() { return onSetDirty_; }

        // The runtime display shows (its view comes with the UI; the text view reads the same panels).
        bool displayRuntimeUI() const { return displayRuntimeUI_; }
        void SetDisplayRuntimeUI(bool value);
        bool displayEditorUI() const { return false; }
        bool isAnyDebugUIActive() const { return displayRuntimeUI_; }

        i32 GetState() const;

        // The panel of a name (case is ignored); made, in its group order, when createIfNull.
        DebugUI::Panel* GetPanel(StringView displayName, bool createIfNull = false, i32 groupIndex = 0, bool overrideIfExist = false);
        i32 PanelIndex(StringView displayName) const;
        // The panel goes (with its widgets).
        void RemovePanel(StringView displayName);
        void RemovePanel(DebugUI::Panel* panel);

        // Every widget (in containers too) that has all of the flags.
        void GetItems(DebugUI::Flags flags, Array<DebugUI::Widget*>& out) const;
        DebugUI::Widget* GetItem(StringView queryPath) const;

        void RegisterData(IDebugData* data);
        void UnregisterData(IDebugData* data);
        // Every registered data block back to its defaults.
        void Reset();
        void RefreshEditor() { onSetDirty_.Invoke(true); }

    private:
        DebugManager() = default;
        static void OnPanelDirty(void* self, DebugUI::Panel* panel);

        Array<DebugUI::Panel*> panels_ = Array<DebugUI::Panel*>();
        Array<IDebugData*> data_ = Array<IDebugData*>();
        engine::Event<bool> onDisplayRuntimeUIChanged_;
        engine::Event<> resetData_;
        engine::Event<bool> onSetDirty_;
        bool displayRuntimeUI_ = false;
        u32 version_ = 0;
    };

    // The debug display as text, a line per widget indented by depth (hidden and editor only widgets left out, a closed
    // foldout's widgets too): for logs and tests until the display's UI comes.
    struct DebugUITextView
    {
        static String Render();
        static String Render(const DebugUI::Panel& panel);
    };
}
