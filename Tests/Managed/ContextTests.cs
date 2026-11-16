using System;
using HelixEngine;
using HelixEngine.Rendering;

namespace Tests
{
    // A C# item of a frame: its object is a native ContextItem with this class's Reset.
    public class CsFrameData : ContextItem
    {
        public int value;
        public static int resets;

        public override void Reset()
        {
            value = 0;
            resets++;
        }
    }

    public class CsOtherData : ContextItem
    {
        public override void Reset() { }
    }

    // A container of C#: an item made once per frame, reset by Dispose and reused; Create twice and Get of an item not
    // made throw.
    public class CsContextTests : GameBehaviour
    {
        void Start()
        {
            var frame = new ContextContainer();
            CsFrameData a = frame.GetOrCreate<CsFrameData>();
            a.value = 7;
            CsFrameData b = frame.Get<CsFrameData>();
            bool same = ReferenceEquals(a, b) && b.value == 7 && frame.Contains<CsFrameData>() && !frame.Contains<CsOtherData>();
            bool twice = false, missing = false;
            try { frame.Create<CsFrameData>(); } catch (InvalidOperationException) { twice = true; }
            try { frame.Get<CsOtherData>(); } catch (InvalidOperationException) { missing = true; }
            CsFrameData.resets = 0;
            frame.Dispose();
            bool reset = CsFrameData.resets == 1 && a.value == 0 && !frame.Contains<CsFrameData>();
            CsFrameData again = frame.Create<CsFrameData>();
            Notes.Note($"context:{same}:{twice}:{missing}:{reset}:{ReferenceEquals(again, a)}");
        }
    }
}
