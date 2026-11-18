using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HermesDesktop.App.Localization;
using HermesDesktop.Core.Auth;
using HermesDesktop.Core.Diagnostics;
using HermesDesktop.Core.Localization;
using HermesDesktop.Core.Models;
using HermesDesktop.Core.Rest;
using HermesDesktop.Core.Rpc;
using HermesDesktop.Core.Security;
using HermesDesktop.Core.Storage;
using HermesDesktop.Platform.Notifications;
using HermesDesktop.Platform.Security;

namespace HermesDesktop.App.ViewModels;

public sealed partial class V2TabOptionViewModel(V2Tab tab, bool isVisible, bool isSelected, bool canHide) : ObservableObject
{
    public V2Tab Tab { get; } = tab;
    public bool CanHide { get; } = canHide;

    [ObservableProperty] private bool _isVisible = isVisible;
    [ObservableProperty] private bool _isSelected = isSelected;

    public string Label => AppLocalization.TabLabel(Tab);
    public string VisibilityLabel => IsVisible ? AppLocalization.T("Shown", "显示") : AppLocalization.T("Hidden", "隐藏");
    partial void OnIsVisibleChanged(bool value) => OnPropertyChanged(nameof(VisibilityLabel));

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(VisibilityLabel));
    }
}

public sealed partial class ProfileOptionViewModel(BotProfile profile, bool isSelected) : ObservableObject
{
    public BotProfile Profile { get; } = profile;
    public string Name => Profile.Name;
    public string DisplayName => string.IsNullOrWhiteSpace(Profile.DisplayName) ? Profile.Name : Profile.DisplayName;
    public string? Model => Profile.Model;
    public string? Description => Profile.Description;
    public string ModelDisplay => string.IsNullOrWhiteSpace(Profile.Model) ? AppLocalization.T("Default model", "默认模型") : Profile.Model!;
    public string DescriptionDisplay => string.IsNullOrWhiteSpace(Profile.Description) ? AppLocalization.T("Self-hosted Hermes profile", "自托管 Hermes 配置文件") : Profile.Description!;
    public string Initial => DisplayName.Length > 0 ? DisplayName[..1].ToUpperInvariant() : "H";

    [ObservableProperty] private bool _isSelected = isSelected;

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(ModelDisplay));
        OnPropertyChanged(nameof(DescriptionDisplay));
    }
}

public sealed partial class CapabilityOptionViewModel : ObservableObject
{
    public string Name { get; }
    public string DisplayTitle { get; }
    public string Description { get; }
    public string? MetaLabel { get; }
    public bool IsLastToolsetGuard { get; set; }
    private readonly Action? _onToggled;

    [ObservableProperty] private bool _isEnabled;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public bool HasMeta => !string.IsNullOrWhiteSpace(MetaLabel);

    public CapabilityOptionViewModel(BotProfileCapability cap, Action? onToggled = null)
    {
        Name = cap.Name;
        DisplayTitle = string.IsNullOrWhiteSpace(cap.Label) ? cap.Name : cap.Label;
        Description = cap.Description;
        MetaLabel = cap.ToolCount.HasValue ? $"{cap.ToolCount.Value} tools" : null;
        _isEnabled = cap.Enabled;
        _onToggled = onToggled;
    }

    partial void OnIsEnabledChanging(bool value)
    {
        if (!value && IsLastToolsetGuard)
        {
            // Restore on next tick if user tried turning off the last remaining toolset
            Dispatcher.UIThread.Post(() => IsEnabled = true);
        }
    }

    partial void OnIsEnabledChanged(bool value) => _onToggled?.Invoke();
}

