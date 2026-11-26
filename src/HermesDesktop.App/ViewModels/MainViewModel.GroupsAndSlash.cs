using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HermesDesktop.App.Localization;
using HermesDesktop.Core.Localization;
using HermesDesktop.Core.Models;
using HermesDesktop.Core.Rpc;
using HermesDesktop.Core.Sessions;

namespace HermesDesktop.App.ViewModels;

public sealed partial class GroupBotChoiceViewModel(BotProfile profile, bool isSelected = false) : ObservableObject
{
    public BotProfile Profile { get; } = profile;
    public string Name => Profile.Name;
    public string DisplayName => string.IsNullOrWhiteSpace(Profile.DisplayName) ? Profile.Name : Profile.DisplayName;
    public string ModelDisplay => string.IsNullOrWhiteSpace(Profile.Model) ? "default" : Profile.Model!;

    [ObservableProperty] private bool _isSelected = isSelected;
}

public sealed partial class GroupMemberStatusViewModel(GroupMember member, string state, string? summary) : ObservableObject
{
    public GroupMember Member { get; } = member;
    public string MemberId => Member.MemberId;
    public string DisplayName => Member.DisplayName;
    public string HandleDisplay => Member.HandleDisplay;
    public string Initial => DisplayName.Length > 0 ? DisplayName[..1].ToUpperInvariant() : "B";

    [ObservableProperty] private string _state = state;
    [ObservableProperty] private string? _summary = summary;
    public bool IsWorking => string.Equals(State, "working", StringComparison.OrdinalIgnoreCase);
}

public sealed class GroupEventItemViewModel
{
    public GroupEvent Event { get; }

    public GroupEventItemViewModel(GroupEvent ev)
    {
        Event = ev;
        Attachments = new ObservableCollection<Attachment>(ev.Attachments);
    }

    public int Seq => Event.Seq;
    public string EventId => Event.EventId;
    public string Kind => Event.Kind;
    public bool IsUser => Event.Actor.IsUser || Event.IsUserMessage;
    public bool IsMember => !IsUser && (Event.Actor.IsMember || Event.IsMemberMessage);
    public bool IsSystem => !IsUser && !IsMember;
    public string ActorName => Event.Actor.DisplayName;
    public string ActorInitial => ActorName.Length > 0 ? ActorName[..1].ToUpperInvariant() : "G";
    public string Text => Event.Text;
    public bool HasAttachments => Attachments.Count > 0;
    public ObservableCollection<Attachment> Attachments { get; }

    public string TimeLabel => Event.CreatedAtMs is { } ms
        ? DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("HH:mm")
        : "";
}

public sealed partial class StartupDashboardOptionViewModel(
    SavedDashboard dashboard,
    bool isSelected,
    bool isConnectedHere,
    bool isBusyHere) : ObservableObject
{
    public SavedDashboard Dashboard { get; } = dashboard;
    public string Label => Dashboard.Label;

    [ObservableProperty] private bool _isSelected = isSelected;
    [ObservableProperty] private bool _isConnectedHere = isConnectedHere;
    [ObservableProperty] private bool _isBusyHere = isBusyHere;

    public string StatusHint => IsBusyHere
        ? LocStrings.Instance.StartupCardConnectingHint
        : IsConnectedHere
            ? LocStrings.Instance.StartupCardReadyHint
            : LocStrings.Instance.StartupCardSwitchHint;
}

public sealed partial class MainViewModel
{
    public event Action? TranscriptSelectionFocusAndSelectRequested;

    // ---- 1. Transcript Copy & Inline Full-Conversation Selection Mode ----

    [ObservableProperty] private bool _isTranscriptSelectionMode;
    [ObservableProperty] private string _transcriptSelectionText = "";
    [ObservableProperty] private bool _isGlobalCopiedFeedback;
    private CancellationTokenSource? _globalCopiedCts;

    public string GlobalCopyButtonText => IsGlobalCopiedFeedback ? Loc.CopiedBtn : Loc.CopyBtn;

    partial void OnIsGlobalCopiedFeedbackChanged(bool value) => OnPropertyChanged(nameof(GlobalCopyButtonText));

    public ObservableCollection<StartupDashboardOptionViewModel> StartupDashboards { get; } = [];

    [ObservableProperty] private bool _isStartupGateVisible;
    [ObservableProperty] private bool _isStartupGateReady;
    [ObservableProperty] private string _startupGateServerName = "";
    [ObservableProperty] private string _startupGateServerUrl = "";
    [ObservableProperty] private string? _startupGateError;
    [ObservableProperty] private bool _openedConnectFromStartupGate;
    private SavedDashboard? _startupGateDashboard;

    public bool CanCloseConnectOverlay => IsConnected || OpenedConnectFromStartupGate;

    partial void OnOpenedConnectFromStartupGateChanged(bool value) => OnPropertyChanged(nameof(CanCloseConnectOverlay));

