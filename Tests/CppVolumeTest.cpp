#include <Support/CoreHost.h>

#include <Cpp/CppVolumes.h>

#include <Helix/Core/Strings/Format.h>
#include <Helix/Engine/GameObject/GameObject.h>
#include <Helix/Engine/GameObject/Lifecycle.h>
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
    scripting::TypeId testType = scripting::FindType("CoreTests.CppTestVolume"), otherType = scripting::FindType("CoreTests.CppOtherVolume");
    scripting::TypeId profileType = scripting::FindType("HelixEngine.Rendering.VolumeProfile"), volumeType = scripting::FindType("HelixEngine.Rendering.Volume");
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

    const char* expected[] = {"params:5:True:True:20",
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
