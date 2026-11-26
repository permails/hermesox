using HermesDesktop.Core.Sessions;
using Xunit;

namespace HermesDesktop.Core.Tests;

public class OutboundMessageBufferTests
{
    [Fact]
    public void IdleSession_WithComposerPayload_SendsImmediately()
    {
        var action = OutboundMessageBufferHelper.ResolveSendAction(
            isSessionBusy: false,
            hasComposerPayload: true,
            hasBufferedMessages: false);

        Assert.Equal(OutboundSendAction.SendCurrentInputImmediately, action);
    }

    [Fact]
    public void BusySession_FirstSendWithPayload_BuffersLocallyWithoutSending()
    {
        var action = OutboundMessageBufferHelper.ResolveSendAction(
            isSessionBusy: true,
            hasComposerPayload: true,
            hasBufferedMessages: false);

        Assert.Equal(OutboundSendAction.BufferCurrentInput, action);
    }

    [Fact]
    public void BusySession_SecondSendClickWithEmptyComposer_ForceSendsBuffered()
    {
        var action = OutboundMessageBufferHelper.ResolveSendAction(
            isSessionBusy: true,
            hasComposerPayload: false,
            hasBufferedMessages: true);

        Assert.Equal(OutboundSendAction.ForceSendNextBuffered, action);
    }

    [Fact]
    public void BusySession_WithAnotherTypedPayload_BuffersAdditionalMessage()
    {
        var action = OutboundMessageBufferHelper.ResolveSendAction(
            isSessionBusy: true,
            hasComposerPayload: true,
            hasBufferedMessages: true);

        Assert.Equal(OutboundSendAction.BufferCurrentInput, action);
    }

    [Fact]
    public void IdleSession_WithEmptyComposerAndBufferedMessages_FlushesNextBuffered()
    {
        var action = OutboundMessageBufferHelper.ResolveSendAction(
            isSessionBusy: false,
            hasComposerPayload: false,
            hasBufferedMessages: true);

        Assert.Equal(OutboundSendAction.SendNextBufferedImmediately, action);
    }

    [Fact]
    public void EmptyComposerAndNoBuffer_IgnoresSend()
    {
        Assert.Equal(
            OutboundSendAction.Ignore,
            OutboundMessageBufferHelper.ResolveSendAction(isSessionBusy: true, hasComposerPayload: false, hasBufferedMessages: false));
        Assert.Equal(
            OutboundSendAction.Ignore,
            OutboundMessageBufferHelper.ResolveSendAction(isSessionBusy: false, hasComposerPayload: false, hasBufferedMessages: false));
    }
}
