﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Dictionary
{
    public unsafe class FastDictionary<TKey, TValue> : IEnumerable<KeyValuePair<TKey,TValue>>
    {
        const int InvalidNodePosition = -1;

        public const uint kUnusedHash = 0xFFFFFFFF;
        public const uint kDeletedHash = 0xFFFFFFFE;

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

        private uint[] _hashes;
        private TKey[] _keys;
        private TValue[] _values;

        private int _capacity;

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

        public FastDictionary(int initialBucketCount = kInitialCapacity)
        {
            Contract.Ensures(_capacity >= initialBucketCount);

            this.comparer = comparer ?? EqualityComparer<TKey>.Default;

            int newCapacity = NextPowerOf2(initialBucketCount >= kMinBuckets ? initialBucketCount : kMinBuckets);

            // Initialization
            _keys = new TKey[newCapacity];
            _values = new TValue[newCapacity];
            _hashes = new uint[newCapacity];
            for (int i = 0; i < newCapacity; i++)
                _hashes[i] = kUnusedHash;

            _capacity = newCapacity;

            _numberOfUsed = _size;
            _numberOfDeleted = 0;

            _nextGrowthThreshold = _capacity * 4 / tLoadFactor4;
        }

        public void Add(TKey key, TValue value)
        {
            if (key == null)
                throw new ArgumentNullException("key");
            Contract.EndContractBlock();

            ResizeIfNeeded();

            int hash = GetInternalHashCode(key);
            int bucket = hash % _capacity;

            if (TryAdd(bucket, (uint)hash, key, value))
                return;

            Contract.Assert(_numberOfUsed < _capacity);

            int numProbes = 0;
            bool couldInsert = false;
            while (!couldInsert)
            {
                numProbes++;

                bucket = (bucket + numProbes) % _capacity;

                couldInsert = TryAdd(bucket, (uint)hash, key, value);
            }
        }

        public bool Remove(TKey key)
        {
            if (key == null)
                throw new ArgumentNullException("key");
            Contract.EndContractBlock();

            int bucket = Lookup(key);
            if (bucket == InvalidNodePosition)
                return false;

            SetDeleted(bucket);

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetDeleted(int node)
        {
            Contract.Ensures(_hashes[node] == kDeletedHash);
            Contract.Ensures(_size <= Contract.OldValue<int>(_size));
            Contract.Ensures(_numberOfDeleted >= Contract.OldValue<int>(_numberOfDeleted));

            if (_hashes[node] != kDeletedHash)
            {
                SetNode(node, kDeletedHash, default(TKey), default(TValue));

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

            var keys = new TKey[newCapacity];
            var values = new TValue[newCapacity];
            var hashes = new uint[newCapacity];
            for (int i = 0; i < newCapacity; i++)
                hashes[i] = kUnusedHash;

            Rehash(keys, values, hashes);

            _capacity = newCapacity;
            _hashes = hashes;
            _keys = keys;
            _values = values;
            
            _numberOfUsed = _size;
            _numberOfDeleted = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetNode(int node, uint hash, TKey key, TValue value)
        {
            _hashes[node] = hash;
            _keys[node] = key;
            _values[node] = value;
        }

        public TValue this[TKey key]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Contract.Requires(key != null);

                int hash = GetInternalHashCode(key);
                int bucket = hash % _capacity;

                var hashes = _hashes;
                var keys = _keys;
                var values = _values;

                if (CompareKey(hashes[bucket], keys[bucket], (uint)hash, key))
                    return values[bucket];

                int numProbes = 1; // how many times we've probed
                Contract.Assert(_numberOfUsed < _capacity);

                bool canContinue = true;
                while (canContinue)
                {
                    bucket = (bucket + numProbes) % _capacity;

                    if (CompareKey(hashes[bucket], keys[bucket], (uint)hash, key, ref canContinue))
                        return values[bucket];

                    numProbes++;
                }

                throw new KeyNotFoundException();
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                Contract.Requires(key != null);                

                ResizeIfNeeded();

                int hash = GetInternalHashCode(key);
                int bucket = hash % _capacity;

                if (TryInsert(bucket, (uint)hash, key, value))
                    return;

                Contract.Assert(_numberOfUsed < _capacity);

                int numProbes = 1;
                bool couldInsert = false;
                while (!couldInsert)
                {
                    bucket = (bucket + numProbes) % _capacity;

                    couldInsert = TryInsert(bucket, (uint)hash, key, value);

                    numProbes++;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryAdd(int node, uint hash, TKey key, TValue value)
        {
            var nHash = _hashes[node];
            if (nHash == kDeletedHash)
            {
                SetNode(node, hash, key, value);

                _numberOfDeleted--;
                _size++;

                return true;
            }
            else if (nHash == kUnusedHash)
            {
                SetNode(node, hash, key, value);

                _numberOfUsed++;
                _size++;

                return true;
            }
            else if (CompareKey(nHash, _keys[node], hash, key))
            {
                throw new ArgumentException("Cannot add duplicated key.", "key");
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryInsert(int node, uint hash, TKey key, TValue value)
        {
            uint nHash = _hashes[node];
            if (nHash == kUnusedHash)
            {
                _numberOfUsed++;
                _size++;

                goto SET;
            }
            else if (nHash == kDeletedHash)
            {
                _numberOfDeleted--;
                _size++;

                goto SET;
            }
            else if (CompareKey(nHash, _keys[node], hash, key ))
            {
                goto SET;
            }
            return false;

            SET:
            SetNode(node, hash, key, value);
            return true;
        }



        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateNode(int node, uint hash, TKey key, TValue value)
        {
            uint nHash = _hashes[node];
            if (nHash == kDeletedHash)
            {
                _numberOfDeleted--;
                _size++;
            }
            else if (nHash == kUnusedHash)
            {
                _numberOfUsed++;
                _size++;
            }

            SetNode(node, hash, key, value);
        }

        public void Clear()
        {
            TKey defaultKey = default(TKey);
            TValue defaultValue = default(TValue);

            for (int i = 0; i < _capacity; i++)
            {
                SetNode(i, kUnusedHash, defaultKey, defaultValue);
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

            var keys = new TKey[newCapacity];
            var values = new TValue[newCapacity];
            var hashes = new uint[newCapacity];
            for (int i = 0; i < newCapacity; i++)
                hashes[i] = kUnusedHash;

            Rehash(keys, values, hashes);

            _capacity = newCapacity;

            _keys = keys;
            _values = values;
            _hashes = hashes;            

            _numberOfUsed = _size;
            _numberOfDeleted = 0;

            _nextGrowthThreshold = _capacity * 4 / tLoadFactor4;
        }

        public bool TryLookup( TKey key, out TValue value )
        {
            int hash = GetInternalHashCode(key);
            int bucket = hash % _capacity;

            var hashes = _hashes;
            var keys = _keys;

            if (CompareKey(hashes[bucket], keys[bucket], (uint)hash, key))
            {
                value = _values[bucket];
                return true;
            }


            uint numProbes = 0; // how many times we've probed
            Contract.Assert(_numberOfUsed < _capacity);

            bool canContinue = true;
            while (canContinue)
            {
                numProbes++;
                bucket = (int)((bucket + numProbes) % _capacity);

                if (CompareKey(hashes[bucket], keys[bucket], (uint)hash, key, ref canContinue))
                {
                    value = _values[bucket];
                    return true;
                }
            }

            value = default(TValue);

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CompareKey(uint nHash, TKey nKey, uint hash, TKey key, ref bool probeAgain)
        {
            probeAgain = nHash != kUnusedHash;
            if (nHash != hash)
                return false;

            if (comparer.Equals(nKey, key))
                return true;

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
            int hash = comparer.GetHashCode(key) & 0x7FFFFFFF;
            int bucket = hash % _capacity;

            var hashes = _hashes;
            var keys = _keys;

            if (CompareKey(hashes[bucket], keys[bucket], (uint)hash, key))
                return bucket;


            uint numProbes = 0; // how many times we've probed
            Contract.Assert(_numberOfUsed < _capacity);

            bool canContinue = true;
            while (canContinue)
            {
                numProbes++;
                bucket = (int)((bucket + numProbes) % _capacity);

                if (CompareKey(hashes[bucket], keys[bucket], (uint)hash, key, ref canContinue))
                    return bucket;
            }

            return InvalidNodePosition;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetInternalHashCode(TKey key)
        {
            return comparer.GetHashCode(key) & 0x7FFFFFFF;
        }

        private void Rehash(TKey[] keys, TValue[] values, uint[] hashes)
        {
            uint capacity = (uint) keys.Length;

            for (int it = 0; it < _hashes.Length; it++)
            {
                uint hash = _hashes[it];
                uint bucket = hash % capacity;

                uint numProbes = 0;
                while (! (hashes[bucket] == kUnusedHash) )
                {
                    numProbes++;
                    bucket = (bucket + numProbes) % capacity;
                }

                hashes[bucket] = _hashes[it];
                keys[bucket] = _keys[it];
                values[bucket] = _values[it];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        IEnumerator IEnumerable.GetEnumerator()
        {
            return new Enumerator(this);
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            return new Enumerator(this);
        }      


        [Serializable]
        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
        {
            private FastDictionary<TKey, TValue> dictionary;
            private int index;
            private KeyValuePair<TKey, TValue> current;

            internal const int DictEntry = 1;
            internal const int KeyValuePair = 2;

            internal Enumerator(FastDictionary<TKey, TValue> dictionary)
            {
                this.dictionary = dictionary;
                index = 0;
                current = new KeyValuePair<TKey, TValue>();
            }

            public bool MoveNext()
            {
                var dict = dictionary;

                // Use unsigned comparison since we set index to dictionary.count+1 when the enumeration ends.
                // dictionary.count+1 could be negative if dictionary.count is Int32.MaxValue
                while (index < dict._capacity)
                {
                    if (dict._hashes[index] < kDeletedHash )
                    {
                        current = new KeyValuePair<TKey, TValue>(dict._keys[index], dict._values[index]);
                        index++;
                        return true;
                    }
                    index++;
                }

                index = dictionary._capacity + 1;
                current = new KeyValuePair<TKey, TValue>();
                return false;
            }

            public KeyValuePair<TKey, TValue> Current
            {
                get { return current; }
            }

            public void Dispose()
            {
            }

            object IEnumerator.Current
            {
                get
                {
                    if (index == 0 || (index == dictionary._capacity + 1))
                        throw new InvalidOperationException("Can't happen.");

                    return new KeyValuePair<TKey, TValue>(current.Key, current.Value);
                }
            }

            void IEnumerator.Reset()
            {
                index = 0;
                current = new KeyValuePair<TKey, TValue>();
            }
        }


    }
}
