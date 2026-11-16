using HelixEngine.Rendering;

namespace Helix.Interop
{
    /// <summary>The core's volume objects by their native ids, for the C# sides of pipelines written in C++.</summary>
    public static class VolumeInterop
    {
        /// <summary>The C# object of a native volume stack (0: null).</summary>
        public static VolumeStack StackOf(ulong stack) => VolumeStack.Of(stack);

        public static ulong IdOf(VolumeStack stack) => stack?.m_Id ?? 0;
    }
}
