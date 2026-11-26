namespace HermesDesktop.Core.Sessions;

public enum OutboundSendAction
{
    Ignore,
    SendCurrentInputImmediately,
    BufferCurrentInput,
    ForceSendNextBuffered,
    SendNextBufferedImmediately
}

/// <summary>
/// Resolves how Composer submit acts depending on whether the active session is busy/compressing,
/// whether the composer has new content, and whether local buffered messages are already waiting.
/// </summary>
public static class OutboundMessageBufferHelper
{
    public static OutboundSendAction ResolveSendAction(
        bool isSessionBusy,
        bool hasComposerPayload,
        bool hasBufferedMessages)
    {
        if (hasComposerPayload)
        {
            return isSessionBusy
                ? OutboundSendAction.BufferCurrentInput
                : OutboundSendAction.SendCurrentInputImmediately;
        }

        if (hasBufferedMessages)
        {
            return isSessionBusy
                ? OutboundSendAction.ForceSendNextBuffered
                : OutboundSendAction.SendNextBufferedImmediately;
        }

        return OutboundSendAction.Ignore;
    }
}
