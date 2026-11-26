using System.Text.Json.Serialization;

namespace HermesDesktop.Core.Models;

public enum MessageRole
{
    User,
    Assistant,
    Reasoning,
    System,
    Partial,
    Tool,
    Clarify,
    Approval,
    InputPrompt,
    Connection
}

public enum SessionSource
{
    Chat,
    Voice,
    VoiceJob,
    Discord,
    Telegram,
    Slack,
    WhatsApp,
    Signal,
    IMessage,
    Matrix,
    Mattermost,
    Teams,
    GoogleChat,
    Email,
    Sms,
    Irc,
    Line,
    Simplex,
    WeChat,
    WeCom,
    Feishu,
    DingTalk,
    QQ,
    Yuanbao,
    Buzz,
    Raft,
    Ntfy,
    A2A,
    Api,
    Webhook,
    Cron,
    Other
}

public static class SessionSourceExtensions
{
    private static readonly Dictionary<string, SessionSource> WireMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chat"] = SessionSource.Chat,
        ["voice"] = SessionSource.Voice,
        ["voice_job"] = SessionSource.VoiceJob,
        ["discord"] = SessionSource.Discord,
        ["telegram"] = SessionSource.Telegram,
        ["slack"] = SessionSource.Slack,
        ["whatsapp"] = SessionSource.WhatsApp,
        ["signal"] = SessionSource.Signal,
        ["imessage"] = SessionSource.IMessage,
        ["matrix"] = SessionSource.Matrix,
        ["mattermost"] = SessionSource.Mattermost,
        ["teams"] = SessionSource.Teams,
        ["google_chat"] = SessionSource.GoogleChat,
        ["email"] = SessionSource.Email,
        ["sms"] = SessionSource.Sms,
        ["irc"] = SessionSource.Irc,
        ["line"] = SessionSource.Line,
        ["simplex"] = SessionSource.Simplex,
        ["weixin"] = SessionSource.WeChat,
        ["wecom"] = SessionSource.WeCom,
        ["feishu"] = SessionSource.Feishu,
        ["dingtalk"] = SessionSource.DingTalk,
        ["qqbot"] = SessionSource.QQ,
        ["yuanbao"] = SessionSource.Yuanbao,
        ["buzz"] = SessionSource.Buzz,
        ["raft"] = SessionSource.Raft,
        ["ntfy"] = SessionSource.Ntfy,
        ["a2a"] = SessionSource.A2A,
        ["api"] = SessionSource.Api,
        ["webhook"] = SessionSource.Webhook,
        ["cron"] = SessionSource.Cron,
        ["other"] = SessionSource.Other
    };

    public const string ChatListExcludedSources = "cron,kanban,tool,oneshot";

    public static SessionSource Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return SessionSource.Chat;
        return WireMap.TryGetValue(raw.Trim(), out var value) ? value : SessionSource.Other;
    }

    public static string ToLabel(this SessionSource source) => source switch
    {
        SessionSource.Chat => "Chat",
        SessionSource.Voice => "Voice",
        SessionSource.VoiceJob => "Voice Jobs",
        SessionSource.GoogleChat => "Google Chat",
        SessionSource.Teams => "Microsoft Teams",
        SessionSource.WeChat => "WeChat",
        SessionSource.IMessage => "iMessage",
        SessionSource.Sms => "SMS",
        SessionSource.Irc => "IRC",
        SessionSource.Line => "LINE",
        SessionSource.Api => "API",
        SessionSource.A2A => "A2A",
        _ => source.ToString()
    };
}

public enum AttachmentKind
{
    Image,
    Video,
    Document
}

public sealed record Attachment(
    string Id,
    string Name,
    string Uri,
    string? MimeType,
    AttachmentKind Kind,
    string? ImageDescription = null);

public enum ToolStatus
{
    Running,
    Complete
}

public sealed record ToolActivity(
    string? Id,
    string Name,
    string? Input,
    string? Output,
    ToolStatus Status,
    double? DurationSeconds = null);

public sealed record ClarifyChoice(string Label, string Value);

