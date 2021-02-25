``` ini

BenchmarkDotNet=v0.12.1, OS=Windows 10.0.19042
AMD Ryzen 9 3900X, 1 CPU, 24 logical and 12 physical cores
.NET Core SDK=5.0.200-preview.20614.14
  [Host]     : .NET Core 5.0.3 (CoreCLR 5.0.321.7212, CoreFX 5.0.321.7212), X64 RyuJIT
  Job-TZBFTD : .NET Core 5.0.3 (CoreCLR 5.0.321.7212, CoreFX 5.0.321.7212), X64 RyuJIT

Runtime=.NET Core 5.0  Server=False  

```
|                    Method | Count |       Mean |     Error |    StdDev | Gen 0 | Gen 1 | Gen 2 | Allocated |
|-------------------------- |------ |-----------:|----------:|----------:|------:|------:|------:|----------:|
| **ImmutableSortedDictionary** |   **100** |   **3.509 μs** | **0.0220 μs** | **0.0205 μs** |     **-** |     **-** |     **-** |         **-** |
| **ImmutableSortedDictionary** | **10000** | **370.203 μs** | **1.0414 μs** | **0.8696 μs** |     **-** |     **-** |     **-** |         **-** |
