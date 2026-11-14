using System;
using HelixEngine;
using HelixEngine.Rendering;

namespace Tests
{
    public enum CsDebugMode
    {
        None,
        Albedo = 3,
        Normals = 7,
    }

    // Debug display data: values reset by the manager.
    class CsDebugData : IDebugData
    {
        public bool wireframe;
        public int samples = 4;
        public float exposure = 1.5f;
        public int mode = (int)CsDebugMode.None;
        public Color tint = Color.white;
        public bool detail = true;

        public Action GetReset() => () =>
        {
            wireframe = false;
            samples = 4;
            exposure = 1.5f;
            mode = (int)CsDebugMode.None;
            tint = Color.white;
        };
    }

    // The debug display's model: a panel of widgets bound to data, queries, validation, reset, the text view.
    public class CsDebugTests : GameBehaviour
    {
        void Start()
        {
            var data = new CsDebugData();
            DebugManager manager = DebugManager.instance;
            bool shown = false;
            manager.onDisplayRuntimeUIChanged += v => shown = v;
            int changes = 0;
            DebugUI.Panel panel = manager.GetPanel("Rendering", true);
            var samples = new DebugUI.IntField { displayName = "Samples", getter = () => data.samples, setter = v => data.samples = v, min = () => 1, max = () => 8 };
            samples.onValueChanged = (f, v) => changes++;
            int pressed = 0;
            panel.children.Add(
                new DebugUI.Value { displayName = "Draw Calls", getter = () => 42 },
                new DebugUI.BoolField { displayName = "Wireframe", getter = () => data.wireframe, setter = v => data.wireframe = v, flags = DebugUI.Flags.RuntimeOnly },
                samples,
                new DebugUI.Foldout("Lighting", new ObservableList<DebugUI.Widget>
                {
                    new DebugUI.FloatField { displayName = "Exposure", getter = () => data.exposure, setter = v => data.exposure = v, min = () => 0f, decimals = 2 },
                    new DebugUI.ColorField { displayName = "Tint", getter = () => data.tint, setter = v => data.tint = v },
                }) { opened = true },
                new DebugUI.EnumField { displayName = "Mode", getter = () => data.mode, setter = v => data.mode = v, autoEnum = typeof(CsDebugMode) },
                new DebugUI.Foldout("Closed", new ObservableList<DebugUI.Widget> { new DebugUI.BoolField { displayName = "Detail", getter = () => data.detail, setter = v => data.detail = v } }),
                new DebugUI.Button { displayName = "Capture", action = () => pressed++ });
            manager.RegisterData(data);

            samples.SetValue(20);
            samples.SetValue(20);
            var exposure = (DebugUI.FloatField)manager.GetItem("Rendering -> Lighting -> Exposure");
            exposure.SetValue(-3f);
            ((DebugUI.ColorField)manager.GetItem("Rendering -> Lighting -> Tint")).SetValue(new Color(2f, 0.5f, -1f, 1f));
            var mode = (DebugUI.EnumField)manager.GetItem("Rendering -> Mode");
            mode.SetValue((int)CsDebugMode.Normals);
            int modeIndex = mode.currentIndex;
            mode.SetValue(5);
            ((DebugUI.Button)manager.GetItem("Rendering -> Capture")).action();
            Notes.Note($"values:{data.samples}:{changes}:{Notes.F(data.exposure)}:{Notes.F(data.tint.r)}:{Notes.F(data.tint.b)}:{modeIndex}:{data.mode}:{pressed}");
            Notes.Note($"query:{exposure.queryPath}:{panel.Contains("Rendering -> Closed -> Detail")}:{manager.GetItems(DebugUI.Flags.RuntimeOnly).Length}:" +
                       $"{manager.GetItem("Rendering -> Nothing") == null}:{manager.PanelIndex("rendering")}");
            mode.SetValue((int)CsDebugMode.Albedo);
            foreach (string line in DebugUITextView.Render().Split('\n'))
                if (line.Length > 0)
                    Notes.Note("text:" + line);
            manager.displayRuntimeUI = true;
            manager.Reset();
            Notes.Note($"reset:{data.samples}:{Notes.F(data.exposure)}:{data.mode}:{shown}:{manager.isAnyDebugUIActive}");
            manager.displayRuntimeUI = false;
            manager.UnregisterData(data);
            manager.RemovePanel("Rendering");
            Notes.Note($"removed:{manager.GetPanel("Rendering") == null}:{manager.panels.Count}");
        }
    }
}
