using System;
using System.Collections.Generic;

namespace HelixEngine.Rendering
{
    /// <summary>A named sample of a frame's commands: what Begin and End put around them in a command buffer.</summary>
    public class ProfilingSampler
    {
        static readonly Dictionary<(Type, int), ProfilingSampler> s_ByMarker = new Dictionary<(Type, int), ProfilingSampler>();

        public ProfilingSampler(string name) => this.name = name ?? string.Empty;

        public string name { get; internal set; }

        /// <summary>The sampler of an enum value (one per value, named as the value).</summary>
        public static ProfilingSampler Get<TEnum>(TEnum marker) where TEnum : Enum
        {
            var key = (typeof(TEnum), Convert.ToInt32(marker));
            if (!s_ByMarker.TryGetValue(key, out ProfilingSampler s))
                s_ByMarker[key] = s = new ProfilingSampler(marker.ToString());
            return s;
        }

        public void Begin(CommandBuffer cmd) => cmd?.BeginSample(name);

        public void End(CommandBuffer cmd) => cmd?.EndSample(name);
    }
}