public sealed record ClarifyActivity(
    string RequestId,
    string? QuestionId,
    string Question,
    IReadOnlyList<ClarifyChoice> Choices,
    bool AllowCustomInput,
    bool IsMultiSelect,
    bool IsResolved = false,
    string? SelectedAnswer = null,
    bool IsExpired = false);

public sealed record ApprovalActivity(
    string RequestId,
    string SessionId,
    string Command,
    string? Description,
    string? RiskLevel,
    IReadOnlyList<string> Patterns,
    bool IsResolved = false,
    string? Choice = null,
    bool IsWithdrawn = false);

public sealed record InputPromptActivity(
    string RequestId,
    string SessionId,
    string Kind,
    string Title,
    string? Prompt,
    string? Origin,
    bool IsMasked = true,
    bool IsResolved = false,
    bool IsExpired = false);

public sealed record ReviewActivity(
    string Summary,
    IReadOnlyList<string>? Details,
    string? FullSessionId);

public sealed record MessageReaction(string Emoji, string Author);

public sealed record DelegateAgentLine(string Kind, string Text, bool IsError);

public sealed record DelegateAgentActivity(
    string Id,
    string Goal,
    string? Model,
    string Status,
    int TaskCount,
    int TaskIndex,
    string? CurrentTool,
    string? Summary,
    IReadOnlyList<DelegateAgentLine> Stream,
    double? DurationSeconds = null);

public sealed record ChatMessage
{
    public required string Id { get; init; }
    public required MessageRole Role { get; init; }
    public string Content { get; set; } = "";
    public string? RawContent { get; init; }
    public string Timestamp { get; init; } = "";
    public string? Author { get; init; }
    public string? Reasoning { get; set; }
    public ToolActivity? Tool { get; set; }
    public ReviewActivity? Review { get; init; }
    public ClarifyActivity? Clarify { get; set; }
    public ApprovalActivity? Approval { get; set; }
    public InputPromptActivity? InputPrompt { get; set; }
    public IReadOnlyList<Attachment>? Attachments { get; init; }
    public string? DisplayKind { get; init; }
    public int? RowId { get; init; }
    public IReadOnlyList<MessageReaction> Reactions { get; init; } = [];
    public double? StepDurationSeconds { get; set; }
    public DelegateAgentActivity? DelegateAgent { get; set; }
}

public enum BackgroundTaskState
{
    None,
    Running,
    NeedsAttention,
    Completed,
    Failed
}

public sealed class SessionSummary : System.ComponentModel.INotifyPropertyChanged
{
    private string _title = "New Chat";
    private string _updatedLabel = "";
    private bool _isActive;
    private bool? _isUnread;
    private bool? _isPinned;
    private string? _preview;
    private BackgroundTaskState _taskStatus = BackgroundTaskState.None;
    private string? _backgroundStatusDetail;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public required string Id { get; init; }
    public string? StoredSessionId { get; set; }
    public string? RuntimeSessionId { get; set; }
    public IReadOnlyList<string> AlternateIds { get; set; } = [];
    public string Model { get; set; } = "";
    public double? LastActivityAt { get; set; }
    public string? Profile { get; init; }
    public SessionSource Source { get; init; } = SessionSource.Chat;
    public bool IsArchived { get; set; }
    public int? MessageCount { get; set; }
    public double? ReadWatermark { get; init; }
    public string? LineageRootId { get; init; }
    public string? EndReason { get; init; }

    public string Title
    {
        get => _title;
        set { if (_title != value) { _title = value; OnPropertyChanged(nameof(Title)); } }
    }

