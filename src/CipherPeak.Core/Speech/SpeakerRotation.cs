using System.Collections.Generic;

namespace CipherPeak.Core.Speech
{
    /// <summary>
    /// Picks which Bing Bong says the next line. Strict alternation while both are available,
    /// automatic fall back to the survivor while the other one is being replaced.
    /// </summary>
    public sealed class SpeakerRotation
    {
        private int _lastIndex = -1;

        /// <summary>Index of the speaker that should talk next, or -1 when nobody can.</summary>
        public int Next(IReadOnlyList<bool> available)
        {
            if (available == null || available.Count == 0) return -1;

            // Preferred: the one that did not speak last.
            for (int step = 1; step <= available.Count; step++)
            {
                int index = (_lastIndex + step) % available.Count;
                if (index < 0) index += available.Count;
                if (!available[index]) continue;
                _lastIndex = index;
                return index;
            }

            return -1;
        }

        public void Reset() { _lastIndex = -1; }
    }
}
