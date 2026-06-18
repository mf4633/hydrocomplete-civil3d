using System;
using System.Collections.Generic;
using HydroComplete.Civil3D.Reading;

namespace HydroComplete.Civil3D.Storage
{
    internal static class NetworkOverrideApplier
    {
        public static void ApplyToPipes(IReadOnlyList<ReadPipe> pipes, IReadOnlyList<NetworkOverrideStore.PipeOverride> overrides)
        {
            if (pipes == null || overrides == null || overrides.Count == 0) return;

            var byKey = new Dictionary<string, NetworkOverrideStore.PipeOverride>(StringComparer.OrdinalIgnoreCase);
            foreach (NetworkOverrideStore.PipeOverride o in overrides)
            {
                if (string.IsNullOrWhiteSpace(o.PipeKey)) continue;
                byKey[o.PipeKey] = o;
            }

            foreach (ReadPipe pipe in pipes)
            {
                string key = string.IsNullOrEmpty(pipe.PipeName)
                    ? pipe.PipeId.Handle.ToString()
                    : pipe.PipeName;
                if (!byKey.TryGetValue(key, out NetworkOverrideStore.PipeOverride? o)) continue;

                if (o.DesignFlowCfs.HasValue && o.DesignFlowCfs.Value > 0)
                    pipe.Segment.DesignFlowCfs = o.DesignFlowCfs.Value;
                if (o.ManningN.HasValue && o.ManningN.Value > 0)
                    pipe.Segment.ManningN = o.ManningN.Value;
            }
        }
    }
}