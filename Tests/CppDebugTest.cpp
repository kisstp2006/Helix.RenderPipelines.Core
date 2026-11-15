#include <Support/CoreHost.h>

#include <Helix/Core/Strings/Format.h>
#include <Helix/Engine/GameObject/GameObject.h>
#include <Helix/Engine/GameObject/Lifecycle.h>
#include <Helix/Engine/Script/ScriptRuntime.h>
#include <Helix/RenderPipelines/Core/Debugging/DebugManager.h>
#include <Helix/Testing/Test.h>

using namespace helix;
using namespace helix::rendering;
using namespace helix::rpcore::testing;

namespace
{
    const char* Bool(bool b) { return b ? "True" : "False"; }

    // Debug display data: values reset by the manager.
    struct CppDebugData final : IDebugData
    {
        bool wireframe = false;
        i32 samples = 4;
        f32 exposure = 1.5f;
        i32 mode = 0;
        Color tint = Color(1, 1, 1, 1);
        bool detail = true;

        Function<void()> GetReset() override
        {
            return [this] {
                wireframe = false;
                samples = 4;
                exposure = 1.5f;
                mode = 0;
                tint = Color(1, 1, 1, 1);
            };
        }
    };

    bool SameLines(const Array<String>& notes, std::initializer_list<const char*> expected)
    {
        bool same = notes.Size() == expected.size();
        usize i = 0;
        for (const char* e : expected)
        {
            same = same && i < notes.Size() && notes[i] == StringView(e);
            i++;
        }
        if (!same)
            for (const String& n : notes)
                std::printf("    %.*s\n", int(n.Size()), n.Data());
        return same;
    }
}

HX_TEST(Cpp, DebugPanelsOfWidgetsBoundToData)
{
    // The C# debug test's scenario with C++ widgets: the same notes.
    CppDebugData data;
    DebugManager& manager = DebugManager::instance();
    bool shown = false;
    u32 shownId = manager.onDisplayRuntimeUIChanged().Add([](void* user, bool v) { *static_cast<bool*>(user) = v; }, &shown);
    i32 changes = 0, pressed = 0;
    DebugUI::Panel* panel = manager.GetPanel("Rendering", true);
    HX_REQUIRE(panel != nullptr);
    auto* draw = new DebugUI::Value();
    draw->SetDisplayName("Draw Calls");
    draw->getter = [] { return String("42"); };
    auto* wireframe = new DebugUI::BoolField();
    wireframe->SetDisplayName("Wireframe");
    wireframe->getter = [&] { return data.wireframe; };
    wireframe->setter = [&](bool v) { data.wireframe = v; };
    wireframe->flags = DebugUI::Flags::RuntimeOnly;
    auto* samples = new DebugUI::IntField();
    samples->SetDisplayName("Samples");
    samples->getter = [&] { return data.samples; };
    samples->setter = [&](i32 v) { data.samples = v; };
    samples->min = [] { return 1; };
    samples->max = [] { return 8; };
    samples->onValueChanged = [&](DebugUI::Field<i32>&, i32) { changes++; };
    auto* lighting = new DebugUI::Foldout("Lighting");
    auto* exposureField = new DebugUI::FloatField();
    exposureField->SetDisplayName("Exposure");
    exposureField->getter = [&] { return data.exposure; };
    exposureField->setter = [&](f32 v) { data.exposure = v; };
    exposureField->min = [] { return 0.0f; };
    exposureField->decimals = 2;
    auto* tint = new DebugUI::ColorField();
    tint->SetDisplayName("Tint");
    tint->getter = [&] { return data.tint; };
    tint->setter = [&](Color v) { data.tint = v; };
    lighting->Add(exposureField);
    lighting->Add(tint);
    lighting->opened = true;
    auto* modeField = new DebugUI::EnumField();
    modeField->SetDisplayName("Mode");
    modeField->getter = [&] { return data.mode; };
    modeField->setter = [&](i32 v) { data.mode = v; };
    const StringView modeNames[] = {"None", "Albedo", "Normals"};
    const i32 modeValues[] = {0, 3, 7};
    modeField->SetEnum(Span<const StringView>(modeNames, 3), Span<const i32>(modeValues, 3));
    auto* closed = new DebugUI::Foldout("Closed");
    auto* detail = new DebugUI::BoolField();
    detail->SetDisplayName("Detail");
    detail->getter = [&] { return data.detail; };
    detail->setter = [&](bool v) { data.detail = v; };
    closed->Add(detail);
    auto* capture = new DebugUI::Button();
    capture->SetDisplayName("Capture");
    capture->action = [&] { pressed++; };
    for (DebugUI::Widget* w : {static_cast<DebugUI::Widget*>(draw), static_cast<DebugUI::Widget*>(wireframe), static_cast<DebugUI::Widget*>(samples),
                               static_cast<DebugUI::Widget*>(lighting), static_cast<DebugUI::Widget*>(modeField), static_cast<DebugUI::Widget*>(closed),
                               static_cast<DebugUI::Widget*>(capture)})
        panel->Add(w);
    manager.RegisterData(&data);

    Array<String> notes;
    samples->SetValue(20);
    samples->SetValue(20);
    auto* exposure = static_cast<DebugUI::FloatField*>(manager.GetItem("Rendering -> Lighting -> Exposure"));
    HX_REQUIRE(exposure == exposureField);
    exposure->SetValue(-3.0f);
    static_cast<DebugUI::ColorField*>(manager.GetItem("Rendering -> Lighting -> Tint"))->SetValue(Color(2.0f, 0.5f, -1.0f, 1.0f));
    auto* mode = static_cast<DebugUI::EnumField*>(manager.GetItem("Rendering -> Mode"));
    mode->SetValue(7);
    i32 modeIndex = mode->currentIndex();
    mode->SetValue(5);
    manager.GetItem("Rendering -> Capture")->Press();
    notes.Push(Format("values:{}:{}:{:.3f}:{:.3f}:{:.3f}:{}:{}:{}", data.samples, changes, data.exposure, data.tint.r, data.tint.b, modeIndex, data.mode, pressed));
    Array<DebugUI::Widget*> runtimeOnly;
    manager.GetItems(DebugUI::Flags::RuntimeOnly, runtimeOnly);
    notes.Push(Format("query:{}:{}:{}:{}:{}", exposure->queryPath(), Bool(panel->Contains("Rendering -> Closed -> Detail")), runtimeOnly.Size(),
                      Bool(manager.GetItem("Rendering -> Nothing") == nullptr), manager.PanelIndex("rendering")));
    mode->SetValue(3);
    String text = DebugUITextView::Render();
    for (StringView rest = text.View(); !rest.Empty();)
    {
        usize end = rest.Find('\n');
        StringView line = rest.Substr(0, end == StringView::npos ? rest.Size() : end);
        if (!line.Empty())
            notes.Push(Format("text:{}", line));
        rest = end == StringView::npos ? StringView() : rest.Substr(end + 1);
    }
    manager.SetDisplayRuntimeUI(true);
    manager.Reset();
    notes.Push(Format("reset:{}:{:.3f}:{}:{}:{}", data.samples, data.exposure, data.mode, Bool(shown), Bool(manager.isAnyDebugUIActive())));
    manager.SetDisplayRuntimeUI(false);
    manager.UnregisterData(&data);
    manager.RemovePanel("Rendering");
    notes.Push(Format("removed:{}:{}", Bool(manager.GetPanel("Rendering") == nullptr), manager.panels().Size()));
    manager.onDisplayRuntimeUIChanged().Remove(shownId);

    HX_CHECK(SameLines(notes, {"values:8:1:0.000:1.000:0.000:2:0:1",
                               "query:Rendering -> Lighting -> Exposure:True:1:True:0",
                               "text:[Rendering]",
                               "text:  Draw Calls: 42",
                               "text:  Wireframe: False",
                               "text:  Samples: 8",
                               "text:  Lighting: open",
                               "text:    Exposure: 0.00",
                               "text:    Tint: (1.000, 0.500, 0.000, 1.000)",
                               "text:  Mode: Albedo",
                               "text:  Closed: closed",
                               "text:  Capture",
                               "reset:4:1.500:0:True:True",
                               "removed:True:0"}));
}

