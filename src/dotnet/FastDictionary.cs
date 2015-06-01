using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Dictionary
{
    public unsafe class FastDictionary<TKey, TValue>
    {
        const int InvalidNodePosition = -1;

        private struct Node
        {
            public const uint kUnusedHash = 0xFFFFFFFF;
            public const uint kDeletedHash = 0xFFFFFFFE;

            internal uint Hash;
            internal TKey Key;
            internal TValue Value;

            public bool IsUnused
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get { return Hash == kUnusedHash; }
            }
            public bool IsDeleted
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get { return Hash == kDeletedHash; }
            }
        }

        /// <summary>
        /// Minimum size we're willing to let hashtables be.
        /// Must be a power of two, and at least 4.
        /// Note, however, that for a given hashtable, the initial size is a function of the first constructor arg, and may be > kMinBuckets.
        /// </summary>
        const int kMinBuckets = 4;

        /// <summary>
        /// By default, if you don't specify a hashtable size at construction-time, we use this size.  Must be a power of two, and  at least kMinBuckets.
        /// </summary>
        const int kInitialCapacity = 32;

        // TLoadFactor4 - controls hash map load. 4 means 100% load, ie. hashmap will grow
        // when number of items == capacity. Default value of 6 means it grows when
        // number of items == capacity * 3/2 (6/4). Higher load == tighter maps, but bigger
        // risk of collisions.
        static int tLoadFactor4 = 5;

        private Node[] _nodes;
        private int _capacity;
        private uint _capacityMask;

        private int _size; // This is the real counter of how many items are in the hash-table (regardless of buckets)
        private int _numberOfUsed; // How many used buckets. 
        private int _numberOfDeleted; // how many occupied buckets are marked deleted
        private int _nextGrowthThreshold;


        private readonly IEqualityComparer<TKey> comparer;
        public IEqualityComparer<TKey> Comparer
        {
            get { return comparer; }
        }

        public int Capacity
        {
            get { return _capacity; }
        }

        public int Count
        {
            get { return _numberOfUsed - _numberOfDeleted; }
        }

        public bool IsEmpty
        {
            get { return Count == 0; }
        }

        private int NextPowerOf2(int v)
        {
            v--;
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            v++;

            return v;
        }

        public FastDictionary(int initialBucketCount = kInitialCapacity)
        {
            // Contract.Ensures(_capacity >= initialBucketCount);

            this.comparer = comparer ?? EqualityComparer<TKey>.Default;

            int newCapacity = NextPowerOf2(initialBucketCount >= kMinBuckets ? initialBucketCount : kMinBuckets);

            _nodes = new Node[newCapacity];
            for (int i = 0; i < newCapacity; i++)
                _nodes[i].Hash = Node.kUnusedHash;

            _capacity = newCapacity;
            _capacityMask = (uint)(newCapacity - 1);
            _numberOfUsed = _size;
            _numberOfDeleted = 0;
            _nextGrowthThreshold = _capacity * 4 / tLoadFactor4;
        }

        public void Add(TKey key, TValue value)
        {
            //if (key == null)
            //    throw new ArgumentNullException("key");
            //Contract.EndContractBlock();

            //ResizeIfNeeded();

            //uint hash = GetInternalHashCode(key);
            //uint bucket = hash & _capacityMask;

            //if (TryAdd(ref _nodes[bucket], hash, ref key, value))
            //    return;

            //Contract.Assert(_numberOfUsed < _capacity);

            //uint numProbes = 0;
            //bool couldInsert = false;
            //while (!couldInsert)
            //{
            //    numProbes++;

            //    bucket = (bucket + numProbes) & _capacityMask;

            //    couldInsert = TryAdd(ref _nodes[bucket], hash, ref key, value);
            //}
        }

        public bool Remove(TKey key)
        {
            if (key == null)
                throw new ArgumentNullException("key");
            Contract.EndContractBlock();

            int bucket = Lookup(key);
            if (bucket == InvalidNodePosition)
                return false;

            SetDeleted(ref _nodes[bucket]);

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetDeleted(ref Node node)
        {
            Contract.Ensures(node.IsDeleted);
            Contract.Ensures(_size <= Contract.OldValue<int>(_size));
            Contract.Ensures(_numberOfDeleted >= Contract.OldValue<int>(_numberOfDeleted));

            if (node.Hash != Node.kDeletedHash)
            {
                SetNode(ref node, Node.kDeletedHash, default(TKey), default(TValue));

                _numberOfDeleted++;
                _size--;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ResizeIfNeeded()
        {
            if (_size >= _nextGrowthThreshold)
            {
                Grow(_capacity * 2);
            }
        }

        public void Shrink()
        {
            Shrink(_size);
        }

        public void Shrink(int newCapacity)
        {
            Contract.Requires(newCapacity >= 0);

            if (newCapacity < _size)
                throw new ArgumentException("Cannot shrink the dictionary beyond the amount of elements in it.", "newCapacity");

            newCapacity = NextPowerOf2(newCapacity);
            if (newCapacity < kMinBuckets)
                newCapacity = kMinBuckets;

            var newNodes = new Node[newCapacity];
            for (int i = 0; i < newCapacity; i++)
                newNodes[i].Hash = Node.kUnusedHash;

            Rehash(ref newNodes, _capacity, _nodes);

            _capacity = newCapacity;
            _capacityMask = (uint)(newCapacity - 1);
            _nodes = newNodes;
            _numberOfUsed = _size;
            _numberOfDeleted = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetNode(ref Node node, uint hash, TKey key, TValue value)
        {
            node.Hash = hash;
            node.Key = key;
            node.Value = value;
        }

        public TValue this[TKey key]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Contract.Requires(key != null);

                int hash = GetInternalHashCode(key);
                //int hash = comparer.GetHashCode(key) & 0x7FFFFFFF;
                //uint bucket = hash & _capacityMask;
                int bucket = hash % _capacity;

                var nodes = _nodes;
                if (CompareKey(nodes[bucket].Hash, nodes[bucket].Key, (uint)hash, key))
                    return nodes[bucket].Value;

                //TValue value;
                //if (CompareKey(ref nodes[bucket], ref key, (uint) hash, out value))
                //    return value;

                int numProbes = 1; // how many times we've probed
                Contract.Assert(_numberOfUsed < _capacity);

                bool canContinue = true;
                while (canContinue)
                {
                    bucket = (bucket + numProbes) % _capacity;
                    // bucket = (bucket + numProbes) & _capacityMask;

                    //if (CompareKey(nodes[bucket].Hash, nodes[bucket].Key, hash, key, ref canContinue))
                    //    return nodes[bucket].Value;
                    if (CompareKey(ref nodes[bucket], ref key, (uint)hash, ref value, ref canContinue))
                        return value;

                    numProbes++;
                }

                throw new KeyNotFoundException();
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                Contract.Requires(key != null);                

                ResizeIfNeeded();

                //uint hash = GetInternalHashCode(key);
                int hash = comparer.GetHashCode(key) & 0x7FFFFFFF;
                //uint bucket = hash & _capacityMask;
                int bucket = hash % _capacity;

                var nodes = _nodes;

                if (TryInsert(ref nodes[bucket], (uint)hash, key, value))
                    return;

                Contract.Assert(_numberOfUsed < _capacity);

                int numProbes = 1;
                bool couldInsert = false;
                while (!couldInsert)
                {
                    //bucket = (bucket + numProbes) & _capacityMask;
                    bucket = (bucket + numProbes) % _capacity;

                    couldInsert = TryInsert(ref nodes[bucket], (uint)hash, key, value);

                    numProbes++;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryAdd(ref Node node, uint hash, ref TKey key, TValue value)
        {
            if (node.IsDeleted)
            {
                SetNode(ref node, hash, key, value);

                _numberOfDeleted--;
                _size++;

                return true;
            }
            else if (node.IsUnused)
            {
                SetNode(ref node, hash, key, value);

                _numberOfUsed++;
                _size++;

                return true;
            }
            else if (CompareKey(ref node, key, hash))
            {
                throw new ArgumentException("Cannot add duplicated key.", "key");
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryInsert(ref Node node, uint hash, TKey key, TValue value)
        {
            uint nHash = node.Hash;
            if (nHash == Node.kUnusedHash)
            {
                _numberOfUsed++;
                _size++;

                goto SET;
            }
            else if (nHash == Node.kDeletedHash)
            {
                _numberOfDeleted--;
                _size++;

                goto SET;
            }
            else if (CompareKey(ref node, key, hash))
            {
                goto SET;
            }
            return false;

            SET:
            node.Hash = hash;
            node.Key = key;
            node.Value = value;
            return true;

        }



        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateNode(ref Node node, uint hash, TKey key, TValue value)
        {
            if (node.IsDeleted)
            {
                _numberOfDeleted--;
                _size++;
            }
            else if (node.IsUnused)
            {
                _numberOfUsed++;
                _size++;
            }

            SetNode(ref node, hash, key, value);
        }

        public void Clear()
        {
            TKey defaultKey = default(TKey);
            TValue defaultValue = default(TValue);

            for (int i = 0; i < _capacity; i++)
            {
                SetNode(ref _nodes[i], Node.kUnusedHash, defaultKey, defaultValue);
            }

            _numberOfUsed = 0;
            _numberOfDeleted = 0;
            _size = 0;
        }

        public bool Contains(TKey key)
        {
            if (key == null)
                throw new ArgumentNullException("key");
            Contract.EndContractBlock();

            return (Lookup(key) != InvalidNodePosition);
        }


        public void Reserve(int minimumSize)
        {
            int newCapacity = (minimumSize < kMinBuckets ? kInitialCapacity : minimumSize);
            while (newCapacity < _capacity)
                newCapacity *= 2;

            if (newCapacity > _capacity)
                Grow(newCapacity);
        }

        private void Grow(int newCapacity)
        {
            Contract.Requires(newCapacity >= _capacity);
            Contract.Ensures((_capacity & (_capacity - 1)) == 0);

            newCapacity = NextPowerOf2(newCapacity);

            var newNodes = new Node[newCapacity];
            for (int i = 0; i < newCapacity; i++)
                newNodes[i].Hash = Node.kUnusedHash;

            Rehash(ref newNodes, _capacity, _nodes);

            _capacity = newCapacity;
            _capacityMask = (uint)(newCapacity - 1);
            _nodes = newNodes;
            _numberOfUsed = _size;
            _numberOfDeleted = 0;
            _nextGrowthThreshold = _capacity * 4 / tLoadFactor4;
        }

        public bool TryLookup( TKey key, out TValue value )
        {
            throw new NotImplementedException();
            //uint hash = GetInternalHashCode(key);
            //uint bucket = hash & _capacityMask;

            //if (CompareKey(ref _nodes[bucket], ref key, hash, out value))
            //    return true;

            //uint numProbes = 0; // how many times we've probed
            //Contract.Assert(_numberOfUsed < _capacity);

            //bool canContinue = true;
            //while (canContinue)
            //{
            //    numProbes++;
            //    bucket = (bucket + numProbes) & _capacityMask;

            //    if (CompareKey(ref _nodes[bucket], ref key, hash, ref value, ref canContinue))
            //        return true;
            //}
           
            //return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CompareKey(uint nHash, TKey nKey, uint hash, TKey key, ref bool probeAgain)
        {
            if (nHash != hash)
                return false;

            if (comparer.Equals(nKey, key))
            {
                probeAgain = nHash != Node.kUnusedHash;
                return true;
            }

            return false;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CompareKey(ref Node node, ref TKey key, uint hash, ref TValue value, ref bool probeAgain)
        {
            var nHash = node.Hash;
            if (nHash != hash)
                return false;

            if (comparer.Equals(node.Key, key))
            {
                value = node.Value;
                probeAgain = nHash != Node.kUnusedHash;
                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CompareKey(ref Node node, ref TKey key, uint hash, out TValue value)
        {
            var nHash = node.Hash;
            if (nHash != hash)
                goto NOTEQUAL;

            var nKey = node.Key;
            if (comparer.Equals(nKey, key))
            {
                value = node.Value;
                return true;
            }

            NOTEQUAL:
            value = default(TValue);
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CompareKey(uint nHash, TKey nKey, uint hash, TKey key)
        {
            if (nHash != hash)
                return false;

            if (comparer.Equals(nKey, key))
                return true;

            return false;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <returns>Position of the node in the array</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Lookup(TKey key)
        {
            throw new NotImplementedException();

            //uint hash = GetInternalHashCode(key);
            //uint bucket = hash & _capacityMask;

            //Node n = _nodes[bucket];
            //if (CompareKey(ref _nodes[bucket], key, hash))
            //    return (int)bucket;

            //uint numProbes = 0; // how many times we've probed

            //Contract.Assert(_numberOfUsed < _capacity);
            //while (!_nodes[bucket].IsUnused)
            //{
            //    numProbes++;

            //    bucket = (bucket + numProbes) & _capacityMask;
            //    if (CompareKey(ref _nodes[bucket], key, hash))
            //        return (int)bucket;
            //}

            //return InvalidNodePosition;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetInternalHashCode(TKey key)
        {
            return comparer.GetHashCode(key) & 0x7FFFFFFF;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CompareKey(ref Node n, TKey key, uint hash)
        {
            if (n.Hash != hash)
                return false;

            return comparer.Equals(n.Key, key);
        }

        private static void Rehash(ref Node[] newNodes, int capacity, Node[] nodes)
        {
            uint mask = (uint)newNodes.Length - 1;
            for (int it = 0; it < nodes.Length; it++)
            {
                var hash = nodes[it].Hash;
                uint bucket = hash & mask;

                uint numProbes = 0;
                while (!newNodes[bucket].IsUnused)
                {
                    numProbes++;
                    bucket = (bucket + numProbes) & mask;
                }

                newNodes[bucket] = nodes[it];
            }
        }
    }


    //public unsafe static class Hashing
    //{
    //    /// <summary>
    //    /// A port of the original XXHash algorithm from Google in 32bits 
    //    /// </summary>
    //    /// <<remarks>The 32bits and 64bits hashes for the same data are different. In short those are 2 entirely different algorithms</remarks>
    //    public static class XXHash32
    //    {
    //        [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //        public static unsafe uint CalculateInline(byte* buffer, int len, uint seed = 0)
    //        {
    //            unchecked
    //            {
    //                uint h32;

    //                byte* bEnd = buffer + len;

    //                if (len >= 16)
    //                {
    //                    byte* limit = bEnd - 16;

    //                    uint v1 = seed + PRIME32_1 + PRIME32_2;
    //                    uint v2 = seed + PRIME32_2;
    //                    uint v3 = seed + 0;
    //                    uint v4 = seed - PRIME32_1;

    //                    do
    //                    {
    //                        v1 += *((uint*)buffer) * PRIME32_2;
    //                        buffer += sizeof(uint);
    //                        v2 += *((uint*)buffer) * PRIME32_2;
    //                        buffer += sizeof(uint);
    //                        v3 += *((uint*)buffer) * PRIME32_2;
    //                        buffer += sizeof(uint);
    //                        v4 += *((uint*)buffer) * PRIME32_2;
    //                        buffer += sizeof(uint);

    //                        v1 = RotateLeft32(v1, 13);
    //                        v2 = RotateLeft32(v2, 13);
    //                        v3 = RotateLeft32(v3, 13);
    //                        v4 = RotateLeft32(v4, 13);

    //                        v1 *= PRIME32_1;
    //                        v2 *= PRIME32_1;
    //                        v3 *= PRIME32_1;
    //                        v4 *= PRIME32_1;
    //                    }
    //                    while (buffer <= limit);

    //                    h32 = RotateLeft32(v1, 1) + RotateLeft32(v2, 7) + RotateLeft32(v3, 12) + RotateLeft32(v4, 18);
    //                }
    //                else
    //                {
    //                    h32 = seed + PRIME32_5;
    //                }

    //                h32 += (uint)len;


    //                while (buffer + 4 <= bEnd)
    //                {
    //                    h32 += *((uint*)buffer) * PRIME32_3;
    //                    h32 = RotateLeft32(h32, 17) * PRIME32_4;
    //                    buffer += 4;
    //                }

    //                while (buffer < bEnd)
    //                {
    //                    h32 += (uint)(*buffer) * PRIME32_5;
    //                    h32 = RotateLeft32(h32, 11) * PRIME32_1;
    //                    buffer++;
    //                }

    //                h32 ^= h32 >> 15;
    //                h32 *= PRIME32_2;
    //                h32 ^= h32 >> 13;
    //                h32 *= PRIME32_3;
    //                h32 ^= h32 >> 16;

    //                return h32;
    //            }
    //        }

    //        public static unsafe uint Calculate(byte* buffer, int len, uint seed = 0)
    //        {
    //            return CalculateInline(buffer, len, seed);
    //        }

    //        public static uint Calculate(string value, Encoding encoder, uint seed = 0)
    //        {
    //            var buf = encoder.GetBytes(value);

    //            fixed (byte* buffer = buf)
    //            {
    //                return CalculateInline(buffer, buf.Length, seed);
    //            }
    //        }
    //        public static uint CalculateRaw(string buf, uint seed = 0)
    //        {
    //            fixed (char* buffer = buf)
    //            {
    //                return CalculateInline((byte*)buffer, buf.Length * sizeof(char), seed);
    //            }
    //        }

    //        public static uint Calculate(byte[] buf, int len = -1, uint seed = 0)
    //        {
    //            if (len == -1)
    //                len = buf.Length;

    //            fixed (byte* buffer = buf)
    //            {
    //                return CalculateInline(buffer, len, seed);
    //            }
    //        }

    //        private static uint PRIME32_1 = 2654435761U;
    //        private static uint PRIME32_2 = 2246822519U;
    //        private static uint PRIME32_3 = 3266489917U;
    //        private static uint PRIME32_4 = 668265263U;
    //        private static uint PRIME32_5 = 374761393U;

    //        [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //        private static uint RotateLeft32(uint value, int count)
    //        {
    //            return (value << count) | (value >> (32 - count));
    //        }
    //    }
    //}
}
