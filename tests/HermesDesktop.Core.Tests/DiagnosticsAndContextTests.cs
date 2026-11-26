using HermesDesktop.Core.Diagnostics;
using HermesDesktop.Core.Rpc;
using Xunit;

namespace HermesDesktop.Core.Tests;

public class DiagnosticsAndContextTests
{
    [Fact]
    public void NormalizeContextPercent_HandlesFractionPercentageAndComputedRatio()
    {
        Assert.Equal(25.0, StreamEventParser.NormalizeContextPercent(0.25, 0, 0), 2);
        Assert.Equal(42.5, StreamEventParser.NormalizeContextPercent(42.5, 0, 0), 2);
        Assert.Equal(50.0, StreamEventParser.NormalizeContextPercent(0, 64_000, 128_000), 2);
        Assert.Equal(100.0, StreamEventParser.NormalizeContextPercent(150.0, 0, 0), 2);
    }

    [Fact]
    public void AppDiagnostics_WritesDailyAndCrashLogsInsideLogsDirectory()
    {
        AppDiagnostics.Log("TEST", "Unit test log entry");
        Assert.True(Directory.Exists(AppDiagnostics.LogsDirectory));
        Assert.True(File.Exists(AppDiagnostics.LogFilePath));
        Assert.Contains("Unit test log entry", File.ReadAllText(AppDiagnostics.LogFilePath));
    }
}
