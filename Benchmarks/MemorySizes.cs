﻿using BenchmarkDotNet.Attributes;
using System.Collections.Immutable;
using Xamarin.Apex.Runtime;

namespace Benchmarks
{
    public class MemorySizes
    {
        private Memory _memory;
        private int[] _intArray;
        private ImmutableSortedDictionary<int, int> _isd;

        [Params(100, 10000)]
        public int Count;

        [GlobalSetup]
        public void Init()
        {
            _memory = new Memory(Memory.Mode.Graph);
            _intArray = new int[Count];
            _isd = ImmutableSortedDictionary<int, int>.Empty;
            for(int i=0;i<Count;++i)
            {
                _isd = _isd.Add(i, i);
            }
        }

        //[Benchmark]
        public void ArrayInt()
        {
            _memory.SizeOf(_intArray);
        }

        [Benchmark]
        public void ImmutableSortedDictionary()
        {
            _memory.SizeOf(_isd);
        }
    }
}
