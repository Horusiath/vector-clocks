using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;

namespace VectorClocks.Benchmarks
{
    [Config(typeof(BenchmarkConfig))]
    public class AxiomsBenchmarks
    {
        [Params(3, 12, 100)] public int Length;

        private string[] keys;
        private int[] hashes;
        private int idx;

        [GlobalSetup]
        public void Setup()
        {
            var rng = RandomNumberGenerator.Create();
            byte[] buffer = new byte[4];
            rng.GetBytes(buffer);
            var random = new Random(BitConverter.ToInt32(buffer, 0));
            
            var sorted = new SortedSet<string>();
            var sortedH = new SortedSet<int>();
            for (int i = 0; i < Length; i++)
            {
                var n = random.Next().ToString();
                sorted.Add(n);
                sortedH.Add(n.GetHashCode());
            }

            keys = sorted.ToArray();
            hashes = sortedH.ToArray();
            idx = random.Next(Length);
        }

        [Benchmark]
        public int BinarySearchString()
        {
            return Array.BinarySearch(keys, idx);
        }

        [Benchmark]
        public int BinarySearchHashCode()
        {
            ReadOnlySpan<int> span = hashes;
            return span.BinarySearch(idx);
        }
    }
}