public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly LocalCacheStore _cache;
    private readonly ICredentialVault _vault;
    private readonly INotificationService _notifications;
    private readonly HermesAuthClient _authClient = new();

    private HermesRestClient? _restClient;
    private HermesWsClient? _wsClient;
    private AppSettingsData _settings;
    private V2TabLayout _tabLayout;
    private MessageItemViewModel? _streamingAssistantMessage;
    private readonly StringBuilder _streamContentBuffer = new();
    private readonly StringBuilder _streamReasoningBuffer = new();
    private readonly DispatcherTimer _streamFlushTimer;
    private readonly Dictionary<string, MessageItemViewModel> _activeTools = new(StringComparer.Ordinal);
    private string? _activeRuntimeSessionId;
    private string? _streamBufferSessionId;
    private bool _suppressSelectionOpen;
    private bool _suppressProfileRefresh;
    private int _sessionsRefreshGeneration;
    private int _openSessionGeneration;
    private readonly Dictionary<string, string> _previewHtmlCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte[]> _mediaBytesCache = new(StringComparer.Ordinal);
    private bool _botModeSupportsProtocol;
    private string _soulOriginalDescription = "";
    private string _soulOriginalText = "";
    private string _soulOriginalTitle = "";
    private bool _soulSlugEditedManually;

    public event Action<bool>? TranscriptScrollRequested;
    public event Action<bool>? BesideScrollRequested;
    public Func<string, Task<string?>> PreviewHtmlLoader => LoadPreviewHtmlAsync;
    public Func<string, Task<byte[]?>> MediaBytesLoader => LoadMediaBytesAsync;

    [ObservableProperty] private ConnectionPhase _connectionPhase = ConnectionPhase.Disconnected;
    [ObservableProperty] private string _statusBanner = "Connect to your self-hosted Hermes Dashboard";
    [ObservableProperty] private bool _showConnectOverlay = true;

    // Connect form fields
    [ObservableProperty] private string _connectLabel = "My Hermes";
    [ObservableProperty] private string _connectUrl = "http://localhost:9119";
    [ObservableProperty] private string _connectUsername = "";
    [ObservableProperty] private string _connectPassword = "";
    [ObservableProperty] private string _connectSessionToken = "";
    [ObservableProperty] private bool _useSessionTokenAuth;
    [ObservableProperty] private bool _useCloudflareAccess;
    [ObservableProperty] private string _cfClientId = "";
    [ObservableProperty] private string _cfClientSecret = "";
    [ObservableProperty] private bool _isAdvancedProxyExpanded;
    [ObservableProperty] private string? _connectError;
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private Guid? _editingDashboardId;

    // Navigation (V2Shell)
    [ObservableProperty] private V2Tab _selectedTab = V2Tab.Chats;
    [ObservableProperty] private bool _showAllProfiles;
    [ObservableProperty] private BotProfile? _selectedProfile;
    [ObservableProperty] private int _needsYouCount;

    // Sessions & Chat
    [ObservableProperty] private string _sessionSearchQuery = "";
    [ObservableProperty] private SessionSummary? _activeSession;
    [ObservableProperty] private SessionSummary? _pendingDeleteSession;
    [ObservableProperty] private bool _isDeletingSession;
    [ObservableProperty] private bool _isLoadingMessages;

    public bool IsConfirmingDeleteSession => PendingDeleteSession is not null;
    public string PendingDeleteSessionPrompt => PendingDeleteSession is null
        ? ""
        : $"{Loc.ConfirmDeleteSessionPrefix}{PendingDeleteSession.Title}{Loc.ConfirmDeleteSessionSuffix}";

    partial void OnPendingDeleteSessionChanged(SessionSummary? value)
    {
        OnPropertyChanged(nameof(IsConfirmingDeleteSession));
        OnPropertyChanged(nameof(PendingDeleteSessionPrompt));
    }
    [ObservableProperty] private bool _isRunningTurn;
    [ObservableProperty] private string _composerText = "";
    [ObservableProperty] private string _currentModelLabel = "default";
    [ObservableProperty] private double _contextPercent;
    [ObservableProperty] private int _contextUsed;
    [ObservableProperty] private int _contextMax;
    [ObservableProperty] private string? _currentCwd;

    // Beside Chat
    [ObservableProperty] private SessionSummary? _besideSession;
    [ObservableProperty] private string _besideComposerText = "";
    [ObservableProperty] private bool _isBesideVisible;

    // Tasks / Pulse / Artifacts
    [ObservableProperty] private string? _tasksStatusNotice;
    [ObservableProperty] private KanbanBoardSummary? _selectedKanbanBoard;
    [ObservableProperty] private string? _pulseStatusNotice;
    [ObservableProperty] private string? _artifactsStatusNotice;

    // Preview Modal (Text), In-App HTML Preview Side Pane & Image Lightbox
    [ObservableProperty] private bool _isPreviewModalOpen;
    [ObservableProperty] private string _previewTitle = "";
    [ObservableProperty] private string _previewText = "";

    [ObservableProperty] private bool _isImageViewerOpen;
    [ObservableProperty] private string _imageViewerTitle = "";
    [ObservableProperty] private string _imageViewerUri = "";
    [ObservableProperty] private Bitmap? _imageViewerBitmap;

    [ObservableProperty] private bool _isHtmlPreviewPaneOpen;
    [ObservableProperty] private string _htmlPreviewTitle = "";
    [ObservableProperty] private string _htmlPreviewFilePath = "";
    [ObservableProperty] private string _htmlPreviewContent = "";
    [ObservableProperty] private double _htmlPreviewZoom = 0.85;

    // Multi-Soul / Profile SOUL.md Editor & Creator Modal
    [ObservableProperty] private bool _isSoulEditorOpen;
    [ObservableProperty] private bool _isCreatingSoul;
    [ObservableProperty] private bool _isSoulEditorBusy;
    [ObservableProperty] private bool _isConfirmingDeleteProfile;
    [ObservableProperty] private string _soulEditorTitleInput = "";
    [ObservableProperty] private string _soulEditorSlugInput = "";
    [ObservableProperty] private string _soulEditorDescription = "";
    [ObservableProperty] private string _soulEditorText = "";
    [ObservableProperty] private string _soulEditorCloneFrom = "(Fresh profile)";
    [ObservableProperty] private bool _soulEditorNoSkills;
    [ObservableProperty] private bool _soulEditorShareAuth = true;
    [ObservableProperty] private string? _soulEditorError;
    [ObservableProperty] private string? _soulEditorNotice;
    [ObservableProperty] private int _soulEditorSubTabIndex;

    public ObservableCollection<string> SoulCloneOptions { get; } = ["(Fresh profile)"];
    public ObservableCollection<CapabilityOptionViewModel> EditingSkills { get; } = [];
    public ObservableCollection<CapabilityOptionViewModel> EditingToolsets { get; } = [];
    public ObservableCollection<CapabilityOptionViewModel> EditingMcpServers { get; } = [];

    private HashSet<string> _baselineDisabledSkills = new(StringComparer.Ordinal);
    private HashSet<string> _baselineEnabledToolsets = new(StringComparer.Ordinal);
    private HashSet<string> _baselineEnabledMcpServers = new(StringComparer.Ordinal);
    private bool _loadedCapabilitiesFromRestOnly;

    // Cron Job Editor Modal
    [ObservableProperty] private bool _isCronEditorOpen;
    [ObservableProperty] private bool _isCreatingCronJob;
    [ObservableProperty] private bool _isCronEditorBusy;
    [ObservableProperty] private bool _isConfirmingDeleteCronJob;
    [ObservableProperty] private string _cronEditorJobId = "";
    [ObservableProperty] private string _cronEditorName = "";
    [ObservableProperty] private string _cronEditorSchedule = "0 9 * * *";
    [ObservableProperty] private string _cronEditorPrompt = "";
    [ObservableProperty] private string _cronEditorDeliver = "local";
    [ObservableProperty] private string? _cronEditorError;

    // Hermes Server Update & Gateway Maintenance
    [ObservableProperty] private string _hermesVersionLabel = "—";
    [ObservableProperty] private string _hermesUpdateStatusText = "";
    [ObservableProperty] private string? _hermesUpdateDetailText;
    [ObservableProperty] private string? _hermesUpdateManualCommand;
    [ObservableProperty] private string? _hermesUpdateCommitsSummary;
    [ObservableProperty] private bool _isCheckingHermesUpdate;
    [ObservableProperty] private bool _isHermesUpdateRunning;
    [ObservableProperty] private bool _canApplyHermesUpdate;
    [ObservableProperty] private bool _hasHermesUpdateConfirm;
    [ObservableProperty] private bool _isGatewayRestartRunning;
    [ObservableProperty] private string? _gatewayMaintainStatusText;

    private CancellationTokenSource? _serverMaintenanceCts;
    private static readonly double[] HtmlZoomSteps = [0.50, 0.65, 0.75, 0.85, 1.00, 1.15, 1.30, 1.50];

    public bool IsHtmlPreviewWebViewVisible => IsHtmlPreviewPaneOpen && !ShowConnectOverlay && !IsPreviewModalOpen && !IsImageViewerOpen && !IsSoulEditorOpen && !IsCronEditorOpen;
    public string HtmlPreviewZoomLabel => $"{Math.Round(HtmlPreviewZoom * 100):0}%";
    public LocStrings Loc => LocStrings.Instance;
    public bool IsSystemLanguage => AppLocalization.Mode == AppLanguageMode.System;
    public bool IsEnglishLanguage => AppLocalization.Mode == AppLanguageMode.English;
    public bool IsChineseLanguage => AppLocalization.Mode == AppLanguageMode.Chinese;
    public bool IsSystemTheme => (_settings.ThemePreference ?? "system").Equals("system", StringComparison.OrdinalIgnoreCase);
    public bool IsDarkTheme => (_settings.ThemePreference ?? "system").Equals("dark", StringComparison.OrdinalIgnoreCase);
    public bool IsLightTheme => (_settings.ThemePreference ?? "system").Equals("light", StringComparison.OrdinalIgnoreCase);
    public bool IsSoulPersonalitySubTab => IsCreatingSoul || SoulEditorSubTabIndex == 0;
    public bool IsSoulCapabilitiesSubTab => !IsCreatingSoul && SoulEditorSubTabIndex == 1;
    public bool HasEditingSkills => EditingSkills.Count > 0;
    public bool HasEditingToolsets => EditingToolsets.Count > 0;
    public bool HasEditingMcpServers => EditingMcpServers.Count > 0;
    public bool HasNoEditingCapabilities => EditingSkills.Count == 0 && EditingToolsets.Count == 0 && EditingMcpServers.Count == 0;
    public string CronEditorHeaderTitle => IsCreatingCronJob
        ? AppLocalization.T("New Scheduled Job", "新建定时任务")
        : AppLocalization.T($"Edit Job · {CronEditorName}", $"编辑定时任务 · {CronEditorName}");
    public string CronEditorPrimaryActionLabel => IsCreatingCronJob
        ? AppLocalization.T("Create Job", "创建任务")
        : AppLocalization.T("Save Changes", "保存修改");
    public string SoulEditorHeaderTitle => IsCreatingSoul
        ? AppLocalization.T("New Bot / Multi-Soul", "新建智能体 / 多 Soul")
        : AppLocalization.T($"Edit SOUL.md · @{SoulEditorSlugInput}", $"编辑 SOUL.md · @{SoulEditorSlugInput}");
    public string SoulEditorPrimaryActionLabel => IsCreatingSoul
        ? AppLocalization.T("Create Soul & Profile", "创建 Soul 与配置")
        : AppLocalization.T("Save SOUL.md", "保存 SOUL.md");
    public bool CanDeleteEditingProfile => !IsCreatingSoul && !string.Equals(SoulEditorSlugInput, "default", StringComparison.OrdinalIgnoreCase);
    public string SoulCharCountLabel => AppLocalization.T(
        $"{SoulEditorText.Length} chars · {(string.IsNullOrEmpty(SoulEditorText) ? 0 : SoulEditorText.Split('\n').Length)} lines",
        $"{SoulEditorText.Length} 字符 · {(string.IsNullOrEmpty(SoulEditorText) ? 0 : SoulEditorText.Split('\n').Length)} 行");
    public string ActiveProfileDisplayLabel => SelectedProfile is null ? "default" : $"{SelectedProfile.DisplayName} (@{SelectedProfile.Name})";
    public string ActiveReplyAuthorName
    {
        get
        {
            var profileName = ActiveSession?.Profile;
            var profile = !string.IsNullOrWhiteSpace(profileName)
                ? ProfileChips.FirstOrDefault(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase))?.Profile
                : SelectedProfile;
            return !string.IsNullOrWhiteSpace(profile?.DisplayName)
                ? profile!.DisplayName
                : (string.IsNullOrWhiteSpace(profileName) ? "Hermes" : profileName);
        }
    }
    public string ActiveReplyAuthorInitial => ActiveReplyAuthorName.Length > 0 ? ActiveReplyAuthorName[..1].ToUpperInvariant() : "H";
    public string ActiveSessionDisplayTitle => ActiveSession?.Title ?? Loc.SelectOrStartChat;
    public string BesideSessionDisplayTitle => BesideSession?.Title ?? Loc.BesideChatFallback;

    public event Action<string>? HtmlPreviewContentLoaded;
    public event Action<double>? HtmlPreviewZoomChanged;

    public string DataDirectoryPath => AppDataPaths.GetDataDirectory();
    public bool IsPortableMode => AppDataPaths.IsPortableMode;

    public ObservableCollection<V2TabOptionViewModel> VisibleTabs { get; } = [];
    public ObservableCollection<V2TabOptionViewModel> AllConfigurableTabs { get; } = [];
    public ObservableCollection<SavedDashboard> SavedDashboards { get; } = [];
    public ObservableCollection<ProfileOptionViewModel> ProfileChips { get; } = [];
    public ObservableCollection<SessionSummary> FilteredSessions { get; } = [];
    public ObservableCollection<MessageItemViewModel> ActiveMessages { get; } = [];
    public ObservableCollection<MessageItemViewModel> BesideMessages { get; } = [];
    public ObservableCollection<CronJobSummary> CronJobs { get; } = [];
    public ObservableCollection<KanbanBoardSummary> KanbanBoards { get; } = [];
    public ObservableCollection<KanbanTaskSummary> KanbanTasks { get; } = [];
    public ObservableCollection<PulseItemSummary> PulseItems { get; } = [];
    public ObservableCollection<ArtifactSummary> ArtifactItems { get; } = [];

    private readonly List<SessionSummary> _allSessions = [];
    private readonly HashSet<string> _userExpandedRunIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _userCollapsedRunIds = new(StringComparer.Ordinal);
    private SavedDashboard? _activeDashboard;
    private DateTimeOffset _lastActiveStreamEventAt = DateTimeOffset.MinValue;
    private CancellationTokenSource? _turnWatchdogCts;

    public bool IsConnected => ConnectionPhase == ConnectionPhase.Connected;
    public bool IsServerMaintenanceIdle => (_restClient is not null || _activeDashboard is not null)
        && !IsCheckingHermesUpdate && !IsHermesUpdateRunning && !IsGatewayRestartRunning;
    public bool ShowUpdateHermesButton => CanApplyHermesUpdate && !HasHermesUpdateConfirm;
    public bool HasSavedDashboards => SavedDashboards.Count > 0;
    public bool HasNoSavedDashboards => SavedDashboards.Count == 0;
    public bool IsEditingSavedDashboard => EditingDashboardId.HasValue;
    public string ConnectPrimaryActionLabel => IsEditingSavedDashboard ? Loc.SaveAndConnect : Loc.ConnectServer;
    public string EditingServerBanner => IsEditingSavedDashboard ? $"{Loc.EditingServerPrefix} {ConnectLabel}" : "";
    public bool IsChatsTab => SelectedTab == V2Tab.Chats;
    public bool IsBotsTab => SelectedTab == V2Tab.Bots;
    public bool IsTasksTab => SelectedTab == V2Tab.Tasks;
    public bool IsPulseTab => SelectedTab == V2Tab.Pulse;
    public bool IsArtifactsTab => SelectedTab == V2Tab.Artifacts;
    public bool IsSettingsTab => SelectedTab == V2Tab.Settings;
    public bool HasActiveSession => ActiveSession is not null;
    public bool HasContextMeter => ContextMax > 0;
    public string ContextMeterText => ContextMax > 0 ? $"{ContextPercent:0.#}% · {ContextUsed:N0}/{ContextMax:N0}" : "";

    public MainViewModel(LocalCacheStore cache, ICredentialVault vault, INotificationService notifications)
    {
        _cache = cache;
        _vault = vault;
        _notifications = notifications;
        _settings = _cache.LoadSettings();
        AppLocalization.SetPreferenceToken(_settings.LanguagePreference);
        ApplyThemePreference(_settings.ThemePreference, save: false);
        AppLocalization.LanguageChanged += OnLanguageChanged;
        _statusBanner = AppLocalization.T("Connect to your self-hosted Hermes Dashboard", "连接到你的自托管 Hermes 网关");
        _tabLayout = new V2TabLayout(_settings.V2TabTokens);
        _streamFlushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _streamFlushTimer.Tick += (_, _) => FlushStreamingDeltas();

        SelectedTab = _tabLayout.Shown.Contains(V2Tab.Chats)
            ? V2Tab.Chats
            : _tabLayout.Shown.FirstOrDefault(V2Tab.Chats);
        RebuildTabCollections();

        foreach (var d in _cache.LoadDashboards())
        {
            SavedDashboards.Add(d);
        }
        SavedDashboards.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSavedDashboards));
            OnPropertyChanged(nameof(HasNoSavedDashboards));
        };

        if (_settings.ActiveDashboardId.HasValue)
        {
            var last = SavedDashboards.FirstOrDefault(d => d.Id == _settings.ActiveDashboardId.Value);
            if (last is not null)
            {
                _ = TryReconnectSavedAsync(last);
            }
        }
    }

    partial void OnSelectedTabChanged(V2Tab value)
    {
        OnPropertyChanged(nameof(IsChatsTab));
        OnPropertyChanged(nameof(IsBotsTab));
        OnPropertyChanged(nameof(IsTasksTab));
        OnPropertyChanged(nameof(IsPulseTab));
        OnPropertyChanged(nameof(IsArtifactsTab));
        OnPropertyChanged(nameof(IsSettingsTab));

        foreach (var item in VisibleTabs)
        {
            item.IsSelected = item.Tab == value;
        }

        if (value == V2Tab.Tasks) _ = RefreshTasksTabAsync();
        else if (value == V2Tab.Pulse) _ = RefreshPulseTabAsync();
        else if (value == V2Tab.Artifacts) _ = RefreshArtifactsTabAsync();
        else if (value == V2Tab.Settings && IsConnected && !IsCheckingHermesUpdate && !IsHermesUpdateRunning && string.IsNullOrEmpty(HermesUpdateStatusText))
            _ = CheckHermesUpdateInternalAsync(force: false);
    }

    partial void OnConnectionPhaseChanged(ConnectionPhase value)
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsServerMaintenanceIdle));
    }

    partial void OnIsCheckingHermesUpdateChanged(bool value) => OnPropertyChanged(nameof(IsServerMaintenanceIdle));
    partial void OnIsHermesUpdateRunningChanged(bool value) => OnPropertyChanged(nameof(IsServerMaintenanceIdle));
    partial void OnIsGatewayRestartRunningChanged(bool value) => OnPropertyChanged(nameof(IsServerMaintenanceIdle));
    partial void OnCanApplyHermesUpdateChanged(bool value) => OnPropertyChanged(nameof(ShowUpdateHermesButton));
    partial void OnHasHermesUpdateConfirmChanged(bool value) => OnPropertyChanged(nameof(ShowUpdateHermesButton));
    partial void OnShowConnectOverlayChanged(bool value) => OnPropertyChanged(nameof(IsHtmlPreviewWebViewVisible));
    partial void OnIsPreviewModalOpenChanged(bool value) => OnPropertyChanged(nameof(IsHtmlPreviewWebViewVisible));
    partial void OnIsImageViewerOpenChanged(bool value) => OnPropertyChanged(nameof(IsHtmlPreviewWebViewVisible));
    partial void OnIsHtmlPreviewPaneOpenChanged(bool value) => OnPropertyChanged(nameof(IsHtmlPreviewWebViewVisible));
    partial void OnIsSoulEditorOpenChanged(bool value) => OnPropertyChanged(nameof(IsHtmlPreviewWebViewVisible));
    partial void OnIsCronEditorOpenChanged(bool value) => OnPropertyChanged(nameof(IsHtmlPreviewWebViewVisible));
    partial void OnIsCreatingCronJobChanged(bool value)
    {
        OnPropertyChanged(nameof(CronEditorHeaderTitle));
        OnPropertyChanged(nameof(CronEditorPrimaryActionLabel));
    }
    partial void OnCronEditorNameChanged(string value) => OnPropertyChanged(nameof(CronEditorHeaderTitle));
    partial void OnSoulEditorSubTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsSoulPersonalitySubTab));
        OnPropertyChanged(nameof(IsSoulCapabilitiesSubTab));
    }
    partial void OnIsCreatingSoulChanged(bool value)
    {
        OnPropertyChanged(nameof(SoulEditorHeaderTitle));
        OnPropertyChanged(nameof(SoulEditorPrimaryActionLabel));
        OnPropertyChanged(nameof(CanDeleteEditingProfile));
        OnPropertyChanged(nameof(IsSoulPersonalitySubTab));
        OnPropertyChanged(nameof(IsSoulCapabilitiesSubTab));
    }
    partial void OnSoulEditorSlugInputChanged(string value)
    {
        OnPropertyChanged(nameof(SoulEditorHeaderTitle));
        OnPropertyChanged(nameof(CanDeleteEditingProfile));
    }
    partial void OnSoulEditorTitleInputChanged(string value)
    {
        if (IsCreatingSoul && !_soulSlugEditedManually)
        {
            SoulEditorSlugInput = BotSoulHelper.ToSlug(value);
        }
    }
    partial void OnSoulEditorTextChanged(string value) => OnPropertyChanged(nameof(SoulCharCountLabel));
    partial void OnHtmlPreviewZoomChanged(double value)
    {
        OnPropertyChanged(nameof(HtmlPreviewZoomLabel));
        HtmlPreviewZoomChanged?.Invoke(value);
    }

    partial void OnActiveSessionChanged(SessionSummary? value)
    {
        OnPropertyChanged(nameof(HasActiveSession));
        OnPropertyChanged(nameof(ActiveSessionDisplayTitle));
        OnPropertyChanged(nameof(ActiveReplyAuthorName));
        OnPropertyChanged(nameof(ActiveReplyAuthorInitial));
        if (!_suppressSelectionOpen && value is not null)
        {
            _ = OpenSessionAsync(value);
        }
    }

    partial void OnBesideSessionChanged(SessionSummary? value) => OnPropertyChanged(nameof(BesideSessionDisplayTitle));

    partial void OnSelectedKanbanBoardChanged(KanbanBoardSummary? value)
    {
        if (value is not null) _ = LoadKanbanTasksAsync(value);
    }

    partial void OnContextMaxChanged(int value)
    {
        OnPropertyChanged(nameof(HasContextMeter));
        OnPropertyChanged(nameof(ContextMeterText));
    }
    partial void OnContextPercentChanged(double value) => OnPropertyChanged(nameof(ContextMeterText));
    partial void OnContextUsedChanged(int value) => OnPropertyChanged(nameof(ContextMeterText));
    partial void OnSessionSearchQueryChanged(string value) => ApplySessionFilter();

    partial void OnShowAllProfilesChanged(bool value)
    {
        UpdateProfileChipSelection();
        _ = RefreshSessionsAsync();
    }

    partial void OnSelectedProfileChanged(BotProfile? value)
    {
        OnPropertyChanged(nameof(ActiveProfileDisplayLabel));
        OnPropertyChanged(nameof(ActiveReplyAuthorName));
        OnPropertyChanged(nameof(ActiveReplyAuthorInitial));
        UpdateProfileChipSelection();
        if (value is not null && !ShowAllProfiles)
        {
            _settings.ActiveProfile = value.Name;
            _cache.SaveSettings(_settings);
            if (!_suppressProfileRefresh)
            {
                _ = RefreshSessionsAsync();
            }
        }
    }

    partial void OnComposerTextChanged(string value)
    {
        if (ActiveSession is not null)
        {
            _cache.SaveDraft(DraftKey(ActiveSession.Id), value);
        }
    }

    private void UpdateProfileChipSelection()
    {
        foreach (var chip in ProfileChips)
        {
            chip.IsSelected = !ShowAllProfiles && SelectedProfile?.Name == chip.Name;
        }
    }

    [RelayCommand]
    private void SelectTab(V2Tab tab) => SelectedTab = tab;

    [RelayCommand]
    private void SelectProfile(ProfileOptionViewModel chip)
    {
        ShowAllProfiles = false;
        SelectedProfile = chip.Profile;
    }

    [RelayCommand]
    private void ToggleAllProfiles() => ShowAllProfiles = !ShowAllProfiles;

    [RelayCommand]
    private void SelectPasswordAuth() => UseSessionTokenAuth = false;

    [RelayCommand]
    private void SelectSessionTokenAuth() => UseSessionTokenAuth = true;

    [RelayCommand]
    private void ToggleAdvancedProxy() => IsAdvancedProxyExpanded = !IsAdvancedProxyExpanded;

    [RelayCommand]
    private void OpenConnectPanel()
    {
        ConnectError = null;
        ShowConnectOverlay = true;
    }

    [RelayCommand]
    private void CloseConnectPanel()
    {
        if (IsConnected) ShowConnectOverlay = false;
    }

    [RelayCommand]
    private void ClosePreviewModal() => IsPreviewModalOpen = false;

    partial void OnEditingDashboardIdChanged(Guid? value)
    {
        OnPropertyChanged(nameof(IsEditingSavedDashboard));
        OnPropertyChanged(nameof(ConnectPrimaryActionLabel));
        OnPropertyChanged(nameof(EditingServerBanner));
    }

    partial void OnConnectLabelChanged(string value)
    {
        if (IsEditingSavedDashboard) OnPropertyChanged(nameof(EditingServerBanner));
    }

    [RelayCommand]
    private void EditSavedDashboard(SavedDashboard dashboard)
    {
        ConnectError = null;
        EditingDashboardId = dashboard.Id;
        var secrets = _vault.LoadSecrets(dashboard.Id);

        ConnectLabel = dashboard.Label;
        ConnectUrl = dashboard.NormalizedUrl;
        UseSessionTokenAuth = dashboard.AuthMode == AuthMode.SessionToken;
        ConnectUsername = dashboard.Username ?? secrets?.Username ?? "";
        ConnectPassword = secrets?.Password ?? "";
        ConnectSessionToken = secrets?.SessionToken ?? "";
        UseCloudflareAccess = dashboard.UseCloudflareAccess;
        CfClientId = secrets?.CfAccessClientId ?? "";
        CfClientSecret = secrets?.CfAccessClientSecret ?? "";
        IsAdvancedProxyExpanded = dashboard.UseCloudflareAccess;
    }

    [RelayCommand]
    private void CancelEditSavedDashboard()
    {
        ConnectError = null;
        EditingDashboardId = null;
        ConnectLabel = "My Hermes";
        ConnectUrl = "http://localhost:9119";
        ConnectUsername = "";
        ConnectPassword = "";
        ConnectSessionToken = "";
        UseSessionTokenAuth = false;
        UseCloudflareAccess = false;
        CfClientId = "";
        CfClientSecret = "";
        IsAdvancedProxyExpanded = false;
    }

    [RelayCommand]
    private async Task ConnectNewAsync()
    {
        ConnectError = null;
        IsConnecting = true;
        ConnectionPhase = ConnectionPhase.Authenticating;

        try
        {
            var normalized = ConnectionURLPolicy.NormalizeBaseUrl(ConnectUrl);
            var targetId = EditingDashboardId ?? Guid.NewGuid();
            var dashboard = new SavedDashboard
            {
                Id = targetId,
                Label = string.IsNullOrWhiteSpace(ConnectLabel) ? new Uri(normalized).Host : ConnectLabel.Trim(),
                NormalizedUrl = normalized,
                Username = string.IsNullOrWhiteSpace(ConnectUsername) ? null : ConnectUsername.Trim(),
                AuthMode = UseSessionTokenAuth ? AuthMode.SessionToken : AuthMode.Password,
                UseCloudflareAccess = UseCloudflareAccess,
                LastConnectedAt = DateTimeOffset.UtcNow
            };

            var secrets = new DashboardSecrets
            {
                Username = dashboard.Username,
                Password = UseSessionTokenAuth ? null : ConnectPassword,
                SessionToken = UseSessionTokenAuth ? ConnectSessionToken.Trim() : null,
                CfAccessClientId = UseCloudflareAccess ? CfClientId.Trim() : null,
                CfAccessClientSecret = UseCloudflareAccess ? CfClientSecret.Trim() : null
            };

            await EstablishConnectionAsync(dashboard, secrets);

            _cache.SaveDashboard(dashboard);
            _vault.SaveSecrets(dashboard.Id, secrets);

            var existingIdx = -1;
            for (var i = 0; i < SavedDashboards.Count; i++)
            {
                if (SavedDashboards[i].Id == dashboard.Id)
                {
                    existingIdx = i;
                    break;
                }
            }
            if (existingIdx >= 0) SavedDashboards[existingIdx] = dashboard;
            else SavedDashboards.Insert(0, dashboard);

            _settings.ActiveDashboardId = dashboard.Id;
            _cache.SaveSettings(_settings);

            EditingDashboardId = null;
            ShowConnectOverlay = false;
        }
        catch (Exception ex)
        {
            ConnectionPhase = ConnectionPhase.Error;
            ConnectError = ex.Message;
            StatusBanner = $"Connection failed: {ex.Message}";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private async Task ConnectSavedDashboardAsync(SavedDashboard dashboard) => await TryReconnectSavedAsync(dashboard);

    [RelayCommand]
    private void RemoveSavedDashboard(SavedDashboard dashboard)
    {
        _cache.DeleteDashboard(dashboard.Id);
        _vault.DeleteSecrets(dashboard.Id);
        SavedDashboards.Remove(dashboard);
        if (EditingDashboardId == dashboard.Id) CancelEditSavedDashboard();
    }

    private async Task TryReconnectSavedAsync(SavedDashboard dashboard)
    {
        var secrets = _vault.LoadSecrets(dashboard.Id);
        if (secrets is null)
        {
            ConnectUrl = dashboard.NormalizedUrl;
            ConnectLabel = dashboard.Label;
            ConnectUsername = dashboard.Username ?? "";
            ShowConnectOverlay = true;
            return;
        }

        IsConnecting = true;
        ConnectionPhase = ConnectionPhase.Connecting;
        ConnectError = null;
        try
        {
            await EstablishConnectionAsync(dashboard, secrets);
            _settings.ActiveDashboardId = dashboard.Id;
            _cache.SaveSettings(_settings);
            ShowConnectOverlay = false;
        }
        catch (Exception ex)
        {
            ConnectionPhase = ConnectionPhase.Error;
            ConnectError = ex.Message;
            StatusBanner = $"Reconnect failed: {ex.Message}";
            ShowConnectOverlay = true;
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private async Task EstablishConnectionAsync(SavedDashboard dashboard, DashboardSecrets secrets)
    {
        AuthSessionTicket ticket;
        if (dashboard.AuthMode == AuthMode.Password && !string.IsNullOrEmpty(secrets.Password))
        {
            ticket = await _authClient.ConnectWithPasswordAsync(
                dashboard.NormalizedUrl,
                secrets.Username ?? "",
                secrets.Password,
                secrets);
        }
        else
        {
            ticket = await _authClient.ConnectWithTokenAsync(dashboard.NormalizedUrl, secrets);
        }

        if (_wsClient is not null)
        {
            _wsClient.EventReceived -= OnWsEventReceived;
            _wsClient.Disconnected -= OnWsDisconnected;
            await _wsClient.DisposeAsync();
        }
        _restClient?.Dispose();

        var dashboardChanged = _activeDashboard?.Id != dashboard.Id;
        _activeDashboard = dashboard;
        _restClient = new HermesRestClient(dashboard.NormalizedUrl, ticket, secrets);
        if (dashboardChanged)
        {
            _serverMaintenanceCts?.Cancel();
            HermesVersionLabel = "—";
            HermesUpdateStatusText = "";
            HermesUpdateDetailText = null;
            HermesUpdateManualCommand = null;
            HermesUpdateCommitsSummary = null;
            CanApplyHermesUpdate = false;
            HasHermesUpdateConfirm = false;
            GatewayMaintainStatusText = null;
        }
        OnPropertyChanged(nameof(IsServerMaintenanceIdle));
        _wsClient = new HermesWsClient();
        _wsClient.EventReceived += OnWsEventReceived;
        _wsClient.Disconnected += OnWsDisconnected;

        await _wsClient.ConnectAsync(dashboard.NormalizedUrl, ticket, secrets);

        ConnectionPhase = ConnectionPhase.Connected;
        StatusBanner = $"Connected · {dashboard.Label}";

        await RefreshProfilesAndSessionsAsync();
    }

    [RelayCommand]
    private async Task RefreshProfilesAndSessionsAsync()
    {
        if (_restClient is null) return;
        try
        {
            IReadOnlyList<BotProfile> profiles;
            if (_wsClient is { IsConnected: true })
            {
                try
                {
                    var roster = await _wsClient.GetBotRosterAsync();
                    profiles = roster.Profiles;
                    _botModeSupportsProtocol = roster.SupportsBotProtocol;
                }
                catch
                {
                    profiles = await _restClient.GetProfilesAsync();
                }
            }
            else
            {
                profiles = await _restClient.GetProfilesAsync();
            }

            ProfileChips.Clear();
            SoulCloneOptions.Clear();
            SoulCloneOptions.Add("(Fresh profile)");
            foreach (var p in profiles)
            {
                ProfileChips.Add(new ProfileOptionViewModel(p, false));
                SoulCloneOptions.Add(p.Name);
            }

            var target = profiles.FirstOrDefault(p => p.Name == _settings.ActiveProfile)
                ?? profiles.FirstOrDefault(p => p.IsDefault)
                ?? profiles.FirstOrDefault();
            _suppressProfileRefresh = true;
            try
            {
                SelectedProfile = target;
            }
            finally
            {
                _suppressProfileRefresh = false;
            }
            UpdateProfileChipSelection();

            await RefreshSessionsAsync();
        }
        catch (Exception ex)
        {
            StatusBanner = $"Catalog refresh error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshSessionsAsync()
    {
        if (_restClient is null) return;
        var gen = Interlocked.Increment(ref _sessionsRefreshGeneration);
        try
        {
            var fetched = new List<SessionSummary>();
            if (ShowAllProfiles && ProfileChips.Count > 0)
            {
                var tasks = ProfileChips.Select(p => _restClient.GetProfileSessionsAsync(p.Name, limit: 60));
                var batches = await Task.WhenAll(tasks);
                foreach (var batch in batches) fetched.AddRange(batch);
                fetched.Sort((a, b) => (b.LastActivityAt ?? 0).CompareTo(a.LastActivityAt ?? 0));
            }
            else
            {
                var sessions = await _restClient.GetProfileSessionsAsync(SelectedProfile?.Name, limit: 100);
                fetched.AddRange(sessions);
            }

            if (gen != _sessionsRefreshGeneration) return;

            var previousActive = ActiveSession;
            var previousRuntimeId = _activeRuntimeSessionId;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _allSessions.Clear();
            foreach (var s in fetched)
            {
                var key = $"{s.Profile ?? ""}:{s.StoredSessionId ?? s.Id}";
                if (seen.Add(key))
                {
                    _allSessions.Add(s);
                }
            }

            // Retain the currently open session if it hasn't been persisted with min_messages=1 yet
            // (e.g. a brand-new empty chat), so background sessions.changed events never yank the user away.
            if (previousActive is not null && !string.IsNullOrEmpty(previousActive.Id))
            {
                var matched = _allSessions.FirstOrDefault(s => SessionsMatch(s, previousActive, previousRuntimeId));
                if (matched is not null)
                {
                    matched.RuntimeSessionId ??= previousRuntimeId ?? previousActive.RuntimeSessionId;
                    matched.StoredSessionId ??= previousActive.StoredSessionId;
                }
                else if (string.IsNullOrEmpty(previousActive.Profile)
                    || ShowAllProfiles
                    || string.Equals(previousActive.Profile, SelectedProfile?.Name, StringComparison.OrdinalIgnoreCase))
                {
                    _allSessions.Insert(0, previousActive);
                }
            }

            ApplySessionFilter(previousActive, previousRuntimeId);

            if (ActiveSession is null && previousActive is null && FilteredSessions.Count > 0)
            {
                await SelectAndOpenSessionAsync(FilteredSessions[0]);
            }
        }
        catch (Exception ex)
        {
            StatusBanner = $"Sessions load error: {ex.Message}";
        }
    }

    private void ApplySessionFilter(SessionSummary? preferredActive = null, string? preferredRuntimeId = null)
    {
        var current = preferredActive ?? ActiveSession;
        var runtimeId = preferredRuntimeId ?? _activeRuntimeSessionId;
        _suppressSelectionOpen = true;
        try
        {
            FilteredSessions.Clear();
            var q = SessionSearchQuery.Trim();
            var ordered = _allSessions
                .Where(s => string.IsNullOrEmpty(q)
                    || s.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || (s.Preview?.Contains(q, StringComparison.OrdinalIgnoreCase) == true))
                .OrderByDescending(s => s.IsPinned == true)
                .ThenByDescending(s => s.LastActivityAt ?? 0);

            foreach (var s in ordered)
            {
                FilteredSessions.Add(s);
            }

            if (current is not null)
            {
                ActiveSession = FilteredSessions.FirstOrDefault(s => SessionsMatch(s, current, runtimeId))
                    ?? FilteredSessions.FirstOrDefault(s => s.Id == current.Id);
            }
        }
        finally
        {
            _suppressSelectionOpen = false;
        }
    }

    private static bool SessionsMatch(SessionSummary candidate, SessionSummary target, string? targetRuntimeId)
    {
        if (ReferenceEquals(candidate, target)) return true;
        var candidateIds = new HashSet<string>(StringComparer.Ordinal) { candidate.Id };
        if (!string.IsNullOrEmpty(candidate.StoredSessionId)) candidateIds.Add(candidate.StoredSessionId);
        if (!string.IsNullOrEmpty(candidate.RuntimeSessionId)) candidateIds.Add(candidate.RuntimeSessionId);
        foreach (var alt in candidate.AlternateIds) candidateIds.Add(alt);

        if (candidateIds.Contains(target.Id)) return true;
        if (!string.IsNullOrEmpty(target.StoredSessionId) && candidateIds.Contains(target.StoredSessionId)) return true;
        if (!string.IsNullOrEmpty(target.RuntimeSessionId) && candidateIds.Contains(target.RuntimeSessionId)) return true;
        if (!string.IsNullOrEmpty(targetRuntimeId) && candidateIds.Contains(targetRuntimeId)) return true;
        return target.AlternateIds.Any(candidateIds.Contains);
    }

    private async Task SelectAndOpenSessionAsync(SessionSummary session)
    {
        _suppressSelectionOpen = true;
        ActiveSession = session;
        _suppressSelectionOpen = false;
        await OpenSessionAsync(session);
    }

    [RelayCommand]
    private async Task CreateNewSessionAsync()
    {
        if (_wsClient is null || !_wsClient.IsConnected) return;
        try
        {
            var id = await _wsClient.CreateSessionAsync(SelectedProfile?.Name);
            AppDiagnostics.Log("SESSION", $"Created new session id={id} profile={SelectedProfile?.Name ?? "default"}");
            var summary = new SessionSummary
            {
                Id = id,
                StoredSessionId = id,
                RuntimeSessionId = id,
                Title = "New Chat",
                Profile = SelectedProfile?.Name,
                UpdatedLabel = "Just now",
                LastActivityAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                MessageCount = 0
            };
            _allSessions.Insert(0, summary);
            ApplySessionFilter(summary, id);

            Interlocked.Increment(ref _openSessionGeneration);
            _activeRuntimeSessionId = id;
            _streamBufferSessionId = null;
            _streamFlushTimer.Stop();
            _streamContentBuffer.Clear();
            _streamReasoningBuffer.Clear();
            if (_streamingAssistantMessage is not null) _streamingAssistantMessage.IsStreaming = false;
            _streamingAssistantMessage = null;
            _activeTools.Clear();
            ActiveMessages.Clear();
            IsRunningTurn = false;
            ComposerText = "";

            _suppressSelectionOpen = true;
            ActiveSession = summary;
            _suppressSelectionOpen = false;
            SelectedTab = V2Tab.Chats;
        }
        catch (Exception ex)
        {
            StatusBanner = $"Create chat failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenSessionAsync(SessionSummary session)
    {
        if (_wsClient is null || _restClient is null) return;
        var openGen = Interlocked.Increment(ref _openSessionGeneration);
        IsLoadingMessages = true;
        // Clear previous runtime session ID immediately so in-flight stream events
        // from the old session cannot leak into this session while resume is awaiting.
        _activeRuntimeSessionId = null;
        _streamBufferSessionId = null;
        _streamFlushTimer.Stop();
        _streamContentBuffer.Clear();
        _streamReasoningBuffer.Clear();
        if (_streamingAssistantMessage is not null) _streamingAssistantMessage.IsStreaming = false;
        _streamingAssistantMessage = null;
        _activeTools.Clear();
        ActiveMessages.Clear();
        IsRunningTurn = false;

        ComposerText = _cache.LoadDraft(DraftKey(session.Id)) ?? "";

        var targetProfile = session.Profile ?? SelectedProfile?.Name;
        var resumeKey = session.StoredSessionId ?? session.Id;

        try
        {
            var resume = await _wsClient.OpenSessionAsync(resumeKey, targetProfile);
            if (openGen != _openSessionGeneration) return;

            _activeRuntimeSessionId = resume.SessionId;
            session.RuntimeSessionId = resume.SessionId;
            if (!string.IsNullOrWhiteSpace(resume.StoredSessionId))
            {
                session.StoredSessionId = resume.StoredSessionId;
            }
            AppDiagnostics.Log("SESSION", $"Opened key={resumeKey} -> runtime={resume.SessionId}, stored={resume.StoredSessionId}, running={resume.IsRunning}");

            IsRunningTurn = resume.IsRunning;
            CurrentModelLabel = string.IsNullOrWhiteSpace(resume.Model)
                ? (string.IsNullOrWhiteSpace(session.Model) ? "default" : session.Model)
                : resume.Model;
            ContextPercent = resume.ContextPercent;
            ContextUsed = resume.ContextUsed;
            ContextMax = resume.ContextMax;
            CurrentCwd = resume.Cwd;

            var historyKey = resume.StoredSessionId ?? session.StoredSessionId ?? session.Id;
            IReadOnlyList<ChatMessage> messages = [];
            try
            {
                messages = await _restClient.GetSessionMessagesAsync(historyKey, targetProfile);
            }
            catch (HttpRequestException) when (session.MessageCount == 0)
            {
                // Brand-new 0-message session may not have a persisted transcript row yet
            }
            if (openGen != _openSessionGeneration) return;

            ActiveMessages.Clear();
            _userExpandedRunIds.Clear();
            _userCollapsedRunIds.Clear();
            foreach (var msg in messages)
            {
                ActiveMessages.Add(new MessageItemViewModel(msg));
            }
            RecomputeTranscriptPresentation(ActiveMessages, IsRunningTurn);
            TranscriptScrollRequested?.Invoke(true);
        }
        catch (Exception ex)
        {
            if (openGen == _openSessionGeneration)
            {
                _activeRuntimeSessionId = session.RuntimeSessionId ?? session.Id;
                StatusBanner = $"Open session failed: {ex.Message}";
                AppDiagnostics.Log("SESSION_ERR", $"Open failed key={resumeKey}: {ex.Message}");
            }
        }
        finally
        {
            if (openGen == _openSessionGeneration)
            {
                IsLoadingMessages = false;
            }
        }
    }

    [RelayCommand]
    private void RequestDeleteSession(SessionSummary? session)
    {
        var target = session ?? ActiveSession;
        if (target is null || IsDeletingSession) return;
        if (IsRunningTurn && ActiveSession is not null && SessionsMatch(target, ActiveSession, _activeRuntimeSessionId))
        {
            StatusBanner = Loc.StopBeforeDeleteSession;
            return;
        }
        PendingDeleteSession = target;
    }

    [RelayCommand]
    private void CancelDeleteSession()
    {
        if (!IsDeletingSession) PendingDeleteSession = null;
    }

    [RelayCommand]
    private async Task ConfirmDeleteSessionAsync()
    {
        var target = PendingDeleteSession;
        if (_restClient is null || target is null || IsDeletingSession) return;

        if (IsRunningTurn && ActiveSession is not null && SessionsMatch(target, ActiveSession, _activeRuntimeSessionId))
        {
            PendingDeleteSession = null;
            StatusBanner = Loc.StopBeforeDeleteSession;
            return;
        }

        IsDeletingSession = true;
        try
        {
            var deleteKey = target.StoredSessionId ?? target.Id;
            var profile = target.Profile ?? SelectedProfile?.Name;
            await _restClient.DeleteSessionAsync(deleteKey, profile);

            var deletedRuntime = ActiveSession is not null && SessionsMatch(target, ActiveSession, _activeRuntimeSessionId)
                ? _activeRuntimeSessionId
                : target.RuntimeSessionId;

            _allSessions.RemoveAll(s => SessionsMatch(s, target, deletedRuntime));
            _cache.SaveDraft(DraftKey(target.Id), "");
            if (!string.IsNullOrEmpty(target.StoredSessionId))
            {
                _cache.SaveDraft(DraftKey(target.StoredSessionId), "");
            }

            if (BesideSession is not null && SessionsMatch(BesideSession, target, deletedRuntime))
            {
                BesideSession = null;
                BesideMessages.Clear();
                IsBesideVisible = false;
            }

            var deletedActive = ActiveSession is not null && SessionsMatch(target, ActiveSession, _activeRuntimeSessionId);
            PendingDeleteSession = null;

            if (deletedActive)
            {
                Interlocked.Increment(ref _openSessionGeneration);
                _activeRuntimeSessionId = null;
                _streamBufferSessionId = null;
                _streamFlushTimer.Stop();
                _streamContentBuffer.Clear();
                _streamReasoningBuffer.Clear();
                if (_streamingAssistantMessage is not null) _streamingAssistantMessage.IsStreaming = false;
                _streamingAssistantMessage = null;
                _activeTools.Clear();
                ActiveMessages.Clear();
                IsRunningTurn = false;
                ComposerText = "";
                ActiveSession = null;
            }

            ApplySessionFilter();

            if (deletedActive && ActiveSession is null && FilteredSessions.Count > 0)
            {
                await SelectAndOpenSessionAsync(FilteredSessions[0]);
            }

            StatusBanner = $"Connected · {_activeDashboard?.Label}";
        }
        catch (Exception ex)
        {
            StatusBanner = $"Delete failed: {ex.Message}";
            AppDiagnostics.Log("SESSION_DEL_ERR", ex.Message);
        }
        finally
        {
            IsDeletingSession = false;
        }
    }

    [RelayCommand]
    private async Task TogglePinSessionAsync(SessionSummary session)
    {
        if (_restClient is null) return;
        try
        {
            var next = !(session.IsPinned == true);
            await _restClient.SetSessionPinnedAsync(session.StoredSessionId ?? session.Id, next, session.Profile ?? SelectedProfile?.Name);
            session.IsPinned = next;
            ApplySessionFilter();
        }
        catch (Exception ex)
        {
            StatusBanner = $"Pin failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CompressContextAsync()
    {
        if (_wsClient is null || ActiveSession is null) return;
        var targetId = _activeRuntimeSessionId ?? ActiveSession.RuntimeSessionId ?? ActiveSession.Id;
        try
        {
            StatusBanner = "Compressing context...";
            await _wsClient.CompressSessionAsync(targetId, ActiveSession.Profile ?? SelectedProfile?.Name);
            StatusBanner = $"Connected · {_activeDashboard?.Label}";
        }
        catch (Exception ex)
        {
            StatusBanner = $"Compress failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SendFollowUpPromptAsync(string promptText)
    {
        if (string.IsNullOrWhiteSpace(promptText)) return;
        ComposerText = promptText;
        await SendOrQueueAsync();
    }

    [RelayCommand]
    private async Task SendOrQueueAsync()
    {
        if (_wsClient is null || ActiveSession is null) return;
        var text = ComposerText.Trim();
        if (string.IsNullOrEmpty(text)) return;

        ComposerText = "";
        _cache.SaveDraft(DraftKey(ActiveSession.Id), "");

        var msgId = Guid.NewGuid().ToString("N");
        var (cleanText, optimisticAttachments) = UserAttachmentExtractor.Extract(text, msgId);
        ActiveMessages.Add(new MessageItemViewModel(new ChatMessage
        {
            Id = msgId,
            Role = MessageRole.User,
            Content = cleanText,
            Attachments = optimisticAttachments,
            Timestamp = DateTimeOffset.Now.ToString("HH:mm")
        }));
        RecomputeTranscriptPresentation(ActiveMessages, IsRunningTurn);
        TranscriptScrollRequested?.Invoke(true);

        var profile = ActiveSession.Profile ?? SelectedProfile?.Name;
        var targetId = _activeRuntimeSessionId ?? ActiveSession.RuntimeSessionId ?? ActiveSession.Id;
        var queued = IsRunningTurn;

        try
        {
            if (!_wsClient.IsConnected)
            {
                var reconnectedId = await EnsureWsReconnectedForActiveSessionAsync(profile);
                if (!string.IsNullOrEmpty(reconnectedId)) targetId = reconnectedId;
                queued = IsRunningTurn;
            }

            var outcome = await _wsClient.SubmitPromptAsync(targetId, text, profile, queued: queued);
            AppDiagnostics.Log("SEND", $"Submitted session={targetId}, queued={queued}, outcome={outcome}");
            _lastActiveStreamEventAt = DateTimeOffset.UtcNow;
            if (!queued) IsRunningTurn = true;
            StatusBanner = $"Connected · {_activeDashboard?.Label}";
        }
        catch (RpcException rpcEx) when (rpcEx.Message.Contains("session not found", StringComparison.OrdinalIgnoreCase))
        {
            // Re-resume once to acquire a fresh live runtime session_id and retry
            try
            {
                var resumed = await _wsClient.OpenSessionAsync(ActiveSession.StoredSessionId ?? ActiveSession.Id, profile);
                _activeRuntimeSessionId = resumed.SessionId;
                ActiveSession.RuntimeSessionId = resumed.SessionId;
                await _wsClient.SubmitPromptAsync(resumed.SessionId, text, profile, queued: false);
                _lastActiveStreamEventAt = DateTimeOffset.UtcNow;
                IsRunningTurn = true;
                StatusBanner = $"Connected · {_activeDashboard?.Label}";
                AppDiagnostics.Log("SEND", $"Retried after session.resume -> {resumed.SessionId}");
            }
            catch (Exception retryEx)
            {
                IsRunningTurn = false;
                StatusBanner = $"Send failed: {retryEx.Message}";
                AppDiagnostics.Log("SEND_ERR", retryEx.Message);
                AppendSystemNotice($"Send failed: {retryEx.Message}");
            }
        }
        catch (Exception ex)
        {
            if (!queued) IsRunningTurn = false;
            StatusBanner = $"Send failed: {ex.Message}";
            AppDiagnostics.Log("SEND_ERR", ex.Message);
            AppendSystemNotice($"Send failed: {ex.Message}");
        }
    }

    private void AppendSystemNotice(string text)
    {
        ActiveMessages.Add(new MessageItemViewModel(new ChatMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Role = MessageRole.System,
            Content = text,
            Timestamp = DateTimeOffset.Now.ToString("HH:mm")
        }));
        TranscriptScrollRequested?.Invoke(false);
    }

    private async Task<string?> EnsureWsReconnectedForActiveSessionAsync(string? profile)
    {
        if (_activeDashboard is null || ActiveSession is null) return null;
        var secrets = _vault.LoadSecrets(_activeDashboard.Id);
        if (secrets is null) return null;

        try
        {
            var ticket = _activeDashboard.AuthMode == AuthMode.Password && !string.IsNullOrEmpty(secrets.Password)
                ? await _authClient.ConnectWithPasswordAsync(_activeDashboard.NormalizedUrl, secrets.Username ?? "", secrets.Password, secrets)
                : await _authClient.ConnectWithTokenAsync(_activeDashboard.NormalizedUrl, secrets);

            if (_wsClient is not null)
            {
                _wsClient.EventReceived -= OnWsEventReceived;
                _wsClient.Disconnected -= OnWsDisconnected;
                await _wsClient.DisposeAsync();
            }
            _restClient?.Dispose();
            _restClient = new HermesRestClient(_activeDashboard.NormalizedUrl, ticket, secrets);
            _wsClient = new HermesWsClient();
            _wsClient.EventReceived += OnWsEventReceived;
            _wsClient.Disconnected += OnWsDisconnected;
            await _wsClient.ConnectAsync(_activeDashboard.NormalizedUrl, ticket, secrets);

            ConnectionPhase = ConnectionPhase.Connected;
            var resumed = await _wsClient.OpenSessionAsync(ActiveSession.StoredSessionId ?? ActiveSession.Id, profile);
            _activeRuntimeSessionId = resumed.SessionId;
            ActiveSession.RuntimeSessionId = resumed.SessionId;
            IsRunningTurn = resumed.IsRunning;
            AppDiagnostics.Log("WS_RECONN", $"Silent WS reconnect succeeded -> runtime={resumed.SessionId}, running={resumed.IsRunning}");
            return resumed.SessionId;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log("WS_RECONN_ERR", ex.Message);
            return null;
        }
    }

    [RelayCommand]
    private async Task SteerTurnAsync()
    {
        if (_wsClient is null || ActiveSession is null) return;
        var text = ComposerText.Trim();
        if (string.IsNullOrEmpty(text)) return;

        var targetId = _activeRuntimeSessionId ?? ActiveSession.RuntimeSessionId ?? ActiveSession.Id;
        ComposerText = "";
        try
        {
            await _wsClient.SteerAsync(targetId, text, ActiveSession.Profile ?? SelectedProfile?.Name);
            ActiveMessages.Add(new MessageItemViewModel(new ChatMessage
            {
                Id = Guid.NewGuid().ToString("N"),
                Role = MessageRole.User,
                Content = $"[Steer] {text}",
                DisplayKind = "steer",
                Timestamp = DateTimeOffset.Now.ToString("HH:mm")
            }));
            TranscriptScrollRequested?.Invoke(true);
        }
        catch (Exception ex)
        {
            StatusBanner = $"Steer failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task InterruptTurnAsync()
    {
        if (_wsClient is null || ActiveSession is null) return;
        var targetId = _activeRuntimeSessionId ?? ActiveSession.RuntimeSessionId ?? ActiveSession.Id;
        try
        {
            await _wsClient.InterruptAsync(targetId, ActiveSession.Profile ?? SelectedProfile?.Name);
            IsRunningTurn = false;
        }
        catch (Exception ex)
        {
            IsRunningTurn = false;
            StatusBanner = $"Interrupt failed: {ex.Message}";
            AppDiagnostics.Log("INTERRUPT_ERR", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ApproveCommandAsync(MessageItemViewModel item)
    {
        if (_wsClient is null || ActiveSession is null || item.Approval is null) return;
        try
        {
            await _wsClient.RespondToApprovalAsync(
                item.Approval.SessionId,
                item.Approval.RequestId,
                "approve",
                ActiveSession.Profile ?? SelectedProfile?.Name);
            item.Approval = item.Approval with { IsResolved = true, Choice = "Approved" };
            NeedsYouCount = Math.Max(0, NeedsYouCount - 1);
        }
        catch (Exception ex)
        {
            StatusBanner = $"Approval failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RejectCommandAsync(MessageItemViewModel item)
    {
        if (_wsClient is null || ActiveSession is null || item.Approval is null) return;
        try
        {
            await _wsClient.RespondToApprovalAsync(
                item.Approval.SessionId,
                item.Approval.RequestId,
                "deny",
                ActiveSession.Profile ?? SelectedProfile?.Name);
            var steer = item.ApprovalSteerText.Trim();
            if (!string.IsNullOrEmpty(steer))
            {
                await _wsClient.SteerAsync(item.Approval.SessionId, steer, ActiveSession.Profile ?? SelectedProfile?.Name);
            }
            item.Approval = item.Approval with { IsResolved = true, Choice = string.IsNullOrEmpty(steer) ? "Rejected" : $"Rejected & steered: {steer}" };
            NeedsYouCount = Math.Max(0, NeedsYouCount - 1);
        }
        catch (Exception ex)
        {
            StatusBanner = $"Reject failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task AnswerClarifyChoiceAsync(object? parameter)
    {
        if (parameter is not Tuple<MessageItemViewModel, ClarifyChoice> pair) return;
        await SubmitClarifyAnswerAsync(pair.Item1, pair.Item2.Value);
    }

    [RelayCommand]
    private async Task SubmitClarifyCustomAsync(MessageItemViewModel item)
    {
        var ans = item.ClarifyCustomAnswer.Trim();
        if (string.IsNullOrEmpty(ans)) return;
        await SubmitClarifyAnswerAsync(item, ans);
    }

    public async Task SubmitClarifyAnswerAsync(MessageItemViewModel item, string answer)
    {
        if (_wsClient is null || ActiveSession is null || item.Clarify is null) return;
        var targetId = _activeRuntimeSessionId ?? ActiveSession.RuntimeSessionId ?? ActiveSession.Id;
        try
        {
            await _wsClient.RespondToClarifyAsync(
                targetId,
                item.Clarify.RequestId,
                answer,
                item.Clarify.QuestionId,
                ActiveSession.Profile ?? SelectedProfile?.Name);
            item.Clarify = item.Clarify with { IsResolved = true, SelectedAnswer = answer };
            NeedsYouCount = Math.Max(0, NeedsYouCount - 1);
        }
        catch (Exception ex)
        {
            StatusBanner = $"Clarify failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenInBesideChatAsync(SessionSummary session)
    {
        IsHtmlPreviewPaneOpen = false;
        BesideSession = session;
        IsBesideVisible = true;
        BesideMessages.Clear();
        if (_restClient is not null)
        {
            try
            {
                var msgs = await _restClient.GetSessionMessagesAsync(session.StoredSessionId ?? session.Id, session.Profile ?? SelectedProfile?.Name, limit: 40);
                foreach (var m in msgs) BesideMessages.Add(new MessageItemViewModel(m));
                RecomputeTranscriptPresentation(BesideMessages, isTurnRunning: false);
            }
            catch
            {
                // Non-fatal
            }
        }
    }

    [RelayCommand]
    private async Task SwapBesideChatAsync()
    {
        if (BesideSession is null) return;
        var prevMain = ActiveSession;
        var nextMain = BesideSession;
        if (prevMain is not null)
        {
            await OpenInBesideChatAsync(prevMain);
        }
        else
        {
            CloseBesideChat();
        }
        await SelectAndOpenSessionAsync(nextMain);
    }

    [RelayCommand]
    private void CloseBesideChat()
    {
        IsBesideVisible = false;
        BesideSession = null;
        BesideMessages.Clear();
    }

    [RelayCommand]
    private async Task SendBesideQuickMessageAsync()
    {
        if (_wsClient is null || BesideSession is null) return;
        var text = BesideComposerText.Trim();
        if (string.IsNullOrEmpty(text)) return;

        BesideComposerText = "";
        BesideMessages.Add(new MessageItemViewModel(new ChatMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Role = MessageRole.User,
            Content = text,
            Timestamp = DateTimeOffset.Now.ToString("HH:mm")
        }));
        try
        {
            var resume = await _wsClient.OpenSessionAsync(BesideSession.StoredSessionId ?? BesideSession.Id, BesideSession.Profile);
            BesideSession.RuntimeSessionId = resume.SessionId;
            await _wsClient.SubmitPromptAsync(resume.SessionId, text, BesideSession.Profile);
        }
        catch (Exception ex)
        {
            StatusBanner = $"Beside send failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenBotChatAsync(ProfileOptionViewModel chip)
    {
        ShowAllProfiles = false;
        SelectedProfile = chip.Profile;
        await RefreshSessionsAsync();
        if (!string.IsNullOrWhiteSpace(chip.Profile.CanonicalSessionId))
        {
            var match = _allSessions.FirstOrDefault(s =>
                s.Id == chip.Profile.CanonicalSessionId || s.StoredSessionId == chip.Profile.CanonicalSessionId);
            if (match is not null)
            {
                await SelectAndOpenSessionAsync(match);
            }
        }
        SelectedTab = V2Tab.Chats;
    }

    [RelayCommand]
    private async Task NewChatWithBotAsync(ProfileOptionViewModel chip)
    {
        ShowAllProfiles = false;
        SelectedProfile = chip.Profile;
        await CreateNewSessionAsync();
    }

    [RelayCommand]
    private async Task OpenActiveProfileSoulEditorAsync()
    {
        var target = ProfileChips.FirstOrDefault(p => p.Name == SelectedProfile?.Name)
            ?? ProfileChips.FirstOrDefault();
        if (target is not null)
        {
            await OpenSoulEditorForProfileAsync(target);
        }
        else if (SelectedProfile is not null)
        {
            await OpenSoulEditorForProfileAsync(new ProfileOptionViewModel(SelectedProfile, true));
        }
    }

    [RelayCommand]
    private async Task OpenSoulEditorForProfileAsync(ProfileOptionViewModel chip)
    {
        if (_wsClient is null || !_wsClient.IsConnected)
        {
            StatusBanner = "Connect to Hermes first to edit SOUL.md.";
            return;
        }

        IsCreatingSoul = false;
        IsConfirmingDeleteProfile = false;
        SoulEditorSubTabIndex = 0;
        SoulEditorError = null;
        SoulEditorNotice = null;
        SoulEditorSlugInput = chip.Name;
        SoulEditorTitleInput = chip.DisplayName;
        SoulEditorDescription = chip.Description ?? "";
        SoulEditorText = "";
        _soulOriginalTitle = chip.DisplayName;
        _soulOriginalDescription = SoulEditorDescription;
        _soulOriginalText = "";
        IsSoulEditorOpen = true;

        await ReloadSoulFromServerAsync();
    }

    [RelayCommand]
    private void OpenCreateSoulModal()
    {
        if (_wsClient is null || !_wsClient.IsConnected)
        {
            StatusBanner = "Connect to Hermes first to create a new Soul.";
            return;
        }

        _soulSlugEditedManually = false;
        IsCreatingSoul = true;
        IsConfirmingDeleteProfile = false;
        SoulEditorError = null;
        SoulEditorNotice = null;
        SoulEditorTitleInput = "";
        SoulEditorSlugInput = "";
        SoulEditorDescription = "";
        SoulEditorText = "";
        SoulEditorCloneFrom = SoulCloneOptions.Contains("default") ? "default" : "(Fresh profile)";
        SoulEditorNoSkills = false;
        SoulEditorShareAuth = true;
        IsSoulEditorOpen = true;
    }

    [RelayCommand]
    private void SelectSoulPersonalityTab() => SoulEditorSubTabIndex = 0;

    [RelayCommand]
    private void SelectSoulCapabilitiesTab() => SoulEditorSubTabIndex = 1;

    [RelayCommand]
    private async Task ReloadSoulFromServerAsync()
    {
        if (IsCreatingSoul || string.IsNullOrWhiteSpace(SoulEditorSlugInput))
            return;

        IsSoulEditorBusy = true;
        SoulEditorError = null;
        SoulEditorNotice = null;
        _loadedCapabilitiesFromRestOnly = false;
        try
        {
            IReadOnlyList<BotProfileCapability> skills = [];
            IReadOnlyList<BotProfileCapability> toolsets = [];
            IReadOnlyList<BotProfileCapability> mcpServers = [];

            if (_wsClient is { IsConnected: true })
            {
                try
                {
                    var details = await _wsClient.DescribeBotProfileAsync(SoulEditorSlugInput);
                    SoulEditorDescription = details.Description;
                    SoulEditorText = details.Soul;
                    _soulOriginalDescription = details.Description;
                    _soulOriginalText = details.Soul;
                    skills = details.Skills;
                    toolsets = details.Toolsets;
                    mcpServers = details.McpServers;
                }
                catch
                {
                    // Fallback to REST capabilities below for older gateways
                }
            }

            if (skills.Count == 0 && toolsets.Count == 0 && _restClient is not null)
            {
                _loadedCapabilitiesFromRestOnly = true;
                try { skills = await _restClient.GetSkillsAsync(SoulEditorSlugInput); } catch { }
                try { toolsets = await _restClient.GetToolsetsAsync(SoulEditorSlugInput); } catch { }
            }

            EditingSkills.Clear();
            foreach (var s in skills) EditingSkills.Add(new CapabilityOptionViewModel(s));

            EditingToolsets.Clear();
            foreach (var t in toolsets) EditingToolsets.Add(new CapabilityOptionViewModel(t, UpdateToolsetGuards));
            UpdateToolsetGuards();

            EditingMcpServers.Clear();
            foreach (var m in mcpServers) EditingMcpServers.Add(new CapabilityOptionViewModel(m));

            _baselineDisabledSkills = EditingSkills.Where(x => !x.IsEnabled).Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
            _baselineEnabledToolsets = EditingToolsets.Where(x => x.IsEnabled).Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
            _baselineEnabledMcpServers = EditingMcpServers.Where(x => x.IsEnabled).Select(x => x.Name).ToHashSet(StringComparer.Ordinal);

            OnPropertyChanged(nameof(HasEditingSkills));
            OnPropertyChanged(nameof(HasEditingToolsets));
            OnPropertyChanged(nameof(HasEditingMcpServers));
            OnPropertyChanged(nameof(HasNoEditingCapabilities));
        }
        catch (Exception ex)
        {
            SoulEditorError = $"Failed to load profile: {ex.Message}";
        }
        finally
        {
            IsSoulEditorBusy = false;
        }
    }

    private void UpdateToolsetGuards()
    {
        var enabledCount = EditingToolsets.Count(t => t.IsEnabled);
        foreach (var t in EditingToolsets)
        {
            t.IsLastToolsetGuard = t.IsEnabled && enabledCount <= 1;
        }
    }

    [RelayCommand]
    private void DuplicateCurrentSoul()
    {
        var sourceSlug = SoulEditorSlugInput;
        var baseSlug = string.IsNullOrWhiteSpace(sourceSlug) ? "soul" : sourceSlug;
        var existing = ProfileChips.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = $"{baseSlug}-2";
        for (var n = 2; n < 100; n++)
        {
            var next = $"{baseSlug}-{n}";
            if (!existing.Contains(next))
            {
                candidate = next;
                break;
            }
        }

        var copyTitle = string.IsNullOrWhiteSpace(SoulEditorTitleInput) ? candidate : $"{SoulEditorTitleInput} (copy)";
        var copyDesc = SoulEditorDescription;
        var copySoul = SoulEditorText;

        _soulSlugEditedManually = true;
        IsCreatingSoul = true;
        IsConfirmingDeleteProfile = false;
        SoulEditorError = null;
        SoulEditorNotice = $"Creating duplicate from @{sourceSlug}";
        SoulEditorTitleInput = copyTitle;
        SoulEditorSlugInput = candidate;
        SoulEditorDescription = copyDesc;
        SoulEditorText = copySoul;
        SoulEditorCloneFrom = SoulCloneOptions.Contains(sourceSlug) ? sourceSlug : "(Fresh profile)";
    }

    [RelayCommand]
    private async Task SaveOrCreateSoulAsync()
    {
        if (_wsClient is null || !_wsClient.IsConnected)
        {
            SoulEditorError = "Not connected to Hermes gateway.";
            return;
        }

        SoulEditorError = null;
        SoulEditorNotice = null;
        IsSoulEditorBusy = true;

        try
        {
            if (IsCreatingSoul)
            {
                var slug = !string.IsNullOrWhiteSpace(SoulEditorSlugInput)
                    ? SoulEditorSlugInput.Trim().ToLowerInvariant()
                    : BotSoulHelper.ToSlug(SoulEditorTitleInput);

                if (!BotSoulHelper.IsValidSlug(slug))
                {
                    SoulEditorError = "Invalid profile slug. Use 1-64 chars matching [a-z0-9][a-z0-9_-]* (or enter a title).";
                    return;
                }
                if (ProfileChips.Any(p => string.Equals(p.Name, slug, StringComparison.OrdinalIgnoreCase)))
                {
                    SoulEditorError = $"Profile '@{slug}' already exists.";
                    return;
                }

                var roster = ProfileChips.Select(p => p.Profile).ToList();
                var composedSoul = BotSoulHelper.Compose(
                    slug,
                    SoulEditorTitleInput,
                    SoulEditorDescription,
                    SoulEditorText,
                    _botModeSupportsProtocol,
                    roster);

                var cloneFrom = string.IsNullOrWhiteSpace(SoulEditorCloneFrom) || SoulEditorCloneFrom == "(Fresh profile)"
                    ? null
                    : SoulEditorCloneFrom;

                await _wsClient.CreateBotProfileAsync(
                    slug: slug,
                    description: SoulEditorDescription.Trim(),
                    soul: composedSoul,
                    cloneFrom: cloneFrom,
                    noSkills: SoulEditorNoSkills,
                    shareAuth: SoulEditorShareAuth);

                var titleTrimmed = SoulEditorTitleInput.Trim();
                if (!string.IsNullOrEmpty(titleTrimmed) && !string.Equals(titleTrimmed, slug, StringComparison.Ordinal))
                {
                    try
                    {
                        await _wsClient.ConfigureBotProfileAsync(slug, displayTitle: titleTrimmed);
                    }
                    catch
                    {
                        // Non-fatal if ui_meta title write isn't needed
                    }
                }

                await RefreshProfilesAndSessionsAsync();
                var created = ProfileChips.FirstOrDefault(p => string.Equals(p.Name, slug, StringComparison.OrdinalIgnoreCase))?.Profile;
                if (created is not null)
                {
                    SelectedProfile = created;
                }
                IsSoulEditorOpen = false;
                StatusBanner = $"Created Soul profile '@{slug}'.";
            }
            else
            {
                var slug = SoulEditorSlugInput.Trim();
                var descChanged = !string.Equals(SoulEditorDescription, _soulOriginalDescription, StringComparison.Ordinal);
                var soulChanged = !string.Equals(SoulEditorText, _soulOriginalText, StringComparison.Ordinal);
                var titleChanged = !string.Equals(SoulEditorTitleInput.Trim(), _soulOriginalTitle, StringComparison.Ordinal);

                var currentDisabledSkills = EditingSkills.Where(x => !x.IsEnabled).Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
                var currentEnabledToolsets = EditingToolsets.Where(x => x.IsEnabled).Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
                var currentEnabledMcp = EditingMcpServers.Where(x => x.IsEnabled).Select(x => x.Name).ToHashSet(StringComparer.Ordinal);

                var skillsChanged = EditingSkills.Count > 0 && !currentDisabledSkills.SetEquals(_baselineDisabledSkills);
                var toolsetsChanged = EditingToolsets.Count > 0 && !currentEnabledToolsets.SetEquals(_baselineEnabledToolsets);
                var mcpChanged = EditingMcpServers.Count > 0 && !currentEnabledMcp.SetEquals(_baselineEnabledMcpServers);

                if (!descChanged && !soulChanged && !titleChanged && !skillsChanged && !toolsetsChanged && !mcpChanged)
                {
                    SoulEditorNotice = AppLocalization.T("No changes to save.", "没有任何改动需要保存。");
                    return;
                }

                if (!_loadedCapabilitiesFromRestOnly)
                {
                    IReadOnlyList<string>? toolsetsParam = null;
                    if (toolsetsChanged)
                    {
                        toolsetsParam = currentEnabledToolsets.Count == EditingToolsets.Count
                            ? Array.Empty<string>()
                            : [.. currentEnabledToolsets];
                    }

                    await _wsClient.ConfigureBotProfileAsync(
                        name: slug,
                        description: descChanged ? SoulEditorDescription : null,
                        soul: soulChanged ? SoulEditorText : null,
                        displayTitle: titleChanged ? SoulEditorTitleInput.Trim() : null,
                        disabledSkills: skillsChanged ? [.. currentDisabledSkills] : null,
                        enabledToolsets: toolsetsParam,
                        enabledMcpServers: mcpChanged ? [.. currentEnabledMcp] : null);
                }
                else
                {
                    if (descChanged || soulChanged || titleChanged)
                    {
                        await _wsClient.ConfigureBotProfileAsync(
                            name: slug,
                            description: descChanged ? SoulEditorDescription : null,
                            soul: soulChanged ? SoulEditorText : null,
                            displayTitle: titleChanged ? SoulEditorTitleInput.Trim() : null);
                    }
                    if (_restClient is not null)
                    {
                        foreach (var s in EditingSkills)
                        {
                            var wasEnabled = !_baselineDisabledSkills.Contains(s.Name);
                            if (s.IsEnabled != wasEnabled)
                            {
                                await _restClient.ToggleSkillAsync(s.Name, s.IsEnabled, slug);
                            }
                        }
                        foreach (var t in EditingToolsets)
                        {
                            var wasEnabled = _baselineEnabledToolsets.Contains(t.Name);
                            if (t.IsEnabled != wasEnabled)
                            {
                                await _restClient.ToggleToolsetAsync(t.Name, t.IsEnabled, slug);
                            }
                        }
                    }
                }

                _soulOriginalDescription = SoulEditorDescription;
                _soulOriginalText = SoulEditorText;
                _soulOriginalTitle = SoulEditorTitleInput.Trim();
                _baselineDisabledSkills = currentDisabledSkills;
                _baselineEnabledToolsets = currentEnabledToolsets;
                _baselineEnabledMcpServers = currentEnabledMcp;
                SoulEditorNotice = AppLocalization.T("Saved profile configuration to Hermes.", "已保存配置到 Hermes 网关。");
                StatusBanner = AppLocalization.T($"Saved config for @{slug}.", $"已保存 @{slug} 配置。");
                await RefreshProfilesAndSessionsAsync();
            }
        }
        catch (Exception ex)
        {
            SoulEditorError = ex.Message;
        }
        finally
        {
            IsSoulEditorBusy = false;
        }
    }

    [RelayCommand]
    private void RequestDeleteProfile()
    {
        if (CanDeleteEditingProfile) IsConfirmingDeleteProfile = true;
    }

    [RelayCommand]
    private void CancelDeleteProfile() => IsConfirmingDeleteProfile = false;

    [RelayCommand]
    private async Task ConfirmDeleteProfileAsync()
    {
        if (_restClient is null || !CanDeleteEditingProfile) return;
        var slug = SoulEditorSlugInput.Trim();
        IsSoulEditorBusy = true;
        SoulEditorError = null;
        try
        {
            await _restClient.DeleteProfileAsync(slug);
            IsConfirmingDeleteProfile = false;
            IsSoulEditorOpen = false;
            StatusBanner = $"Deleted profile '@{slug}'.";
            await RefreshProfilesAndSessionsAsync();
        }
        catch (Exception ex)
        {
            SoulEditorError = $"Delete failed: {ex.Message}";
        }
        finally
        {
            IsSoulEditorBusy = false;
        }
    }

    [RelayCommand]
    private void CloseSoulEditor()
    {
        IsConfirmingDeleteProfile = false;
        IsSoulEditorOpen = false;
    }

    [RelayCommand]
    private async Task RefreshTasksTabAsync()
    {
        if (_restClient is null) return;
        TasksStatusNotice = null;
        try
        {
            CronJobs.Clear();
            var jobs = await _restClient.GetCronJobsAsync(SelectedProfile?.Name);
            foreach (var j in jobs) CronJobs.Add(j);
        }
        catch (Exception ex)
        {
            TasksStatusNotice = $"Cron jobs unavailable: {ex.Message}";
        }

        try
        {
            KanbanBoards.Clear();
            var boards = await _restClient.GetKanbanBoardsAsync();
            foreach (var b in boards) KanbanBoards.Add(b);
            if (SelectedKanbanBoard is null && KanbanBoards.Count > 0)
            {
                SelectedKanbanBoard = KanbanBoards[0];
            }
        }
        catch
        {
            // Kanban plugin may not be enabled on all hosts
        }
    }

    private async Task LoadKanbanTasksAsync(KanbanBoardSummary board)
    {
        if (_restClient is null) return;
        try
        {
            KanbanTasks.Clear();
            var tasks = await _restClient.GetKanbanBoardTasksAsync(board.Slug);
            foreach (var t in tasks) KanbanTasks.Add(t);
        }
        catch
        {
            // Non-fatal
        }
    }

    [RelayCommand]
    private async Task TriggerCronJobAsync(CronJobSummary job)
    {
        if (_restClient is null) return;
        try
        {
            await _restClient.CronJobActionAsync(job.Id, "trigger", SelectedProfile?.Name);
            StatusBanner = $"Triggered job: {job.Name}";
        }
        catch (Exception ex)
        {
            StatusBanner = $"Trigger failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleCronJobAsync(CronJobSummary job)
    {
        if (_restClient is null) return;
        try
        {
            var action = job.Enabled ? "pause" : "resume";
            await _restClient.CronJobActionAsync(job.Id, action, SelectedProfile?.Name);
            await RefreshTasksTabAsync();
        }
        catch (Exception ex)
        {
            StatusBanner = $"Job toggle failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenCreateCronJobModal()
    {
        if (_restClient is null)
        {
            StatusBanner = AppLocalization.T("Connect to Hermes first.", "请先连接到 Hermes 网关。");
            return;
        }
        IsCreatingCronJob = true;
        IsConfirmingDeleteCronJob = false;
        CronEditorError = null;
        CronEditorJobId = "";
        CronEditorName = "";
        CronEditorSchedule = "0 9 * * *";
        CronEditorPrompt = "";
        CronEditorDeliver = "local";
        IsCronEditorOpen = true;
    }

    [RelayCommand]
    private void OpenEditCronJobModal(CronJobSummary job)
    {
        if (_restClient is null) return;
        IsCreatingCronJob = false;
        IsConfirmingDeleteCronJob = false;
        CronEditorError = null;
        CronEditorJobId = job.Id;
        CronEditorName = job.Name;
        CronEditorSchedule = string.IsNullOrWhiteSpace(job.RawSchedule) ? job.ScheduleDisplay : job.RawSchedule;
        CronEditorPrompt = job.Prompt ?? "";
        CronEditorDeliver = string.IsNullOrWhiteSpace(job.Deliver) ? "local" : job.Deliver;
        IsCronEditorOpen = true;
    }

    [RelayCommand]
    private void ApplyCronSchedulePreset(string preset) => CronEditorSchedule = preset;

    [RelayCommand]
    private async Task SaveCronJobAsync()
    {
        if (_restClient is null) return;
        CronEditorError = null;

        if (string.IsNullOrWhiteSpace(CronEditorSchedule))
        {
            CronEditorError = AppLocalization.T("Please enter a schedule (e.g. 0 9 * * * or every 2h).", "请填写执行周期（例如 0 9 * * * 或 every 2h）。");
            return;
        }
        if (string.IsNullOrWhiteSpace(CronEditorPrompt))
        {
            CronEditorError = AppLocalization.T("Please enter a prompt for this scheduled job.", "请填写定时任务的执行提示词。");
            return;
        }

        IsCronEditorBusy = true;
        try
        {
            var profile = SelectedProfile?.Name;
            var name = string.IsNullOrWhiteSpace(CronEditorName)
                ? CronEditorPrompt.Trim().Split('\n')[0] is { Length: > 36 } line ? line[..36] : CronEditorPrompt.Trim().Split('\n')[0]
                : CronEditorName.Trim();

            if (IsCreatingCronJob)
            {
                await _restClient.CreateCronJobAsync(name, CronEditorPrompt, CronEditorSchedule, CronEditorDeliver, profile);
                StatusBanner = AppLocalization.T($"Created scheduled job: {name}", $"已创建定时任务：{name}");
            }
            else
            {
                await _restClient.UpdateCronJobAsync(CronEditorJobId, name, CronEditorPrompt, CronEditorSchedule, CronEditorDeliver, profile);
                StatusBanner = AppLocalization.T($"Updated scheduled job: {name}", $"已更新定时任务：{name}");
            }

            IsCronEditorOpen = false;
            await RefreshTasksTabAsync();
        }
        catch (Exception ex)
        {
            CronEditorError = ex.Message;
        }
        finally
        {
            IsCronEditorBusy = false;
        }
    }

    [RelayCommand]
    private void RequestDeleteCronJob()
    {
        if (!IsCreatingCronJob) IsConfirmingDeleteCronJob = true;
    }

    [RelayCommand]
    private void CancelDeleteCronJob() => IsConfirmingDeleteCronJob = false;

    [RelayCommand]
    private async Task ConfirmDeleteCronJobAsync()
    {
        if (_restClient is null || IsCreatingCronJob || string.IsNullOrWhiteSpace(CronEditorJobId)) return;
        IsCronEditorBusy = true;
        CronEditorError = null;
        try
        {
            await _restClient.DeleteCronJobAsync(CronEditorJobId, SelectedProfile?.Name);
            IsConfirmingDeleteCronJob = false;
            IsCronEditorOpen = false;
            StatusBanner = AppLocalization.T("Deleted scheduled job.", "已删除定时任务。");
            await RefreshTasksTabAsync();
        }
        catch (Exception ex)
        {
            CronEditorError = ex.Message;
        }
        finally
        {
            IsCronEditorBusy = false;
        }
    }

    [RelayCommand]
    private void CloseCronEditor()
    {
        IsConfirmingDeleteCronJob = false;
        IsCronEditorOpen = false;
    }

    [RelayCommand]
    private async Task RefreshPulseTabAsync()
    {
        if (_restClient is null) return;
        PulseStatusNotice = null;
        try
        {
            PulseItems.Clear();
            var items = await _restClient.GetPulseFeedAsync();
            foreach (var p in items) PulseItems.Add(p);
            if (PulseItems.Count == 0) PulseStatusNotice = "No Pulse briefs right now.";
        }
        catch
        {
            PulseStatusNotice = AppLocalization.T(
                "Pulse requires the optional server companion plugin.",
                "动态流需要服务端安装可选的伴侣扩展插件。");
        }
    }

    [RelayCommand]
    private async Task RefreshArtifactsTabAsync()
    {
        if (_restClient is null) return;
        ArtifactsStatusNotice = null;
        try
        {
            ArtifactItems.Clear();
            var items = await _restClient.GetArtifactsAsync();
            foreach (var a in items) ArtifactItems.Add(a);
            if (ArtifactItems.Count == 0) ArtifactsStatusNotice = "No artifacts found yet.";
        }
        catch
        {
            ArtifactsStatusNotice = AppLocalization.T(
                "Artifacts requires the optional server companion plugin.",
                "产物列表需要服务端安装可选的伴侣扩展插件。");
        }
    }

    public async Task<string?> LoadPreviewHtmlAsync(string remotePath)
    {
        if (_restClient is null || string.IsNullOrWhiteSpace(remotePath)) return null;
        if (_previewHtmlCache.TryGetValue(remotePath, out var cached)) return cached;
        try
        {
            var content = await _restClient.ReadWorkspaceTextAsync(remotePath, ActiveSession?.Profile ?? SelectedProfile?.Name);
            if (!string.IsNullOrWhiteSpace(content))
            {
                _previewHtmlCache[remotePath] = content;
            }
            return content;
        }
        catch
        {
            return null;
        }
    }

    [RelayCommand]
    public async Task OpenInAppPreviewAsync(string remotePath)
    {
        if (string.IsNullOrWhiteSpace(remotePath)) return;
        var isHtml = remotePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            || remotePath.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);
        if (!isHtml)
        {
            await OpenPreviewFileAsync(remotePath);
            return;
        }

        IsBesideVisible = false;
        HtmlPreviewFilePath = remotePath;
        HtmlPreviewTitle = Path.GetFileName(remotePath);
        IsHtmlPreviewPaneOpen = true;

        var html = await LoadPreviewHtmlAsync(remotePath);
        if (!string.IsNullOrWhiteSpace(html))
        {
            HtmlPreviewContent = html;
            HtmlPreviewContentLoaded?.Invoke(html);
        }
        else
        {
            StatusBanner = $"Could not load preview: {remotePath}";
        }
    }

    [RelayCommand]
    private void CloseHtmlPreviewPane() => IsHtmlPreviewPaneOpen = false;

    [RelayCommand]
    private void ZoomInHtmlPreview()
    {
        var idx = Array.FindLastIndex(HtmlZoomSteps, s => s <= HtmlPreviewZoom + 0.001);
        if (idx < HtmlZoomSteps.Length - 1) HtmlPreviewZoom = HtmlZoomSteps[idx + 1];
    }

    [RelayCommand]
    private void ZoomOutHtmlPreview()
    {
        var idx = Array.FindIndex(HtmlZoomSteps, s => s >= HtmlPreviewZoom - 0.001);
        if (idx > 0) HtmlPreviewZoom = HtmlZoomSteps[idx - 1];
    }

    [RelayCommand]
    private void ResetHtmlPreviewZoom()
    {
        HtmlPreviewZoom = Math.Abs(HtmlPreviewZoom - 1.0) < 0.01 ? 0.85 : 1.0;
    }

    [RelayCommand]
    private async Task OpenCurrentPreviewInBrowserAsync()
    {
        if (!string.IsNullOrWhiteSpace(HtmlPreviewFilePath))
        {
            await OpenPreviewFileAsync(HtmlPreviewFilePath);
        }
    }

    private static readonly HashSet<string> BinaryMediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".heic", ".tiff",
        ".mp4", ".mov", ".m4v", ".webm", ".avi", ".mkv",
        ".mp3", ".wav", ".m4a", ".ogg", ".flac", ".aac",
        ".pdf", ".zip"
    };

    public async Task<byte[]?> LoadMediaBytesAsync(string pathOrUrl)
    {
        if (_restClient is null || string.IsNullOrWhiteSpace(pathOrUrl)) return null;
        if (_mediaBytesCache.TryGetValue(pathOrUrl, out var cached)) return cached;
        try
        {
            var bytes = await _restClient.ReadMediaBytesAsync(pathOrUrl, ActiveSession?.Profile ?? SelectedProfile?.Name);
            if (bytes is { Length: > 0 })
            {
                _mediaBytesCache[pathOrUrl] = bytes;
            }
            return bytes;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log("MEDIA_ERR", $"Failed loading media '{pathOrUrl}': {ex.Message}");
            return null;
        }
    }

    [RelayCommand]
    public async Task OpenImageViewerAsync(string pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl)) return;
        ImageViewerUri = pathOrUrl;
        ImageViewerTitle = Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var u) && u.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(u.AbsolutePath) is { Length: > 0 } name ? name : pathOrUrl
            : Path.GetFileName(pathOrUrl) is { Length: > 0 } fname ? fname : pathOrUrl;
        ImageViewerBitmap?.Dispose();
        ImageViewerBitmap = null;
        IsImageViewerOpen = true;

        var bytes = await LoadMediaBytesAsync(pathOrUrl);
        if (bytes is not { Length: > 0 })
        {
            StatusBanner = $"Could not load image: {pathOrUrl}";
            return;
        }
        try
        {
            using var ms = new MemoryStream(bytes);
            ImageViewerBitmap = new Avalonia.Media.Imaging.Bitmap(ms);
        }
        catch (Exception ex)
        {
            StatusBanner = $"Unsupported image format: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CloseImageViewer()
    {
        IsImageViewerOpen = false;
        ImageViewerBitmap?.Dispose();
        ImageViewerBitmap = null;
    }

    [RelayCommand]
    private async Task OpenImageExternallyAsync()
    {
        if (string.IsNullOrWhiteSpace(ImageViewerUri)) return;
        var bytes = await LoadMediaBytesAsync(ImageViewerUri);
        if (bytes is not { Length: > 0 })
        {
            StatusBanner = $"Could not download image: {ImageViewerUri}";
            return;
        }
        var ext = Path.GetExtension(ImageViewerTitle ?? ImageViewerUri);
        if (string.IsNullOrWhiteSpace(ext) || ext.Length > 8) ext = ".png";
        var tempFile = Path.Combine(Path.GetTempPath(), $"hermes-media-{Guid.NewGuid():N}{ext}");
        await File.WriteAllBytesAsync(tempFile, bytes);
        Process.Start(new ProcessStartInfo(tempFile) { UseShellExecute = true });
    }

    [RelayCommand]
    public async Task OpenPreviewFileAsync(string remotePath)
    {
        if (_restClient is null || string.IsNullOrWhiteSpace(remotePath)) return;
        try
        {
            var ext = Path.GetExtension(remotePath);
            if (!string.IsNullOrEmpty(ext) && BinaryMediaExtensions.Contains(ext))
            {
                var bytes = await LoadMediaBytesAsync(remotePath);
                if (bytes is not { Length: > 0 })
                {
                    StatusBanner = $"Could not download file: {remotePath}";
                    return;
                }
                var tempMedia = Path.Combine(Path.GetTempPath(), $"hermes-file-{Guid.NewGuid():N}{ext}");
                await File.WriteAllBytesAsync(tempMedia, bytes);
                Process.Start(new ProcessStartInfo(tempMedia) { UseShellExecute = true });
                return;
            }

            if (!_previewHtmlCache.TryGetValue(remotePath, out var content))
            {
                content = await _restClient.ReadWorkspaceTextAsync(remotePath, ActiveSession?.Profile ?? SelectedProfile?.Name);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    _previewHtmlCache[remotePath] = content;
                }
            }
            if (remotePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                || remotePath.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
            {
                var tempHtml = Path.Combine(Path.GetTempPath(), $"hermes-preview-{Guid.NewGuid():N}.html");
                await File.WriteAllTextAsync(tempHtml, content);
                Process.Start(new ProcessStartInfo(tempHtml) { UseShellExecute = true });
                return;
            }
            PreviewTitle = Path.GetFileName(remotePath);
            PreviewText = content;
            IsPreviewModalOpen = true;
        }
        catch (Exception ex)
        {
            StatusBanner = $"Preview failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenArtifactItem(ArtifactSummary artifact)
    {
        if (!string.IsNullOrWhiteSpace(artifact.Path))
        {
            _ = OpenPreviewFileAsync(artifact.Path);
        }
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo(DataDirectoryPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusBanner = $"Cannot open folder: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenDiagnosticsLog()
    {
        try
        {
            var path = AppDiagnostics.LogFilePath;
            if (!File.Exists(path)) AppDiagnostics.Log("INFO", "Diagnostics log opened by user.");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusBanner = $"Cannot open diagnostics log: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ToggleTabVisibility(V2TabOptionViewModel item)
    {
        if (_tabLayout.SetHidden(item.Tab, item.IsVisible))
        {
            _settings.V2TabTokens = [.. _tabLayout.ToStoredTokens()];
            _cache.SaveSettings(_settings);
            RebuildTabCollections();
        }
    }

    [RelayCommand]
    private void MoveTabUp(V2TabOptionViewModel item)
    {
        var idx = _tabLayout.Order.ToList().IndexOf(item.Tab);
        if (idx <= 0) return;
        _tabLayout.Move(idx, idx - 1);
        _settings.V2TabTokens = [.. _tabLayout.ToStoredTokens()];
        _cache.SaveSettings(_settings);
        RebuildTabCollections();
    }

    [RelayCommand]
    private void SelectSystemLanguage() => SetLanguageMode(AppLanguageMode.System);

    [RelayCommand]
    private void SelectEnglishLanguage() => SetLanguageMode(AppLanguageMode.English);

    [RelayCommand]
    private void SelectChineseLanguage() => SetLanguageMode(AppLanguageMode.Chinese);

    [RelayCommand]
    private void SelectSystemTheme() => ApplyThemePreference("system", save: true);

    [RelayCommand]
    private void SelectDarkTheme() => ApplyThemePreference("dark", save: true);

    [RelayCommand]
    private void SelectLightTheme() => ApplyThemePreference("light", save: true);

    private void ApplyThemePreference(string? token, bool save)
    {
        var norm = token?.Trim().ToLowerInvariant() switch
        {
            "dark" => "dark",
            "light" => "light",
            _ => "system"
        };
        _settings.ThemePreference = norm;
        if (save) _cache.SaveSettings(_settings);

        if (Avalonia.Application.Current is not null)
        {
            Avalonia.Application.Current.RequestedThemeVariant = norm switch
            {
                "dark" => Avalonia.Styling.ThemeVariant.Dark,
                "light" => Avalonia.Styling.ThemeVariant.Light,
                _ => Avalonia.Styling.ThemeVariant.Default
            };
        }

        OnPropertyChanged(nameof(IsSystemTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsSystemLanguage));
        OnPropertyChanged(nameof(IsEnglishLanguage));
        OnPropertyChanged(nameof(IsChineseLanguage));
        OnPropertyChanged(nameof(UseSessionTokenAuth));
        OnPropertyChanged(nameof(IsSoulPersonalitySubTab));
        OnPropertyChanged(nameof(IsSoulCapabilitiesSubTab));
        foreach (var t in VisibleTabs) t.RefreshLocalization();
        foreach (var p in ProfileChips) p.RefreshLocalization();
    }

    private void SetLanguageMode(AppLanguageMode mode)
    {
        _settings.LanguagePreference = AppLocalization.ToToken(mode);
        _cache.SaveSettings(_settings);
        AppLocalization.SetMode(mode);
        OnLanguageChanged();
    }

    private void OnLanguageChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(IsSystemLanguage));
            OnPropertyChanged(nameof(IsEnglishLanguage));
            OnPropertyChanged(nameof(IsChineseLanguage));
            OnPropertyChanged(nameof(SoulEditorHeaderTitle));
            OnPropertyChanged(nameof(SoulEditorPrimaryActionLabel));
            OnPropertyChanged(nameof(SoulCharCountLabel));
            OnPropertyChanged(nameof(ActiveProfileDisplayLabel));
            OnPropertyChanged(nameof(ActiveSessionDisplayTitle));
            OnPropertyChanged(nameof(BesideSessionDisplayTitle));
            OnPropertyChanged(nameof(CronEditorHeaderTitle));
            OnPropertyChanged(nameof(CronEditorPrimaryActionLabel));
            OnPropertyChanged(nameof(ConnectPrimaryActionLabel));
            OnPropertyChanged(nameof(EditingServerBanner));
            OnPropertyChanged(nameof(PendingDeleteSessionPrompt));

            foreach (var t in VisibleTabs) t.RefreshLocalization();
            foreach (var t in AllConfigurableTabs) t.RefreshLocalization();
            foreach (var p in ProfileChips) p.RefreshLocalization();
            foreach (var m in ActiveMessages) m.RefreshLocalization();
            foreach (var m in BesideMessages) m.RefreshLocalization();
            RecomputeTranscriptPresentation(ActiveMessages, IsRunningTurn);
            RecomputeTranscriptPresentation(BesideMessages, isTurnRunning: false);
        });
    }

    private void RebuildTabCollections()
    {
        VisibleTabs.Clear();
        foreach (var t in _tabLayout.Shown)
        {
            VisibleTabs.Add(new V2TabOptionViewModel(t, true, t == SelectedTab, _tabLayout.CanHide(t)));
        }

        AllConfigurableTabs.Clear();
        foreach (var t in _tabLayout.Order)
        {
            AllConfigurableTabs.Add(new V2TabOptionViewModel(t, !_tabLayout.Hidden.Contains(t), t == SelectedTab, _tabLayout.CanHide(t)));
        }
    }

    partial void OnIsRunningTurnChanged(bool value)
    {
        _turnWatchdogCts?.Cancel();
        if (!value) return;

        var cts = new CancellationTokenSource();
        _turnWatchdogCts = cts;
        _lastActiveStreamEventAt = DateTimeOffset.UtcNow;
        _ = MonitorRunningTurnAsync(cts.Token);
    }

    private async Task MonitorRunningTurnAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && IsRunningTurn)
        {
            try
            {
                await Task.Delay(5000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            if (ct.IsCancellationRequested || !IsRunningTurn) break;

            // Only poll REST if no WS stream frame has arrived recently
            if ((DateTimeOffset.UtcNow - _lastActiveStreamEventAt).TotalSeconds < 4.5) continue;
            if (ActiveSession is null) continue;

            try
            {
                if (_wsClient is { IsConnected: false })
                {
                    await EnsureWsReconnectedForActiveSessionAsync(ActiveSession.Profile ?? SelectedProfile?.Name);
                }
                else if (_wsClient is { IsConnected: true })
                {
                    var resumeKey = ActiveSession.StoredSessionId ?? ActiveSession.Id;
                    var resume = await _wsClient.OpenSessionAsync(resumeKey, ActiveSession.Profile ?? SelectedProfile?.Name, ct: ct);
                    if (!resume.IsRunning)
                    {
                        AppDiagnostics.Log("TURN_SYNC", $"Turn completed quietly on server for {resumeKey}; syncing messages.");
                        await RefreshActiveSessionMessagesAsync();
                        IsRunningTurn = false;
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AppDiagnostics.Log("TURN_SYNC_ERR", ex.Message);
            }
        }
    }

    private async Task RefreshActiveSessionMessagesAsync()
    {
        if (_restClient is null || ActiveSession is null) return;
        var session = ActiveSession;
        var historyKey = session.StoredSessionId ?? session.Id;
        var targetProfile = session.Profile ?? SelectedProfile?.Name;
        try
        {
            var messages = await _restClient.GetSessionMessagesAsync(historyKey, targetProfile);
            if (ActiveSession != session) return;

            var lastLocalCount = ActiveMessages.Count;
            if (messages.Count >= lastLocalCount || !(_streamingAssistantMessage?.IsStreaming ?? false))
            {
                ReconcileMessagesInPlace(ActiveMessages, messages);
                RecomputeTranscriptPresentation(ActiveMessages, IsRunningTurn);
                TranscriptScrollRequested?.Invoke(false);
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log("REST_SYNC_ERR", ex.Message);
        }
    }

    private static void ReconcileMessagesInPlace(
        ObservableCollection<MessageItemViewModel> target,
        IReadOnlyList<ChatMessage> incoming)
    {
        var common = 0;
        var limit = Math.Min(target.Count, incoming.Count);
        while (common < limit)
        {
            var existing = target[common];
            var next = incoming[common];
            if (existing.Role != next.Role || !string.Equals(existing.Id, next.Id, StringComparison.Ordinal))
            {
                break;
            }
            if (!string.Equals(existing.Content, next.Content, StringComparison.Ordinal))
            {
                existing.Content = next.Content;
            }
            if (!string.Equals(existing.Reasoning, next.Reasoning, StringComparison.Ordinal))
            {
                existing.Reasoning = next.Reasoning;
            }
            if (existing.Tool != next.Tool)
            {
                existing.Tool = next.Tool;
            }
            if (existing.Approval != next.Approval)
            {
                existing.Approval = next.Approval;
            }
            if (existing.Clarify != next.Clarify)
            {
                existing.Clarify = next.Clarify;
            }
            existing.IsStreaming = false;
            common++;
        }

        while (target.Count > common)
        {
            target.RemoveAt(target.Count - 1);
        }

        for (var i = common; i < incoming.Count; i++)
        {
            target.Add(new MessageItemViewModel(incoming[i]));
        }
    }

    private void OnWsDisconnected(string? reason)
    {
        AppDiagnostics.Log("WS_DISC", reason ?? "(no reason)");
        Dispatcher.UIThread.Post(() =>
        {
            ConnectionPhase = ConnectionPhase.Disconnected;
            StatusBanner = string.IsNullOrEmpty(reason) ? "Disconnected from Hermes Gateway" : $"Disconnected: {reason}";
        });
    }

    private void OnWsEventReceived(StreamEvent ev)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                HandleStreamEventOnUi(ev);
            }
            catch (Exception ex)
            {
                AppDiagnostics.Log("UI_STREAM_ERR", ex.ToString());
            }
        });
    }

    private void HandleStreamEventOnUi(StreamEvent ev)
    {
        if (ev is SessionsChangedEvent)
        {
            _ = RefreshSessionsAsync();
            return;
        }

        if (ev is SessionTitleEvent titleEv)
        {
            var match = _allSessions.FirstOrDefault(s =>
                s.Id == titleEv.StoredSessionId
                || s.Id == titleEv.RuntimeSessionId
                || s.RuntimeSessionId == titleEv.RuntimeSessionId);
            if (match is not null)
            {
                match.Title = titleEv.Title;
                ApplySessionFilter();
            }
            return;
        }

        var isMain = ActiveSession is not null && MatchesSession(ActiveSession, ev.SessionId, _activeRuntimeSessionId);
        var isBeside = BesideSession is not null && MatchesSession(BesideSession, ev.SessionId, BesideSession.RuntimeSessionId);

        if (isBeside)
        {
            ApplyEventToTranscript(BesideMessages, ev, trackTools: false);
        }
        if (!isMain)
        {
            if (ev is ApprovalRequestEvent or ClarifyRequestEvent)
            {
                NeedsYouCount++;
                _notifications.ShowNeedsAttention("Hermes", "A background session requires your approval or input.");
            }
            return;
        }

        _lastActiveStreamEventAt = DateTimeOffset.UtcNow;

        switch (ev)
        {
            case SessionBusyEvent busy:
                IsRunningTurn = busy.Busy;
                if (!busy.Busy)
                {
                    FlushStreamingDeltas();
                    if (_streamingAssistantMessage is not null) _streamingAssistantMessage.IsStreaming = false;
                    _streamingAssistantMessage = null;
                    if (ActiveMessages.Count == 0 || ActiveMessages[^1].IsUser)
                    {
                        _ = RefreshActiveSessionMessagesAsync();
                    }
                }
                break;
            case ContextUpdateEvent ctx:
                ContextPercent = ctx.Percent;
                ContextUsed = ctx.Used;
                ContextMax = ctx.Max;
                break;
            case CwdUpdateEvent cwd:
                CurrentCwd = cwd.Cwd;
                break;
            case ModelUpdateEvent model:
                if (!string.IsNullOrWhiteSpace(model.Model)) CurrentModelLabel = model.Model;
                break;
            default:
                ApplyEventToTranscript(ActiveMessages, ev, trackTools: true);
                break;
        }
    }

    private void FlushStreamingDeltas()
    {
        if (_streamContentBuffer.Length == 0 && _streamReasoningBuffer.Length == 0)
        {
            _streamFlushTimer.Stop();
            return;
        }

        if (ActiveSession is null
            || string.IsNullOrEmpty(_streamBufferSessionId)
            || !MatchesSession(ActiveSession, _streamBufferSessionId, _activeRuntimeSessionId))
        {
            _streamContentBuffer.Clear();
            _streamReasoningBuffer.Clear();
            _streamBufferSessionId = null;
            _streamFlushTimer.Stop();
            return;
        }

        AppDiagnostics.SetPhase("FlushStreamingDeltas", $"contentLen={_streamContentBuffer.Length}, reasonLen={_streamReasoningBuffer.Length}");
        var msg = _streamingAssistantMessage ?? EnsureStreamingAssistant(ActiveMessages);
        var updated = false;
        if (_streamReasoningBuffer.Length > 0)
        {
            msg.Reasoning = (msg.Reasoning ?? string.Empty) + _streamReasoningBuffer.ToString();
            _streamReasoningBuffer.Clear();
            updated = true;
        }
        if (_streamContentBuffer.Length > 0)
        {
            msg.Content += _streamContentBuffer.ToString();
            _streamContentBuffer.Clear();
            updated = true;
        }
        if (updated)
        {
            TranscriptScrollRequested?.Invoke(false);
        }
        AppDiagnostics.SetPhase("idle");
    }

    private void ApplyEventToTranscript(ObservableCollection<MessageItemViewModel> target, StreamEvent ev, bool trackTools)
    {
        switch (ev)
        {
            case MessageStartEvent:
                if (trackTools) IsRunningTurn = true;
                EnsureStreamingAssistant(target);
                break;

            case MessageDeltaEvent delta:
                if (target == ActiveMessages)
                {
                    if (!string.IsNullOrEmpty(_streamBufferSessionId)
                        && !string.Equals(_streamBufferSessionId, delta.SessionId, StringComparison.Ordinal))
                    {
                        FlushStreamingDeltas();
                    }
                    _streamBufferSessionId = delta.SessionId;
                    EnsureStreamingAssistant(target);
                    _streamContentBuffer.Append(delta.Text);
                    if (!_streamFlushTimer.IsEnabled) _streamFlushTimer.Start();
                }
                else
                {
                    var msg = EnsureStreamingAssistant(target);
                    msg.Content += delta.Text;
                }
                break;

            case ReasoningDeltaEvent reason:
                if (target == ActiveMessages)
                {
                    if (!string.IsNullOrEmpty(_streamBufferSessionId)
                        && !string.Equals(_streamBufferSessionId, reason.SessionId, StringComparison.Ordinal))
                    {
                        FlushStreamingDeltas();
                    }
                    _streamBufferSessionId = reason.SessionId;
                    EnsureStreamingAssistant(target);
                    _streamReasoningBuffer.Append(reason.Text);
                    if (!_streamFlushTimer.IsEnabled) _streamFlushTimer.Start();
                }
                else
                {
                    var rMsg = EnsureStreamingAssistant(target);
                    rMsg.Reasoning = (rMsg.Reasoning ?? string.Empty) + reason.Text;
                }
                break;

            case MessageInterimEvent interim when !interim.AlreadyStreamed:
            {
                var interimMsg = EnsureStreamingAssistant(target);
                if (string.IsNullOrEmpty(interimMsg.Content)) interimMsg.Content = interim.Text;
                else if (!interimMsg.Content.EndsWith(interim.Text, StringComparison.Ordinal)) interimMsg.Content += "\n" + interim.Text;
                interimMsg.IsStreaming = false;
                if (target == ActiveMessages) _streamingAssistantMessage = null;
                break;
            }

            case MessageCompleteEvent complete:
                if (target == ActiveMessages) FlushStreamingDeltas();
                if (_streamingAssistantMessage is not null && target == ActiveMessages)
                {
                    if (!string.IsNullOrEmpty(complete.Content)) _streamingAssistantMessage.Content = complete.Content;
                    if (!string.IsNullOrEmpty(complete.Reasoning)) _streamingAssistantMessage.Reasoning = complete.Reasoning;
                    _streamingAssistantMessage.IsStreaming = false;
                    _streamingAssistantMessage = null;
                }
                else if (!string.IsNullOrEmpty(complete.Content))
                {
                    target.Add(new MessageItemViewModel(new ChatMessage
                    {
                        Id = complete.MessageId ?? Guid.NewGuid().ToString("N"),
                        Role = MessageRole.Assistant,
                        Content = complete.Content,
                        Reasoning = complete.Reasoning,
                        Timestamp = DateTimeOffset.Now.ToString("HH:mm")
                    }));
                }
                else if (target == ActiveMessages && (ActiveMessages.Count == 0 || ActiveMessages[^1].IsUser))
                {
                    _ = RefreshActiveSessionMessagesAsync();
                }
                if (trackTools) IsRunningTurn = false;
                if (target == ActiveMessages) TranscriptScrollRequested?.Invoke(false);
                else if (target == BesideMessages) BesideScrollRequested?.Invoke(false);
                break;

            case MessageInterruptedEvent:
                if (target == ActiveMessages) FlushStreamingDeltas();
                if (_streamingAssistantMessage is not null) _streamingAssistantMessage.IsStreaming = false;
                _streamingAssistantMessage = null;
                if (trackTools) IsRunningTurn = false;
                break;

            case MessageErrorEvent err:
                if (target == ActiveMessages) FlushStreamingDeltas();
                if (_streamingAssistantMessage is not null) _streamingAssistantMessage.IsStreaming = false;
                _streamingAssistantMessage = null;
                if (trackTools) IsRunningTurn = false;
                target.Add(new MessageItemViewModel(new ChatMessage
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Role = MessageRole.System,
                    Content = $"Error: {err.Message}",
                    Timestamp = DateTimeOffset.Now.ToString("HH:mm")
                }));
                if (target == ActiveMessages) TranscriptScrollRequested?.Invoke(false);
                else if (target == BesideMessages) BesideScrollRequested?.Invoke(false);
                break;

            case ToolStartEvent tStart when trackTools:
                FlushStreamingDeltas();
                if (_streamingAssistantMessage is not null) _streamingAssistantMessage.IsStreaming = false;
                _streamingAssistantMessage = null;
                var toolMsg = new MessageItemViewModel(new ChatMessage
                {
                    Id = tStart.ToolId ?? Guid.NewGuid().ToString("N"),
                    Role = MessageRole.Tool,
                    Content = tStart.ToolName,
                    Tool = new ToolActivity(tStart.ToolId, tStart.ToolName, tStart.ToolInput, null, ToolStatus.Running),
                    Timestamp = DateTimeOffset.Now.ToString("HH:mm")
                });
                if (!string.IsNullOrEmpty(tStart.ToolId)) _activeTools[tStart.ToolId] = toolMsg;
                target.Add(toolMsg);
                TranscriptScrollRequested?.Invoke(false);
                break;

            case ToolCompleteEvent tEnd when trackTools:
                if (!string.IsNullOrEmpty(tEnd.ToolId) && _activeTools.TryGetValue(tEnd.ToolId, out var existingTool) && existingTool.Tool is not null)
                {
                    existingTool.Tool = existingTool.Tool with { Output = tEnd.ToolOutput, Status = ToolStatus.Complete };
                }
                else
                {
                    target.Add(new MessageItemViewModel(new ChatMessage
                    {
                        Id = tEnd.ToolId ?? Guid.NewGuid().ToString("N"),
                        Role = MessageRole.Tool,
                        Content = tEnd.ToolName,
                        Tool = new ToolActivity(tEnd.ToolId, tEnd.ToolName, null, tEnd.ToolOutput, ToolStatus.Complete),
                        Timestamp = DateTimeOffset.Now.ToString("HH:mm")
                    }));
                }
                TranscriptScrollRequested?.Invoke(false);
                break;

            case ApprovalRequestEvent approval:
                NeedsYouCount++;
                target.Add(new MessageItemViewModel(new ChatMessage
                {
                    Id = $"apr-{approval.Activity.RequestId}",
                    Role = MessageRole.Approval,
                    Content = approval.Activity.Command,
                    Approval = approval.Activity,
                    Timestamp = DateTimeOffset.Now.ToString("HH:mm")
                }));
                _notifications.ShowNeedsAttention(ActiveSession?.Title ?? "Hermes", $"Approve command: {approval.Activity.Command}");
                if (target == ActiveMessages) TranscriptScrollRequested?.Invoke(false);
                break;

            case ApprovalWithdrawnEvent withdrawn:
                foreach (var item in target)
                {
                    if (item.Approval?.RequestId == withdrawn.RequestId)
                    {
                        item.Approval = item.Approval with { IsWithdrawn = true };
                        NeedsYouCount = Math.Max(0, NeedsYouCount - 1);
                    }
                }
                break;

            case ClarifyRequestEvent clarify:
                NeedsYouCount++;
                target.Add(new MessageItemViewModel(new ChatMessage
                {
                    Id = $"clr-{clarify.Activity.RequestId}",
                    Role = MessageRole.Clarify,
                    Content = clarify.Activity.Question,
                    Clarify = clarify.Activity,
                    Timestamp = DateTimeOffset.Now.ToString("HH:mm")
                }));
                _notifications.ShowNeedsAttention(ActiveSession?.Title ?? "Hermes", clarify.Activity.Question);
                if (target == ActiveMessages) TranscriptScrollRequested?.Invoke(false);
                break;

            case ClarifyExpireEvent expire:
                foreach (var item in target)
                {
                    if (item.Clarify?.RequestId == expire.RequestId)
                    {
                        item.Clarify = item.Clarify with { IsExpired = true };
                        NeedsYouCount = Math.Max(0, NeedsYouCount - 1);
                    }
                }
                break;
        }

        if (ev is not MessageDeltaEvent)
        {
            RecomputeTranscriptPresentation(target, target == ActiveMessages && IsRunningTurn);
        }
    }

    [RelayCommand]
    private void ToggleStepRun(MessageItemViewModel? header)
    {
        if (header is null || string.IsNullOrEmpty(header.RunGroupId)) return;
        var nextExpanded = !header.IsRunExpanded;
        if (nextExpanded)
        {
            _userExpandedRunIds.Add(header.RunGroupId);
            _userCollapsedRunIds.Remove(header.RunGroupId);
        }
        else
        {
            _userExpandedRunIds.Remove(header.RunGroupId);
            _userCollapsedRunIds.Add(header.RunGroupId);
        }

        RecomputeTranscriptPresentation(ActiveMessages, IsRunningTurn);
        if (IsBesideVisible) RecomputeTranscriptPresentation(BesideMessages, isTurnRunning: false);
    }

    private void RecomputeTranscriptPresentation(ObservableCollection<MessageItemViewModel> list, bool isTurnRunning)
    {
        var n = list.Count;
        if (n == 0) return;

        var isProcessNarration = new bool[n];
        var isRunHeader = new bool[n];
        var isInStepRun = new bool[n];
        var isRunExpanded = new bool[n];
        var isRunLive = new bool[n];
        var runSummary = new string[n];
        var isHiddenInCollapsedRun = new bool[n];
        var runGroupIds = new string?[n];

        var lastUserIndex = -1;
        for (var i = 0; i < n; i++)
        {
            var m = list[i];
            if (m.IsUser) lastUserIndex = i;

            if (m.IsAssistantBubble)
            {
                var followedByStep = false;
                if (i + 1 < n)
                {
                    var next = list[i + 1];
                    followedByStep = next.IsReasoningOnly || next.IsTool;
                }
                isProcessNarration[i] = TranscriptRunHelper.IsPreToolNarration(
                    m.Content, m.HasAttachments, m.IsStreaming, followedByStep);
            }
        }

        // Pass 2: group contiguous process items (ReasoningOnly, StandardTool, ProcessNarration)
        var idx = 0;
        while (idx < n)
        {
            if (!IsProcessRunCandidate(list[idx], isProcessNarration[idx]))
            {
                idx++;
                continue;
            }

            var start = idx;
            while (idx < n && IsProcessRunCandidate(list[idx], isProcessNarration[idx]))
            {
                idx++;
            }
            var end = idx - 1;
            var count = end - start + 1;
            var runId = list[start].Id;
            // Keep any run belonging to the currently active turn expanded while the turn is running
            // so thinking/tool calls do not collapse mid-turn when a partial reply begins.
            var isLiveRun = isTurnRunning && (start > lastUserIndex || end == n - 1);

            if (count >= 2)
            {
                var expanded = _userExpandedRunIds.Contains(runId)
                    || (!_userCollapsedRunIds.Contains(runId) && isLiveRun);

                var distinctLabels = new List<string>();
                for (var k = start; k <= end; k++)
                {
                    var step = list[k];
                    string? label = null;
                    if (step.IsReasoningOnly) label = Loc.Thought;
                    else if (step.IsStandardTool) label = step.ToolDisplayTitle;
                    if (!string.IsNullOrWhiteSpace(label) && !distinctLabels.Contains(label, StringComparer.Ordinal))
                        distinctLabels.Add(label);
                }

                var summary = TranscriptRunHelper.FormatStepRunSummary(count, distinctLabels, isLiveRun);
                for (var k = start; k <= end; k++)
                {
                    runGroupIds[k] = runId;
                    isInStepRun[k] = true;
                    isRunHeader[k] = k == start;
                    isRunExpanded[k] = expanded;
                    isRunLive[k] = isLiveRun;
                    runSummary[k] = k == start ? summary : string.Empty;
                    isHiddenInCollapsedRun[k] = !expanded;
                }
            }
            else
            {
                runGroupIds[start] = runId;
                isInStepRun[start] = true;
            }
        }

        // Apply only changed fields so prior turns never toggle visibility true->false->true on every event.
        for (var i = 0; i < n; i++)
        {
            var m = list[i];
            m.RunGroupId = runGroupIds[i];
            m.IsProcessNarration = isProcessNarration[i];
            m.IsInStepRun = isInStepRun[i];
            m.IsRunHeader = isRunHeader[i];
            m.IsRunExpanded = isRunExpanded[i];
            m.IsRunLive = isRunLive[i];
            m.RunSummaryText = runSummary[i];
            m.IsHiddenInCollapsedRun = isHiddenInCollapsedRun[i];
        }
    }

    private static bool IsProcessRunCandidate(MessageItemViewModel item, bool isProcessNarration) =>
        item.IsReasoningOnly || item.IsStandardTool || (item.IsAssistantBubble && isProcessNarration);

    private MessageItemViewModel EnsureStreamingAssistant(ObservableCollection<MessageItemViewModel> target)
    {
        if (target == ActiveMessages && _streamingAssistantMessage is not null)
        {
            return _streamingAssistantMessage;
        }
        if (target.Count > 0 && target[^1] is { Role: MessageRole.Partial, IsStreaming: true } tail)
        {
            return tail;
        }
        var vm = new MessageItemViewModel(new ChatMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Role = MessageRole.Partial,
            Content = string.Empty,
            Timestamp = DateTimeOffset.Now.ToString("HH:mm")
        })
        {
            IsStreaming = true
        };
        target.Add(vm);
        if (target == ActiveMessages) _streamingAssistantMessage = vm;
        return vm;
    }

    private static bool MatchesSession(SessionSummary session, string eventSessionId, string? activeRuntimeId) =>
        !string.IsNullOrEmpty(eventSessionId)
        && (eventSessionId == activeRuntimeId
            || session.Id == eventSessionId
            || session.StoredSessionId == eventSessionId
            || session.RuntimeSessionId == eventSessionId
            || session.AlternateIds.Contains(eventSessionId));

    private string DraftKey(string sessionId) => $"{_activeDashboard?.Id}:{sessionId}";

    #region Hermes Host Maintenance & Updates

    [RelayCommand]
    private async Task CheckHermesUpdateAsync() => await CheckHermesUpdateInternalAsync(force: true);

    [RelayCommand]
    private void RequestHermesUpdate()
    {
        if (!IsServerMaintenanceIdle || !CanApplyHermesUpdate) return;
        HasHermesUpdateConfirm = true;
    }

    [RelayCommand]
    private void CancelHermesUpdateConfirm() => HasHermesUpdateConfirm = false;

    [RelayCommand]
    private async Task ConfirmHermesUpdateAsync()
    {
        HasHermesUpdateConfirm = false;
        if (!IsServerMaintenanceIdle || !await EnsureRestClientReadyAsync()) return;
        if (_restClient is null) return;

        _serverMaintenanceCts?.Cancel();
        _serverMaintenanceCts = new CancellationTokenSource();
        var ct = _serverMaintenanceCts.Token;

        IsHermesUpdateRunning = true;
        CanApplyHermesUpdate = false;
        HermesUpdateManualCommand = null;
        HermesUpdateDetailText = null;
        HermesUpdateStatusText = Loc.HermesUpdating;

        try
        {
            var start = await _restClient.StartHermesUpdateAsync(ct);
            if (!start.Ok)
            {
                HermesUpdateStatusText = Loc.HermesManualUpdate;
                HermesUpdateDetailText = start.Message;
                HermesUpdateManualCommand = start.UpdateCommand;
                return;
            }

            await PollHermesUpdateActionAsync(start.ActionName, start.ActionId, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented)
        {
            HermesUpdateStatusText = Loc.HermesUpdateUnsupported;
        }
        catch (OperationCanceledException)
        {
            // Ignored when dashboard switched or disposed.
        }
        catch (Exception ex)
        {
            HermesUpdateStatusText = AppLocalization.T("Couldn't update Hermes", "Hermes 更新失败");
            HermesUpdateDetailText = ex.Message;
        }
        finally
        {
            IsHermesUpdateRunning = false;
        }
    }

    private async Task PollHermesUpdateActionAsync(string actionName, string? actionId, CancellationToken ct)
    {
        if (_restClient is null) return;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(900);
        var restarting = false;
        string? latestLine = null;

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(1500, ct);
            ActionStatusSnapshot? status = null;
            try
            {
                status = await _restClient.GetActionStatusAsync(actionName, lines: 40, ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                if (await TryRenewDashboardClientAsync(ct)) continue;
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // Dashboard restarting on the new version answers nothing briefly.
                if (!restarting)
                {
                    restarting = true;
                    var comeBack = DateTimeOffset.UtcNow.AddSeconds(240);
                    if (comeBack > deadline) deadline = comeBack;
                }
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    HermesUpdateStatusText = AppLocalization.T(
                        "Hermes isn't back yet. Check again in a few minutes.",
                        "Hermes 尚未恢复响应，请稍后再检查状态。");
                    return;
                }
                HermesUpdateStatusText = Loc.HermesRestarting;
                continue;
            }

            if (status is null) continue;
            latestLine = status.LatestLine ?? latestLine;
            if (!string.IsNullOrWhiteSpace(latestLine)) HermesUpdateDetailText = latestLine;

            var verdict = HermesRestClient.EvaluateHermesUpdateVerdict(status, actionId);
            if (verdict == ActionPollVerdict.Running)
            {
                restarting = false;
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    HermesUpdateStatusText = AppLocalization.T(
                        "Hermes update is still running in the background; click Check for Updates shortly.",
                        "服务端仍在后台更新，请稍后点击「检查更新」查看进度。");
                    return;
                }
                HermesUpdateStatusText = Loc.HermesUpdating;
                continue;
            }

            if (verdict == ActionPollVerdict.Succeeded)
            {
                HermesUpdateCommitsSummary = null;
                HermesUpdateStatusText = Loc.HermesUpdated;
                if (!IsConnected && _activeDashboard is not null)
                {
                    await TryReconnectSavedAsync(_activeDashboard);
                }
                await CheckHermesUpdateInternalAsync(force: true, skipRunningActionCheck: true);
                return;
            }

            if (verdict == ActionPollVerdict.Failed)
            {
                HermesUpdateStatusText = AppLocalization.T("Couldn't update Hermes", "Hermes 更新失败");
                return;
            }

            // Unknown: dashboard restarted cleanly before receipt settled.
            if (!restarting)
            {
                restarting = true;
                var comeBack = DateTimeOffset.UtcNow.AddSeconds(240);
                if (comeBack > deadline) deadline = comeBack;
            }
            if (DateTimeOffset.UtcNow >= deadline)
            {
                HermesUpdateStatusText = AppLocalization.T(
                    "Hermes isn't back yet. Check again in a few minutes.",
                    "Hermes 尚未恢复响应，请稍后再检查状态。");
                return;
            }
            HermesUpdateStatusText = Loc.HermesRestarting;
        }
    }

    [RelayCommand]
    private async Task RestartGatewayAsync()
    {
        if (!IsServerMaintenanceIdle || !await EnsureRestClientReadyAsync()) return;
        if (_restClient is null) return;

        _serverMaintenanceCts?.Cancel();
        _serverMaintenanceCts = new CancellationTokenSource();
        var ct = _serverMaintenanceCts.Token;

        IsGatewayRestartRunning = true;
        GatewayMaintainStatusText = Loc.GatewayRestarting;

        try
        {
            int? priorPid = null;
            try
            {
                var before = await _restClient.GetGatewayStatusAsync(ct);
                priorPid = before.Pid;
            }
            catch { /* Ignore preflight status errors */ }

            await _restClient.RestartGatewayAsync(ct);

            var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
            var sawRestartTransition = false;
            while (!ct.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(1500, ct);

                ActionStatusSnapshot? action = null;
                try
                {
                    action = await _restClient.GetActionStatusAsync("gateway-restart", lines: 20, ct);
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    await TryRenewDashboardClientAsync(ct);
                }
                catch { /* Dashboard may restart together with gateway */ }

                if (action?.LatestLine is { } line)
                    GatewayMaintainStatusText = $"{Loc.GatewayRestarting} ({line})";

                if (action is { Running: false, ExitCode: > 0 })
                {
                    GatewayMaintainStatusText = AppLocalization.T(
                        $"Gateway restart exited with code {action.ExitCode}",
                        $"网关重启返回错误码 {action.ExitCode}");
                    return;
                }

                try
                {
                    var gw = await _restClient.GetGatewayStatusAsync(ct);
                    if (!gw.Running || (priorPid.HasValue && gw.Pid.HasValue && gw.Pid != priorPid))
                        sawRestartTransition = true;

                    if (gw.IsServing && (sawRestartTransition || action is { Running: false, ExitCode: 0 }))
                    {
                        GatewayMaintainStatusText = Loc.GatewayRestarted;
                        if (!IsConnected && _activeDashboard is not null)
                        {
                            await TryReconnectSavedAsync(_activeDashboard);
                        }
                        return;
                    }
                }
                catch
                {
                    sawRestartTransition = true;
                }
            }

            GatewayMaintainStatusText = AppLocalization.T(
                "Gateway restart requested; verify connection shortly.",
                "已发送重启指令，请稍候确认连接状态。");
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented)
        {
            GatewayMaintainStatusText = AppLocalization.T(
                "This Hermes dashboard version doesn't expose gateway restart.",
                "当前连接的 Hermes 网关版本不支持远程重启接口。");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            GatewayMaintainStatusText = AppLocalization.T($"Gateway restart failed: {ex.Message}", $"网关重启失败：{ex.Message}");
        }
        finally
        {
            IsGatewayRestartRunning = false;
        }
    }

    private async Task CheckHermesUpdateInternalAsync(bool force, bool skipRunningActionCheck = false)
    {
        if (!await EnsureRestClientReadyAsync() || _restClient is null)
        {
            HermesUpdateStatusText = Loc.NotConnectedServer;
            return;
        }

        IsCheckingHermesUpdate = true;
        HasHermesUpdateConfirm = false;
        HermesUpdateStatusText = Loc.HermesCheckingUpdates;
        HermesUpdateDetailText = null;
        HermesUpdateManualCommand = null;
        HermesUpdateCommitsSummary = null;
        CanApplyHermesUpdate = false;

        try
        {
            // If an update is already running on the host (e.g. slow pip/uv build or after reconnect), resume live tracking.
            if (!skipRunningActionCheck)
            {
                try
                {
                    var ongoing = await _restClient.GetActionStatusAsync("hermes-update", lines: 40, CancellationToken.None);
                    if (ongoing.Running)
                    {
                        IsCheckingHermesUpdate = false;
                        IsHermesUpdateRunning = true;
                        HermesUpdateStatusText = Loc.HermesUpdating;
                        if (!string.IsNullOrWhiteSpace(ongoing.LatestLine))
                            HermesUpdateDetailText = ongoing.LatestLine;

                        _serverMaintenanceCts?.Cancel();
                        _serverMaintenanceCts = new CancellationTokenSource();
                        try
                        {
                            await PollHermesUpdateActionAsync("hermes-update", ongoing.ReceiptActionId, _serverMaintenanceCts.Token);
                        }
                        finally
                        {
                            IsHermesUpdateRunning = false;
                        }
                        return;
                    }
                }
                catch (OperationCanceledException) { return; }
                catch { /* Fall through to normal version check */ }
            }

            if (!IsConnected && _activeDashboard is not null)
            {
                await TryReconnectSavedAsync(_activeDashboard);
            }

            var check = await _restClient.GetHermesUpdateCheckAsync(force, CancellationToken.None);
            HermesVersionLabel = string.IsNullOrWhiteSpace(check.CurrentVersion)
                ? (string.IsNullOrWhiteSpace(check.InstallMethod) ? "—" : check.InstallMethod)
                : $"{check.CurrentVersion} · {check.InstallMethod}";

            CanApplyHermesUpdate = check.CanUpdateHere;

            if (check.Commits.Count > 0)
            {
                HermesUpdateCommitsSummary = string.Join("\n", check.Commits.Take(6).Select(c => $"• {c.Summary}"));
            }

            if (check.CanUpdateHere)
            {
                var behindText = check.Behind is > 0
                    ? AppLocalization.T($"Update available ({check.Behind} commits behind)", $"发现新版本（落后 {check.Behind} 个提交）")
                    : AppLocalization.T("Update available", "发现新版本");
                HermesUpdateStatusText = behindText;
                if (!string.IsNullOrWhiteSpace(check.RollbackTo))
                {
                    HermesUpdateDetailText = AppLocalization.T(
                        $"Updating moves this host to release {check.RollbackTo}.",
                        $"更新将把宿主机切换到 {check.RollbackTo} 版本。");
                }
            }
            else if (check.UpdateAvailable || (!check.CanApply && !string.IsNullOrWhiteSpace(check.UpdateCommand)))
            {
                HermesUpdateStatusText = check.UpdateAvailable
                    ? AppLocalization.T("Update available (manual host update required)", "发现新版本（需在宿主机手动更新）")
                    : Loc.HermesManualUpdate;
                HermesUpdateDetailText = check.Message;
                HermesUpdateManualCommand = check.UpdateCommand;
            }
            else if (check.Behind is null)
            {
                HermesUpdateStatusText = AppLocalization.T("Couldn't check for updates", "无法检查更新");
                HermesUpdateDetailText = check.Message;
            }
            else
            {
                HermesUpdateStatusText = Loc.HermesUpToDate;
                HermesUpdateDetailText = check.Message;
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented)
        {
            HermesUpdateStatusText = Loc.HermesUpdateUnsupported;
        }
        catch (Exception ex)
        {
            HermesUpdateStatusText = AppLocalization.T("Couldn't check for updates", "检查更新失败");
            HermesUpdateDetailText = ex.Message;
        }
        finally
        {
            IsCheckingHermesUpdate = false;
        }
    }

    private async Task<bool> EnsureRestClientReadyAsync()
    {
        if (_restClient is not null)
        {
            if (IsConnected) return true;
            // Renew REST auth silently if only the WebSocket disconnected during maintenance.
            if (_activeDashboard is not null)
                return await TryRenewDashboardClientAsync(CancellationToken.None) || _restClient is not null;
            return true;
        }
        return await TryRenewDashboardClientAsync(CancellationToken.None);
    }

    private async Task<bool> TryRenewDashboardClientAsync(CancellationToken ct)
    {
        if (_activeDashboard is null) return false;
        var secrets = _vault.LoadSecrets(_activeDashboard.Id);
        if (secrets is null) return false;
        try
        {
            var ticket = _activeDashboard.AuthMode == AuthMode.Password && !string.IsNullOrEmpty(secrets.Password)
                ? await _authClient.ConnectWithPasswordAsync(_activeDashboard.NormalizedUrl, secrets.Username ?? "", secrets.Password, secrets, ct)
                : await _authClient.ConnectWithTokenAsync(_activeDashboard.NormalizedUrl, secrets, ct);
            _restClient?.Dispose();
            _restClient = new HermesRestClient(_activeDashboard.NormalizedUrl, ticket, secrets);
            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    public async ValueTask DisposeAsync()
    {
        _turnWatchdogCts?.Cancel();
        _turnWatchdogCts?.Dispose();
        _serverMaintenanceCts?.Cancel();
        _serverMaintenanceCts?.Dispose();
        AppLocalization.LanguageChanged -= OnLanguageChanged;
        if (_wsClient is not null) await _wsClient.DisposeAsync();
        _restClient?.Dispose();
    }
}