    public string UpdatedLabel
    {
        get => _updatedLabel;
        set { if (_updatedLabel != value) { _updatedLabel = value; OnPropertyChanged(nameof(UpdatedLabel)); } }
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnPropertyChanged(nameof(IsActive));
                OnTaskIndicatorChanged();
            }
        }
    }

    public bool? IsUnread
    {
        get => _isUnread;
        set
        {
            if (_isUnread != value)
            {
                _isUnread = value;
                OnPropertyChanged(nameof(IsUnread));
                OnTaskIndicatorChanged();
            }
        }
    }

    public bool? IsPinned
    {
        get => _isPinned;
        set { if (_isPinned != value) { _isPinned = value; OnPropertyChanged(nameof(IsPinned)); } }
    }

    public string? Preview
    {
        get => _preview;
        set { if (_preview != value) { _preview = value; OnPropertyChanged(nameof(Preview)); } }
    }

    public BackgroundTaskState TaskStatus
    {
        get => _taskStatus;
        set
        {
            if (_taskStatus != value)
            {
                _taskStatus = value;
                OnPropertyChanged(nameof(TaskStatus));
                OnTaskIndicatorChanged();
            }
        }
    }

    public string? BackgroundStatusDetail
    {
        get => _backgroundStatusDetail;
        set
        {
            if (_backgroundStatusDetail != value)
            {
                _backgroundStatusDetail = value;
                OnPropertyChanged(nameof(BackgroundStatusDetail));
                OnPropertyChanged(nameof(BackgroundStatusTooltip));
            }
        }
    }

    public bool ShowRunningDot => TaskStatus == BackgroundTaskState.Running;

    public bool ShowAlertDot => TaskStatus is BackgroundTaskState.NeedsAttention or BackgroundTaskState.Completed or BackgroundTaskState.Failed
        || (TaskStatus == BackgroundTaskState.None && IsUnread == true);

    public bool HasBackgroundBadge => ShowRunningDot || ShowAlertDot;

    public string BackgroundBadgeText => TaskStatus switch
    {
        BackgroundTaskState.Running => Localization.AppLocalization.T("Running", "运行中"),
        BackgroundTaskState.NeedsAttention => Localization.AppLocalization.T("Needs you", "待确认"),
        BackgroundTaskState.Completed => Localization.AppLocalization.T("Done", "已完成"),
        BackgroundTaskState.Failed => Localization.AppLocalization.T("Failed", "出错"),
        BackgroundTaskState.None when IsUnread == true => Localization.AppLocalization.T("Unread", "未读"),
        _ => ""
    };

    public string? BackgroundStatusTooltip => string.IsNullOrWhiteSpace(BackgroundStatusDetail)
        ? (HasBackgroundBadge ? BackgroundBadgeText : null)
        : $"{BackgroundBadgeText}: {BackgroundStatusDetail}";

    public void RefreshLocalization() => OnTaskIndicatorChanged();

    private void OnTaskIndicatorChanged()
    {
        OnPropertyChanged(nameof(ShowRunningDot));
        OnPropertyChanged(nameof(ShowAlertDot));
        OnPropertyChanged(nameof(HasBackgroundBadge));
        OnPropertyChanged(nameof(BackgroundBadgeText));
        OnPropertyChanged(nameof(BackgroundStatusTooltip));
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
}

public sealed record CronJobSummary(
    string Id,
    string Name,
    bool Enabled,
    string ScheduleDisplay,
    string? State,
    string? LastRunAt,
    string? NextRunAt,
    string? LastError,
    string? Prompt,
    string? RawSchedule = null,
    string? Deliver = null);

public sealed record KanbanBoardSummary(
    string Slug,
    string Title,
    int TaskCount);

public sealed record KanbanTaskSummary(
    string Id,
    string Title,
    string Status,
    string? Assignee,
    string? Priority,
    string? LatestSummary);

public sealed record PulseItemSummary(
    string Id,
    string Title,
    string Body,
    string? Topic,
    string? CreatedAt);

public sealed record ArtifactSummary(
    string Id,
    string Name,
    string Path,
    string? MimeType,
    bool IsPinned);

public sealed record BotProfile
{
    public required string Name { get; init; }
    public string? BotTitle { get; init; }
    public string DisplayName { get; init; } = "";
    public string? Description { get; init; }
    public string? Model { get; init; }
    public string? Provider { get; init; }
    public bool HasAvatar { get; init; }
    public bool IsPinned { get; init; }
    public string? AppearanceColorHex { get; init; }
    public string? CanonicalSessionId { get; init; }
    public string? LastPreview { get; init; }
    public bool IsDefault { get; init; }
}

