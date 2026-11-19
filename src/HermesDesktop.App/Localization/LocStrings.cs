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
    public string ComposerWatermark => L(
        "Message Hermes... (Enter to send, Shift+Enter for newline)",
        "给 Hermes 发消息... (Enter 发送，Shift+Enter 换行)");
    public string Steer => L("Steer", "纠偏");
    public string Queue => L("Queue", "排队");
    public string Send => L("Send", "发送");

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
        "Each profile keeps its own SOUL.md personality, memory, skills, and conversation history.",
        "每个 Profile 拥有独立的 SOUL.md 人格、记忆、技能配置与会话历史。");
    public string Reload => L("Reload", "重新加载");
    public string DisplayNameTitleLabel => L("Display Name / Title", "显示名称 / 标题");
    public string DisplayNameTitleWatermark => L("e.g. Math Tutor / Atlas", "例如：数学辅导 / Atlas");
    public string ProfileSlugLabel => L("Profile Slug (a-z, 0-9, _ or -)", "Profile 标识 (小写字母、数字、_ 或 -)");
    public string ProfileSlugWatermark => L("e.g. math-tutor", "例如：math-tutor");
    public string DisplayTitleLabel => L("Display Title", "显示名称");
    public string DisplayTitleWatermark => L("Profile display name", "Profile 显示名称");
    public string DuplicateAsNewSoul => L("Duplicate as New Soul", "复制为新 Soul");
    public string MissionDescriptionLabel => L("Mission / Description", "职责 / 简介");
    public string MissionDescriptionWatermark => L(
        "Short role summary shown on bot cards and teammate rosters",
        "用于智能体卡片与协作列表的简短职责描述");
    public string StartFromLabel => L("Start From (Clone Config)", "基于配置模板创建");
    public string ShareAuth => L("Share Auth", "共享鉴权凭据");
    public string NoSkills => L("No Skills", "不继承技能");
    public string SoulContentWatermark => L(
        "Leave blank when creating to auto-generate an identity SOUL.md from Title + Mission, or write custom SOUL.md instructions here...",
        "新建时留空将自动根据名称与职责生成 SOUL.md，也可直接在此编写自定义人格提示词...");
    public string DeleteProfile => L("Delete Profile", "删除配置文件");
    public string ConfirmDeleteQuestion => L("Confirm delete?", "确认删除？");
    public string YesDelete => L("Yes, Delete", "确认删除");
    public string Keep => L("Keep", "保留");
    public string SoulTabPersonality => L("SOUL.md Personality", "SOUL.md 人格设定");
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
}
