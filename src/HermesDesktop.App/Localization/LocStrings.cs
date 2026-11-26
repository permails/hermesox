using System.ComponentModel;
using Avalonia.Threading;
using HermesDesktop.Core.Localization;

namespace HermesDesktop.App.Localization;

/// <summary>
/// Strongly-typed observable UI string table supporting live switching between
/// Follow System, English, and Simplified Chinese with Avalonia compiled bindings.
/// </summary>
public sealed class LocStrings : INotifyPropertyChanged
{
    public static LocStrings Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private LocStrings()
    {
        AppLocalization.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty)));
        }
    }

    private static string L(string en, string zh) => AppLocalization.T(en, zh);

    // Top bar & Navigation
    public string AppTitle => "Hermes Desktop";
    public string Servers => L("Servers", "服务器");
    public string AllProfiles => L("All", "全部");
    public string NeedsYouPrefix => L("Needs You:", "待处理:");
    public string ContextCompressTip => L("Click to compress session context", "点击压缩当前会话上下文");
    public string Refresh => L("Refresh", "刷新");
    public string NewChat => L("New Chat", "新对话");
    public string NewShort => L("New", "新建");
    public string FooterText => L(
        "Hermes Desktop · Direct Gateway Connection",
        "Hermes Desktop 桌面端 · 直连自托管网关");

    // Sessions & Chat
    public string SearchSessionsWatermark => L("Search sessions...", "搜索会话...");
    public string PinUnpinTip => L("Pin / Unpin", "置顶 / 取消置顶");
    public string OpenInBesideChatTip => L("Open in Beside Chat", "在右侧并行窗口打开");
    public string ViewSessionBtn => L("View", "查看");
    public string CopySelectionBtn => L("Copy Selection", "复制所选");
    public string CopyAllBtn => L("Copy All", "复制全部");
    public string CopiedBtn => L("Copied ✓", "已复制 ✓");
    public string DeleteSessionTip => L("Delete session", "删除会话");
    public string DeleteSession => L("Delete Session", "删除会话");
    public string Deleting => L("Deleting...", "正在删除...");
    public string ConfirmDeleteSessionPrefix => L("Delete conversation ", "确认删除会话「");
    public string ConfirmDeleteSessionSuffix => L("?", "」？");
    public string DeleteSessionWarning => L(
        "This conversation and all its messages will be permanently deleted and cannot be undone.",
        "删除后将永久移除该会话及其所有消息，无法恢复。");
    public string StopBeforeDeleteSession => L(
        "Stop the active response before deleting this session.",
        "请先停止当前正在生成的回复，再删除该会话。");
    public string JumpToLatest => L("↓ Latest", "↓ 回到最新");
    public string SelectOrStartChat => L("Select or start a chat", "选择或新建一个对话");
    public string Stop => L("Stop", "停止");
    public string Thought => L("Thought", "思考");
    public string ThoughtProcess => L("Thought process", "思考过程");
    public string Arguments => L("Arguments", "参数");
    public string Output => L("Output", "输出");
    public string Running => L("Running", "运行中");
    public string ApprovalRequired => L("Approval Required", "需要确认执行");
    public string ApprovalSteerWatermark => L(
        "Optional: tell Hermes what to do instead when rejecting...",
        "可选：拒绝时告诉 Hermes 改做什么...");
    public string RunCommand => L("Run Command", "允许执行");
    public string RejectSteer => L("Reject / Steer", "拒绝 / 引导");
    public string ClarificationNeeded => L("Clarification Needed", "需要进一步确认");
    public string ClarifyCustomWatermark => L("Or type a custom answer...", "或输入自定义回答...");
    public string Submit => L("Submit", "提交");
    public string LoadingHistoryNotice => L("Loading conversation history...", "正在加载会话历史记录...");
    public string ComposerWatermark => L(
        "Message Hermes... (Enter or Ctrl+Enter to send, Shift+Enter for newline)",
        "给 Hermes 发消息... (Enter 或 Ctrl+Enter 发送，Shift+Enter 换行)");
    public string Steer => L("Steer", "纠偏");
    public string Queue => L("Queue", "排队");
    public string Send => L("Send", "发送");
    public string AttachFilesTip => L("Attach image, video, or file (drag & drop or Ctrl+V screenshot supported)", "上传图片、视频或文件（支持拖拽文件或直接 Ctrl+V 粘贴截图）");
    public string AttachmentLimitSectionTitle => L("Attachment & Video Upload Limit", "附件与视频上传大小限制");
    public string AttachmentLimitHint => L(
        "Default is 256 MB. Larger videos require more memory while encoding for upload.",
        "默认上限为 256 MB；发送超大视频时会占用更多内存进行编码上传（实际最大允许大小亦受服务端配置限制）。");
    public string VoiceDictationTip => L("Voice to text (System IME voice typing Win+H)", "输入法语音转文字（调用系统语音输入 Win+H）");
    public string VoiceDictationFallbackNotice => L(
        "Place cursor in the input box and press Win+H (or your IME voice key) to start voice typing.",
        "请点击输入框后按 Win+H（或输入法语音键）开启语音转文字。");
    public string UploadingAttachments => L("Uploading attachments...", "正在上传附件...");
    public string RemoveAttachmentTip => L("Remove attachment", "移除附件");

    // Beside Chat
    public string BesideChatFallback => L("Beside Chat", "并行对话");
    public string Pop => L("Pop", "弹出");
    public string Swap => L("Swap", "切换为主窗口");
    public string SwapToMain => L("Swap to Main", "切换至主窗口");
    public string QuickMessageWatermark => L("Quick message...", "快速发送消息...");
    public string QuickBesideWatermark => L("Quick message to beside chat...", "向并行会话快速发消息...");

    // Preview Pane & Modals
    public string ToggleZoomTip => L("Toggle 100% / 85%", "在 100% / 85% 缩放间切换");
    public string Browser => L("Browser", "浏览器打开");
    public string Close => L("Close", "关闭");
    public string OpenExternally => L("Open Externally", "用外部应用打开");
    public string LoadingImage => L("Loading image...", "正在加载图片...");

    // Bots Tab
    public string BotsAndProfilesTitle => L("Bots & Profiles", "智能体与配置文件");
    public string NewSoulBotBtn => L("+ New Soul / Bot", "+ 新建 Soul / 智能体");
    public string DefaultProfileFallback => L("Self-hosted Hermes profile", "自托管 Hermes 配置文件");
    public string DefaultModelLabel => L("Default model", "默认模型");
    public string OpenChatsBtn => L("Open Chats", "打开会话");
    public string SoulMdBtn => "SOUL.md";

    // Tasks / Pulse / Artifacts
    public string TasksTitle => L("Tasks · Scheduled Jobs & Kanban", "任务 · 定时任务与看板");
    public string RefreshTasks => L("Refresh Tasks", "刷新任务");
    public string ScheduledJobs => L("Scheduled Jobs (Cron)", "定时任务 (Cron)");
    public string RunNow => L("Run Now", "立即运行");
    public string PauseResume => L("Pause/Resume", "暂停 / 恢复");
    public string KanbanBoards => L("Kanban Boards", "任务看板");
    public string PulseTitle => L("Pulse", "动态摘要");
    public string ArtifactsTitle => L("Artifacts", "产物文件");
    public string Open => L("Open", "打开");

    // Settings Tab
    public string SettingsTitle => L("Settings", "设置");
    public string LanguageSectionTitle => L("Language · 语言设置", "语言设置 · Language");
    public string LanguageFollowSystem => L("Auto (Follow System)", "跟随系统 (自动)");
    public string LanguageEnglish => "English";
    public string LanguageChinese => "简体中文";
    public string LanguageHint => AppLocalization.DetectSystemIsChinese()
        ? L("System UI language detected: Chinese (Simplified). Switch instantly without restart.",
            "检测到当前系统语言：简体中文。切换后即时生效，无需重启。")
        : L("System UI language detected: English. Switch instantly without restart.",
            "检测到当前系统语言：English。切换后即时生效，无需重启。");
    public string ThemeSectionTitle => L("Appearance · Windows 11 Theme", "外观主题 · Windows 11 Fluent 2");
    public string ThemeFollowSystem => L("Follow System", "跟随系统");
    public string ThemeDark => L("Dark", "深色");
    public string ThemeLight => L("Light", "浅色");
    public string ThemeHint => L(
        "Uses Windows 11 Mica backdrop, 4px/8px Fluent 2 geometry, and automatic Light / Dark adaptation.",
        "遵循 Windows 11 Fluent 2 规范：Mica 云母背景、4px/8px 标准圆角与系统明暗模式自适应。");
    public string ProfilePersonalityTitle => L(
        "Profile Personality (SOUL.md & Multi-Soul)",
        "角色人格设定 (SOUL.md 与多 Soul 管理)");
    public string ProfilePersonalityDesc => L(
        "Edit each profile's SOUL.md identity, or create multiple isolated Soul profiles on this Hermes gateway.",
        "查看和编辑各个 Profile 的 SOUL.md 人格设定，或在当前 Hermes 网关上创建多个独立的 Soul 角色。");
    public string EditActiveSoulBtn => L("Edit Active SOUL.md", "编辑当前 SOUL.md");
    public string LocalDataTitle => L("Local Data & Storage", "本地数据与存储");
    public string PortableModeNotice => L(
        "Portable Mode active (./data folder detected beside executable).",
        "当前为便携模式（已在程序同级目录启用 ./data 数据目录）。");
    public string DpapiStorageDesc => L(
        "Credentials are protected with Windows DPAPI (on Windows). Upgrading the .exe preserves all local drafts, cache, and saved servers.",
        "凭据在 Windows 上通过 DPAPI 加密存储。替换升级 .exe 时，本地草稿、缓存与已保存服务器配置均会完整保留。");
    public string OpenDataFolder => L("Open Data Folder", "打开数据目录");
    public string DiagnosticsLog => L("Diagnostics Log", "查看诊断日志");
    public string V2NavigationTabs => L("V2 Navigation Tabs", "侧边导航栏标签管理");
    public string MoveUpTip => L("Move up", "上移");
    public string MoveDownTip => L("Move down", "下移");

    // SOUL.md Editor / Creator Modal
    public string SoulModalSubtitle => L(
        "Configure identity, model, and persona. Search & tool capabilities are inherited automatically.",
        "设定名称、对话模型与角色指令，联网检索与工具能力将自动就绪。");
    public string Reload => L("Reload", "重新加载");
    public string DisplayNameTitleLabel => L("Name", "智能体名称");
    public string DisplayNameTitleWatermark => L("e.g. Product Expert / Atlas", "例如：产品专家 / Atlas");
    public string ProfileSlugLabel => L("Handle (@ID)", "唯一标识 (@ID)");
    public string ProfileSlugWatermark => L("e.g. pm-expert", "例如：pm-expert");
    public string ProfileSlugHint => L(
        "Used for @mentions in group collaboration (lowercase letters, digits, _ or -).",
        "用于多智能体 @点名协作，仅限小写字母、数字、_ 或 -");
    public string DisplayTitleLabel => L("Name", "智能体名称");
    public string DisplayTitleWatermark => L("Profile display name", "Profile 显示名称");
    public string DuplicateAsNewSoul => L("Duplicate as New Soul", "复制为新 Soul");
    public string MissionDescriptionLabel => L("Summary", "职责简介");
    public string MissionDescriptionWatermark => L(
        "One-line role summary (optional)",
        "一句话描述它擅长的领域（选填）");
    public string StartFromLabel => L("Clone Capabilities From", "能力继承源");
    public string ShareAuth => L("Share Credentials", "共享凭据");
    public string NoSkills => L("Skip Bundled Skills", "不加载预设技能");
    public string PersonaSectionHeader => L("Persona Instructions", "角色设定与指令");
    public string PersonaAutoHint => L("Auto-generated if left blank", "留空自动生成");
    public string AdvancedInheritHeader => L("Advanced Inheritance Options", "高级继承选项");
    public string SoulContentWatermark => L(
        "Leave blank to auto-generate persona from Name & Summary, or write custom instructions…",
        "留空将根据名称与简介自动生成角色设定，也可在此编写专属指令…");
    public string DeleteProfile => L("Delete Profile", "删除配置文件");
    public string ConfirmDeleteQuestion => L("Confirm delete?", "确认删除？");
    public string YesDelete => L("Yes, Delete", "确认删除");
    public string HideSessionLocally => L("Hide From List", "仅从本机列表移除");
    public string Keep => L("Keep", "保留");
    public string SoulTabPersonality => L("Overview & Persona", "基础与角色");
    public string SoulTabCapabilities => L("Skills · Tools · MCP", "技能 · 工具 · MCP");
    public string SkillsSectionHeader => L("SKILLS", "技能列表 (Skills)");
    public string ToolsetsSectionHeader => L("TOOLSETS (at least 1 must stay enabled)", "工具集 (Toolsets，至少保留 1 项开启)");
    public string McpSectionHeader => L("MCP SERVERS", "MCP 服务器");
    public string NoCapabilitiesNotice => L(
        "No configurable skills, toolsets, or MCP servers reported for this profile.",
        "当前配置文件暂无可配置的技能、工具集或 MCP 服务。");

    // Tasks / Cron Job Editor Modal
    public string NewJobBtn => L("+ New Job", "+ 新建任务");
    public string Edit => L("Edit", "编辑");
    public string CronJobSubtitle => L(
        "Configure scheduled automation on your Hermes Gateway (cron, interval, or natural schedule).",
        "配置自托管 Hermes 网关上的定时自动任务（支持 Cron 表达式、间隔或自然语言周期）。");
    public string JobNameLabel => L("Job Name", "任务名称");
    public string JobNameWatermark => L("e.g. Daily Market Brief", "例如：每日财经摘要");
    public string ScheduleLabel => L("Schedule (Cron / Interval / Phrase)", "执行周期（Cron 表达式 / 间隔 / 短语）");
    public string ScheduleWatermark => L("e.g. 0 9 * * *  or  every 2h  or  weekdays at 9am", "例如：0 9 * * *  或  every 2h  或  weekdays at 9am");
    public string PromptLabel => L("Prompt / Instructions", "执行提示词 (Prompt)");
    public string PromptWatermark => L("What should Hermes do when this job triggers?", "到达执行时间时，需要 Hermes 执行什么任务？");
    public string DeliverTargetLabel => L("Delivery Target", "结果投递目标");
    public string DeliverTargetHint => L("Default: local (visible in Hermes Desktop)", "默认填 local（直接在客户端内查看结果）");
    public string DeleteJob => L("Delete Job", "删除任务");

    // Connect / Saved Dashboards Modal
    public string ServersConnectionTitle => L("Servers & Connection", "服务器与连接管理");
    public string ServersConnectionSubtitle => L(
        "Connect directly to your self-hosted Hermes Gateway. No Apple relay required.",
        "直连自托管 Hermes 网关，无需任何中继服务。");
    public string SavedServersHeader => L("SAVED SERVERS", "已保存服务器");
    public string NoSavedServers => L(
        "No saved servers yet. Add your first Hermes Dashboard on the right.",
        "暂无已保存服务器，请在右侧填写并连接你的 Hermes 服务。");
    public string RemoveServerTip => L("Remove server", "移除服务器");
    public string Connect => L("Connect", "连接");
    public string Step1ServerHeader => L("1. SERVER ADDRESS", "1. 服务器地址");
    public string ServerUrlHint => L(
        "LAN example: http://192.168.10.67:9119 · Public HTTPS: https://hermes.example.com",
        "局域网直连填 http://IP:9119 · 公网域名填 https://hermes.example.com");
    public string Step2LoginHeader => L("2. HERMES LOGIN (CHOOSE ONE METHOD)", "2. Hermes 登录方式（二选一，非两套系统）");
    public string PasswordAuthDesc => L(
        "Sign in with the Hermes Dashboard username & password (recommended for most setups).",
        "使用 Hermes Dashboard 的用户名与密码登录（最常用）。");
    public string SessionTokenAuthDesc => L(
        "Only if your server sets HERMES_DASHBOARD_SESSION_TOKEN instead of user accounts.",
        "仅当服务端通过 HERMES_DASHBOARD_SESSION_TOKEN 启用单令牌认证时使用。");
    public string AdvancedProxyToggleBtn => L(
        "Advanced: Reverse Proxy / Cloudflare Access (Optional)",
        "高级选项：前置反代 / Cloudflare Access（局域网直连无需开启）");
    public string CfAccessDesc => L(
        "Extra Zero Trust gate in front of Hermes (HTTPS/WSS only). Leave OFF for LAN or direct IP connections.",
        "仅当服务端前置了 Cloudflare Tunnel + Zero Trust 访问控制时才需要；局域网 http:// 直连请保持关闭。");
    public string Label => L("Label", "名称标签");
    public string HomeLabWatermark => L("Home Lab", "我的 Hermes");
    public string DashboardUrl => L("Dashboard URL", "Dashboard 地址");
    public string PasswordAuth => L("Username & Password", "用户名 + 密码");
    public string SessionTokenAuth => L("Deployment Token", "部署令牌 (Token)");
    public string Username => L("Username", "用户名");
    public string Password => L("Password", "密码");
    public string DeploymentTokenLabel => L(
        "Deployment Session Token (X-Hermes-Session-Token)",
        "部署会话令牌 (X-Hermes-Session-Token)");
    public string CfAccessLabel => L(
        "Enable Cloudflare Access Service Token (HTTPS/WSS only)",
        "启用 Cloudflare Access 服务令牌（仅限 HTTPS/WSS）");
    public string DpapiFooterNotice => L(
        "Credentials are stored encrypted with Windows DPAPI on Windows.",
        "凭据在 Windows 下采用系统 DPAPI 加密存储。");
    public string Cancel => L("Cancel", "取消");
    public string ConnectServer => L("Connect Server", "连接服务器");

    // Hermes Server Update & Gateway Maintenance
    public string HermesServerTitle => L("Hermes Host & Updates", "Hermes 服务端与更新");
    public string HermesServerDesc => L(
        "Check your Hermes host version, run remote updates, or restart the Hermes gateway.",
        "查看当前连接的 Hermes 服务端版本、远程执行 hermes update 升级或重启网关。");
    public string ServerVersionLabel => L("Server version", "服务端版本");
    public string ManageServersBtn => L("Manage Servers & Connection", "管理服务器连接");
    public string CheckForUpdates => L("Check for Updates", "检查更新");
    public string UpdateHermes => L("Update Hermes", "更新 Hermes");
    public string ConfirmUpdateHermes => L("Confirm Update", "确认更新");
    public string RestartGateway => L("Restart Gateway", "重启网关");
    public string HermesUpToDate => L("Hermes is up to date", "Hermes 已是最新版本");
    public string HermesCheckingUpdates => L("Checking for updates…", "正在检查更新…");
    public string HermesUpdating => L("Updating Hermes on host…", "正在宿主机执行更新…");
    public string HermesRestarting => L("Restarting Hermes on the new version…", "正在重启以应用新版本…");
    public string HermesUpdated => L("Hermes updated", "Hermes 已完成更新");
    public string HermesManualUpdate => L("Update Hermes on the host", "请在宿主机手动更新");
    public string HermesUpdateUnsupported => L(
        "This Hermes dashboard version doesn't expose remote update routes.",
        "当前连接的 Hermes 网关版本不支持远程更新接口。");
    public string GatewayRestarting => L("Restarting Hermes gateway…", "正在重启 Hermes 网关…");
    public string GatewayRestarted => L("Gateway is back online", "网关已重启并恢复在线");
    public string NotConnectedServer => L("Connect to a Hermes server first", "请先连接 Hermes 服务端");
    public string StopTurn => L("Stop", "停止");
    public string AgentWorkingNotice => L("Hermes is thinking and responding…", "Hermes 正在思考并生成回复…");
    public string SaveAndConnect => L("Save & Connect", "保存并连接");
    public string EditingServerPrefix => L("Editing saved server:", "正在编辑已保存服务器：");
    public string NewServerBtn => L("New instead", "改为新建");

    // Copy / Select All / Export / Rename Session
    public string CopyBtn => L("Copy", "复制");
    public string CopyAllTip => L("Copy entire conversation (excluding thinking process)", "复制完整对话内容（不含思考过程）");
    public string CopyMessageTip => L("Copy message (excluding thinking process)", "复制此条内容（不含思考过程）");
    public string OpenLogsFolderBtn => L("Open Logs Folder", "打开日志目录");
    public string LocalLogsLabel => L("Local Logs Directory", "本地日志目录");

    // Startup Connection Buffer Gate
    public string StartupConnectingTitle => L("Connecting…", "正在连接工作台…");
    public string StartupReadyTitle => L("Ready — Click workspace to enter", "已就绪 · 点击工作台进入");
    public string StartupErrorTitle => L("Connection Failed", "连接失败");
    public string StartupCardReadyHint => L("Click to enter", "点击进入");
    public string StartupCardConnectingHint => L("Connecting…", "连接中…");
    public string StartupCardSwitchHint => L("Click to connect", "点击连接");
    public string StartupRetryBtn => L("Retry", "重试");
    public string StartupSwitchServerBtn => L("Manage Servers", "管理服务器");

    // Per-Bot Provider & Model Selection
    public string BotModelSectionHeader => L("Model", "对话模型");
    public string BotUseFirstPresetBtn => L("Default Model", "沿用默认模型");
    public string BotCustomModelBtn => L("Custom Model", "指定服务商与模型");
    public string BotProviderLabel => L("Provider", "服务商");
    public string BotModelLabel => L("Model", "模型");
    public string BotManualInputToggle => L("Manual input (custom provider / model)", "手动输入自定义 Provider / Model");
    public string BotRefreshModelsBtn => L("↻ Refresh", "↻ 刷新");
    public string SelectTextBtn => L("Select", "框选");
    public string SelectAllBtn => L("Select All", "全选");
    public string BackToChatViewBtn => L("Back to Chat View", "返回排版视图");
    public string TranscriptSelectBanner => L(
        "Full-Conversation Text Selection — Drag across any messages, Ctrl+A to select all, Ctrl+C to copy (Esc to exit)",
        "全会话纯文本框选模式 — 支持跨消息任意拖选、Ctrl+A 全选、Ctrl+C 复制（按 Esc 返回）");
    public string SelectAllTranscriptBtn => L("Select / Copy Chat", "框选 / 全选对话");
    public string SelectAllTranscriptTip => L("Switch to full-conversation selectable mode (Ctrl+A / Cmd+A)", "切换到全对话可框选与全选复制模式（支持 Ctrl+A / Cmd+A）");
    public string ExportFileBtn => L("Export / Save", "导出 / 下载");
    public string ExportFileTip => L("Save or export this file to disk", "导出或下载此文件到本地");
    public string RenameSessionBtn => L("Rename", "重命名");
    public string RenameSessionTitle => L("Rename Session", "重命名会话");
    public string RenameSessionWatermark => L("Enter a new session title…", "输入新的会话标题…");
    public string SaveBtn => L("Save", "保存");

    // Slash commands
    public string SlashCommandExecuting => L("Running slash command…", "正在执行命令…");
    public string SlashCommandCompressing => L("Compressing session context…", "正在压缩会话上下文…");
    public string SlashCommandDone => L("Command completed", "命令已执行");

    // Multi-Agent Group Chats
    public string GroupsTitle => L("Agent Groups", "多智能体群组");
    public string NewGroupBtn => L("New Group", "新建群组");
    public string RefreshGroupsBtn => L("Refresh", "刷新");
    public string GroupRoomsEmpty => L("No group chats yet. Create a room with 2-6 bots to collaborate.", "暂无群聊。点击「新建群组」拉起 2-6 个智能体协同对话。");
    public string SelectGroupHint => L("Select a group room or create a new one.", "选择一个群组或新建多智能体群组。");
    public string GroupComposerWatermark => L("Message the group… use @handle to target a bot (Enter to send)", "在群里发言… 可输入 @bot 指定智能体（Enter 发送）");
    public string GroupSettingsBtn => L("Room Settings", "群设置");
    public string GroupStopBtn => L("Stop Run", "停止讨论");
    public string GroupContinueBtn => L("Continue Discussion", "继续讨论");
    public string GroupDisbandBtn => L("Disband", "解散群组");
    public string GroupRenameBtn => L("Rename Room", "重命名群组");
    public string GroupCreateTitle => L("Create Multi-Agent Group", "创建多智能体群组");
    public string GroupNameLabel => L("Room Name", "群组名称");
    public string GroupNameWatermark => L("e.g. Architecture Review", "例如：架构评审群");
    public string GroupSelectMembersLabel => L("Select 2-6 Bots", "选择 2-6 个群成员智能体");
    public string GroupCreateSubmit => L("Create Group", "创建群组");
    public string GroupPurposeLabel => L("Purpose / Goal", "群目标 / 背景");
    public string GroupNotesLabel => L("Shared Notes", "群共享备忘");
    public string GroupRoutingLabel => L("Routing Mode", "路由模式");
    public string GroupSaveContextBtn => L("Save Context", "保存设置");
    public string GroupMembersCountFormat => L("{0} bots", "{0} 个智能体");
    public string GroupApprovalRequired => L("Group Approval Required", "群成员请求命令授权");
    public string GroupApproveBtn => L("Approve", "允许");
    public string GroupRejectBtn => L("Reject", "拒绝");
}
