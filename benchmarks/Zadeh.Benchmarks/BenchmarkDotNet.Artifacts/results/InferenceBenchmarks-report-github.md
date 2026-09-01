```

BenchmarkDotNet v0.13.12, macOS 26.5 (25F71) [Darwin 25.5.0]
Apple M4, 1 CPU, 10 logical and 10 physical cores
.NET SDK 8.0.124
  [Host]   : .NET 8.0.24 (8.0.2426.7010), Arm64 RyuJIT AdvSIMD
  ShortRun : .NET 8.0.24 (8.0.2426.7010), Arm64 RyuJIT AdvSIMD

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                  | Mean      | Error     | StdDev    | Gen0   | Gen1   | Allocated |
|------------------------ |----------:|----------:|----------:|-------:|-------:|----------:|
| Small_1Input_3Rules     |  2.104 μs | 0.4595 μs | 0.0252 μs | 0.1755 |      - |   1.45 KB |
| Medium_2Inputs_5Rules   |  2.337 μs | 0.4446 μs | 0.0244 μs | 0.2022 |      - |   1.66 KB |
| Large_3Inputs_12Rules   |  4.039 μs | 0.6189 μs | 0.0339 μs | 0.2289 |      - |   1.88 KB |
| Detailed_2Inputs_5Rules |  2.198 μs | 0.1691 μs | 0.0093 μs | 0.1678 |      - |   1.38 KB |
| Trace_2Inputs_5Rules    |  2.250 μs | 0.2759 μs | 0.0151 μs | 0.2556 |      - |    2.1 KB |
| FromJson_2Inputs_5Rules | 13.638 μs | 1.9682 μs | 0.1079 μs | 3.7384 | 0.2747 |  30.64 KB |
