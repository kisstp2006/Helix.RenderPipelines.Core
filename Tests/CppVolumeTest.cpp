#include <Support/CoreHost.h>

#include <Cpp/CppVolumes.h>

#include <Helix/Core/Strings/Format.h>
#include <Helix/Engine/GameObject/GameObject.h>
#include <Helix/Engine/GameObject/Lifecycle.h>
#include <Helix/Engine/Asset/Asset.h>
#include <Helix/Engine/Scene/SceneFile.h>
#include <Helix/Engine/Script/MonoScript.h>
#include <Helix/Engine/Scene/SceneManager.h>
#include <Helix/Engine/Script/ScriptRuntime.h>
#include <Helix/Engine/Transform/Transform.h>
#include <Helix/Testing/Test.h>

using namespace helix;
using namespace helix::rendering;
using namespace helix::rpcore::testing;

namespace
{
    const char* Bool(bool b) { return b ? "True" : "False"; }
}

HX_TEST(Cpp, VolumesBlendByPriorityWeightDistanceAndLayer)
{
    // The C# volume test's scenario with C++ components: the same values.
    HX_REQUIRE(StartScripts());
    // The core's types through TypeOf in the test program (bound by the core's binary).
    scripting::TypeId testType = scripting::TypeOf<CppTestVolume>(), otherType = scripting::TypeOf<CppOtherVolume>();
    scripting::TypeId profileType = scripting::TypeOf<VolumeProfile>(), volumeType = scripting::TypeOf<Volume>();
    HX_REQUIRE(testType.IsValid() && otherType.IsValid() && profileType.IsValid() && volumeType.IsValid());
    auto newProfile = [&] { return static_cast<VolumeProfile*>(engine::ScriptableObject::CreateInstance(profileType)); };
    VolumeManager& manager = VolumeManager::instance();

    VolumeProfile* defaults = newProfile();
    static_cast<CppOtherVolume*>(defaults->Add(otherType))->count.Override(7);
    HX_REQUIRE(manager.Initialize(defaults));
    bool hasTest = false, hasOther = false;
    for (scripting::TypeId t : manager.baseComponentTypeArray())
        hasTest = hasTest || t == testType, hasOther = hasOther || t == otherType;
    HX_CHECK(manager.isInitialized() && hasTest && hasOther);
    VolumeStack* stack = manager.CreateStack();

    Array<engine::GameObject*> objects;
    auto make = [&](StringView name, VolumeProfile* profile, bool global, f32 priority, f32 weight) {
        engine::GameObject* g = engine::GameObject::Create(name);
        objects.Push(g);
        auto* v = static_cast<Volume*>(g->AddComponent(volumeType));
        v->SetSharedProfile(profile);
        v->SetIsGlobal(global);
        v->SetPriority(priority);
        v->weight = weight;
        return v;
    };
    auto test = [&](VolumeProfile* p) { return static_cast<CppTestVolume*>(p->Add(testType)); };
    VolumeProfile* pa = newProfile();
    CppTestVolume* ca = test(pa);
    ca->intensity.Override(4);
    ca->tint.Override(Color(1, 0, 0, 1));
    ca->flag.Override(true);
    Volume* a = make("VolumeA", pa, true, 0, 1);
    VolumeProfile* pb = newProfile();
    CppTestVolume* cb = test(pb);
    cb->intensity.Override(10);
    cb->steps.Override(8);
    Volume* b = make("VolumeB", pb, true, 1, 0.5f);
    VolumeProfile* pl = newProfile();
    CppTestVolume* cl = test(pl);
    cl->intensity.SetValue(25.0f);
    cl->intensity.SetOverrideState(true);
    Volume* local = make("VolumeLocal", pl, false, 2, 1);
    local->blendDistance = 4;
    local->transform()->SetPosition(Vector3(10, 0, 0));
    engine::GameObject* trigger = engine::GameObject::Create("Trigger");
    objects.Push(trigger);
    trigger->transform()->SetPosition(Vector3(8, 0, 0));

    auto state = [&](engine::Transform* at, i32 mask) {
        manager.Update(stack, at, engine::LayerMask(mask));
        auto* s = static_cast<CppTestVolume*>(stack->GetComponent(testType));
        auto* o = static_cast<CppOtherVolume*>(stack->GetComponent(otherType));
        return Format("{:.3f}:{:.3f}:{}:{:.3f}:{:.3f}:{}:{}", s->intensity.value(), s->tint.value().g, Bool(s->flag.value()), s->steps.value(),
                      s->untouched.value(), o->count.value(), Bool(s->intensity.overrideState()));
    };
    Array<String> notes;
    notes.Push(Format("params:{}:{}:{}:{}", ca->parameterCount(), Bool(ca->parameter(0) == &ca->intensity), Bool(ca->parameter(4) == &ca->untouched),
                      cl->intensity.value()));
    notes.Push(Format("global:{}", state(nullptr, ~0)));
    notes.Push(Format("near:{}", state(trigger->transform(), ~0)));
    trigger->transform()->SetPosition(Vector3(100, 0, 0));
    notes.Push(Format("far:{}", state(trigger->transform(), ~0)));
    b->gameObject()->SetLayer(4);
    Array<Volume*> all, layer4;
    String layers = state(nullptr, ~(1 << 4));
    manager.GetVolumes(engine::LayerMask(~0), all);
    manager.GetVolumes(engine::LayerMask(1 << 4), layer4);
    notes.Push(Format("layers:{}:{}:{}", layers, all.Size(), layer4.Size()));
    b->gameObject()->SetLayer(0);
    a->weight = 0;
    notes.Push(Format("weight:{}", state(nullptr, ~0)));
    a->weight = 1;
    ca->active = false;
    notes.Push(Format("inactive:{}", state(nullptr, ~0)));
    ca->active = true;
    a->SetPriority(5);
    notes.Push(Format("priority:{}", state(nullptr, ~0)));
    notes.Push(Format("active:{}:{}", Bool(manager.IsComponentActiveInMask(testType, engine::LayerMask(~0))),
                      Bool(manager.IsComponentActiveInMask(otherType, engine::LayerMask(~0)))));
    // The instance profile: a copy only this volume blends.
    bool before = a->HasInstantiatedProfile();
    auto* copy = static_cast<CppTestVolume*>(a->profile()->Find(testType, false));
    HX_REQUIRE(copy != nullptr && copy != ca);
    copy->intensity.Override(0);
    String instance = state(nullptr, ~0);
    notes.Push(Format("instance:{}:{}:{:.3f}:{}", Bool(before), Bool(a->HasInstantiatedProfile()), ca->intensity.value(), instance));
    // A second component of a type is refused (with an error); adding and removing another.
    bool refused = pb->Add(testType) == nullptr;
    VolumeComponent* other = pb->Add(otherType, true);
    bool had = pb->Has(otherType) && other && static_cast<CppOtherVolume*>(other)->count.overrideState();
    pb->Remove(otherType);
    notes.Push(Format("profile:{}:{}:{}:{}", Bool(refused), Bool(had), Bool(pb->Has(otherType)), pb->componentCount()));

    const char* expected[] = {"params:6:True:True:20",
                              "global:7.000:0.000:True:8.000:5.000:7:True",
                              "near:16.750:0.000:True:8.000:5.000:7:True",
                              "far:7.000:0.000:True:8.000:5.000:7:True",
                              "layers:4.000:0.000:True:2.000:5.000:7:True:3:1",
                              "weight:5.500:1.000:False:8.000:5.000:7:True",
                              "inactive:5.500:1.000:False:8.000:5.000:7:True",
                              "priority:4.000:0.000:True:8.000:5.000:7:True",
                              "active:True:False",
                              "instance:False:True:4.000:0.000:0.000:True:8.000:5.000:7:True",
                              "profile:True:True:False:1"};
    bool same = notes.Size() == sizeof(expected) / sizeof(expected[0]);
    for (usize i = 0; same && i < notes.Size(); i++)
        same = notes[i] == StringView(expected[i]);
    if (!same)
        for (const String& n : notes)
            std::printf("    %.*s\n", int(n.Size()), n.Data());
    HX_CHECK(same);

    for (engine::GameObject* g : objects)
        engine::DestroyImmediate(g);
    manager.DestroyStack(stack);
    manager.Deinitialize();
    engine::testing::Cleanup();
}

