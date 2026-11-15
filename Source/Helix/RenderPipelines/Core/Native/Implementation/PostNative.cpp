#include <Helix/RenderPipelines/Core/Native/PostNative.h>

#include <Helix/RenderPipelines/Core/Native/GraphNative.h>

#include <bit>

namespace helix::rendering
{
    namespace
    {
        Array<engine::EntityId>& Listed()
        {
            static Array<engine::EntityId> listed;
            return listed;
        }
    }

    void PostNative::SetManagedRender(u64 render) { PostProcessVolumeComponent::SetManagedRender(std::bit_cast<void (*)(u64, u64, u64)>(uptr(render))); }

    void PostNative::Render(PostProcessVolumeComponent* component, u64 commandBuffer, u64 source)
    {
        CommandBuffer* cmd = CommandBuffer::Find(commandBuffer);
        if (component && cmd)
            component->Render(*cmd, RTHandle::Find(source));
    }

    void PostNative::SetOrder(u64 passes, Span<const u32> order)
    {
        PostProcessPasses* p = PostProcessPasses::Find(passes);
        if (!p)
            return;
        p->order().Clear();
        for (u32 t : order)
            p->order().Push(scripting::TypeId{t});
    }

    i32 PostNative::ActiveComponents(u64 passes, u64 stack, i32 point)
    {
        Listed().Clear();
        PostProcessPasses* p = PostProcessPasses::Find(passes);
        if (!p)
            return 0;
        Array<PostProcessVolumeComponent*> active;
        p->ActiveComponents(VolumeStack::Find(stack), PostProcessInjectionPoint(point), active);
        for (PostProcessVolumeComponent* c : active)
            Listed().Push(c->GetEntityId());
        return i32(Listed().Size());
    }

    PostProcessVolumeComponent* PostNative::ActiveAt(i32 index)
    {
        if (index < 0 || usize(index) >= Listed().Size())
            return nullptr;
        return static_cast<PostProcessVolumeComponent*>(engine::EntityIdToObject(Listed()[usize(index)]));
    }

    u64 PostNative::Record(u64 passes, u64 graph, u64 stack, i32 point, u64 color)
    {
        PostProcessPasses* p = PostProcessPasses::Find(passes);
        RenderGraph* g = RenderGraph::Find(graph);
        if (!p || !g)
            return color;
        return GraphNative::Pack(p->Record(*g, VolumeStack::Find(stack), PostProcessInjectionPoint(point), GraphNative::Unpack(color)));
    }
}
