using System.Text.Json;
using HermesDesktop.Core.Rpc;
using Xunit;

namespace HermesDesktop.Core.Tests;

public class StreamEventParserTests
{
    [Fact]
    public void Parse_MessageDeltaAndReasoningDelta()
    {
        using var deltaDoc = JsonDocument.Parse("""
            {"type":"message.delta","session_id":"s1","payload":{"text":"Hello "}}
            """);
        var delta = Assert.IsType<MessageDeltaEvent>(StreamEventParser.Parse(deltaDoc.RootElement));
        Assert.Equal("s1", delta.SessionId);
        Assert.Equal("Hello ", delta.Text);

        using var reasonDoc = JsonDocument.Parse("""
            {"type":"reasoning.delta","session_id":"s1","payload":{"reasoning_content":"Thinking..."}}
            """);
        var reason = Assert.IsType<ReasoningDeltaEvent>(StreamEventParser.Parse(reasonDoc.RootElement));
        Assert.Equal("Thinking...", reason.Text);
    }

    [Fact]
    public void Parse_ToolStartAndToolComplete()
    {
        using var startDoc = JsonDocument.Parse("""
            {"type":"tool.start","session_id":"s1","payload":{"tool_call_id":"tc-1","name":"shell","args_text":"ls -la"}}
            """);
        var start = Assert.IsType<ToolStartEvent>(StreamEventParser.Parse(startDoc.RootElement));
        Assert.Equal("tc-1", start.ToolId);
        Assert.Equal("shell", start.ToolName);
        Assert.Equal("ls -la", start.ToolInput);

        using var endDoc = JsonDocument.Parse("""
            {"type":"tool.complete","session_id":"s1","payload":{"call_id":"tc-1","name":"shell","output":"total 0"}}
            """);
        var end = Assert.IsType<ToolCompleteEvent>(StreamEventParser.Parse(endDoc.RootElement));
        Assert.Equal("tc-1", end.ToolId);
        Assert.Equal("total 0", end.ToolOutput);
    }

    [Fact]
    public void Parse_ApprovalAndClarify()
    {
        using var appDoc = JsonDocument.Parse("""
            {
              "type":"approval.request",
              "session_id":"s1",
              "payload":{
                "request_id":"apr-1",
                "command":"rm -rf /tmp/foo",
                "risk_level":"high",
                "patterns":["recursive delete"]
              }
            }
            """);
        var app = Assert.IsType<ApprovalRequestEvent>(StreamEventParser.Parse(appDoc.RootElement));
        Assert.Equal("apr-1", app.Activity.RequestId);
        Assert.Equal("rm -rf /tmp/foo", app.Activity.Command);
        Assert.Single(app.Activity.Patterns);

        using var clrDoc = JsonDocument.Parse("""
            {
              "type":"clarify.request",
              "session_id":"s1",
              "payload":{
                "request_id":"clr-1",
                "question":"Deploy to staging?",
                "choices":[{"label":"Yes","value":"yes"},{"label":"No","value":"no"}]
              }
            }
            """);
        var clr = Assert.IsType<ClarifyRequestEvent>(StreamEventParser.Parse(clrDoc.RootElement));
        Assert.Equal("clr-1", clr.Activity.RequestId);
        Assert.Equal(2, clr.Activity.Choices.Count);
    }

    [Fact]
    public void Parse_SessionUsageComputesPercentFallback()
    {
        using var doc = JsonDocument.Parse("""
            {
              "type":"session.usage",
              "session_id":"s1",
              "payload":{"usage":{"context_used":25000,"context_max":100000}}
            }
            """);
        var ctx = Assert.IsType<ContextUpdateEvent>(StreamEventParser.Parse(doc.RootElement));
        Assert.Equal(25.0, ctx.Percent, 2);
        Assert.Equal(25000, ctx.Used);
        Assert.Equal(100000, ctx.Max);
    }
}
