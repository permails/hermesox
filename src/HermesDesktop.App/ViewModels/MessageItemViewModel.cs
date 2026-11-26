using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HermesDesktop.App.Localization;
using HermesDesktop.Core.Localization;
using HermesDesktop.Core.Models;

namespace HermesDesktop.App.ViewModels;

public sealed partial class MessageItemViewModel : ObservableObject
{
    public string Id { get; }
    public MessageRole Role { get; }
    public string Timestamp { get; }
    public string? DisplayKind { get; }

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private string? _reasoning;

    [ObservableProperty]
    private bool _isReasoningExpanded;

    [ObservableProperty]
    private ToolActivity? _tool;

    [ObservableProperty]
    private bool _isToolExpanded;

    [ObservableProperty]
    private ApprovalActivity? _approval;

    [ObservableProperty]
    private string _approvalSteerText = string.Empty;

    [ObservableProperty]
    private ClarifyActivity? _clarify;

    [ObservableProperty]
    private string _clarifyCustomAnswer = string.Empty;

    [ObservableProperty]
    private DelegateAgentActivity? _delegateAgent;

    [ObservableProperty]
    private bool _isStreaming;

    [ObservableProperty]
    private bool _isProcessNarration;

    [ObservableProperty]
    private bool _isRunHeader;

    [ObservableProperty]
    private bool _isInStepRun;

    [ObservableProperty]
    private bool _isRunExpanded;

    [ObservableProperty]
    private bool _isRunLive;

    [ObservableProperty]
    private string _runSummaryText = string.Empty;

    [ObservableProperty]
    private bool _isHiddenInCollapsedRun;

    public string? RunGroupId { get; set; }

    public bool IsUser => Role == MessageRole.User;
    public bool HasVisibleContent => !string.IsNullOrWhiteSpace(Content);
    public bool HasAttachments => Attachments.Count > 0;
    public bool HasReasoning => !string.IsNullOrWhiteSpace(Reasoning);
    public bool IsAssistantBubble => (Role == MessageRole.Assistant || Role == MessageRole.Partial) && (HasVisibleContent || HasAttachments);
    public bool IsElevatedAssistantReply => IsAssistantBubble && !IsProcessNarration;
    public bool IsCollapsedProcessNarration => IsAssistantBubble && IsProcessNarration && !IsHiddenInCollapsedRun;
    public bool IsReasoningOnly => (Role == MessageRole.Reasoning && HasReasoning)
        || ((Role == MessageRole.Assistant || Role == MessageRole.Partial) && !HasVisibleContent && HasReasoning);
    public bool IsReasoningRowVisible => IsReasoningOnly && !IsHiddenInCollapsedRun;

    public bool IsSystem => Role == MessageRole.System;
    public bool IsTool => Role == MessageRole.Tool;
    public bool IsRichBoardCard => IsTool && string.Equals(Tool?.Name, "conduit_show", StringComparison.OrdinalIgnoreCase);
    public bool IsStandardTool => IsTool && !IsRichBoardCard;
    public bool IsStandardToolRowVisible => IsStandardTool && !IsHiddenInCollapsedRun;
    public bool IsApproval => Role == MessageRole.Approval;
    public bool IsClarify => Role == MessageRole.Clarify;
    public bool HasTool => Tool is not null;
    public bool IsToolRunning => Tool?.Status == ToolStatus.Running;
    public bool IsApprovalPending => Approval is { IsResolved: false, IsWithdrawn: false };
    public bool IsClarifyPending => Clarify is { IsResolved: false, IsExpired: false };
    public bool IsAnyPartVisible => !IsHiddenInCollapsedRun || IsRunHeader || IsUser || IsElevatedAssistantReply || IsRichBoardCard || IsApproval || IsClarify || IsSystem;
    public string RunChevron => IsRunExpanded ? "▾" : "▸";
    public string ProcessNarrationOneLine => CollapseWhitespace(Content);

