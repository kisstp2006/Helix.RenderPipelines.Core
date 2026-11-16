# Helix.RenderPipelines.Core

The shared core of the Helix engine's scriptable render pipelines, written in C++ with C# bindings: the render graph
(passes recorded with the textures and renderer lists they use, then culled, given pooled textures that alias, merged
into native render passes and executed into one command buffer), render textures sized from a reference size
(`RTHandle`, `RTHandleSystem`, `RTHandles`), copies with the core's blit shaders (`Blitter`), the volume framework
(profiles of volume components blended by priority, weight and distance), a post processing framework over volumes
and the render graph, the debug display (its model and a text view), the pipelines' helpers (`CoreUtils`), and the
shader library the pipelines' C# shaders call. Pipelines in C++ use it directly; C# reaches it through generated
bindings with the familiar C# API (HelixEngine.Rendering). The core's shaders are C#, compiled from their IL by the
engine when they load.

The core is a native module (`HelixRenderPipelinesCore`, a shared library beside the engine's; a static one on the
Web). Its volume components, profiles and volumes are script classes of both languages: a C# volume component or post
process effect derives from the C++ class, its C# parameters keep their values in C#. A public field of the C# API
whose data lives in C++ is a property of the same name and type (`Volume.weight`, `blendDistance`, `sharedProfile`,
`VolumeComponent.active`, `VolumeProfile.components` and `isDirty`).

## Contents