namespace
{
    i32 gCount = 2;
    bool gOn = false;
    i32 gGo = 0;
}

HX_TEST(Cpp, CSharpSeesCppDebugWidgets)
{
    // A C++ panel C# reads and changes through wrappers (the C++ validation), and a C# widget C# puts into it.
    HX_REQUIRE(StartScripts());
    DebugManager& manager = DebugManager::instance();
    DebugUI::Panel* panel = manager.GetPanel("Native", true);
    auto* count = new DebugUI::IntField();
    count->SetDisplayName("Count");
    count->getter = [] { return gCount; };
    count->setter = [](i32 v) { gCount = v; };
    count->max = [] { return 5; };
    auto* speed = new DebugUI::FloatField();
    speed->SetDisplayName("Speed");
    speed->getter = [] { return 1.25f; };
    auto* group = new DebugUI::Foldout("Group");
    auto* on = new DebugUI::BoolField();
    on->SetDisplayName("On");
    on->getter = [] { return gOn; };
    on->setter = [](bool v) { gOn = v; };
    group->Add(on);
    auto* go = new DebugUI::Button();
    go->SetDisplayName("Go");
    go->action = [] { gGo++; };
    panel->Add(count);
    panel->Add(speed);
    panel->Add(group);
    panel->Add(go);
    Notes().Clear();
    engine::GameObject* host = engine::GameObject::Create("DebugReader");
    host->AddComponent(CSharp("CsDebugReader").type);
    RunScriptPhase(engine::ScriptMessage::Update);
    HX_CHECK(SameNotes({"wrap:IntField=Count,FloatField=Speed,Foldout=Group,Button=Go:5:True:Native -> Count:True:1.25"}));
    HX_CHECK(gCount == 5 && gOn && gGo == 1 && panel->children().Size() == 5);
    String text = DebugUITextView::Render(*panel);
    HX_CHECK(text == StringView("[Native]\n  Count: 5\n  Speed: 1.250\n  Group: closed\n  Go\n  From C#: 7\n"));
    if (text != StringView("[Native]\n  Count: 5\n  Speed: 1.250\n  Group: closed\n  Go\n  From C#: 7\n"))
        std::printf("%.*s", int(text.Size()), text.Data());
    engine::DestroyImmediate(host);
    manager.RemovePanel(panel);
}
