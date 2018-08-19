This is an experiment for providing vector clocks with vectorization-aware optimizations. Some of the key assumptions where made here:

1. We optimize mostly for read speed.
2. In clustered environment we don't expect constant growth of nodes - the node set itself will grow at the beginning, but when compared to number of other operations, is should be relativelly rare.
3. Most of the time vector clocks we compare to each other are probably going to contain the same set of nodes (since replicas doesn't change so often - see: pt.2).

Most vector clocks are represented as maps or lists of key-value pairs. Here we represent them instead as separate arrays for sorted keys (node identifiers) and values. If two vectors have exactly matching keys (see pt.3), we can expect that the values of corresponding keys are located in exactly the same offsets in value their arrays. This means that for things like comparisons and merge operation, we can simply apply CPU vectorization to run the same operation over multiple values at the same CPU cycle.

### Initiali benchmarks

This is a comparison with an existing VectorClock implementation from Akka.NET.

``` ini

BenchmarkDotNet=v0.11.0, OS=Windows 10.0.17134.228 (1803/April2018Update/Redstone4)
Intel Core i7-7660U CPU 2.50GHz (Kaby Lake), 1 CPU, 4 logical and 2 physical cores
.NET Core SDK=2.1.400
  [Host]     : .NET Core 2.1.2 (CoreCLR 4.6.26628.05, CoreFX 4.6.26629.01), 64bit RyuJIT
  DefaultJob : .NET Core 2.1.2 (CoreCLR 4.6.26628.05, CoreFX 4.6.26629.01), 64bit RyuJIT


```
|              Method | Count |         Mean |        Error |       StdDev |       Median | Scaled |   Gen 0 | Allocated |
|-------------------- |------ |-------------:|-------------:|-------------:|-------------:|-------:|--------:|----------:|
|  **NaiveClockEquality** |     **3** |  **10,386.3 ns** |   **783.441 ns** |  **2,309.99 ns** |  **10,844.5 ns** |   **1.00** |  **0.7935** |    **1688 B** |
| VectorClockEquality |     3 |     120.6 ns |     4.184 ns |     12.14 ns |     124.6 ns |   0.01 |       - |       0 B |
|                     |       |              |              |              |              |        |         |           |
|  **NaiveClockEquality** |    **12** |  **32,817.0 ns** | **2,509.154 ns** |  **7,398.30 ns** |  **34,229.5 ns** |  **1.000** |  **2.1362** |    **4544 B** |
| VectorClockEquality |    12 |     252.1 ns |    19.522 ns |     57.56 ns |     271.7 ns |  0.008 |       - |       0 B |
|                     |       |              |              |              |              |        |         |           |
|  **NaiveClockEquality** |   **100** | **316,258.1 ns** | **8,265.195 ns** | **24,370.11 ns** | **316,500.8 ns** |  **1.000** | **15.1367** |   **32049 B** |
| VectorClockEquality |   100 |   1,729.2 ns |    40.046 ns |    118.08 ns |   1,738.8 ns |  0.006 |       - |       0 B |

Keep in mind, that this is the **golden case** for new implementation (we compare structural equality of 2 exactly same vectors, which however don't have referential equality on their content - this case in optimized in Akka).