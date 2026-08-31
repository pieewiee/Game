using System;

namespace Game.Sim
{
    /// <summary>
    /// Deterministic PRNG owned by the simulation. xoshiro256** seeded via
    /// splitmix64. System.Random is deliberately not used anywhere in Game.Sim:
    /// its algorithm differs between runtimes, and this sim must produce
    /// identical output under the dotnet console runner and under Unity's Mono.
    /// The full generator state is part of SimState and is serialised with it.
    /// </summary>
    public sealed class SimRandom
    {
        private ulong _s0, _s1, _s2, _s3;

        public SimRandom(ulong seed)
        {
            // splitmix64 to spread a small seed over 256 bits of state.
            ulong x = seed;
            _s0 = SplitMix(ref x);
            _s1 = SplitMix(ref x);
            _s2 = SplitMix(ref x);
            _s3 = SplitMix(ref x);
            if ((_s0 | _s1 | _s2 | _s3) == 0UL) _s0 = 0x9E3779B97F4A7C15UL;
        }

        private static ulong SplitMix(ref ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            ulong z = x;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        private static ulong Rotl(ulong x, int k) { return (x << k) | (x >> (64 - k)); }

        public ulong NextULong()
        {
            ulong result = Rotl(_s1 * 5UL, 7) * 9UL;
            ulong t = _s1 << 17;
            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = Rotl(_s3, 45);
            return result;
        }

        /// <summary>Uniform double in [0, 1).</summary>
        public double NextDouble()
        {
            return (NextULong() >> 11) * (1.0 / 9007199254740992.0);
        }

        /// <summary>Uniform double in [min, max).</summary>
        public double Range(double min, double max)
        {
            return min + (max - min) * NextDouble();
        }

        /// <summary>Uniform int in [min, max).</summary>
        public int Range(int min, int max)
        {
            if (max <= min) return min;
            return min + (int)(NextULong() % (ulong)(max - min));
        }

        // ------------------------------------------------------------------
        // Stateless hash noise, for the climate generator: the weather must be
        // a pure function of (seed, time) so it is random-access and identical
        // regardless of what order systems sampled it in.
        // ------------------------------------------------------------------

        public static double Hash01(ulong seed, ulong a, ulong b)
        {
            ulong x = seed ^ (a * 0x9E3779B97F4A7C15UL) ^ (b * 0xC2B2AE3D27D4EB4FUL);
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            x ^= x >> 31;
            return (x >> 11) * (1.0 / 9007199254740992.0);
        }

        /// <summary>
        /// Smooth 1D value noise in [-1, 1] over a continuous coordinate,
        /// stateless. Used for multi-day weather waves.
        /// </summary>
        public static double SmoothNoise(ulong seed, ulong channel, double t)
        {
            double floor = Math.Floor(t);
            long i = (long)floor;
            double f = t - floor;
            // smoothstep interpolation between per-integer hash values
            double a = Hash01(seed, channel, unchecked((ulong)i)) * 2.0 - 1.0;
            double b = Hash01(seed, channel, unchecked((ulong)(i + 1))) * 2.0 - 1.0;
            double u = f * f * (3.0 - 2.0 * f);
            return a + (b - a) * u;
        }
    }
}
