using System;
using System.Globalization;
using System.Text;
using Helix.Scripting;
using HelixEngine;
using HelixEngine.Experimental.Rendering;
using HelixEngine.Rendering;

namespace Tests
{
    // The lines the C# tests write, read by the C++ test program (its export by name).
    static unsafe class Notes
    {
        static delegate* unmanaged[Cdecl]<byte*, int, void> s_Note;

        public static void Note(string message)
        {
            if (s_Note == null)
                s_Note = (delegate* unmanaged[Cdecl]<byte*, int, void>)Host.FindExport("CoreTests.Note");
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            fixed (byte* p = bytes)
                s_Note(p, bytes.Length);
        }

        public static string F(float v) => v.ToString("F3", CultureInfo.InvariantCulture);
        public static string V(Vector4 v) => F(v.x) + "," + F(v.y) + "," + F(v.z) + "," + F(v.w);
        public static string S(RenderTexture rt) => rt == null ? "null" : rt.width + "x" + rt.height;
    }

    // A handle system: scaled handles follow the reference size (grown, never shrunk, unless reset), fixed ones keep theirs.
    public class CsRTHandleTests : GameBehaviour
    {
        void Start()
        {
            var system = new RTHandleSystem();
            system.Initialize(64, 32);
            RTHandle half = system.Alloc(new Vector2(0.5f, 0.5f), colorFormat: GraphicsFormat.R8G8B8A8_UNorm, name: "Half");
            RTHandle quarter = system.Alloc(size => new Vector2Int(size.x / 4, size.y), name: "Quarter");
            RTHandle fixedSize = system.Alloc(10, 10, name: "Fixed");
            Notes.Note($"alloc:{Notes.S(half.rt)}:{Notes.S(quarter.rt)}:{Notes.S(fixedSize.rt)}:{half.useScaling}:{fixedSize.useScaling}:{half.rt.name}");
            system.SetReferenceSize(40, 20);
            Notes.Note($"smaller:{Notes.S(half.rt)}:{Notes.V(system.rtHandleProperties.rtHandleScale)}:{half.GetScaledSize(new Vector2Int(40, 20))}");
            system.SetReferenceSize(100, 50);
            Notes.Note($"larger:{Notes.S(half.rt)}:{Notes.S(quarter.rt)}:{Notes.S(fixedSize.rt)}:{system.GetMaxWidth()}x{system.GetMaxHeight()}:" +
                       $"{Notes.V(system.rtHandleProperties.rtHandleScale)}:{half.referenceSize}");
            system.ResetReferenceSize(20, 10);
            Notes.Note($"reset:{Notes.S(half.rt)}:{system.GetMaxWidth()}x{system.GetMaxHeight()}:{system.rtHandleProperties.previousViewportSize}");
            RTHandle white = system.Alloc(Texture2D.whiteTexture);
            RTHandle camera = system.Alloc(new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget), "Camera");
            Notes.Note($"wrapped:{(Texture)white == Texture2D.whiteTexture}:{white.rt == null}:{camera.name}:" +
                       $"{camera.nameID == new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget)}:{white.GetScaledSize()}");
            half.Release();
            Notes.Note($"released:{half.rt == null}:{quarter.rt != null}");
            system.Dispose();
            Notes.Note($"disposed:{quarter.rt == null}:{fixedSize.rt != null}");
            fixedSize.Release();
            Notes.Note($"names:{CoreUtils.GetRenderTargetAutoName(8, 4, 1, GraphicsFormat.R16G16B16A16_SFloat, "Color", true)}:" +
                       $"{CoreUtils.GetTextureAutoName(8, 4, TextureFormat.RGBA32, TextureDimension.Tex2D, "Lut")}");
            Material missing = CoreUtils.CreateEngineMaterial("Missing/Shader");
            Material engine = CoreUtils.CreateEngineMaterial(Shader.Find("Tests/Unlit"));
            Notes.Note($"materials:{missing == null}:{engine != null && engine.hideFlags == HideFlags.HideAndDontSave}");
            CoreUtils.Destroy(engine);
        }
    }

    // Blits with the core's shaders: a 2x2 texture (red, green at the bottom; blue, white at the top) through each way into the screen.
    public class CsBlitTests : GameBehaviour
    {
        Texture2D m_Texture;
        RTHandle m_Source;
        RTHandle m_Target;
        int m_Mode = -1;

        void Start()
        {
            Blitter.Initialize(Shader.Find("Hidden/Core/Blit"), Shader.Find("Hidden/Core/BlitColorAndDepth"));
            m_Texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            m_Texture.SetPixels32(new[] { new Color32(255, 0, 0, 255), new Color32(0, 255, 0, 255), new Color32(0, 0, 255, 255), new Color32(255, 255, 255, 255) });
            m_Texture.Apply(false);
            m_Source = RTHandles.Alloc(m_Texture);
            m_Target = RTHandles.Alloc(4, 4, colorFormat: GraphicsFormat.R8G8B8A8_UNorm, name: "BlitTarget");
        }

        void Update()
        {
            m_Mode++;
            var cmd = new CommandBuffer { name = "Blits" };
            RenderTargetIdentifier screen = BuiltinRenderTextureType.CameraTarget;
            if (m_Mode == 0)
            {
                // Straight into the screen.
                CoreUtils.SetRenderTarget(cmd, screen, ClearFlag.All, Color.black);
                Blitter.BlitTexture(cmd, m_Source, new Vector4(1, 1, 0, 0), 0, false);
            }
            else if (m_Mode == 1)
            {
                // Through a render texture handle.
                Blitter.BlitCameraTexture(cmd, m_Source, m_Target);
                CoreUtils.SetRenderTarget(cmd, screen, ClearFlag.All, Color.black);
                Blitter.BlitTexture(cmd, m_Target, new Vector4(1, 1, 0, 0), 0, true);
            }
            else if (m_Mode == 2)
            {
                // Into the top right quarter.
                CoreUtils.SetRenderTarget(cmd, screen, ClearFlag.All, Color.black);
                Blitter.BlitQuad(cmd, m_Texture, new Vector4(1, 1, 0, 0), new Vector4(0.5f, 0.5f, 0.5f, 0.5f), 0, false);
            }
            else if (m_Mode == 3)
            {
                CoreUtils.SetRenderTarget(cmd, screen, ClearFlag.All, Color.black);
                Blitter.BlitColorAndDepth(cmd, m_Texture, null, new Vector4(1, 1, 0, 0), 0, false);
            }
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();
        }

        void OnDestroy()
        {
            m_Target?.Release();
            m_Source?.Release();
            Blitter.Cleanup();
            if (m_Texture != null)
                DestroyImmediate(m_Texture);
        }
    }
}
