using System;
using System.Reflection;
using BenchmarkDotNet.Running;

namespace VectorClocks.Benchmarks
{
    class Program
    {
        static void Main(string[] args)
        {
            BenchmarkSwitcher.FromAssemblies(new[] { Assembly.GetExecutingAssembly() }).Run(args);
        }
    }
}
