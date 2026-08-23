namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// Stable integer hashing for procedural seeds. Same inputs always produce
    /// the same value on every platform and run — never use string.GetHashCode
    /// or System.HashCode here, both are randomized per process. Chunks seed
    /// their RNG with Combine(globalSeed, x, y); world-space features (arterial
    /// lines, border crossings) hash their own coordinates so neighbouring
    /// chunks agree without talking to each other.
    /// </summary>
    public static class DeterministicHash
    {
        const uint FnvOffset = 2166136261u;
        const uint FnvPrime = 16777619u;

        /// <summary>FNV-1a over the byte representation of the given ints.</summary>
        public static int Combine(int a, int b)
        {
            uint h = FnvOffset;
            h = Mix(h, a);
            h = Mix(h, b);
            return (int)h;
        }

        public static int Combine(int a, int b, int c)
        {
            uint h = FnvOffset;
            h = Mix(h, a);
            h = Mix(h, b);
            h = Mix(h, c);
            return (int)h;
        }

        public static int Combine(int a, int b, int c, int d)
        {
            uint h = FnvOffset;
            h = Mix(h, a);
            h = Mix(h, b);
            h = Mix(h, c);
            h = Mix(h, d);
            return (int)h;
        }

        /// <summary>Hash mapped to [0, 1) — handy for probability rolls tied to world coordinates.</summary>
        public static float Value01(int a, int b, int c)
        {
            return (uint)Combine(a, b, c) / 4294967296f;
        }

        static uint Mix(uint h, int value)
        {
            unchecked
            {
                uint v = (uint)value;
                h = (h ^ (v & 0xFF)) * FnvPrime;
                h = (h ^ ((v >> 8) & 0xFF)) * FnvPrime;
                h = (h ^ ((v >> 16) & 0xFF)) * FnvPrime;
                h = (h ^ ((v >> 24) & 0xFF)) * FnvPrime;
                return h;
            }
        }

        /// <summary>Floor division that stays correct for negative coordinates.</summary>
        public static int FloorDiv(int a, int b)
        {
            int q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return q;
        }

        /// <summary>Positive modulo that stays correct for negative coordinates.</summary>
        public static int Mod(int a, int b)
        {
            int m = a % b;
            return m < 0 ? m + b : m;
        }
    }
}
