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
        const uint InvalidHash = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct Node
        {
            public static readonly uint kUnusedHash = 0xFFFFFFFF;
            public static readonly uint kDeletedHash = 0xFFFFFFFE;

            internal uint Hash;
            internal KeyValuePair<TKey, TValue> Data;

            public bool IsUnused { get { return Hash == kUnusedHash; } }
            public bool IsDeleted { get { return Hash == kDeletedHash; } }
            public bool IsOccupied { get { return Hash < kDeletedHash; } }

            public Node(uint hash = 0xFFFFFFFF)
            {
                Hash = hash;
                Data = default(KeyValuePair<TKey, TValue>);
            }

            public Node(uint hash, KeyValuePair<TKey, TValue> data)
            {
                Hash = hash;
                Data = data;
            }
        }        

        /// <summary>
        /// How full we let the table get before we resize, by default. Knuth says .8 is good 
        /// higher causes us to probe too much, though it saves memory.
        /// However, we go with .5, getting better performance at the cost of more space (a trade-off explicitly chooses to make).
        /// Feel free to play around with different values, though, via .LoadFactor
        /// </summary>
        const int kOccupancy = 50; // .5
        /// <summary>
        /// How empty we let the table get before we resize lower, by default. (0.0 means never resize lower.)
        /// It should be less than kOccupancy / 2 or we thrash resizing
        /// </summary>
        const int kEmpty = (int)(kOccupancy * 0.4);

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

        /// <summary>
        /// Node size calculated on generic type instantiation.
        /// </summary>
        static readonly int kNodeSize;
        
        /// <summary>
        /// List of empty nodes, used in order to avoid null values. 
        /// </summary>
        static readonly Node[] kEmptyNodes = new Node[0];

        // TLoadFactor4 - controls hash map load. 4 means 100% load, ie. hashmap will grow
        // when number of items == capacity. Default value of 6 means it grows when
        // number of items == capacity * 3/2 (6/4). Higher load == tighter maps, but bigger
        // risk of collisions.
        static int tLoadFactor4 = 6;

        private Node[] _nodes;
        private int _capacity;
        private uint _capacityMask;

        private int _size; // This is the real counter of how many items are in the hash-table (regardless of buckets)
        private int _numberOfUsed; // How many used buckets. 
        private int _numberOfDeleted; // how many occupied buckets are marked deleted


        private IEqualityComparer<TKey> comparer;
        public IEqualityComparer<TKey> Comparer
        {
            get { return comparer; }
        }


        static FastDictionary()
        {
            kNodeSize = Marshal.SizeOf(default(Node));
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

        public int UsedMemory
        {
            get { return _capacity * kNodeSize; }
        }

        public FastDictionary(int initialBucketCount = kInitialCapacity)
        {
            Contract.Requires(initialBucketCount > 0);
            Contract.Ensures(_capacity >= initialBucketCount);
            Contract.Ensures((_capacity & (_capacity - 1)) == 0);

            _capacity = 0;
            _capacityMask = 0;
            _size = 0;
            _numberOfUsed = 0;
            _numberOfDeleted = 0;
            _nodes = kEmptyNodes;

            this.comparer = comparer ?? EqualityComparer<TKey>.Default;

            Reserve(initialBucketCount);
        }

        public void Add(TKey key, TValue value)
        {
            throw new NotImplementedException();
        }

        public bool Remove(TKey key)
        {
            int bucket = Lookup(key);
            if (bucket == InvalidNodePosition)
                return false;

            Contract.Assert(!_nodes[bucket].IsDeleted);
                        
            SetDeleted(bucket);            

            return true;
        }

        private void SetConsiderShrink(bool considerShrink)
        {
            throw new NotImplementedException();
        }

        private void SetDeleted(int bucket)
        {
            if ( _nodes[bucket].Hash != Node.kDeletedHash)
            {
                _nodes[bucket].Hash = Node.kDeletedHash;
                _nodes[bucket].Data = new KeyValuePair<TKey, TValue>();
                _numberOfDeleted++;
                _size--;

                SetConsiderShrink(true);                
            }
        }


        private void SetUnused(int bucket)
        {
            throw new NotImplementedException();
        }



        public TValue this[TKey key]
        {
            get
            {
                int i = Lookup(key);
                if (i == InvalidNodePosition)
                    throw new NotImplementedException();

                return _nodes[i].Data.Value;
            }
            set
            {
                if (_numberOfUsed * tLoadFactor4 >= _capacity * 4)
                    Grow();

                uint hash;
                int i = FindForInsert(key, out hash);
                if (i == InvalidNodePosition)
                    throw new NotImplementedException();

                if (_nodes[i].IsOccupied)
                {
                    Contract.Assert(hash == _nodes[i].Hash && comparer.Equals(key, _nodes[i].Data.Key));
                }

                if (_nodes[i].IsUnused)
                    _numberOfUsed++;

                _nodes[i] = new Node(hash, new KeyValuePair<TKey, TValue>(key, value));
                _size++;
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _capacity; i++)
            {
                _nodes[i].Hash = Node.kUnusedHash;
                _nodes[i].Data = new KeyValuePair<TKey, TValue>();
            }

            _numberOfUsed = 0;
            _numberOfDeleted = 0;
        }

        public bool Contains(TKey key)
        {
            return (Lookup(key) != InvalidNodePosition);
        }


        public void Reserve(int minimumSize)
        {
            int newCapacity = (_capacity == 0 ? kInitialCapacity : _capacity);
            while (newCapacity < minimumSize)
                newCapacity *= 2;

            if (newCapacity > _capacity)
                Grow(newCapacity);
        }

        private void Grow()
        {
            int size = (_capacity == 0 ? kInitialCapacity : _capacity * 2);
            Grow(size);
        }

        private void Grow(int newCapacity)
        {
            Contract.Requires((newCapacity & (newCapacity - 1)) == 0);

            var newNodes = new Node[newCapacity];
            Rehash(ref newNodes, _capacity, _nodes);

            _capacity = newCapacity;
            _capacityMask = (uint)(newCapacity - 1);
            _nodes = newNodes;
            _numberOfUsed = _size;
            _numberOfDeleted = 0;

            Contract.Assert(_numberOfUsed < _capacity);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <returns>Position of the node in the array</returns>
        private int Lookup(TKey key)
        {
            uint hash = GetInternalHashCode(key);
            uint bucketNumber = hash & _capacityMask;

            Node n = _nodes[bucketNumber];
            if (n.Hash == hash && comparer.Equals(key, n.Data.Key))
                return (int)bucketNumber;

            uint numProbes = 0; // how many times we've probed

            Contract.Assert(_capacity == 0 || _numberOfUsed < _capacity);
            while (!n.IsUnused)
            {
                numProbes++;
                bucketNumber = (bucketNumber + numProbes) & _capacityMask;
                n = _nodes[bucketNumber];

                if (CompareKey(n, key, hash))
                    return (int)bucketNumber;
            }

            return InvalidNodePosition;
        }

        private int FindForInsert(TKey key, out uint hash)
        {
            if (_capacity == 0)
            {
                hash = InvalidHash;
                return InvalidNodePosition;
            }

            hash = GetInternalHashCode(key);
            uint bucket = hash & _capacityMask;
            Node n = _nodes[bucket];
            if (n.Hash == hash && comparer.Equals(key, n.Data.Key))
                return (int)bucket;

            int freeNode = InvalidNodePosition;
            if (n.IsDeleted)
                freeNode = (int)bucket;

            uint numProbes = 0;
            Contract.Assert(_numberOfUsed < _capacity);
            while (!n.IsUnused)
            {
                numProbes++;
                bucket = (bucket + numProbes) & _capacityMask;
                
                n = _nodes[bucket];
                if (CompareKey(n, key, hash))
                    return (int)bucket;

                if (n.IsDeleted && freeNode == InvalidNodePosition)
                    freeNode = (int)bucket;
            }

            return freeNode != InvalidNodePosition ? freeNode : (int)bucket;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint GetInternalHashCode(TKey key)
        {
            return (uint)(key.GetHashCode() & 0xFFFFFFFD);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CompareKey(Node n, TKey key, uint hash)
        {
            return (n.Hash == hash && comparer.Equals(key, n.Data.Key));
        }

        private static void Rehash(ref Node[] newNodes, int capacity, Node[] nodes)
        {
            uint mask = (uint)newNodes.Length - 1;
            for (int it = 0; it < nodes.Length; it++)
            {
                var hash = nodes[it].Hash;
                uint i = hash & mask;

                uint numProbes = 0;
                while (!newNodes[i].IsUnused)
                {
                    numProbes++;
                    i = (i + numProbes) & mask;
                }

                newNodes[i] = nodes[it];
            }
        }
    }


    public unsafe static class Hashing
    {
        /// <summary>
        /// A port of the original XXHash algorithm from Google in 32bits 
        /// </summary>
        /// <<remarks>The 32bits and 64bits hashes for the same data are different. In short those are 2 entirely different algorithms</remarks>
        public static class XXHash32
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static unsafe uint CalculateInline(byte* buffer, int len, uint seed = 0)
            {
                unchecked
                {
                    uint h32;

                    byte* bEnd = buffer + len;

                    if (len >= 16)
                    {
                        byte* limit = bEnd - 16;

                        uint v1 = seed + PRIME32_1 + PRIME32_2;
                        uint v2 = seed + PRIME32_2;
                        uint v3 = seed + 0;
                        uint v4 = seed - PRIME32_1;

                        do
                        {
                            v1 += *((uint*)buffer) * PRIME32_2;
                            buffer += sizeof(uint);
                            v2 += *((uint*)buffer) * PRIME32_2;
                            buffer += sizeof(uint);
                            v3 += *((uint*)buffer) * PRIME32_2;
                            buffer += sizeof(uint);
                            v4 += *((uint*)buffer) * PRIME32_2;
                            buffer += sizeof(uint);

                            v1 = RotateLeft32(v1, 13);
                            v2 = RotateLeft32(v2, 13);
                            v3 = RotateLeft32(v3, 13);
                            v4 = RotateLeft32(v4, 13);

                            v1 *= PRIME32_1;
                            v2 *= PRIME32_1;
                            v3 *= PRIME32_1;
                            v4 *= PRIME32_1;
                        }
                        while (buffer <= limit);

                        h32 = RotateLeft32(v1, 1) + RotateLeft32(v2, 7) + RotateLeft32(v3, 12) + RotateLeft32(v4, 18);
                    }
                    else
                    {
                        h32 = seed + PRIME32_5;
                    }

                    h32 += (uint)len;


                    while (buffer + 4 <= bEnd)
                    {
                        h32 += *((uint*)buffer) * PRIME32_3;
                        h32 = RotateLeft32(h32, 17) * PRIME32_4;
                        buffer += 4;
                    }

                    while (buffer < bEnd)
                    {
                        h32 += (uint)(*buffer) * PRIME32_5;
                        h32 = RotateLeft32(h32, 11) * PRIME32_1;
                        buffer++;
                    }

                    h32 ^= h32 >> 15;
                    h32 *= PRIME32_2;
                    h32 ^= h32 >> 13;
                    h32 *= PRIME32_3;
                    h32 ^= h32 >> 16;

                    return h32;
                }
            }

            public static unsafe uint Calculate(byte* buffer, int len, uint seed = 0)
            {
                return CalculateInline(buffer, len, seed);
            }

            public static uint Calculate(string value, Encoding encoder, uint seed = 0)
            {
                var buf = encoder.GetBytes(value);

                fixed (byte* buffer = buf)
                {
                    return CalculateInline(buffer, buf.Length, seed);
                }
            }
            public static uint CalculateRaw(string buf, uint seed = 0)
            {
                fixed (char* buffer = buf)
                {
                    return CalculateInline((byte*)buffer, buf.Length * sizeof(char), seed);
                }
            }

            public static uint Calculate(byte[] buf, int len = -1, uint seed = 0)
            {
                if (len == -1)
                    len = buf.Length;

                fixed (byte* buffer = buf)
                {
                    return CalculateInline(buffer, len, seed);
                }
            }

            private static uint PRIME32_1 = 2654435761U;
            private static uint PRIME32_2 = 2246822519U;
            private static uint PRIME32_3 = 3266489917U;
            private static uint PRIME32_4 = 668265263U;
            private static uint PRIME32_5 = 374761393U;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static uint RotateLeft32(uint value, int count)
            {
                return (value << count) | (value >> (32 - count));
            }
        }
    }
}