    partial void OnIsStartupGateVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(IsHtmlPreviewWebViewVisible));
        if (value) RebuildStartupDashboardOptions();
    }

    partial void OnIsStartupGateReadyChanged(bool value)
    {
        OnPropertyChanged(nameof(StartupGateHeadline));
        OnPropertyChanged(nameof(IsStartupGateBuffering));
        RebuildStartupDashboardOptions();
    }

    partial void OnStartupGateErrorChanged(string? value)
    {
        OnPropertyChanged(nameof(HasStartupGateError));
        OnPropertyChanged(nameof(StartupGateHeadline));
        OnPropertyChanged(nameof(IsStartupGateBuffering));
        RebuildStartupDashboardOptions();
    }

    public bool HasStartupGateError => !string.IsNullOrWhiteSpace(StartupGateError);
    public bool IsStartupGateBuffering => !IsStartupGateReady && !HasStartupGateError;
    public string StartupGateHeadline => HasStartupGateError
        ? Loc.StartupErrorTitle
        : IsStartupGateReady ? Loc.StartupReadyTitle : Loc.StartupConnectingTitle;

    private void RebuildStartupDashboardOptions()
    {
        var selectedId = _startupGateDashboard?.Id ?? _activeDashboard?.Id ?? _settings.ActiveDashboardId;
        StartupDashboards.Clear();
        foreach (var d in SavedDashboards)
        {
            var isSel = selectedId.HasValue && d.Id == selectedId.Value;
            var isBusy = isSel && IsConnecting && IsStartupGateBuffering;
            var isConn = !isBusy && IsConnected && _activeDashboard?.Id == d.Id;
            StartupDashboards.Add(new StartupDashboardOptionViewModel(d, isSel, isConn, isBusy));
        }
    }

    [RelayCommand]
    private async Task SelectStartupDashboardAsync(StartupDashboardOptionViewModel? option)
    {
        if (option is null) return;
        if (IsConnecting) return;

        if (_activeDashboard?.Id == option.Dashboard.Id && IsConnected && IsStartupGateReady)
        {
            IsStartupGateVisible = false;
            return;
        }
        await TryReconnectSavedAsync(option.Dashboard, fromStartupGate: true);
    }

    [RelayCommand]
    public void EnterWorkspace()
    {
        if (!IsStartupGateReady || IsConnecting) return;
        IsStartupGateVisible = false;
    }

    [RelayCommand]
    private async Task RetryStartupConnectAsync()
    {
        var dash = _startupGateDashboard ?? SessionCatalogHelper.ResolveStartupDashboard(SavedDashboards, _settings.ActiveDashboardId);
        if (dash is null)
        {
            OpenConnectFromStartupGate();
            return;
        }
        await TryReconnectSavedAsync(dash, fromStartupGate: true);
    }

    [RelayCommand]
    private void OpenConnectFromStartupGate()
    {
        var dash = _startupGateDashboard ?? SessionCatalogHelper.ResolveStartupDashboard(SavedDashboards, _settings.ActiveDashboardId);
        OpenedConnectFromStartupGate = true;
        IsStartupGateVisible = false;
        if (dash is not null)
        {
            EditingDashboardId = dash.Id;
            PopulateConnectFormFromSaved(dash, _vault.LoadSecrets(dash.Id));
        }
        ConnectError = StartupGateError;
        ShowConnectOverlay = true;
    }

    public bool HasOpenOverlayModal =>
        IsStartupGateVisible || ShowConnectOverlay || IsPreviewModalOpen || IsConfirmingDeleteSession || IsConfirmingRenameSession
        || IsImageViewerOpen || IsSoulEditorOpen || IsCronEditorOpen
        || IsGroupCreateModalOpen || IsGroupSettingsModalOpen;

    [RelayCommand]
    private void CopyMessage(MessageItemViewModel? item)
    {
        if (item is null) return;
        var text = ExtractCopyableMessageText(item);
        if (string.IsNullOrWhiteSpace(text)) return;
        CopyTextRequested?.Invoke(text);
        item.TriggerCopiedFeedback();
    }

    [RelayCommand]
    private async Task CopyGlobalConversationAsync()
    {
        var fullText = BuildFullTranscriptPlainText();
        if (string.IsNullOrWhiteSpace(fullText))
        {
            StatusBanner = AppLocalization.T("Current session has no text to copy.", "当前会话暂无可复制内容。");
            return;
        }
        CopyTextRequested?.Invoke(fullText);
        try
        {
            _globalCopiedCts?.Cancel();
            _globalCopiedCts?.Dispose();
            var cts = new CancellationTokenSource();
            _globalCopiedCts = cts;
            IsGlobalCopiedFeedback = true;
            await Task.Delay(1800, cts.Token);
            if (!cts.IsCancellationRequested)
            {
                IsGlobalCopiedFeedback = false;
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by another click
        }
    }

    private async Task RefreshContextBreakdownAsync(string sessionId, int openGen)
    {
        if (_wsClient is not { IsConnected: true } || string.IsNullOrWhiteSpace(sessionId)) return;
        try
        {
            var breakdown = await _wsClient.GetContextBreakdownAsync(sessionId);
            if (openGen != _openSessionGeneration || breakdown is null) return;
            var (pct, used, max) = breakdown.Value;
            if (max > 0)
            {
                ContextUsed = used;
                ContextMax = max;
                ContextPercent = pct;
                HermesDesktop.Core.Diagnostics.AppDiagnostics.Log(
                    "CONTEXT",
                    $"Refreshed context breakdown session={sessionId} used={used} max={max} pct={pct:0.#}");
            }
        }
        catch (Exception ex)
        {
            HermesDesktop.Core.Diagnostics.AppDiagnostics.Log(
                "CONTEXT_WARN",
                $"session.context_breakdown skipped/failed session={sessionId}: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenMessageSelectModal(MessageItemViewModel? item)
    {
        if (item is null) return;
        var text = ExtractCopyableMessageText(item);
        if (string.IsNullOrWhiteSpace(text)) return;

        PreviewTitle = item.IsUser
            ? AppLocalization.T("Select & Copy Message", "框选 / 复制消息")
            : ActiveReplyAuthorName;
        PreviewFilePath = "";
        PreviewText = text;
        IsPreviewModalOpen = true;
        PreviewFocusAndSelectRequested?.Invoke();
    }

    [RelayCommand]
    public void ToggleTranscriptSelectionMode()
    {
        if (IsTranscriptSelectionMode)
        {
            IsTranscriptSelectionMode = false;
            return;
        }
        EnterTranscriptSelectionMode(selectAll: true);
    }

    [RelayCommand]
    public void CloseTranscriptSelectionMode()
    {
        IsTranscriptSelectionMode = false;
    }

    public void EnterTranscriptSelectionMode(bool selectAll = true)
    {
        var fullText = BuildFullTranscriptPlainText();
        if (string.IsNullOrWhiteSpace(fullText))
        {
            StatusBanner = AppLocalization.T("Current session has no text to select.", "当前会话暂无可复制内容。");
            return;
        }

        TranscriptSelectionText = fullText;
        IsTranscriptSelectionMode = true;
        if (selectAll)
        {
            TranscriptSelectionFocusAndSelectRequested?.Invoke();
        }
    }

    [RelayCommand]
    public void OpenTranscriptSelectModal()
    {
        EnterTranscriptSelectionMode(selectAll: true);
    }

    [RelayCommand]
    public void OpenGroupTranscriptSelectModal()
    {
        var sb = new StringBuilder();
        foreach (var ev in GroupEvents)
        {
            if (string.IsNullOrWhiteSpace(ev.Text)) continue;
            if (sb.Length > 0) sb.AppendLine().AppendLine();
            var time = string.IsNullOrWhiteSpace(ev.TimeLabel) ? "" : $" ({ev.TimeLabel})";
            sb.AppendLine($"[{ev.ActorName}{time}]");
            sb.Append(ev.Text.TrimEnd());
        }
        var text = sb.ToString();
        if (string.IsNullOrWhiteSpace(text)) return;

        PreviewTitle = ActiveGroupTitle;
        PreviewFilePath = "";
        PreviewText = text;
        IsPreviewModalOpen = true;
        PreviewFocusAndSelectRequested?.Invoke();
    }

    [RelayCommand]
    private void CopyFullTranscript()
    {
        var fullText = BuildFullTranscriptPlainText();
        if (string.IsNullOrWhiteSpace(fullText)) return;
        CopyTextRequested?.Invoke(fullText);
        StatusBanner = Loc.CopiedBtn;
    }

    [RelayCommand]
    private void ExportTranscriptMarkdown()
    {
        var fullText = BuildFullTranscriptPlainText();
        if (string.IsNullOrWhiteSpace(fullText)) return;
        var safeTitle = string.IsNullOrWhiteSpace(ActiveSessionDisplayTitle)
            ? "transcript"
            : string.Join("_", ActiveSessionDisplayTitle.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(safeTitle)) safeTitle = "transcript";
        SaveFileRequested?.Invoke($"{safeTitle}.md", null, fullText);
    }

    private static string ExtractCopyableMessageText(MessageItemViewModel item)
    {
        if (!string.IsNullOrWhiteSpace(item.Content))
        {
            return StripThinkingTags(item.Content);
        }
        if (item.Tool is not null)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{item.ToolDisplayTitle}]");
            if (!string.IsNullOrWhiteSpace(item.Tool.Input)) sb.AppendLine(item.Tool.Input);
            if (!string.IsNullOrWhiteSpace(item.Tool.Output)) sb.AppendLine(item.Tool.Output);
            return sb.ToString().Trim();
        }
        return "";
    }

    private string BuildFullTranscriptPlainText()
    {
        var sb = new StringBuilder();
        foreach (var item in ActiveMessages)
        {
            if (item.IsUser)
            {
                if (!item.HasVisibleContent && !item.HasAttachments) continue;
                if (sb.Length > 0) sb.AppendLine().AppendLine();
                var ts = string.IsNullOrWhiteSpace(item.Timestamp) ? "" : $" ({item.Timestamp})";
                sb.AppendLine($"[You{ts}]");
                if (item.HasVisibleContent) sb.Append(StripThinkingTags(item.Content).TrimEnd());
                if (item.HasAttachments)
                {
                    if (item.HasVisibleContent) sb.AppendLine();
                    sb.Append(string.Join(", ", item.Attachments.Select(a => $"[Attachment: {a.Name}]")));
                }
            }
            else if (item.IsElevatedAssistantReply)
            {
                var body = StripThinkingTags(item.Content);
                if (string.IsNullOrWhiteSpace(body) && !item.HasAttachments) continue;
                if (sb.Length > 0) sb.AppendLine().AppendLine();
                var ts = string.IsNullOrWhiteSpace(item.Timestamp) ? "" : $" ({item.Timestamp})";
                sb.AppendLine($"[{ActiveReplyAuthorName}{ts}]");
                if (!string.IsNullOrWhiteSpace(body)) sb.Append(body.TrimEnd());
                if (item.HasAttachments)
                {
                    if (!string.IsNullOrWhiteSpace(body)) sb.AppendLine();
                    sb.Append(string.Join(", ", item.Attachments.Select(a => $"[Attachment: {a.Name}]")));
                }
            }
            else if (item.IsClarify && item.Clarify is not null)
            {
                if (sb.Length > 0) sb.AppendLine().AppendLine();
                sb.Append($"[{ActiveReplyAuthorName} Clarify] {item.Clarify.Question}");
            }
            else if (item.IsSystem && !string.IsNullOrWhiteSpace(item.Content))
            {
                if (sb.Length > 0) sb.AppendLine().AppendLine();
                sb.Append($"[System] {item.Content.Trim()}");
            }
        }
        return sb.ToString();
    }

    private static string StripThinkingTags(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var cleaned = System.Text.RegularExpressions.Regex.Replace(
            raw,
            @"<(think|thinking|thought|reasoning)>[\s\S]*?</\1>",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return cleaned.Trim();
    }

    // ---- 3. Rename Session Modal & Command ----

    [ObservableProperty] private SessionSummary? _pendingRenameSession;
    [ObservableProperty] private string _renameSessionInput = "";
    [ObservableProperty] private string? _renameSessionError;
    [ObservableProperty] private bool _isRenamingSession;

    public bool IsConfirmingRenameSession => PendingRenameSession is not null;
    public bool HasRenameSessionError => !string.IsNullOrWhiteSpace(RenameSessionError);

    partial void OnPendingRenameSessionChanged(SessionSummary? value)
    {
        OnPropertyChanged(nameof(IsConfirmingRenameSession));
        OnPropertyChanged(nameof(IsHtmlPreviewWebViewVisible));
    }

    partial void OnRenameSessionErrorChanged(string? value) => OnPropertyChanged(nameof(HasRenameSessionError));

    [RelayCommand]
    private void RequestRenameSession(SessionSummary? session)
    {
        var target = session ?? ActiveSession;
        if (target is null) return;
        RenameSessionError = null;
        RenameSessionInput = target.Title ?? "";
        PendingRenameSession = target;
    }

    [RelayCommand]
    private void CancelRenameSession()
    {
        if (IsRenamingSession) return;
        RenameSessionError = null;
        PendingRenameSession = null;
    }

    [RelayCommand]
    private async Task ConfirmRenameSessionAsync()
    {
        var target = PendingRenameSession;
        if (target is null || IsRenamingSession) return;
        var nextTitle = RenameSessionInput.Trim();
        if (string.IsNullOrWhiteSpace(nextTitle))
        {
            RenameSessionError = AppLocalization.T("Title cannot be empty.", "会话标题不能为空。");
            return;
        }

        IsRenamingSession = true;
        RenameSessionError = null;
        try
        {
            await ApplySessionRenameAsync(target, nextTitle);
            PendingRenameSession = null;
        }
        catch (Exception ex)
        {
            RenameSessionError = ex.Message;
        }
        finally
        {
            IsRenamingSession = false;
        }
    }

    private async Task ApplySessionRenameAsync(SessionSummary target, string newTitle)
    {
        var targetId = target.StoredSessionId ?? target.Id;
        var profile = target.Profile ?? SelectedProfile?.Name;
        Exception? lastErr = null;
        var updatedRemote = false;

        if (_wsClient is { IsConnected: true })
        {
            try
            {
                await _wsClient.RenameSessionAsync(targetId, newTitle, profile);
                updatedRemote = true;
            }
            catch (Exception ex)
            {
                lastErr = ex;
            }
        }

        if (!updatedRemote && _restClient is not null)
        {
            await _restClient.RenameSessionAsync(targetId, newTitle, profile);
            updatedRemote = true;
        }
        else if (!updatedRemote && lastErr is not null)
        {
            throw lastErr;
        }

        var runtimeId = ActiveSession is not null && SessionsMatch(target, ActiveSession, _activeRuntimeSessionId)
            ? _activeRuntimeSessionId
            : target.RuntimeSessionId;

        foreach (var s in _allSessions.Where(s => SessionsMatch(s, target, runtimeId)))
        {
            s.Title = newTitle;
        }
        if (ActiveSession is not null && SessionsMatch(ActiveSession, target, runtimeId))
        {
            ActiveSession.Title = newTitle;
            OnPropertyChanged(nameof(ActiveSessionDisplayTitle));
        }
        if (BesideSession is not null && SessionsMatch(BesideSession, target, runtimeId))
        {
            BesideSession.Title = newTitle;
            OnPropertyChanged(nameof(BesideSessionDisplayTitle));
        }
        if (SideOrActiveSessionMatches(target))
        {
            PersistActiveSessionSnapshot();
        }
        StatusBanner = AppLocalization.T($"Renamed session to \"{newTitle}\"", $"已重命名会话为「{newTitle}」");
    }

    private bool SideOrActiveSessionMatches(SessionSummary target) =>
        ActiveSession is not null && SessionsMatch(ActiveSession, target, _activeRuntimeSessionId);

    private string ActiveMediaSessionKey(SessionSummary? session = null)
    {
        var s = session ?? ActiveSession;
        return s?.StoredSessionId ?? s?.Id ?? s?.RuntimeSessionId ?? "default";
    }

    private bool TryHydrateSessionFromLocalCache(SessionSummary session)
    {
        if (_activeDashboard is null) return false;
        var profile = session.Profile ?? SelectedProfile?.Name ?? "default";
        var aliases = SessionCacheHelper.CollectAliasIds(session);
        var snap = _cache.LoadSessionSnapshot(_activeDashboard.Id, profile, aliases);
        if (snap is null || snap.Messages.Count == 0) return false;

        ActiveMessages.Clear();
        _userExpandedRunIds.Clear();
        _userCollapsedRunIds.Clear();
        foreach (var msg in snap.Messages)
        {
            ActiveMessages.Add(new MessageItemViewModel(msg));
        }

        if (snap.ContextMax > 0)
        {
            ContextUsed = snap.ContextUsed;
            ContextMax = snap.ContextMax;
            ContextPercent = snap.ContextPercent;
        }
        if (!string.IsNullOrWhiteSpace(snap.Model)) CurrentModelLabel = snap.Model;
        if (!string.IsNullOrWhiteSpace(snap.Cwd)) CurrentCwd = snap.Cwd;

        RecomputeTranscriptPresentation(ActiveMessages, IsRunningTurn);
        TranscriptScrollRequested?.Invoke(true);
        HermesDesktop.Core.Diagnostics.AppDiagnostics.Log(
            "CACHE",
            $"Hydrated {snap.Messages.Count} cached messages for key={aliases.FirstOrDefault()} ctx={snap.ContextUsed}/{snap.ContextMax}");
        return true;
    }

    private void PersistActiveSessionSnapshot(IReadOnlyList<ChatMessage>? authoritativeMessages = null, int? revision = null)
    {
        if (_activeDashboard is null || ActiveSession is null) return;
        var list = authoritativeMessages ?? ActiveMessages.Select(m => m.ToChatMessage()).ToList();
        if (list.Count == 0) return;

        var profile = ActiveSession.Profile ?? SelectedProfile?.Name ?? "default";
        var aliases = SessionCacheHelper.CollectAliasIds(ActiveSession, _activeRuntimeSessionId);
        var mediaKeys = SessionCacheHelper.CollectMediaKeys(list);
        var snap = new CachedSessionSnapshot(
            Revision: revision,
            ContextUsed: ContextUsed,
            ContextMax: ContextMax,
            ContextPercent: ContextPercent,
            Model: CurrentModelLabel,
            Cwd: CurrentCwd,
            Messages: list,
            ReferencedMediaKeys: mediaKeys);
        _cache.SaveSessionSnapshot(_activeDashboard.Id, profile, aliases, snap);
    }

    private void PurgeDeletedSessionLocalAndInMemory(
        SessionSummary target,
        string? deletedRuntime,
        IReadOnlyList<ChatMessage>? extraMessages = null)
    {
        var aliases = SessionCacheHelper.CollectAliasIds(target, deletedRuntime);
        if (_activeDashboard is not null)
        {
            var purgedKeys = _cache.DeleteSessionCacheAndFiles(_activeDashboard.Id, target.Profile ?? SelectedProfile?.Name, aliases);
            foreach (var mk in purgedKeys)
            {
                _mediaBytesCache.TryRemove(mk, out _);
                _previewHtmlCache.Remove(mk);
            }
        }

        if (extraMessages is not null)
        {
            foreach (var mk in SessionCacheHelper.CollectMediaKeys(extraMessages))
            {
                _mediaBytesCache.TryRemove(mk, out _);
                _previewHtmlCache.Remove(mk);
            }
        }

        // Also purge any pending or uploaded attachment URLs from memory caches
        if (!string.IsNullOrWhiteSpace(HtmlPreviewFilePath)
            && extraMessages is not null
            && SessionCacheHelper.CollectMediaKeys(extraMessages).Contains(HtmlPreviewFilePath, StringComparer.Ordinal))
        {
            IsHtmlPreviewPaneOpen = false;
            HtmlPreviewFilePath = "";
            HtmlPreviewContent = "";
        }
        if (IsPreviewModalOpen && !string.IsNullOrWhiteSpace(PreviewFilePath)
            && extraMessages is not null
            && SessionCacheHelper.CollectMediaKeys(extraMessages).Contains(PreviewFilePath, StringComparer.Ordinal))
        {
            IsPreviewModalOpen = false;
            PreviewFilePath = "";
            PreviewText = "";
        }
    }

    // ---- 4. Export / Download Generated Files (.md, text, binary) ----

    [RelayCommand]
    public async Task DownloadRemoteFileAsync(string? remotePath)
    {
        if (string.IsNullOrWhiteSpace(remotePath)) return;
        var fileName = Path.GetFileName(remotePath.Split('?')[0]);
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "download.md";

        try
        {
            var ext = Path.GetExtension(fileName);
            if (!string.IsNullOrEmpty(ext) && BinaryMediaExtensions.Contains(ext))
            {
                var bytes = await LoadMediaBytesAsync(remotePath);
                if (bytes is not { Length: > 0 })
                {
                    StatusBanner = AppLocalization.T($"Could not download {fileName}.", $"无法下载文件：{fileName}");
                    return;
                }
                SaveFileRequested?.Invoke(fileName, bytes, null);
                return;
            }

            var content = await LoadPreviewHtmlAsync(remotePath);
            if (content is null)
            {
                StatusBanner = AppLocalization.T($"Could not read {fileName}.", $"无法读取文件：{fileName}");
                return;
            }
            SaveFileRequested?.Invoke(fileName, null, content);
        }
        catch (Exception ex)
        {
            StatusBanner = AppLocalization.T($"Export failed: {ex.Message}", $"导出失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ExportCurrentPreviewFileAsync()
    {
        if (!string.IsNullOrWhiteSpace(PreviewFilePath))
        {
            await DownloadRemoteFileAsync(PreviewFilePath);
            return;
        }
        if (string.IsNullOrWhiteSpace(PreviewText)) return;
        var safeTitle = string.IsNullOrWhiteSpace(ActiveSessionDisplayTitle)
            ? "transcript"
            : string.Join("_", ActiveSessionDisplayTitle.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(safeTitle)) safeTitle = "transcript";
        SaveFileRequested?.Invoke($"{safeTitle}.md", null, PreviewText);
    }

    // ---- 7. Native Slash Commands (/compress, /new, /btw, /bg, slash.exec, etc.) ----

    private List<SlashCommandDescriptor> _slashCatalog = [.. SlashCommandParser.BuiltInCommands];
    public ObservableCollection<SlashCommandDescriptor> SlashSuggestions { get; } = [];
    public bool IsSlashSuggestionsVisible => IsChatsTab && SlashSuggestions.Count > 0;

    private async Task SyncSlashCatalogAsync()
    {
        if (_wsClient is not { IsConnected: true }) return;
        try
        {
            var serverCatalog = await _wsClient.CommandsCatalogAsync();
            if (serverCatalog.Count > 0)
            {
                _slashCatalog = serverCatalog
                    .Concat(SlashCommandParser.BuiltInCommands)
                    .GroupBy(c => c.Name.ToLowerInvariant())
                    .Select(g => g.First())
                    .ToList();
                UpdateSlashSuggestions(ComposerText);
            }
        }
        catch
        {
            // Older gateways may not expose commands.catalog; built-ins stay active.
        }
    }

    private void UpdateSlashSuggestions(string text)
    {
        var matches = SlashCommandParser.FilterSuggestions(text, _slashCatalog, maxResults: 8);
        SlashSuggestions.Clear();
        foreach (var item in matches)
        {
            SlashSuggestions.Add(item);
        }
        OnPropertyChanged(nameof(IsSlashSuggestionsVisible));
    }

    [RelayCommand]
    private void ApplySlashSuggestion(SlashCommandDescriptor? cmd)
    {
        if (cmd is null) return;
        ComposerText = string.IsNullOrWhiteSpace(cmd.ArgsHint) ? cmd.Name : $"{cmd.Name} ";
        SlashSuggestions.Clear();
        OnPropertyChanged(nameof(IsSlashSuggestionsVisible));
    }

    private async Task<bool> TryHandleSlashCommandAsync(string input)
    {
        if (!SlashCommandParser.TryParse(input, out var parsed)) return false;
        if (_wsClient is null) return false;

        ComposerText = "";
        SlashSuggestions.Clear();
        OnPropertyChanged(nameof(IsSlashSuggestionsVisible));
        if (ActiveSession is not null) _cache.SaveDraft(DraftKey(ActiveSession.Id), "");

        // Commands that don't require an active session first
        if (parsed.NormalizedName is "/new" or "/reset" or "/clear")
        {
            await CreateNewSessionAsync();
            AppendSystemNotice(AppLocalization.T("Started a fresh session.", "已新建干净会话。"));
            return true;
        }

        if (parsed.NormalizedName == "/help")
        {
            var sb = new StringBuilder();
            sb.AppendLine(AppLocalization.T("### Native Hermes Slash Commands", "### Hermes 原生斜杠命令"));
            foreach (var c in _slashCatalog)
            {
                var args = string.IsNullOrWhiteSpace(c.ArgsHint) ? "" : $" `{c.ArgsHint}`";
                sb.AppendLine($"- **`{c.Name}`**{args} — {c.Description}");
            }
            AppendAssistantMarkdownNotice(sb.ToString().TrimEnd());
            return true;
        }

        if (ActiveSession is null)
        {
            await CreateNewSessionAsync();
            if (ActiveSession is null) return true;
        }

        var session = ActiveSession;
        var profile = session.Profile ?? SelectedProfile?.Name;
        var targetId = _activeRuntimeSessionId ?? session.RuntimeSessionId ?? session.Id;

        if (parsed.NormalizedName == "/queue")
        {
            if (string.IsNullOrWhiteSpace(parsed.ArgsText))
            {
                AppendSystemNotice(AppLocalization.T("Usage: /queue <prompt>", "用法：/queue <提示词>"));
                return true;
            }
            ComposerText = parsed.ArgsText;
            return false; // Let normal prompt pipeline send or queue it
        }

        if (parsed.NormalizedName == "/steer")
        {
            if (string.IsNullOrWhiteSpace(parsed.ArgsText))
            {
                AppendSystemNotice(AppLocalization.T("Usage: /steer <instruction>", "用法：/steer <纠偏指令>"));
                return true;
            }
            ComposerText = parsed.ArgsText;
            await SteerTurnAsync();
            return true;
        }

        if (parsed.NormalizedName == "/rename")
        {
            if (string.IsNullOrWhiteSpace(parsed.ArgsText))
            {
                RequestRenameSession(session);
            }
            else
            {
                await ApplySessionRenameAsync(session, parsed.ArgsText);
            }
            return true;
        }

        try
        {
            switch (parsed.NormalizedName)
            {
                case "/compress":
                case "/compact":
                    StatusBanner = Loc.SlashCommandCompressing;
                    AppendSystemNotice(Loc.SlashCommandCompressing);
                    var summary = await _wsClient.CompressSessionWithSummaryAsync(targetId, parsed.ArgsText, profile);
                    AppendSystemNotice(summary);
                    StatusBanner = $"Connected · {_activeDashboard?.Label}";
                    await OpenSessionAsync(session);
                    return true;

                case "/stop":
                    await InterruptTurnAsync();
                    var killed = await _wsClient.StopProcessesAsync(targetId);
                    AppendSystemNotice(AppLocalization.T(
                        killed > 0 ? $"Stopped turn and terminated {killed} background process(es)." : "Stopped active turn.",
                        killed > 0 ? $"已停止当前回合，并终止了 {killed} 个后台进程。" : "已停止当前回合。"));
                    return true;

                case "/btw":
                case "/bg":
                case "/background":
                    if (string.IsNullOrWhiteSpace(parsed.ArgsText))
                    {
                        AppendSystemNotice(AppLocalization.T($"Usage: {parsed.NormalizedName} <prompt>", $"用法：{parsed.NormalizedName} <内容>"));
                        return true;
                    }
                    var method = parsed.NormalizedName == "/btw" ? "prompt.btw" : "prompt.background";
                    var taskId = await _wsClient.StartSideAgentAsync(method, targetId, parsed.ArgsText);
                    AppendSystemNotice(AppLocalization.T(
                        $"Started side task{(string.IsNullOrWhiteSpace(taskId) ? "" : $" ({taskId})")}: {parsed.ArgsText}",
                        $"已启动后台子任务{(string.IsNullOrWhiteSpace(taskId) ? "" : $"（{taskId}）")}：{parsed.ArgsText}"));
                    return true;

                case "/reload-mcp":
                    var mcpMsg = await _wsClient.ReloadMcpAsync(targetId, confirm: true);
                    AppendSystemNotice(mcpMsg);
                    return true;

                case "/reload-skills":
                    var skillMsg = await _wsClient.ReloadSkillsAsync(targetId);
                    AppendSystemNotice(skillMsg);
                    return true;

                default:
                    StatusBanner = Loc.SlashCommandExecuting;
                    SlashExecOutcome outcome;
                    try
                    {
                        outcome = await _wsClient.ExecuteSlashAsync(targetId, parsed.RawCommand);
                    }
                    catch (RpcException ex) when (IsMissingMethodError(ex))
                    {
                        outcome = await _wsClient.DispatchCommandAsync(targetId, parsed.NormalizedName, parsed.ArgsText);
                    }
                    StatusBanner = $"Connected · {_activeDashboard?.Label}";
                    await HandleSlashExecOutcomeAsync(outcome, parsed);
                    return true;
            }
        }
        catch (Exception ex)
        {
            StatusBanner = $"Slash command failed: {ex.Message}";
            AppendSystemNotice(AppLocalization.T(
                $"Command {parsed.NormalizedName} failed: {ex.Message}",
                $"命令 {parsed.NormalizedName} 执行失败：{ex.Message}"));
            return true;
        }
    }

    private async Task HandleSlashExecOutcomeAsync(SlashExecOutcome outcome, ParsedSlashCommand parsed)
    {
        if (string.Equals(outcome.Type, "prefill", StringComparison.OrdinalIgnoreCase))
        {
            ComposerText = outcome.Prompt ?? outcome.Text ?? "";
            return;
        }
        if (string.Equals(outcome.Type, "send", StringComparison.OrdinalIgnoreCase))
        {
            var expanded = outcome.Prompt ?? outcome.Text;
            if (!string.IsNullOrWhiteSpace(expanded))
            {
                ComposerText = expanded;
                await SendOrQueueAsync();
                return;
            }
        }

        var output = outcome.Text ?? outcome.Message ?? Loc.SlashCommandDone;
        if (!string.IsNullOrWhiteSpace(outcome.Title))
        {
            output = $"### {outcome.Title}\n\n{output}";
        }
        if (output.Contains('\n') || output.Contains('`') || output.Contains('#'))
        {
            AppendAssistantMarkdownNotice(output);
        }
        else
        {
            AppendSystemNotice(output);
        }
    }

    private void AppendAssistantMarkdownNotice(string markdown)
    {
        ActiveMessages.Add(new MessageItemViewModel(new ChatMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Role = MessageRole.Assistant,
            Content = markdown,
            Timestamp = DateTimeOffset.Now.ToString("HH:mm")
        }));
        TranscriptScrollRequested?.Invoke(true);
    }

    private static bool IsMissingMethodError(RpcException ex) =>
        ex.Code == -32601
        || ex.Message.Contains("unknown", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("unsupported", StringComparison.OrdinalIgnoreCase);

    // ---- 2. Multi-Agent Group Chats (V2Tab.Groups) ----

    public ObservableCollection<GroupRoom> GroupRooms { get; } = [];
    public ObservableCollection<GroupEventItemViewModel> GroupEvents { get; } = [];
    public ObservableCollection<GroupMemberStatusViewModel> ActiveGroupMembers { get; } = [];
    public ObservableCollection<GroupPendingApproval> GroupPendingApprovals { get; } = [];
    public ObservableCollection<GroupBotChoiceViewModel> GroupCreateMemberChoices { get; } = [];
    public ObservableCollection<string> GroupRoutingModes { get; } = ["auto", "lead", "round_robin", "mentions_only"];

    [ObservableProperty] private GroupRoom? _selectedGroupRoom;
    [ObservableProperty] private string _groupComposerText = "";
    [ObservableProperty] private string? _groupStatusNotice;
    [ObservableProperty] private bool _isGroupsSupported = true;
    [ObservableProperty] private bool _isLoadingGroups;
    [ObservableProperty] private bool _isSendingGroupMessage;
    [ObservableProperty] private bool _isGroupRunning;
    [ObservableProperty] private bool _canContinueGroupDiscussion;
    [ObservableProperty] private string _groupMemberSummaryText = "";
    [ObservableProperty] private string _groupDriverBanner = "";
    private string? _groupContinueEventId;
    private int _groupLastSeq;
    private GroupCapabilities? _groupCapabilities;
    private DispatcherTimer? _groupPollTimer;

    // Create Group Modal
    [ObservableProperty] private bool _isGroupCreateModalOpen;
    [ObservableProperty] private string _groupCreateName = "";
    [ObservableProperty] private string? _groupCreateError;
    [ObservableProperty] private bool _isCreatingGroup;

    // Group Settings / Context Modal
    [ObservableProperty] private bool _isGroupSettingsModalOpen;
    [ObservableProperty] private string _groupEditName = "";
    [ObservableProperty] private string _groupEditPurpose = "";
    [ObservableProperty] private string _groupEditNotes = "";
    [ObservableProperty] private string _groupEditRoutingMode = "auto";
    [ObservableProperty] private string? _groupSettingsError;
    [ObservableProperty] private bool _isSavingGroupSettings;

    public bool HasSelectedGroupRoom => SelectedGroupRoom is not null;
    public bool HasNoGroupRooms => !IsLoadingGroups && GroupRooms.Count == 0;
    public bool HasGroupPendingApprovals => GroupPendingApprovals.Count > 0;
    public bool HasGroupCreateError => !string.IsNullOrWhiteSpace(GroupCreateError);
    public bool HasGroupSettingsError => !string.IsNullOrWhiteSpace(GroupSettingsError);
    public string ActiveGroupTitle => SelectedGroupRoom?.Name ?? Loc.GroupsTitle;

    partial void OnIsGroupCreateModalOpenChanged(bool value) => OnPropertyChanged(nameof(IsHtmlPreviewWebViewVisible));
    partial void OnIsGroupSettingsModalOpenChanged(bool value) => OnPropertyChanged(nameof(IsHtmlPreviewWebViewVisible));
    partial void OnGroupCreateErrorChanged(string? value) => OnPropertyChanged(nameof(HasGroupCreateError));
    partial void OnGroupSettingsErrorChanged(string? value) => OnPropertyChanged(nameof(HasGroupSettingsError));
    partial void OnIsLoadingGroupsChanged(bool value) => OnPropertyChanged(nameof(HasNoGroupRooms));

    partial void OnSelectedGroupRoomChanged(GroupRoom? value)
    {
        OnPropertyChanged(nameof(HasSelectedGroupRoom));
        OnPropertyChanged(nameof(ActiveGroupTitle));
        if (value is not null)
        {
            _ = OpenGroupRoomAsync(value);
        }
        else
        {
            GroupEvents.Clear();
            ActiveGroupMembers.Clear();
            GroupPendingApprovals.Clear();
            OnPropertyChanged(nameof(HasGroupPendingApprovals));
        }
    }

    private async Task EnsureGroupsLoadedAsync()
    {
        if (!IsConnected || _wsClient is not { IsConnected: true }) return;
        await RefreshGroupRoomsAsync();
        StartOrUpdateGroupPollTimer();
    }

    private void StartOrUpdateGroupPollTimer()
    {
        if (_groupPollTimer is null)
        {
            _groupPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
            _groupPollTimer.Tick += async (_, _) => await PollActiveGroupRoomAsync();
        }
        _groupPollTimer.Interval = TimeSpan.FromSeconds(IsGroupRunning ? 1.5 : 4.0);
        if (SelectedTab == V2Tab.Groups && !_groupPollTimer.IsEnabled)
        {
            _groupPollTimer.Start();
        }
    }

    private void StopGroupPollingIfInactive()
    {
        _groupPollTimer?.Stop();
    }

    [RelayCommand]
    private async Task RefreshGroupRoomsAsync()
    {
        if (_wsClient is not { IsConnected: true }) return;
        IsLoadingGroups = true;
        GroupStatusNotice = null;
        try
        {
            _groupCapabilities ??= await _wsClient.GroupsCapabilitiesAsync();
            IsGroupsSupported = true;
            var rooms = await _wsClient.GroupsListAsync();
            var prevId = SelectedGroupRoom?.RoomId;
            GroupRooms.Clear();
            foreach (var r in rooms)
            {
                GroupRooms.Add(r);
            }
            OnPropertyChanged(nameof(HasNoGroupRooms));
            if (GroupRooms.Count > 0)
            {
                var next = prevId is not null ? GroupRooms.FirstOrDefault(r => r.RoomId == prevId) : null;
                SelectedGroupRoom = next ?? GroupRooms[0];
            }
            else
            {
                SelectedGroupRoom = null;
            }
        }
        catch (RpcException ex) when (IsMissingMethodError(ex))
        {
            IsGroupsSupported = false;
            GroupStatusNotice = AppLocalization.T(
                "This Hermes gateway does not expose Multi-Agent Group Chats (`groups.*`) yet.",
                "当前连接的 Hermes 网关版本尚未启用多智能体群组（groups.*）接口。");
        }
        catch (Exception ex)
        {
            GroupStatusNotice = AppLocalization.T($"Could not load groups: {ex.Message}", $"加载群组失败：{ex.Message}");
        }
        finally
        {
            IsLoadingGroups = false;
            OnPropertyChanged(nameof(HasNoGroupRooms));
        }
    }

    private async Task OpenGroupRoomAsync(GroupRoom room)
    {
        if (_wsClient is not { IsConnected: true }) return;
        GroupEvents.Clear();
        GroupPendingApprovals.Clear();
        _groupLastSeq = 0;
        GroupStatusNotice = null;

        try
        {
            var state = await _wsClient.GroupsStateAsync(room.RoomId);
            if (state is not null) ApplyGroupState(state);

            var page = await _wsClient.GroupsLogAsync(room.RoomId, sinceSeq: 0, limit: 150);
            AppendGroupEvents(page.Events);
            _groupLastSeq = Math.Max(_groupLastSeq, page.LatestSeq);
            GroupTranscriptScrollRequested?.Invoke();
            StartOrUpdateGroupPollTimer();
        }
        catch (Exception ex)
        {
            GroupStatusNotice = AppLocalization.T($"Failed to open group: {ex.Message}", $"打开群聊失败：{ex.Message}");
        }
    }

    private async Task PollActiveGroupRoomAsync()
    {
        if (SelectedTab != V2Tab.Groups || _wsClient is not { IsConnected: true } || SelectedGroupRoom is null)
            return;

        var roomId = SelectedGroupRoom.RoomId;
        try
        {
            var state = await _wsClient.GroupsStateAsync(roomId);
            if (SelectedGroupRoom?.RoomId != roomId) return;
            if (state is not null) ApplyGroupState(state);

            var page = await _wsClient.GroupsLogAsync(roomId, sinceSeq: _groupLastSeq, limit: 150);
            if (SelectedGroupRoom?.RoomId != roomId) return;
            if (page.Events.Count > 0)
            {
                AppendGroupEvents(page.Events);
                _groupLastSeq = Math.Max(_groupLastSeq, page.LatestSeq);
                GroupTranscriptScrollRequested?.Invoke();
            }
            StartOrUpdateGroupPollTimer();
        }
        catch
        {
            // Transient poll errors ignored silently to avoid UI spam
        }
    }

    private void ApplyGroupState(GroupStateSnapshot state)
    {
        IsGroupRunning = state.DriverStatus.Running || state.DriverStatus.WorkingCount > 0;
        CanContinueGroupDiscussion = state.DriverStatus.CanContinue && !string.IsNullOrWhiteSpace(state.DriverStatus.ContinueDiscussionEventId);
        _groupContinueEventId = state.DriverStatus.ContinueDiscussionEventId;
        GroupEditName = state.Room.Name;
        GroupEditPurpose = state.Context.Purpose;
        GroupEditNotes = state.Context.Notes;
        GroupEditRoutingMode = string.IsNullOrWhiteSpace(state.Context.Routing.Mode) ? "auto" : state.Context.Routing.Mode;

        if (state.DriverStatus.RoutingModes.Count > 0)
        {
            GroupRoutingModes.Clear();
            foreach (var m in state.DriverStatus.RoutingModes) GroupRoutingModes.Add(m);
        }

        var activityById = state.DriverStatus.MemberActivities.ToDictionary(a => a.MemberId, a => a, StringComparer.OrdinalIgnoreCase);
        ActiveGroupMembers.Clear();
        foreach (var member in state.Room.Members)
        {
            activityById.TryGetValue(member.MemberId, out var act);
            ActiveGroupMembers.Add(new GroupMemberStatusViewModel(member, act?.State ?? "idle", act?.Summary));
        }
        GroupMemberSummaryText = string.Format(Loc.GroupMembersCountFormat, state.Room.Members.Count);

        GroupPendingApprovals.Clear();
        foreach (var approval in state.DriverStatus.PendingApprovals)
        {
            GroupPendingApprovals.Add(approval);
        }
        OnPropertyChanged(nameof(HasGroupPendingApprovals));

        GroupDriverBanner = IsGroupRunning
            ? AppLocalization.T(
                $"Running · {Math.Max(1, state.DriverStatus.WorkingCount)} bot(s) responding…",
                $"讨论进行中 · {Math.Max(1, state.DriverStatus.WorkingCount)} 个智能体正在响应…")
            : (!string.IsNullOrWhiteSpace(state.Context.Purpose)
                ? state.Context.Purpose
                : $"{Loc.GroupRoutingLabel}: {GroupEditRoutingMode}");
    }

    private void AppendGroupEvents(IReadOnlyList<GroupEvent> incoming)
    {
        var seen = GroupEvents.Select(e => e.EventId).ToHashSet(StringComparer.Ordinal);
        foreach (var ev in incoming)
        {
            if (seen.Add(ev.EventId))
            {
                GroupEvents.Add(new GroupEventItemViewModel(ev));
            }
        }
    }

    [RelayCommand]
    private void InsertGroupMention(GroupMemberStatusViewModel? member)
    {
        if (member is null) return;
        var mention = $"{member.HandleDisplay} ";
        if (string.IsNullOrWhiteSpace(GroupComposerText))
            GroupComposerText = mention;
        else if (GroupComposerText.EndsWith(' ') || GroupComposerText.EndsWith('\n'))
            GroupComposerText += mention;
        else
            GroupComposerText += " " + mention;
    }

    [RelayCommand]
    private async Task SendGroupMessageAsync()
    {
        if (_wsClient is not { IsConnected: true } || SelectedGroupRoom is null || IsSendingGroupMessage) return;
        var text = GroupComposerText.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        IsSendingGroupMessage = true;
        GroupComposerText = "";
        GroupStatusNotice = null;
        var roomId = SelectedGroupRoom.RoomId;
        var eventId = Guid.NewGuid().ToString("N");
        try
        {
            var result = await _wsClient.GroupsSendAsync(roomId, eventId, text);
            if (result.Event is not null)
            {
                AppendGroupEvents([result.Event]);
                _groupLastSeq = Math.Max(_groupLastSeq, result.Event.Seq);
            }
            if (result.DriverStarted)
            {
                IsGroupRunning = true;
            }
            GroupTranscriptScrollRequested?.Invoke();
            StartOrUpdateGroupPollTimer();
            await PollActiveGroupRoomAsync();
        }
        catch (Exception ex)
        {
            GroupComposerText = text;
            GroupStatusNotice = AppLocalization.T($"Send failed: {ex.Message}", $"发送失败：{ex.Message}");
        }
        finally
        {
            IsSendingGroupMessage = false;
        }
    }

    [RelayCommand]
    private async Task StopGroupRunAsync()
    {
        if (_wsClient is not { IsConnected: true } || SelectedGroupRoom is null) return;
        try
        {
            await _wsClient.GroupsStopAsync(SelectedGroupRoom.RoomId);
            await PollActiveGroupRoomAsync();
        }
        catch (Exception ex)
        {
            GroupStatusNotice = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ContinueGroupDiscussionAsync()
    {
        if (_wsClient is not { IsConnected: true } || SelectedGroupRoom is null || string.IsNullOrWhiteSpace(_groupContinueEventId)) return;
        try
        {
            await _wsClient.GroupsContinueAsync(SelectedGroupRoom.RoomId, _groupContinueEventId);
            await PollActiveGroupRoomAsync();
        }
        catch (Exception ex)
        {
            GroupStatusNotice = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DisbandGroupRoomAsync()
    {
        if (_wsClient is not { IsConnected: true } || SelectedGroupRoom is null) return;
        try
        {
            await _wsClient.GroupsDisbandAsync(SelectedGroupRoom.RoomId);
            await RefreshGroupRoomsAsync();
        }
        catch (Exception ex)
        {
            GroupStatusNotice = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ApproveGroupActionAsync(GroupPendingApproval? approval)
    {
        if (_wsClient is not { IsConnected: true } || SelectedGroupRoom is null || approval is null) return;
        try
        {
            await _wsClient.GroupsApproveAsync(SelectedGroupRoom.RoomId, approval, "allow");
            await PollActiveGroupRoomAsync();
        }
        catch (Exception ex)
        {
            GroupStatusNotice = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RejectGroupActionAsync(GroupPendingApproval? approval)
    {
        if (_wsClient is not { IsConnected: true } || SelectedGroupRoom is null || approval is null) return;
        try
        {
            await _wsClient.GroupsApproveAsync(SelectedGroupRoom.RoomId, approval, "deny");
            await PollActiveGroupRoomAsync();
        }
        catch (Exception ex)
        {
            GroupStatusNotice = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenCreateGroupModal()
    {
        GroupCreateName = "";
        GroupCreateError = null;
        GroupCreateMemberChoices.Clear();
        var index = 0;
        foreach (var chip in ProfileChips)
        {
            GroupCreateMemberChoices.Add(new GroupBotChoiceViewModel(chip.Profile, isSelected: index < 2));
            index++;
        }
        if (GroupCreateMemberChoices.Count < 2)
        {
            GroupCreateError = AppLocalization.T(
                "Need at least 2 bot profiles configured on the Hermes host.",
                "需要至少配置 2 个智能体（Bots）才能组建多智能体群组。");
        }
        IsGroupCreateModalOpen = true;
    }

    [RelayCommand]
    private void CloseCreateGroupModal()
    {
        if (IsCreatingGroup) return;
        IsGroupCreateModalOpen = false;
    }

    [RelayCommand]
    private async Task ConfirmCreateGroupAsync()
    {
        if (_wsClient is not { IsConnected: true } || IsCreatingGroup) return;
        var selectedBots = GroupCreateMemberChoices.Where(c => c.IsSelected).ToList();
        if (selectedBots.Count < 2 || selectedBots.Count > 6)
        {
            GroupCreateError = AppLocalization.T("Please select between 2 and 6 bots.", "请选择 2 到 6 个智能体。");
            return;
        }
        var roomName = string.IsNullOrWhiteSpace(GroupCreateName)
            ? string.Join(" · ", selectedBots.Select(b => b.DisplayName))
            : GroupCreateName.Trim();

        var members = selectedBots.Select((b, idx) => new GroupMember(
            MemberId: $"m-{idx + 1}-{SanitizeHandle(b.Name)}",
            Profile: b.Name,
            Handle: SanitizeHandle(b.Name),
            DisplayName: b.DisplayName)).ToList();

        IsCreatingGroup = true;
        GroupCreateError = null;
        try
        {
            var created = await _wsClient.GroupsCreateAsync(Guid.NewGuid().ToString("N"), roomName, members);
            IsGroupCreateModalOpen = false;
            await RefreshGroupRoomsAsync();
            if (created is not null)
            {
                var match = GroupRooms.FirstOrDefault(r => r.RoomId == created.RoomId);
                if (match is not null) SelectedGroupRoom = match;
            }
        }
        catch (Exception ex)
        {
            GroupCreateError = ex.Message;
        }
        finally
        {
            IsCreatingGroup = false;
        }
    }

    [RelayCommand]
    private void OpenGroupSettingsModal()
    {
        if (SelectedGroupRoom is null) return;
        GroupSettingsError = null;
        GroupEditName = SelectedGroupRoom.Name;
        IsGroupSettingsModalOpen = true;
    }

    [RelayCommand]
    private void CloseGroupSettingsModal()
    {
        if (IsSavingGroupSettings) return;
        IsGroupSettingsModalOpen = false;
    }

    [RelayCommand]
    private async Task SaveGroupSettingsAsync()
    {
        if (_wsClient is not { IsConnected: true } || SelectedGroupRoom is null || IsSavingGroupSettings) return;
        IsSavingGroupSettings = true;
        GroupSettingsError = null;
        var roomId = SelectedGroupRoom.RoomId;
        try
        {
            var newName = GroupEditName.Trim();
            if (!string.IsNullOrWhiteSpace(newName)
                && !string.Equals(newName, SelectedGroupRoom.Name, StringComparison.Ordinal)
                && (_groupCapabilities?.Supports("groups.rename") ?? true))
            {
                await _wsClient.GroupsRenameAsync(roomId, newName);
            }

            await _wsClient.GroupsContextAsync(
                roomId,
                Guid.NewGuid().ToString("N"),
                GroupEditPurpose.Trim(),
                GroupEditNotes.Trim(),
                GroupEditRoutingMode);

            IsGroupSettingsModalOpen = false;
            await RefreshGroupRoomsAsync();
            var match = GroupRooms.FirstOrDefault(r => r.RoomId == roomId);
            if (match is not null) SelectedGroupRoom = match;
        }
        catch (Exception ex)
        {
            GroupSettingsError = ex.Message;
        }
        finally
        {
            IsSavingGroupSettings = false;
        }
    }

    private static string SanitizeHandle(string raw)
    {
        var sb = new StringBuilder();
        foreach (var ch in raw.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_') sb.Append(ch);
            else if (char.IsWhiteSpace(ch)) sb.Append('-');
        }
        return sb.Length > 0 ? sb.ToString() : "bot";
    }

    private void ResetGroupsAndSlashState()
    {
        _groupPollTimer?.Stop();
        _groupCapabilities = null;
        IsGroupsSupported = true;
        GroupRooms.Clear();
        GroupEvents.Clear();
        ActiveGroupMembers.Clear();
        GroupPendingApprovals.Clear();
        SelectedGroupRoom = null;
        GroupComposerText = "";
        GroupStatusNotice = null;
        GroupDriverBanner = "";
        GroupMemberSummaryText = "";
        IsGroupRunning = false;
        CanContinueGroupDiscussion = false;
        IsGroupCreateModalOpen = false;
        IsGroupSettingsModalOpen = false;
        IsTranscriptSelectionMode = false;
        TranscriptSelectionText = "";
        PendingRenameSession = null;
        AvailableModelProviders.Clear();
        AvailableModelsForSelectedProvider.Clear();
        _firstBotProvider = "";
        _firstBotModel = "";
        _slashCatalog = [.. SlashCommandParser.BuiltInCommands];
        SlashSuggestions.Clear();
        OnPropertyChanged(nameof(IsSlashSuggestionsVisible));
        OnPropertyChanged(nameof(HasGroupPendingApprovals));
        OnPropertyChanged(nameof(HasNoGroupRooms));
    }

    private void DisposeGroupsAndSlashResources()
    {
        _groupPollTimer?.Stop();
    }

    // ---- 8. Per-Bot API Provider & Model Configuration (Default First Bot Preset + Custom/New) ----

    public ObservableCollection<ModelProviderOption> AvailableModelProviders { get; } = [];
    public ObservableCollection<string> AvailableModelsForSelectedProvider { get; } = [];

    [ObservableProperty] private ModelProviderOption? _selectedModelProviderOption;
    [ObservableProperty] private bool _soulUseCustomProviderModel;
    [ObservableProperty] private bool _soulManualProviderInput;
    [ObservableProperty] private string _soulEditorProviderInput = "";
    [ObservableProperty] private string _soulEditorModelInput = "";
    [ObservableProperty] private string _soulFirstBotPresetSummary = "";
    [ObservableProperty] private string _botProvidersStatusSummary = "";
    [ObservableProperty] private bool _isLoadingBotModels;

    private string _soulOriginalProvider = "";
    private string _soulOriginalModel = "";
    private string _firstBotProvider = "";
    private string _firstBotModel = "";
    private bool _syncingProviderSelection;

    public bool HasAnyAvailableModelProviders => AvailableModelProviders.Count > 0;
    public bool HasModelsForSelectedProvider => AvailableModelsForSelectedProvider.Count > 0;
    public bool ShowBotDropdownModelPickers => SoulUseCustomProviderModel && !SoulManualProviderInput && AvailableModelProviders.Count > 0;
    public bool ShowBotDropdownModelBox => ShowBotDropdownModelPickers && HasModelsForSelectedProvider;
    public bool ShowBotCustomModelFallbackBox => ShowBotDropdownModelPickers && !HasModelsForSelectedProvider;
    public bool ShowBotManualModelInputs => SoulUseCustomProviderModel && (SoulManualProviderInput || AvailableModelProviders.Count == 0);

    partial void OnSoulUseCustomProviderModelChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowBotDropdownModelPickers));
        OnPropertyChanged(nameof(ShowBotDropdownModelBox));
        OnPropertyChanged(nameof(ShowBotCustomModelFallbackBox));
        OnPropertyChanged(nameof(ShowBotManualModelInputs));
    }

    partial void OnSoulManualProviderInputChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowBotDropdownModelPickers));
        OnPropertyChanged(nameof(ShowBotDropdownModelBox));
        OnPropertyChanged(nameof(ShowBotCustomModelFallbackBox));
        OnPropertyChanged(nameof(ShowBotManualModelInputs));
    }

    partial void OnSelectedModelProviderOptionChanged(ModelProviderOption? value)
    {
        if (value is null) return;
        if (!SoulManualProviderInput)
        {
            SoulEditorProviderInput = value.Slug;
        }

        AvailableModelsForSelectedProvider.Clear();
        foreach (var m in value.Models)
        {
            AvailableModelsForSelectedProvider.Add(m);
        }

        if (_syncingProviderSelection)
        {
            if (!string.IsNullOrWhiteSpace(SoulEditorModelInput)
                && !AvailableModelsForSelectedProvider.Contains(SoulEditorModelInput, StringComparer.OrdinalIgnoreCase)
                && AvailableModelsForSelectedProvider.Count > 0)
            {
                AvailableModelsForSelectedProvider.Insert(0, SoulEditorModelInput);
            }
            if (string.IsNullOrWhiteSpace(SoulEditorModelInput) && AvailableModelsForSelectedProvider.Count > 0)
            {
                SoulEditorModelInput = AvailableModelsForSelectedProvider[0];
            }
        }
        else
        {
            if (AvailableModelsForSelectedProvider.Count > 0)
            {
                if (string.IsNullOrWhiteSpace(SoulEditorModelInput)
                    || !AvailableModelsForSelectedProvider.Contains(SoulEditorModelInput, StringComparer.OrdinalIgnoreCase))
                {
                    SoulEditorModelInput = AvailableModelsForSelectedProvider[0];
                }
            }
            else
            {
                SoulEditorModelInput = "";
            }
        }

        OnPropertyChanged(nameof(HasModelsForSelectedProvider));
        OnPropertyChanged(nameof(ShowBotDropdownModelBox));
        OnPropertyChanged(nameof(ShowBotCustomModelFallbackBox));
    }

    [RelayCommand]
    private void SelectDefaultFirstBotModelMode()
    {
        SoulUseCustomProviderModel = false;
        SoulEditorProviderInput = _firstBotProvider;
        SoulEditorModelInput = _firstBotModel;
        SyncProviderSelectionFromStrings(_firstBotProvider, _firstBotModel);
    }

    [RelayCommand]
    private void SelectCustomBotModelMode()
    {
        SoulUseCustomProviderModel = true;
        if (string.IsNullOrWhiteSpace(SoulEditorModelInput) && !string.IsNullOrWhiteSpace(_firstBotModel))
        {
            SoulEditorProviderInput = _firstBotProvider;
            SoulEditorModelInput = _firstBotModel;
        }
        SyncProviderSelectionFromStrings(SoulEditorProviderInput, SoulEditorModelInput);
    }

    [RelayCommand]
    private async Task RefreshBotModelProvidersAsync()
    {
        await LoadBotModelProvidersAsync(refresh: true);
    }

    private void InitializeFirstBotProviderPreset()
    {
        var firstCreated = ProfileChips.FirstOrDefault(p =>
            !p.Profile.IsDefault
            && (!string.IsNullOrWhiteSpace(p.Model) || !string.IsNullOrWhiteSpace(p.Profile.Provider)))
            ?? ProfileChips.FirstOrDefault(p => !p.Profile.IsDefault)
            ?? ProfileChips.FirstOrDefault();

        _firstBotProvider = firstCreated?.Profile.Provider?.Trim() ?? "";
        _firstBotModel = firstCreated?.Model?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(_firstBotModel)
            && !string.IsNullOrWhiteSpace(CurrentModelLabel)
            && !string.Equals(CurrentModelLabel, "default", StringComparison.OrdinalIgnoreCase))
        {
            _firstBotModel = CurrentModelLabel;
        }

        UpdateFirstBotPresetSummary(firstCreated?.DisplayName);
        if (firstCreated is not null && string.IsNullOrWhiteSpace(_firstBotProvider))
        {
            _ = PopulateFirstBotDetailsFromServerAsync(firstCreated.Name, firstCreated.DisplayName);
        }
    }

    private void UpdateFirstBotPresetSummary(string? sourceName)
    {
        var prefix = string.IsNullOrWhiteSpace(sourceName) ? "" : $"{sourceName} · ";
        if (!string.IsNullOrWhiteSpace(_firstBotProvider) && !string.IsNullOrWhiteSpace(_firstBotModel))
        {
            SoulFirstBotPresetSummary = $"{prefix}{_firstBotProvider} / {_firstBotModel}";
        }
        else if (!string.IsNullOrWhiteSpace(_firstBotModel))
        {
            SoulFirstBotPresetSummary = $"{prefix}{_firstBotModel}";
        }
        else
        {
            SoulFirstBotPresetSummary = AppLocalization.T("Gateway default provider & model", "服务端默认提供商与模型");
        }
    }

    private async Task PopulateFirstBotDetailsFromServerAsync(string profileName, string? displayName)
    {
        if (_wsClient is not { IsConnected: true }) return;
        try
        {
            var details = await _wsClient.DescribeBotProfileAsync(profileName);
            if (!string.IsNullOrWhiteSpace(details.Provider)) _firstBotProvider = details.Provider.Trim();
            if (!string.IsNullOrWhiteSpace(details.Model)) _firstBotModel = details.Model.Trim();
            UpdateFirstBotPresetSummary(displayName);
            if (IsCreatingSoul && !SoulUseCustomProviderModel)
            {
                SoulEditorProviderInput = _firstBotProvider;
                SoulEditorModelInput = _firstBotModel;
                SyncProviderSelectionFromStrings(_firstBotProvider, _firstBotModel);
            }
        }
        catch
        {
            // Keep roster summary on failure
        }
    }

    private async Task LoadBotModelProvidersAsync(bool refresh)
    {
        if ((_wsClient is not { IsConnected: true } && _restClient is null) || IsLoadingBotModels) return;
        IsLoadingBotModels = true;
        BotProvidersStatusSummary = AppLocalization.T("Loading server providers...", "正在同步服务端 API 提供商...");
        try
        {
            var sessionId = _activeRuntimeSessionId ?? ActiveSession?.StoredSessionId ?? ActiveSession?.Id;
            var targetProfile = SelectedProfile?.Name;

            async Task<IReadOnlyList<ModelProviderOption>> LoadFromWsAsync()
            {
                if (_wsClient is not { IsConnected: true }) return [];
                try
                {
                    return await _wsClient.GetModelProvidersAsync(sessionId, refresh);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    HermesDesktop.Core.Diagnostics.AppDiagnostics.Log("MODELS_WARN", $"WS model.options failed: {ex.Message}");
                    return [];
                }
            }

            async Task<IReadOnlyList<ModelProviderOption>> LoadFromRestAsync()
            {
                if (_restClient is null) return [];
                try
                {
                    return await _restClient.GetServerDefinedProvidersAsync(targetProfile, refresh);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    HermesDesktop.Core.Diagnostics.AppDiagnostics.Log("MODELS_WARN", $"REST server providers failed: {ex.Message}");
                    return [];
                }
            }

            var wsTask = LoadFromWsAsync();
            var restTask = LoadFromRestAsync();
            await Task.WhenAll(wsTask, restTask);

            var merged = HermesDesktop.Core.Rpc.HermesWsClient.MergeModelProviders(wsTask.Result, restTask.Result);

            AvailableModelProviders.Clear();
            foreach (var p in merged)
            {
                AvailableModelProviders.Add(p);
            }

            BotProvidersStatusSummary = AvailableModelProviders.Count > 0
                ? AppLocalization.T($"Synced {AvailableModelProviders.Count} server providers", $"已同步服务端 {AvailableModelProviders.Count} 个 API 提供商")
                : AppLocalization.T("No server providers returned (manual entry enabled)", "服务端未返回提供商列表，可手动填写");

            OnPropertyChanged(nameof(HasAnyAvailableModelProviders));
            OnPropertyChanged(nameof(ShowBotDropdownModelPickers));
            OnPropertyChanged(nameof(ShowBotDropdownModelBox));
            OnPropertyChanged(nameof(ShowBotCustomModelFallbackBox));
            OnPropertyChanged(nameof(ShowBotManualModelInputs));

            SyncProviderSelectionFromStrings(SoulEditorProviderInput, SoulEditorModelInput);
        }
        catch (Exception ex)
        {
            HermesDesktop.Core.Diagnostics.AppDiagnostics.Log("MODELS_WARN", $"LoadBotModelProvidersAsync failed: {ex.Message}");
            BotProvidersStatusSummary = AppLocalization.T("Could not load server providers", "服务端 API 提供商加载失败");
        }
        finally
        {
            IsLoadingBotModels = false;
        }
    }

    private void SyncProviderSelectionFromStrings(string provider, string model)
    {
        if (AvailableModelProviders.Count == 0)
        {
            AvailableModelsForSelectedProvider.Clear();
            OnPropertyChanged(nameof(HasModelsForSelectedProvider));
            OnPropertyChanged(nameof(ShowBotDropdownModelBox));
            OnPropertyChanged(nameof(ShowBotCustomModelFallbackBox));
            return;
        }

        ModelProviderOption? match = null;
        if (!string.IsNullOrWhiteSpace(provider))
        {
            var norm = provider.Trim();
            var customNorm = norm.StartsWith("custom:", StringComparison.OrdinalIgnoreCase) ? norm : $"custom:{norm}";
            match = AvailableModelProviders.FirstOrDefault(p =>
                string.Equals(p.Slug, norm, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Slug, customNorm, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.DisplayName, norm, StringComparison.OrdinalIgnoreCase)
                || p.Aliases.Any(a => string.Equals(a, norm, StringComparison.OrdinalIgnoreCase)));
        }
        match ??= AvailableModelProviders.FirstOrDefault(p => p.IsCurrent) ?? AvailableModelProviders[0];

        _syncingProviderSelection = true;
        try
        {
            SelectedModelProviderOption = match;
            if (!string.IsNullOrWhiteSpace(model))
            {
                if (!AvailableModelsForSelectedProvider.Contains(model, StringComparer.OrdinalIgnoreCase))
                {
                    AvailableModelsForSelectedProvider.Insert(0, model);
                    OnPropertyChanged(nameof(HasModelsForSelectedProvider));
                    OnPropertyChanged(nameof(ShowBotDropdownModelBox));
                    OnPropertyChanged(nameof(ShowBotCustomModelFallbackBox));
                }
                SoulEditorModelInput = model;
            }
        }
        finally
        {
            _syncingProviderSelection = false;
        }
    }
}
