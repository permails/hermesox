using HermesDesktop.Core.Localization;
using HermesDesktop.Core.Models;
using Xunit;

namespace HermesDesktop.Core.Tests;

public sealed class TranscriptRunHelperTests
{
    [Fact]
    public void IsPreToolNarration_ClassifiesShortPreToolLeadInAsProcess()
    {
        Assert.True(TranscriptRunHelper.IsPreToolNarration(
            "怪事：入库说 1 份，检查说 0 docs。直接查库诊断：",
            hasAttachments: false,
            isStreaming: false,
            followedByStepInTurn: true));

        Assert.False(TranscriptRunHelper.IsPreToolNarration(
            "怪事：入库说 1 份，检查说 0 docs。",
            hasAttachments: false,
            isStreaming: false,
            followedByStepInTurn: false));

        Assert.False(TranscriptRunHelper.IsPreToolNarration(
            "🎉 全部搞定了！\n\n- 第一点\n- 第二点",
            hasAttachments: false,
            isStreaming: false,
            followedByStepInTurn: true));
    }

    [Fact]
    public void ExtractBatchToolCalls_UnwrapsToolCallJson()
    {
        const string json = """
        {
          "calls": [
            {
              "name": "conduit_show",
              "arguments": {
                "title": "6册英语词表勘验 全部完成",
                "cards": []
              }
            }
          ]
        }
        """;

        var (names, arg) = TranscriptRunHelper.ExtractBatchToolCalls(json);
        Assert.Single(names);
        Assert.Equal("conduit_show", names[0]);
        Assert.Equal("6册英语词表勘验 全部完成", arg);
    }

    [Fact]
    public void FormatStepRunSummary_RespectsLocaleAndCap()
    {
        var zh = TranscriptRunHelper.FormatStepRunSummary(
            11,
            ["执行脚本", "思考", "处理文件", "读写记忆", "展示看板"],
            isLive: false,
            isChineseOverride: true);
        Assert.Contains("已完成 11 个操作步骤", zh);
        Assert.Contains("执行脚本、思考、处理文件、读写记忆 等", zh);

        var en = TranscriptRunHelper.FormatStepRunSummary(
            4,
            ["Ran script", "Thought"],
            isLive: true,
            isChineseOverride: false);
        Assert.Equal("Working · 4 steps · Ran script, Thought", en);
    }
}
