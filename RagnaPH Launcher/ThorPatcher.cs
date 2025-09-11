using System;

namespace RagnaPHPatcher
{
    public static class ThorPatcher
    {
        /// <summary>
        /// Represents progress information for patching.
        /// </summary>
        public readonly struct PatchProgress
        {
            public PatchProgress(string phase, int index = 0, int count = 0, string path = null)
            {
                Phase = phase;
                Index = index;
                Count = count;
                Path = path;
            }

            public string Phase { get; }
            public int Index { get; }
            public int Count { get; }
            public string Path { get; }
        }

        /// <summary>
        /// Applies a Thor patch archive by merging its contents into the target GRF.
        /// </summary>
        /// <param name="thorFilePath">Path to the downloaded Thor archive.</param>
        /// <param name="progress">Optional progress reporter.</param>
        public static void ApplyPatch(string thorFilePath, IProgress<PatchProgress> progress = null)
        {
            SafeGrfPatcher.ApplyThorPatchTransactional(thorFilePath, progress);
        }
    }
}