public sealed record SavedDashboard
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Label { get; set; } = "My Hermes";
    public string NormalizedUrl { get; set; } = "";
    public string? Username { get; set; }
    public AuthMode AuthMode { get; set; } = AuthMode.Password;
    public bool UseCloudflareAccess { get; set; }
    public bool IsCombined { get; set; }
    public DateTimeOffset LastConnectedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum AuthMode
{
    Password,
    SessionToken,
    NativePkce
}

public enum ConnectionPhase
{
    Disconnected,
    Connecting,
    Authenticating,
    Connected,
    Reconnecting,
    Error
}

public sealed record GatewayReadyInfo(
    bool SessionAuthority,
    bool SendsChangeEvents,
    string? Version = null);

public sealed record SessionCreateResult(
    string SessionId,
    string? StoredSessionId = null,
    string? Profile = null);

public sealed record SessionResumeResult(
    string SessionId,
    string? StoredSessionId,
    bool IsRunning,
    int? ExecutionGeneration,
    int? Revision,
    string? Model,
    string? Provider,
    string? Cwd,
    double ContextPercent,
    int ContextUsed,
    int ContextMax,
    IReadOnlyList<ChatMessage> Messages);

public enum PromptSubmissionOutcome
{
    Accepted,
    Queued
}

public enum V2Tab
{
    Bots,
    Chats,
    Groups,
    Tasks,
    Pulse,
    Artifacts,
    Settings
}

public sealed class V2TabLayout
{
    public static readonly V2Tab[] StandardOrder = [V2Tab.Bots, V2Tab.Chats, V2Tab.Groups, V2Tab.Tasks, V2Tab.Settings];
    public static readonly V2Tab[] OptionalTabs = [V2Tab.Pulse, V2Tab.Artifacts];
    public static readonly HashSet<V2Tab> AlwaysShown = [V2Tab.Chats, V2Tab.Settings];
    public const int MostShown = 5;

    private readonly List<V2Tab> _order = [];
    private readonly HashSet<V2Tab> _hidden = [];
    private readonly List<(string Token, V2Tab? After)> _unknown = [];

    public IReadOnlyList<V2Tab> Order => _order;
    public IReadOnlySet<V2Tab> Hidden => _hidden;
    public IReadOnlyList<V2Tab> Shown => _order.Where(t => !_hidden.Contains(t)).ToList();

    public V2TabLayout(IEnumerable<string>? storedTokens = null)
    {
        foreach (var raw in storedTokens ?? [])
        {
            var isHidden = raw.StartsWith('-');
            var name = isHidden ? raw[1..] : raw;
            if (!TryParseTab(name, out var tab))
            {
                if (!string.IsNullOrWhiteSpace(name) && _unknown.All(u => u.Token != raw))
                {
                    _unknown.Add((raw, _order.Count > 0 ? _order[^1] : null));
                }
                continue;
            }
            if (_order.Contains(tab)) continue;
            _order.Add(tab);
            if (isHidden && !AlwaysShown.Contains(tab))
            {
                _hidden.Add(tab);
            }
        }

        foreach (var tab in StandardOrder)
        {
            if (!_order.Contains(tab))
            {
                var settingsIdx = _order.IndexOf(V2Tab.Settings);
                if (settingsIdx >= 0 && tab != V2Tab.Settings)
                    _order.Insert(settingsIdx, tab);
                else
                    _order.Add(tab);
            }
        }

        foreach (var tab in OptionalTabs)
        {
            if (!_order.Contains(tab))
            {
                var settingsIdx = _order.IndexOf(V2Tab.Settings);
                if (settingsIdx >= 0) _order.Insert(settingsIdx, tab);
                else _order.Add(tab);
                _hidden.Add(tab);
            }
        }

        for (var i = _order.Count - 1; i >= 0 && (_order.Count - _hidden.Count) > MostShown; i--)
        {
            var tab = _order[i];
            if (!AlwaysShown.Contains(tab) && !_hidden.Contains(tab))
            {
                _hidden.Add(tab);
            }
        }
    }

