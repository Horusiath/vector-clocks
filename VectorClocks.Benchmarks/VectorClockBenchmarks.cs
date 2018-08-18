using System;
using System.Collections.Immutable;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;

namespace VectorClocks.Benchmarks
{
    using NaiveVectorClock = Akka.Cluster.VectorClock;
    using NodeId = Int64;

    [Config(typeof(BenchmarkConfig))]
    public class VectorClockBenchmarks
    {
        private NaiveVectorClock naiveA;
        private NaiveVectorClock naiveB;

        private VectorClock clockA;
        private VectorClock clockB;

        [Params(3, 12, 100)]
        public int Count;

        [GlobalSetup]
        public void Setup()
        {
            var rng = RandomNumberGenerator.Create();
            byte[] buffer = new byte[4];
            rng.GetBytes(buffer);
            var random = new Random(BitConverter.ToInt32(buffer, 0));

            var a = ImmutableSortedDictionary<NaiveVectorClock.Node, long>.Empty.ToBuilder();
            var b = ImmutableSortedDictionary<NaiveVectorClock.Node, long>.Empty.ToBuilder();
            var c = ImmutableSortedDictionary<string, long>.Empty.ToBuilder();
            var d = ImmutableSortedDictionary<string, long>.Empty.ToBuilder();
            for (int i = 0; i < Count; i++)
            {
                var node = random.Next();
                var value = random.Next();

                a.Add(NaiveVectorClock.Node.Create(node.ToString()), value);
                b.Add(NaiveVectorClock.Node.Create(node.ToString()), value);
                c.Add(node.ToString(), value);
                d.Add(node.ToString(), value);
            }

            naiveA = NaiveVectorClock.Create(a.ToImmutable());
            naiveB = NaiveVectorClock.Create(b.ToImmutable());

            clockA = new VectorClock(c.ToImmutable());
            clockB = new VectorClock(d.ToImmutable());
        }

        [Benchmark(Baseline = true)]
        public bool NaiveClockEquality()
        {
            return naiveA.Equals(naiveB);
        }

        [Benchmark]
        public bool VectorClockEquality()
        {
            return clockA.Equals(clockB);
        }
    }
}