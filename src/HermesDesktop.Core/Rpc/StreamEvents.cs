using HermesDesktop.Core.Models;

namespace HermesDesktop.Core.Rpc;

public abstract record StreamEvent(string SessionId);

public sealed record GatewayReadyEvent(
    bool SessionAuthority,
    bool SendsChangeEvents) : StreamEvent("");

public sealed record SessionsChangedEvent() : StreamEvent("");

public sealed record MessageStartEvent(string SessionId) : StreamEvent(SessionId);

public sealed record MessageDeltaEvent(string SessionId, string Text) : StreamEvent(SessionId);

public sealed record ReasoningDeltaEvent(string SessionId, string Text) : StreamEvent(SessionId);

public sealed record MessageCompleteEvent(
    string SessionId,
    string? MessageId,
    string? Content,
    string? Reasoning,
    bool AlreadyShown) : StreamEvent(SessionId);

public sealed record MessageInterimEvent(
    string SessionId,
    string Text,
    bool AlreadyStreamed) : StreamEvent(SessionId);

public sealed record MessageErrorEvent(string SessionId, string Message) : StreamEvent(SessionId);

public sealed record MessageInterruptedEvent(string SessionId) : StreamEvent(SessionId);

public sealed record SessionBusyEvent(string SessionId, bool Busy) : StreamEvent(SessionId);

public sealed record SessionTitleEvent(
    string RuntimeSessionId,
    string StoredSessionId,
    string Title) : StreamEvent(RuntimeSessionId);

public sealed record StatusUpdateEvent(
    string SessionId,
    string Kind,
    string? Text) : StreamEvent(SessionId);

public sealed record ToolStartEvent(
    string SessionId,
    string ToolName,
    string? ToolInput,
    string? ToolId) : StreamEvent(SessionId);

public sealed record ToolCompleteEvent(
    string SessionId,
    string ToolName,
    string? ToolOutput,
    string? ToolId) : StreamEvent(SessionId);

public sealed record ClarifyRequestEvent(
    string SessionId,
    ClarifyActivity Activity) : StreamEvent(SessionId);

public sealed record ClarifyExpireEvent(
    string SessionId,
    string RequestId,
    string? Reason) : StreamEvent(SessionId);

public sealed record ApprovalRequestEvent(
    string SessionId,
    ApprovalActivity Activity) : StreamEvent(SessionId);

public sealed record ApprovalWithdrawnEvent(
    string SessionId,
    string RequestId,
    string Reason) : StreamEvent(SessionId);

public sealed record InputPromptExpireEvent(
    string SessionId,
    string RequestId,
    string Reason) : StreamEvent(SessionId);

public sealed record ContextUpdateEvent(
    string SessionId,
    double Percent,
    int Used,
    int Max) : StreamEvent(SessionId);

public sealed record CwdUpdateEvent(string SessionId, string Cwd) : StreamEvent(SessionId);

public sealed record ModelUpdateEvent(string SessionId, string Model, string Provider) : StreamEvent(SessionId);

public sealed record SideAgentCompleteEvent(
    string SessionId,
    bool IsBtw,
    string TaskId,
    string Text,
    string? Question) : StreamEvent(SessionId);

public sealed record DelegateAgentEvent(
    string SessionId,
    DelegateAgentActivity Activity) : StreamEvent(SessionId);

public sealed record UnparsedStreamEvent(string SessionId, string EventType) : StreamEvent(SessionId);