    public IReadOnlyList<string> ToStoredTokens()
    {
        var tokens = new List<string>();
        tokens.AddRange(_unknown.Where(u => u.After == null).Select(u => u.Token));
        foreach (var tab in _order)
        {
            var wire = tab.ToString().ToLowerInvariant();
            tokens.Add(_hidden.Contains(tab) ? "-" + wire : wire);
            tokens.AddRange(_unknown.Where(u => u.After == tab).Select(u => u.Token));
        }
        return tokens;
    }

    public bool CanHide(V2Tab tab) => !AlwaysShown.Contains(tab);

    public bool CanShow(V2Tab tab) => _hidden.Contains(tab) && Shown.Count < MostShown;

    public bool SetHidden(V2Tab tab, bool hide)
    {
        if (hide)
        {
            if (!CanHide(tab)) return false;
            return _hidden.Add(tab);
        }
        if (!_hidden.Contains(tab)) return false;
        if (Shown.Count >= MostShown) return false;
        return _hidden.Remove(tab);
    }

    public void Move(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _order.Count || toIndex < 0 || toIndex >= _order.Count || fromIndex == toIndex)
            return;
        var item = _order[fromIndex];
        _order.RemoveAt(fromIndex);
        _order.Insert(toIndex, item);
    }

    private static bool TryParseTab(string value, out V2Tab tab)
    {
        return value.ToLowerInvariant() switch
        {
            "bots" => Set(V2Tab.Bots, out tab),
            "chats" => Set(V2Tab.Chats, out tab),
            "groups" => Set(V2Tab.Groups, out tab),
            "tasks" => Set(V2Tab.Tasks, out tab),
            "pulse" => Set(V2Tab.Pulse, out tab),
            "artifacts" => Set(V2Tab.Artifacts, out tab),
            "settings" => Set(V2Tab.Settings, out tab),
            _ => Fail(out tab)
        };

        static bool Set(V2Tab v, out V2Tab t) { t = v; return true; }
        static bool Fail(out V2Tab t) { t = default; return false; }
    }
}

public sealed record BotProfileCapability(
    string Name,
    string? Label,
    string Description,
    int? ToolCount,
    bool Enabled);

public sealed record BotProfileDetails(
    string Name,
    string Description,
    string Soul,
    string Provider,
    string Model,
    IReadOnlyList<BotProfileCapability> Skills,
    IReadOnlyList<BotProfileCapability> Toolsets,
    IReadOnlyList<BotProfileCapability> McpServers);

public sealed record ModelProviderOption(
    string Slug,
    string DisplayName,
    IReadOnlyList<string> Aliases,
    bool IsCurrent,
    IReadOnlyList<string> Models,
    bool IsConfigured = true,
    string? EnvVarName = null,
    string? SourceTag = null)
{
    public bool IsReady => IsCurrent || IsConfigured;

    public string StatusBadge => IsCurrent
        ? Localization.AppLocalization.T("Active", "当前")
        : IsConfigured
            ? (string.Equals(SourceTag, "custom", StringComparison.OrdinalIgnoreCase)
                ? Localization.AppLocalization.T("Custom", "自定义")
                : Localization.AppLocalization.T("Ready", "已就绪"))
            : Localization.AppLocalization.T("Needs Key", "需配密钥");
}

public sealed record FunctionalApiKeyInfo(
    string Key,
    string Category,
    string Label,
    bool IsSet);

public sealed record ToolsetBackendSnapshot(
    string ToolsetName,
    string? ActiveProvider,
    string? ActiveSearchBackend,
    string? ActiveExtractBackend,
    IReadOnlyList<FunctionalApiKeyInfo> SetEnvVars);

public sealed record BotRosterSnapshot(
    IReadOnlyList<BotProfile> Profiles,
    bool SupportsBotProtocol);

public sealed record HermesUpdateCommit(
    string Sha,
    string Summary);