    public string ReasoningPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Reasoning)) return "";
            var oneLine = Reasoning.Replace('\n', ' ').Replace('\r', ' ').Trim();
            return oneLine.Length > 72 ? oneLine[..72] + "…" : oneLine;
        }
    }

    public string ToolDisplayTitle
    {
        get
        {
            var rawName = Tool?.Name;
            if (rawName is "tool_call" or "multi_tool_use.parallel")
            {
                var (calls, _) = TranscriptRunHelper.ExtractBatchToolCalls(Tool?.Input);
                if (calls.Count == 1) return ResolveFriendlyToolName(calls[0]);
                if (calls.Count > 1)
                {
                    var distinct = calls.Select(ResolveFriendlyToolName).Distinct().Take(2);
                    return AppLocalization.T($"Batch tools ({string.Join(", ", distinct)})", $"批量工具 ({string.Join("、", distinct)})");
                }
                return AppLocalization.T("Batch tool call", "批量调用工具");
            }
            return ResolveFriendlyToolName(rawName);
        }
    }

    public static string ResolveFriendlyToolName(string? name) => name?.ToLowerInvariant() switch
    {
        null or "" => AppLocalization.T("Used a tool", "调用工具"),
        "shell" or "terminal" or "bash" or "exec" or "run_command" => AppLocalization.T("Ran a command", "运行命令"),
        "execute_code" or "run_python" or "python" => AppLocalization.T("Ran script", "执行脚本"),
        "process" or "process_manage" => AppLocalization.T("Managed process", "管理进程"),
        "read_file" or "write_file" or "edit_file" or "patch_file" or "list_dir" or "find_files" => AppLocalization.T("Worked with files", "处理文件"),
        "web_search" or "search_web" or "web_fetch" or "fetch_url" => AppLocalization.T("Searched the web", "搜索网页"),
        "browser" or "browser_navigate" => AppLocalization.T("Used the browser", "操作浏览器"),
        "memory" or "save_memory" or "search_memory" => AppLocalization.T("Checked memory", "读写记忆"),
        "delegate_task" or "subagent" => AppLocalization.T("Delegated task", "委派任务"),
        "tool_search" => AppLocalization.T("Found tools", "查找工具"),
        "tool_describe" => AppLocalization.T("Read tool docs", "读取工具文档"),
        "conduit_show" => AppLocalization.T("Rendered rich board", "展示结果看板"),
        _ => name!
    };

    public string ToolPreviewLine => FormatToolPreview(Tool?.Name, Tool?.Input);

    private static string FormatToolPreview(string? toolName, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string? extracted = null;
        if (toolName is "tool_call" or "multi_tool_use.parallel")
        {
            extracted = TranscriptRunHelper.ExtractBatchToolCalls(raw).PrimaryArg;
        }
        extracted ??= TryExtractPrimaryArgument(raw.Trim());
        var cleaned = SkipScriptBoilerplate(extracted);
        var collapsed = CollapseWhitespace(cleaned);
        return collapsed.Length > 84 ? collapsed[..84] + "…" : collapsed;
    }

    private static string SkipScriptBoilerplate(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var meaningful = new System.Collections.Generic.List<string>();
        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();
            if (trimmed.Length == 0) continue;
            if (trimmed.StartsWith("from hermes_tools import ", StringComparison.Ordinal) ||
                trimmed.StartsWith("import hermes_tools", StringComparison.Ordinal) ||
                trimmed.StartsWith("# -*- coding:", StringComparison.Ordinal) ||
                trimmed is "r\"\"\"" or "\"\"\"" or "'''")
            {
                continue;
            }
            if (trimmed.StartsWith("r = terminal(r\"\"\"", StringComparison.Ordinal))
                trimmed = trimmed["r = terminal(r\"\"\"".Length..].TrimStart();
            else if (trimmed.StartsWith("r = terminal(\"\"\"", StringComparison.Ordinal))
                trimmed = trimmed["r = terminal(\"\"\"".Length..].TrimStart();
            else if (trimmed.StartsWith("code = r\"\"\"", StringComparison.Ordinal))
                trimmed = trimmed["code = r\"\"\"".Length..].TrimStart();
            if (trimmed.Length > 0)
                meaningful.Add(trimmed);
            if (meaningful.Count >= 2) break;
        }
        return meaningful.Count > 0 ? string.Join(" ", meaningful) : text;
    }

    private static string TryExtractPrimaryArgument(string raw)
    {
        if (!raw.StartsWith('{')) return raw;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return raw;
            string[] preferredKeys = ["command", "cmd", "code", "script", "path", "file_path", "query", "url", "goal", "prompt", "name"];
            foreach (var key in preferredKeys)
            {
                if (doc.RootElement.TryGetProperty(key, out var val) && val.ValueKind == JsonValueKind.String)
                {
                    var s = val.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    var s = prop.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return $"{prop.Name}: {s}";
                }
            }
        }
        catch
        {
            // Fall back to raw text if truncated/streaming JSON
        }
        return raw;
    }

    private static string CollapseWhitespace(string text)
    {
        var oneLine = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ').Trim();
        while (oneLine.Contains("  ", StringComparison.Ordinal))
        {
            oneLine = oneLine.Replace("  ", " ", StringComparison.Ordinal);
        }
        return oneLine;
    }

    public string ReasoningChevron => IsReasoningExpanded ? "▾" : "▸";
    public string ToolChevron => IsToolExpanded ? "▾" : "▸";

    public ObservableCollection<ClarifyChoice> ClarifyChoices { get; } = [];
    public ObservableCollection<Attachment> Attachments { get; } = [];

    public MessageItemViewModel(ChatMessage model)
    {
        Id = model.Id;
        Role = model.Role;
        Timestamp = model.Timestamp;
        DisplayKind = model.DisplayKind;
        _content = model.Content;
        _reasoning = model.Reasoning;
        _tool = model.Tool;
        _approval = model.Approval;
        _clarify = model.Clarify;
        _delegateAgent = model.DelegateAgent;

        if (model.Clarify is not null)
        {
            foreach (var c in model.Clarify.Choices)
            {
                ClarifyChoices.Add(c);
            }
        }
        if (model.Attachments is not null)
        {
            foreach (var a in model.Attachments)
            {
                Attachments.Add(a);
            }
        }
    }

    [RelayCommand]
    private void ToggleReasoning() => IsReasoningExpanded = !IsReasoningExpanded;

    [RelayCommand]
    private void ToggleTool() => IsToolExpanded = !IsToolExpanded;

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(ToolDisplayTitle));
        OnPropertyChanged(nameof(RunSummaryText));
    }

    partial void OnContentChanged(string value)
    {
        OnPropertyChanged(nameof(HasVisibleContent));
        OnPropertyChanged(nameof(IsAssistantBubble));
        OnPropertyChanged(nameof(IsElevatedAssistantReply));
        OnPropertyChanged(nameof(IsCollapsedProcessNarration));
        OnPropertyChanged(nameof(ProcessNarrationOneLine));
        OnPropertyChanged(nameof(IsReasoningOnly));
        OnPropertyChanged(nameof(IsReasoningRowVisible));
    }

    partial void OnReasoningChanged(string? value)
    {
        OnPropertyChanged(nameof(HasReasoning));
        OnPropertyChanged(nameof(IsAssistantBubble));
        OnPropertyChanged(nameof(IsElevatedAssistantReply));
        OnPropertyChanged(nameof(IsReasoningOnly));
        OnPropertyChanged(nameof(IsReasoningRowVisible));
        OnPropertyChanged(nameof(ReasoningPreview));
    }

    partial void OnIsReasoningExpandedChanged(bool value) => OnPropertyChanged(nameof(ReasoningChevron));
    partial void OnIsToolExpandedChanged(bool value) => OnPropertyChanged(nameof(ToolChevron));

    partial void OnIsProcessNarrationChanged(bool value)
    {
        OnPropertyChanged(nameof(IsElevatedAssistantReply));
        OnPropertyChanged(nameof(IsCollapsedProcessNarration));
    }

    partial void OnIsRunHeaderChanged(bool value) => OnPropertyChanged(nameof(IsAnyPartVisible));
    partial void OnIsRunExpandedChanged(bool value) => OnPropertyChanged(nameof(RunChevron));

    partial void OnIsHiddenInCollapsedRunChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCollapsedProcessNarration));
        OnPropertyChanged(nameof(IsReasoningRowVisible));
        OnPropertyChanged(nameof(IsStandardToolRowVisible));
        OnPropertyChanged(nameof(IsAnyPartVisible));
    }

    partial void OnToolChanged(ToolActivity? value)
    {
        OnPropertyChanged(nameof(HasTool));
        OnPropertyChanged(nameof(IsToolRunning));
        OnPropertyChanged(nameof(IsRichBoardCard));
        OnPropertyChanged(nameof(IsStandardTool));
        OnPropertyChanged(nameof(IsStandardToolRowVisible));
        OnPropertyChanged(nameof(ToolDisplayTitle));
        OnPropertyChanged(nameof(ToolPreviewLine));
    }

    partial void OnApprovalChanged(ApprovalActivity? value) => OnPropertyChanged(nameof(IsApprovalPending));
    partial void OnClarifyChanged(ClarifyActivity? value) => OnPropertyChanged(nameof(IsClarifyPending));

    private CancellationTokenSource? _copiedCts;

    [ObservableProperty]
    private bool _isCopiedFeedback;

    public string CopyButtonLabel => IsCopiedFeedback ? LocStrings.Instance.CopiedBtn : LocStrings.Instance.CopyBtn;

    partial void OnIsCopiedFeedbackChanged(bool value) => OnPropertyChanged(nameof(CopyButtonLabel));

    public async void TriggerCopiedFeedback()
    {
        try
        {
            _copiedCts?.Cancel();
            _copiedCts?.Dispose();
            var cts = new CancellationTokenSource();
            _copiedCts = cts;
            IsCopiedFeedback = true;
            await Task.Delay(1800, cts.Token);
            if (!cts.IsCancellationRequested)
            {
                IsCopiedFeedback = false;
            }
        }
        catch
        {
            // Ignore cancellation/timer disposal
        }
    }
}