HX_TEST(Cpp, VolumeParameterMembersInCSharpAndInFiles)
{
    // C# sees a C++ component's parameter members as objects of their C# classes; files hold them as the reference.
    HX_REQUIRE(StartScripts());
    Notes().Clear();
    engine::GameObject* host = engine::GameObject::Create("ParameterHost");
    host->AddComponent(CSharp("CsCppParameters").type);
    engine::RunScriptPhase(engine::ScriptMessage::Update);
    HX_CHECK(SameNotes({"cpp:True:0:20:20:True:0.5:NoInterpFloatParameter:6", "enum:CppTestModeParameter:Second:Third:True"}));
    engine::DestroyImmediate(host);

    // A scene of the C++ script (its asset by guid): read, changed, written again.
    Guid guid;
    Guid::TryParse("c0000000000000000000000000000001", guid);
    engine::MonoScript* script = engine::MonoScript::FromScriptClass(*engine::FindScriptClass(scripting::TypeOf<CppParameterHolder>()));
    engine::RegisterAssetObject(guid, 11500000, script, true);
    const char* file = R"(%YAML 1.1
%TAG !h! tag:helixengine,2026:
--- !h!1 &100
GameObject:
  m_Component:
  - component: {fileID: 101}
  - component: {fileID: 102}
  m_Name: Holder
  m_IsActive: 1
--- !h!4 &101
Transform:
  m_GameObject: {fileID: 100}
  m_Children: []
  m_Father: {fileID: 0}
--- !h!114 &102
GameBehaviour:
  m_GameObject: {fileID: 100}
  m_Enabled: 1
  m_Script: {fileID: 11500000, guid: c0000000000000000000000000000001, type: 3}
  strength:
    m_OverrideState: 1
    m_Value: 12
  tint:
    m_OverrideState: 0
    m_Value: {r: 0.25, g: 0.5, b: 0.75, a: 1}
  mask:
    m_OverrideState: 1
    m_Value:
      serializedVersion: 2
      m_Bits: 6
  texture:
    m_OverrideState: 0
    m_Value: {fileID: 0}
  mode:
    m_OverrideState: 1
    m_Value: 2
)";
    engine::Scene scene = engine::SceneManager::CreateScene("Parameters");
    Array<String> errors;
    HX_REQUIRE(engine::ReadSceneFile(StringView(file), scene, errors));
    for (const String& e : errors)
        std::printf("    %.*s\n", int(e.Size()), e.Data());
    Array<engine::GameObject*> roots = scene.GetRootGameObjects();
    HX_REQUIRE(errors.Empty() && roots.Size() == 1);
    auto* holder = static_cast<CppParameterHolder*>(roots[0]->GetComponent(scripting::TypeOf<CppParameterHolder>()));
    HX_REQUIRE(holder != nullptr);
    // The read value is kept within the parameter's limits (0 to 10).
    HX_CHECK(holder->strength.value() == 10.0f && holder->strength.overrideState() && !holder->tint.overrideState() &&
             holder->tint.value().b == 0.75f && holder->mask.value().value() == 6 && holder->mask.overrideState() && !holder->texture.overrideState() &&
             holder->mode.value() == CppTestMode::Third && holder->mode.overrideState());
    holder->mode.Override(CppTestMode::Second);
    holder->strength.Override(3.5f);
    holder->tint.SetOverrideState(true);
    String text, error;
    HX_REQUIRE(engine::WriteSceneYaml(scene, text, error));
    bool written = text.View().Contains("  strength:\n    m_OverrideState: 1\n    m_Value: 3.5\n") &&
                   text.View().Contains("  tint:\n    m_OverrideState: 1\n    m_Value: {r: 0.25, g: 0.5, b: 0.75, a: 1}\n") &&
                   text.View().Contains("  mask:\n    m_OverrideState: 1\n    m_Value:\n      serializedVersion: 2\n      m_Bits: 6\n") &&
                   text.View().Contains("  texture:\n    m_OverrideState: 0\n    m_Value: {fileID: 0}\n") &&
                   text.View().Contains("  mode:\n    m_OverrideState: 1\n    m_Value: 1\n");
    if (!written)
        std::printf("%.*s\n", int(text.Size()), text.Data());
    HX_CHECK(written);
    engine::SceneManager::UnloadSceneAsync(scene);
    engine::SceneManager::ProcessSceneLoads();
    engine::ResetAssets();
    engine::DestroyImmediate(script, true);
    engine::testing::Cleanup();
}
