# Helix.RenderPipelines.Core

The shared core of the Helix engine's scriptable render pipelines, in C#: the render graph (passes recorded with the
textures and renderer lists they use, then culled, given pooled textures that alias, merged into native render
passes and executed into one command buffer), render textures sized from a reference size (`RTHandle`,
`RTHandleSystem`, `RTHandles`), copies with the core's blit shaders (`Blitter`), the volume framework (profiles of
volume components blended by priority, weight and distance), a post processing framework over volumes and the render
graph, the debug display's model, the pipelines' helpers (`CoreUtils`), and the shader library the pipelines' C#
shaders call. It runs over the engine's script API (HelixEngine.CoreModule) and its scriptable pipeline API (renderer
lists, native render passes, `DrawProcedural`, readbacks); the core's shaders are C# too, compiled from their IL by
the engine when they load.

## Contents

| Path | Content |
|---|---|
| `Runtime/RenderGraph/` | `RenderGraph` (`BeginRecording`, `ImportTexture`, `ImportBackbuffer`, `CreateTexture`, `CreateRendererList`, `AddRasterRenderPass`, `AddComputePass`, `AddUnsafePass`, `EndRecordingAndExecute`, `EndFrame`, `Cleanup`), the builders (`IRasterRenderGraphBuilder` and the others), `TextureHandle`, `RendererListHandle`, `TextureDesc`, `RenderGraphParameters`, `RenderGraphDefaultResources`, the contexts and their command buffers (`RasterCommandBuffer`, `ComputeCommandBuffer`, `UnsafeCommandBuffer`) |
| `Runtime/Textures/` | `RTHandle`, `RTHandleSystem` (handles scaled from the reference size, grown when it grows, reset), `RTHandles` (the shared system), `RTHandleProperties`, `ScaleFunc`, `DepthBits`, `MSAASamples` |
| `Runtime/Utilities/` | `Blitter` (textures, handles, quads, color and depth into the bound target or a handle; overloads for render graph passes), `CoreUtils` (targets set and cleared with the viewport of a handle, engine materials, keywords, full screen draws, target names), `ClearFlag` |
| `Runtime/Volume/` | `Volume` (global, or local by blend distance), `VolumeProfile`, `VolumeComponent`, `VolumeParameter<T>` and the typed parameters (bool, int, float, clamped, min, max, range, color, vectors, textures, enum; their no interpolation kinds), `VolumeManager` (layers, priorities, default profiles, stacks), `VolumeStack` |
| `Runtime/PostProcessing/` | `PostProcessVolumeComponent`, `IPostProcessComponent`, `PostProcessInjectionPoint`, `PostProcessPasses` (the active components of a point as raster passes of the graph, each reading the last one's result) |
| `Runtime/Debugging/` | `DebugManager` (panels, data blocks, reset), `DebugUI` (panels, containers, foldouts, values, bool, int, float, enum and color fields, buttons), `ObservableList<T>`, `DebugUITextView` (the panels as text) |
| `ShaderLibrary/` | C# shader code for the pipelines' shaders: `SpaceTransforms`, `Instancing`, `Common`, `Color`, `Packing`, `Sampling` (full screen triangles and quads from the vertex id, for the bound target) |
| `Shaders/` | The core's shaders: `Hidden/Core/Blit` (nearest and bilinear, full screen and quads) and `Hidden/Core/BlitColorAndDepth` |
| `Tests/` | C# tests run in the engine by a C++ test program on every device (each D3D11 adapter, OpenGL; WebGL 2 on the Web): a pipeline on the render graph drawing the engine's golden scene with and without a volume's post effect, culling, merging and aliasing counted, volumes, handles, blits, the debug display; `Tests/Shaders/` calls the whole shader library; `Tests/Api/Helix.RenderPipelines.Core.csv` is the inventory of the public surface |

## Use

```csharp
using HelixEngine;
using HelixEngine.Experimental.Rendering;
using HelixEngine.Rendering;
using HelixEngine.Rendering.RenderGraphModule;
using HelixEngine.Rendering.RendererUtils;

class PassData { public RendererListHandle list; }

// In a pipeline's Render, per camera (the culling results, a command buffer and the graph made before).
RTHandles.SetReferenceSize(camera.pixelWidth, camera.pixelHeight);
graph.BeginRecording(new RenderGraphParameters { commandBuffer = cmd, scriptableRenderContext = context });
TextureHandle color = graph.CreateTexture(new TextureDesc(Vector2.one) { colorFormat = GraphicsFormat.R8G8B8A8_UNorm, clearBuffer = true });
TextureHandle backbuffer = graph.ImportBackbuffer(BuiltinRenderTextureType.CameraTarget);
using (var builder = graph.AddRasterRenderPass("Opaque", out PassData data))
{
    data.list = graph.CreateRendererList(new RendererListDesc(new ShaderTagId("SRPDefaultUnlit"), culling, camera));
    builder.UseRendererList(data.list);
    builder.SetRenderAttachment(color, 0);
    builder.SetRenderFunc((PassData d, RasterGraphContext ctx) => ctx.cmd.DrawRendererList(d.list));
}
TextureHandle result = post.Record(graph, VolumeManager.instance.stack, PostProcessInjectionPoint.AfterOpaqueAndSky, color);
// ... a pass blitting result into backbuffer with Blitter.BlitTexture, then:
graph.EndRecordingAndExecute();
context.ExecuteCommandBuffer(cmd);
```

- A pass whose results nothing reads is culled unless it calls `AllowPassCulling(false)` or changes global state; a
  texture set with `SetGlobalTextureAfterPass` keeps its pass when a later pass declares `UseGlobalTexture`.
- Textures the graph makes live from their first use to their last and share pooled textures of the same kind; the
  pool releases kinds unused for ten frames (`EndFrame`).
- With `nativeRenderPassesEnabled` consecutive raster passes with the same attachments run as subpasses of one native
  render pass; without it they share one target bind.
- A handle made with a scale or a scale function is reallocated when the reference size grows past the largest so
  far; a smaller reference only changes the viewport (`rtHandleProperties.rtHandleScale` is the used part).
- Volumes without colliders are global, or local within their blend distance of their position.
- The blit shaders read `_BlitTexture` (a global the Blitter sets) with `_BlitScaleBias`; the bilinear passes sample
  with the texture's own sampler. Full screen draws need no camera matrices: the shader library flips them for texture
  targets by the engine's `helix_ProjectionParams`.
- The debug display is its data model here; `DebugUITextView` renders it as text.

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