public sealed record HermesUpdateCheck(
    string InstallMethod,
    string CurrentVersion,
    int? Behind,
    bool UpdateAvailable,
    bool CanApply,
    string? UpdateCommand,
    string? Message,
    string? SourceVersion,
    string? RollbackTo,
    IReadOnlyList<HermesUpdateCommit> Commits)
{
    public bool CanUpdateHere => UpdateAvailable && CanApply;
}

public sealed record HermesUpdateStartResult(
    bool Ok,
    string ActionName,
    string? ActionId,
    string? Message,
    string? UpdateCommand);

public enum ActionPollVerdict
{
    Running,
    Succeeded,
    Failed,
    Unknown
}

public sealed record ActionStatusSnapshot(
    bool Running,
    int? ExitCode,
    IReadOnlyList<string> Lines,
    string? ReceiptOutcome = null,
    string? ReceiptActionId = null)
{
    public string? LatestLine
    {
        get
        {
            for (var i = Lines.Count - 1; i >= 0; i--)
            {
                var trimmed = Lines[i]?.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("===", StringComparison.Ordinal))
                    continue;
                return trimmed.Length > 240 ? trimmed[..240] + "…" : trimmed;
            }
            return null;
        }
    }
}

public sealed record GatewayRuntimeStatus(
    bool Running,
    string? State,
    int? Pid)
{
    public bool IsServing
    {
        get
        {
            if (!Running) return false;
            var s = State?.Trim().ToLowerInvariant();
            return s is not ("starting" or "stopped" or "startup_failed" or "draining");
        }
    }
}

public sealed record CachedSessionSnapshot(
    int? Revision,
    int ContextUsed,
    int ContextMax,
    double ContextPercent,
    string? Model,
    string? Cwd,
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<string> ReferencedMediaKeys);

public static class SessionCacheHelper
{
    private static readonly System.Text.RegularExpressions.Regex MarkdownMediaOrLinkRegex =
        new(@"\[[^\]]*\]\(([^)\s]+)\)", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static IReadOnlyList<string> CollectAliasIds(SessionSummary? session, params string?[] extras)
    {
        var ids = new List<string>();
        void Add(string? val)
        {
            if (!string.IsNullOrWhiteSpace(val) && !ids.Contains(val, StringComparer.Ordinal))
            {
                ids.Add(val.Trim());
            }
        }
        if (session is not null)
        {
            Add(session.StoredSessionId);
            Add(session.Id);
            Add(session.RuntimeSessionId);
            foreach (var alt in session.AlternateIds) Add(alt);
        }
        foreach (var ex in extras) Add(ex);
        return ids;
    }

    public static bool AreMessagesEquivalent(IReadOnlyList<ChatMessage> left, IReadOnlyList<ChatMessage> right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
        {
            var a = left[i];
            var b = right[i];
            if (a.Role != b.Role
                || a.RowId != b.RowId
                || !string.Equals(a.Id, b.Id, StringComparison.Ordinal)
                || !string.Equals(a.Content, b.Content, StringComparison.Ordinal)
                || !string.Equals(a.Reasoning, b.Reasoning, StringComparison.Ordinal)
                || (a.Attachments?.Count ?? 0) != (b.Attachments?.Count ?? 0)
                || !Equals(a.Tool, b.Tool))
            {
                return false;
            }
        }
        return true;
    }

    public static IReadOnlyList<string> CollectMediaKeys(IEnumerable<ChatMessage> messages)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var msg in messages)
        {
            if (msg.Attachments is not null)
            {
                foreach (var att in msg.Attachments)
                {
                    if (!string.IsNullOrWhiteSpace(att.Uri)) keys.Add(att.Uri);
                }
            }
            if (!string.IsNullOrWhiteSpace(msg.Content))
            {
                foreach (System.Text.RegularExpressions.Match m in MarkdownMediaOrLinkRegex.Matches(msg.Content))
                {
                    var target = m.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(target) && (target.StartsWith('/') || target.StartsWith("http", StringComparison.OrdinalIgnoreCase)))
                    {
                        keys.Add(target);
                    }
                }
            }
        }
        return keys.ToList();
    }
}