| Path | Content |
|---|---|
| `Source/Helix/RenderPipelines/Core/RenderGraph/` | `RenderGraph` (`BeginRecording`, `ImportTexture`, `ImportBackbuffer`, `CreateTexture`, `CreateRendererList`, `AddRasterRenderPass`, `AddComputePass`, `AddUnsafePass`, `EndRecordingAndExecute`, `EndFrame`, `Cleanup`), the pass builders, `TextureHandle`, `RendererListHandle`, `TextureDesc`, `RenderGraphParameters`, the graph contexts |
| `Source/Helix/RenderPipelines/Core/Textures/` | `RTHandle`, `RTHandleSystem` (handles scaled from the reference size, grown when it grows, reset), `RTHandles` (the shared system), `RTHandleProperties` |
| `Source/Helix/RenderPipelines/Core/Utilities/` | `Blitter` (textures, handles, quads, color and depth into the bound target or a handle), `CoreUtils` (targets set and cleared with the viewport of a handle, engine materials, keywords, full screen draws, target names) |
| `Source/Helix/RenderPipelines/Core/Volume/` | `Volume` (global, or local by blend distance), `VolumeProfile`, `VolumeComponent`, `VolumeParameter` and the typed parameters (bool, int, float, clamped, min, max, range, color, vectors, layer masks, textures, enums; their no interpolation kinds), `VolumeManager` (layers, priorities, default profiles, stacks), `VolumeStack` |
| `Source/Helix/RenderPipelines/Core/PostProcessing/` | `PostProcessVolumeComponent`, `PostProcessInjectionPoint`, `PostProcessPasses` (the active components of a point as raster passes of the graph, each reading the last one's result) |
| `Source/Helix/RenderPipelines/Core/Debugging/` | `DebugUI` (panels, containers, foldouts, values, bool, int, uint, float, enum, color and vector fields, buttons), `DebugManager` (panels, data blocks, reset), `DebugUITextView` (the panels as text) |
| `Source/Helix/RenderPipelines/Core/Native/` | The internal functions the C# side calls (objects by id, callbacks of C# parameters, effects and widgets) |
| `Source/Helix.RenderPipelines.Core.Runtime/` | The C# side: the generated bindings and the C# written by hand over them (the render graph's C# builders and command buffers, `RTHandle`, the volume parameters, `IPostProcessComponent`, the debug display's C# widgets, `ObservableList<T>`) |
| `ShaderLibrary/` | C# shader code for the pipelines' shaders: `SpaceTransforms`, `Instancing`, `Common`, `Color`, `Packing`, `Sampling` (full screen triangles and quads from the vertex id, for the bound target), `ColorGrading` (LogC, the fast sRGB curves, the neutral tonemapper, strip look up tables), `ACES` (the ACES tonemapper), `Filtering` (bicubic sampling from four bilinear taps) |
| `Shaders/` | The core's shaders: `Hidden/Core/Blit` (nearest and bilinear, full screen and quads) and `Hidden/Core/BlitColorAndDepth` |
| `Tests/` | Tests in both languages run in the engine by a C++ test program on every device (each D3D11 adapter, OpenGL; WebGL 2 on the Web): a C# and a C++ pipeline on the render graph drawing the engine's golden scene with and without a volume's post effect, culling, merging and aliasing counted, volumes, post effects of both languages, handles, blits, the debug display read and changed across the languages; `Tests/Shaders/` calls the whole shader library; `Tests/Api/Helix.RenderPipelines.Core.csv` is the inventory of the public C# surface |

## Use

In C++, in a pipeline's `Render` (an `engine::RenderPipeline`), per camera:

```cpp
using namespace helix::rendering;

struct ListData
{
    RendererListHandle list;
    RenderGraph* graph = nullptr;
};

RTHandles::SetReferenceSize(camera->pixelWidth(), camera->pixelHeight());
RenderGraphParameters parameters;
parameters.commandBuffer = &cmd;
parameters.scriptableRenderContext = &context;
graph.BeginRecording(parameters);
TextureDesc desc(Vector2(1, 1));
desc.colorFormat = GraphicsFormat::R8G8B8A8_UNorm;
desc.clearBuffer = true;
TextureHandle color = graph.CreateTexture(desc);
ListData* data = nullptr;
{
    PassBuilder<ListData> builder = graph.AddRasterRenderPass("Opaque", data);
    data->list = graph.CreateRendererList(listDesc);
    data->graph = &graph;
    builder.UseRendererList(data->list);
    builder.SetRenderAttachment(color, 0);
    builder.SetRenderFunc([](ListData& d, RasterGraphContext& ctx) { ctx.cmd->DrawRendererList(d.graph->ResolveList(d.list)); });
}
TextureHandle result = post.Record(graph, stack, PostProcessInjectionPoint::AfterOpaqueAndSky, color);
// ... a pass blitting result into the imported backbuffer with Blitter::BlitTexture, then:
graph.EndRecordingAndExecute();
context.ExecuteCommandBuffer(cmd);
```

In C#, the same graph:

```csharp
using HelixEngine.Rendering;
using HelixEngine.Rendering.RenderGraphModule;

graph.BeginRecording(new RenderGraphParameters { commandBuffer = cmd, scriptableRenderContext = context });
TextureHandle color = graph.CreateTexture(new TextureDesc(Vector2.one) { colorFormat = GraphicsFormat.R8G8B8A8_UNorm, clearBuffer = true });
using (var builder = graph.AddRasterRenderPass("Opaque", out PassData data))
{
    data.list = graph.CreateRendererList(new RendererListDesc(new ShaderTagId("SRPDefaultUnlit"), culling, camera));
    builder.UseRendererList(data.list);
    builder.SetRenderAttachment(color, 0);
    builder.SetRenderFunc((PassData d, RasterGraphContext ctx) => ctx.cmd.DrawRendererList(d.list));
}
TextureHandle result = post.Record(graph, VolumeManager.instance.stack, PostProcessInjectionPoint.AfterOpaqueAndSky, color);
graph.EndRecordingAndExecute();
```

- A pass whose results nothing reads is culled unless it calls `AllowPassCulling(false)` or changes global state; a
  texture set with `SetGlobalTextureAfterPass` keeps its pass when a later pass declares `UseGlobalTexture`.
- Textures the graph makes live from their first use to their last and share pooled textures of the same kind; the
  pool releases kinds unused for ten frames (`EndFrame`).
- With `nativeRenderPassesEnabled` consecutive raster passes with the same attachments run as subpasses of one native
  render pass; without it they share one target bind.
- A handle made with a scale or a scale function is reallocated when the reference size grows past the largest so
  far; a smaller reference only changes the viewport (`rtHandleProperties.rtHandleScale` is the used part).
- Volumes without colliders are global, or local within their blend distance of their position. The volume component
  types are the script classes of both languages below `VolumeComponent`, ordered by full name.
- A post process effect's `Render` draws from the source into the bound target. A C# effect renders in the frame of a
  C# render graph; a C++ effect in graphs of both languages.
- The blit shaders read `_BlitTexture` (a global the Blitter sets) with `_BlitScaleBias`; the bilinear passes sample
  with the texture's own sampler. Full screen draws need no camera matrices: the shader library flips them for texture
  targets by the engine's `helix_ProjectionParams`.
- The debug display's panels are shared by both languages: a C# widget keeps its delegates and settings in C#, a C++
  widget is seen from C# as the C# class of its kind (its settings as they were when C# first saw it).

## Build

```
git clone https://github.com/kisstp2006/Helix.RenderPipelines.Core
git -C Helix.RenderPipelines.Core submodule update --init
cmake --preset windows-debug -S Helix.RenderPipelines.Core
cmake --build Helix.RenderPipelines.Core/build/windows-debug
ctest --test-dir Helix.RenderPipelines.Core/build/windows-debug
```

The .NET 11 SDK is needed (`HELIX_DOTNET_ROOT` or `DOTNET_ROOT`).

## License

GPL-3.0, see `LICENSE`.
