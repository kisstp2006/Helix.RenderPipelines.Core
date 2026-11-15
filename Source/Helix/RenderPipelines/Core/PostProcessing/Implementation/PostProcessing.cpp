#include <Helix/RenderPipelines/Core/PostProcessing/PostProcessing.h>

#include <Helix/Core/Containers/HashMap.h>
#include <Helix/Engine/Script/ScriptClass.h>
#include <Helix/Scripting/Bindings/Interop.h>

#include <algorithm>

namespace helix::rendering
{
    namespace
    {
        void (*gManagedRender)(u64 wrapper, u64 commandBuffer, u64 source) = nullptr;

        u32 PostProcessClass()
        {
            const engine::ScriptClass* s = engine::FindScriptClass(scripting::TypeOf<PostProcessVolumeComponent>());
            return s && s->alive ? s->engineClass : engine::kNoClass;
        }

        scripting::TypeId TypeOfObject(const engine::Object* o) { return o ? o->Class().type : scripting::TypeId{}; }

        HashMap<u64, PostProcessPasses*>& AllPasses()
        {
            static HashMap<u64, PostProcessPasses*> passes;
            return passes;
        }
        u64 gNextPasses = 0;

        struct PassData
        {
            PostProcessVolumeComponent* component = nullptr;
            TextureHandle source;
            RenderGraph* graph = nullptr;
        };
    }

    // ------------------------------------------------------------------------------------------------ the component

    void PostProcessVolumeComponent::SetManagedRender(void (*render)(u64, u64, u64)) { gManagedRender = render; }

    void PostProcessVolumeComponent::Render(CommandBuffer& cmd, RTHandle* source)
    {
        // A C++ class without its own Render draws nothing; a C# class's object renders in C#.
        const engine::ScriptClass* s = script();
        if (!s || s->language != engine::ScriptLanguage::CSharp || !gManagedRender)
            return;
        u64 wrapper = scripting::ObjectHandles<engine::Object>::WrapperOf(this);
        if (wrapper)
            gManagedRender(wrapper, cmd.Id(), source ? source->Id() : 0);
    }

    void PostProcessVolumeComponent::EnsureSetup()
    {
        if (setUp_)
            return;
        Setup();
        setUp_ = true;
    }

    void PostProcessVolumeComponent::CleanupIfSetUp()
    {
        if (!setUp_)
            return;
        Cleanup();
        setUp_ = false;
    }

    // ------------------------------------------------------------------------------------------------ the passes

    PostProcessPasses::PostProcessPasses() : id_(++gNextPasses) { AllPasses().Set(id_, this); }

    PostProcessPasses::~PostProcessPasses()
    {
        Dispose();
        AllPasses().Remove(id_);
    }

    PostProcessPasses* PostProcessPasses::Find(u64 id)
    {
        PostProcessPasses** p = AllPasses().Find(id);
        return p ? *p : nullptr;
    }

    void PostProcessPasses::ActiveComponents(const VolumeStack* stack, PostProcessInjectionPoint point, Array<PostProcessVolumeComponent*>& out) const
    {
        out.Clear();
        u32 post = PostProcessClass();
        if (!stack || post == engine::kNoClass)
            return;
        for (u32 i = 0; i < stack->componentCount(); i++)
        {
            VolumeComponent* c = stack->component(i);
            if (!c || !engine::IsDerivedFrom(c->ClassIndex(), post))
                continue;
            auto* p = static_cast<PostProcessVolumeComponent*>(c);
            if (p->injectionPoint() == point && p->active && p->IsActive())
                out.Push(p);
        }
        auto rank = [this](const PostProcessVolumeComponent* c) {
            scripting::TypeId type = TypeOfObject(c);
            for (usize i = 0; i < order_.Size(); i++)
                if (order_[i] == type)
                    return i;
            return usize(~usize(0));
        };
        std::stable_sort(out.begin(), out.end(), [&](const PostProcessVolumeComponent* a, const PostProcessVolumeComponent* b) {
            usize ra = rank(a), rb = rank(b);
            if (ra != rb)
                return ra < rb;
            return ScriptTypeFullName(TypeOfObject(a)).View() < ScriptTypeFullName(TypeOfObject(b)).View();
        });
    }

    TextureHandle PostProcessPasses::Record(RenderGraph& graph, const VolumeStack* stack, PostProcessInjectionPoint point, TextureHandle color)
    {
        Array<PostProcessVolumeComponent*> components;
        ActiveComponents(stack, point, components);
        if (components.Empty())
            return color;
        TextureDesc desc = graph.GetTextureDesc(color);
        desc.clearBuffer = false;
        desc.depthBufferBits = DepthBits::None;
        desc.name = String("PostProcess");
        TextureHandle targets[2] = {graph.CreateTexture(desc), graph.CreateTexture(desc)};
        TextureHandle source = color;
        for (usize i = 0; i < components.Size(); i++)
        {
            PostProcessVolumeComponent* component = components[i];
            component->EnsureSetup();
            engine::EntityId id = component->GetEntityId();
            bool known = false;
            for (engine::EntityId e : setUp_)
                known = known || e == id;
            if (!known)
                setUp_.Push(id);
            TextureHandle target = targets[i & 1];
            const engine::ScriptClass* s = engine::FindScriptClass(TypeOfObject(component));
            PassData* data = nullptr;
            PassBuilder<PassData> builder = graph.AddRasterRenderPass(s ? StringView(s->name) : StringView("PostProcess"), data);
            data->component = component;
            data->source = source;
            data->graph = &graph;
            builder.UseTexture(source);
            builder.SetRenderAttachment(target, 0, AccessFlags::WriteAll);
            builder.SetRenderFunc([](PassData& d, RasterGraphContext& ctx) { d.component->Render(*ctx.cmd, d.graph->Resolve(d.source)); });
            builder.Dispose();
            source = target;
        }
        return source;
    }

    void PostProcessPasses::Dispose()
    {
        for (engine::EntityId e : setUp_)
            if (auto* c = static_cast<PostProcessVolumeComponent*>(engine::EntityIdToObject(e)))
                c->CleanupIfSetUp();
        setUp_.Clear();
    }
}
