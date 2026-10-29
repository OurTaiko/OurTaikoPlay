#nullable enable
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine.Profiling;

namespace MajdataPlay.Diagnostics
{
    // A profiler sample for a using block, as in MajdataPlay.Diagnostics.
    public readonly ref struct UnityProfiler
    {
        [Conditional("ENABLE_PROFILER")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose() => Profiler.EndSample();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UnityProfiler Create(string name)
        {
            Profiler.BeginSample(name);
            return default;
        }
    }
}
