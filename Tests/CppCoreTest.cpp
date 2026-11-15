#include <Support/CoreHost.h>

#include <Helix/RenderPipelines/Core/Textures/RTHandle.h>
#include <Helix/RenderPipelines/Core/Utilities/CoreUtils.h>
#include <Helix/Testing/Test.h>

using namespace helix;
using namespace helix::rendering;
using namespace helix::rpcore::testing;

namespace
{
    Vector2Int QuarterWidth(Vector2Int size, void*) { return Vector2Int(size.x / 4, size.y); }
}

HX_TEST(Cpp, ScaledHandlesFollowTheReferenceSize)
{
    // The C++ API gives what the C# test sees through the bindings.
    HX_REQUIRE(StartScripts());
    RTHandleSystem system;
    system.Initialize(64, 32);
    RTHandleAllocInfo half;
    half.format = GraphicsFormat::R8G8B8A8_UNorm;
    half.name = "Half";
    RTHandle* h = system.Alloc(Vector2(0.5f, 0.5f), half);
    RTHandleAllocInfo named;
    named.name = "Quarter";
    RTHandle* q = system.Alloc(QuarterWidth, nullptr, named);
    HX_REQUIRE(h->rt() && q->rt());
    HX_CHECK(h->rt()->width() == 32 && h->rt()->height() == 16 && q->rt()->width() == 16 && q->rt()->height() == 32 && h->useScaling());
    HX_CHECK(h->rt()->Name() == StringView("Half_32x16_R8G8B8A8_UNorm_Tex2D"));
    system.SetReferenceSize(40, 20);
    HX_CHECK(h->rt()->width() == 32 && system.rtHandleProperties().rtHandleScale.x == 0.625f);
    system.SetReferenceSize(100, 50);
    HX_CHECK(h->rt()->width() == 50 && h->rt()->height() == 25 && q->rt()->width() == 25 && system.GetMaxWidth() == 100);
    system.ResetReferenceSize(20, 10);
    HX_CHECK(h->rt()->width() == 10 && h->rt()->height() == 5);
    u64 id = h->Id();
    h->Release();
    HX_CHECK(RTHandle::Find(id) == nullptr && q->rt() != nullptr);
    HX_CHECK(CoreUtils::GetRenderTargetAutoName(8, 4, 1, GraphicsFormat::R16G16B16A16_SFloat, engine::TextureDimension::None, "Color", true) ==
             StringView("Color_8x4_Mips_R16G16B16A16_SFloat"));
    system.Dispose();
    // The render textures the handles made.
    engine::testing::Cleanup();
}
