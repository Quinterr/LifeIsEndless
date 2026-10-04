// Ecosphere — stage 07: deterministic world-state hashing (pure, engine-free).
//
// The hash is the backbone of the save/rewind determinism check:
//   * save at day T, keep running to T+10  -> hash A
//   * load the day-T snapshot, run to T+10 -> hash B
//   * CI asserts A == B (see SaveDeterminismTests and Tools/ProductCli).
//
// Requirements that shaped this implementation:
//   * Platform independent: FNV-1a 64 over explicit little-endian byte order. Floats are
//     hashed bitwise (never formatted), so no culture or rounding can leak in.
//   * Incremental and allocation free: the serializer feeds bytes as it writes them.
//   * Order sensitive: entity/component order is part of the simulation state, so a
//     reordering bug must change the hash.

using System;

namespace Ecosphere.Core.Simulation
{
    /// <summary>Incremental FNV-1a 64 hasher over primitive values.</summary>
    public struct StateHasher
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        private ulong _hash;
        private bool _started;

        /// <summary>Creates a hasher seeded with the FNV offset basis.</summary>
        public static StateHasher Create()
        {
            return new StateHasher { _hash = OffsetBasis, _started = true };
        }

        /// <summary>Creates a hasher seeded with a tag, so unrelated streams differ.</summary>
        public static StateHasher CreateTagged(string tag)
        {
            StateHasher hasher = Create();
            hasher.AddString(tag);
            return hasher;
        }

        /// <summary>Current 64-bit digest. Mixing in further values changes it.</summary>
        public ulong Value => _hash;

        public void AddByte(byte value)
        {
            EnsureStarted();
            unchecked
            {
                _hash ^= value;
                _hash *= Prime;
            }
        }

        public void AddBool(bool value) => AddByte(value ? (byte)1 : (byte)0);

        public void AddUInt16(ushort value)
        {
            AddByte((byte)value);
            AddByte((byte)(value >> 8));
        }

        public void AddInt32(int value)
        {
            uint v = unchecked((uint)value);
            AddByte((byte)v);
            AddByte((byte)(v >> 8));
            AddByte((byte)(v >> 16));
            AddByte((byte)(v >> 24));
        }

        public void AddUInt32(uint value)
        {
            AddByte((byte)value);
            AddByte((byte)(value >> 8));
            AddByte((byte)(value >> 16));
            AddByte((byte)(value >> 24));
        }

        public void AddUInt64(ulong value)
        {
            for (int i = 0; i < 8; i++) AddByte((byte)(value >> (i * 8)));
        }

        public void AddInt64(long value) => AddUInt64(unchecked((ulong)value));

        /// <summary>Bitwise float hashing (platform independent; -0f != 0f on purpose).</summary>
        public void AddFloat(float value) => AddUInt32(unchecked((uint)BitConverter.SingleToInt32Bits(value)));

        public void AddDouble(double value) => AddUInt64(unchecked((ulong)BitConverter.DoubleToInt64Bits(value)));

        /// <summary>Struct hashing for blittable data (explicit little-endian per field).</summary>
        public void AddString(string value)
        {
            if (value == null)
            {
                AddInt32(-1);
                return;
            }
            AddInt32(value.Length);
            for (int i = 0; i < value.Length; i++) AddUInt16(value[i]);
        }

        /// <summary>Hashesthe low 32 bits of a Unity-style 2-field handle (entity index/version).</summary>
        public void AddPair(int a, int b)
        {
            AddInt32(a);
            AddInt32(b);
        }

        /// <summary>Mixes in a sub-hash (used for per-entity digests).</summary>
        public void AddDigest(ulong digest) => AddUInt64(digest);

        /// <summary>Digest as a short human-readable hex string (HUD/debug output).</summary>
        public string ToHex() => _hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);

        private void EnsureStarted()
        {
            if (_started) return;
            _hash = OffsetBasis;
            _started = true;
        }
    }
}
