using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace BenchmarkSuite1;

internal sealed class Program
{
    private static void Main(string[] args)
    {
        var config = DefaultConfig.Instance.WithArtifactsPath(Path.Combine(Path.GetTempPath(), "BoxcarsBenchmarkArtifacts"));
        _ = BenchmarkRunner.Run(typeof(Program).Assembly, config);
    }
}
