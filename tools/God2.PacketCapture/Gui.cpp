#include "Gui.h"

#include "EtwConsumer.h"
#include "EvidencePackage.h"
#include "Gameplay.h"
#include "GpuAcceleration.h"
#include "Resource.h"
#include "SharedSemanticRing.h"
#include "Storage.h"
#include "Transport.h"
#include "UltimateRecovery.h"
#include "Version.h"

#include <minizip/ioapi.h>
#include <minizip/iowin32.h>
#include <minizip/unzip.h>

#include <commctrl.h>
#include <commdlg.h>
#include <dwmapi.h>
#include <psapi.h>
#include <sddl.h>
#include <shellapi.h>
#include <shlobj.h>
#include <tlhelp32.h>
#include <uxtheme.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cctype>
#include <cstddef>
#include <cstdlib>
#include <cwctype>
#include <cstring>
#include <fstream>
#include <functional>
#include <iterator>
#include <limits>
#include <memory>
#include <mutex>
#include <new>
#include <set>
#include <sstream>
#include <stdexcept>
#include <thread>
#include <tuple>

#pragma comment(lib, "dwmapi.lib")
#pragma comment(lib, "uxtheme.lib")

namespace god2 {
namespace {

constexpr wchar_t kWindowClass[] = L"God2SemanticRecoveryEngineMainWindow";
constexpr wchar_t kObservationWindowClass[] = L"God2PacketCaptureObservationWindow";
constexpr wchar_t kWindowTitle[] = L"God2 Semantic Recovery Engine Ultimate - Private Edition";
constexpr std::string_view kExactClientVersion = "1.0.0.1";
constexpr std::string_view kExactClientSha256 =
    "6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B";
constexpr COLORREF kUiBackground = RGB(241, 245, 249);
constexpr COLORREF kUiSurface = RGB(255, 255, 255);
constexpr COLORREF kUiBorder = RGB(214, 222, 232);
constexpr COLORREF kUiText = RGB(30, 41, 59);
constexpr COLORREF kUiMutedText = RGB(71, 85, 105);
constexpr COLORREF kUiAccent = RGB(37, 99, 235);
constexpr COLORREF kUiAccentPressed = RGB(29, 78, 216);
constexpr COLORREF kUiTeal = RGB(8, 145, 178);
constexpr UINT kControllerMessage = WM_APP + 1;
constexpr UINT kBeginProbeMessage = WM_APP + 2;
constexpr UINT_PTR kMetricsTimer = 1;
constexpr int kMainWindowDesignWidth = 716;
constexpr int kMainWindowDesignHeight = 530;
constexpr int kMainClientDesignWidth = 700;
constexpr int kMainClientDesignHeight = 491;

constexpr int IDC_GAME_PATH = 1001;
constexpr int IDC_BROWSE_GAME = 1002;
constexpr int IDC_START_CAPTURE = 1003;
constexpr int IDC_STOP_CAPTURE = 1004;
constexpr int IDC_OPEN_OUTPUT = 1005;
constexpr int IDC_COPY_OUTPUT = 1006;
constexpr int IDC_STATUS_TEXT = 1007;
constexpr int IDC_PACKET_COUNT = 1008;
constexpr int IDC_CAPTURE_SIZE = 1009;
constexpr int IDC_OUTPUT_PATH = 1010;
constexpr int IDC_OS_TEXT = 1011;
constexpr int IDC_ADMIN_TEXT = 1012;
constexpr int IDC_BACKEND_TEXT = 1013;
constexpr int IDC_GAME_PROCESS = 1014;
constexpr int IDC_PID_TEXT = 1015;
constexpr int IDC_CLASSIFIED_COUNT = 1016;
constexpr int IDC_UNKNOWN_COUNT = 1017;
constexpr int IDC_MEMORY_USE = 1018;
constexpr int IDC_START_TIME = 1019;
constexpr int IDC_OPEN_ANALYSIS = 1020;
constexpr int IDC_OPEN_WORLD = 1021;
constexpr int IDC_RETRY_PROBE = 1022;
constexpr int IDC_LANGUAGE = 1023;
constexpr int IDC_HELP_BUTTON = 1024;
constexpr int IDC_CAPTURE_SCOPE = 1025;
constexpr int IDC_ENHANCED_CAPTURE = 1026;
constexpr int IDC_CONSUMER_TEXT = 1027;
constexpr int IDC_ANALYSIS_STATE_TEXT = 1028;
constexpr int IDC_GPU_MODE = 1029;
constexpr int IDC_GPU_STATUS = 1030;
constexpr int IDC_AI_STATUS = 1031;
constexpr int IDC_COVERAGE_STATUS = 1032;

constexpr int IDC_TITLE = 1100;
constexpr int IDC_SUBTITLE = 1101;
constexpr int IDC_AUTHOR = 1102;
constexpr int IDC_LANGUAGE_LABEL = 1103;
constexpr int IDC_LAUNCHER_LABEL = 1110;
constexpr int IDC_OS_LABEL = 1111;
constexpr int IDC_ADMIN_LABEL = 1112;
constexpr int IDC_BACKEND_LABEL = 1113;
constexpr int IDC_PROCESS_LABEL = 1114;
constexpr int IDC_PID_LABEL = 1115;
constexpr int IDC_STATUS_LABEL = 1116;
constexpr int IDC_SCOPE_LABEL = 1117;
constexpr int IDC_PACKET_LABEL = 1120;
constexpr int IDC_SIZE_LABEL = 1121;
constexpr int IDC_CLASSIFIED_LABEL = 1122;
constexpr int IDC_UNKNOWN_LABEL = 1123;
constexpr int IDC_MEMORY_LABEL = 1124;
constexpr int IDC_STARTED_LABEL = 1125;
constexpr int IDC_OUTPUT_LABEL = 1130;
constexpr int IDC_CONSUMER_LABEL = 1131;
constexpr int IDC_ANALYSIS_STATE_LABEL = 1132;
constexpr int IDC_GPU_LABEL = 1133;
constexpr int IDC_AI_LABEL = 1134;
constexpr int IDC_COVERAGE_LABEL = 1135;

enum class UiLanguage : int { TraditionalChinese = 0, SimplifiedChinese = 1, English = 2 };

enum class CaptureState {
    Idle,
    Preparing,
    WaitingForUac,
    StartingElevatedWorker,
    StartingBackend,
    StartingConsumer,
    WaitingForLauncher,
    WaitingForGod2Process,
    ValidatingGod2Process,
    StartingAnalysis,
    AttachingEnhancedCapture,
    Capturing,
    PostCaptureFallback,
    Cancelling,
    Stopping,
    Completed,
    Cancelled,
    Failed,
    CleanupRequired
};

enum class BackendState { NotStarted, Starting, Active, EvidenceBlocked, Failed, Stopping, Stopped };
enum class ConsumerState { NotStarted, Starting, Ready, EvidenceBlocked, Failed, PostCaptureFallback, Stopping, Stopped };
enum class GameProcessState {
    NotProbed,
    Searching,
    WindowDetected,
    ProcessDetected,
    AccessDenied,
    PathMismatch,
    ArchitectureMismatch,
    Validated,
    Exited,
    QueryFailed
};
enum class AnalysisState { NotStarted, WaitingForPid, Starting, NearRealtime, PostCaptureFallback, Finalizing, Completed, Failed };

bool IsStartupCancellable(CaptureState state) {
    switch (state) {
    case CaptureState::Preparing:
    case CaptureState::WaitingForUac:
    case CaptureState::StartingElevatedWorker:
    case CaptureState::StartingBackend:
    case CaptureState::StartingConsumer:
    case CaptureState::WaitingForLauncher:
    case CaptureState::WaitingForGod2Process:
    case CaptureState::ValidatingGod2Process:
    case CaptureState::StartingAnalysis:
    case CaptureState::AttachingEnhancedCapture:
        return true;
    default:
        return false;
    }
}

bool CanRequestStop(CaptureState state) {
    return IsStartupCancellable(state) || state == CaptureState::Capturing ||
        state == CaptureState::PostCaptureFallback || state == CaptureState::CleanupRequired;
}

bool WorkflowNeedsFinalization(CaptureState state) {
    return CanRequestStop(state) || state == CaptureState::Cancelling ||
        state == CaptureState::Stopping;
}

bool IsTerminalCaptureState(CaptureState state) {
    return state == CaptureState::Idle || state == CaptureState::Completed ||
        state == CaptureState::Cancelled || state == CaptureState::Failed;
}

enum class UiString {
    Title, Subtitle, Author, Language, Launcher, Browse, Os, Administrator, Backend, Consumer, Process, Pid,
    AnalysisStateLabel, Status, Scope, Retry, Start, Stop, CancelStart, PacketCount, CaptureSize, Classified, Unknown, Memory, StartedAt, Output,
    OpenOutput, CopyOutput, OpenAnalysis, OpenWorld, Help, Ready, Probing, NotStarted, ScopeWaiting,
    ScopeExact, ScopeRejected, AdminYes, AdminNo, WaitingLauncher, Capturing, Starting, Stopping,
    SelectLauncherTitle, InvalidLauncher, InvalidLauncherStatus, NoOutput, OpenOutputFailed, NoSession,
    ObservationFailed, NoClipboardPath, ProbeFailed, BusyStarting, BusyStopping, ConfirmExit,
    AlreadyRunning, CaptureAlreadyRunning, NothingToStop, StartCompleteWaiting, StartCompleteTracking,
    WrongArchitecture, HelpTitle, WorldTitle, AnalysisTitle, WorldHeading, WorldEmpty, AnalysisHeading,
    EnhancedCapture, EnhancedCaptureTip, AnalysisTip, WorldTip, GpuAcceleration,
    AiBackend, Coverage
};

struct UiTranslation {
    UiString id;
    const wchar_t* traditional;
    const wchar_t* simplified;
    const wchar_t* english;
};

const UiTranslation kUiTranslations[] = {
    {UiString::Title, L"God2 語意恢復引擎 Ultimate", L"God2 语义恢复引擎 Ultimate", L"God2 Semantic Recovery Engine Ultimate"},
    {UiString::Subtitle, L"完整恢復取證 · CPU 權威驗證 · Codex 恢復包", L"完整恢复取证 · CPU 权威验证 · Codex 恢复包", L"Full recovery forensics · CPU authority · Codex recovery bundle"},
    {UiString::Author, L"版本：" GOD2_TOOL_DISPLAY_VERSION_W L"    作者：RayCat    Discord：raycat66",
                       L"版本：" GOD2_TOOL_DISPLAY_VERSION_W L"    作者：RayCat    Discord：raycat66",
                       L"Version: " GOD2_TOOL_DISPLAY_VERSION_W L"    Author: RayCat    Discord: raycat66"},
    {UiString::Language, L"語言", L"语言", L"Language"},
    {UiString::Launcher, L"遊戲登入器", L"游戏登录器", L"Game launcher"},
    {UiString::Browse, L"選擇...", L"选择...", L"Browse..."},
    {UiString::Os, L"作業系統", L"操作系统", L"Operating system"},
    {UiString::Administrator, L"管理員權限", L"管理员权限", L"Administrator"},
    {UiString::Backend, L"取證探針", L"取证探针", L"Forensic probe"},
    {UiString::Consumer, L"DLL 增強擷取", L"DLL 增强捕获", L"DLL enhanced capture"},
    {UiString::Process, L"遊戲程序", L"游戏进程", L"Game process"},
    {UiString::Pid, L"PID", L"PID", L"PID"},
    {UiString::AnalysisStateLabel, L"恢復狀態", L"恢复状态", L"Recovery"},
    {UiString::GpuAcceleration, L"GPU 加速", L"GPU 加速", L"GPU acceleration"},
    {UiString::AiBackend, L"AI / ML", L"AI / ML", L"AI / ML"},
    {UiString::Coverage, L"恢復覆蓋率", L"恢复覆盖率", L"Recovery coverage"},
    {UiString::Status, L"目前狀態", L"当前状态", L"Status"},
    {UiString::Scope, L"捕捉範圍", L"捕获范围", L"Capture scope"},
    {UiString::Retry, L"重新檢查", L"重新检查", L"Check again"},
    {UiString::Start, L"開始完整恢復取證", L"开始完整恢复取证", L"Start full recovery forensics"},
    {UiString::Stop, L"停止並產生 Codex 恢復包", L"停止并生成 Codex 恢复包", L"Stop and create Codex recovery bundle"},
    {UiString::CancelStart, L"取消啟動", L"取消启动", L"Cancel startup"},
    {UiString::PacketCount, L"語意事件", L"语义事件", L"Semantic events"},
    {UiString::CaptureSize, L"捕捉大小", L"捕获大小", L"Capture size"},
    {UiString::Classified, L"已恢復候選", L"已恢复候选", L"Recovered candidates"},
    {UiString::Unknown, L"未知／待補證據", L"未知／待补证据", L"Unknown / missing evidence"},
    {UiString::Memory, L"記憶體使用", L"内存使用", L"Memory"},
    {UiString::StartedAt, L"開始時間", L"开始时间", L"Started"},
    {UiString::Output, L"輸出資料夾", L"输出文件夹", L"Output folder"},
    {UiString::OpenOutput, L"開啟輸出資料夾", L"打开输出文件夹", L"Open output"},
    {UiString::CopyOutput, L"複製輸出路徑", L"复制输出路径", L"Copy path"},
    {UiString::OpenAnalysis, L"查看自動即時分析", L"查看自动实时分析", L"View auto analysis"},
    {UiString::OpenWorld, L"查看自動世界實體", L"查看自动世界实体", L"View auto entities"},
    {UiString::Help, L"使用說明", L"使用说明", L"How to use"},
    {UiString::Ready, L"就緒", L"就绪", L"Ready"},
    {UiString::Probing, L"背景探測中", L"后台探测中", L"Checking in background"},
    {UiString::NotStarted, L"尚未開始探測", L"尚未开始探测", L"Not probed"},
    {UiString::ScopeWaiting, L"等待 32 位元 God2_opt.exe", L"等待 32 位 God2_opt.exe", L"Waiting for 32-bit God2_opt.exe"},
    {UiString::ScopeExact, L"僅分析 32 位元 God2_opt.exe（PID 已鎖定）", L"仅分析 32 位 God2_opt.exe（PID 已锁定）", L"32-bit God2_opt.exe only (PID locked)"},
    {UiString::ScopeRejected, L"已阻擋：God2_opt.exe 不是 32 位元程序", L"已阻止：God2_opt.exe 不是 32 位进程", L"Blocked: God2_opt.exe is not a 32-bit process"},
    {UiString::AdminYes, L"是", L"是", L"Yes"},
    {UiString::AdminNo, L"否（開始捕捉時按需顯示 UAC）", L"否（开始捕获时按需显示 UAC）", L"No (UAC appears when capture starts)"},
    {UiString::WaitingLauncher, L"x86 遮罩語意流程已就緒；請在登入器按「開始遊戲」", L"x86 遮罩语义流程已就绪；请在登录器点击“开始游戏”", L"x86 masked semantic workflow ready; click Start Game in the launcher"},
    {UiString::Capturing, L"正在收集 x86 遮罩語意證據", L"正在收集 x86 遮罩语义证据", L"Collecting x86 masked semantic evidence"},
    {UiString::Starting, L"正在啟動遮罩語意取證流程", L"正在启动遮罩语义取证流程", L"Starting masked semantic evidence workflow"},
    {UiString::Stopping, L"正在完成語意證據與匯出", L"正在完成语义证据与导出", L"Finalizing semantic evidence and export"},
    {UiString::SelectLauncherTitle, L"選擇 Launcher.exe（由它啟動 God2_opt.exe）", L"选择 Launcher.exe（由它启动 God2_opt.exe）", L"Select Launcher.exe (it starts God2_opt.exe)"},
    {UiString::InvalidLauncher, L"請選擇遊戲安裝目錄中的 Launcher.exe，確認同目錄存在 32 位元 God2_opt.exe。", L"请选择游戏安装目录中的 Launcher.exe，并确认同目录存在 32 位 God2_opt.exe。", L"Select Launcher.exe in the game folder and verify that a 32-bit God2_opt.exe is beside it."},
    {UiString::InvalidLauncherStatus, L"Launcher.exe 無效、缺少 God2_opt.exe，或遊戲不是 32 位元。", L"Launcher.exe 无效、缺少 God2_opt.exe，或游戏不是 32 位。", L"Launcher.exe is invalid, God2_opt.exe is missing, or the game is not 32-bit."},
    {UiString::NoOutput, L"尚未建立輸出資料夾。", L"尚未创建输出文件夹。", L"The output folder has not been created."},
    {UiString::OpenOutputFailed, L"無法開啟輸出資料夾。錯誤代碼：", L"无法打开输出文件夹。错误代码：", L"Could not open the output folder. Error: "},
    {UiString::NoSession, L"尚未建立 Capture Session。", L"尚未创建捕获会话。", L"No capture session has been created."},
    {UiString::ObservationFailed, L"無法建立觀測視窗。", L"无法创建观察窗口。", L"Could not create the observation window."},
    {UiString::NoClipboardPath, L"目前沒有可複製的輸出路徑。", L"当前没有可复制的输出路径。", L"There is no output path to copy."},
    {UiString::ProbeFailed, L"後端探測失敗；主視窗仍可操作", L"后端探测失败；主窗口仍可操作", L"Backend check failed; the main window remains usable"},
    {UiString::BusyStarting, L"遮罩語意取證流程正在啟動，請等候結果後再關閉。", L"遮罩语义取证流程正在启动，请等待完成后再关闭。", L"The masked semantic evidence workflow is starting. Wait for the result before closing."},
    {UiString::BusyStopping, L"正在安全完成語意證據與匯出，完成後才可關閉。", L"正在安全完成语义证据与导出，完成后才可关闭。", L"Semantic evidence and export are being finalized safely. Wait before closing."},
    {UiString::ConfirmExit, L"x86 遮罩語意證據收集正在進行。\n\n要安全完成並退出嗎？", L"x86 遮罩语义证据收集正在进行。\n\n要安全完成并退出吗？", L"x86 masked semantic evidence collection is active.\n\nFinalize it safely and exit?"},
    {UiString::AlreadyRunning, L"God2 Semantic Recovery Engine 已經執行中。\n\n既有主視窗仍在啟動，請稍候。", L"God2 Semantic Recovery Engine 已经在运行。\n\n现有主窗口仍在启动，请稍候。", L"God2 Semantic Recovery Engine is already running.\n\nIts main window is still starting; please wait."},
    {UiString::CaptureAlreadyRunning, L"遮罩語意取證流程已在執行。", L"遮罩语义取证流程已在运行。", L"A masked semantic evidence workflow is already running."},
    {UiString::NothingToStop, L"目前沒有可完成的語意取證流程。", L"当前没有可完成的语义取证流程。", L"There is no semantic evidence workflow to finalize."},
    {UiString::StartCompleteWaiting, L"x86 遮罩語意取證流程已就緒；系統層 raw payload 狀態為 EvidenceBlockedPrivacyPolicy。請在 Launcher.exe 按「開始遊戲」。", L"x86 遮罩语义取证流程已就绪；系统层 raw payload 状态为 EvidenceBlockedPrivacyPolicy。请在 Launcher.exe 点击“开始游戏”。", L"x86 masked semantic evidence workflow is ready; system-wide raw payload is EvidenceBlockedPrivacyPolicy. Click Start Game in Launcher.exe."},
    {UiString::StartCompleteTracking, L"x86 遮罩語意取證流程已鎖定 32 位元 God2_opt.exe；系統層 raw payload 維持 EvidenceBlockedPrivacyPolicy。", L"x86 遮罩语义取证流程已锁定 32 位 God2_opt.exe；系统层 raw payload 保持 EvidenceBlockedPrivacyPolicy。", L"x86 masked semantic evidence workflow is locked to the 32-bit God2_opt.exe; system-wide raw payload remains EvidenceBlockedPrivacyPolicy."},
    {UiString::WrongArchitecture, L"偵測到 God2_opt.exe，但它不是 32 位元程序；已停止把事件送入分析器。", L"检测到 God2_opt.exe，但它不是 32 位进程；已停止将事件送入分析器。", L"God2_opt.exe was found, but it is not a 32-bit process; events are blocked from analysis."},
    {UiString::HelpTitle, L"God2 Ultimate 使用說明", L"God2 Ultimate 使用说明", L"God2 Ultimate instructions"},
    {UiString::WorldTitle, L"God2 世界實體觀測", L"God2 世界实体观察", L"God2 World Entities"},
    {UiString::AnalysisTitle, L"God2 即時分析", L"God2 实时分析", L"God2 Live Analysis"},
    {UiString::WorldHeading, L"世界實體觀測（保留 EvidenceLevel；未觀測資料不會假定為 Verified）", L"世界实体观察（保留 EvidenceLevel；未观察数据不会假定为 Verified）", L"World entity observations (EvidenceLevel is preserved; unobserved data is never assumed Verified)"},
    {UiString::WorldEmpty, L"目前尚未觀測到世界實體資料。", L"当前尚未观察到世界实体数据。", L"No world entity data has been observed yet."},
    {UiString::AnalysisHeading, L"即時分析摘要", L"实时分析摘要", L"Live analysis summary"},
    {UiString::EnhancedCapture, L"x86 DLL 增強擷取（自動啟用／需要 UAC）", L"x86 DLL 增强捕获（自动启用／需要 UAC）", L"x86 DLL enhanced capture (automatic / UAC)"},
    {UiString::EnhancedCaptureTip, L"只處理已驗證的 32 位元 God2_opt.exe；短暫暫停主執行緒、載入 x86 Winsock DLL、確認初始化完成後恢復。停止時必須排空並證明 DLL 已卸載；尚未完成時維持 EvidenceBlocked、繼續有界重試，絕不誤報停止成功。", L"只处理已验证的 32 位 God2_opt.exe；短暂暂停主线程、加载 x86 Winsock DLL、确认初始化完成后恢复。停止时必须排空并证明 DLL 已卸载；尚未完成时保持 EvidenceBlocked、继续有界重试，绝不误报停止成功。", L"Only the verified x86 God2_opt.exe is handled. The primary thread is paused, the Winsock DLL is loaded and verified ready, then resumed. Stop succeeds only after draining and proving that the DLL is absent; until then it remains EvidenceBlocked and performs bounded retries without falsely reporting success."},
    {UiString::AnalysisTip, L"自動啟用：開始抓包後即時分析會自己執行並輸出封包、Opcode、玩法分類、未知封包與 Coverage；這個按鈕只是查看自動更新的摘要，不按也會分析。", L"自动启用：开始捕获后实时分析会自行运行并输出封包、Opcode、玩法分类、未知封包与 Coverage；此按钮只是查看自动更新摘要，不点也会分析。", L"Automatically enabled: capture starts the live analysis pipeline and writes packet, opcode, gameplay, unknown-packet, and coverage output. This button only views the auto-updating summary; analysis still runs if you never click it."},
    {UiString::WorldTip, L"自動啟用：玩法分類會自動抽取 NPC、怪物、角色、寵物、神仙與坐騎的座標、能力值及 EvidenceLevel；這個按鈕只是查看觀測結果，未觀測資料不會假稱 Verified。", L"自动启用：玩法分类会自动提取 NPC、怪物、角色、宠物、神仙与坐骑的坐标、属性及 EvidenceLevel；此按钮只是查看观察结果，未观察数据不会冒充 Verified。", L"Automatically enabled: gameplay analysis extracts NPC, monster, character, pet, immortal, and mount coordinates, attributes, and EvidenceLevel. This button only views observations; unobserved data is never claimed Verified."}
};

std::atomic<UiLanguage> g_ui_language{UiLanguage::English};

UiLanguage DetectUiLanguage() {
    const LANGID language = GetUserDefaultUILanguage();
    if (PRIMARYLANGID(language) != LANG_CHINESE) return UiLanguage::English;
    const WORD sublanguage = SUBLANGID(language);
    return sublanguage == SUBLANG_CHINESE_SIMPLIFIED || sublanguage == SUBLANG_CHINESE_SINGAPORE ?
        UiLanguage::SimplifiedChinese : UiLanguage::TraditionalChinese;
}

const wchar_t* Text(UiString id) {
    const auto language = g_ui_language.load();
    for (const auto& item : kUiTranslations) {
        if (item.id != id) continue;
        if (language == UiLanguage::TraditionalChinese) return item.traditional;
        if (language == UiLanguage::SimplifiedChinese) return item.simplified;
        return item.english;
    }
    return L"";
}

const wchar_t* Localized(const wchar_t* traditional, const wchar_t* simplified, const wchar_t* english) {
    const auto language = g_ui_language.load();
    return language == UiLanguage::TraditionalChinese ? traditional :
        (language == UiLanguage::SimplifiedChinese ? simplified : english);
}

const wchar_t* BackendStateText(BackendState state) {
    switch (state) {
    case BackendState::NotStarted: return Localized(L"尚未啟動", L"尚未启动", L"Not started");
    case BackendState::Starting: return Localized(L"正在啟動", L"正在启动", L"Starting");
    case BackendState::Active: return L"Active";
    case BackendState::EvidenceBlocked: return L"EvidenceBlockedPrivacyPolicy";
    case BackendState::Failed: return L"Failed";
    case BackendState::Stopping: return Localized(L"正在停止", L"正在停止", L"Stopping");
    case BackendState::Stopped: return L"Stopped";
    }
    return L"-";
}

bool SemanticWorkflowCanRun(BackendState state) {
    return state == BackendState::Active || state == BackendState::EvidenceBlocked;
}

const wchar_t* ConsumerStateText(ConsumerState state) {
    switch (state) {
    case ConsumerState::NotStarted:
        return Localized(L"自動啟用 · 等待已驗證的 God2_opt.exe",
                         L"自动启用 · 等待已验证的 God2_opt.exe",
                         L"Auto-enabled · waiting for verified God2_opt.exe");
    case ConsumerState::Starting:
        return Localized(L"DLL 附加／共享通道建立中", L"DLL 附加／共享通道建立中",
                         L"Attaching DLL / opening shared channel");
    case ConsumerState::Ready:
        return Localized(L"DLL Ready · 25 領域有界語意通道", L"DLL Ready · 25 领域有界语义通道",
                         L"DLL Ready · bounded 25-domain semantic channel");
    case ConsumerState::EvidenceBlocked:
        return Localized(L"EvidenceBlocked · DLL 尚未證明卸載 · 請重試停止",
                         L"EvidenceBlocked · DLL 尚未证明卸载 · 请重试停止",
                         L"EvidenceBlocked · DLL unload is not proven · retry Stop");
    case ConsumerState::Failed:
        return Localized(L"EvidenceBlocked · DLL 未附加", L"EvidenceBlocked · DLL 未附加",
                         L"EvidenceBlocked · DLL not attached");
    case ConsumerState::PostCaptureFallback:
        return Localized(L"DLL 通道未就緒 · 僅完成安全證據", L"DLL 通道未就绪 · 仅完成安全证据",
                         L"DLL channel unavailable · safe evidence only");
    case ConsumerState::Stopping:
        return Localized(L"DLL 停止／排空中", L"DLL 停止／排空中", L"Stopping / draining DLL");
    case ConsumerState::Stopped:
        return Localized(L"DLL 已安全停止", L"DLL 已安全停止", L"DLL stopped safely");
    }
    return L"-";
}

const wchar_t* AnalysisStateText(AnalysisState state) {
    switch (state) {
    case AnalysisState::NotStarted: return Localized(L"尚未啟動", L"尚未启动", L"Not started");
    case AnalysisState::WaitingForPid: return L"WaitingForPid";
    case AnalysisState::Starting: return L"Starting";
    case AnalysisState::NearRealtime: return L"NearRealtime";
    case AnalysisState::PostCaptureFallback: return L"PostCaptureFallback";
    case AnalysisState::Finalizing: return L"Finalizing";
    case AnalysisState::Completed: return L"Completed";
    case AnalysisState::Failed: return L"Failed";
    }
    return L"-";
}

const wchar_t* GameProcessStateText(GameProcessState state) {
    switch (state) {
    case GameProcessState::NotProbed: return Text(UiString::NotStarted);
    case GameProcessState::Searching: return Localized(L"正在尋找 God2_opt.exe", L"正在查找 God2_opt.exe", L"Searching for God2_opt.exe");
    case GameProcessState::WindowDetected: return Localized(L"已偵測到遊戲視窗，正在驗證程序", L"已检测到游戏窗口，正在验证进程", L"Game window detected; validating process");
    case GameProcessState::ProcessDetected: return Localized(L"已偵測到 God2_opt.exe，正在驗證", L"已检测到 God2_opt.exe，正在验证", L"God2_opt.exe detected; validating");
    case GameProcessState::AccessDenied: return Localized(L"已偵測到遊戲；權限不足，正由管理員 Worker 驗證", L"已检测到游戏；权限不足，正由管理员 Worker 验证", L"Game detected; elevated worker is validating access");
    case GameProcessState::PathMismatch: return Localized(L"偵測到同名程序，但完整路徑不符", L"检测到同名进程，但完整路径不匹配", L"Same-name process found, but path does not match");
    case GameProcessState::ArchitectureMismatch: return Text(UiString::WrongArchitecture);
    case GameProcessState::Validated: return L"God2_opt.exe";
    case GameProcessState::Exited: return Localized(L"God2_opt.exe 已退出，繼續搜尋", L"God2_opt.exe 已退出，继续查找", L"God2_opt.exe exited; searching continues");
    case GameProcessState::QueryFailed: return Localized(L"遊戲程序查詢失敗，將重試", L"游戏进程查询失败，将重试", L"Game process query failed; retrying");
    }
    return L"-";
}

enum class ControllerEventKind { Probe, Started, Stopped };

struct ControllerEvent {
    ControllerEventKind kind = ControllerEventKind::Probe;
    bool success = false;
    std::wstring message;
};

struct CaptureSnapshot {
    CaptureState state = CaptureState::Idle;
    BackendState backend_state = BackendState::NotStarted;
    ConsumerState consumer_state = ConsumerState::NotStarted;
    GameProcessState game_process_state = GameProcessState::NotProbed;
    AnalysisState analysis_state = AnalysisState::NotStarted;
    bool busy = false;
    bool capturing = false;
    std::wstring status = Text(UiString::Ready);
    std::wstring backend = Text(UiString::Probing);
    std::wstring consumer = ConsumerStateText(ConsumerState::NotStarted);
    std::wstring game_process = Text(UiString::NotStarted);
    std::wstring analysis = AnalysisStateText(AnalysisState::NotStarted);
    std::wstring gpu = L"Checking in background";
    std::wstring capture_scope;
    DWORD process_id = 0;
    DWORD game_process_error = ERROR_SUCCESS;
    DWORD parent_process_id = 0;
    std::uint64_t process_creation_time = 0;
    fs::path game_process_path;
    fs::path session_path;
    fs::path evidence_zip_path;
    std::string evidence_zip_sha256;
    std::string package_status;
    std::uint64_t capture_record_count = 0;
    std::uint64_t transport_chunk_count = 0;
    std::uint64_t protocol_frame_count = 0;
    std::uint64_t decoded_message_count = 0;
    std::uint64_t handler_observation_count = 0;
    std::uint64_t gameplay_candidate_count = 0;
    std::uint64_t packet_count = 0;
    std::uint64_t raw_etl_packet_count = 0;
    std::uint64_t captured_size = 0;
    std::uint64_t classified_count = 0;
    std::uint64_t candidate_frame_count = 0;
    std::uint64_t semantic_candidate_count = 0;
    std::uint64_t high_confidence_semantic_candidate_count = 0;
    std::uint64_t unknown_count = 0;
    std::uint64_t memory_bytes = 0;
    std::wstring started_at;
};

std::mutex g_log_mutex;
fs::path g_log_path;
std::wstring g_startup_log_warning;
std::atomic<bool> g_probe_running{false};
constexpr wchar_t kInstanceTagProperty[] = L"God2PacketCapture.InstanceTag";
std::wstring g_instance_property_name = kInstanceTagProperty;
std::mutex g_live_binding_mutex;
GuiLiveSessionBinding* g_live_binding = nullptr;

std::wstring NormalizeFinalHandlePath(std::wstring path) {
    constexpr std::wstring_view unc_prefix = L"\\\\?\\UNC\\";
    constexpr std::wstring_view local_prefix = L"\\\\?\\";
    if (path.starts_with(unc_prefix)) path = L"\\\\" + path.substr(unc_prefix.size());
    else if (path.starts_with(local_prefix)) path.erase(0, local_prefix.size());
    while (path.size() > 3 && (path.back() == L'\\' || path.back() == L'/')) path.pop_back();
    return path;
}

std::optional<std::wstring> FinalPathForHandle(HANDLE handle) {
    if (handle == nullptr || handle == INVALID_HANDLE_VALUE) return std::nullopt;
    std::vector<wchar_t> buffer(512);
    for (;;) {
        const DWORD length = GetFinalPathNameByHandleW(handle, buffer.data(),
            static_cast<DWORD>(buffer.size()), FILE_NAME_NORMALIZED | VOLUME_NAME_DOS);
        if (length == 0) return std::nullopt;
        if (length < buffer.size())
            return NormalizeFinalHandlePath(std::wstring(buffer.data(), length));
        buffer.resize(static_cast<std::size_t>(length) + 1U);
    }
}

bool IsLiveBindingNonce(std::string_view value) {
    return value.size() >= 32 && value.size() <= 128 &&
        std::all_of(value.begin(), value.end(), [](unsigned char character) {
            return std::isalnum(character) != 0 || character == '-';
        });
}

bool PublishLiveSessionBinding(const fs::path& session_path,
                               std::string_view session_id,
                               std::string* error) {
    std::lock_guard lock(g_live_binding_mutex);
    if (g_live_binding == nullptr) return true;
    auto& binding = *g_live_binding;
    if (!IsLiveBindingNonce(binding.challenge_nonce) || !binding.confirmation_nonce.empty() ||
        binding.session_directory_handle != nullptr || session_id.empty()) {
        binding.error = "live validation session challenge is invalid or was already consumed";
        if (error != nullptr) *error = binding.error;
        return false;
    }
    const fs::path capture_root = LocalDataRoot() / L"Captures";
    std::error_code path_error;
    const auto expected_root = fs::weakly_canonical(capture_root, path_error);
    const auto expected = fs::absolute(session_path, path_error).lexically_normal();
    const DWORD root_attributes = GetFileAttributesW(capture_root.c_str());
    const DWORD session_attributes = GetFileAttributesW(session_path.c_str());
    if (path_error || expected.empty() || expected_root.empty() ||
        expected.parent_path() != expected_root || root_attributes == INVALID_FILE_ATTRIBUTES ||
        session_attributes == INVALID_FILE_ATTRIBUTES ||
        (root_attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0 ||
        (session_attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0) {
        binding.error = "live validation session is outside the direct non-reparse capture root";
        if (error != nullptr) *error = binding.error;
        return false;
    }
    HANDLE directory = CreateFileW(session_path.c_str(), FILE_READ_ATTRIBUTES | SYNCHRONIZE,
        FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING,
        FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
    BY_HANDLE_FILE_INFORMATION identity{};
    const auto final_path = FinalPathForHandle(directory);
    const bool identity_read = directory != INVALID_HANDLE_VALUE &&
        GetFileInformationByHandle(directory, &identity) != FALSE;
    const bool final_matches = final_path &&
        _wcsicmp(final_path->c_str(), expected.wstring().c_str()) == 0;
    if (!identity_read || !final_matches ||
        (identity.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0 ||
        (identity.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0) {
        if (directory != INVALID_HANDLE_VALUE) CloseHandle(directory);
        binding.error = "live validation session handle identity/final path validation failed";
        if (error != nullptr) *error = binding.error;
        return false;
    }
    binding.confirmation_nonce = binding.challenge_nonce;
    binding.session_path = expected;
    binding.session_id = std::string(session_id);
    binding.final_directory_path = *final_path;
    binding.session_directory_handle = directory;
    binding.volume_serial_number = identity.dwVolumeSerialNumber;
    binding.file_index_high = identity.nFileIndexHigh;
    binding.file_index_low = identity.nFileIndexLow;
    binding.error.clear();
    return true;
}

class LiveBindingRegistration {
public:
    explicit LiveBindingRegistration(GuiLiveSessionBinding* binding) : binding_(binding) {
        std::lock_guard lock(g_live_binding_mutex);
        if (binding_ == nullptr) return;
        if (g_live_binding != nullptr) {
            binding_->error = "another in-memory live validation binding is active";
            return;
        }
        binding_->confirmation_nonce.clear();
        binding_->session_path.clear();
        binding_->session_id.clear();
        binding_->final_directory_path.clear();
        binding_->error.clear();
        g_live_binding = binding_;
        registered_ = true;
    }
    ~LiveBindingRegistration() {
        std::lock_guard lock(g_live_binding_mutex);
        if (registered_ && g_live_binding == binding_) g_live_binding = nullptr;
    }
    bool Registered() const noexcept { return binding_ == nullptr || registered_; }
private:
    GuiLiveSessionBinding* binding_ = nullptr;
    bool registered_ = false;
};

BOOL CALLBACK FindTaggedMainWindow(HWND window, LPARAM parameter) {
    auto* result = reinterpret_cast<HWND*>(parameter);
    wchar_t class_name[128]{};
    wchar_t title[256]{};
    GetClassNameW(window, class_name, static_cast<int>(std::size(class_name)));
    GetWindowTextW(window, title, static_cast<int>(std::size(title)));
    const bool matching_tag = GetPropW(window, g_instance_property_name.c_str()) != nullptr;
    if (wcscmp(class_name, kWindowClass) == 0 && wcscmp(title, kWindowTitle) == 0 && matching_tag) {
        *result = window;
        return FALSE;
    }
    return TRUE;
}

HWND ExistingInstanceWindow() {
    HWND result = nullptr;
    EnumWindows(FindTaggedMainWindow, reinterpret_cast<LPARAM>(&result));
    return result;
}

class UniqueHandle final {
public:
    explicit UniqueHandle(HANDLE handle = nullptr) : handle_(handle) {}
    ~UniqueHandle() { if (handle_ != nullptr && handle_ != INVALID_HANDLE_VALUE) CloseHandle(handle_); }
    UniqueHandle(const UniqueHandle&) = delete;
    UniqueHandle& operator=(const UniqueHandle&) = delete;
    UniqueHandle(UniqueHandle&& other) noexcept : handle_(other.handle_) { other.handle_ = nullptr; }
    UniqueHandle& operator=(UniqueHandle&& other) noexcept {
        if (this != &other) {
            if (handle_ != nullptr && handle_ != INVALID_HANDLE_VALUE) CloseHandle(handle_);
            handle_ = other.handle_;
            other.handle_ = nullptr;
        }
        return *this;
    }
    HANDLE Get() const { return handle_; }

private:
    HANDLE handle_ = nullptr;
};

std::wstring ErrorText(DWORD error);

bool CurrentProcessUserSidString(std::wstring* value, std::string* error) {
    if (value == nullptr) return false;
    value->clear();
    UniqueHandle token;
    HANDLE raw_token = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &raw_token)) {
        if (error != nullptr) *error = "cannot query the current user token: " +
            WideToUtf8(ErrorText(GetLastError()));
        return false;
    }
    token = UniqueHandle(raw_token);
    DWORD bytes = 0;
    GetTokenInformation(token.Get(), TokenUser, nullptr, 0, &bytes);
    if (bytes == 0 || GetLastError() != ERROR_INSUFFICIENT_BUFFER) {
        if (error != nullptr) *error = "cannot size the current user SID";
        return false;
    }
    std::vector<unsigned char> storage(bytes);
    if (!GetTokenInformation(token.Get(), TokenUser, storage.data(), bytes, &bytes)) {
        if (error != nullptr) *error = "cannot read the current user SID: " +
            WideToUtf8(ErrorText(GetLastError()));
        return false;
    }
    const auto* token_user = reinterpret_cast<const TOKEN_USER*>(storage.data());
    LPWSTR text_sid = nullptr;
    if (!IsValidSid(token_user->User.Sid) ||
        !ConvertSidToStringSidW(token_user->User.Sid, &text_sid) || text_sid == nullptr) {
        if (error != nullptr) *error = "cannot serialize the current user SID";
        return false;
    }
    *value = text_sid;
    LocalFree(text_sid);
    return !value->empty();
}

bool ValidateProtectedPayloadSecurityDescriptor(PSECURITY_DESCRIPTOR descriptor,
                                                std::wstring_view user_sid_text,
                                                std::string* error) {
    if (descriptor == nullptr || !IsValidSecurityDescriptor(descriptor) ||
        user_sid_text.empty()) {
        if (error != nullptr) *error = "protected payload security descriptor is invalid";
        return false;
    }
    SECURITY_DESCRIPTOR_CONTROL control{};
    DWORD revision = 0;
    if (!GetSecurityDescriptorControl(descriptor, &control, &revision) ||
        (control & SE_DACL_PROTECTED) == 0) {
        if (error != nullptr) *error = "protected payload DACL is not inheritance-protected";
        return false;
    }
    PSID user_sid = nullptr;
    if (!ConvertStringSidToSidW(std::wstring(user_sid_text).c_str(), &user_sid) ||
        user_sid == nullptr) {
        if (error != nullptr) *error = "cannot materialize the current user SID";
        return false;
    }
    std::array<unsigned char, SECURITY_MAX_SID_SIZE> world_storage{};
    DWORD world_bytes = static_cast<DWORD>(world_storage.size());
    PSID world_sid = world_storage.data();
    const bool world_ready = CreateWellKnownSid(WinWorldSid, nullptr, world_sid, &world_bytes) != FALSE;
    BOOL dacl_present = FALSE;
    BOOL dacl_defaulted = FALSE;
    PACL dacl = nullptr;
    bool user_allow_found = false;
    ACCESS_MASK user_mask = 0;
    bool world_ace_found = false;
    if (!GetSecurityDescriptorDacl(descriptor, &dacl_present, &dacl, &dacl_defaulted) ||
        !dacl_present || dacl == nullptr || !world_ready) {
        LocalFree(user_sid);
        if (error != nullptr) *error = "protected payload DACL is unavailable";
        return false;
    }
    for (DWORD index = 0; index < dacl->AceCount; ++index) {
        void* raw_ace = nullptr;
        if (!GetAce(dacl, index, &raw_ace) || raw_ace == nullptr) continue;
        const auto* header = static_cast<const ACE_HEADER*>(raw_ace);
        if (header->AceType != ACCESS_ALLOWED_ACE_TYPE &&
            header->AceType != ACCESS_DENIED_ACE_TYPE) continue;
        const auto* ace = static_cast<const ACCESS_ALLOWED_ACE*>(raw_ace);
        PSID ace_sid = const_cast<DWORD*>(&ace->SidStart);
        if (EqualSid(ace_sid, world_sid)) world_ace_found = true;
        if (header->AceType == ACCESS_ALLOWED_ACE_TYPE && EqualSid(ace_sid, user_sid)) {
            user_allow_found = true;
            user_mask |= ace->Mask;
        }
    }
    const ACCESS_MASK forbidden = GENERIC_WRITE | DELETE | WRITE_DAC | WRITE_OWNER |
        FILE_WRITE_DATA | FILE_APPEND_DATA | FILE_WRITE_EA | FILE_WRITE_ATTRIBUTES |
        FILE_DELETE_CHILD;
    const bool user_rx_only = user_allow_found && (user_mask & GENERIC_READ) != 0 &&
        (user_mask & GENERIC_EXECUTE) != 0 && (user_mask & forbidden) == 0;

    BOOL sacl_present = FALSE;
    BOOL sacl_defaulted = FALSE;
    PACL sacl = nullptr;
    bool medium_no_write_up = false;
    if (GetSecurityDescriptorSacl(descriptor, &sacl_present, &sacl, &sacl_defaulted) &&
        sacl_present && sacl != nullptr) {
        for (DWORD index = 0; index < sacl->AceCount; ++index) {
            void* raw_ace = nullptr;
            if (!GetAce(sacl, index, &raw_ace) || raw_ace == nullptr) continue;
            const auto* header = static_cast<const ACE_HEADER*>(raw_ace);
            if (header->AceType != SYSTEM_MANDATORY_LABEL_ACE_TYPE) continue;
            const auto* label = static_cast<const SYSTEM_MANDATORY_LABEL_ACE*>(raw_ace);
            PSID label_sid = const_cast<DWORD*>(&label->SidStart);
            if (!IsValidSid(label_sid)) continue;
            const UCHAR count = *GetSidSubAuthorityCount(label_sid);
            if (count == 0) continue;
            const DWORD rid = *GetSidSubAuthority(label_sid, count - 1);
            medium_no_write_up = rid == SECURITY_MANDATORY_MEDIUM_RID &&
                (label->Mask & SYSTEM_MANDATORY_LABEL_NO_WRITE_UP) != 0;
        }
    }
    LocalFree(user_sid);
    if (world_ace_found || !user_rx_only || !medium_no_write_up) {
        if (error != nullptr) *error = world_ace_found ?
            "protected payload DACL contains an Everyone ACE" :
            !user_rx_only ? "current user payload rights are not read/execute-only" :
                            "protected payload mandatory label is not medium/no-write-up";
        return false;
    }
    return true;
}

bool BuildProtectedPayloadSecurityDescriptor(PSECURITY_DESCRIPTOR* descriptor,
                                             std::string* error) {
    if (descriptor == nullptr) return false;
    *descriptor = nullptr;
    std::wstring user_sid;
    if (!CurrentProcessUserSidString(&user_sid, error)) return false;
    const std::wstring sddl =
        L"O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;GRGX;;;" +
        user_sid + L")S:(ML;OICI;NW;;;ME)";
    if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(
            sddl.c_str(), SDDL_REVISION_1, descriptor, nullptr)) {
        if (error != nullptr) *error = "cannot construct the protected payload ACL: " +
            WideToUtf8(ErrorText(GetLastError()));
        return false;
    }
    if (!ValidateProtectedPayloadSecurityDescriptor(*descriptor, user_sid, error)) {
        LocalFree(*descriptor);
        *descriptor = nullptr;
        return false;
    }
    return true;
}

class StartupFailure final : public std::runtime_error {
public:
    StartupFailure(std::wstring stage, std::wstring api, std::string message, DWORD win32_error,
                   std::wstring return_value = L"failure", HRESULT result = S_OK)
        : std::runtime_error(std::move(message)), stage_(std::move(stage)), api_(std::move(api)),
          win32_error_(win32_error), return_value_(std::move(return_value)), hresult_(result) {}

    const std::wstring& Stage() const { return stage_; }
    const std::wstring& Api() const { return api_; }
    DWORD Win32Error() const { return win32_error_; }
    const std::wstring& ReturnValue() const { return return_value_; }
    HRESULT Result() const { return hresult_; }

private:
    std::wstring stage_;
    std::wstring api_;
    DWORD win32_error_ = ERROR_SUCCESS;
    std::wstring return_value_;
    HRESULT hresult_ = S_OK;
};

std::wstring ErrorText(DWORD error) {
    wchar_t* buffer = nullptr;
    const DWORD length = FormatMessageW(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM |
                                        FORMAT_MESSAGE_IGNORE_INSERTS,
                                        nullptr, error, 0, reinterpret_cast<wchar_t*>(&buffer), 0, nullptr);
    std::wstring result = length != 0 && buffer != nullptr ? std::wstring(buffer, length) :
        L"Win32 error " + std::to_wstring(error);
    if (buffer != nullptr) LocalFree(buffer);
    while (!result.empty() && (result.back() == L'\r' || result.back() == L'\n')) result.pop_back();
    return result;
}

std::wstring LocalTimestamp(bool file_name) {
    SYSTEMTIME time{};
    GetLocalTime(&time);
    wchar_t buffer[64]{};
    if (file_name) {
        swprintf_s(buffer, L"%04u-%02u-%02u_%02u-%02u-%02u", time.wYear, time.wMonth, time.wDay,
                   time.wHour, time.wMinute, time.wSecond);
    } else {
        swprintf_s(buffer, L"%04u-%02u-%02u %02u:%02u:%02u", time.wYear, time.wMonth, time.wDay,
                   time.wHour, time.wMinute, time.wSecond);
    }
    return buffer;
}

fs::path StartupLogRoot() {
    int argument_count = 0;
    wchar_t** arguments = CommandLineToArgvW(GetCommandLineW(), &argument_count);
    if (arguments != nullptr) {
        for (int index = 1; index + 1 < argument_count; ++index) {
            if (wcscmp(arguments[index], L"--internal-gui-test-log-root") != 0) continue;
            const fs::path candidate(arguments[index + 1]);
            const bool approved = IsApprovedSessionPath(candidate);
            LocalFree(arguments);
            return approved ? candidate : fs::path{};
        }
        LocalFree(arguments);
    }
    const auto root = LocalDataRoot();
    if (!root.empty()) return root / L"Logs";
    std::vector<wchar_t> temporary(32'768);
    const DWORD length = GetTempPathW(static_cast<DWORD>(temporary.size()), temporary.data());
    if (length == 0 || length >= temporary.size()) return {};
    return fs::path(std::wstring(temporary.data(), length)) / L"God2PacketCapture";
}

bool EnsureDirectoryTree(const fs::path& path, DWORD* captured_error = nullptr) {
    if (captured_error != nullptr) *captured_error = ERROR_SUCCESS;
    if (path.empty()) {
        if (captured_error != nullptr) *captured_error = ERROR_PATH_NOT_FOUND;
        return false;
    }
    const fs::path normalized = path.lexically_normal();
    fs::path current = normalized.root_path();
    for (const auto& component : normalized.relative_path()) {
        current /= component;
        if (CreateDirectoryW(current.c_str(), nullptr)) continue;
        const DWORD create_error = GetLastError();
        if (create_error == ERROR_ALREADY_EXISTS) {
            const DWORD attributes = GetFileAttributesW(current.c_str());
            if (attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0) continue;
            const DWORD attribute_error = attributes == INVALID_FILE_ATTRIBUTES ? GetLastError() : ERROR_DIRECTORY;
            if (captured_error != nullptr) *captured_error = attribute_error;
            return false;
        }
        if (captured_error != nullptr) *captured_error = create_error;
        return false;
    }
    return true;
}

void AppendStartupLog(std::wstring_view key, std::wstring_view value) noexcept {
    // Diagnostics must never turn a recoverable GUI/controller failure into a
    // process-terminating exception (for example, allocation or codec errors).
    try {
        std::lock_guard lock(g_log_mutex);
        if (g_log_path.empty()) return;
        std::ofstream stream(g_log_path, std::ios::binary | std::ios::app);
        if (!stream) return;
        stream << WideToUtf8(key) << '=' << WideToUtf8(value) << "\r\n";
    } catch (...) {
    }
}

void LogStageBegin(std::wstring_view stage) {
    AppendStartupLog(L"StageBegin", stage);
}

void LogStageSuccess(std::wstring_view stage) {
    AppendStartupLog(L"StageSuccess", stage);
}

void LogStageFailure(std::wstring_view stage, std::wstring_view api, std::wstring_view return_value,
                     DWORD win32_error, HRESULT result = S_OK) {
    AppendStartupLog(L"StageFailure", stage);
    AppendStartupLog(L"ApiName", api);
    AppendStartupLog(L"ReturnValue", return_value);
    AppendStartupLog(L"CapturedWin32Error", std::to_wstring(win32_error));
    wchar_t result_text[16]{};
    swprintf_s(result_text, L"0x%08lX", static_cast<unsigned long>(result));
    AppendStartupLog(L"HRESULT", result_text);
}

bool InitializeCommonControlsForGui(DWORD* failure_error) {
    INITCOMMONCONTROLSEX controls{};
    controls.dwSize = sizeof(controls);
    controls.dwICC = ICC_WIN95_CLASSES | ICC_STANDARD_CLASSES | ICC_LISTVIEW_CLASSES |
                     ICC_TREEVIEW_CLASSES | ICC_TAB_CLASSES | ICC_PROGRESS_CLASS | ICC_BAR_CLASSES;
    SetLastError(ERROR_SUCCESS);
    const BOOL initialized = InitCommonControlsEx(&controls);
    if (!initialized) {
        const DWORD captured_error = GetLastError();
        if (failure_error != nullptr) *failure_error = captured_error;
        return false;
    }
    return true;
}

void RegisterOrValidateWindowClass(const WNDCLASSEXW& requested, std::wstring_view stage) {
    const ATOM atom = RegisterClassExW(&requested);
    if (atom != 0) return;
    const DWORD register_error = GetLastError();
    if (register_error == ERROR_CLASS_ALREADY_EXISTS) {
        WNDCLASSEXW existing{};
        existing.cbSize = sizeof(existing);
        const BOOL found = GetClassInfoExW(requested.hInstance, requested.lpszClassName, &existing);
        if (!found) {
            const DWORD lookup_error = GetLastError();
            throw StartupFailure(std::wstring(stage), L"GetClassInfoExW",
                                 "registered window class could not be inspected", lookup_error, L"FALSE");
        }
        if (existing.lpfnWndProc == requested.lpfnWndProc && existing.hInstance == requested.hInstance) return;
        throw StartupFailure(std::wstring(stage), L"RegisterClassExW",
                             "an incompatible window class is already registered", ERROR_CLASS_ALREADY_EXISTS,
                             L"0 (class mismatch)");
    }
    throw StartupFailure(std::wstring(stage), L"RegisterClassExW",
                         "window class registration failed", register_error, L"0");
}

bool InitializeStartupLog() {
    const auto root = StartupLogRoot();
    if (root.empty()) {
        g_startup_log_warning = Localized(L"無法解析啟動日誌資料夾。", L"无法解析启动日志文件夹。", L"Could not resolve the startup log folder.");
        return false;
    }
    DWORD directory_error = ERROR_SUCCESS;
    if (!EnsureDirectoryTree(root, &directory_error)) {
        g_startup_log_warning = std::wstring(Localized(L"無法建立啟動日誌資料夾。Win32=", L"无法创建启动日志文件夹。Win32=", L"Could not create the startup log folder. Win32=")) + std::to_wstring(directory_error);
        return false;
    }
    g_log_path = root / (L"startup-" + LocalTimestamp(true) + L"-" + std::to_wstring(GetCurrentProcessId()) + L".log");
    HANDLE log_file = CreateFileW(g_log_path.c_str(), GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                                  nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (log_file == INVALID_HANDLE_VALUE) {
        const DWORD open_error = GetLastError();
        g_log_path.clear();
        g_startup_log_warning = std::wstring(Localized(L"無法開啟啟動日誌。Win32=", L"无法打开启动日志。Win32=", L"Could not open the startup log. Win32=")) + std::to_wstring(open_error);
        return false;
    }
    CloseHandle(log_file);
    AppendStartupLog(L"StageBegin", L"CreateLogDirectory");
    AppendStartupLog(L"StageSuccess", L"CreateLogDirectory");
    AppendStartupLog(L"StageBegin", L"OpenStartupLog");
    AppendStartupLog(L"StageSuccess", L"OpenStartupLog");
    AppendStartupLog(L"ToolVersion", GOD2_TOOL_VERSION_W L"-gui");
    AppendStartupLog(L"ExecutablePath", ExecutablePath().wstring());
    const auto os = DetectOsVersion();
    AppendStartupLog(L"OperatingSystem", Utf8ToWide(os.edition) + L" build " + std::to_wstring(os.build));
    AppendStartupLog(L"ProcessId", std::to_wstring(GetCurrentProcessId()));
    AppendStartupLog(L"IsElevated", IsAdministrator() ? L"true" : L"false");
    AppendStartupLog(L"CommandLine", GetCommandLineW());
    return true;
}

LONG WINAPI UnhandledExceptionLogger(EXCEPTION_POINTERS* exception) {
    const DWORD last_observed_error = GetLastError();
    const DWORD code = exception != nullptr && exception->ExceptionRecord != nullptr ?
        exception->ExceptionRecord->ExceptionCode : ERROR_UNHANDLED_EXCEPTION;
    wchar_t code_text[16]{};
    swprintf_s(code_text, L"0x%08lX", code);
    AppendStartupLog(L"ExceptionType", L"SEH");
    AppendStartupLog(L"LastObservedWin32Error", std::to_wstring(last_observed_error));
    AppendStartupLog(L"HRESULT", code_text);
    AppendStartupLog(L"ExceptionMessage", L"Unhandled structured exception " + std::to_wstring(code));
    return EXCEPTION_EXECUTE_HANDLER;
}

std::wstring QuoteArgument(std::wstring_view value) {
    std::wstring result = L"\"";
    std::size_t slashes = 0;
    for (const wchar_t c : value) {
        if (c == L'\\') ++slashes;
        else if (c == L'\"') {
            result.append(slashes * 2 + 1, L'\\');
            result.push_back(L'\"');
            slashes = 0;
        } else {
            result.append(slashes, L'\\');
            result.push_back(c);
            slashes = 0;
        }
    }
    result.append(slashes * 2, L'\\');
    result.push_back(L'\"');
    return result;
}

std::optional<std::wstring> ArgumentValue(const std::vector<std::wstring>& arguments, std::wstring_view option) {
    for (std::size_t index = 0; index + 1 < arguments.size(); ++index) {
        if (arguments[index] == option) return arguments[index + 1];
    }
    return std::nullopt;
}

std::unique_ptr<ICaptureBackend> CreateBackend(std::string_view name) {
    if (name == "PktMonCaptureBackend") return std::make_unique<PktMonCaptureBackend>();
    if (name == "EtwNetworkTraceBackend") return std::make_unique<EtwNetworkTraceBackend>();
    return nullptr;
}

bool IsRawPrivacyPolicyBlock(const CaptureResult& result) {
    return !result.success && result.exit_code == ERROR_ACCESS_DISABLED_BY_POLICY &&
        result.message.rfind("EvidenceBlockedPrivacyPolicy:", 0) == 0;
}

bool IsForbiddenSystemRawArtifact(const fs::path& path) {
    std::wstring name = path.filename().wstring();
    std::transform(name.begin(), name.end(), name.begin(),
        [](wchar_t value) { return static_cast<wchar_t>(towlower(value)); });
    std::wstring extension = path.extension().wstring();
    std::transform(extension.begin(), extension.end(), extension.begin(),
        [](wchar_t value) { return static_cast<wchar_t>(towlower(value)); });
    return name == L"etw-events.jsonl" || name == L"capture.etl" ||
        extension == L".etl" || extension == L".pcap" || extension == L".pcapng" ||
        extension == L".cap";
}

bool EnforceRawNetworkPrivacyPolicy(const fs::path& session_path,
                                    std::string* error,
                                    std::uint64_t* removed_count = nullptr) {
    if (removed_count != nullptr) *removed_count = 0;
    if (!IsApprovedSessionPath(session_path)) {
        if (error != nullptr) *error = "raw-network privacy enforcement rejected an unapproved session path";
        return false;
    }
    const fs::path raw = session_path / L"raw";
    std::error_code operation_error;
    const DWORD raw_attributes = GetFileAttributesW(Win32ExtendedPathForFileIo(raw).c_str());
    if (raw_attributes != INVALID_FILE_ATTRIBUTES &&
        (raw_attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0) {
        if (error != nullptr) *error = "raw-network privacy enforcement rejected a reparse-point raw directory";
        return false;
    }

    std::uint64_t removed = 0;
    if (fs::is_directory(raw, operation_error) && !operation_error) {
        for (fs::directory_iterator iterator(raw, operation_error), end;
             iterator != end && !operation_error; iterator.increment(operation_error)) {
            const fs::path candidate = iterator->path();
            if (!IsForbiddenSystemRawArtifact(candidate)) continue;
            const auto status = iterator->symlink_status(operation_error);
            if (operation_error) break;
            if (!fs::is_regular_file(status) && !fs::is_symlink(status)) {
                if (error != nullptr) *error = "forbidden raw-network artifact is not a removable regular file";
                return false;
            }
            if (!fs::remove(candidate, operation_error) || operation_error) break;
            ++removed;
        }
    }
    if (operation_error) {
        if (error != nullptr) *error = "failed to remove a forbidden system-wide raw-network artifact: " +
            operation_error.message();
        return false;
    }

    // Re-enumerate after deletion.  A failed removal or immediate replacement
    // blocks both package builders; absence is established structurally, never
    // inferred by scanning the artifact for selected strings.
    if (fs::is_directory(raw, operation_error) && !operation_error) {
        for (fs::directory_iterator iterator(raw, operation_error), end;
             iterator != end && !operation_error; iterator.increment(operation_error)) {
            if (IsForbiddenSystemRawArtifact(iterator->path())) {
                if (error != nullptr) *error = "a forbidden system-wide raw-network artifact remains after privacy enforcement";
                return false;
            }
        }
    }
    if (operation_error) {
        if (error != nullptr) *error = "could not verify raw-network artifact absence: " + operation_error.message();
        return false;
    }

    DWORD directory_error = ERROR_SUCCESS;
    if (!EnsureDirectoryTree(session_path / L"reports", &directory_error)) {
        if (error != nullptr) *error = "could not create the privacy report directory";
        return false;
    }
    const std::string report = MakeJsonObject({
        {"SchemaVersion", "1"},
        {"Status", "PASS_RAW_SYSTEM_PAYLOAD_DISABLED"},
        {"RawSystemCaptureStatus", "EvidenceBlockedPrivacyPolicy"},
        {"RawSystemPayloadArtifactsPresent", "false"},
        {"PayloadBytesCapturedBySystemBackend", "false"},
        {"AuthenticationPlaintextPersistence", "Prohibited"},
        {"X86MaskedSemanticChannel", "AllowedExactBuildOnly"},
        {"RemovedForbiddenArtifactCount", std::to_string(removed)},
        {"Enforcement", "FailClosedDeleteAndReenumerateBeforePackaging"}
    }, {"SchemaVersion", "RawSystemPayloadArtifactsPresent",
        "PayloadBytesCapturedBySystemBackend", "RemovedForbiddenArtifactCount"});
    if (!WriteUtf8FileAtomic(session_path / L"reports" / L"raw-network-privacy-policy.json",
                             report + "\n")) {
        if (error != nullptr) *error = "could not commit the raw-network privacy policy report";
        return false;
    }
    if (removed_count != nullptr) *removed_count = removed;
    return true;
}

std::optional<bool> EvidenceZipContainsText(const fs::path& zip_path, std::string_view needle) {
    if (needle.empty()) return false;
    std::vector<char> buffer;
    try {
        buffer.resize(16U * 1024U);
    } catch (const std::bad_alloc&) {
        return std::nullopt;
    }
    zlib_filefunc64_def file_functions{};
    fill_win32_filefunc64W(&file_functions);
    unzFile archive = unzOpen2_64(zip_path.c_str(), &file_functions);
    if (archive == nullptr) return std::nullopt;
    unz_global_info64 global{};
    if (unzGetGlobalInfo64(archive, &global) != UNZ_OK ||
        (global.number_entry != 0 && unzGoToFirstFile(archive) != UNZ_OK)) {
        unzClose(archive);
        return std::nullopt;
    }
    bool found = false;
    bool valid = true;
    for (ZPOS64_T index = 0; index < global.number_entry && valid && !found; ++index) {
        unz_file_info64 info{};
        if (unzGetCurrentFileInfo64(archive, &info, nullptr, 0, nullptr, 0, nullptr, 0) != UNZ_OK ||
            unzOpenCurrentFile(archive) != UNZ_OK) {
            valid = false;
            break;
        }
        std::string overlap;
        for (;;) {
            const int read = unzReadCurrentFile(archive, buffer.data(),
                                                static_cast<unsigned>(buffer.size()));
            if (read < 0) { valid = false; break; }
            if (read == 0) break;
            std::string window = std::move(overlap);
            window.append(buffer.data(), static_cast<std::size_t>(read));
            if (window.find(needle) != std::string::npos) { found = true; break; }
            const std::size_t keep = (std::min)(needle.size() - 1, window.size());
            overlap.assign(window.end() - static_cast<std::ptrdiff_t>(keep), window.end());
        }
        if (unzCloseCurrentFile(archive) != UNZ_OK) valid = false;
        if (index + 1 < global.number_entry && valid && !found &&
            unzGoToNextFile(archive) != UNZ_OK) valid = false;
    }
    unzClose(archive);
    if (!valid) return std::nullopt;
    return found;
}

bool WritePipeMessage(HANDLE pipe, std::string_view message) {
    const std::string framed = std::string(message) + "\n";
    DWORD written = 0;
    return WriteFile(pipe, framed.data(), static_cast<DWORD>(framed.size()), &written, nullptr) &&
           written == framed.size();
}

std::optional<std::string> ReadPipeMessage(HANDLE pipe, DWORD timeout_ms = INFINITE,
                                           HANDLE peer_process = nullptr,
                                           HANDLE cancel_event = nullptr) {
    std::string result;
    std::array<char, 512> buffer{};
    const ULONGLONG started_at = GetTickCount64();
    for (;;) {
        if (cancel_event != nullptr && WaitForSingleObject(cancel_event, 0) == WAIT_OBJECT_0) {
            SetLastError(ERROR_CANCELLED);
            return std::nullopt;
        }
        DWORD read = 0;
        if (ReadFile(pipe, buffer.data(), static_cast<DWORD>(buffer.size()), &read, nullptr)) {
            if (read == 0) {
                if (peer_process != nullptr && WaitForSingleObject(peer_process, 0) == WAIT_OBJECT_0) return std::nullopt;
                if (timeout_ms != INFINITE && GetTickCount64() - started_at >= timeout_ms) return std::nullopt;
                Sleep(50);
                continue;
            }
            result.append(buffer.data(), read);
        } else {
            const DWORD code = GetLastError();
            if (code != ERROR_NO_DATA && code != ERROR_PIPE_LISTENING) return std::nullopt;
            if (peer_process != nullptr && WaitForSingleObject(peer_process, 0) == WAIT_OBJECT_0) return std::nullopt;
            if (timeout_ms != INFINITE && GetTickCount64() - started_at >= timeout_ms) return std::nullopt;
            Sleep(50);
            continue;
        }
        const auto newline = result.find('\n');
        if (newline != std::string::npos) {
            result.resize(newline);
            return result;
        }
        if (result.size() > 64 * 1024) return std::nullopt;
    }
}

bool IsDelayedGameProbeResponse(std::string_view response, std::string_view worker_nonce) {
    return response.rfind("GAME_PROCESS\t" + std::string(worker_nonce) + "\t", 0) == 0;
}

std::uint64_t DirectoryBytes(const fs::path& root) {
    std::uint64_t total = 0;
    std::error_code error;
    if (root.empty() || !fs::exists(root, error)) return 0;
    for (fs::recursive_directory_iterator it(root, fs::directory_options::skip_permission_denied, error), end;
         it != end && !error; it.increment(error)) {
        if (it->is_regular_file(error)) total += it->file_size(error);
    }
    return total;
}

std::uint64_t CountLines(const fs::path& path) {
    std::ifstream stream(path, std::ios::binary);
    if (!stream) return 0;
    return static_cast<std::uint64_t>(std::count(std::istreambuf_iterator<char>(stream),
                                                 std::istreambuf_iterator<char>(), '\n'));
}

struct CandidateClassificationMetrics {
    std::uint64_t candidate_frames = 0;
    std::uint64_t semantic_candidates = 0;
    std::uint64_t high_confidence_semantic_candidates = 0;
};

struct RawEtlMetrics {
    bool report_present = false;
    bool audit_success = false;
    std::uint64_t total_events = 0;
    std::uint64_t ndis_packet_events = 0;
};

std::uint64_t JsonUInt64(const Fields& fields, std::string_view key) {
    const auto value = GetInt64(fields, key);
    return value && *value > 0 ? static_cast<std::uint64_t>(*value) : 0;
}

CandidateClassificationMetrics ReadCandidateClassificationMetrics(const fs::path& session_path) {
    CandidateClassificationMetrics metrics;
    Fields fields;
    std::string parse_error;
    if (const auto frame_report = ReadUtf8File(session_path / L"reports" / L"god2-frame-candidates.json");
        frame_report && ParseFlatJson(*frame_report, fields, &parse_error)) {
        metrics.candidate_frames = JsonUInt64(fields, "CandidateFrames");
    }
    if (metrics.candidate_frames == 0) {
        metrics.candidate_frames = CountLines(session_path / L"raw" / L"god2-frame-candidates.jsonl");
    }
    fields.clear();
    parse_error.clear();
    if (const auto semantic_report = ReadUtf8File(session_path / L"reports" / L"god2-opcode-semantic-candidates.json");
        semantic_report && ParseFlatJson(*semantic_report, fields, &parse_error)) {
        metrics.semantic_candidates = JsonUInt64(fields, "SemanticCandidateCount");
        metrics.high_confidence_semantic_candidates = JsonUInt64(fields, "HighConfidenceSemanticCandidates");
    }
    if (metrics.semantic_candidates == 0) {
        metrics.semantic_candidates = CountLines(session_path / L"raw" / L"god2-opcode-semantic-candidates.jsonl");
    }
    if (metrics.high_confidence_semantic_candidates > metrics.semantic_candidates) {
        metrics.high_confidence_semantic_candidates = metrics.semantic_candidates;
    }
    return metrics;
}

RawEtlMetrics ReadRawEtlMetrics(const fs::path& session_path) {
    RawEtlMetrics metrics;
    const auto report = ReadUtf8File(session_path / L"reports" / L"raw-etl-inventory.json");
    if (!report) return metrics;
    Fields fields;
    std::string parse_error;
    if (!ParseFlatJson(*report, fields, &parse_error)) return metrics;
    metrics.report_present = true;
    metrics.audit_success = GetString(fields, "Status") != "Failed";
    metrics.total_events = JsonUInt64(fields, "TotalEvents");
    metrics.ndis_packet_events = JsonUInt64(fields, "NdisPacketEvents");
    return metrics;
}

bool SessionCompletedWithWarnings(const fs::path& session_path) {
    const auto summary = ReadUtf8File(session_path / L"session-summary.json");
    if (!summary) return false;
    Fields fields;
    std::string parse_error;
    return ParseFlatJson(*summary, fields, &parse_error) &&
        GetString(fields, "Status") == "CompletedWithWarnings";
}

std::uint64_t VerifiedGameplayClassificationCount(const AnalysisStatistics& statistics) {
    return statistics.movement + statistics.mount + statistics.quest + statistics.skill +
           statistics.consumable + statistics.entity + statistics.character_lifecycle +
           statistics.party + statistics.economy + statistics.progression + statistics.formula;
}

std::string ClassificationStatus(const AnalysisStatistics& statistics,
                                 const CandidateClassificationMetrics& candidates) {
    const auto verified = VerifiedGameplayClassificationCount(statistics);
    if (verified > 0 && candidates.semantic_candidates > 0) return "VerifiedAndCandidate";
    if (verified > 0) return "Verified";
    if (candidates.semantic_candidates > 0) return "CandidateOnly";
    if (statistics.frames > 0 || statistics.unknown > 0) return "UnknownOnly";
    return "NoEvidence";
}

struct InjectedTransportEvidenceSummary {
    std::uint64_t records = 0;
    std::uint64_t client_to_server = 0;
    std::uint64_t server_to_client = 0;
    std::uint64_t send_calls = 0;
    std::uint64_t recv_calls = 0;
    std::uint64_t post_decrypt = 0;
    std::uint64_t pre_encrypt = 0;
    std::uint64_t handler_decoded = 0;
    std::uint64_t frame208_candidates = 0;
    std::uint64_t payload_bytes = 0;
};

bool WriteInjectedTransportEvidenceReport(const fs::path& session_path,
                                          const fs::path& injected_packets,
                                          std::string* error) {
    std::ifstream stream(injected_packets, std::ios::binary);
    if (!stream) {
        if (error) *error = "cannot open injected transport evidence";
        return false;
    }
    InjectedTransportEvidenceSummary summary;
    std::string line;
    while (std::getline(stream, line)) {
        if (line.empty()) continue;
        Fields fields;
        std::string parse_error;
        if (!ParseFlatJson(line, fields, &parse_error)) {
            if (error) *error = "cannot parse injected transport evidence: " + parse_error;
            return false;
        }
        ++summary.records;
        const auto direction = GetString(fields, "PacketDirection", GetString(fields, "direction"));
        if (direction == "ClientToServer") ++summary.client_to_server;
        else if (direction == "ServerToClient") ++summary.server_to_client;
        const auto api = GetString(fields, "Api");
        if (api == "send" || api == "WSASend") ++summary.send_calls;
        else if (api == "recv" || api == "WSARecv") ++summary.recv_calls;
        else if (api == "PostDecrypt") ++summary.post_decrypt;
        else if (api == "PreEncrypt") ++summary.pre_encrypt;
        else if (api == "HandlerDecoded") ++summary.handler_decoded;
        if (GetString(fields, "frame208") == "true") ++summary.frame208_candidates;
        summary.payload_bytes += GetString(fields, "PayloadHex").size() / 2;
    }
    std::uint64_t file_bytes = 0;
    std::error_code size_error;
    if (fs::exists(injected_packets, size_error)) file_bytes = fs::file_size(injected_packets, size_error);
    const auto report = MakeJsonObject({
        {"Status", summary.records == 0 ? "NotObserved" : "Observed"},
        {"EvidenceLevel", summary.records == 0 ? "NotObserved" : "Transmitted"},
        {"Reason", "x86 DLL enhanced capture produced transport plus guarded post-decrypt, pre-encrypt, and handler-level evidence"},
        {"Decision", "Plaintext records enter protocol handlers; unresolved meanings remain Candidate and are blocked from authoritative runtime mutation"},
        {"TargetExecutable", "God2_opt.exe"},
        {"TargetArchitecture", "x86"},
        {"CaptureSource", "OptInX86Dll"},
        {"Transport", "InjectedWinsock"},
        {"Records", std::to_string(summary.records)},
        {"ClientToServer", std::to_string(summary.client_to_server)},
        {"ServerToClient", std::to_string(summary.server_to_client)},
        {"SendCalls", std::to_string(summary.send_calls)},
        {"RecvCalls", std::to_string(summary.recv_calls)},
        {"PostDecryptRecords", std::to_string(summary.post_decrypt)},
        {"PreEncryptRecords", std::to_string(summary.pre_encrypt)},
        {"HandlerDecodedRecords", std::to_string(summary.handler_decoded)},
        {"Frame208Candidates", std::to_string(summary.frame208_candidates)},
        {"PayloadBytes", std::to_string(summary.payload_bytes)},
        {"InjectedPacketsPath", WideToUtf8(injected_packets.wstring())},
        {"InjectedPacketsBytes", std::to_string(file_bytes)}
    }, {"Records","ClientToServer","ServerToClient","SendCalls","RecvCalls","PostDecryptRecords","PreEncryptRecords","HandlerDecodedRecords","Frame208Candidates","PayloadBytes","InjectedPacketsBytes"});
    if (!WriteUtf8FileAtomic(session_path / L"reports" / L"injected-transport-evidence.json", report + "\n")) {
        if (error) *error = "could not write injected transport evidence report";
        return false;
    }
    return true;
}

std::optional<std::string> ReadUtf8Tail(const fs::path& path, std::size_t maximum_bytes) {
    std::ifstream stream(path, std::ios::binary);
    if (!stream) return std::nullopt;
    stream.seekg(0, std::ios::end);
    const auto end = stream.tellg();
    if (end < 0) return std::nullopt;
    const auto available = static_cast<std::uint64_t>(end);
    const auto wanted = static_cast<std::size_t>(std::min<std::uint64_t>(available, maximum_bytes));
    stream.seekg(static_cast<std::streamoff>(available - wanted), std::ios::beg);
    std::string result(wanted, '\0');
    if (wanted != 0) {
        stream.read(result.data(), static_cast<std::streamsize>(wanted));
        result.resize(static_cast<std::size_t>(stream.gcount()));
    }
    return result;
}

std::uint64_t WorkingSetBytes() {
    PROCESS_MEMORY_COUNTERS counters{};
    return GetProcessMemoryInfo(GetCurrentProcess(), &counters, sizeof(counters)) ?
        static_cast<std::uint64_t>(counters.WorkingSetSize) : 0;
}

class CaptureSession {
public:
    bool Create(std::string* error) {
        const auto local = LocalDataRoot();
        if (local.empty()) {
            if (error) *error = "LocalAppData is unavailable";
            return false;
        }
        const fs::path capture_root = local / L"Captures";
        DWORD directory_error = ERROR_SUCCESS;
        if (!EnsureDirectoryTree(capture_root, &directory_error)) {
            if (error) *error = "capture root could not be created: Win32 " + std::to_string(directory_error);
            return false;
        }
        path_ = capture_root / (LocalTimestamp(true) + L"_" + Utf8ToWide(NewId()));
        if (!IsApprovedSessionPath(path_)) {
            if (error) *error = "capture session path is outside the approved data root";
            return false;
        }
        SessionStore store(path_);
        if (!store.Initialize(error)) return false;
        session_id_ = store.SessionId();
        return true;
    }

    const fs::path& Path() const { return path_; }
    const std::string& SessionId() const { return session_id_; }

private:
    fs::path path_;
    std::string session_id_;
};

bool SaveSessionExecutablePaths(const fs::path& session_path, const fs::path& launcher,
                                 const fs::path& client, AccelerationMode acceleration_mode,
                                 std::string* error) {
    const auto manifest_path = session_path / L"session.json";
    const auto content = ReadUtf8File(manifest_path);
    Fields fields;
    if (!content || !ParseFlatJson(*content, fields, error)) return false;
    fields["LauncherExecutablePath"] = WideToUtf8(launcher.wstring());
    fields["ClientExecutablePath"] = WideToUtf8(client.wstring());
    fields["IdentityCapturedAtUtc"] = UtcNow();
    fields["AccelerationMode"] = ToString(acceleration_mode);
    std::vector<std::pair<std::string, std::string>> values(fields.begin(), fields.end());
    std::sort(values.begin(), values.end(), [](const auto& left, const auto& right) {
        return left.first < right.first;
    });
    if (!WriteUtf8FileAtomic(manifest_path, MakeJsonObject(values, {"SchemaVersion"}) + "\n")) {
        if (error) *error = "cannot persist launcher/client identity paths";
        return false;
    }
    return true;
}

class CaptureBackendManager {
public:
    BackendSelection Probe() const { return SelectCaptureBackend(); }
};

struct GameProcessDetectionResult {
    GameProcessState state = GameProcessState::NotProbed;
    DWORD process_id = 0;
    DWORD win32_error = ERROR_SUCCESS;
    fs::path actual_path;
    bool is_x86 = false;
    DWORD parent_process_id = 0;
    FILETIME process_creation_time{};
    HWND game_window = nullptr;
};

class GameProcessTracker {
public:
    static bool ResolveClientPaths(const fs::path& selected, fs::path* launcher, fs::path* game,
                                   std::string* error = nullptr) {
        std::error_code file_error;
        if (_wcsicmp(selected.filename().c_str(), L"Launcher.exe") != 0 ||
            !fs::is_regular_file(selected, file_error) || file_error) {
            if (error) *error = "the selected client entry must be an existing Launcher.exe";
            return false;
        }
        const fs::path game_executable = selected.parent_path() / L"God2_opt.exe";
        file_error.clear();
        if (!fs::is_regular_file(game_executable, file_error) || file_error) {
            if (error) *error = "God2_opt.exe was not found beside Launcher.exe";
            return false;
        }
        DWORD binary_type = 0;
        if (!GetBinaryTypeW(game_executable.c_str(), &binary_type) || binary_type != SCS_32BIT_BINARY) {
            if (error) *error = "God2_opt.exe must be a valid 32-bit Windows executable";
            return false;
        }
        if (launcher) *launcher = selected;
        if (game) *game = game_executable;
        return true;
    }

    static DWORD Find(const fs::path& executable) {
        std::error_code target_error;
        const auto target_path = fs::weakly_canonical(executable, target_error);
        if (target_error) return 0;
        const auto target = target_path.wstring();
        HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == INVALID_HANDLE_VALUE) return 0;
        PROCESSENTRY32W entry{};
        entry.dwSize = sizeof(entry);
        DWORD result = 0;
        if (Process32FirstW(snapshot, &entry)) {
            do {
                HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, entry.th32ProcessID);
                if (process == nullptr) continue;
                std::vector<wchar_t> image(32'768);
                DWORD length = static_cast<DWORD>(image.size());
                if (QueryFullProcessImageNameW(process, 0, image.data(), &length)) {
                    std::error_code error;
                    const auto actual = fs::weakly_canonical(fs::path(std::wstring(image.data(), length)), error).wstring();
                    if (!error && _wcsicmp(actual.c_str(), target.c_str()) == 0) result = entry.th32ProcessID;
                }
                CloseHandle(process);
            } while (result == 0 && Process32NextW(snapshot, &entry));
        }
        CloseHandle(snapshot);
        return result;
    }

    static bool Is32BitProcess(DWORD process_id, std::string* error = nullptr) {
        UniqueHandle process(OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, process_id));
        if (process.Get() == nullptr) {
            if (error) *error = "cannot inspect God2_opt.exe process architecture";
            return false;
        }
        using IsWow64Process2Function = BOOL (WINAPI*)(HANDLE, USHORT*, USHORT*);
        const HMODULE kernel = GetModuleHandleW(L"kernel32.dll");
        const auto is_wow64_process_2 = kernel == nullptr ? nullptr :
            reinterpret_cast<IsWow64Process2Function>(GetProcAddress(kernel, "IsWow64Process2"));
        if (is_wow64_process_2 != nullptr) {
            USHORT process_machine = IMAGE_FILE_MACHINE_UNKNOWN;
            USHORT native_machine = IMAGE_FILE_MACHINE_UNKNOWN;
            if (!is_wow64_process_2(process.Get(), &process_machine, &native_machine)) {
                if (error) *error = "IsWow64Process2 could not inspect God2_opt.exe";
                return false;
            }
            return process_machine == IMAGE_FILE_MACHINE_I386 ||
                   (process_machine == IMAGE_FILE_MACHINE_UNKNOWN && native_machine == IMAGE_FILE_MACHINE_I386);
        }
        BOOL wow64 = FALSE;
        if (!IsWow64Process(process.Get(), &wow64)) {
            if (error) *error = "IsWow64Process could not inspect God2_opt.exe";
            return false;
        }
#if defined(_WIN64)
        return wow64 != FALSE;
#else
        return true;
#endif
    }

    static GameProcessDetectionResult Detect(const fs::path& executable,
                                              const fs::path& session_path = {}) {
        GameProcessDetectionResult best;
        best.state = GameProcessState::Searching;
        std::error_code target_error;
        auto target = fs::weakly_canonical(executable, target_error).wstring();
        if (target_error) {
            best.state = GameProcessState::QueryFailed;
            best.win32_error = static_cast<DWORD>(target_error.value());
            return best;
        }

        struct WindowSearch {
            std::vector<std::pair<DWORD, HWND>> matches;
        } windows;
        EnumWindows([](HWND window, LPARAM parameter) -> BOOL {
            if (!IsWindowVisible(window)) return TRUE;
            wchar_t title[512]{};
            if (GetWindowTextW(window, title, static_cast<int>(std::size(title))) <= 0) return TRUE;
            if (wcsstr(title, L"XianJieZhuan") == nullptr && wcsstr(title, L"Build -") == nullptr) return TRUE;
            DWORD process_id = 0;
            GetWindowThreadProcessId(window, &process_id);
            if (process_id != 0)
                reinterpret_cast<WindowSearch*>(parameter)->matches.emplace_back(process_id, window);
            return TRUE;
        }, reinterpret_cast<LPARAM>(&windows));

        HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == INVALID_HANDLE_VALUE) {
            best.state = windows.matches.empty() ? GameProcessState::QueryFailed : GameProcessState::WindowDetected;
            best.win32_error = GetLastError();
            if (!windows.matches.empty()) {
                best.process_id = windows.matches.front().first;
                best.game_window = windows.matches.front().second;
            }
            return best;
        }
        PROCESSENTRY32W entry{};
        entry.dwSize = sizeof(entry);
        if (Process32FirstW(snapshot, &entry)) {
            do {
                const auto window = std::find_if(windows.matches.begin(), windows.matches.end(),
                    [&](const auto& candidate) { return candidate.first == entry.th32ProcessID; });
                const bool name_matches = _wcsicmp(entry.szExeFile, L"God2_opt.exe") == 0;
                if (!name_matches && window == windows.matches.end()) continue;
                GameProcessDetectionResult candidate;
                candidate.state = GameProcessState::ProcessDetected;
                candidate.process_id = entry.th32ProcessID;
                candidate.parent_process_id = entry.th32ParentProcessID;
                candidate.game_window = window == windows.matches.end() ? nullptr : window->second;
                UniqueHandle process(OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, entry.th32ProcessID));
                if (process.Get() == nullptr) {
                    candidate.win32_error = GetLastError();
                    candidate.state = candidate.win32_error == ERROR_ACCESS_DENIED ?
                        GameProcessState::AccessDenied :
                        (candidate.game_window != nullptr ? GameProcessState::WindowDetected : GameProcessState::QueryFailed);
                    AppendCandidate(session_path, candidate);
                    if (best.state == GameProcessState::Searching || candidate.state == GameProcessState::AccessDenied)
                        best = candidate;
                    continue;
                }
                FILETIME exited{}, kernel{}, user{};
                if (!GetProcessTimes(process.Get(), &candidate.process_creation_time, &exited, &kernel, &user))
                    candidate.win32_error = GetLastError();
                std::vector<wchar_t> image(32'768);
                DWORD length = static_cast<DWORD>(image.size());
                if (!QueryFullProcessImageNameW(process.Get(), 0, image.data(), &length)) {
                    candidate.win32_error = GetLastError();
                    candidate.state = candidate.game_window != nullptr ?
                        GameProcessState::WindowDetected : GameProcessState::QueryFailed;
                    AppendCandidate(session_path, candidate);
                    best = candidate;
                    continue;
                }
                candidate.actual_path = fs::path(std::wstring(image.data(), length));
                std::error_code actual_error;
                auto actual = fs::weakly_canonical(candidate.actual_path, actual_error).wstring();
                if (actual_error || _wcsicmp(actual.c_str(), target.c_str()) != 0) {
                    candidate.state = GameProcessState::PathMismatch;
                    candidate.win32_error = actual_error ? static_cast<DWORD>(actual_error.value()) : ERROR_BAD_PATHNAME;
                    AppendCandidate(session_path, candidate);
                    if (best.state == GameProcessState::Searching) best = candidate;
                    continue;
                }
                candidate.is_x86 = Is32BitProcessHandle(process.Get());
                if (!candidate.is_x86) {
                    candidate.state = GameProcessState::ArchitectureMismatch;
                    candidate.win32_error = ERROR_BAD_EXE_FORMAT;
                    AppendCandidate(session_path, candidate);
                    best = candidate;
                    continue;
                }
                candidate.state = GameProcessState::Validated;
                candidate.win32_error = ERROR_SUCCESS;
                AppendCandidate(session_path, candidate);
                CloseHandle(snapshot);
                return candidate;
            } while (Process32NextW(snapshot, &entry));
        } else {
            best.state = GameProcessState::QueryFailed;
            best.win32_error = GetLastError();
        }
        CloseHandle(snapshot);
        if (best.state == GameProcessState::Searching && !windows.matches.empty()) {
            best.state = GameProcessState::WindowDetected;
            best.process_id = windows.matches.front().first;
            best.game_window = windows.matches.front().second;
        }
        return best;
    }

    static bool EnsureLauncherRunning(const fs::path& launcher_path, std::string* error) {
        if (Find(launcher_path) != 0) return true;
        SHELLEXECUTEINFOW launch{};
        launch.cbSize = sizeof(launch);
        launch.fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_FLAG_NO_UI;
        launch.lpVerb = L"open";
        launch.lpFile = launcher_path.c_str();
        launch.lpDirectory = launcher_path.parent_path().c_str();
        launch.nShow = SW_SHOWNORMAL;
        if (!ShellExecuteExW(&launch)) {
            const DWORD launch_error = GetLastError();
            if (error) *error = "could not start Launcher.exe: " + WideToUtf8(ErrorText(launch_error));
            return false;
        }
        if (launch.hProcess != nullptr) CloseHandle(launch.hProcess);
        return true;
    }

private:
    static bool Is32BitProcessHandle(HANDLE process) {
        using IsWow64Process2Function = BOOL (WINAPI*)(HANDLE, USHORT*, USHORT*);
        const HMODULE kernel = GetModuleHandleW(L"kernel32.dll");
        const auto is_wow64_process_2 = kernel == nullptr ? nullptr :
            reinterpret_cast<IsWow64Process2Function>(GetProcAddress(kernel, "IsWow64Process2"));
        if (is_wow64_process_2 != nullptr) {
            USHORT process_machine = IMAGE_FILE_MACHINE_UNKNOWN;
            USHORT native_machine = IMAGE_FILE_MACHINE_UNKNOWN;
            return is_wow64_process_2(process, &process_machine, &native_machine) != FALSE &&
                (process_machine == IMAGE_FILE_MACHINE_I386 ||
                 (process_machine == IMAGE_FILE_MACHINE_UNKNOWN && native_machine == IMAGE_FILE_MACHINE_I386));
        }
        BOOL wow64 = FALSE;
        if (!IsWow64Process(process, &wow64)) return false;
#if defined(_WIN64)
        return wow64 != FALSE;
#else
        return true;
#endif
    }

    static std::uint64_t FileTimeTicks(const FILETIME& value) {
        return (static_cast<std::uint64_t>(value.dwHighDateTime) << 32) | value.dwLowDateTime;
    }

    static void AppendCandidate(const fs::path& session_path,
                                const GameProcessDetectionResult& candidate) noexcept {
        if (session_path.empty()) return;
        try {
            const auto line = MakeJsonObject({
                {"ObservedAtUtc", UtcNow()}, {"CandidateExecutable", "God2_opt.exe"},
                {"ProcessId", std::to_string(candidate.process_id)},
                {"OpenOrQueryWin32Error", std::to_string(candidate.win32_error)},
                {"FullPath", WideToUtf8(candidate.actual_path.wstring())},
                {"X86", candidate.is_x86 ? "true" : "false"},
                {"ParentProcessId", std::to_string(candidate.parent_process_id)},
                {"CreationTime", std::to_string(FileTimeTicks(candidate.process_creation_time))},
                {"WindowHandle", std::to_string(reinterpret_cast<std::uintptr_t>(candidate.game_window))},
                {"State", std::to_string(static_cast<int>(candidate.state))}
            }, {"ProcessId", "OpenOrQueryWin32Error", "ParentProcessId", "CreationTime", "WindowHandle", "State"});
            WriteUtf8File(session_path / L"reports" / L"game-process-candidates.jsonl", line + "\n", true);
        } catch (...) {
        }
    }
};

class ExportController {
public:
    static bool Export(SessionStore& store, std::string* error) { return store.ExportAll(error); }
};

class AnalysisController {
public:
    ~AnalysisController() {
        AbortNoThrow();
    }

    bool Start(const fs::path& session_path, const BackendCapabilities& capabilities, std::string* error) {
        session_path_ = session_path;
        live_active_.store(false);
        {
            std::lock_guard lock(target_process_mutex_);
            verified_process_ids_.clear();
            last_verified_process_id_ = 0;
        }
        if (capabilities.analysis_mode != AnalysisMode::NearRealtime) return true;
        live_store_ = std::make_unique<SessionStore>(session_path);
        if (!live_store_->Initialize(error) || !live_store_->BeginAnalysis("GuiNearRealtime", error)) {
            live_store_.reset();
            return false;
        }
        live_engine_ = std::make_unique<GameplayAnalysisEngine>(*live_store_);
        live_inputs_.clear();
        live_inputs_.push_back({session_path / L"raw" / L"etw-events.jsonl"});
        live_inputs_.push_back({session_path / L"raw" / L"injected-packets.jsonl"});
        live_error_.clear();
        live_success_.store(true);
        stop_requested_.store(false);
        live_active_.store(true);
        try {
            live_thread_ = std::thread([this] {
                try {
                    RunNearRealtime();
                } catch (const std::exception& exception) {
                    live_error_ = "near-realtime analysis exception: " + std::string(exception.what());
                    live_success_.store(false);
                } catch (...) {
                    live_error_ = "near-realtime analysis raised an unknown exception";
                    live_success_.store(false);
                }
            });
        } catch (const std::exception& exception) {
            live_active_.store(false);
            if (error != nullptr) *error = "cannot start near-realtime analysis thread: " + std::string(exception.what());
            live_engine_.reset();
            live_store_.reset();
            return false;
        }
        return true;
    }

    bool NearRealtimeFailed() const noexcept {
        return live_active_.load() && !live_success_.load();
    }

    void SetTargetProcess(DWORD process_id, bool verified_32_bit) noexcept {
        const DWORD verified_process_id = verified_32_bit ? process_id : 0;
        DWORD reported_process_id = 0;
        bool has_verified_history = false;
        std::string verified_process_ids;
        {
            std::lock_guard lock(target_process_mutex_);
            if (verified_process_id != 0) {
                verified_process_ids_.insert(verified_process_id);
                last_verified_process_id_ = verified_process_id;
            }
            reported_process_id = verified_process_id != 0 ? verified_process_id : last_verified_process_id_;
            has_verified_history = reported_process_id != 0;
            for (const DWORD recorded_process_id : verified_process_ids_) {
                if (!verified_process_ids.empty()) verified_process_ids += ",";
                verified_process_ids += std::to_string(recorded_process_id);
            }
        }
        if (session_path_.empty()) return;
        const auto report = MakeJsonObject({
            {"UpdatedAtUtc", UtcNow()},
            {"TargetExecutable", "God2_opt.exe"},
            {"TargetProcessId", std::to_string(reported_process_id)},
            {"CurrentProcessId", std::to_string(process_id)},
            {"CurrentProcessVerified", verified_32_bit ? "true" : "false"},
            {"VerifiedProcessIds", verified_process_ids},
            {"TargetArchitecture", has_verified_history ? "x86" : "RejectedOrWaiting"},
            {"TargetProcessStatus", verified_32_bit ? "CurrentVerified" :
                (has_verified_history ? "VerifiedHistoryRetained" : "WaitingForVerifiedX86Process")},
            {"RawCaptureScope", "EvidenceBlockedPrivacyPolicy"},
            {"AnalysisScope", "ExactTargetProcessIdOnly"},
            {"UnattributedEvents", "ExcludedFromGameplayAnalysis"}
        }, {"TargetProcessId", "CurrentProcessId"});
        WriteUtf8FileAtomic(session_path_ / L"reports" / L"target-process-scope.json", report + "\n");
    }

    void AbortNoThrow() noexcept {
        try {
            stop_requested_.store(true);
            if (live_thread_.joinable()) live_thread_.join();
            live_active_.store(false);
            live_engine_.reset();
            live_store_.reset();
            live_inputs_.clear();
        } catch (...) {
        }
    }

    bool Finalize(const fs::path& session_path, bool force_post_capture, std::string* error) {
        const auto raw_etl = session_path / L"raw" / L"capture.etl";
        if (fs::exists(raw_etl)) {
            std::string audit_error;
            const auto audit = AuditEtwFile(raw_etl,
                session_path / L"reports" / L"raw-etl-inventory.json", &audit_error);
            if (!audit.success) {
                AppendStartupLog(L"OfflineEtlAuditWarning", Utf8ToWide(audit_error));
            }
        }
        if (force_post_capture && live_store_ != nullptr) {
            stop_requested_.store(true);
            if (live_thread_.joinable()) live_thread_.join();
            live_active_.store(false);
            std::string transition_error;
            const bool transition_saved = live_store_->FinishAnalysis("PostCaptureFallback", &transition_error);
            live_engine_.reset();
            live_store_.reset();
            live_inputs_.clear();
            const bool fallback_finished = FinalizePostCapture(session_path, error);
            if (!transition_saved) {
                if (error != nullptr) {
                    if (!error->empty()) *error += "; ";
                    *error += "could not close the interrupted near-realtime analysis: " + transition_error;
                }
                return false;
            }
            return fallback_finished;
        }
        if (live_store_ != nullptr) {
            stop_requested_.store(true);
            if (live_thread_.joinable()) live_thread_.join();
            live_active_.store(false);
            for (const auto& input : live_inputs_) {
                if (!input.pending_line.empty()) {
                    live_error_ = "near-realtime input ended with an incomplete JSON line";
                    live_success_.store(false);
                    break;
                }
            }
            if (!live_success_.load()) {
                const std::string near_realtime_error = live_error_.empty() ?
                    "near-realtime analysis stopped unexpectedly" : live_error_;
                std::string transition_error;
                const bool transition_saved = live_store_->FinishAnalysis("PostCaptureFallback", &transition_error);
                live_engine_.reset();
                live_store_.reset();
                live_inputs_.clear();
                AppendStartupLog(L"NearRealtimeAnalysisFallback", Utf8ToWide(near_realtime_error));
                const bool fallback_finished = FinalizePostCapture(session_path, error);
                if (!transition_saved) {
                    if (error != nullptr) {
                        if (!error->empty()) *error += "; ";
                        *error += "could not close the interrupted near-realtime analysis: " + transition_error;
                    }
                    return false;
                }
                return fallback_finished;
            }
            bool success = live_success_.load();
            if (!live_store_->FinishAnalysis(success ? "Completed" : "Failed", &live_error_)) success = false;
            if (success && !ExportController::Export(*live_store_, &live_error_)) success = false;
            if (!WriteSummary(session_path, live_engine_->Statistics(), success, live_store_->AnalysisRunId(),
                              "NearRealtime")) {
                live_error_ = "could not atomically write the session summary";
                success = false;
            }
            if (!success && error != nullptr) *error = live_error_;
            live_engine_.reset();
            live_store_.reset();
            return success;
        }
        return FinalizePostCapture(session_path, error);
    }

private:
    static bool WriteSummary(const fs::path& session_path, const AnalysisStatistics& statistics,
                             bool success, std::string_view analysis_run_id, std::string_view mode) {
        const auto candidates = ReadCandidateClassificationMetrics(session_path);
        const auto raw_etl = ReadRawEtlMetrics(session_path);
        const bool raw_only = statistics.frames == 0 && raw_etl.ndis_packet_events > 0;
        const bool warnings = success && raw_etl.report_present && (!raw_etl.audit_success || raw_only);
        const std::string classification_status = raw_only ? "EvidenceBlockedRawOnly" :
            ClassificationStatus(statistics, candidates);
        Fields transport_fields;
        if (const auto report = ReadUtf8File(session_path / L"reports" / L"injected-transport-evidence.json"))
            ParseFlatJson(*report, transport_fields, nullptr);
        const auto transport_chunks = GetInt64(transport_fields, "SendCalls").value_or(0) +
            GetInt64(transport_fields, "RecvCalls").value_or(0);
        const auto decoded_messages = GetInt64(transport_fields, "PreEncryptRecords").value_or(0) +
            GetInt64(transport_fields, "PostDecryptRecords").value_or(0);
        const auto handler_observations = GetInt64(transport_fields, "HandlerDecodedRecords").value_or(0);
        const auto summary = MakeJsonObject({
            {"CompletedAtUtc", UtcNow()}, {"Status", success ?
                (warnings ? "CompletedWithWarnings" : "Completed") : "Failed"},
            {"AnalysisMode", std::string(mode)}, {"AnalysisRunId", std::string(analysis_run_id)},
            {"CaptureRecords", std::to_string(statistics.frames)},
            {"TransportChunks", std::to_string(transport_chunks)}, {"ProtocolFrames", "0"},
            {"DecodedMessages", std::to_string(decoded_messages)},
            {"HandlerObservations", std::to_string(handler_observations)},
            {"RawEtlTotalEvents", std::to_string(raw_etl.total_events)},
            {"RawEtlPacketEvents", std::to_string(raw_etl.ndis_packet_events)},
            {"VerifiedGameplayClassifications", std::to_string(VerifiedGameplayClassificationCount(statistics))},
            {"CandidateFrames", std::to_string(candidates.candidate_frames)},
            {"OpcodeSemanticCandidates", std::to_string(candidates.semantic_candidates)},
            {"HighConfidenceOpcodeSemanticCandidates", std::to_string(candidates.high_confidence_semantic_candidates)},
            {"ClassificationStatus", classification_status},
            {"Movement", std::to_string(statistics.movement)}, {"Mount", std::to_string(statistics.mount)},
            {"Quest", std::to_string(statistics.quest)}, {"Skill", std::to_string(statistics.skill)},
            {"Consumable", std::to_string(statistics.consumable)}, {"Entity", std::to_string(statistics.entity)},
            {"CharacterLifecycle", std::to_string(statistics.character_lifecycle)},
            {"Party", std::to_string(statistics.party)}, {"Economy", std::to_string(statistics.economy)},
            {"Progression", std::to_string(statistics.progression)},
            {"Formula", std::to_string(statistics.formula)},
            {"ProtocolDecoded", std::to_string(statistics.protocol_decoded)},
            {"Unknown", std::to_string(statistics.unknown)}
        }, {"CaptureRecords", "TransportChunks", "ProtocolFrames", "DecodedMessages", "HandlerObservations", "RawEtlTotalEvents", "RawEtlPacketEvents", "VerifiedGameplayClassifications", "CandidateFrames", "OpcodeSemanticCandidates",
            "HighConfidenceOpcodeSemanticCandidates", "Movement", "Mount", "Quest", "Skill", "Consumable", "Entity",
            "CharacterLifecycle", "Party", "Economy", "Progression", "Formula", "ProtocolDecoded", "Unknown"});
        return WriteUtf8FileAtomic(session_path / L"session-summary.json", summary + "\n");
    }

    bool WriteRawEtlFallbackReport(const fs::path& session_path, std::uint64_t etw_event_lines,
                                   std::string* error) const {
        const auto etl = session_path / L"raw" / L"capture.etl";
        std::error_code size_error;
        const auto etl_bytes = fs::exists(etl, size_error) ? fs::file_size(etl, size_error) : 0;
        if (size_error || etl_bytes == 0) return true;
        const auto raw_etl = ReadRawEtlMetrics(session_path);
        const auto report = MakeJsonObject({
            {"Status", "EvidenceBlocked"},
            {"Reason", "An untrusted raw ETL artifact was found; production policy prohibits retaining or packaging it"},
            {"Decision", "The artifact is excluded from gameplay analysis and must be removed by the fail-closed privacy gate before packaging"},
            {"RecommendedNextCapture", "Enable x86 DLL enhanced capture to collect process-scoped Winsock send/recv payloads"},
            {"TargetExecutable", "God2_opt.exe"},
            {"TargetArchitecture", HasVerifiedProcessIds() ? "x86" : "RejectedOrWaiting"},
            {"VerifiedProcessIds", VerifiedProcessIdsCsv()},
            {"RawEtlPath", WideToUtf8(etl.wstring())},
            {"RawEtlBytes", std::to_string(etl_bytes)},
            {"RawEtlTotalEvents", std::to_string(raw_etl.total_events)},
            {"RawEtlPacketEvents", std::to_string(raw_etl.ndis_packet_events)},
            {"EtwEventLines", std::to_string(etw_event_lines)}
        }, {"RawEtlBytes", "RawEtlTotalEvents", "RawEtlPacketEvents", "EtwEventLines"});
        if (!WriteUtf8FileAtomic(session_path / L"reports" / L"raw-etl-evidence-blocked.json",
                                 report + "\n")) {
            if (error) *error = "could not write the untrusted raw-artifact scope report";
            return false;
        }
        return true;
    }

    struct LiveInputState {
        fs::path path;
        std::uint64_t offset = 0;
        std::string pending_line;
    };

    bool ReadAvailableLinesFrom(LiveInputState& input) {
        std::error_code size_error;
        const auto size = fs::file_size(input.path, size_error);
        if (size_error) {
            if (size_error == std::errc::no_such_file_or_directory) return true;
            live_error_ = "cannot inspect near-realtime input: " + size_error.message();
            return false;
        }
        if (size < input.offset) {
            live_error_ = "near-realtime input was truncated during capture";
            return false;
        }
        if (size == input.offset) return true;
        UniqueHandle stream;
        DWORD open_error = ERROR_SUCCESS;
        const std::wstring input_path = Win32ExtendedPathForFileIo(input.path);
        for (int attempt = 0; attempt != 5; ++attempt) {
            stream = UniqueHandle(CreateFileW(input_path.c_str(), GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL | FILE_FLAG_SEQUENTIAL_SCAN, nullptr));
            if (stream.Get() != INVALID_HANDLE_VALUE) break;
            open_error = GetLastError();
            if (open_error != ERROR_SHARING_VIOLATION && open_error != ERROR_LOCK_VIOLATION) break;
            Sleep(20);
        }
        if (stream.Get() == INVALID_HANDLE_VALUE) {
            live_error_ = "cannot open near-realtime input '" + WideToUtf8(input.path.wstring()) +
                "' (Win32=" + std::to_string(open_error) + ": " + WideToUtf8(ErrorText(open_error)) + ")";
            return false;
        }
        LARGE_INTEGER seek_position{};
        seek_position.QuadPart = static_cast<LONGLONG>(input.offset);
        if (!SetFilePointerEx(stream.Get(), seek_position, nullptr, FILE_BEGIN)) {
            const DWORD seek_error = GetLastError();
            live_error_ = "cannot seek near-realtime input '" + WideToUtf8(input.path.wstring()) +
                "' (Win32=" + std::to_string(seek_error) + ")";
            return false;
        }
        std::vector<char> chunk(64 * 1024);
        while (input.offset < size) {
            const DWORD wanted = static_cast<DWORD>(
                std::min<std::uintmax_t>(chunk.size(), size - input.offset));
            DWORD read = 0;
            if (!ReadFile(stream.Get(), chunk.data(), wanted, &read, nullptr)) {
                const DWORD read_error = GetLastError();
                live_error_ = "cannot read near-realtime input '" + WideToUtf8(input.path.wstring()) +
                    "' (Win32=" + std::to_string(read_error) + ")";
                return false;
            }
            if (read == 0) break;
            input.pending_line.append(chunk.data(), static_cast<std::size_t>(read));
            input.offset += read;
            std::size_t consumed = 0;
            for (;;) {
                const auto newline = input.pending_line.find('\n', consumed);
                if (newline == std::string::npos) break;
                std::string_view line(input.pending_line.data() + consumed, newline - consumed);
                bool process_line = false;
                if (!line.empty()) {
                    Fields fields;
                    std::string parse_error;
                    if (!ParseFlatJson(line, fields, &parse_error)) {
                        live_error_ = "cannot validate target PID for near-realtime event: " + parse_error;
                        return false;
                    }
                    const auto event_process_id = GetInt64(fields, "ProcessId").value_or(0);
                    process_line = IsVerifiedProcessId(event_process_id);
                }
                if (process_line && !live_engine_->ProcessJsonLine(line, true, &live_error_)) return false;
                consumed = newline + 1;
            }
            if (consumed != 0) input.pending_line.erase(0, consumed);
            if (input.pending_line.size() > kMaximumPendingBytes) {
                live_error_ = "near-realtime event exceeds the fixed analysis buffer";
                return false;
            }
        }
        if (input.offset != size) {
            live_error_ = "near-realtime input could not be read completely";
            return false;
        }
        return true;
    }

    bool ReadAvailableLines() {
        for (auto& input : live_inputs_)
            if (!ReadAvailableLinesFrom(input)) return false;
        return true;
    }

    void RunNearRealtime() {
        while (!stop_requested_.load()) {
            if (!ReadAvailableLines()) {
                live_success_.store(false);
                return;
            }
            Sleep(100);
        }
        if (!ReadAvailableLines()) live_success_.store(false);
    }

    bool FinalizePostCapture(const fs::path& session_path, std::string* error) {
        SessionStore store(session_path);
        if (!store.Initialize(error) || !store.BeginAnalysis("GuiCaptureStop", error)) return false;
        GameplayAnalysisEngine engine(store);
        bool success = true;
        std::vector<fs::path> captures;
        std::error_code enumerate_error;
        for (const auto& entry : fs::directory_iterator(session_path / L"raw", enumerate_error)) {
            if (entry.is_regular_file() && entry.path().extension() == L".pcapng") captures.push_back(entry.path());
        }
        if (enumerate_error) {
            if (error) *error = "cannot enumerate capture evidence: " + enumerate_error.message();
            success = false;
        }
        std::sort(captures.begin(), captures.end());
        if (!captures.empty()) {
            const auto scope_report = MakeJsonObject({
                {"Status", "EvidenceBlocked"},
                {"Reason", "PCAPNG does not carry a verified owning process id"},
                {"Decision", "The untrusted raw artifact is excluded from analysis and must be removed by the fail-closed privacy gate before packaging"},
                {"TargetExecutable", "God2_opt.exe"},
                {"TargetArchitecture", "x86"}
            });
            if (!WriteUtf8FileAtomic(session_path / L"reports" / L"process-scope-evidence-blocked.json",
                                     scope_report + "\n")) {
                if (error) *error = "could not write process-scope evidence report";
                success = false;
            }
        }
        const auto etw_events = session_path / L"raw" / L"etw-events.jsonl";
        std::uint64_t etw_event_lines = 0;
        if (success && fs::exists(etw_events)) {
            etw_event_lines = CountLines(etw_events);
            if (HasVerifiedProcessIds()) {
                success = AnalyzeProcessScopedJsonl(etw_events, engine, error);
            } else {
                const auto scope_report = MakeJsonObject({
                    {"Status", "EvidenceBlocked"},
                    {"Reason", "No verified God2_opt.exe process id was observed"},
                    {"Decision", "Raw ETW artifacts are excluded and removed by privacy policy; gameplay analysis uses sanitized semantic evidence only"},
                    {"TargetExecutable", "God2_opt.exe"},
                    {"TargetArchitecture", "x86"}
                });
                if (!WriteUtf8FileAtomic(session_path / L"reports" / L"etw-process-scope-evidence-blocked.json",
                                         scope_report + "\n")) {
                    if (error) *error = "could not write ETW process-scope evidence report";
                    success = false;
                }
            }
        }
        if (success && etw_event_lines == 0)
            success = WriteRawEtlFallbackReport(session_path, etw_event_lines, error);
        const auto injected_packets = session_path / L"raw" / L"injected-packets.jsonl";
        if (success && fs::exists(injected_packets))
            success = WriteInjectedTransportEvidenceReport(session_path, injected_packets, error);
        if (success && fs::exists(injected_packets))
            success = AnalyzeInjectedGod2FrameCandidates(session_path, injected_packets, error).success;
        if (success && fs::exists(injected_packets))
            success = engine.AnalyzeJsonl(injected_packets, true, error);
        if (!store.FinishAnalysis(success ? "Completed" : "Failed", error)) success = false;
        if (success && !ExportController::Export(store, error)) success = false;
        if (!WriteSummary(session_path, engine.Statistics(), success, store.AnalysisRunId(), "PostCapture")) {
            if (error) *error = "could not atomically write the session summary";
            success = false;
        }
        return success;
    }

    static constexpr std::size_t kMaximumPendingBytes = 1024 * 1024;

    bool IsVerifiedProcessId(std::int64_t process_id) const {
        if (process_id <= 0 || process_id > MAXDWORD) return false;
        std::lock_guard lock(target_process_mutex_);
        return verified_process_ids_.find(static_cast<DWORD>(process_id)) != verified_process_ids_.end();
    }

    bool HasVerifiedProcessIds() const {
        std::lock_guard lock(target_process_mutex_);
        return !verified_process_ids_.empty();
    }

    std::string VerifiedProcessIdsCsv() const {
        std::lock_guard lock(target_process_mutex_);
        std::string result;
        for (const DWORD process_id : verified_process_ids_) {
            if (!result.empty()) result += ",";
            result += std::to_string(process_id);
        }
        return result;
    }

    bool AnalyzeProcessScopedJsonl(const fs::path& path, GameplayAnalysisEngine& engine,
                                   std::string* error) const {
        std::ifstream stream(path, std::ios::binary);
        if (!stream) {
            if (error) *error = "cannot open process-scoped ETW evidence";
            return false;
        }
        std::string line;
        while (std::getline(stream, line)) {
            if (line.size() > kMaximumPendingBytes) {
                if (error) *error = "process-scoped ETW event exceeds the fixed analysis buffer";
                return false;
            }
            if (!line.empty() && line.back() == '\r') line.pop_back();
            if (line.empty()) continue;
            Fields fields;
            std::string parse_error;
            if (!ParseFlatJson(line, fields, &parse_error)) {
                if (error) *error = "cannot validate target PID for offline ETW event: " + parse_error;
                return false;
            }
            if (!IsVerifiedProcessId(GetInt64(fields, "ProcessId").value_or(0))) continue;
            if (!engine.ProcessJsonLine(line, true, error)) return false;
        }
        if (!stream.eof()) {
            if (error) *error = "could not read process-scoped ETW evidence completely";
            return false;
        }
        return true;
    }

    fs::path session_path_;
    std::vector<LiveInputState> live_inputs_;
    std::unique_ptr<SessionStore> live_store_;
    std::unique_ptr<GameplayAnalysisEngine> live_engine_;
    std::thread live_thread_;
    std::atomic<bool> stop_requested_{false};
    std::atomic<bool> live_success_{true};
    std::atomic<bool> live_active_{false};
    mutable std::mutex target_process_mutex_;
    std::set<DWORD> verified_process_ids_;
    DWORD last_verified_process_id_ = 0;
    std::string live_error_;
};

bool ExtractEmbeddedBinary(int resource_id, const fs::path& destination, std::string* error,
                           SECURITY_ATTRIBUTES* security = nullptr,
                           DWORD creation_disposition = CREATE_ALWAYS) {
    HRSRC resource = FindResourceW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(resource_id), RT_RCDATA);
    if (resource == nullptr) {
        if (error) *error = "embedded x86 capture payload is missing";
        return false;
    }
    HGLOBAL loaded = LoadResource(GetModuleHandleW(nullptr), resource);
    const DWORD size = SizeofResource(GetModuleHandleW(nullptr), resource);
    const void* data = loaded != nullptr ? LockResource(loaded) : nullptr;
    if (data == nullptr || size == 0) {
        if (error) *error = "embedded x86 capture payload could not be loaded";
        return false;
    }
    HANDLE file = CreateFileW(destination.c_str(), GENERIC_WRITE, 0, security, creation_disposition,
                              FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_TEMPORARY, nullptr);
    if (file == INVALID_HANDLE_VALUE) {
        if (error) *error = "x86 capture payload could not be extracted: " + WideToUtf8(ErrorText(GetLastError()));
        return false;
    }
    DWORD written = 0;
    const BOOL write_ok = WriteFile(file, data, size, &written, nullptr);
    const DWORD write_error = write_ok ? ERROR_SUCCESS : GetLastError();
    if (write_ok) FlushFileBuffers(file);
    CloseHandle(file);
    if (!write_ok || written != size) {
        if (error) *error = "x86 capture payload extraction was incomplete: " + WideToUtf8(ErrorText(write_error));
        return false;
    }
    return true;
}

bool EmbeddedBinaryMatchesFile(int resource_id, const fs::path& path, std::string* error) {
    HRSRC resource = FindResourceW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(resource_id), RT_RCDATA);
    if (resource == nullptr) {
        if (error) *error = "embedded x86 capture payload resource is missing during integrity verification";
        return false;
    }
    HGLOBAL loaded = LoadResource(GetModuleHandleW(nullptr), resource);
    const DWORD resource_size = SizeofResource(GetModuleHandleW(nullptr), resource);
    const auto* resource_bytes = static_cast<const unsigned char*>(
        loaded != nullptr ? LockResource(loaded) : nullptr);
    if (resource_bytes == nullptr || resource_size == 0) {
        if (error) *error = "embedded x86 capture payload resource could not be read during integrity verification";
        return false;
    }
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
                              FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) {
        if (error) *error = "extracted x86 capture payload is missing during integrity verification";
        return false;
    }
    LARGE_INTEGER file_size{};
    bool matches = GetFileSizeEx(file, &file_size) != FALSE &&
        file_size.QuadPart == static_cast<LONGLONG>(resource_size);
    std::vector<unsigned char> buffer(64 * 1024);
    std::size_t offset = 0;
    while (matches && offset < resource_size) {
        const DWORD requested = static_cast<DWORD>(
            std::min<std::size_t>(buffer.size(), resource_size - offset));
        DWORD read = 0;
        if (!ReadFile(file, buffer.data(), requested, &read, nullptr) || read != requested ||
            std::memcmp(buffer.data(), resource_bytes + offset, requested) != 0) {
            matches = false;
            break;
        }
        offset += read;
    }
    CloseHandle(file);
    if (!matches && error)
        *error = "extracted x86 capture payload does not exactly match the embedded product resource bytes";
    return matches;
}

bool IsX86PeFile(const fs::path& path) {
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
                              FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    IMAGE_DOS_HEADER dos{};
    DWORD read = 0;
    bool valid = ReadFile(file, &dos, sizeof(dos), &read, nullptr) && read == sizeof(dos) &&
                 dos.e_magic == IMAGE_DOS_SIGNATURE && dos.e_lfanew > 0;
    if (valid) {
        LARGE_INTEGER offset{};
        offset.QuadPart = dos.e_lfanew;
        valid = SetFilePointerEx(file, offset, nullptr, FILE_BEGIN) != FALSE;
    }
    DWORD signature = 0;
    IMAGE_FILE_HEADER header{};
    if (valid) valid = ReadFile(file, &signature, sizeof(signature), &read, nullptr) &&
                       read == sizeof(signature) && signature == IMAGE_NT_SIGNATURE;
    if (valid) valid = ReadFile(file, &header, sizeof(header), &read, nullptr) &&
                       read == sizeof(header) && header.Machine == IMAGE_FILE_MACHINE_I386;
    CloseHandle(file);
    return valid;
}

bool MatchesExactClientFileIdentity(const fs::path& path, std::string* observed_version = nullptr,
                                    std::optional<std::string>* observed_sha256 = nullptr) {
    const std::string version = FileVersion(path);
    const auto sha256 = CalculateFileSha256(path);
    if (observed_version != nullptr) *observed_version = version;
    if (observed_sha256 != nullptr) *observed_sha256 = sha256;
    return IsX86PeFile(path) && version == kExactClientVersion && sha256 && *sha256 == kExactClientSha256;
}

struct EnhancedInjectorResultV2 {
    std::string status;
    std::uint32_t code{};
    bool injection_attempted{};
    bool module_was_ever_loaded{};
    bool module_load_state_verified{};
    bool module_snapshot_verified{};
    bool module_absent{};
    bool target_process_exited{};
    bool target_identity_verified{};
    bool probe_ready{};
    bool stop_succeeded{};
    bool unload_safe{};
    bool module_unloaded{};
    bool module_resident_inactive{};
    bool strict_unload_verified{};
    bool cleanup_retriable{};
    std::string injection_mode;
    std::uint32_t suspended_thread_count{};
    std::uint32_t primary_thread_id{};
    bool extra_reference_requested{};
    bool extra_reference_loaded{};
    bool extra_reference_first_free_library_still_present{};
    bool extra_reference_not_claimed_unloaded{};
    bool extra_reference_released_then_module_absent{};
};

std::optional<EnhancedInjectorResultV2> ParseEnhancedInjectorResultV2(
    std::string_view wire) noexcept;
bool IsVerifiedEnhancedNoModuleLoadedResult(
    const EnhancedInjectorResultV2& result) noexcept;
bool IsVerifiedEnhancedPayloadStructureResult(
    const EnhancedInjectorResultV2& result) noexcept;

bool ValidateEnhancedCapturePayloadStructure(const fs::path& root, std::string* error) {
    std::error_code directory_error;
    fs::create_directories(root, directory_error);
    if (directory_error) {
        if (error) *error = "cannot create enhanced payload self-test directory: " + directory_error.message();
        return false;
    }
    const fs::path probe = root / L"God2PacketCaptureProbe.dll";
    const fs::path injector = root / L"God2PacketCaptureInjector.exe";
    if (!ExtractEmbeddedBinary(IDR_X86_PACKET_PROBE, probe, error) ||
        !ExtractEmbeddedBinary(IDR_X86_PACKET_INJECTOR, injector, error)) return false;
    if (!EmbeddedBinaryMatchesFile(IDR_X86_PACKET_PROBE, probe, error) ||
        !EmbeddedBinaryMatchesFile(IDR_X86_PACKET_INJECTOR, injector, error)) return false;
    if (!IsX86PeFile(probe) || !IsX86PeFile(injector)) {
        if (error) *error = "embedded enhanced capture payload is not entirely x86";
        return false;
    }
    const fs::path result_path = root / L"self-test-result.txt";
    std::wstring command = QuoteArgument(injector.wstring()) + L" --self-test --dll " +
        QuoteArgument(probe.wstring()) + L" --result " + QuoteArgument(result_path.wstring());
    std::vector<wchar_t> mutable_command(command.begin(), command.end());
    mutable_command.push_back(L'\0');
    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(injector.c_str(), mutable_command.data(), nullptr, nullptr, FALSE,
                        CREATE_NO_WINDOW, nullptr, root.c_str(), &startup, &process)) {
        if (error) *error = "x86 enhanced capture helper self-test could not start";
        return false;
    }
    CloseHandle(process.hThread);
    const DWORD wait = WaitForSingleObject(process.hProcess, 10'000);
    DWORD exit_code = ERROR_TIMEOUT;
    if (wait == WAIT_OBJECT_0) GetExitCodeProcess(process.hProcess, &exit_code);
    else if (wait == WAIT_TIMEOUT) {
        // Structural validation never owns a capture backend or target process,
        // so bounded termination is safe and prevents an internal-test helper
        // from surviving a failed GUI smoke run.
        TerminateProcess(process.hProcess, ERROR_TIMEOUT);
        WaitForSingleObject(process.hProcess, 5'000);
    }
    CloseHandle(process.hProcess);
    const auto result_wire = ReadUtf8File(result_path);
    const auto result = result_wire ? ParseEnhancedInjectorResultV2(*result_wire) :
        std::optional<EnhancedInjectorResultV2>{};
    const bool success = wait == WAIT_OBJECT_0 && exit_code == ERROR_SUCCESS && result &&
        IsVerifiedEnhancedPayloadStructureResult(*result);
    if (!success && error)
        *error = "x86 enhanced capture payload failed executable PE/export validation";
    return success;
}

std::optional<EnhancedInjectorResultV2> ParseEnhancedInjectorResultV2(
    std::string_view wire) noexcept {
    try {
        if (wire.ends_with("\r\n")) wire.remove_suffix(2);
        else if (wire.ends_with('\n')) wire.remove_suffix(1);
        if (wire.empty() || wire.find_first_of("\r\n\0") != std::string_view::npos)
            return std::nullopt;
        Fields fields;
        std::string parse_error;
        if (!ParseFlatJson(wire, fields, &parse_error) || fields.size() != 25U)
            return std::nullopt;
        constexpr std::array<std::string_view, 25> keys{
            "schemaVersion", "status", "code", "injectionAttempted",
            "moduleWasEverLoaded", "moduleLoadStateVerified",
            "moduleSnapshotVerified", "moduleAbsent", "targetProcessExited",
            "targetIdentityVerified", "probeReady", "stopSucceeded", "unloadSafe",
            "moduleUnloaded", "moduleResidentInactive", "strictUnloadVerified",
            "cleanupRetriable", "injectionMode", "suspendedThreadCount",
            "primaryThreadId", "extraReferenceRequested", "extraReferenceLoaded",
            "extraReferenceNegativeFirstFreeLibraryStillPresent",
            "extraReferenceNegativeNotClaimedUnloaded",
            "extraReferenceReleasedThenModuleAbsent"};
        for (const auto key : keys)
            if (fields.find(std::string(key)) == fields.end()) return std::nullopt;
        const auto parse_uint32 = [&](std::string_view name) -> std::optional<std::uint32_t> {
            const auto value = GetInt64(fields, name);
            if (!value || *value < 0 ||
                static_cast<std::uint64_t>(*value) > MAXDWORD) return std::nullopt;
            return static_cast<std::uint32_t>(*value);
        };
        const auto parse_bool = [&](std::string_view name) -> std::optional<bool> {
            const auto found = fields.find(std::string(name));
            if (found == fields.end() ||
                (found->second != "true" && found->second != "false"))
                return std::nullopt;
            return found->second == "true";
        };
        const auto schema_version = parse_uint32("schemaVersion");
        const auto code = parse_uint32("code");
        const auto suspended_threads = parse_uint32("suspendedThreadCount");
        const auto primary_thread = parse_uint32("primaryThreadId");
        if (!schema_version || *schema_version != 2U || !code ||
            !suspended_threads || !primary_thread) return std::nullopt;
        constexpr std::array<std::string_view, 19> boolean_keys{
            "injectionAttempted", "moduleWasEverLoaded", "moduleLoadStateVerified",
            "moduleSnapshotVerified", "moduleAbsent", "targetProcessExited",
            "targetIdentityVerified", "probeReady", "stopSucceeded", "unloadSafe",
            "moduleUnloaded", "moduleResidentInactive", "strictUnloadVerified",
            "cleanupRetriable", "extraReferenceRequested", "extraReferenceLoaded",
            "extraReferenceNegativeFirstFreeLibraryStillPresent",
            "extraReferenceNegativeNotClaimedUnloaded",
            "extraReferenceReleasedThenModuleAbsent"};
        std::array<bool, boolean_keys.size()> boolean_values{};
        for (std::size_t index = 0; index < boolean_keys.size(); ++index) {
            const auto value = parse_bool(boolean_keys[index]);
            if (!value) return std::nullopt;
            boolean_values[index] = *value;
        }
        const auto status = GetString(fields, "status");
        const auto mode = GetString(fields, "injectionMode");
        const auto safe_token = [](std::string_view value) {
            return !value.empty() && value.size() <= 96U &&
                std::all_of(value.begin(), value.end(), [](unsigned char c) {
                    return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                        (c >= '0' && c <= '9') || c == '_' || c == '-';
                });
        };
        if (!safe_token(status) || !safe_token(mode)) return std::nullopt;

        // ParseFlatJson intentionally exposes scalar text for compatibility.
        // Reconstructing the one permitted canonical wire shape proves that
        // booleans/numbers were typed primitives (not quoted strings), rejects
        // alternate ordering/whitespace, and keeps the injector/GUI contract
        // closed against duplicate or unknown fields.
        const auto canonical = MakeJsonObject({
            {"schemaVersion", "2"},
            {"status", status}, {"code", std::to_string(*code)},
            {"injectionAttempted", boolean_values[0] ? "true" : "false"},
            {"moduleWasEverLoaded", boolean_values[1] ? "true" : "false"},
            {"moduleLoadStateVerified", boolean_values[2] ? "true" : "false"},
            {"moduleSnapshotVerified", boolean_values[3] ? "true" : "false"},
            {"moduleAbsent", boolean_values[4] ? "true" : "false"},
            {"targetProcessExited", boolean_values[5] ? "true" : "false"},
            {"targetIdentityVerified", boolean_values[6] ? "true" : "false"},
            {"probeReady", boolean_values[7] ? "true" : "false"},
            {"stopSucceeded", boolean_values[8] ? "true" : "false"},
            {"unloadSafe", boolean_values[9] ? "true" : "false"},
            {"moduleUnloaded", boolean_values[10] ? "true" : "false"},
            {"moduleResidentInactive", boolean_values[11] ? "true" : "false"},
            {"strictUnloadVerified", boolean_values[12] ? "true" : "false"},
            {"cleanupRetriable", boolean_values[13] ? "true" : "false"},
            {"injectionMode", mode},
            {"suspendedThreadCount", std::to_string(*suspended_threads)},
            {"primaryThreadId", std::to_string(*primary_thread)},
            {"extraReferenceRequested", boolean_values[14] ? "true" : "false"},
            {"extraReferenceLoaded", boolean_values[15] ? "true" : "false"},
            {"extraReferenceNegativeFirstFreeLibraryStillPresent",
                boolean_values[16] ? "true" : "false"},
            {"extraReferenceNegativeNotClaimedUnloaded",
                boolean_values[17] ? "true" : "false"},
            {"extraReferenceReleasedThenModuleAbsent",
                boolean_values[18] ? "true" : "false"}
        }, {"schemaVersion", "code", "injectionAttempted", "moduleWasEverLoaded",
            "moduleLoadStateVerified", "moduleSnapshotVerified", "moduleAbsent",
            "targetProcessExited", "targetIdentityVerified", "probeReady",
            "stopSucceeded", "unloadSafe", "moduleUnloaded",
            "moduleResidentInactive", "strictUnloadVerified", "cleanupRetriable",
            "suspendedThreadCount", "primaryThreadId", "extraReferenceRequested",
            "extraReferenceLoaded",
            "extraReferenceNegativeFirstFreeLibraryStillPresent",
            "extraReferenceNegativeNotClaimedUnloaded",
            "extraReferenceReleasedThenModuleAbsent"});
        if (wire != canonical) return std::nullopt;
        EnhancedInjectorResultV2 result;
        result.status = status;
        result.code = *code;
        result.injection_attempted = boolean_values[0];
        result.module_was_ever_loaded = boolean_values[1];
        result.module_load_state_verified = boolean_values[2];
        result.module_snapshot_verified = boolean_values[3];
        result.module_absent = boolean_values[4];
        result.target_process_exited = boolean_values[5];
        result.target_identity_verified = boolean_values[6];
        result.probe_ready = boolean_values[7];
        result.stop_succeeded = boolean_values[8];
        result.unload_safe = boolean_values[9];
        result.module_unloaded = boolean_values[10];
        result.module_resident_inactive = boolean_values[11];
        result.strict_unload_verified = boolean_values[12];
        result.cleanup_retriable = boolean_values[13];
        result.injection_mode = mode;
        result.suspended_thread_count = *suspended_threads;
        result.primary_thread_id = *primary_thread;
        result.extra_reference_requested = boolean_values[14];
        result.extra_reference_loaded = boolean_values[15];
        result.extra_reference_first_free_library_still_present = boolean_values[16];
        result.extra_reference_not_claimed_unloaded = boolean_values[17];
        result.extra_reference_released_then_module_absent = boolean_values[18];
        return result;
    } catch (...) {
        return std::nullopt;
    }
}

bool IsVerifiedEnhancedDetachResult(const EnhancedInjectorResultV2& result,
                                    bool target_process_exit_observed) noexcept {
    const bool known_mode = result.injection_mode ==
            "PausePrimaryRemoteThreadResume" ||
        result.injection_mode == "PauseResumeThenRemoteThreadComplete";
    const bool no_extra_reference = !result.extra_reference_requested &&
        !result.extra_reference_loaded &&
        !result.extra_reference_first_free_library_still_present &&
        !result.extra_reference_not_claimed_unloaded &&
        !result.extra_reference_released_then_module_absent;
    if (result.code != ERROR_SUCCESS || !result.injection_attempted ||
        !result.module_was_ever_loaded || !result.module_load_state_verified ||
        !result.target_identity_verified || result.probe_ready ||
        !result.strict_unload_verified || result.module_resident_inactive ||
        result.cleanup_retriable || !known_mode ||
        result.suspended_thread_count != 1U || result.primary_thread_id == 0U ||
        !no_extra_reference)
        return false;
    if (result.status == "DETACHED_PROCESS_EXITED") {
        return result.target_process_exited && target_process_exit_observed &&
            result.module_absent && !result.module_snapshot_verified;
    }
    return result.status == "DETACHED" && !result.target_process_exited &&
        result.stop_succeeded && result.unload_safe && result.module_unloaded &&
        result.module_snapshot_verified && result.module_absent &&
        !result.cleanup_retriable;
}

bool IsVerifiedEnhancedAttachResult(
    const EnhancedInjectorResultV2& result) noexcept {
    const bool known_mode = result.injection_mode ==
            "PausePrimaryRemoteThreadResume" ||
        result.injection_mode == "PauseResumeThenRemoteThreadComplete";
    const bool no_extra_reference = !result.extra_reference_requested &&
        !result.extra_reference_loaded &&
        !result.extra_reference_first_free_library_still_present &&
        !result.extra_reference_not_claimed_unloaded &&
        !result.extra_reference_released_then_module_absent;
    return result.status == "ATTACHED" && result.code == ERROR_SUCCESS &&
        result.injection_attempted && result.module_was_ever_loaded &&
        result.module_load_state_verified && !result.module_snapshot_verified &&
        !result.module_absent && !result.target_process_exited &&
        result.target_identity_verified && result.probe_ready &&
        !result.stop_succeeded && !result.unload_safe &&
        !result.module_unloaded && !result.module_resident_inactive &&
        !result.strict_unload_verified && result.cleanup_retriable && known_mode &&
        result.suspended_thread_count == 1U && result.primary_thread_id != 0U &&
        no_extra_reference;
}

bool IsVerifiedEnhancedNoModuleLoadedResult(
    const EnhancedInjectorResultV2& result) noexcept {
    constexpr std::array<std::string_view, 7> allowed_statuses{
        "REJECTED_DLL_ARCHITECTURE", "OPEN_PROCESS_FAILED",
        "EVIDENCE_BLOCKED_BUILD_MISMATCH", "OPEN_STOP_EVENT_FAILED",
        "SUSPEND_FAILED", "INJECTION_FAILED", "REJECTED_MODE"};
    const bool allowed_status = std::find(allowed_statuses.begin(),
        allowed_statuses.end(), result.status) != allowed_statuses.end();
    const bool known_injection_mode = result.injection_mode ==
            "PausePrimaryRemoteThreadResume" ||
        result.injection_mode == "PauseResumeThenRemoteThreadComplete";
    const bool context_consistent = result.injection_attempted ?
        (result.target_identity_verified && known_injection_mode &&
         result.suspended_thread_count == 1U && result.primary_thread_id != 0U) :
        (result.injection_mode == "None" && result.suspended_thread_count == 0U &&
         result.primary_thread_id == 0U);
    const bool no_extra_reference = !result.extra_reference_requested &&
        !result.extra_reference_loaded &&
        !result.extra_reference_first_free_library_still_present &&
        !result.extra_reference_not_claimed_unloaded &&
        !result.extra_reference_released_then_module_absent;
    const bool snapshot_consistent =
        result.status == "EVIDENCE_BLOCKED_BUILD_MISMATCH" ?
        result.module_snapshot_verified : !result.module_snapshot_verified;
    return allowed_status && result.code != ERROR_SUCCESS && context_consistent &&
        no_extra_reference && !result.module_was_ever_loaded &&
        result.module_load_state_verified &&
        result.module_absent && !result.target_process_exited &&
        !result.probe_ready && !result.stop_succeeded && !result.unload_safe &&
        !result.module_unloaded && !result.module_resident_inactive &&
        result.strict_unload_verified && !result.cleanup_retriable &&
        snapshot_consistent;
}

bool IsVerifiedEnhancedPayloadStructureResult(
    const EnhancedInjectorResultV2& result) noexcept {
    return result.status == "PASS_X86_PAYLOAD_STRUCTURE" &&
        result.code == ERROR_SUCCESS && !result.injection_attempted &&
        !result.module_was_ever_loaded && !result.module_load_state_verified &&
        !result.module_snapshot_verified && !result.module_absent &&
        !result.target_process_exited && !result.target_identity_verified &&
        !result.probe_ready && !result.stop_succeeded && !result.unload_safe &&
        !result.module_unloaded && !result.module_resident_inactive &&
        !result.strict_unload_verified && !result.cleanup_retriable &&
        result.injection_mode == "None" && result.suspended_thread_count == 0U &&
        result.primary_thread_id == 0U && !result.extra_reference_requested &&
        !result.extra_reference_loaded &&
        !result.extra_reference_first_free_library_still_present &&
        !result.extra_reference_not_claimed_unloaded &&
        !result.extra_reference_released_then_module_absent;
}

bool IsVerifiedEnhancedLoadedFailureCleanupResult(
    const EnhancedInjectorResultV2& result) noexcept {
    constexpr std::array<std::string_view, 2> allowed_statuses{
        "RESUME_FAILED", "PROBE_INIT_FAILED"};
    const bool allowed_status = std::find(allowed_statuses.begin(),
        allowed_statuses.end(), result.status) != allowed_statuses.end();
    const bool known_mode = result.injection_mode ==
            "PausePrimaryRemoteThreadResume" ||
        result.injection_mode == "PauseResumeThenRemoteThreadComplete";
    const bool no_extra_reference = !result.extra_reference_requested &&
        !result.extra_reference_loaded &&
        !result.extra_reference_first_free_library_still_present &&
        !result.extra_reference_not_claimed_unloaded &&
        !result.extra_reference_released_then_module_absent;
    return allowed_status && result.code != ERROR_SUCCESS &&
        result.injection_attempted && result.module_was_ever_loaded &&
        result.module_load_state_verified && result.module_snapshot_verified &&
        result.module_absent && !result.target_process_exited &&
        result.target_identity_verified && !result.probe_ready &&
        result.stop_succeeded && result.unload_safe && result.module_unloaded &&
        !result.module_resident_inactive && result.strict_unload_verified &&
        !result.cleanup_retriable && known_mode &&
        result.suspended_thread_count == 1U && result.primary_thread_id != 0U &&
        no_extra_reference;
}

bool IsVerifiedEnhancedCleanupResult(const EnhancedInjectorResultV2& result,
                                     bool target_process_exit_observed) noexcept {
    return IsVerifiedEnhancedDetachResult(result, target_process_exit_observed) ||
        IsVerifiedEnhancedNoModuleLoadedResult(result) ||
        IsVerifiedEnhancedLoadedFailureCleanupResult(result);
}

std::optional<std::string> ReadExitedEnhancedMonitorResult(
    HANDLE monitorProcess, DWORD timeoutMs, const fs::path& resultPath) {
    if (monitorProcess == nullptr ||
        WaitForSingleObject(monitorProcess, timeoutMs) != WAIT_OBJECT_0)
        return std::nullopt;
    // The caller must validate this evidence before closing handles or deleting
    // the protected staging directory that owns resultPath.
    return ReadUtf8File(resultPath);
}

bool CanCompleteCaptureStop(bool backendStopped, bool enhancedCaptureStopped,
                            bool analysisFinished, bool privacyEnforced,
                            bool packageFinished,
                            bool ultimatePackageFinished) noexcept {
    return backendStopped && enhancedCaptureStopped && analysisFinished &&
        privacyEnforced && packageFinished && ultimatePackageFinished;
}

ConsumerState EnhancedCaptureConsumerState(bool requested, bool attach_in_progress,
                                           bool probe_ready, bool strict_stopped,
                                           bool evidence_blocked) noexcept {
    if (!requested) return ConsumerState::NotStarted;
    if (evidence_blocked) return ConsumerState::EvidenceBlocked;
    if (strict_stopped) return ConsumerState::Stopped;
    if (probe_ready) return ConsumerState::Ready;
    if (attach_in_progress) return ConsumerState::Starting;
    return ConsumerState::NotStarted;
}

ConsumerState EnhancedCaptureConsumerStateAfterTargetDetection(
    ConsumerState current, GameProcessState target_state) noexcept {
    if (target_state == GameProcessState::Exited &&
        (current == ConsumerState::Ready || current == ConsumerState::Starting))
        return ConsumerState::Stopping;
    return current;
}

bool ConsumeSemanticDomainPending(
    god2::shared::SemanticSharedRing* ring,
    std::uint32_t consumed_domain) noexcept {
    if (ring == nullptr || consumed_domain >= god2::shared::kSemanticDomainCount)
        return false;
    auto& domain_lag = ring->domains[consumed_domain].consumer_lag;
    if (InterlockedDecrement(&domain_lag) >= 0)
        return true;
    InterlockedExchange(&domain_lag, 0);
    return false;
}

struct SharedSemanticSegmentEvidence {
    fs::path path;
    std::uint64_t records = 0;
    std::uint64_t bytes = 0;
    std::uint32_t first_sequence = 0;
    std::uint32_t last_sequence = 0;
    std::string sha256;
};

class EnhancedCaptureManager {
public:
    ~EnhancedCaptureManager() {
        StopNoThrow();
        if (target_process_identity_handle_ != nullptr)
            CloseHandle(target_process_identity_handle_);
    }

    void Configure(bool requested, const fs::path& session_path, std::string_view session_id,
                   const fs::path& game_path, HANDLE controller_cancel_event) {
        std::lock_guard lock(mutex_);
        StopSharedTransport();
        if (monitor_process_ == nullptr) ReleaseSecurePayloadStaging();
        requested_ = requested;
        stop_requested_.store(false, std::memory_order_release);
        session_path_ = session_path;
        session_id_ = std::string(session_id);
        game_path_ = game_path;
        controller_cancel_event_ = controller_cancel_event;
        attached_pid_ = 0;
        ever_attached_ = false;
        probe_ready_ = false;
        safe_detach_observed_ = false;
        injection_attempted_ = false;
        module_was_ever_loaded_ = false;
        module_load_state_verified_ = true;
        module_snapshot_verified_ = false;
        module_absent_ = true;
        target_process_exited_ = false;
        target_identity_verified_ = false;
        injector_payload_validated_ = false;
        probe_payload_validated_ = false;
        if (target_process_identity_handle_ != nullptr) {
            CloseHandle(target_process_identity_handle_);
            target_process_identity_handle_ = nullptr;
        }
        failure_detail_.clear();
        failure_kind_.clear();
        probe_path_.clear();
        injector_path_.clear();
        result_path_.clear();
        WriteStatus(requested ? "WaitingForVerifiedX86Process" : "Disabled", "");
    }

    bool Requested() const {
        std::lock_guard lock(mutex_);
        return requested_;
    }

    bool Active() const {
        std::lock_guard lock(mutex_);
        return monitor_process_ != nullptr && WaitForSingleObject(monitor_process_, 0) == WAIT_TIMEOUT;
    }

    ConsumerState ObserveConsumerState() {
        std::lock_guard lock(mutex_);
        if (!requested_) return ConsumerState::NotStarted;
        if (monitor_process_ == nullptr) {
            return EnhancedCaptureConsumerState(true, false, false,
                safe_detach_observed_, !failure_detail_.empty());
        }
        const DWORD monitor_wait = WaitForSingleObject(monitor_process_, 0);
        if (monitor_wait == WAIT_TIMEOUT) {
            return EnhancedCaptureConsumerState(true, !probe_ready_, probe_ready_,
                                                false, false);
        }
        if (monitor_wait != WAIT_OBJECT_0) {
            return EnhancedCaptureConsumerState(true, false, false, false, true);
        }
        DWORD exit_code = ERROR_GEN_FAILURE;
        GetExitCodeProcess(monitor_process_, &exit_code);
        const auto result_wire = ReadUtf8File(result_path_);
        const auto result = result_wire ? ParseEnhancedInjectorResultV2(*result_wire) :
            std::optional<EnhancedInjectorResultV2>{};
        if (!result || exit_code != ERROR_SUCCESS) {
            return EnhancedCaptureConsumerState(true, false, false, false, true);
        }
        ApplyInjectorResult(*result);
        const bool strict_stopped = IsVerifiedEnhancedDetachResult(
            *result, ObserveTrackedTargetProcessExit());
        return EnhancedCaptureConsumerState(true, false, false, strict_stopped,
                                            !strict_stopped);
    }

    bool TryAttach(DWORD process_id, std::string* error) {
        std::lock_guard lock(mutex_);
        if (!requested_ || stop_requested_.load(std::memory_order_acquire) || process_id == 0) return true;
        if (monitor_process_ != nullptr) {
            if (WaitForSingleObject(monitor_process_, 0) == WAIT_TIMEOUT && attached_pid_ == process_id) return true;
            if (WaitForSingleObject(monitor_process_, 0) == WAIT_TIMEOUT)
                return Fail("a previous enhanced capture helper is still running and requires Stop cleanup", error);
            DWORD prior_exit_code = ERROR_GEN_FAILURE;
            GetExitCodeProcess(monitor_process_, &prior_exit_code);
            const auto prior_result_wire = ReadUtf8File(result_path_);
            const auto prior_result = prior_result_wire ?
                ParseEnhancedInjectorResultV2(*prior_result_wire) :
                std::optional<EnhancedInjectorResultV2>{};
            if (prior_result) ApplyInjectorResult(*prior_result);
            const bool prior_cleanup_verified = prior_exit_code == ERROR_SUCCESS &&
                prior_result && IsVerifiedEnhancedCleanupResult(
                    *prior_result, ObserveTrackedTargetProcessExit());
            if (!prior_cleanup_verified) {
                failure_kind_ = "EvidenceBlockedPriorInjectorCleanupUnverified";
                failure_detail_ = prior_result_wire ? *prior_result_wire :
                    "a prior enhanced capture helper exited without canonical strict cleanup evidence";
                WriteStatus("EvidenceBlocked", failure_detail_);
                if (error != nullptr) *error = failure_detail_;
                return false;
            }
            CloseMonitorHandles();
            failure_detail_.clear();
            failure_kind_.clear();
        }
        if (!IsAdministrator()) {
            return Fail(
                "EvidenceBlockedAdministratorRequiredForSecurePayloadStaging: restart God2 Semantic Recovery Engine as administrator; enhanced attach never elevates an injector from a user-writable path",
                error, "EvidenceBlockedAdministratorRequiredForSecurePayloadStaging");
        }
        // Re-bind the selected file to the exact running PID immediately before
        // launching the injector from protected ProgramData staging.  This
        // process must already be elevated; no user-writable image is passed
        // across a UAC boundary.  The injector independently repeats identity.
        UniqueHandle target_process(OpenProcess(
            PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_DUP_HANDLE | SYNCHRONIZE,
            FALSE, process_id));
        fs::path observed_path;
        FILETIME created{}, exited{}, kernel{}, user{};
        bool process_times_ok = false;
        if (target_process.Get() != nullptr) {
            std::vector<wchar_t> image(32'768);
            DWORD image_length = static_cast<DWORD>(image.size());
            if (QueryFullProcessImageNameW(target_process.Get(), 0, image.data(), &image_length))
                observed_path = fs::path(std::wstring(image.data(), image_length));
            process_times_ok = GetProcessTimes(target_process.Get(), &created, &exited, &kernel, &user) == TRUE;
        }
        std::error_code observed_path_error;
        std::error_code expected_path_error;
        const auto canonical_observed = observed_path.empty() ? fs::path{} :
            fs::weakly_canonical(observed_path, observed_path_error);
        const auto canonical_expected = fs::weakly_canonical(game_path_, expected_path_error);
        const bool path_matches = !observed_path_error && !expected_path_error &&
            !canonical_observed.empty() &&
            _wcsicmp(canonical_observed.c_str(), canonical_expected.c_str()) == 0;
        const bool process_is_x86 = GameProcessTracker::Is32BitProcess(process_id);
        std::optional<std::string> observed_hash;
        std::string observed_version;
        const bool file_identity_matches = path_matches && MatchesExactClientFileIdentity(
            canonical_observed, &observed_version, &observed_hash);
        const std::uint64_t creation_ticks = process_times_ok ?
            (static_cast<std::uint64_t>(created.dwHighDateTime) << 32) | created.dwLowDateTime : 0;
        const bool exact_build = file_identity_matches && process_is_x86 && process_times_ok;
        const fs::path identity_report = session_path_ / L"reports" / L"client-build-identity.json";
        const bool identity_report_written = WriteUtf8FileAtomic(identity_report, MakeJsonObject({
            {"SchemaVersion", "god2-ultimate-client-build-identity-v1"},
            {"ObservedAtUtc", UtcNow()},
            {"Status", exact_build ? "PASS" : "EvidenceBlockedBuildMismatch"},
            {"ProcessId", std::to_string(process_id)},
            {"ProcessCreationTime", std::to_string(creation_ticks)},
            {"ImagePath", WideToUtf8(observed_path.wstring())},
            {"PathMatches", path_matches ? "true" : "false"},
            {"Architecture", process_is_x86 ? "x86" : "NotX86"},
            {"FileVersion", observed_version.empty() ? "Unavailable" : observed_version},
            {"SHA256", observed_hash.value_or("Unavailable")},
            {"RequiredFileName", "God2_opt.exe"},
            {"RequiredArchitecture", "x86"},
            {"RequiredFileVersion", std::string(kExactClientVersion)},
            {"RequiredSHA256", std::string(kExactClientSha256)}
        }, {"ProcessId", "ProcessCreationTime", "PathMatches"}) + "\n");
        if (!identity_report_written)
            return Fail("cannot persist the exact client build identity gate", error,
                        "EvidenceBlockedBuildIdentityReportUnavailable");
        if (!exact_build) {
            return Fail("EvidenceBlockedBuildMismatch: God2_opt.exe must match x86 / 1.0.0.1 / SHA-256 " +
                std::string(kExactClientSha256), error, "EvidenceBlockedBuildMismatch");
        }
        HANDLE target_identity_handle = nullptr;
        if (!DuplicateHandle(GetCurrentProcess(), target_process.Get(),
                GetCurrentProcess(), &target_identity_handle,
                SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION,
                FALSE, 0) || target_identity_handle == nullptr) {
            return Fail("cannot retain an identity-bound target process handle for strict DLL cleanup proof",
                error, "EvidenceBlockedTargetProcessIdentityHandleUnavailable");
        }
        if (target_process_identity_handle_ != nullptr)
            CloseHandle(target_process_identity_handle_);
        target_process_identity_handle_ = target_identity_handle;
        target_identity_verified_ = true;
        std::error_code directory_error;
        const fs::path enhanced_dir = session_path_ / L"raw" / L"enhanced-x86";
        fs::create_directories(enhanced_dir, directory_error);
        if (directory_error) return Fail("cannot create enhanced capture directory: " + directory_error.message(), error);
        if (!PrepareSecurePayloadStaging(error)) {
            return Fail(error != nullptr ? *error :
                "cannot create protected ProgramData payload staging", error,
                "EvidenceBlockedSecurePayloadStagingUnavailable");
        }
        probe_path_ = secure_payload_dir_ / L"God2PacketCaptureProbe.dll";
        injector_path_ = secure_payload_dir_ / L"God2PacketCaptureInjector.exe";
        SECURITY_ATTRIBUTES payload_security = SecurePayloadSecurityAttributes();
        if (payload_security.lpSecurityDescriptor == nullptr ||
            !ExtractEmbeddedBinary(IDR_X86_PACKET_PROBE, probe_path_, error,
                                   &payload_security, CREATE_NEW) ||
            !ExtractEmbeddedBinary(IDR_X86_PACKET_INJECTOR, injector_path_, error,
                                   &payload_security, CREATE_NEW)) {
            return Fail(error != nullptr ? *error : "payload extraction failed", error,
                        "PayloadExtractionFailed");
        }
        std::error_code payload_error;
        const bool probe_present = fs::is_regular_file(probe_path_, payload_error) && !payload_error;
        payload_error.clear();
        const bool injector_present = fs::is_regular_file(injector_path_, payload_error) && !payload_error;
        if (!probe_present || !injector_present) {
            return Fail(
                "x86 enhanced capture payload disappeared immediately after extraction; Windows Security or other security software may have quarantined it. Enhanced plaintext capture was not started and this session must not be treated as decrypted",
                error, "SecuritySoftwareRemovalSuspected");
        }
        if (!LockSecurePayloadFile(probe_path_, &secure_probe_handle_, error) ||
            !LockSecurePayloadFile(injector_path_, &secure_injector_handle_, error)) {
            return Fail(error != nullptr ? *error :
                "cannot lock protected enhanced capture payload", error,
                "EvidenceBlockedSecurePayloadLockUnavailable");
        }
        if (!EmbeddedBinaryMatchesFile(IDR_X86_PACKET_PROBE, probe_path_, error) ||
            !EmbeddedBinaryMatchesFile(IDR_X86_PACKET_INJECTOR, injector_path_, error)) {
            return Fail(error != nullptr ? *error :
                "x86 enhanced capture payload integrity verification failed", error,
                "PayloadIntegrityFailed");
        }
        if (!IsX86PeFile(probe_path_) || !IsX86PeFile(injector_path_)) {
            return Fail(
                "x86 enhanced capture payload failed PE validation after extraction; the file may be incomplete or have been altered by security software",
                error, "PayloadIntegrityFailed");
        }
        probe_payload_validated_ = true;
        injector_payload_validated_ = true;
        if (!StartSharedTransport(enhanced_dir, target_process.Get(), error))
            return Fail(error != nullptr ? *error : "cannot initialize bounded semantic shared transport",
                        error, "EvidenceBlockedSharedTransportUnavailable");
        const std::string config =
            "traceDir=" + WideToUtf8(enhanced_dir.wstring()) + "\n" +
            "generalLog=" + WideToUtf8((session_path_ / L"reports" / L"enhanced-capture.log").wstring()) + "\n" +
            "metadata=" + WideToUtf8((session_path_ / L"raw" / L"injected-packets.jsonl").wstring()) + "\n" +
            "semantic=" + WideToUtf8((session_path_ / L"raw" / L"semantic-events.jsonl").wstring()) + "\n" +
            "sessionId=" + session_id_ + "\n" +
            "sharedHandleContract=God2SemanticSharedHandle/2\n"
            "sharedMemoryHandle=" + std::to_string(
                static_cast<unsigned long long>(reinterpret_cast<std::uintptr_t>(
                    shared_remote_mapping_))) + "\n" +
            "sharedEventHandle=" + std::to_string(
                static_cast<unsigned long long>(reinterpret_cast<std::uintptr_t>(
                    shared_remote_event_))) + "\n" +
            "clientBuildVerified=1\n"
            "clientVersion=" + observed_version + "\n" +
            "clientSha256=" + *observed_hash + "\n" +
            "clientProcessId=" + std::to_string(process_id) + "\n" +
            "clientProcessCreationTime=" + std::to_string(creation_ticks) + "\n" +
            "enableInline=auto\n"
            "enableInputInline=0\n"
            "enableInputHooks=0\n"
            // The exact-build guarded version probe installs the post-decrypt
            // hook. Unsupported builds fail closed and retain Winsock capture.
            "enableVersionProbe=1\n"
            "networkOnly=0\n";
        config_path_ = secure_payload_dir_ / L"God2ClientTraceProbe.attach.env";
        if (!WriteSecureStagingFile(config_path_, config, error) ||
            !LockSecurePayloadFile(config_path_, &secure_config_handle_, error))
            return Fail("cannot write the enhanced plaintext probe configuration", error);

        const std::wstring nonce = Utf8ToWide(NewId());
        stop_event_name_ = L"Local\\God2PacketCapture.EnhancedStop." + nonce;
        stop_event_ = CreateEventW(nullptr, TRUE, FALSE, stop_event_name_.c_str());
        if (stop_event_ == nullptr)
            return Fail("cannot create the enhanced capture stop event", error);
        // The protected payload directory is intentionally read/execute-only
        // for the interactive user. Publish the non-secret control record into
        // this approved session instead, then cross-bind ATTACHED to the
        // producer-ready shared ring and exact target PID below.
        result_path_ = enhanced_dir / (L"injector-control-" + nonce + L".json");
        if (GetFileAttributesW(result_path_.c_str()) != INVALID_FILE_ATTRIBUTES)
            return Fail("enhanced capture control result path unexpectedly exists", error,
                        "EvidenceBlockedSecurePayloadStagingUnavailable");
        const std::wstring parameters = L"--monitor --pid " + std::to_wstring(process_id) +
            L" --expected-exe " + QuoteArgument(game_path_.wstring()) +
            L" --dll " + QuoteArgument(probe_path_.wstring()) +
            L" --stop-event " + QuoteArgument(stop_event_name_) +
            L" --result " + QuoteArgument(result_path_.wstring());
        const std::wstring command = QuoteArgument(injector_path_.wstring()) + L" " + parameters;
        std::vector<wchar_t> mutable_command(command.begin(), command.end());
        mutable_command.push_back(L'\0');
        STARTUPINFOW startup{};
        startup.cb = sizeof(startup);
        PROCESS_INFORMATION process{};
        if (!CreateProcessW(injector_path_.c_str(), mutable_command.data(), nullptr, nullptr, FALSE,
                            CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT, nullptr,
                            secure_payload_dir_.c_str(), &startup, &process)) {
            const DWORD code = GetLastError();
            CloseHandle(stop_event_);
            stop_event_ = nullptr;
            payload_error.clear();
            const bool injector_still_present = fs::is_regular_file(injector_path_, payload_error) && !payload_error;
            if (!injector_still_present || code == ERROR_FILE_NOT_FOUND || code == ERROR_PATH_NOT_FOUND) {
                return Fail(
                    "x86 enhanced capture injector was removed before it could start; Windows Security or other security software may have quarantined it. Enhanced plaintext capture is unavailable",
                    error, "SecuritySoftwareRemovalSuspected");
            }
            return Fail("enhanced capture helper could not start from protected ProgramData staging: " +
                WideToUtf8(ErrorText(code)), error, "InjectorLaunchFailed");
        }
        CloseHandle(process.hThread);
        if (process.hProcess == nullptr) {
            SetEvent(stop_event_);
            CloseHandle(stop_event_);
            stop_event_ = nullptr;
            return Fail("enhanced capture helper returned no monitor process handle", error);
        }
        monitor_process_ = process.hProcess;
        probe_ready_ = false;
        // Once the helper exists, module-load state is unknown until its exact
        // v2 result is parsed.  No timeout or process-exit path may fall back
        // to the pre-injection "absent" assumption.
        module_load_state_verified_ = false;
        module_absent_ = false;
        safe_detach_observed_ = false;
        for (int attempt = 0; attempt < 600; ++attempt) {
            if (stop_requested_.load(std::memory_order_acquire) ||
                (controller_cancel_event_ != nullptr &&
                 WaitForSingleObject(controller_cancel_event_, 0) == WAIT_OBJECT_0)) {
                if (stop_event_ == nullptr || !SetEvent(stop_event_))
                    return Fail("enhanced capture cancellation could not signal the helper; Stop must retry cleanup", error);
                const auto cancellation_result_wire = ReadExitedEnhancedMonitorResult(
                    monitor_process_, 30'000, result_path_);
                const auto cancellation_result = cancellation_result_wire ?
                    ParseEnhancedInjectorResultV2(*cancellation_result_wire) :
                    std::optional<EnhancedInjectorResultV2>{};
                if (cancellation_result) ApplyInjectorResult(*cancellation_result);
                const bool helper_stopped = cancellation_result_wire.has_value() ||
                    (monitor_process_ != nullptr &&
                     WaitForSingleObject(monitor_process_, 0) == WAIT_OBJECT_0);
                const bool cleanup_safe = helper_stopped && cancellation_result &&
                    IsVerifiedEnhancedCleanupResult(
                        *cancellation_result, ObserveTrackedTargetProcessExit());
                safe_detach_observed_ = cleanup_safe;
                if (helper_stopped && cleanup_safe) CloseMonitorHandles();
                WriteStatus(cleanup_safe ? "StoppedDuringAttach" : "EvidenceBlocked",
                    cleanup_safe ? *cancellation_result_wire :
                        (cancellation_result_wire ? *cancellation_result_wire :
                         "enhanced capture cancellation did not prove safe DLL unload; Stop must retry cleanup"));
                if (error != nullptr) *error = cleanup_safe ?
                    "enhanced capture was cancelled while attaching after verified DLL unload" :
                    "enhanced capture cancellation did not prove safe DLL unload";
                return false;
            }
            if (const auto result_wire = ReadUtf8File(result_path_);
                result_wire && !result_wire->empty()) {
                const auto result = ParseEnhancedInjectorResultV2(*result_wire);
                if (!result) {
                    return Fail("enhanced capture helper emitted a malformed or non-canonical v2 result",
                        error, "EvidenceBlockedInvalidInjectorResult");
                }
                ApplyInjectorResult(*result);
                const bool producer_ring_bound = shared_ring_ != nullptr &&
                    InterlockedCompareExchange(&shared_ring_->producer_ready, 0, 0) == 1 &&
                    shared_ring_->producer_process_id == process_id;
                if (IsVerifiedEnhancedAttachResult(*result) && producer_ring_bound) {
                    MarkSharedTransportHandlesTransferred();
                    attached_pid_ = process_id;
                    ever_attached_ = true;
                    probe_ready_ = true;
                    failure_detail_.clear();
                    failure_kind_.clear();
                    WriteStatus("Active", *result_wire);
                    return true;
                }
                if (IsVerifiedEnhancedAttachResult(*result) && !producer_ring_bound) {
                    return Fail("injector reported ATTACHED without an exact-PID producer-ready shared ring",
                        error, "EvidenceBlockedSharedTransportProducerBindingMissing");
                }
                const std::string detail = "enhanced capture rejected: " + *result_wire;
                const bool terminal_cleanup_safe = IsVerifiedEnhancedCleanupResult(
                    *result, ObserveTrackedTargetProcessExit());
                if (terminal_cleanup_safe) {
                    if (WaitForSingleObject(monitor_process_, 5'000) != WAIT_OBJECT_0)
                        return Fail(detail +
                            "; strict cleanup was reported before the helper process exited, so Stop must retry",
                            error, "EvidenceBlockedInjectorCleanupProcessStillRunning");
                    CloseMonitorHandles();
                    safe_detach_observed_ = true;
                    failure_detail_.clear();
                    failure_kind_.clear();
                    WriteStatus("StoppedDuringAttach", *result_wire);
                    if (error != nullptr)
                        *error = "enhanced capture did not become Ready, but strict no-residual cleanup was verified";
                    return false;
                }
                if (stop_event_ == nullptr || !SetEvent(stop_event_))
                    return Fail(detail + "; helper stop signal failed and Stop must retry cleanup", error);
                const auto cleanup_result_wire = ReadExitedEnhancedMonitorResult(
                    monitor_process_, 30'000, result_path_);
                const auto cleanup_result = cleanup_result_wire ?
                    ParseEnhancedInjectorResultV2(*cleanup_result_wire) :
                    std::optional<EnhancedInjectorResultV2>{};
                if (!cleanup_result_wire)
                    return Fail(detail + "; helper retained for Stop retry", error);
                if (cleanup_result) ApplyInjectorResult(*cleanup_result);
                const bool cleanup_safe = cleanup_result && IsVerifiedEnhancedCleanupResult(
                    *cleanup_result, ObserveTrackedTargetProcessExit());
                if (!cleanup_safe)
                    return Fail(detail + "; helper exited without strict no-residual cleanup proof", error,
                                "EvidenceBlockedInjectorCleanupUnverified");
                CloseMonitorHandles();
                safe_detach_observed_ = true;
                failure_detail_.clear();
                failure_kind_.clear();
                WriteStatus("StoppedDuringAttach", *cleanup_result_wire);
                if (error != nullptr)
                    *error = "enhanced capture did not become Ready, but strict no-residual cleanup was verified";
                return false;
            }
            if (WaitForSingleObject(monitor_process_, 0) == WAIT_OBJECT_0) break;
            Sleep(100);
        }
        if (monitor_process_ != nullptr && WaitForSingleObject(monitor_process_, 0) == WAIT_OBJECT_0) {
            DWORD exit_code = ERROR_GEN_FAILURE;
            GetExitCodeProcess(monitor_process_, &exit_code);
            payload_error.clear();
            const bool injector_still_present = fs::is_regular_file(injector_path_, payload_error) && !payload_error;
            const auto terminal_wire = ReadUtf8File(result_path_);
            const auto terminal_result = terminal_wire ?
                ParseEnhancedInjectorResultV2(*terminal_wire) :
                std::optional<EnhancedInjectorResultV2>{};
            if (terminal_result) ApplyInjectorResult(*terminal_result);
            const bool cleanup_safe = exit_code == ERROR_SUCCESS && terminal_result &&
                IsVerifiedEnhancedCleanupResult(
                    *terminal_result, ObserveTrackedTargetProcessExit());
            if (cleanup_safe) {
                CloseMonitorHandles();
                safe_detach_observed_ = true;
                failure_detail_.clear();
                failure_kind_.clear();
                WriteStatus("StoppedDuringAttach", *terminal_wire);
                if (error != nullptr)
                    *error = "enhanced capture helper exited before Ready after verified no-residual cleanup";
                return false;
            }
            if (!injector_still_present) {
                return Fail(
                    "x86 enhanced capture injector exited without a readiness result and its file is no longer present; Windows Security or other security software likely quarantined it. Enhanced plaintext capture was not started",
                    error, "SecuritySoftwareRemovalSuspected");
            }
            return Fail("x86 enhanced capture injector exited before DLL readiness with code " +
                        std::to_string(exit_code), error, "InjectorExitedBeforeReady");
        }
        if (stop_event_ == nullptr || !SetEvent(stop_event_))
            return Fail("enhanced capture initialization timed out and the helper stop signal failed", error);
        const auto timeout_cleanup_wire = ReadExitedEnhancedMonitorResult(
            monitor_process_, 30'000, result_path_);
        if (!timeout_cleanup_wire)
            return Fail("enhanced capture initialization timed out; helper retained for Stop retry", error);
        const auto timeout_cleanup_result =
            ParseEnhancedInjectorResultV2(*timeout_cleanup_wire);
        if (timeout_cleanup_result) ApplyInjectorResult(*timeout_cleanup_result);
        const bool timeout_cleanup_safe = timeout_cleanup_result &&
            IsVerifiedEnhancedCleanupResult(
                *timeout_cleanup_result, ObserveTrackedTargetProcessExit());
        if (!timeout_cleanup_safe)
            return Fail("enhanced capture initialization timed out and helper cleanup was not strictly verified",
                        error, "EvidenceBlockedInjectorCleanupUnverified");
        CloseMonitorHandles();
        safe_detach_observed_ = true;
        failure_detail_.clear();
        failure_kind_.clear();
        WriteStatus("StoppedDuringAttach", *timeout_cleanup_wire);
        if (error != nullptr)
            *error = "enhanced capture initialization timed out after verified no-residual cleanup";
        return false;
    }

    bool Stop(std::string* error) {
        stop_requested_.store(true, std::memory_order_release);
        std::lock_guard lock(mutex_);
        if (monitor_process_ == nullptr) {
            StopSharedTransport();
            bool success = false;
            if (!requested_) {
                WriteStatus("Disabled", "");
                success = true;
            } else if (!failure_detail_.empty()) {
                WriteStatus("EvidenceBlocked", failure_detail_);
            } else if (safe_detach_observed_) {
                WriteStatus("Stopped", "verified x86 attachment had already ended before Stop");
                success = true;
            } else if (ever_attached_) {
                failure_kind_ = "EvidenceBlockedProbeNotUnloaded";
                failure_detail_ =
                    "the verified x86 probe attachment ended without proof that the DLL was unloaded";
                WriteStatus("EvidenceBlocked", failure_detail_);
            } else {
                failure_kind_ = "NoVerifiedAttachment";
                failure_detail_ =
                    "x86 enhanced capture was requested, but no verified DLL attachment or readiness handshake was observed";
                WriteStatus("EvidenceBlocked", failure_detail_);
            }
            if (!success && error != nullptr) *error = failure_detail_;
            ReleaseSecurePayloadStaging();
            return success;
        }
        if (WaitForSingleObject(monitor_process_, 0) == WAIT_OBJECT_0) {
            DWORD exit_code = ERROR_GEN_FAILURE;
            GetExitCodeProcess(monitor_process_, &exit_code);
            const auto result_wire = ReadUtf8File(result_path_);
            const auto result = result_wire ? ParseEnhancedInjectorResultV2(*result_wire) :
                std::optional<EnhancedInjectorResultV2>{};
            if (result) ApplyInjectorResult(*result);
            const bool success = exit_code == ERROR_SUCCESS && result &&
                IsVerifiedEnhancedCleanupResult(
                    *result, ObserveTrackedTargetProcessExit());
            safe_detach_observed_ = success;
            const std::string detail = result_wire ? *result_wire :
                "x86 enhanced capture helper returned no valid v2 result";
            if (success) {
                CloseMonitorHandles();
                WriteStatus("Stopped", detail);
            } else {
                failure_kind_ = ever_attached_ ? "EvidenceBlockedProbeNotUnloaded" :
                    "InjectorExitedBeforeReady";
                failure_detail_ = result_wire ? *result_wire :
                    "x86 enhanced capture helper exited without a verified safe-unload result with code " +
                        std::to_string(exit_code);
                WriteStatus("EvidenceBlocked", failure_detail_);
                if (error != nullptr) *error = failure_detail_;
            }
            return success;
        }
        if (stop_event_ == nullptr || !SetEvent(stop_event_))
            return Fail("could not request the x86 probe to flush and detach", error);
        if (WaitForSingleObject(monitor_process_, 30'000) != WAIT_OBJECT_0)
            return Fail("x86 probe did not confirm flush and detach within 30 seconds", error);
        DWORD exit_code = ERROR_GEN_FAILURE;
        GetExitCodeProcess(monitor_process_, &exit_code);
        const auto result_wire = ReadUtf8File(result_path_);
        const auto result = result_wire ? ParseEnhancedInjectorResultV2(*result_wire) :
            std::optional<EnhancedInjectorResultV2>{};
        if (result) ApplyInjectorResult(*result);
        const bool success = exit_code == ERROR_SUCCESS && result &&
            IsVerifiedEnhancedCleanupResult(
                *result, ObserveTrackedTargetProcessExit());
        const std::string detail = result_wire ? *result_wire :
            "enhanced capture helper returned no valid v2 detach result";
        safe_detach_observed_ = success;
        if (!success) {
            failure_kind_ = "EvidenceBlockedProbeNotUnloaded";
            failure_detail_ = detail;
        } else {
            CloseMonitorHandles();
        }
        WriteStatus(success ? "Stopped" : "EvidenceBlocked", detail);
        if (!success && error != nullptr) *error = detail;
        return success;
    }

private:
    SECURITY_ATTRIBUTES SecurePayloadSecurityAttributes() const noexcept {
        SECURITY_ATTRIBUTES security{};
        security.nLength = sizeof(security);
        security.lpSecurityDescriptor = secure_security_descriptor_;
        security.bInheritHandle = FALSE;
        return security;
    }

    bool WriteSecureStagingFile(const fs::path& path, std::string_view value,
                                std::string* error) const {
        SECURITY_ATTRIBUTES security = SecurePayloadSecurityAttributes();
        if (security.lpSecurityDescriptor == nullptr) {
            if (error != nullptr) *error = "protected staging security descriptor is unavailable";
            return false;
        }
        HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, 0, &security, CREATE_NEW,
                                  FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_TEMPORARY, nullptr);
        if (file == INVALID_HANDLE_VALUE) {
            if (error != nullptr) *error = "cannot create protected staging file: " +
                WideToUtf8(ErrorText(GetLastError()));
            return false;
        }
        bool success = true;
        std::size_t offset = 0;
        while (offset < value.size()) {
            const DWORD requested = static_cast<DWORD>(std::min<std::size_t>(
                value.size() - offset, static_cast<std::size_t>(MAXDWORD)));
            DWORD written = 0;
            if (!WriteFile(file, value.data() + offset, requested, &written, nullptr) ||
                written != requested) {
                success = false;
                if (error != nullptr) *error = "cannot write protected staging file: " +
                    WideToUtf8(ErrorText(GetLastError()));
                break;
            }
            offset += written;
        }
        if (success && !FlushFileBuffers(file)) {
            success = false;
            if (error != nullptr) *error = "cannot flush protected staging file: " +
                WideToUtf8(ErrorText(GetLastError()));
        }
        CloseHandle(file);
        if (!success) DeleteFileW(path.c_str());
        return success;
    }

    static bool HandleHasExpectedType(HANDLE handle, bool directory, std::string* error) {
        FILE_ATTRIBUTE_TAG_INFO attributes{};
        if (!GetFileInformationByHandleEx(handle, FileAttributeTagInfo,
                                          &attributes, sizeof(attributes))) {
            if (error != nullptr) *error = "cannot inspect protected payload staging handle: " +
                WideToUtf8(ErrorText(GetLastError()));
            return false;
        }
        const bool is_directory = (attributes.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
        if (is_directory != directory ||
            (attributes.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0) {
            if (error != nullptr) *error = directory ?
                "protected ProgramData staging is not a non-reparse directory" :
                "protected enhanced payload is not a non-reparse regular file";
            return false;
        }
        return true;
    }

    bool PrepareSecurePayloadStaging(std::string* error) {
        ReleaseSecurePayloadStaging();

        std::array<wchar_t, MAX_PATH> common_app_data{};
        const HRESULT folder_result = SHGetFolderPathW(nullptr, CSIDL_COMMON_APPDATA, nullptr,
                                                       SHGFP_TYPE_CURRENT, common_app_data.data());
        if (FAILED(folder_result) || common_app_data[0] == L'\0') {
            if (error != nullptr) *error = "cannot resolve ProgramData for protected payload staging";
            return false;
        }
        const fs::path program_data(common_app_data.data());
        HANDLE program_data_handle = CreateFileW(
            program_data.c_str(), GENERIC_READ | READ_CONTROL, FILE_SHARE_READ | FILE_SHARE_WRITE,
            nullptr, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
        if (program_data_handle == INVALID_HANDLE_VALUE) {
            if (error != nullptr) *error = "cannot open ProgramData without following a reparse point: " +
                WideToUtf8(ErrorText(GetLastError()));
            return false;
        }
        const bool program_data_safe = HandleHasExpectedType(program_data_handle, true, error);
        CloseHandle(program_data_handle);
        if (!program_data_safe) return false;

        if (!BuildProtectedPayloadSecurityDescriptor(&secure_security_descriptor_, error)) {
            if (error != nullptr && error->empty())
                *error = "cannot construct the protected payload security descriptor";
            return false;
        }
        SECURITY_ATTRIBUTES security{};
        security.nLength = sizeof(security);
        security.lpSecurityDescriptor = secure_security_descriptor_;
        security.bInheritHandle = FALSE;

        DWORD create_error = ERROR_ALREADY_EXISTS;
        for (int attempt = 0; attempt < 8; ++attempt) {
            const fs::path candidate = program_data /
                (L"God2SemanticRecoveryEngine-Payload-" + Utf8ToWide(NewId()));
            if (CreateDirectoryW(candidate.c_str(), &security)) {
                secure_payload_dir_ = candidate;
                create_error = ERROR_SUCCESS;
                break;
            }
            create_error = GetLastError();
            if (create_error != ERROR_ALREADY_EXISTS && create_error != ERROR_FILE_EXISTS) break;
        }
        if (secure_payload_dir_.empty()) {
            if (error != nullptr) *error = "cannot create unique protected ProgramData staging: " +
                WideToUtf8(ErrorText(create_error));
            LocalFree(secure_security_descriptor_);
            secure_security_descriptor_ = nullptr;
            return false;
        }

        secure_directory_handle_ = CreateFileW(
            secure_payload_dir_.c_str(), GENERIC_READ | READ_CONTROL, FILE_SHARE_READ,
            nullptr, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
            nullptr);
        if (secure_directory_handle_ == INVALID_HANDLE_VALUE) secure_directory_handle_ = nullptr;
        if (secure_directory_handle_ == nullptr ||
            !HandleHasExpectedType(secure_directory_handle_, true, error)) {
            if (secure_directory_handle_ != nullptr) {
                CloseHandle(secure_directory_handle_);
                secure_directory_handle_ = nullptr;
            }
            RemoveDirectoryW(secure_payload_dir_.c_str());
            secure_payload_dir_.clear();
            if (error != nullptr && error->empty())
                *error = "cannot lock protected ProgramData staging directory";
            return false;
        }
        return true;
    }

    static bool LockSecurePayloadFile(const fs::path& path, HANDLE* retained,
                                      std::string* error) {
        if (retained == nullptr) return false;
        if (*retained != nullptr && *retained != INVALID_HANDLE_VALUE) CloseHandle(*retained);
        *retained = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr,
                                OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT,
                                nullptr);
        if (*retained == INVALID_HANDLE_VALUE) *retained = nullptr;
        if (*retained == nullptr) {
            if (error != nullptr) *error = "cannot retain immutable read handle for protected payload: " +
                WideToUtf8(ErrorText(GetLastError()));
            return false;
        }
        if (!HandleHasExpectedType(*retained, false, error)) {
            CloseHandle(*retained);
            *retained = nullptr;
            return false;
        }
        return true;
    }

    void ReleaseSecurePayloadStaging() noexcept {
        if (secure_probe_handle_ != nullptr && secure_probe_handle_ != INVALID_HANDLE_VALUE)
            CloseHandle(secure_probe_handle_);
        if (secure_config_handle_ != nullptr && secure_config_handle_ != INVALID_HANDLE_VALUE)
            CloseHandle(secure_config_handle_);
        if (secure_injector_handle_ != nullptr && secure_injector_handle_ != INVALID_HANDLE_VALUE)
            CloseHandle(secure_injector_handle_);
        if (secure_directory_handle_ != nullptr && secure_directory_handle_ != INVALID_HANDLE_VALUE)
            CloseHandle(secure_directory_handle_);
        secure_probe_handle_ = nullptr;
        secure_config_handle_ = nullptr;
        secure_injector_handle_ = nullptr;
        secure_directory_handle_ = nullptr;
        if (secure_security_descriptor_ != nullptr) LocalFree(secure_security_descriptor_);
        secure_security_descriptor_ = nullptr;

        // Only the exact files created inside our random, protected ProgramData
        // directory are eligible for cleanup. A resident-inactive probe can
        // keep its image open until game exit, so an immediate sharing violation
        // is converted to an explicit reboot cleanup instead of silently losing
        // the protected residual path.
        bool immediate_cleanup = true;
        bool reboot_cleanup_scheduled = false;
        bool cleanup_schedule_failed = false;
        const auto remove_file = [&](const fs::path& path) {
            if (path.empty()) return;
            if (DeleteFileW(path.c_str())) return;
            const DWORD code = GetLastError();
            if (code == ERROR_FILE_NOT_FOUND || code == ERROR_PATH_NOT_FOUND) return;
            immediate_cleanup = false;
            if (MoveFileExW(path.c_str(), nullptr, MOVEFILE_DELAY_UNTIL_REBOOT))
                reboot_cleanup_scheduled = true;
            else
                cleanup_schedule_failed = true;
        };
        remove_file(result_path_);
        remove_file(config_path_);
        remove_file(injector_path_);
        remove_file(probe_path_);
        if (!secure_payload_dir_.empty() && !RemoveDirectoryW(secure_payload_dir_.c_str())) {
            const DWORD code = GetLastError();
            if (code != ERROR_FILE_NOT_FOUND && code != ERROR_PATH_NOT_FOUND) {
                immediate_cleanup = false;
                if (MoveFileExW(secure_payload_dir_.c_str(), nullptr, MOVEFILE_DELAY_UNTIL_REBOOT))
                    reboot_cleanup_scheduled = true;
                else
                    cleanup_schedule_failed = true;
            }
        }
        if (!secure_payload_dir_.empty() && !session_path_.empty()) {
            try {
                const std::string cleanup_status = immediate_cleanup ? "ImmediateCleanupComplete" :
                    (cleanup_schedule_failed ? "ProtectedResidualRequiresAdministratorCleanup" :
                     "CleanupScheduledAtReboot");
                WriteUtf8FileAtomic(session_path_ / L"reports" / L"secure-payload-cleanup.json",
                    MakeJsonObject({
                        {"SchemaVersion", "god2-secure-payload-cleanup-v1"},
                        {"UpdatedAtUtc", UtcNow()},
                        {"Status", cleanup_status},
                        {"ImmediateCleanup", immediate_cleanup ? "true" : "false"},
                        {"RebootCleanupScheduled", reboot_cleanup_scheduled ? "true" : "false"},
                        {"ProtectedResidualPath", immediate_cleanup ? "" :
                            WideToUtf8(secure_payload_dir_.wstring())}
                    }, {"ImmediateCleanup", "RebootCleanupScheduled"}) + "\n");
            } catch (...) {
            }
        }
        result_path_.clear();
        config_path_.clear();
        injector_path_.clear();
        probe_path_.clear();
        secure_payload_dir_.clear();
    }

    void CloseUntransferredRemoteHandle(HANDLE* remote) noexcept {
        if (remote == nullptr || *remote == nullptr) return;
        if (shared_target_process_ != nullptr) {
            HANDLE reclaimed = nullptr;
            if (DuplicateHandle(shared_target_process_, *remote, GetCurrentProcess(),
                                &reclaimed, 0, FALSE,
                                DUPLICATE_SAME_ACCESS | DUPLICATE_CLOSE_SOURCE) &&
                reclaimed != nullptr) {
                CloseHandle(reclaimed);
            }
        }
        *remote = nullptr;
    }

    void CloseUntransferredSharedTransportHandles() noexcept {
        CloseUntransferredRemoteHandle(&shared_remote_event_);
        CloseUntransferredRemoteHandle(&shared_remote_mapping_);
        if (shared_target_process_ != nullptr) CloseHandle(shared_target_process_);
        shared_target_process_ = nullptr;
    }

    void MarkSharedTransportHandlesTransferred() noexcept {
        // God2TraceProbeWaitReady is emitted only after the DLL has mapped the
        // duplicated section and adopted both target-local handles.  From this
        // point the probe closes them during its Stop handshake; the GUI must
        // not invalidate live handles underneath the medium-IL target.
        shared_remote_mapping_ = nullptr;
        shared_remote_event_ = nullptr;
        if (shared_target_process_ != nullptr) CloseHandle(shared_target_process_);
        shared_target_process_ = nullptr;
    }

    bool StartSharedTransport(const fs::path& enhanced_dir, HANDLE target_process,
                              std::string* error) {
        StopSharedTransport();
        shared_mapping_name_.clear();
        shared_event_name_.clear();
        shared_output_path_ = session_path_ / L"raw" / L"semantic-events.jsonl";
        shared_health_path_ = session_path_ / L"reports" / L"semantic-shared-ring.json";
        shared_segment_directory_ = session_path_ / L"raw" / L"semantic-segments";
        shared_segment_manifest_path_ = session_path_ / L"reports" /
            L"semantic-segment-manifest.json";
        shared_segments_.clear();
        shared_last_sequence_ = 0;
        shared_sequence_continuity_ = true;

        if (target_process == nullptr || target_process == INVALID_HANDLE_VALUE ||
            WaitForSingleObject(target_process, 0) != WAIT_TIMEOUT ||
            !DuplicateHandle(GetCurrentProcess(), target_process, GetCurrentProcess(),
                             &shared_target_process_, 0, FALSE, DUPLICATE_SAME_ACCESS)) {
            if (error != nullptr) *error =
                "cannot retain the exact target process for handle-bound semantic IPC";
            StopSharedTransport();
            return false;
        }

        std::error_code directory_error;
        fs::create_directories(enhanced_dir, directory_error);
        if (directory_error) {
            if (error != nullptr) *error = "cannot create shared semantic transport directory: " +
                directory_error.message();
            return false;
        }
        {
            std::error_code existing_error;
            const auto existing_bytes = fs::exists(shared_output_path_, existing_error) ?
                fs::file_size(shared_output_path_, existing_error) : 0u;
            if (existing_error || existing_bytes != 0u) {
                if (error != nullptr) *error =
                    "refusing to overwrite an existing semantic transport output";
                return false;
            }
            std::ofstream output(shared_output_path_, std::ios::binary | std::ios::trunc);
            if (!output) {
                if (error != nullptr) *error = "cannot open the x64 semantic transport output";
                return false;
            }
        }
        fs::create_directories(shared_segment_directory_, directory_error);
        if (directory_error) {
            if (error != nullptr) *error = "cannot create semantic segment directory: " +
                directory_error.message();
            return false;
        }

        shared_mapping_ = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0,
            static_cast<DWORD>(sizeof(god2::shared::SemanticSharedRing)), nullptr);
        if (shared_mapping_ == nullptr) {
            if (error != nullptr) *error = "cannot create the bounded semantic shared-memory mapping";
            StopSharedTransport();
            return false;
        }
        shared_ring_ = static_cast<god2::shared::SemanticSharedRing*>(MapViewOfFile(
            shared_mapping_, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0,
            sizeof(god2::shared::SemanticSharedRing)));
        if (shared_ring_ == nullptr) {
            if (error != nullptr) *error = "cannot map the bounded semantic shared-memory transport";
            StopSharedTransport();
            return false;
        }
        ZeroMemory(shared_ring_, sizeof(*shared_ring_));
        shared_ring_->magic = god2::shared::kSemanticRingMagic;
        shared_ring_->version = god2::shared::kSemanticRingVersion;
        shared_ring_->header_bytes = static_cast<std::uint32_t>(
            offsetof(god2::shared::SemanticSharedRing, slots));
        shared_ring_->slot_bytes = static_cast<std::uint32_t>(sizeof(god2::shared::SemanticRingSlot));
        shared_ring_->capacity = god2::shared::kSemanticPriorityCount *
            god2::shared::kSemanticLaneSlotCount;
        shared_ring_->domain_count = god2::shared::kSemanticDomainCount;
        shared_ring_->lane_capacity = god2::shared::kSemanticLaneSlotCount;
        shared_ring_->lane_count = god2::shared::kSemanticPriorityCount;
        InterlockedExchange(&shared_ring_->consumer_ready, 1);
        InterlockedExchange(&shared_ring_->consumer_closed, 0);
        InterlockedExchange(&shared_ring_->consumer_failure, 0);

        shared_event_ = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        if (shared_event_ == nullptr) {
            if (error != nullptr) *error = "cannot create the semantic shared-memory notification event";
            StopSharedTransport();
            return false;
        }
        if (!DuplicateHandle(GetCurrentProcess(), shared_mapping_, target_process,
                             &shared_remote_mapping_, 0, FALSE, DUPLICATE_SAME_ACCESS) ||
            !DuplicateHandle(GetCurrentProcess(), shared_event_, target_process,
                             &shared_remote_event_, 0, FALSE, DUPLICATE_SAME_ACCESS) ||
            reinterpret_cast<std::uintptr_t>(shared_remote_mapping_) > MAXDWORD ||
            reinterpret_cast<std::uintptr_t>(shared_remote_event_) > MAXDWORD) {
            if (error != nullptr) *error =
                "cannot duplicate x86-compatible semantic IPC handles into the exact target";
            StopSharedTransport();
            return false;
        }
        shared_transport_stop_.store(false, std::memory_order_release);
        shared_transport_ready_.store(true, std::memory_order_release);
        shared_consumer_io_failure_.store(false, std::memory_order_release);
        shared_consumed_.store(0, std::memory_order_release);
        shared_invalid_.store(0, std::memory_order_release);
        try {
            shared_consumer_thread_ = std::thread([this]() { ConsumeSharedTransport(); });
        } catch (const std::exception& exception) {
            if (error != nullptr) *error = "cannot start the x64 semantic transport consumer: " +
                std::string(exception.what());
            StopSharedTransport();
            return false;
        }
        WriteSharedTransportHealth("WaitingForProducer");
        return true;
    }

    void ConsumeSharedTransport() noexcept {
        constexpr std::uint64_t kSegmentMaximumBytes = 16u * 1024u * 1024u;
        constexpr std::uint64_t kSegmentMaximumRecords = 100000u;
        std::ofstream segment_output;
        SharedSemanticSegmentEvidence current_segment;
        std::uint32_t segment_index = 0;
        const auto finish_segment = [&]() {
            if (!segment_output.is_open()) return;
            segment_output.flush();
            const bool stream_ok = static_cast<bool>(segment_output);
            segment_output.close();
            const auto hash = stream_ok ? CalculateFileSha256(current_segment.path) :
                std::optional<std::string>{};
            if (!hash || current_segment.records == 0) {
                shared_consumer_io_failure_.store(true, std::memory_order_release);
                return;
            }
            current_segment.sha256 = *hash;
            shared_segments_.push_back(current_segment);
        };
        const auto start_segment = [&]() -> bool {
            do {
                ++segment_index;
                wchar_t name[64]{};
                swprintf_s(name, L"semantic-%06lu.jsonl",
                    static_cast<unsigned long>(segment_index));
                current_segment = {};
                current_segment.path = shared_segment_directory_ / name;
                std::error_code exists_error;
                if (!fs::exists(current_segment.path, exists_error) && !exists_error)
                    break;
                if (exists_error) return false;
            } while (true);
            segment_output.open(current_segment.path, std::ios::binary | std::ios::out);
            return static_cast<bool>(segment_output);
        };
        if (!start_segment())
            shared_consumer_io_failure_.store(true, std::memory_order_release);
        for (;;) {
            if (shared_event_ != nullptr) WaitForSingleObject(shared_event_, 250);
            for (std::uint32_t consumed_this_pass = 0;
                 shared_ring_ != nullptr && consumed_this_pass <
                    god2::shared::kSemanticPriorityCount *
                        god2::shared::kSemanticLaneSlotCount;
                 ++consumed_this_pass) {
                const LONG read = InterlockedCompareExchange(&shared_ring_->read_sequence, 0, 0);
                const LONG write = InterlockedCompareExchange(&shared_ring_->write_sequence, 0, 0);
                if (read >= write) break;
                const LONG expected_sequence = read + 1;
                god2::shared::SemanticRingSlot* selected_slot = nullptr;
                god2::shared::SemanticPriorityLane* selected_lane = nullptr;
                LONG selected_lane_read = 0;
                for (std::uint32_t priority = 0;
                     priority < god2::shared::kSemanticPriorityCount; ++priority) {
                    auto& lane = god2::shared::SemanticLane(*shared_ring_, priority);
                    const LONG lane_read = InterlockedCompareExchange(
                        &lane.read_sequence, 0, 0);
                    const LONG lane_write = InterlockedCompareExchange(
                        &lane.write_sequence, 0, 0);
                    if (lane_read >= lane_write) continue;
                    auto& candidate = god2::shared::SemanticLaneSlot(
                        *shared_ring_, priority, lane_read);
                    if (InterlockedCompareExchange(&candidate.state, 2, 2) == 2 &&
                        candidate.sequence == static_cast<std::uint32_t>(expected_sequence)) {
                        selected_slot = &candidate;
                        selected_lane = &lane;
                        selected_lane_read = lane_read;
                        break;
                    }
                }
                if (selected_slot == nullptr || selected_lane == nullptr) break;
                auto& slot = *selected_slot;
                // Snapshot the producer-owned routing fields before releasing
                // this slot. Once state becomes free and read_sequence advances,
                // the producer may immediately reuse and overwrite the slot.
                const std::uint32_t consumed_domain = slot.domain;
                const bool valid = slot.payload_bytes > 0 &&
                    slot.payload_bytes < god2::shared::kSemanticPayloadBytes &&
                    consumed_domain < god2::shared::kSemanticDomainCount &&
                    god2::shared::IsValidSemanticPriority(slot.priority);
                const std::uint64_t record_bytes = valid ? slot.payload_bytes +
                    (slot.payload[slot.payload_bytes - 1] == '\n' ? 0u : 1u) : 0u;
                if (valid && segment_output && current_segment.records != 0 &&
                    (current_segment.bytes + record_bytes > kSegmentMaximumBytes ||
                     current_segment.records >= kSegmentMaximumRecords)) {
                    finish_segment();
                    if (!start_segment())
                        shared_consumer_io_failure_.store(true, std::memory_order_release);
                }
                if (valid && segment_output) {
                    segment_output.write(slot.payload,
                        static_cast<std::streamsize>(slot.payload_bytes));
                    if (slot.payload[slot.payload_bytes - 1] != '\n') segment_output.put('\n');
                    if (!segment_output) {
                        shared_consumer_io_failure_.store(true, std::memory_order_release);
                    } else {
                        if (current_segment.records == 0)
                            current_segment.first_sequence = slot.sequence;
                        if (shared_last_sequence_ != 0 &&
                            slot.sequence != shared_last_sequence_ + 1u)
                            shared_sequence_continuity_ = false;
                        shared_last_sequence_ = slot.sequence;
                        current_segment.last_sequence = slot.sequence;
                        ++current_segment.records;
                        current_segment.bytes += record_bytes;
                        shared_consumed_.fetch_add(1, std::memory_order_acq_rel);
                    }
                } else {
                    shared_invalid_.fetch_add(1, std::memory_order_acq_rel);
                }
                if (!ConsumeSemanticDomainPending(shared_ring_, consumed_domain))
                    shared_invalid_.fetch_add(1, std::memory_order_acq_rel);
                MemoryBarrier();
                InterlockedExchange(&slot.state, 0);
                const LONG next_read = expected_sequence;
                InterlockedExchange(&shared_ring_->read_sequence, next_read);
                const LONG lag = std::max<LONG>(0,
                    InterlockedCompareExchange(&shared_ring_->write_sequence, 0, 0) - next_read);
                InterlockedExchange(&shared_ring_->consumer_lag, lag);
                const LONG next_lane_read = selected_lane_read + 1;
                InterlockedExchange(&selected_lane->read_sequence, next_lane_read);
                const LONG lane_lag = std::max<LONG>(0,
                    InterlockedCompareExchange(&selected_lane->write_sequence, 0, 0) -
                        next_lane_read);
                InterlockedExchange(&selected_lane->consumer_lag, lane_lag);
            }
            if (shared_ring_ != nullptr &&
                shared_consumer_io_failure_.load(std::memory_order_acquire))
                InterlockedExchange(&shared_ring_->consumer_failure, 1);
            if (shared_transport_stop_.load(std::memory_order_acquire)) break;
        }
        finish_segment();

        std::ofstream merged(shared_output_path_, std::ios::binary | std::ios::app);
        std::uint64_t merged_records = 0;
        for (const auto& segment : shared_segments_) {
            std::ifstream input(segment.path, std::ios::binary);
            if (!input || !merged) {
                shared_consumer_io_failure_.store(true, std::memory_order_release);
                break;
            }
            merged << input.rdbuf();
            if (!merged) {
                shared_consumer_io_failure_.store(true, std::memory_order_release);
                break;
            }
            merged_records += segment.records;
        }
        merged.flush();
        if (!merged || merged_records !=
                shared_consumed_.load(std::memory_order_acquire))
            shared_consumer_io_failure_.store(true, std::memory_order_release);
        merged.close();

        std::ostringstream segment_rows;
        segment_rows << '[';
        for (std::size_t index = 0; index < shared_segments_.size(); ++index) {
            if (index != 0) segment_rows << ',';
            const auto& segment = shared_segments_[index];
            segment_rows << MakeJsonObject({
                {"Index", std::to_string(index)},
                {"Path", "raw/semantic-segments/" +
                    WideToUtf8(segment.path.filename().wstring())},
                {"Records", std::to_string(segment.records)},
                {"Bytes", std::to_string(segment.bytes)},
                {"FirstSequence", std::to_string(segment.first_sequence)},
                {"LastSequence", std::to_string(segment.last_sequence)},
                {"SHA256", segment.sha256}
            }, {"Index", "Records", "Bytes", "FirstSequence", "LastSequence"});
        }
        segment_rows << ']';
        const auto merged_hash = CalculateFileSha256(shared_output_path_).value_or("");
        if (!shared_sequence_continuity_)
            shared_consumer_io_failure_.store(true, std::memory_order_release);
        const bool segment_manifest_complete =
            !shared_consumer_io_failure_.load(std::memory_order_acquire) &&
            !shared_segments_.empty() && !merged_hash.empty() &&
            shared_sequence_continuity_;
        const auto segment_manifest = MakeJsonObject({
            {"SchemaVersion", "god2-semantic-segment-manifest-v1"},
            {"GeneratedAtUtc", UtcNow()},
            {"Status", segment_manifest_complete ? "PASS" :
                "EVIDENCE_BLOCKED_SEGMENT_OR_CONTINUITY_FAILURE"},
            {"SegmentMaximumBytes", std::to_string(kSegmentMaximumBytes)},
            {"SegmentMaximumRecords", std::to_string(kSegmentMaximumRecords)},
            {"SegmentCount", std::to_string(shared_segments_.size())},
            {"RecordCount", std::to_string(merged_records)},
            {"SequenceContinuity", shared_sequence_continuity_ ? "true" : "false"},
            {"MergedPath", "raw/semantic-events.jsonl"},
            {"MergedSHA256", merged_hash},
            {"Segments", segment_rows.str()}
        }, {"SegmentMaximumBytes", "SegmentMaximumRecords", "SegmentCount",
            "RecordCount", "SequenceContinuity", "Segments"});
        if (!WriteUtf8FileAtomic(shared_segment_manifest_path_,
                segment_manifest + "\n"))
            shared_consumer_io_failure_.store(true, std::memory_order_release);
        if (shared_ring_ != nullptr) {
            if (shared_consumer_io_failure_.load(std::memory_order_acquire))
                InterlockedExchange(&shared_ring_->consumer_failure, 1);
            InterlockedExchange(&shared_ring_->consumer_ready, 0);
            InterlockedExchange(&shared_ring_->consumer_closed, 1);
        }
    }

    void WriteSharedTransportHealth(std::string_view lifecycle) const {
        if (shared_health_path_.empty()) return;
        std::vector<std::pair<std::string, std::string>> values{
            {"SchemaVersion", "god2-semantic-shared-ring-health-v4"},
            {"UpdatedAtUtc", UtcNow()},
            {"Lifecycle", std::string(lifecycle)},
            {"TransportReady", shared_transport_ready_.load(std::memory_order_acquire) ? "true" : "false"},
            {"ConsumerIoFailure", shared_consumer_io_failure_.load(std::memory_order_acquire) ? "true" : "false"},
            {"AttachedTargetProcessId", std::to_string(attached_pid_)},
            {"Consumed", std::to_string(shared_consumed_.load(std::memory_order_acquire))},
            {"InvalidPayloads", std::to_string(shared_invalid_.load(std::memory_order_acquire))}
        };
        std::set<std::string> unquoted{"TransportReady", "ConsumerIoFailure", "AttachedTargetProcessId",
                                       "Consumed", "InvalidPayloads"};
        if (shared_ring_ != nullptr) {
            values.emplace_back("ProducerReady", std::to_string(
                InterlockedCompareExchange(&shared_ring_->producer_ready, 0, 0)));
            values.emplace_back("ProducerClosed", std::to_string(
                InterlockedCompareExchange(&shared_ring_->producer_closed, 0, 0)));
            values.emplace_back("ConsumerReady", std::to_string(
                InterlockedCompareExchange(&shared_ring_->consumer_ready, 0, 0)));
            values.emplace_back("ConsumerClosed", std::to_string(
                InterlockedCompareExchange(&shared_ring_->consumer_closed, 0, 0)));
            values.emplace_back("ConsumerFailure", std::to_string(
                InterlockedCompareExchange(&shared_ring_->consumer_failure, 0, 0)));
            values.emplace_back("ProducerProcessId", std::to_string(shared_ring_->producer_process_id));
            values.emplace_back("Attempted", std::to_string(
                InterlockedCompareExchange(&shared_ring_->attempted_sequence, 0, 0)));
            values.emplace_back("Accepted", std::to_string(
                InterlockedCompareExchange(&shared_ring_->write_sequence, 0, 0)));
            values.emplace_back("Dropped", std::to_string(
                InterlockedCompareExchange(&shared_ring_->dropped_total, 0, 0)));
            values.emplace_back("Sampled", std::to_string(
                InterlockedCompareExchange(&shared_ring_->sampled_total, 0, 0)));
            values.emplace_back("HighWaterMark", std::to_string(
                InterlockedCompareExchange(&shared_ring_->high_water_mark, 0, 0)));
            values.emplace_back("ConsumerLag", std::to_string(
                InterlockedCompareExchange(&shared_ring_->consumer_lag, 0, 0)));
            unquoted.insert({"ProducerReady", "ProducerClosed", "ConsumerReady",
                "ConsumerClosed", "ConsumerFailure", "ProducerProcessId", "Attempted",
                "Accepted", "Dropped", "Sampled", "HighWaterMark", "ConsumerLag"});
            for (std::uint32_t priority = 0;
                 priority < god2::shared::kSemanticPriorityCount; ++priority) {
                const std::string prefix = "Lane0" + std::to_string(priority);
                auto& lane = god2::shared::SemanticLane(*shared_ring_, priority);
                const std::array<std::pair<std::string, LONG>, 9> lane_values{{
                    {prefix + "Accepted", InterlockedCompareExchange(&lane.write_sequence, 0, 0)},
                    {prefix + "Consumed", InterlockedCompareExchange(&lane.read_sequence, 0, 0)},
                    {prefix + "Dropped", InterlockedCompareExchange(&lane.dropped, 0, 0)},
                    {prefix + "Sampled", InterlockedCompareExchange(&lane.sampled, 0, 0)},
                    {prefix + "FirstDroppedSequence", InterlockedCompareExchange(&lane.first_dropped_sequence, 0, 0)},
                    {prefix + "LastDroppedSequence", InterlockedCompareExchange(&lane.last_dropped_sequence, 0, 0)},
                    {prefix + "LastDropReason", InterlockedCompareExchange(&lane.last_drop_reason, 0, 0)},
                    {prefix + "HighWaterMark", InterlockedCompareExchange(&lane.high_water_mark, 0, 0)},
                    {prefix + "ConsumerLag", InterlockedCompareExchange(&lane.consumer_lag, 0, 0)}
                }};
                for (const auto& [key, value] : lane_values) {
                    values.emplace_back(key, std::to_string(value));
                    unquoted.insert(key);
                }
            }
            LONG domain_write_failures_total = 0;
            for (std::uint32_t domain = 0; domain < god2::shared::kSemanticDomainCount; ++domain) {
                const std::string prefix = "Domain" + (domain < 10 ? std::string("0") : std::string()) +
                    std::to_string(domain);
                auto& counters = shared_ring_->domains[domain];
                const LONG domain_write_failures =
                    InterlockedCompareExchange(&counters.write_failures, 0, 0);
                domain_write_failures_total += domain_write_failures;
                const std::array<std::pair<std::string, LONG>, 8> domain_values{{
                    {prefix + "Accepted", InterlockedCompareExchange(&counters.accepted, 0, 0)},
                    {prefix + "Dropped", InterlockedCompareExchange(&counters.dropped, 0, 0)},
                    {prefix + "FirstDroppedSequence", InterlockedCompareExchange(&counters.first_dropped_sequence, 0, 0)},
                    {prefix + "LastDroppedSequence", InterlockedCompareExchange(&counters.last_dropped_sequence, 0, 0)},
                    {prefix + "LastDropReason", InterlockedCompareExchange(&counters.last_drop_reason, 0, 0)},
                    {prefix + "HighWaterMark", InterlockedCompareExchange(&counters.high_water_mark, 0, 0)},
                    {prefix + "ConsumerLag", InterlockedCompareExchange(&counters.consumer_lag, 0, 0)},
                    {prefix + "WriteFailures", domain_write_failures}
                }};
                for (const auto& [key, value] : domain_values) {
                    values.emplace_back(key, std::to_string(value));
                    unquoted.insert(key);
                }
            }
            values.emplace_back("WriteFailures", std::to_string(domain_write_failures_total));
            unquoted.insert("WriteFailures");
        }
        WriteUtf8FileAtomic(shared_health_path_, MakeJsonObject(values, unquoted) + "\n");
    }

    void StopSharedTransport() noexcept {
        const bool had_transport = shared_ring_ != nullptr || shared_mapping_ != nullptr ||
            shared_event_ != nullptr || shared_consumer_thread_.joinable() ||
            shared_target_process_ != nullptr || shared_remote_mapping_ != nullptr ||
            shared_remote_event_ != nullptr;
        if (!had_transport) return;
        shared_transport_stop_.store(true, std::memory_order_release);
        if (shared_event_ != nullptr) SetEvent(shared_event_);
        if (shared_consumer_thread_.joinable()) shared_consumer_thread_.join();
        shared_transport_ready_.store(false, std::memory_order_release);
        bool complete_and_priority_safe = shared_ring_ != nullptr && attached_pid_ != 0 &&
            !shared_consumer_io_failure_.load(std::memory_order_acquire) &&
            shared_invalid_.load(std::memory_order_acquire) == 0;
        if (shared_ring_ != nullptr) {
            const LONG producer_ready = InterlockedCompareExchange(&shared_ring_->producer_ready, 0, 0);
            const LONG producer_closed = InterlockedCompareExchange(&shared_ring_->producer_closed, 0, 0);
            const LONG attempted = InterlockedCompareExchange(&shared_ring_->attempted_sequence, 0, 0);
            const LONG accepted = InterlockedCompareExchange(&shared_ring_->write_sequence, 0, 0);
            const LONG dropped = InterlockedCompareExchange(&shared_ring_->dropped_total, 0, 0);
            const LONG sampled = InterlockedCompareExchange(&shared_ring_->sampled_total, 0, 0);
            const LONG lag = InterlockedCompareExchange(&shared_ring_->consumer_lag, 0, 0);
            const LONG consumer_ready = InterlockedCompareExchange(&shared_ring_->consumer_ready, 0, 0);
            const LONG consumer_closed = InterlockedCompareExchange(&shared_ring_->consumer_closed, 0, 0);
            const LONG consumer_failure = InterlockedCompareExchange(&shared_ring_->consumer_failure, 0, 0);
            const auto consumed = shared_consumed_.load(std::memory_order_acquire);
            complete_and_priority_safe = complete_and_priority_safe &&
                producer_ready == 1 && producer_closed == 1 && consumer_ready == 0 &&
                consumer_closed == 1 && consumer_failure == 0 &&
                shared_ring_->producer_process_id == attached_pid_ && attempted >= 0 && accepted >= 0 &&
                dropped >= 0 && sampled >= 0 && dropped == sampled &&
                attempted == accepted + dropped &&
                consumed == static_cast<std::uint64_t>(accepted) && lag == 0;
            std::int64_t lane_accepted_total = 0;
            std::int64_t lane_dropped_total = 0;
            std::int64_t lane_sampled_total = 0;
            for (std::uint32_t priority = 0;
                 priority < god2::shared::kSemanticPriorityCount; ++priority) {
                auto& lane = god2::shared::SemanticLane(*shared_ring_, priority);
                const LONG lane_accepted = InterlockedCompareExchange(
                    &lane.write_sequence, 0, 0);
                const LONG lane_consumed = InterlockedCompareExchange(
                    &lane.read_sequence, 0, 0);
                const LONG lane_dropped = InterlockedCompareExchange(&lane.dropped, 0, 0);
                const LONG lane_sampled = InterlockedCompareExchange(&lane.sampled, 0, 0);
                const LONG first_dropped = InterlockedCompareExchange(
                    &lane.first_dropped_sequence, 0, 0);
                const LONG last_dropped = InterlockedCompareExchange(
                    &lane.last_dropped_sequence, 0, 0);
                const LONG last_reason = InterlockedCompareExchange(
                    &lane.last_drop_reason, 0, 0);
                const LONG lane_lag = InterlockedCompareExchange(&lane.consumer_lag, 0, 0);
                const bool high_priority = priority <= static_cast<std::uint32_t>(
                    god2::shared::SemanticPriority::ObjectEvidence);
                const bool drop_ledger_valid = high_priority ?
                    lane_dropped == 0 && lane_sampled == 0 && first_dropped == 0 &&
                        last_dropped == 0 && last_reason == 0 :
                    lane_dropped == lane_sampled &&
                        ((lane_dropped == 0 && first_dropped == 0 && last_dropped == 0 &&
                          last_reason == 0) ||
                         (lane_dropped > 0 && first_dropped > 0 &&
                          last_dropped >= first_dropped && last_reason == static_cast<LONG>(
                            god2::shared::SemanticDropReason::SamplingPolicy)));
                complete_and_priority_safe = complete_and_priority_safe &&
                    lane_accepted >= 0 && lane_consumed == lane_accepted && lane_lag == 0 &&
                    drop_ledger_valid;
                lane_accepted_total += lane_accepted;
                lane_dropped_total += lane_dropped;
                lane_sampled_total += lane_sampled;
            }
            complete_and_priority_safe = complete_and_priority_safe &&
                lane_accepted_total == accepted && lane_dropped_total == dropped &&
                lane_sampled_total == sampled;
            std::int64_t domain_accepted_total = 0;
            std::int64_t domain_dropped_total = 0;
            for (std::uint32_t domain = 0; domain < god2::shared::kSemanticDomainCount; ++domain) {
                auto& counters = shared_ring_->domains[domain];
                const LONG domain_accepted = InterlockedCompareExchange(&counters.accepted, 0, 0);
                const LONG domain_dropped = InterlockedCompareExchange(&counters.dropped, 0, 0);
                const LONG first_dropped = InterlockedCompareExchange(
                    &counters.first_dropped_sequence, 0, 0);
                const LONG last_dropped = InterlockedCompareExchange(
                    &counters.last_dropped_sequence, 0, 0);
                const LONG last_reason = InterlockedCompareExchange(
                    &counters.last_drop_reason, 0, 0);
                domain_accepted_total += domain_accepted;
                domain_dropped_total += domain_dropped;
                const bool domain_drop_ledger_valid =
                    (domain_dropped == 0 && first_dropped == 0 && last_dropped == 0 &&
                     last_reason == 0) ||
                    (domain_dropped > 0 && first_dropped > 0 && last_dropped >= first_dropped &&
                     last_reason == static_cast<LONG>(
                        god2::shared::SemanticDropReason::SamplingPolicy));
                complete_and_priority_safe = complete_and_priority_safe &&
                    domain_accepted >= 0 && domain_dropped >= 0 && domain_drop_ledger_valid &&
                    InterlockedCompareExchange(&counters.consumer_lag, 0, 0) == 0 &&
                    InterlockedCompareExchange(&counters.write_failures, 0, 0) == 0;
            }
            complete_and_priority_safe = complete_and_priority_safe &&
                domain_accepted_total == static_cast<std::int64_t>(accepted) &&
                domain_dropped_total == static_cast<std::int64_t>(dropped);
        }
        WriteSharedTransportHealth(complete_and_priority_safe ?
            "StoppedAndDrained" : "EvidenceBlockedSemanticEvidenceIncomplete");
        CloseUntransferredSharedTransportHandles();
        if (shared_ring_ != nullptr) UnmapViewOfFile(shared_ring_);
        if (shared_event_ != nullptr) CloseHandle(shared_event_);
        if (shared_mapping_ != nullptr) CloseHandle(shared_mapping_);
        shared_ring_ = nullptr;
        shared_event_ = nullptr;
        shared_mapping_ = nullptr;
        shared_mapping_name_.clear();
        shared_event_name_.clear();
        shared_health_path_.clear();
        shared_segment_directory_.clear();
        shared_segment_manifest_path_.clear();
    }

    bool Fail(const std::string& detail, std::string* error,
              std::string_view failure_kind = "AttachFailure") {
        StopSharedTransport();
        failure_detail_ = detail;
        failure_kind_ = std::string(failure_kind);
        WriteStatus("EvidenceBlocked", detail);
        if (error != nullptr) *error = detail;
        AppendStartupLog(L"EnhancedCapture", Utf8ToWide(detail));
        if (monitor_process_ == nullptr) ReleaseSecurePayloadStaging();
        return false;
    }

    bool ObserveTrackedTargetProcessExit() noexcept {
        if (target_process_exited_) return true;
        if (target_process_identity_handle_ != nullptr &&
            WaitForSingleObject(target_process_identity_handle_, 0) == WAIT_OBJECT_0)
            target_process_exited_ = true;
        return target_process_exited_;
    }

    void ApplyInjectorResult(const EnhancedInjectorResultV2& result) noexcept {
        injection_attempted_ = result.injection_attempted;
        module_was_ever_loaded_ = result.module_was_ever_loaded;
        module_load_state_verified_ = result.module_load_state_verified;
        module_snapshot_verified_ = result.module_snapshot_verified;
        module_absent_ = result.module_absent;
        target_identity_verified_ = result.target_identity_verified;
        probe_ready_ = result.probe_ready;
        if (result.target_process_exited && ObserveTrackedTargetProcessExit())
            target_process_exited_ = true;
        const bool detach_verified = IsVerifiedEnhancedDetachResult(
            result, ObserveTrackedTargetProcessExit());
        const bool no_module_verified = IsVerifiedEnhancedNoModuleLoadedResult(result);
        const bool loaded_failure_cleanup_verified =
            IsVerifiedEnhancedLoadedFailureCleanupResult(result);
        safe_detach_observed_ = detach_verified || no_module_verified ||
            loaded_failure_cleanup_verified;
    }

    std::optional<EnhancedInjectorResultV2> ReadInjectorResult() const {
        if (result_path_.empty()) return std::nullopt;
        const auto wire = ReadUtf8File(result_path_);
        return wire ? ParseEnhancedInjectorResultV2(*wire) : std::nullopt;
    }

    void WriteStatus(std::string_view status, std::string_view detail) const {
        if (session_path_.empty()) return;
        std::error_code file_error;
        const bool injector_present = !injector_path_.empty() &&
            fs::is_regular_file(injector_path_, file_error) && !file_error;
        file_error.clear();
        const bool probe_present = !probe_path_.empty() &&
            fs::is_regular_file(probe_path_, file_error) && !file_error;
        const bool probe_ready = probe_ready_ && attached_pid_ != 0 && monitor_process_ != nullptr &&
            WaitForSingleObject(monitor_process_, 0) == WAIT_TIMEOUT &&
            !stop_requested_.load();
        const auto report = MakeJsonObject({
            {"SchemaVersion", "god2-enhanced-capture-status-v2"},
            {"UpdatedAtUtc", UtcNow()}, {"Requested", requested_ ? "true" : "false"},
            {"Status", std::string(status)}, {"Detail", std::string(detail)},
            {"WasEverAttached", ever_attached_ ? "true" : "false"},
            {"ProbeReady", probe_ready ? "true" : "false"},
            {"StrictUnloadVerified", safe_detach_observed_ ? "true" : "false"},
            {"InjectionAttempted", injection_attempted_ ? "true" : "false"},
            {"ModuleWasEverLoaded", module_was_ever_loaded_ ? "true" : "false"},
            {"ModuleLoadStateVerified", module_load_state_verified_ ? "true" : "false"},
            {"ModuleSnapshotVerified", module_snapshot_verified_ ? "true" : "false"},
            {"ModuleAbsent", module_absent_ ? "true" : "false"},
            {"TargetProcessExited", target_process_exited_ ? "true" : "false"},
            {"TargetIdentityVerified", target_identity_verified_ ? "true" : "false"},
            {"FailureKind", failure_kind_},
            {"InjectorPresent", injector_present ? "true" : "false"},
            {"ProbePresent", probe_present ? "true" : "false"},
            {"InjectorPayloadValidated", injector_payload_validated_ ? "true" : "false"},
            {"ProbePayloadValidated", probe_payload_validated_ ? "true" : "false"},
            {"TargetExecutable", "God2_opt.exe"}, {"TargetArchitecture", "x86"},
            {"ProbeScope", "WinsockTransport+PostDecrypt+PreEncrypt+HandlerDecoded"},
            {"ProbeDomainCount", "25"},
            {"ProbeDomains", "Network,Parser,Serializer,Handler,Object,Allocation,VTable,Factory,ManagerLookup,Registry,ResourceDecode,Mutation,TaintSeed,FormulaOperand,Quest,Map,Portal,NPC,Monster,Battle,Inventory,Item,Skill,Pet/Mount,Snapshot"},
            {"ConfirmedExactBuildDomainCount", "4"},
            {"ConfirmedExactBuildDomains", "Network,Parser,Serializer,Handler"},
            {"CandidateOnlyDomainCount", "21"},
            {"CandidateOnlyStatus", "EvidenceBlockedUnconfirmedProbe"},
            {"DeepProbeCandidateMapSchema", "god2-deep-probe-candidate-map-v2"},
            {"CandidatePromotionGateCount", "10"},
            {"CandidateAbiSafetyGateCount", "4"},
            {"CandidateActivationGateCount", "14"},
            {"CandidateAbiSafetyContract", "ArgumentContract;ReturnValueLifetime;ThreadContext;ReentrancyRisk"},
            {"SemanticEventContract", "God2SemanticEvent;SchemaVersion=2;StrictTypes;25EventTypes"},
            {"SharedTransportContract", "VersionedBounded25DomainPriorityRing;Priority0AuthoritativeTo3Candidate;BatchPublishing;Sequence;PerDomainDropAndWriteFailureCounters;FirstLastDroppedSequence;Reason;ConsumerLag"},
            {"InjectionSequence", "PauseVerifiedPrimaryThread,RemoteThreadLoadLibraryW,ResumePrimaryThread"},
            {"ReadinessHandshake", "God2TraceProbeWaitReady"},
            {"PayloadStaging", "ProtectedProgramDataRandomDirectory"},
            {"PayloadDacl", "Protected:SYSTEM+AdministratorsFull;CurrentUserReadExecuteOnly;EveryoneAbsent"},
            {"PayloadMandatoryLabel", "MediumNoWriteUp"},
            {"SemanticIpcBinding", "DuplicatedTargetHandlesV2;NoNamedObjectAuthority"},
            {"PayloadLaunchMode", "AlreadyElevatedCreateProcessW"},
            {"UnloadPolicy", "QuiesceRestoreDrainAndProveModuleAbsent;OtherwiseEvidenceBlockedAndBoundedRetry;NeverReportResidentInactiveAsStopped"},
            {"Apis", "send,WSASend,recv,WSARecv,OverlappedWSARecvCompletionRoutine,WSAGetOverlappedResult,GetQueuedCompletionStatus,GetQueuedCompletionStatusEx,PostDecrypt,PreEncrypt,HandlerDecoded"},
            {"InputAndWindowHooks", "Disabled"}
        }, {"Requested", "WasEverAttached", "ProbeReady", "StrictUnloadVerified",
            "InjectionAttempted", "ModuleWasEverLoaded", "ModuleLoadStateVerified",
            "ModuleSnapshotVerified", "ModuleAbsent", "TargetProcessExited",
            "TargetIdentityVerified",
            "InjectorPresent", "ProbePresent", "InjectorPayloadValidated",
            "ProbePayloadValidated",
            "ProbeDomainCount", "ConfirmedExactBuildDomainCount", "CandidateOnlyDomainCount",
            "CandidatePromotionGateCount", "CandidateAbiSafetyGateCount",
            "CandidateActivationGateCount"});
        WriteUtf8FileAtomic(session_path_ / L"reports" / L"enhanced-capture-status.json", report + "\n");
    }

    void CloseMonitorHandles() {
        StopSharedTransport();
        ObserveTrackedTargetProcessExit();
        if (monitor_process_ != nullptr) CloseHandle(monitor_process_);
        if (stop_event_ != nullptr) CloseHandle(stop_event_);
        if (target_process_identity_handle_ != nullptr)
            CloseHandle(target_process_identity_handle_);
        monitor_process_ = nullptr;
        stop_event_ = nullptr;
        target_process_identity_handle_ = nullptr;
        attached_pid_ = 0;
        probe_ready_ = false;
        stop_event_name_.clear();
        ReleaseSecurePayloadStaging();
    }

    void StopNoThrow() noexcept {
        try {
            std::string ignored;
            Stop(&ignored);
        } catch (...) {
        }
    }

    mutable std::mutex mutex_;
    bool requested_ = false;
    bool ever_attached_ = false;
    bool probe_ready_ = false;
    bool safe_detach_observed_ = false;
    bool injection_attempted_ = false;
    bool module_was_ever_loaded_ = false;
    bool module_load_state_verified_ = true;
    bool module_snapshot_verified_ = false;
    bool module_absent_ = true;
    bool target_process_exited_ = false;
    bool target_identity_verified_ = false;
    bool injector_payload_validated_ = false;
    bool probe_payload_validated_ = false;
    std::atomic<bool> stop_requested_{false};
    std::string failure_detail_;
    std::string failure_kind_;
    DWORD attached_pid_ = 0;
    HANDLE target_process_identity_handle_ = nullptr;
    fs::path session_path_;
    std::string session_id_;
    fs::path game_path_;
    fs::path probe_path_;
    fs::path injector_path_;
    fs::path result_path_;
    fs::path config_path_;
    fs::path secure_payload_dir_;
    HANDLE secure_directory_handle_ = nullptr;
    HANDLE secure_injector_handle_ = nullptr;
    HANDLE secure_probe_handle_ = nullptr;
    HANDLE secure_config_handle_ = nullptr;
    PSECURITY_DESCRIPTOR secure_security_descriptor_ = nullptr;
    std::wstring stop_event_name_;
    HANDLE stop_event_ = nullptr;
    HANDLE monitor_process_ = nullptr;
    HANDLE controller_cancel_event_ = nullptr;
    std::wstring shared_mapping_name_;
    std::wstring shared_event_name_;
    fs::path shared_output_path_;
    fs::path shared_health_path_;
    fs::path shared_segment_directory_;
    fs::path shared_segment_manifest_path_;
    std::vector<SharedSemanticSegmentEvidence> shared_segments_;
    std::uint32_t shared_last_sequence_ = 0;
    bool shared_sequence_continuity_ = true;
    HANDLE shared_mapping_ = nullptr;
    HANDLE shared_event_ = nullptr;
    HANDLE shared_target_process_ = nullptr;
    HANDLE shared_remote_mapping_ = nullptr;
    HANDLE shared_remote_event_ = nullptr;
    god2::shared::SemanticSharedRing* shared_ring_ = nullptr;
    std::thread shared_consumer_thread_;
    std::atomic<bool> shared_transport_stop_{false};
    std::atomic<bool> shared_transport_ready_{false};
    std::atomic<bool> shared_consumer_io_failure_{false};
    std::atomic<std::uint64_t> shared_consumed_{0};
    std::atomic<std::uint64_t> shared_invalid_{0};
};

UltimateBundleResult BuildUltimateRecoveryPackage(const fs::path& session_path,
                                                   const fs::path& client_path) {
    UltimateBundleResult bundle;
    UltimateRecoveryInput input;
    Fields session_manifest;
    std::string session_manifest_error;
    const auto session_manifest_text = ReadUtf8File(session_path / L"session.json");
    if (!session_manifest_text ||
        !ParseFlatJson(*session_manifest_text, session_manifest, &session_manifest_error)) {
        bundle.error = "EvidenceBlockedSessionManifestUnavailable";
        return bundle;
    }
    input.session_id = GetString(session_manifest, "SessionId");
    const bool safe_session_id = !input.session_id.empty() && input.session_id.size() <= 128 &&
        std::all_of(input.session_id.begin(), input.session_id.end(), [](unsigned char character) {
            return std::isalnum(character) != 0 || character == '-' || character == '_' ||
                character == '.';
        });
    if (!safe_session_id) {
        bundle.error = "EvidenceBlockedSessionIdentityInvalid";
        return bundle;
    }
    std::uint64_t rejected_lines = 0;
    const fs::path semantic_path = session_path / L"raw" / L"semantic-events.jsonl";
    std::ifstream semantic_stream(semantic_path, std::ios::binary);
    std::string line;
    while (std::getline(semantic_stream, line)) {
        if (!line.empty() && line.back() == '\r') line.pop_back();
        if (line.empty()) continue;
        auto read = ReadSemanticEvent(line);
        if (read.success && read.event.session_id == input.session_id &&
            read.event.process_id != 0 && !read.event.client_build_id.empty())
            input.semantic_events.push_back(std::move(read.event));
        else ++rejected_lines;
    }
    input.semantic_evidence_incomplete = rejected_lines != 0 || input.semantic_events.empty();

    const fs::path health_path = session_path / L"reports" / L"semantic-shared-ring.json";
    Fields health;
    std::string health_parse_error;
    if (const auto health_json = ReadUtf8File(health_path)) {
        if (!ParseFlatJson(*health_json, health, &health_parse_error)) {
            input.semantic_evidence_incomplete = true;
        } else {
            const auto transport_ready = GetBool(health, "TransportReady");
            const auto consumer_io_failure = GetBool(health, "ConsumerIoFailure");
            const auto producer_ready = GetInt64(health, "ProducerReady");
            const auto producer_closed = GetInt64(health, "ProducerClosed");
            const auto producer_pid = GetInt64(health, "ProducerProcessId");
            const auto attached_pid = GetInt64(health, "AttachedTargetProcessId");
            const auto attempted = GetInt64(health, "Attempted");
            const auto accepted = GetInt64(health, "Accepted");
            const auto dropped = GetInt64(health, "Dropped");
            const auto sampled = GetInt64(health, "Sampled");
            const auto consumed = GetInt64(health, "Consumed");
            const auto invalid = GetInt64(health, "InvalidPayloads");
            const auto high_water = GetInt64(health, "HighWaterMark");
            const auto consumer_lag = GetInt64(health, "ConsumerLag");
            const auto write_failures = GetInt64(health, "WriteFailures");
            bool transport_incomplete =
                GetString(health, "SchemaVersion") != "god2-semantic-shared-ring-health-v4" ||
                GetString(health, "UpdatedAtUtc").empty() ||
                GetString(health, "Lifecycle") != "StoppedAndDrained" ||
                !transport_ready || *transport_ready ||
                !consumer_io_failure || *consumer_io_failure ||
                !producer_ready || *producer_ready != 1 ||
                !producer_closed || *producer_closed != 1 ||
                GetInt64(health, "ConsumerReady") != 0 ||
                GetInt64(health, "ConsumerClosed") != 1 ||
                GetInt64(health, "ConsumerFailure") != 0 ||
                !producer_pid || !attached_pid || *producer_pid <= 0 ||
                *producer_pid != *attached_pid ||
                !attempted || !accepted || !dropped || !sampled || !consumed || !invalid || !high_water ||
                !consumer_lag || !write_failures ||
                *attempted < 0 || *accepted < 0 || *dropped < 0 ||
                *sampled != *dropped ||
                *attempted != *accepted + *dropped || *consumed != *accepted ||
                *invalid != 0 || *high_water < 0 || *high_water > 256 ||
                *consumer_lag != 0 ||
                *write_failures != 0;
            std::int64_t lane_accepted_total = 0;
            std::int64_t lane_dropped_total = 0;
            std::int64_t lane_sampled_total = 0;
            for (std::uint32_t priority = 0;
                 priority < god2::shared::kSemanticPriorityCount; ++priority) {
                const std::string prefix = "Lane0" + std::to_string(priority);
                const auto lane_accepted = GetInt64(health, prefix + "Accepted");
                const auto lane_consumed = GetInt64(health, prefix + "Consumed");
                const auto lane_dropped = GetInt64(health, prefix + "Dropped");
                const auto lane_sampled = GetInt64(health, prefix + "Sampled");
                const auto first_dropped = GetInt64(health, prefix + "FirstDroppedSequence");
                const auto last_dropped = GetInt64(health, prefix + "LastDroppedSequence");
                const auto drop_reason = GetInt64(health, prefix + "LastDropReason");
                const auto lane_high_water = GetInt64(health, prefix + "HighWaterMark");
                const auto lane_lag = GetInt64(health, prefix + "ConsumerLag");
                const bool no_loss = lane_dropped && lane_sampled && first_dropped &&
                    last_dropped && drop_reason && *lane_dropped == 0 &&
                    *lane_sampled == 0 && *first_dropped == 0 && *last_dropped == 0 &&
                    *drop_reason == 0;
                const bool sampled_loss = lane_dropped && lane_sampled && first_dropped &&
                    last_dropped && drop_reason && *lane_dropped > 0 &&
                    *lane_sampled == *lane_dropped && *first_dropped > 0 &&
                    *last_dropped >= *first_dropped && *drop_reason == 10;
                if (!lane_accepted || !lane_consumed || !lane_dropped || !lane_sampled ||
                    !lane_high_water || !lane_lag || *lane_accepted < 0 ||
                    *lane_consumed != *lane_accepted || *lane_high_water < 0 ||
                    *lane_high_water > 64 || *lane_lag != 0 ||
                    (priority < 2 ? !no_loss : !(no_loss || sampled_loss))) {
                    transport_incomplete = true;
                } else {
                    lane_accepted_total += *lane_accepted;
                    lane_dropped_total += *lane_dropped;
                    lane_sampled_total += *lane_sampled;
                }
            }
            std::int64_t domain_accepted_total = 0;
            std::int64_t domain_dropped_total = 0;
            std::int64_t domain_write_failures_total = 0;
            for (std::uint32_t domain = 0; domain < god2::shared::kSemanticDomainCount; ++domain) {
                const std::string prefix = "Domain" +
                    (domain < 10 ? std::string("0") : std::string()) + std::to_string(domain);
                const auto domain_accepted = GetInt64(health, prefix + "Accepted");
                const auto domain_dropped = GetInt64(health, prefix + "Dropped");
                const auto first_dropped = GetInt64(health, prefix + "FirstDroppedSequence");
                const auto last_dropped = GetInt64(health, prefix + "LastDroppedSequence");
                const auto drop_reason = GetInt64(health, prefix + "LastDropReason");
                const auto domain_high_water = GetInt64(health, prefix + "HighWaterMark");
                const auto domain_lag = GetInt64(health, prefix + "ConsumerLag");
                const auto domain_write_failures = GetInt64(health, prefix + "WriteFailures");
                const bool no_loss = domain_dropped && first_dropped && last_dropped &&
                    drop_reason && *domain_dropped == 0 && *first_dropped == 0 &&
                    *last_dropped == 0 && *drop_reason == 0;
                const bool sampled_loss = domain_dropped && first_dropped && last_dropped &&
                    drop_reason && *domain_dropped > 0 && *first_dropped > 0 &&
                    *last_dropped >= *first_dropped && *drop_reason == 10;
                if (!domain_accepted || !domain_dropped || !first_dropped || !last_dropped ||
                    !drop_reason || !domain_high_water || !domain_lag || !domain_write_failures ||
                    *domain_accepted < 0 || !(no_loss || sampled_loss) ||
                    *domain_high_water < 0 || *domain_high_water > 256 || *domain_lag != 0 ||
                    *domain_write_failures != 0) {
                    transport_incomplete = true;
                } else {
                    domain_accepted_total += *domain_accepted;
                    domain_dropped_total += *domain_dropped;
                    domain_write_failures_total += *domain_write_failures;
                }
            }
            if (!accepted || domain_accepted_total != *accepted || !write_failures ||
                domain_write_failures_total != *write_failures || !dropped || !sampled ||
                domain_dropped_total != *dropped || lane_accepted_total != *accepted ||
                lane_dropped_total != *dropped || lane_sampled_total != *sampled)
                transport_incomplete = true;
            input.semantic_evidence_incomplete = input.semantic_evidence_incomplete || transport_incomplete;
        }
    } else {
        input.semantic_evidence_incomplete = true;
    }
    const auto has_nonzero_probe_counter = [](std::string_view text,
                                              std::string_view name) noexcept {
        const auto position = text.rfind(name);
        if (position == std::string_view::npos) return false;
        std::size_t cursor = position + name.size();
        while (cursor < text.size() && (text[cursor] == ' ' || text[cursor] == '=' ||
                                        text[cursor] == ':' || text[cursor] == '"')) ++cursor;
        std::uint64_t value = 0;
        bool digits = false;
        while (cursor < text.size() && text[cursor] >= '0' && text[cursor] <= '9') {
            digits = true;
            value = value * 10 + static_cast<std::uint64_t>(text[cursor] - '0');
            ++cursor;
        }
        return digits && value != 0;
    };
    if (const auto probe_log = ReadUtf8File(session_path / L"reports" / L"enhanced-capture.log")) {
        input.semantic_evidence_incomplete = input.semantic_evidence_incomplete ||
            has_nonzero_probe_counter(*probe_log, "semanticWriteFailures") ||
            has_nonzero_probe_counter(*probe_log, "semanticEvidenceIncomplete");
    }
    input.interrupted = input.semantic_evidence_incomplete;

    const auto recovery = UltimateRecoveryEngine::Recover(input);
    UltimateBundleOptions options;
    options.output_directory = session_path / L"ultimate-recovery";
    options.package_id = "God2UltimateRecovery_" + input.session_id;
    options.client_executable_path = client_path;
    options.create_zip = true;
    bundle = WriteUltimateRecoveryBundle(recovery, options);

    std::error_code bundle_file_error;
    const auto committed_zip_hash = bundle.zip_path.empty() ? std::optional<std::string>{} :
        CalculateFileSha256(bundle.zip_path);
    const bool artifact_written = bundle.success && !bundle.zip_path.empty() &&
        fs::is_regular_file(bundle.zip_path, bundle_file_error) && !bundle_file_error &&
        committed_zip_hash && *committed_zip_hash == bundle.zip_sha256;
    if (bundle.success && !artifact_written) {
        bundle.success = false;
        if (!bundle.error.empty()) bundle.error += "; ";
        bundle.error += "ultimate recovery ZIP commit could not be independently verified";
    }
    const bool exact_client_identity = bundle.client_identity_status ==
        "ExactClientIdentityComputedAndValidated";
    const bool recovery_evidence_complete = artifact_written && exact_client_identity &&
        !input.semantic_evidence_incomplete && recovery.status.rfind("EvidenceBlocked", 0) != 0;
    const auto make_report = [&](std::string_view status, bool evidence_complete,
                                 bool report_written) {
        return MakeJsonObject({
            {"SchemaVersion", "god2-ultimate-session-package-result-v1"},
            {"GeneratedAtUtc", UtcNow()},
            {"PackageId", options.package_id},
            {"SessionId", input.session_id},
            {"Status", std::string(status)},
            {"ArtifactWritten", artifact_written ? "true" : "false"},
            {"EvidenceComplete", evidence_complete ? "true" : "false"},
            {"ReportWritten", report_written ? "true" : "false"},
            {"RecoveryStatus", recovery.status},
            {"SemanticEventCount", std::to_string(input.semantic_events.size())},
            {"RejectedSemanticLineCount", std::to_string(rejected_lines)},
            {"SemanticEvidenceIncomplete", input.semantic_evidence_incomplete ? "true" : "false"},
            {"ClientIdentityStatus", bundle.client_identity_status},
            {"ZipPath", bundle.zip_path.empty() ? "" :
                WideToUtf8(fs::relative(bundle.zip_path, session_path).generic_wstring())},
            {"StagingPath", bundle.staging_directory.empty() ? "" :
                WideToUtf8(fs::relative(bundle.staging_directory, session_path).generic_wstring())},
            {"ManifestPath", bundle.manifest_path.empty() ? "" :
                WideToUtf8(fs::relative(bundle.manifest_path, session_path).generic_wstring())},
            {"ZipSHA256", bundle.zip_sha256},
            {"Error", bundle.error}
        }, {"ArtifactWritten", "EvidenceComplete", "ReportWritten", "SemanticEventCount",
            "RejectedSemanticLineCount", "SemanticEvidenceIncomplete"});
    };
    const fs::path report_path = session_path / L"reports" /
        L"ultimate-recovery-package-result.json";
    // First replace any stale PASS with a durable fail-closed marker.  Only a
    // second successful atomic commit is allowed to expose EvidenceComplete.
    const bool pending_written = WriteUtf8FileAtomic(report_path,
        make_report(artifact_written ? "ARTIFACT_WRITTEN_REPORT_COMMIT_PENDING" : "FAIL",
                    false, false) + "\n");
    const std::string final_status = !artifact_written ? "FAIL" :
        (recovery_evidence_complete ? "PASS" : "ARTIFACT_WRITTEN_EVIDENCE_BLOCKED");
    const bool report_written = pending_written && WriteUtf8FileAtomic(
        report_path, make_report(final_status, recovery_evidence_complete, true) + "\n");
    if (!report_written) {
        bundle.success = false;
        if (!bundle.error.empty()) bundle.error += "; ";
        bundle.error += "ultimate recovery package result report could not be committed";
    }
    return bundle;
}

class CaptureController final : public std::enable_shared_from_this<CaptureController> {
public:
    using Callback = std::function<void(bool, std::wstring)>;

    CaptureController() {
        cancel_event_name_ = L"Local\\God2PacketCapture.ControllerCancel." + Utf8ToWide(NewId());
        cancel_event_ = CreateEventW(nullptr, TRUE, FALSE, cancel_event_name_.c_str());
        startup_done_event_ = CreateEventW(nullptr, TRUE, TRUE, nullptr);
    }

    ~CaptureController() {
        if (cancel_event_ != nullptr) SetEvent(cancel_event_);
        CloseWorkerHandles();
        if (startup_done_event_ != nullptr) CloseHandle(startup_done_event_);
        if (cancel_event_ != nullptr) CloseHandle(cancel_event_);
    }

    CaptureSnapshot Snapshot() const {
        CaptureSnapshot result;
        {
            std::lock_guard lock(mutex_);
            result = snapshot_;
        }
        result.memory_bytes = WorkingSetBytes();
        return result;
    }

    void RefreshMetricsAsync() {
        if (metrics_refresh_running_.exchange(true)) return;
        fs::path session_path;
        {
            std::lock_guard lock(mutex_);
            session_path = snapshot_.session_path;
        }
        if (session_path.empty()) {
            metrics_refresh_running_.store(false);
            return;
        }
        auto self = shared_from_this();
        try {
            std::thread([self, session_path = std::move(session_path)] {
                try {
                    if (self->metrics_session_path_ != session_path) {
                        self->metrics_session_path_ = session_path;
                        self->metrics_line_states_.clear();
                    }
                    const std::uint64_t captured_size = DirectoryBytes(session_path);
                    const std::uint64_t packet_count = self->IncrementalLineCount(session_path / L"raw" / L"frames.jsonl");
                    const std::uint64_t unknown_count = self->IncrementalLineCount(session_path / L"raw" / L"unknown-frames.jsonl");
                    const auto candidates = ReadCandidateClassificationMetrics(session_path);
                    const auto raw_etl = ReadRawEtlMetrics(session_path);
                    std::uint64_t classified_count = 0;
                    for (const auto& path : RequiredGameplayFiles())
                        classified_count += self->IncrementalLineCount(session_path / path);
                    std::lock_guard lock(self->mutex_);
                    if (self->snapshot_.session_path == session_path) {
                        self->snapshot_.captured_size = captured_size;
                        self->snapshot_.packet_count = packet_count;
                        self->snapshot_.raw_etl_packet_count = raw_etl.ndis_packet_events;
                        self->snapshot_.unknown_count = unknown_count;
                        self->snapshot_.classified_count = classified_count;
                        self->snapshot_.candidate_frame_count = candidates.candidate_frames;
                        self->snapshot_.semantic_candidate_count = candidates.semantic_candidates;
                        self->snapshot_.high_confidence_semantic_candidate_count =
                            candidates.high_confidence_semantic_candidates;
                    }
                } catch (const std::exception& exception) {
                    try { AppendStartupLog(L"MetricsRefreshException", Utf8ToWide(exception.what())); } catch (...) {}
                } catch (...) {
                    try { AppendStartupLog(L"MetricsRefreshException", L"Unknown exception"); } catch (...) {}
                }
                self->metrics_refresh_running_.store(false);
            }).detach();
        } catch (const std::exception& exception) {
            metrics_refresh_running_.store(false);
            try { AppendStartupLog(L"MetricsRefreshThreadError", Utf8ToWide(exception.what())); } catch (...) {}
        } catch (...) {
            metrics_refresh_running_.store(false);
            try { AppendStartupLog(L"MetricsRefreshThreadError", L"Unknown exception"); } catch (...) {}
        }
    }

    void SetProbeResult(std::wstring backend) {
        std::lock_guard lock(mutex_);
        if (snapshot_.backend_state == BackendState::NotStarted) snapshot_.backend = std::move(backend);
    }

    void SetGpuProbeResult(std::wstring gpu) {
        std::lock_guard lock(mutex_);
        snapshot_.gpu = std::move(gpu);
    }

    void StartAsync(fs::path selected_launcher_path, bool enhanced_capture,
                    AccelerationMode acceleration_mode, Callback callback) {
        {
            std::lock_guard lock(mutex_);
            if (!IsTerminalCaptureState(snapshot_.state)) {
                NotifyNoThrow(callback, false, Text(UiString::CaptureAlreadyRunning));
                return;
            }
            if (cancel_event_ == nullptr || startup_done_event_ == nullptr) {
                SetStateLocked(CaptureState::Failed,
                    Localized(L"無法建立控制器取消事件", L"无法创建控制器取消事件", L"Controller cancellation event is unavailable"));
                NotifyNoThrow(callback, false, snapshot_.status);
                return;
            }
            ResetEvent(cancel_event_);
            ResetEvent(startup_done_event_);
            ++tracker_generation_;
            post_capture_fallback_ = false;
            startup_cancel_requested_ = false;
            worker_consumer_fallback_ = false;
            snapshot_ = CaptureSnapshot{};
            SetStateLocked(CaptureState::Preparing,
                Localized(L"正在準備 Capture Session", L"正在准备捕获会话", L"Preparing capture session"));
        }
        auto self = shared_from_this();
        try {
            Callback thread_callback(callback);
            std::thread([self, selected_launcher_path = std::move(selected_launcher_path),
                         enhanced_capture, acceleration_mode,
                         callback = std::move(thread_callback)]() mutable {
                self->RunStart(std::move(selected_launcher_path), enhanced_capture, acceleration_mode, callback);
                SetEvent(self->startup_done_event_);
            }).detach();
        } catch (const std::exception& exception) {
            SetEvent(startup_done_event_);
            FailStart({}, Utf8ToWide(exception.what()));
            NotifyNoThrow(callback, false, Utf8ToWide(exception.what()));
        }
    }

    void StopAsync(Callback callback) {
        CaptureState requested_state;
        {
            std::lock_guard lock(mutex_);
            requested_state = snapshot_.state;
            if (!CanRequestStop(requested_state)) {
                NotifyNoThrow(callback, false, Text(UiString::NothingToStop));
                return;
            }
            SetEvent(cancel_event_);
            if (IsStartupCancellable(requested_state)) startup_cancel_requested_ = true;
            SetStateLocked(IsStartupCancellable(requested_state) ? CaptureState::Cancelling : CaptureState::Stopping,
                IsStartupCancellable(requested_state) ?
                    Localized(L"正在取消啟動並保存證據", L"正在取消启动并保存证据", L"Cancelling startup and preserving evidence") :
                    Localized(L"正在停止、排空與匯出", L"正在停止、排空并导出", L"Stopping, draining, and exporting"));
            snapshot_.backend_state = snapshot_.backend_state == BackendState::Active ?
                BackendState::Stopping : snapshot_.backend_state;
            snapshot_.consumer_state =
                (snapshot_.consumer_state == ConsumerState::Ready ||
                 snapshot_.consumer_state == ConsumerState::Starting) ?
                    ConsumerState::Stopping : snapshot_.consumer_state;
            snapshot_.analysis_state = AnalysisState::Finalizing;
        }
        auto self = shared_from_this();
        try {
            Callback thread_callback(callback);
            std::thread([self, callback = std::move(thread_callback)]() mutable {
                self->RunStop(callback);
            }).detach();
        } catch (const std::exception& exception) {
            {
                std::lock_guard lock(mutex_);
                SetStateLocked(CaptureState::CleanupRequired,
                    Localized(L"無法建立停止工作；請重試", L"无法创建停止任务；请重试", L"Could not create stop task; retry Stop"));
            }
            NotifyNoThrow(callback, false, Utf8ToWide(exception.what()));
        }
    }

private:
    void SetStateLocked(CaptureState state, std::wstring status) {
        snapshot_.state = state;
        snapshot_.status = std::move(status);
        snapshot_.busy = !IsTerminalCaptureState(state) && state != CaptureState::Capturing &&
            state != CaptureState::PostCaptureFallback && state != CaptureState::WaitingForGod2Process &&
            state != CaptureState::CleanupRequired;
        // This legacy UI flag means that the semantic workflow still needs a
        // Stop/finalization action. A policy-blocked raw backend alone must not
        // make a terminal Completed/Cancelled state look actively capturing.
        snapshot_.capturing = WorkflowNeedsFinalization(state);
    }

    bool CancellationRequested() const noexcept {
        return cancel_event_ != nullptr && WaitForSingleObject(cancel_event_, 0) == WAIT_OBJECT_0;
    }

    void MarkCancellationObserved() {
        std::lock_guard lock(mutex_);
        if (snapshot_.state == CaptureState::CleanupRequired) return;
        SetStateLocked(CaptureState::Cancelling,
            Localized(L"已收到取消要求；正在保存已捕捉證據", L"已收到取消请求；正在保存已捕获证据", L"Cancellation received; preserving captured evidence"));
    }

    void RunStart(fs::path selected_launcher_path, bool enhanced_capture,
                  AccelerationMode acceleration_mode, const Callback& callback) noexcept {
        CaptureSession session;
        try {
            wchar_t internal_delay_text[16]{};
            const DWORD internal_delay_length = GetEnvironmentVariableW(
                L"GOD2_PACKET_CAPTURE_INTERNAL_START_DELAY_MS", internal_delay_text,
                static_cast<DWORD>(std::size(internal_delay_text)));
            if (wcsstr(GetCommandLineW(), L"--internal-selftest") != nullptr &&
                internal_delay_length != 0 && internal_delay_length < std::size(internal_delay_text)) {
                const DWORD delay = wcstoul(internal_delay_text, nullptr, 10);
                if (delay != 0 && WaitForSingleObject(cancel_event_, delay) == WAIT_OBJECT_0) {
                    MarkCancellationObserved();
                    NotifyNoThrow(callback, false, Localized(L"啟動已取消。", L"启动已取消。", L"Startup was cancelled."));
                    return;
                }
            }
            std::string error;
            fs::path launcher_path;
            fs::path game_path;
            if (!GameProcessTracker::ResolveClientPaths(selected_launcher_path, &launcher_path, &game_path, &error)) {
                FailStart({}, Utf8ToWide(error));
                NotifyNoThrow(callback, false, std::wstring(Localized(L"遊戲登入器設定無效：\n", L"游戏登录器设置无效：\n", L"Invalid game launcher configuration:\n")) + Utf8ToWide(error));
                return;
            }
            if (CancellationRequested()) {
                MarkCancellationObserved();
                NotifyNoThrow(callback, false, Localized(L"啟動已取消。", L"启动已取消。", L"Startup was cancelled."));
                return;
            }
            if (!session.Create(&error)) {
                FailStart({}, Utf8ToWide(error));
                NotifyNoThrow(callback, false, std::wstring(Localized(L"無法建立 Capture Session：\n", L"无法创建捕获会话：\n", L"Could not create the capture session:\n")) + Utf8ToWide(error));
                return;
            }
            if (!SaveSessionExecutablePaths(session.Path(), launcher_path, game_path, acceleration_mode, &error)) {
                FailStart(session.Path(), Utf8ToWide(error));
                NotifyNoThrow(callback, false, Utf8ToWide(error));
                return;
            }
            if (!PublishLiveSessionBinding(session.Path(), session.SessionId(), &error)) {
                FailStart(session.Path(), Utf8ToWide(error));
                NotifyNoThrow(callback, false, Utf8ToWide(error));
                return;
            }
            {
                std::lock_guard lock(mutex_);
                snapshot_.session_path = session.Path();
                snapshot_.started_at = LocalTimestamp(false);
                snapshot_.game_process_state = GameProcessState::Searching;
                snapshot_.game_process = GameProcessStateText(GameProcessState::Searching);
                snapshot_.analysis_state = AnalysisState::WaitingForPid;
                snapshot_.analysis = AnalysisStateText(snapshot_.analysis_state);
                game_path_ = game_path;
            }
            enhanced_capture_.Configure(enhanced_capture, session.Path(), session.SessionId(),
                                        game_path, cancel_event_);
            StartGameProcessTracker(session.Path(), game_path, tracker_generation_.load());

            CaptureBackendManager manager;
            auto selection = manager.Probe();
            if (!selection.backend) {
                FailStart(session.Path(), Localized(L"無法套用 raw payload 隱私後端", L"无法应用 raw payload 隐私后端", L"No raw-payload privacy backend was available"));
                NotifyNoThrow(callback, false, Localized(L"無法套用 raw payload 隱私政策；遮罩語意取證流程未啟動。", L"无法应用 raw payload 隐私政策；遮罩语义取证流程未启动。", L"The raw-payload privacy policy could not be applied; the masked semantic evidence workflow was not started."));
                return;
            }
            {
                SessionStore store(session.Path());
                if (!store.Initialize(&error) || !store.WriteCompatibilityReports(selection, &error)) {
                    FailStart(session.Path(), Utf8ToWide(error));
                    NotifyNoThrow(callback, false, Utf8ToWide(error));
                    return;
                }
            }
            if (CancellationRequested()) {
                MarkCancellationObserved();
                NotifyNoThrow(callback, false, Localized(L"啟動已取消。", L"启动已取消。", L"Startup was cancelled."));
                return;
            }

            const std::string backend_name = selection.backend->Name();
            BackendCapabilities capabilities = selection.capabilities;
            CaptureRequest request;
            request.session_path = session.Path();
            CaptureResult start;
            {
                std::lock_guard lock(mutex_);
                snapshot_.backend_state = BackendState::Starting;
                snapshot_.backend = BackendStateText(snapshot_.backend_state);
                SetStateLocked(IsAdministrator() ? CaptureState::StartingBackend : CaptureState::WaitingForUac,
                    IsAdministrator() ? Localized(L"正在套用 raw payload 隱私政策", L"正在应用 raw payload 隐私政策", L"Applying raw-payload privacy policy") :
                        Localized(L"正在等待 x86 語意工作者的管理員授權；可取消", L"正在等待 x86 语义工作者的管理员授权；可取消", L"Waiting for administrator approval for the x86 semantic worker; startup can be cancelled"));
            }
            bool raw_privacy_blocked = false;
            bool semantic_workflow_ready = false;
            if (IsAdministrator()) {
                start = selection.backend->Start(request);
                raw_privacy_blocked = IsRawPrivacyPolicyBlock(start);
                semantic_workflow_ready = start.success || raw_privacy_blocked;
                if (start.success) {
                    backend_ = std::move(selection.backend);
                    request_ = request;
                    {
                        std::lock_guard lock(mutex_);
                        snapshot_.backend_state = BackendState::Active;
                        snapshot_.backend = Localized(
                            L"系統 raw payload 已停用 · x86 遮罩通道",
                            L"系统 raw payload 已停用 · x86 遮罩通道",
                            L"System raw payload disabled · x86 masked channel");
                        snapshot_.consumer_state = ConsumerState::NotStarted;
                        snapshot_.consumer = ConsumerStateText(snapshot_.consumer_state);
                        SetStateLocked(CaptureState::StartingConsumer,
                            Localized(L"後端已明確啟動；正在等待安全 Consumer READY", L"后端已明确启动；正在等待安全 Consumer READY", L"Backend explicitly started; waiting for a safe consumer READY"));
                    }
                    StartEvidenceWorker(capabilities, &error);
                    if (!error.empty()) {
                        post_capture_fallback_ = true;
                        std::lock_guard lock(mutex_);
                        snapshot_.consumer_state = ConsumerState::NotStarted;
                        snapshot_.consumer = ConsumerStateText(snapshot_.consumer_state);
                    } else {
                        std::lock_guard lock(mutex_);
                        snapshot_.consumer_state = ConsumerState::NotStarted;
                        snapshot_.consumer = ConsumerStateText(snapshot_.consumer_state);
                    }
                } else if (raw_privacy_blocked) {
                    std::lock_guard lock(mutex_);
                    snapshot_.backend_state = BackendState::EvidenceBlocked;
                    snapshot_.backend = L"EvidenceBlockedPrivacyPolicy · x86 masked semantic only";
                    snapshot_.consumer_state = ConsumerState::NotStarted;
                    snapshot_.consumer = Localized(L"等待 x86 遮罩語意事件", L"等待 x86 遮罩语义事件",
                                                    L"Waiting for x86 masked semantic events");
                }
            } else {
                start = StartElevatedWorker(session.Path(), backend_name);
                post_capture_fallback_ = worker_consumer_fallback_;
                raw_privacy_blocked = start.success &&
                    start.message.rfind("EvidenceBlockedPrivacyPolicy:", 0) == 0;
                semantic_workflow_ready = start.success;
                if (start.success) {
                    std::wstring elevated_backend_status = raw_privacy_blocked ?
                        L"EvidenceBlockedPrivacyPolicy · x86 masked semantic only" :
                        Utf8ToWide(backend_name) + L" Active";
                    std::lock_guard<std::mutex> elevated_state_lock(mutex_);
                    snapshot_.backend_state = raw_privacy_blocked ?
                        BackendState::EvidenceBlocked : BackendState::Active;
                    snapshot_.backend = std::move(elevated_backend_status);
                    snapshot_.consumer_state = ConsumerState::NotStarted;
                    snapshot_.consumer = raw_privacy_blocked ?
                        Localized(L"等待 x86 遮罩語意事件", L"等待 x86 遮罩语义事件",
                                  L"Waiting for x86 masked semantic events") :
                        ConsumerStateText(snapshot_.consumer_state);
                }
            }
            if (CancellationRequested()) {
                MarkCancellationObserved();
                NotifyNoThrow(callback, false, Localized(L"啟動已取消；正在關閉語意取證元件。", L"启动已取消；正在关闭语义取证组件。", L"Startup cancelled; semantic evidence components are closing."));
                return;
            }
            if (!semantic_workflow_ready) {
                const bool cleanup_required = HasElevatedWorker() || backend_ != nullptr;
                if (cleanup_required) MarkStartCleanupRequired(session.Path());
                else FailStart(session.Path(), Utf8ToWide(start.message));
                NotifyNoThrow(callback, false, std::wstring(Localized(L"無法啟動遮罩語意取證流程：\n", L"无法启动遮罩语义取证流程：\n", L"Could not start the masked semantic evidence workflow:\n")) + Utf8ToWide(start.message));
                return;
            }

            BackendCapabilities analysis_capabilities = capabilities;
            if (enhanced_capture) {
                // The x86 probe writes process-scoped Winsock records directly.
                // It is the only production network-evidence source.  The
                // system-wide netsh/PktMon raw-payload path is privacy-blocked.
                post_capture_fallback_ = false;
                analysis_capabilities.analysis_mode = AnalysisMode::NearRealtime;
            } else if (post_capture_fallback_ || capabilities.analysis_mode != AnalysisMode::NearRealtime) {
                post_capture_fallback_ = true;
                analysis_capabilities.analysis_mode = AnalysisMode::PostCapture;
            }
            {
                std::lock_guard lock(mutex_);
                snapshot_.analysis_state = AnalysisState::Starting;
                snapshot_.analysis = AnalysisStateText(snapshot_.analysis_state);
                SetStateLocked(CaptureState::StartingAnalysis,
                    Localized(L"正在啟動分析管線", L"正在启动分析管线", L"Starting analysis pipeline"));
            }
            error.clear();
            if (!analysis_.Start(session.Path(), analysis_capabilities, &error)) {
                analysis_.AbortNoThrow();
                analysis_capabilities.analysis_mode = AnalysisMode::PostCapture;
                if (!analysis_.Start(session.Path(), analysis_capabilities, &error)) {
                    post_capture_fallback_ = true;
                    std::lock_guard lock(mutex_);
                    snapshot_.analysis_state = AnalysisState::Failed;
                    snapshot_.analysis = AnalysisStateText(snapshot_.analysis_state);
                } else {
                    post_capture_fallback_ = true;
                }
            }
            {
                std::lock_guard lock(mutex_);
                snapshot_.analysis_state = post_capture_fallback_ ? AnalysisState::PostCaptureFallback : AnalysisState::WaitingForPid;
                snapshot_.analysis = AnalysisStateText(snapshot_.analysis_state);
                SetStateLocked(post_capture_fallback_ ? CaptureState::PostCaptureFallback : CaptureState::WaitingForLauncher,
                    post_capture_fallback_ ?
                        Localized(L"系統層 raw payload 維持 EvidenceBlockedPrivacyPolicy；x86 即時語意分析未就緒，只會完成已遮罩的安全證據。", L"系统层 raw payload 保持 EvidenceBlockedPrivacyPolicy；x86 实时语义分析未就绪，仅会完成已遮罩的安全证据。", L"System-wide raw payload remains EvidenceBlockedPrivacyPolicy; x86 live semantic analysis is unavailable, so only already-masked safe evidence will be finalized.") :
                        Localized(L"x86 遮罩語意流程已就緒；正在確認 Launcher.exe", L"x86 遮罩语义流程已就绪；正在确认 Launcher.exe", L"x86 masked semantic workflow ready; checking Launcher.exe"));
            }
            if (CancellationRequested()) {
                MarkCancellationObserved();
                NotifyNoThrow(callback, false, Localized(L"啟動已取消。", L"启动已取消。", L"Startup was cancelled."));
                return;
            }
            error.clear();
            if (!GameProcessTracker::EnsureLauncherRunning(launcher_path, &error)) {
                MarkStartCleanupRequired(session.Path());
                NotifyNoThrow(callback, false, std::wstring(Localized(L"語意取證工作者仍需完成清理，但無法啟動 Launcher.exe：\n", L"语义取证工作者仍需完成清理，但无法启动 Launcher.exe：\n", L"The semantic evidence worker still requires cleanup, but Launcher.exe could not start:\n")) + Utf8ToWide(error));
                return;
            }
            {
                std::lock_guard lock(mutex_);
                if (post_capture_fallback_) {
                    SetStateLocked(CaptureState::PostCaptureFallback,
                        Localized(L"系統 raw payload 已依隱私政策停用；x86 語意通道無法啟動，停止後只會封裝可證明安全的證據。", L"系统 raw payload 已依隐私政策停用；x86 语义通道无法启动，停止后只会封装可证明安全的证据。", L"System raw payload is disabled by privacy policy; the x86 semantic channel could not start, so Stop will package only provably safe evidence."));
                } else if (snapshot_.game_process_state == GameProcessState::Validated) {
                    snapshot_.analysis_state = AnalysisState::NearRealtime;
                    snapshot_.analysis = AnalysisStateText(snapshot_.analysis_state);
                    SetStateLocked(CaptureState::Capturing, Text(UiString::Capturing));
                } else {
                    SetStateLocked(CaptureState::WaitingForGod2Process,
                        Localized(L"Launcher.exe 已啟動；正在尋找 God2_opt.exe", L"Launcher.exe 已启动；正在查找 God2_opt.exe", L"Launcher.exe is running; searching for God2_opt.exe"));
                }
            }
            NotifyNoThrow(callback, true, post_capture_fallback_ ?
                Localized(L"系統 raw payload 已停用；x86 語意 Consumer 未就緒，狀態維持 EvidenceBlocked。", L"系统 raw payload 已停用；x86 语义 Consumer 未就绪，状态保持 EvidenceBlocked。", L"System raw payload is disabled; the x86 semantic consumer is unavailable and remains EvidenceBlocked.") :
                Text(UiString::StartCompleteWaiting));
        } catch (const std::exception& exception) {
            const fs::path path = session.Path();
            if (backend_ != nullptr || HasElevatedWorker()) MarkStartCleanupRequired(path);
            else FailStart(path, Utf8ToWide(exception.what()));
            AppendStartupLog(L"CaptureStartException", Utf8ToWide(exception.what()));
            NotifyNoThrow(callback, false, Utf8ToWide(exception.what()));
        } catch (...) {
            const fs::path path = session.Path();
            if (backend_ != nullptr || HasElevatedWorker()) MarkStartCleanupRequired(path);
            else FailStart(path, Localized(L"未知錯誤", L"未知错误", L"Unknown error"));
            AppendStartupLog(L"CaptureStartException", L"Unknown exception");
            NotifyNoThrow(callback, false, Localized(L"啟動遮罩語意取證流程時發生未知錯誤。", L"启动遮罩语义取证流程时发生未知错误。", L"An unknown error occurred while starting the masked semantic evidence workflow."));
        }
    }

    void RunStop(const Callback& callback) noexcept {
        try {
            const bool startup_was_cancelled = startup_cancel_requested_.load();
            if (startup_done_event_ != nullptr && WaitForSingleObject(startup_done_event_, 0) != WAIT_OBJECT_0) {
                const DWORD startup_wait = WaitForSingleObject(startup_done_event_, 60'000);
                if (startup_wait != WAIT_OBJECT_0) {
                    AppendStartupLog(L"StartupCancelWait", L"Startup task did not acknowledge cancellation within 60 seconds");
                    {
                        std::lock_guard lock(mutex_);
                        SetStateLocked(CaptureState::CleanupRequired,
                            Localized(L"啟動工作尚未確認取消；請關閉 UAC 提示後重試停止",
                                      L"启动任务尚未确认取消；请关闭 UAC 提示后重试停止",
                                      L"Startup has not acknowledged cancellation; dismiss the UAC prompt and retry Stop"));
                    }
                    NotifyNoThrow(callback, false,
                        Localized(L"取消等待逾時。請關閉仍顯示的 UAC 提示，然後再次按停止。",
                                  L"取消等待超时。请关闭仍显示的 UAC 提示，然后再次点击停止。",
                                  L"Cancellation timed out. Dismiss any open UAC prompt, then click Stop again."));
                    return;
                }
            }
            std::string enhanced_error;
            const bool enhanced_stopped = enhanced_capture_.Stop(&enhanced_error);
            std::string stop_error;
            const bool stopped = StopBackendOnly(&stop_error);
            fs::path session_path;
            fs::path client_path;
            {
                std::lock_guard lock(mutex_);
                session_path = snapshot_.session_path;
                client_path = game_path_;
                const bool raw_was_privacy_blocked =
                    snapshot_.backend_state == BackendState::EvidenceBlocked;
                snapshot_.backend_state = stopped ?
                    (raw_was_privacy_blocked ? BackendState::EvidenceBlocked : BackendState::Stopped) :
                    BackendState::Failed;
                snapshot_.backend = BackendStateText(snapshot_.backend_state);
                snapshot_.consumer_state = enhanced_stopped ?
                    ConsumerState::Stopped : ConsumerState::EvidenceBlocked;
                snapshot_.consumer = ConsumerStateText(snapshot_.consumer_state);
                snapshot_.analysis_state = AnalysisState::Finalizing;
                snapshot_.analysis = AnalysisStateText(snapshot_.analysis_state);
            }
            std::string analysis_error;
            const bool analysis_finished = stopped && enhanced_stopped && (session_path.empty() ||
                analysis_.Finalize(session_path, post_capture_fallback_.load(), &analysis_error));
            std::string privacy_error;
            const bool privacy_enforced = session_path.empty() || (stopped && analysis_finished &&
                EnforceRawNetworkPrivacyPolicy(session_path, &privacy_error));
            EvidencePackageResult evidence_package;
            const bool package_finished = session_path.empty() || (stopped && analysis_finished && privacy_enforced &&
                (evidence_package = EvidencePackageBuilder::Build(session_path)).success);
            UltimateBundleResult ultimate_package;
            const bool ultimate_package_finished = session_path.empty() ||
                (stopped && analysis_finished && privacy_enforced &&
                 (ultimate_package = BuildUltimateRecoveryPackage(session_path, client_path)).success);
            const bool finalized = CanCompleteCaptureStop(stopped, enhanced_stopped,
                analysis_finished, privacy_enforced, package_finished,
                ultimate_package_finished);
            const bool cleanup_warning = !enhanced_stopped;
            const bool completed_with_warnings = finalized && !session_path.empty() &&
                (SessionCompletedWithWarnings(session_path) ||
                 evidence_package.package_status == "ReadyWithWarnings");
            std::string combined = enhanced_error;
            if (!stop_error.empty()) { if (!combined.empty()) combined += "; "; combined += stop_error; }
            if (!analysis_error.empty()) { if (!combined.empty()) combined += "; "; combined += analysis_error; }
            if (!privacy_error.empty()) { if (!combined.empty()) combined += "; "; combined += privacy_error; }
            if (!evidence_package.error.empty()) { if (!combined.empty()) combined += "; "; combined += evidence_package.error; }
            if (!ultimate_package.error.empty()) { if (!combined.empty()) combined += "; "; combined += ultimate_package.error; }
            std::wstring gpu_execution_status;
            if (evidence_package.success) {
                gpu_execution_status = L"Detected: " + Utf8ToWide(evidence_package.gpu_device == "None" ?
                    "none" : evidence_package.gpu_device) + L" | Capability: " +
                    Utf8ToWide(evidence_package.gpu_capability) + L" | Current: " +
                    Utf8ToWide(evidence_package.gpu_selected_backend) + L" | Reason: " +
                    Utf8ToWide(evidence_package.gpu_selection_reason);
            }
            {
                std::lock_guard lock(mutex_);
                snapshot_.analysis_state = !enhanced_stopped ? AnalysisState::Finalizing :
                    (analysis_finished ? AnalysisState::Completed : AnalysisState::Failed);
                snapshot_.analysis = AnalysisStateText(snapshot_.analysis_state);
                if (evidence_package.success) {
                    snapshot_.evidence_zip_path = evidence_package.zip_path;
                    snapshot_.evidence_zip_sha256 = evidence_package.zip_sha256;
                    snapshot_.package_status = evidence_package.package_status;
                    snapshot_.capture_record_count = evidence_package.capture_records;
                    snapshot_.transport_chunk_count = evidence_package.transport_chunks;
                    snapshot_.candidate_frame_count = evidence_package.candidate_protocol_frames;
                    snapshot_.protocol_frame_count = evidence_package.protocol_frames;
                    snapshot_.decoded_message_count = evidence_package.decoded_messages;
                    snapshot_.handler_observation_count = evidence_package.handler_observations;
                    snapshot_.gameplay_candidate_count = evidence_package.gameplay_candidates;
                    snapshot_.gpu = std::move(gpu_execution_status);
                }
                if (finalized) {
                    SetStateLocked(startup_was_cancelled ? CaptureState::Cancelled : CaptureState::Completed,
                        startup_was_cancelled ?
                            Localized(L"啟動已取消；證據已保存", L"启动已取消；证据已保存", L"Startup cancelled; evidence preserved") :
                            (completed_with_warnings ?
                                Localized(L"語意證據、分析與匯出已完成；證據已保存，但存在非阻斷警告",
                                          L"语义证据、分析与导出已完成；证据已保存，但存在非阻断警告",
                                          L"Semantic evidence, analysis, and export are complete; evidence is saved with non-blocking warnings") :
                                Localized(L"語意證據、分析與匯出已完成", L"语义证据、分析与导出已完成", L"Semantic evidence, analysis, and export are complete")));
                } else {
                    SetStateLocked(CaptureState::CleanupRequired,
                        stopped ? Localized(L"語意工作者已完成，但 Session 或 Evidence ZIP 尚未完成；可重試停止",
                                            L"语义工作者已完成，但 Session 或 Evidence ZIP 尚未完成；可重试停止",
                                            L"The semantic worker finished, but session finalization or Evidence ZIP packaging is incomplete; retry Stop") :
                            Localized(L"停止尚未完成；可重試停止", L"停止尚未完成；可重试停止",
                                      L"Stop is incomplete; retry Stop"));
                }
            }
            std::wstring completion_message;
            if (evidence_package.success) {
                completion_message = L"UltimateRecoveryPackage: " +
                    (ultimate_package.success ? ultimate_package.zip_path.wstring() : L"EvidenceBlocked") +
                    L"\nUltimateRecoverySHA-256: " +
                    Utf8ToWide(ultimate_package.zip_sha256.empty() ? "Unavailable" : ultimate_package.zip_sha256) +
                    L"\n\nCompatibilityPackageStatus: " + Utf8ToWide(evidence_package.package_status) + L"\n" +
                    Localized(L"Evidence ZIP 已就緒：\n", L"Evidence ZIP 已就绪：\n", L"Evidence ZIP is ready:\n") +
                    evidence_package.zip_path.wstring() + L"\n\nSHA-256: " + Utf8ToWide(evidence_package.zip_sha256) +
                    L"\n\nCaptureRecords / TransportChunks / CandidateProtocolFrames / ProtocolFrames / DecodedMessages / HandlerObservations / GameplayCandidates = " +
                    std::to_wstring(evidence_package.capture_records) + L" / " +
                    std::to_wstring(evidence_package.transport_chunks) + L" / " +
                    std::to_wstring(evidence_package.candidate_protocol_frames) + L" / " +
                    std::to_wstring(evidence_package.protocol_frames) + L" / " +
                    std::to_wstring(evidence_package.decoded_messages) + L" / " +
                    std::to_wstring(evidence_package.handler_observations) + L" / " +
                    std::to_wstring(evidence_package.gameplay_candidates) +
                    L"\nCompressionRatio: " + std::to_wstring(evidence_package.compression_ratio) +
                    L"\nWarningsCount: " + std::to_wstring(evidence_package.warnings.size()) +
                    Localized(
                        L"\n\n系統層原始封包已停用且不會打包；ZIP 只保留經遮罩的 x86 語意證據，但仍可能包含角色資訊及網路端點，請只交給受信任的 Codex 工作環境。",
                        L"\n\n系统层原始封包已停用且不会打包；ZIP 仅保留已遮罩的 x86 语义证据，但仍可能包含角色信息及网络端点，请仅交给受信任的 Codex 工作环境。",
                        L"\n\nSystem-wide raw packets are disabled and never packaged. The ZIP retains only masked x86 semantic evidence, but may still contain character information and network endpoints; share it only with a trusted Codex workspace.");
                if (cleanup_warning)
                    completion_message += Localized(
                        L"\n\nDLL 尚未證明已卸載；整體停止未完成，請再次按停止重試排空與卸載。",
                        L"\n\nDLL 尚未证明已卸载；整体停止未完成，请再次点击停止重试排空与卸载。",
                        L"\n\nDLL unload is not yet proven; overall Stop is incomplete. Retry Stop to drain and unload it.");
            } else if (finalized) {
                completion_message = startup_was_cancelled ?
                    Localized(L"啟動已取消並保留目前證據。", L"启动已取消并保留当前证据。",
                              L"Startup was cancelled and current evidence was preserved.") :
                    Localized(L"語意證據、分析與匯出已完成。", L"语义证据、分析与导出已完成。",
                              L"Semantic evidence, analysis, and export are complete.");
            } else {
                completion_message = Localized(L"停止、完成 Session 或建立 Evidence ZIP 時發生錯誤：\n",
                    L"停止、完成会话或建立 Evidence ZIP 时发生错误：\n",
                    L"An error occurred while stopping, finalizing, or packaging the session:\n") + Utf8ToWide(combined);
            }
            NotifyNoThrow(callback, finalized, std::move(completion_message));
        } catch (const std::exception& exception) {
            {
                std::lock_guard lock(mutex_);
                SetStateLocked(CaptureState::CleanupRequired,
                    Localized(L"停止發生錯誤；可重試", L"停止发生错误；可重试", L"Stop failed; retry is available"));
            }
            AppendStartupLog(L"CaptureStopException", Utf8ToWide(exception.what()));
            NotifyNoThrow(callback, false, Utf8ToWide(exception.what()));
        } catch (...) {
            {
                std::lock_guard lock(mutex_);
                SetStateLocked(CaptureState::CleanupRequired,
                    Localized(L"停止發生未知錯誤；可重試", L"停止发生未知错误；可重试", L"Stop failed with an unknown error; retry is available"));
            }
            NotifyNoThrow(callback, false, Localized(L"停止時發生未知錯誤。", L"停止时发生未知错误。", L"An unknown stop error occurred."));
        }
    }

    void StartGameProcessTracker(const fs::path& session_path, const fs::path& game_path,
                                 std::uint64_t generation) {
        auto self = shared_from_this();
        std::thread([self, session_path, game_path, generation] {
            self->TrackGameProcessUntilStopped(session_path, game_path, generation);
        }).detach();
    }

    void TrackGameProcessUntilStopped(const fs::path& session_path, const fs::path& game_path,
                                       std::uint64_t generation) noexcept {
        try {
            DWORD last_process_id = 0;
            for (;;) {
                if (tracker_generation_.load() != generation || CancellationRequested()) return;
                ObserveLocalConsumerRuntime();
                const ConsumerState observed_enhanced_state =
                    enhanced_capture_.ObserveConsumerState();
                auto detection = GameProcessTracker::Detect(game_path, session_path);
                GameProcessDetectionResult elevated;
                bool elevated_consumer_failed = false;
                DWORD elevated_consumer_exit = STILL_ACTIVE;
                if (TryElevatedGameProbe(game_path, &elevated, &elevated_consumer_failed,
                                         &elevated_consumer_exit)) {
                    detection = std::move(elevated);
                    if (elevated_consumer_failed) {
                        TransitionToPostCaptureFallback("elevated ETW consumer exited after READY with code " +
                            std::to_string(elevated_consumer_exit));
                    }
                }
                if (detection.state == GameProcessState::Searching && last_process_id != 0)
                    detection.state = GameProcessState::Exited;
                const bool validated = detection.state == GameProcessState::Validated && detection.is_x86;
                bool attach_requested = false;
                {
                    std::lock_guard lock(mutex_);
                    if (tracker_generation_.load() != generation || snapshot_.session_path != session_path) return;
                    snapshot_.game_process_state = detection.state;
                    snapshot_.process_id = detection.process_id;
                    snapshot_.game_process_error = detection.win32_error;
                    snapshot_.parent_process_id = detection.parent_process_id;
                    snapshot_.process_creation_time =
                        (static_cast<std::uint64_t>(detection.process_creation_time.dwHighDateTime) << 32) |
                        detection.process_creation_time.dwLowDateTime;
                    snapshot_.game_process_path = detection.actual_path;
                    snapshot_.game_process = GameProcessStateText(detection.state);
                    snapshot_.consumer_state =
                        EnhancedCaptureConsumerStateAfterTargetDetection(
                            observed_enhanced_state, detection.state);
                    snapshot_.consumer = ConsumerStateText(snapshot_.consumer_state);
                    if (validated) {
                        snapshot_.capture_scope = Text(UiString::ScopeExact);
                    } else if (detection.state == GameProcessState::ArchitectureMismatch) {
                        snapshot_.capture_scope = Text(UiString::ScopeRejected);
                    } else if (detection.state == GameProcessState::Exited && last_process_id != 0) {
                        snapshot_.capture_scope = std::wstring(Localized(
                            L"已保留先前 32 位元 God2_opt.exe PID ",
                            L"已保留先前 32 位 God2_opt.exe PID ",
                            L"Retained previous x86 God2_opt.exe PID ")) +
                            std::to_wstring(last_process_id) + Localized(
                            L"；繼續搜尋新程序",
                            L"；继续查找新进程",
                            L"; searching for a new process");
                    } else {
                        snapshot_.capture_scope = Text(UiString::ScopeWaiting);
                    }
                    if (validated && SemanticWorkflowCanRun(snapshot_.backend_state)) {
                        last_process_id = detection.process_id;
                        snapshot_.analysis_state = post_capture_fallback_ ?
                            AnalysisState::PostCaptureFallback : AnalysisState::NearRealtime;
                        snapshot_.analysis = AnalysisStateText(snapshot_.analysis_state);
                        attach_requested = enhanced_capture_.Requested();
                        if (attach_requested) {
                            snapshot_.consumer_state = ConsumerState::Starting;
                            snapshot_.consumer = ConsumerStateText(snapshot_.consumer_state);
                            SetStateLocked(CaptureState::AttachingEnhancedCapture,
                                Localized(L"已驗證 God2_opt.exe；正在附加 x86 增強擷取", L"已验证 God2_opt.exe；正在附加 x86 增强捕获", L"God2_opt.exe validated; attaching x86 enhanced capture"));
                        } else if (!post_capture_fallback_) {
                            SetStateLocked(CaptureState::Capturing, Text(UiString::Capturing));
                        }
                    } else if (!post_capture_fallback_ && SemanticWorkflowCanRun(snapshot_.backend_state) &&
                               snapshot_.state != CaptureState::Stopping && snapshot_.state != CaptureState::Cancelling) {
                        SetStateLocked(detection.state == GameProcessState::ProcessDetected ||
                                           detection.state == GameProcessState::WindowDetected ||
                                           detection.state == GameProcessState::AccessDenied ?
                                           CaptureState::ValidatingGod2Process : CaptureState::WaitingForGod2Process,
                            GameProcessStateText(detection.state));
                    }
                }
                analysis_.SetTargetProcess(detection.process_id, validated);
                if (validated && attach_requested) {
                    std::string enhanced_error;
                    const bool enhanced_active = enhanced_capture_.TryAttach(detection.process_id, &enhanced_error);
                    {
                        std::lock_guard lock(mutex_);
                        if (snapshot_.process_id == detection.process_id &&
                            snapshot_.state != CaptureState::Stopping && snapshot_.state != CaptureState::Cancelling) {
                            snapshot_.capture_scope += enhanced_active ?
                                Localized(L" + x86 DLL 增強擷取", L" + x86 DLL 增强捕获", L" + x86 DLL enhanced capture") :
                                Localized(L"（DLL 增強失敗；不會改用 raw payload）", L"（DLL 增强失败；不会改用 raw payload）", L" (DLL enhancement unavailable; no raw-payload fallback)");
                            snapshot_.consumer_state = enhanced_active ?
                                ConsumerState::Ready : ConsumerState::EvidenceBlocked;
                            snapshot_.consumer = enhanced_active ?
                                Localized(L"x86 DLL Ready（遮罩語意通道）", L"x86 DLL Ready（遮罩语义通道）", L"x86 DLL Ready (masked semantic channel)") :
                                ConsumerStateText(snapshot_.consumer_state);
                            if (enhanced_active && !post_capture_fallback_)
                                SetStateLocked(CaptureState::Capturing, Text(UiString::Capturing));
                        }
                    }
                    if (!enhanced_active)
                        TransitionToPostCaptureFallback("x86 enhanced capture attach failed: " + enhanced_error);
                }
                if (WaitForSingleObject(cancel_event_, 500) == WAIT_OBJECT_0) return;
            }
        } catch (const std::exception& exception) {
            try { AppendStartupLog(L"GameProcessTrackerException", Utf8ToWide(exception.what())); } catch (...) {}
        } catch (...) {
            AppendStartupLog(L"GameProcessTrackerException", L"Unknown exception");
        }
    }

    struct MetricLineState {
        fs::path path;
        std::uint64_t offset = 0;
        std::uint64_t lines = 0;
    };

    std::uint64_t IncrementalLineCount(const fs::path& path) {
        auto iterator = std::find_if(metrics_line_states_.begin(), metrics_line_states_.end(),
            [&](const MetricLineState& state) { return state.path == path; });
        if (iterator == metrics_line_states_.end()) {
            metrics_line_states_.push_back(MetricLineState{path});
            iterator = std::prev(metrics_line_states_.end());
        }
        std::error_code size_error;
        const auto size = fs::file_size(path, size_error);
        if (size_error) {
            if (size_error == std::errc::no_such_file_or_directory) {
                iterator->offset = 0;
                iterator->lines = 0;
                return 0;
            }
            throw fs::filesystem_error("cannot inspect metrics input", path, size_error);
        }
        if (size < iterator->offset) {
            iterator->offset = 0;
            iterator->lines = 0;
        }
        if (size == iterator->offset) return iterator->lines;
        std::ifstream stream(path, std::ios::binary);
        if (!stream) throw std::runtime_error("cannot open metrics input: " + WideToUtf8(path.wstring()));
        stream.seekg(static_cast<std::streamoff>(iterator->offset));
        std::vector<char> buffer(64 * 1024);
        while (iterator->offset < size && stream) {
            const auto wanted = static_cast<std::streamsize>(
                std::min<std::uintmax_t>(buffer.size(), size - iterator->offset));
            stream.read(buffer.data(), wanted);
            const auto read = stream.gcount();
            if (read <= 0) break;
            iterator->lines += static_cast<std::uint64_t>(
                std::count(buffer.data(), buffer.data() + read, '\n'));
            iterator->offset += static_cast<std::uint64_t>(read);
        }
        return iterator->lines;
    }

    static void NotifyNoThrow(const Callback& callback, bool success, std::wstring message) noexcept {
        try {
            callback(success, std::move(message));
        } catch (const std::exception& exception) {
            try { AppendStartupLog(L"ControllerCallbackException", Utf8ToWide(exception.what())); } catch (...) {}
        } catch (...) {
            try { AppendStartupLog(L"ControllerCallbackException", L"Unknown exception"); } catch (...) {}
        }
    }

    void FailStart(const fs::path& session_path = {}, std::wstring detail = {}) {
        std::lock_guard lock(mutex_);
        snapshot_.backend_state = BackendState::Failed;
        snapshot_.backend = BackendStateText(snapshot_.backend_state);
        if (detail.empty()) detail = Localized(L"啟動失敗", L"启动失败", L"Start failed");
        SetStateLocked(CaptureState::Failed, std::move(detail));
        if (!session_path.empty()) snapshot_.session_path = session_path;
    }

    void MarkStartCleanupRequired(const fs::path& session_path) {
        std::lock_guard lock(mutex_);
        SetStateLocked(CaptureState::CleanupRequired,
            Localized(L"啟動未完成，但語意取證元件仍需清理；請按停止", L"启动未完成，但语义取证组件仍需清理；请点击停止", L"Startup did not complete, but semantic evidence components require cleanup; click Stop"));
        snapshot_.session_path = session_path;
    }

    void StartEvidenceWorker(const BackendCapabilities& capabilities, std::string* error) {
        if (backend_ == nullptr) return;
        std::optional<CaptureWorkerIdentity> started_worker;
        if (backend_->Name() == "EtwNetworkTraceBackend" && capabilities.supports_realtime_events) {
            started_worker = StartEtwConsumerProcess(request_.session_path, cancel_event_,
                                                     cancel_event_name_, error);
        } else if (backend_->Name() == "PktMonCaptureBackend" && capabilities.supports_drop_statistics) {
            started_worker = StartPktMonCounterProcess(request_.session_path, error);
        }
        std::lock_guard worker_lock(evidence_worker_mutex_);
        evidence_worker_ = std::move(started_worker);
    }

    bool HasEvidenceWorker() const {
        std::lock_guard worker_lock(evidence_worker_mutex_);
        return evidence_worker_.has_value();
    }

    void ObserveLocalConsumerRuntime() {
        if (analysis_.NearRealtimeFailed()) {
            TransitionToPostCaptureFallback("near-realtime analysis stopped; offline recovery is scheduled");
        }
        std::optional<CaptureWorkerIdentity> identity;
        {
            std::lock_guard worker_lock(evidence_worker_mutex_);
            identity = evidence_worker_;
        }
        if (!identity || identity->worker_type != "etw-consumer" || !identity->ready_confirmed ||
            CancellationRequested()) return;
        const auto runtime = QueryCaptureWorkerRuntime(*identity);
        if (runtime.identity_valid && !runtime.running) {
            TransitionToPostCaptureFallback("ETW consumer exited after READY with code " +
                std::to_string(runtime.exit_code));
        }
    }

    void TransitionToPostCaptureFallback(std::string detail) {
        if (CancellationRequested()) return;
        const bool was_fallback = post_capture_fallback_.exchange(true);
        if (!was_fallback) AppendStartupLog(L"ConsumerRuntimeFallback", Utf8ToWide(detail));
        std::lock_guard lock(mutex_);
        if (!SemanticWorkflowCanRun(snapshot_.backend_state) || snapshot_.state == CaptureState::Stopping ||
            snapshot_.state == CaptureState::Cancelling) return;
        snapshot_.analysis_state = AnalysisState::PostCaptureFallback;
        snapshot_.analysis = AnalysisStateText(snapshot_.analysis_state);
        SetStateLocked(CaptureState::PostCaptureFallback,
            Localized(L"x86 遮罩即時語意 Consumer 已停止；raw payload 維持 EvidenceBlockedPrivacyPolicy，只會完成已遮罩的安全證據。",
                      L"x86 遮罩实时语义 Consumer 已停止；raw payload 保持 EvidenceBlockedPrivacyPolicy，仅会完成已遮罩的安全证据。",
                      L"The x86 masked live semantic consumer stopped; raw payload remains EvidenceBlockedPrivacyPolicy, and only already-masked safe evidence will be finalized."));
    }

    CaptureResult StartElevatedWorker(const fs::path& session_path, const std::string& backend_name) {
        const std::string nonce = NewId();
        const std::wstring pipe_name = L"\\\\.\\pipe\\God2PacketCapture-" + Utf8ToWide(nonce);
        HANDLE pipe = CreateNamedPipeW(pipe_name.c_str(), PIPE_ACCESS_DUPLEX | FILE_FLAG_FIRST_PIPE_INSTANCE,
                                       PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_NOWAIT, 1, 4096, 4096, 60'000, nullptr);
        if (pipe == INVALID_HANDLE_VALUE) {
            const DWORD pipe_error = GetLastError();
            return {false, pipe_error, "cannot create privileged worker pipe"};
        }
        const std::wstring parameters = L"--internal-worker elevated-capture --session " +
            QuoteArgument(session_path.wstring()) + L" --backend " + QuoteArgument(Utf8ToWide(backend_name)) +
            L" --pipe " + QuoteArgument(pipe_name) + L" --nonce " + QuoteArgument(Utf8ToWide(nonce)) +
            L" --cancel-event " + QuoteArgument(cancel_event_name_);
        SHELLEXECUTEINFOW elevation{};
        elevation.cbSize = sizeof(elevation);
        elevation.fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_FLAG_NO_UI;
        elevation.lpVerb = L"runas";
        const auto executable = ExecutablePath();
        elevation.lpFile = executable.c_str();
        elevation.lpParameters = parameters.c_str();
        elevation.nShow = SW_HIDE;
        if (!ShellExecuteExW(&elevation)) {
            const DWORD code = GetLastError();
            CloseHandle(pipe);
            return {false, code, WideToUtf8(ErrorText(code))};
        }
        if (elevation.hProcess == nullptr) {
            CloseHandle(pipe);
            return {false, ERROR_INVALID_HANDLE, "privileged worker returned no process handle"};
        }
        {
            std::lock_guard lock(mutex_);
            SetStateLocked(CaptureState::StartingElevatedWorker,
                Localized(L"管理員授權完成；正在啟動 x86 語意 Worker", L"管理员授权完成；正在启动 x86 语义 Worker", L"Administrator approval complete; starting x86 semantic worker"));
        }
        BOOL connected = FALSE;
        DWORD connection_error = ERROR_PIPE_LISTENING;
        for (int attempt = 0; attempt < 600 && !connected; ++attempt) {
            if (CancellationRequested()) {
                connection_error = ERROR_CANCELLED;
                break;
            }
            connected = ConnectNamedPipe(pipe, nullptr);
            connection_error = connected ? ERROR_SUCCESS : GetLastError();
            if (connection_error == ERROR_PIPE_CONNECTED) connected = TRUE;
            else if (connection_error != ERROR_PIPE_LISTENING && connection_error != ERROR_NO_DATA) break;
            if (!connected && WaitForSingleObject(elevation.hProcess, 0) == WAIT_OBJECT_0) break;
            if (!connected) Sleep(100);
        }
        if (!connected) {
            const DWORD code = connection_error == ERROR_SUCCESS ? ERROR_TIMEOUT : connection_error;
            if (WaitForSingleObject(elevation.hProcess, 2'000) == WAIT_TIMEOUT)
                TerminateProcess(elevation.hProcess, code);
            CloseHandle(elevation.hProcess);
            CloseHandle(pipe);
            return {false, code, "privileged worker could not connect"};
        }
        // Once connected, the worker either owns a real backend or hosts only
        // the elevated exact-build x86 semantic workflow. Wait for an explicit
        // bound result before deciding which state applies.
        const auto response = ReadPipeMessage(pipe, 120'000, elevation.hProcess);
        const std::string cleanup_prefix = "CLEANUP_REQUIRED\t" + nonce + "\t";
        const std::string cancelled_prefix = "CANCELLED_STOPPED\t" + nonce + "\t";
        if (response && response->rfind(cancelled_prefix, 0) == 0) {
            if (WaitForSingleObject(elevation.hProcess, 10'000) == WAIT_TIMEOUT) {
                // Worker cleanup was explicitly confirmed, so bounded
                // termination cannot strand a semantic evidence workflow.
                TerminateProcess(elevation.hProcess, ERROR_TIMEOUT);
                WaitForSingleObject(elevation.hProcess, 5'000);
            }
            CloseHandle(elevation.hProcess);
            CloseHandle(pipe);
            return {false, ERROR_CANCELLED, response->substr(cancelled_prefix.size())};
        }
        if (response && response->rfind(cleanup_prefix, 0) == 0) {
            // A required post-start component failed and the worker could not
            // yet confirm cleanup. Preserve the pipe and process so StartAsync
            // can retry Stop and expose CleanupRequired to the GUI.
            {
                std::lock_guard pipe_lock(worker_pipe_mutex_);
                worker_process_ = elevation.hProcess;
                worker_pipe_ = pipe;
                worker_nonce_ = nonce;
            }
            return {false, ERROR_BUSY, response->substr(cleanup_prefix.size())};
        }
        const std::string started_prefix = "STARTED\t" + nonce + "\t";
        const std::string semantic_prefix = "SEMANTIC_READY\t" + nonce + "\t";
        const bool raw_started = response && response->rfind(started_prefix, 0) == 0;
        const bool semantic_ready = response && response->rfind(semantic_prefix, 0) == 0;
        if (!raw_started && !semantic_ready) {
            const std::string message = response ? *response :
                (CancellationRequested() ? "privileged worker startup was cancelled" :
                                           "privileged worker returned no start result");
            if (WaitForSingleObject(elevation.hProcess, 0) == WAIT_TIMEOUT) {
                std::lock_guard pipe_lock(worker_pipe_mutex_);
                worker_process_ = elevation.hProcess;
                worker_pipe_ = pipe;
                worker_nonce_ = nonce;
                return {false, ERROR_BUSY, message + "; privileged worker remains available for Stop cleanup"};
            }
            CloseHandle(elevation.hProcess);
            CloseHandle(pipe);
            return {false, ERROR_GEN_FAILURE, message};
        }
        {
            std::lock_guard pipe_lock(worker_pipe_mutex_);
            worker_process_ = elevation.hProcess;
            worker_pipe_ = pipe;
            worker_nonce_ = nonce;
        }
        const auto detail = response->substr((semantic_ready ? semantic_prefix : started_prefix).size());
        worker_consumer_fallback_ = detail.rfind("POST_CAPTURE_FALLBACK:", 0) == 0;
        return {true, 0, detail};
    }

    bool TryElevatedGameProbe(const fs::path& game_path, GameProcessDetectionResult* result,
                              bool* consumer_failed, DWORD* consumer_exit_code) {
        if (result == nullptr) return false;
        if (consumer_failed != nullptr) *consumer_failed = false;
        if (consumer_exit_code != nullptr) *consumer_exit_code = STILL_ACTIVE;
        std::lock_guard pipe_lock(worker_pipe_mutex_);
        if (worker_pipe_ == INVALID_HANDLE_VALUE || worker_process_ == nullptr ||
            WaitForSingleObject(worker_process_, 0) != WAIT_TIMEOUT) return false;
        if (!WritePipeMessage(worker_pipe_, "PROBE_GAME\t" + worker_nonce_ + "\t" +
                             WideToUtf8(game_path.wstring()))) return false;
        const auto response = ReadPipeMessage(worker_pipe_, 3'000, worker_process_, cancel_event_);
        if (!response) return false;
        std::vector<std::string> fields;
        std::size_t start = 0;
        for (;;) {
            const auto delimiter = response->find('\t', start);
            fields.push_back(response->substr(start, delimiter == std::string::npos ?
                std::string::npos : delimiter - start));
            if (delimiter == std::string::npos) break;
            start = delimiter + 1;
        }
        if ((fields.size() != 10 && fields.size() != 12) || fields[0] != "GAME_PROCESS" ||
            fields[1] != worker_nonce_) return false;
        try {
            result->process_id = static_cast<DWORD>(std::stoul(fields[2]));
            result->actual_path = Utf8ToWide(fields[3]);
            result->is_x86 = fields[4] == "true";
            result->parent_process_id = static_cast<DWORD>(std::stoul(fields[5]));
            const auto creation = std::stoull(fields[6]);
            result->process_creation_time.dwLowDateTime = static_cast<DWORD>(creation & 0xffffffffu);
            result->process_creation_time.dwHighDateTime = static_cast<DWORD>(creation >> 32);
            result->state = static_cast<GameProcessState>(std::stoi(fields[7]));
            result->win32_error = static_cast<DWORD>(std::stoul(fields[8]));
            result->game_window = reinterpret_cast<HWND>(static_cast<std::uintptr_t>(std::stoull(fields[9])));
            if (fields.size() == 12) {
                if (consumer_failed != nullptr) *consumer_failed = fields[10] == "Failed";
                if (consumer_exit_code != nullptr) *consumer_exit_code = static_cast<DWORD>(std::stoul(fields[11]));
            }
            return true;
        } catch (...) {
            return false;
        }
    }

    bool StopBackendOnly(std::string* error) {
        {
            std::unique_lock pipe_lock(worker_pipe_mutex_);
            if (worker_pipe_ != INVALID_HANDLE_VALUE) {
                if (!WritePipeMessage(worker_pipe_, "STOP\t" + worker_nonce_)) {
                    if (error) *error = "could not request the privileged semantic worker to stop";
                    return false;
                }
                // A PROBE_GAME request can finish just after its bounded reader
                // timed out.  Its delayed GAME_PROCESS reply then remains queued
                // ahead of the STOPPED reply.  Treating that valid diagnostic as
                // the stop result leaves the GUI in CleanupRequired even though
                // the backend and injected probe stopped correctly.  Drain only
                // authenticated replies for this worker/nonce while preserving
                // the original bounded stop deadline.
                const ULONGLONG stop_deadline = GetTickCount64() + 240'000;
                std::optional<std::string> response;
                for (;;) {
                    const ULONGLONG now = GetTickCount64();
                    if (now >= stop_deadline) break;
                    const auto remaining = static_cast<DWORD>(
                        std::min<ULONGLONG>(stop_deadline - now, MAXDWORD));
                    response = ReadPipeMessage(worker_pipe_, remaining, worker_process_);
                    if (!response || !IsDelayedGameProbeResponse(*response, worker_nonce_)) break;
                    AppendStartupLog(L"DelayedGameProbeReplyDrained", Utf8ToWide(*response));
                }
                const bool success = response &&
                    (response->rfind("STOPPED\t" + worker_nonce_ + "\t", 0) == 0 ||
                     response->rfind("SEMANTIC_STOPPED\t" + worker_nonce_ + "\t", 0) == 0);
                if (!success) {
                    if (error) *error = response ? *response : "privileged semantic worker returned no stop result";
                    return false;
                }
                const auto warning = response->find("\tWARNING:");
                if (warning != std::string::npos && error != nullptr)
                    *error = response->substr(warning + 1);
                if (worker_process_ != nullptr && WaitForSingleObject(worker_process_, 10'000) == WAIT_TIMEOUT) {
                    TerminateProcess(worker_process_, ERROR_TIMEOUT);
                    WaitForSingleObject(worker_process_, 5'000);
                    if (error) {
                        if (!error->empty()) *error += "; ";
                        *error += "privileged worker required bounded termination after confirming semantic workflow cleanup";
                    }
                }
                CloseWorkerHandlesLocked();
                return true;
            }
        }
        if (backend_ == nullptr) return true;
        const auto stopped = backend_->Stop(request_);
        if (!stopped.success) {
            if (error) *error = stopped.message;
            return false;
        }
        std::string completion_warning;
        if (stopped.exit_code != ERROR_SUCCESS) completion_warning = stopped.message;
        std::optional<CaptureWorkerIdentity> evidence_worker;
        {
            std::lock_guard worker_lock(evidence_worker_mutex_);
            evidence_worker = evidence_worker_;
        }
        std::string worker_error;
        const bool evidence_ok = !evidence_worker || WaitForCaptureWorker(*evidence_worker, 30'000, &worker_error);
        {
            std::lock_guard worker_lock(evidence_worker_mutex_);
            evidence_worker_.reset();
        }
        backend_.reset();
        if (!evidence_ok) {
            if (!completion_warning.empty()) completion_warning += "; ";
            completion_warning += worker_error;
        }
        if (!completion_warning.empty() && error) *error = completion_warning;
        return true;
    }

    bool HasElevatedWorker() const {
        std::lock_guard pipe_lock(worker_pipe_mutex_);
        return worker_pipe_ != INVALID_HANDLE_VALUE || worker_process_ != nullptr;
    }

    void CloseWorkerHandles() {
        std::lock_guard pipe_lock(worker_pipe_mutex_);
        CloseWorkerHandlesLocked();
    }

    void CloseWorkerHandlesLocked() {
        if (worker_pipe_ != INVALID_HANDLE_VALUE) {
            CloseHandle(worker_pipe_);
            worker_pipe_ = INVALID_HANDLE_VALUE;
        }
        if (worker_process_ != nullptr) {
            CloseHandle(worker_process_);
            worker_process_ = nullptr;
        }
        worker_nonce_.clear();
    }

    mutable std::mutex mutex_;
    CaptureSnapshot snapshot_;
    std::unique_ptr<ICaptureBackend> backend_;
    CaptureRequest request_;
    std::optional<CaptureWorkerIdentity> evidence_worker_;
    mutable std::mutex evidence_worker_mutex_;
    AnalysisController analysis_;
    EnhancedCaptureManager enhanced_capture_;
    HANDLE worker_process_ = nullptr;
    HANDLE worker_pipe_ = INVALID_HANDLE_VALUE;
    std::string worker_nonce_;
    mutable std::mutex worker_pipe_mutex_;
    HANDLE cancel_event_ = nullptr;
    HANDLE startup_done_event_ = nullptr;
    std::wstring cancel_event_name_;
    std::atomic<std::uint64_t> tracker_generation_{0};
    std::atomic<bool> post_capture_fallback_{false};
    std::atomic<bool> startup_cancel_requested_{false};
    bool worker_consumer_fallback_ = false;
    fs::path game_path_;
    std::atomic<bool> metrics_refresh_running_{false};
    fs::path metrics_session_path_;
    std::vector<MetricLineState> metrics_line_states_;
};

struct MainControlLayout {
    HWND control = nullptr;
    RECT design_bounds{};
};

struct AppState {
    HWND window = nullptr;
    HWND tooltip = nullptr;
    HFONT font = nullptr;
    HFONT title_font = nullptr;
    HFONT small_font = nullptr;
    HBRUSH background_brush = nullptr;
    HBRUSH surface_brush = nullptr;
    std::vector<MainControlLayout> control_layout;
    double layout_scale = 1.0;
    double font_scale = 0.0;
    int layout_offset_x = 0;
    int layout_offset_y = 0;
    std::wstring tooltip_text;
    std::shared_ptr<CaptureController> controller = std::make_shared<CaptureController>();
    bool close_after_stop = false;
};

struct ObservationWindowState {
    fs::path session_path;
    bool entity_view = false;
    HWND edit = nullptr;
};

HWND Control(HWND parent, const wchar_t* class_name, const wchar_t* text, DWORD style,
             int x, int y, int width, int height, int id, DWORD extended = 0) {
    if (lstrcmpW(class_name, L"STATIC") == 0) style |= SS_CENTERIMAGE;
    return CreateWindowExW(extended, class_name, text, WS_CHILD | WS_VISIBLE | style,
                           x, y, width, height, parent, reinterpret_cast<HMENU>(static_cast<INT_PTR>(id)),
                           GetModuleHandleW(nullptr), nullptr);
}

void AddTooltip(HWND tooltip, HWND control) {
    if (tooltip == nullptr || control == nullptr) return;
    TOOLINFOW info{};
    info.cbSize = sizeof(info);
    info.uFlags = TTF_IDISHWND | TTF_SUBCLASS;
    info.hwnd = GetParent(control);
    info.uId = reinterpret_cast<UINT_PTR>(control);
    info.lpszText = LPSTR_TEXTCALLBACKW;
    SendMessageW(tooltip, TTM_ADDTOOLW, 0, reinterpret_cast<LPARAM>(&info));
}

void DrawModernButton(const DRAWITEMSTRUCT& item) {
    const bool disabled = (item.itemState & ODS_DISABLED) != 0;
    const bool pressed = (item.itemState & ODS_SELECTED) != 0;
    const int id = static_cast<int>(item.CtlID);
    COLORREF background = kUiSurface;
    COLORREF border = RGB(196, 207, 220);
    COLORREF foreground = kUiText;
    if (id == IDC_START_CAPTURE) {
        background = pressed ? kUiAccentPressed : kUiAccent;
        border = background;
        foreground = RGB(255, 255, 255);
    } else if (id == IDC_STOP_CAPTURE) {
        background = pressed ? RGB(174, 38, 38) : RGB(196, 43, 43);
        border = background;
        foreground = RGB(255, 255, 255);
    } else if (pressed) {
        background = RGB(238, 242, 247);
    }
    if (disabled) {
        background = RGB(238, 242, 247);
        border = RGB(222, 228, 236);
        foreground = RGB(148, 163, 184);
    }
    HBRUSH fill = CreateSolidBrush(background);
    HPEN pen = CreatePen(PS_SOLID, 1, border);
    HGDIOBJ old_brush = SelectObject(item.hDC, fill);
    HGDIOBJ old_pen = SelectObject(item.hDC, pen);
    RoundRect(item.hDC, item.rcItem.left, item.rcItem.top, item.rcItem.right, item.rcItem.bottom, 7, 7);
    SelectObject(item.hDC, old_brush);
    SelectObject(item.hDC, old_pen);
    DeleteObject(fill);
    DeleteObject(pen);
    wchar_t caption[256]{};
    GetWindowTextW(item.hwndItem, caption, static_cast<int>(std::size(caption)));
    SetBkMode(item.hDC, TRANSPARENT);
    SetTextColor(item.hDC, foreground);
    HGDIOBJ old_font = SelectObject(item.hDC, reinterpret_cast<HFONT>(
        SendMessageW(item.hwndItem, WM_GETFONT, 0, 0)));
    RECT text_bounds = item.rcItem;
    if (pressed) OffsetRect(&text_bounds, 0, 1);
    DrawTextW(item.hDC, caption, -1, &text_bounds, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
    if (old_font != nullptr) SelectObject(item.hDC, old_font);
    if ((item.itemState & ODS_FOCUS) != 0) {
        RECT focus = item.rcItem;
        InflateRect(&focus, -4, -4);
        DrawFocusRect(item.hDC, &focus);
    }
}

void ApplyFont(HWND window, HFONT font) {
    EnumChildWindows(window, [](HWND child, LPARAM parameter) -> BOOL {
        SendMessageW(child, WM_SETFONT, parameter, TRUE);
        return TRUE;
    }, reinterpret_cast<LPARAM>(font));
}

RECT FittedMainWindowBounds(const RECT& work_area) {
    const int available_width = (std::max)(1,
        static_cast<int>(work_area.right - work_area.left) - 16);
    const int available_height = (std::max)(1,
        static_cast<int>(work_area.bottom - work_area.top) - 16);
    const double scale = (std::min)({1.0,
        static_cast<double>(available_width) / kMainWindowDesignWidth,
        static_cast<double>(available_height) / kMainWindowDesignHeight});
    const int width = (std::max)(1, static_cast<int>(std::floor(kMainWindowDesignWidth * scale)));
    const int height = (std::max)(1, static_cast<int>(std::floor(kMainWindowDesignHeight * scale)));
    const int work_width = static_cast<int>(work_area.right - work_area.left);
    const int work_height = static_cast<int>(work_area.bottom - work_area.top);
    const int left = static_cast<int>(work_area.left) + (work_width - width) / 2;
    const int top = static_cast<int>(work_area.top) + (work_height - height) / 2;
    return RECT{left, top, left + width, top + height};
}

void FitMainWindowToCurrentWorkArea(HWND window) {
    MONITORINFO monitor_info{};
    monitor_info.cbSize = sizeof(monitor_info);
    const HMONITOR monitor = MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST);
    if (monitor == nullptr || !GetMonitorInfoW(monitor, &monitor_info)) return;
    const RECT fitted = FittedMainWindowBounds(monitor_info.rcWork);
    SetWindowPos(window, nullptr, fitted.left, fitted.top,
                 fitted.right - fitted.left, fitted.bottom - fitted.top,
                 SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOZORDER);
}

RECT ScaleMainDesignRect(const RECT& design, double scale, int offset_x, int offset_y) {
    const int left = offset_x + static_cast<int>(std::lround(design.left * scale));
    const int top = offset_y + static_cast<int>(std::lround(design.top * scale));
    const int right = offset_x + static_cast<int>(std::lround(design.right * scale));
    const int bottom = offset_y + static_cast<int>(std::lround(design.bottom * scale));
    return RECT{left, top, (std::max)(left + 1, right), (std::max)(top + 1, bottom)};
}

void CaptureMainControlLayout(AppState* state) {
    state->control_layout.clear();
    for (HWND child = GetWindow(state->window, GW_CHILD); child != nullptr;
         child = GetWindow(child, GW_HWNDNEXT)) {
        RECT bounds{};
        if (!GetWindowRect(child, &bounds)) continue;
        MapWindowPoints(HWND_DESKTOP, state->window, reinterpret_cast<POINT*>(&bounds), 2);
        state->control_layout.push_back({child, bounds});
    }
}

void ApplyScaledMainFonts(AppState* state, double scale) {
    if (std::abs(state->font_scale - scale) < 0.02) return;
    const int normal_height = -(std::max)(8, static_cast<int>(std::lround(13.0 * scale)));
    const int title_height = -(std::max)(12, static_cast<int>(std::lround(21.0 * scale)));
    const int small_height = -(std::max)(8, static_cast<int>(std::lround(11.0 * scale)));
    HFONT normal = CreateFontW(normal_height, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
        DEFAULT_PITCH | FF_DONTCARE, L"Segoe UI");
    HFONT title = CreateFontW(title_height, 0, 0, 0, FW_SEMIBOLD, FALSE, FALSE, FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
        DEFAULT_PITCH | FF_DONTCARE, L"Segoe UI");
    HFONT small_font_handle = CreateFontW(small_height, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
        DEFAULT_PITCH | FF_DONTCARE, L"Segoe UI");
    if (normal == nullptr || title == nullptr || small_font_handle == nullptr) {
        if (normal != nullptr) DeleteObject(normal);
        if (title != nullptr) DeleteObject(title);
        if (small_font_handle != nullptr) DeleteObject(small_font_handle);
        return;
    }
    ApplyFont(state->window, normal);
    SendMessageW(GetDlgItem(state->window, IDC_TITLE), WM_SETFONT,
                 reinterpret_cast<WPARAM>(title), TRUE);
    SendMessageW(GetDlgItem(state->window, IDC_SUBTITLE), WM_SETFONT,
                 reinterpret_cast<WPARAM>(small_font_handle), TRUE);
    SendMessageW(GetDlgItem(state->window, IDC_AUTHOR), WM_SETFONT,
                 reinterpret_cast<WPARAM>(small_font_handle), TRUE);
    if (state->font != nullptr) DeleteObject(state->font);
    if (state->title_font != nullptr) DeleteObject(state->title_font);
    if (state->small_font != nullptr) DeleteObject(state->small_font);
    state->font = normal;
    state->title_font = title;
    state->small_font = small_font_handle;
    state->font_scale = scale;
}

void LayoutMainControls(AppState* state) {
    if (state == nullptr || state->window == nullptr || state->control_layout.empty()) return;
    RECT client{};
    if (!GetClientRect(state->window, &client)) return;
    const int client_width = (std::max)(1, static_cast<int>(client.right - client.left));
    const int client_height = (std::max)(1, static_cast<int>(client.bottom - client.top));
    const double scale = (std::min)({1.0,
        static_cast<double>(client_width) / kMainClientDesignWidth,
        static_cast<double>(client_height) / kMainClientDesignHeight});
    const int content_width = static_cast<int>(std::lround(kMainClientDesignWidth * scale));
    const int content_height = static_cast<int>(std::lround(kMainClientDesignHeight * scale));
    state->layout_scale = scale;
    state->layout_offset_x = (client_width - content_width) / 2;
    state->layout_offset_y = (client_height - content_height) / 2;

    HDWP deferred = BeginDeferWindowPos(static_cast<int>(state->control_layout.size()));
    for (const auto& item : state->control_layout) {
        const RECT target = ScaleMainDesignRect(item.design_bounds, scale,
                                                 state->layout_offset_x,
                                                 state->layout_offset_y);
        if (deferred != nullptr) {
            deferred = DeferWindowPos(deferred, item.control, nullptr, target.left, target.top,
                target.right - target.left, target.bottom - target.top,
                SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOZORDER);
        } else {
            MoveWindow(item.control, target.left, target.top,
                       target.right - target.left, target.bottom - target.top, TRUE);
        }
    }
    if (deferred != nullptr) EndDeferWindowPos(deferred);
    ApplyScaledMainFonts(state, scale);
    InvalidateRect(state->window, nullptr, TRUE);
}

bool EssentialControlsFitSimulatedWorkArea(const RECT& work_area) {
    const RECT fitted = FittedMainWindowBounds(work_area);
    const int outer_width = fitted.right - fitted.left;
    const int outer_height = fitted.bottom - fitted.top;
    // Use the Win7-compatible non-client design delta as a conservative
    // simulation.  The runtime path uses the actual client rectangle.
    const int client_width = (std::max)(1, outer_width -
        (kMainWindowDesignWidth - kMainClientDesignWidth));
    const int client_height = (std::max)(1, outer_height -
        (kMainWindowDesignHeight - kMainClientDesignHeight));
    const double scale = (std::min)({1.0,
        static_cast<double>(client_width) / kMainClientDesignWidth,
        static_cast<double>(client_height) / kMainClientDesignHeight});
    const int offset_x = (client_width - static_cast<int>(std::lround(kMainClientDesignWidth * scale))) / 2;
    const int offset_y = (client_height - static_cast<int>(std::lround(kMainClientDesignHeight * scale))) / 2;
    const RECT essentials[] = {
        {670, 11, 700, 38},   // help
        {26, 337, 342, 375},  // start
        {352, 337, 674, 375}, // stop
        {140, 448, 672, 473}  // output
    };
    for (const RECT& design : essentials) {
        const RECT mapped = ScaleMainDesignRect(design, scale, offset_x, offset_y);
        if (mapped.left < 0 || mapped.top < 0 || mapped.right > client_width ||
            mapped.bottom > client_height || mapped.right <= mapped.left ||
            mapped.bottom <= mapped.top) return false;
    }
    return fitted.left >= work_area.left && fitted.top >= work_area.top &&
        fitted.right <= work_area.right && fitted.bottom <= work_area.bottom;
}

bool EmbeddedManifestUsesWin7SystemDpiAwareness() {
    HMODULE module = GetModuleHandleW(nullptr);
    HRSRC resource = FindResourceW(module, MAKEINTRESOURCEW(1), RT_MANIFEST);
    if (resource == nullptr) return false;
    const DWORD size = SizeofResource(module, resource);
    HGLOBAL loaded = LoadResource(module, resource);
    const void* bytes = loaded != nullptr ? LockResource(loaded) : nullptr;
    if (bytes == nullptr || size == 0) return false;
    const std::string manifest(static_cast<const char*>(bytes), size);
    return manifest.find("dpiAware") != std::string::npos &&
        manifest.find(">true</dpiAware>") != std::string::npos &&
        manifest.find("PerMonitor") == std::string::npos &&
        manifest.find("dpiAwareness") == std::string::npos;
}

void ApplyModernWindowStyle(HWND window) {
    SetWindowTheme(window, L"Explorer", nullptr);
    const auto os = DetectOsVersion();
    if (os.build >= 22'000) {
        const DWM_WINDOW_CORNER_PREFERENCE corners = DWMWCP_ROUND;
        DwmSetWindowAttribute(window, DWMWA_WINDOW_CORNER_PREFERENCE, &corners, sizeof(corners));
        const COLORREF caption = kUiBackground;
        const COLORREF border = kUiBorder;
        const COLORREF text = kUiText;
        DwmSetWindowAttribute(window, DWMWA_CAPTION_COLOR, &caption, sizeof(caption));
        DwmSetWindowAttribute(window, DWMWA_BORDER_COLOR, &border, sizeof(border));
        DwmSetWindowAttribute(window, DWMWA_TEXT_COLOR, &text, sizeof(text));
        const DWM_SYSTEMBACKDROP_TYPE backdrop = DWMSBT_MAINWINDOW;
        DwmSetWindowAttribute(window, DWMWA_SYSTEMBACKDROP_TYPE, &backdrop, sizeof(backdrop));
    }
}

void ApplyThemeToChildren(HWND parent) {
    HWND child = nullptr;
    while ((child = FindWindowExW(parent, child, nullptr, nullptr)) != nullptr)
        SetWindowTheme(child, L"Explorer", nullptr);
}

bool IsUiLabel(int control_id) {
    switch (control_id) {
    case IDC_LANGUAGE_LABEL:
    case IDC_LAUNCHER_LABEL:
    case IDC_OS_LABEL:
    case IDC_ADMIN_LABEL:
    case IDC_BACKEND_LABEL:
    case IDC_PROCESS_LABEL:
    case IDC_PID_LABEL:
    case IDC_STATUS_LABEL:
    case IDC_SCOPE_LABEL:
    case IDC_PACKET_LABEL:
    case IDC_SIZE_LABEL:
    case IDC_CLASSIFIED_LABEL:
    case IDC_UNKNOWN_LABEL:
    case IDC_MEMORY_LABEL:
    case IDC_STARTED_LABEL:
    case IDC_OUTPUT_LABEL:
    case IDC_CONSUMER_LABEL:
    case IDC_ANALYSIS_STATE_LABEL:
    case IDC_GPU_LABEL:
    case IDC_AI_LABEL:
    case IDC_COVERAGE_LABEL:
        return true;
    default:
        return false;
    }
}

std::wstring HelpContent() {
    if (g_ui_language.load() == UiLanguage::TraditionalChinese) {
        return L"Ultimate 使用步驟\n\n"
               L"1. 按「開始完整恢復取證」。首次使用時只會要求選取 Launcher.exe，並在需要時顯示 Windows UAC。\n"
               L"   主畫面的「DLL 增強擷取」狀態列會全程顯示自動啟用、附加、Ready、EvidenceBlocked、排空與安全停止狀態；它不是第三個操作按鈕。\n"
               L"2. 在 Launcher.exe 按「開始遊戲」。Launcher.exe 啟動 God2_opt.exe 後，請在 God2_opt.exe 的遊戲登入畫面輸入帳號密碼。正常操作遊戲，讓工具收集真實語意證據。\n"
               L"3. 按「停止並產生 Codex 恢復包」，等待有界事件管線、CPU 權威驗證與 ZIP 完成。輸出路徑會顯示在主畫面。\n\n"
               L"證據與安全規則\n\n"
               L"• 只對指定 SHA-256 的 x86 God2_opt.exe 使用三個已驗證 choke point；其餘 Probe 顯示 EvidenceBlockedUnconfirmedProbe，不猜 RVA。\n"
               L"• 單一內嵌 DLL 具備全部 25 個 Probe Domain。Network 與三個 exact-build choke point 有正式實作；其餘 21 個 Domain 具備有界 discovery、candidate map、十項 promotion gate 與四項 ABI 安全閘門，證據不足時絕不安裝 Hook。\n"
               L"• 不擷取鍵盤或滑鼠。系統層 raw ETW／PktMon 封包已停用，不落盤也不打包；僅保留指定版本 x86 通道在寫入前完成帳密遮罩的語意事件，無法證明完整遮罩時即標示 EvidenceBlocked。\n"
               L"• 不做廣域記憶體掃描、不建立持久化、不修改正式 Server 或資料庫。恢復包仍可能包含角色資訊與網路端點，請只交給受信任環境。\n"
               L"• CPU 永遠是證據權威。GPU 只產生候選並由 CPU 重算；ML 輸出一律是 HYPOTHESIS。沒有合法已驗證模型時顯示 EvidenceBlockedModelUnavailable。\n"
               L"• 未觀測到的掉落率、公式與 Server-only 規則維持 UNKNOWN_SERVER_ONLY；恢復包會列出 Missing Evidence Queue。\n\n"
               L"作者：RayCat\nDiscord：raycat66";
    }
    if (g_ui_language.load() == UiLanguage::SimplifiedChinese) {
        return L"Ultimate 使用步骤\n\n"
               L"1. 点击“开始完整恢复取证”。首次使用时只会要求选择 Launcher.exe，并在需要时显示 Windows UAC。\n"
               L"   主界面的“DLL 增强捕获”状态栏会全程显示自动启用、附加、Ready、EvidenceBlocked、排空与安全停止状态；它不是第三个操作按钮。\n"
               L"2. 在 Launcher.exe 点击“开始游戏”。Launcher.exe 启动 God2_opt.exe 后，请在 God2_opt.exe 的游戏登录界面输入账号密码。正常操作游戏，让工具收集真实语义证据。\n"
               L"3. 点击“停止并生成 Codex 恢复包”，等待有界事件管线、CPU 权威验证与 ZIP 完成。输出路径会显示在主界面。\n\n"
               L"证据与安全规则\n\n"
               L"• 仅对指定 SHA-256 的 x86 God2_opt.exe 使用三个已验证 choke point；其余 Probe 显示 EvidenceBlockedUnconfirmedProbe，不猜测 RVA。\n"
               L"• 单一内嵌 DLL 具备全部 25 个 Probe Domain。Network 与三个 exact-build choke point 有正式实现；其余 21 个 Domain 具备有界 discovery、candidate map、十项 promotion gate 与四项 ABI 安全闸门，证据不足时绝不安装 Hook。\n"
               L"• 不捕获键盘或鼠标。系统层 raw ETW／PktMon 封包已停用，不落盘也不打包；仅保留指定版本 x86 通道在写入前完成账号密码遮罩的语义事件，无法证明完整遮罩时即标记 EvidenceBlocked。\n"
               L"• 不做广域内存扫描、不建立持久化、不修改正式 Server 或数据库。恢复包仍可能包含角色信息与网络端点，请仅交给受信任环境。\n"
               L"• CPU 永远是证据权威。GPU 只生成候选并由 CPU 重算；ML 输出一律是 HYPOTHESIS。没有合法已验证模型时显示 EvidenceBlockedModelUnavailable。\n"
               L"• 未观察到的掉落率、公式与 Server-only 规则保持 UNKNOWN_SERVER_ONLY；恢复包会列出 Missing Evidence Queue。\n\n"
               L"作者：RayCat\nDiscord：raycat66";
    }
    return L"Ultimate workflow\n\n"
           L"1. Click Start full recovery forensics. On first use, select Launcher.exe and approve Windows UAC if requested.\n"
           L"   The permanent DLL enhanced capture row shows auto-enable, attach, Ready, EvidenceBlocked, drain, and safe-stop states. It is not a third action button.\n"
           L"2. Click Start Game in Launcher.exe. After Launcher.exe starts God2_opt.exe, enter the account credentials in the God2_opt.exe game login screen. Play normally so the engine can collect real semantic evidence.\n"
           L"3. Click Stop and create Codex recovery bundle, then wait for the bounded event pipeline, CPU authority verification, and ZIP finalization.\n\n"
           L"Evidence and safety\n\n"
           L"• Only three verified choke points are enabled for the exact x86 client SHA-256. Every other target probe remains EvidenceBlockedUnconfirmedProbe; no RVA is guessed.\n"
           L"• The single embedded DLL contains all 25 Probe Domains. Network and three exact-build choke points have formal implementations; the other 21 have bounded discovery, candidate maps, ten promotion gates, and four ABI-safety gates, and never install hooks without sufficient evidence.\n"
           L"• Keyboard and mouse input are not captured. System-wide raw ETW/PktMon packets are disabled, never persisted, and never packaged. Only exact-build x86 semantic events masked before durable write are retained; unprovable masking is EvidenceBlocked.\n"
           L"• The engine does not scan broad memory, establish persistence, or mutate the production server/database. Recovery bundles may still contain character information and network endpoints; share them only with a trusted environment.\n"
           L"• CPU is always authoritative. GPU candidates are recomputed on CPU and every ML result starts as HYPOTHESIS. Without a legal verified model, the status is EvidenceBlockedModelUnavailable.\n"
           L"• Unobserved drop rates, formulae, and server-only rules remain UNKNOWN_SERVER_ONLY and enter the Missing Evidence Queue.\n\n"
           L"Author: RayCat\nDiscord: raycat66";
}

void ShowHelp(HWND owner) {
    TASKDIALOGCONFIG config{};
    config.cbSize = sizeof(config);
    config.hwndParent = owner;
    config.dwFlags = TDF_SIZE_TO_CONTENT | TDF_ALLOW_DIALOG_CANCELLATION;
    config.dwCommonButtons = TDCBF_OK_BUTTON;
    config.pszWindowTitle = Text(UiString::HelpTitle);
    config.pszMainInstruction = Text(UiString::Title);
    const std::wstring content = HelpContent();
    config.pszContent = content.c_str();
    if (FAILED(TaskDialogIndirect(&config, nullptr, nullptr, nullptr)))
        MessageBoxW(owner, content.c_str(), Text(UiString::HelpTitle), MB_OK | MB_ICONINFORMATION);
}

void SetText(HWND window, int id, const std::wstring& text) {
    SetWindowTextW(GetDlgItem(window, id), text.c_str());
}

std::wstring GetText(HWND window, int id) {
    const HWND control = GetDlgItem(window, id);
    const int length = GetWindowTextLengthW(control);
    std::wstring text(static_cast<std::size_t>(length) + 1, L'\0');
    if (length != 0) GetWindowTextW(control, text.data(), length + 1);
    text.resize(static_cast<std::size_t>(length));
    return text;
}

std::wstring FormatBytes(std::uint64_t value) {
    const wchar_t* suffix = L"B";
    double shown = static_cast<double>(value);
    if (value >= 1024ull * 1024ull * 1024ull) { shown /= 1024.0 * 1024.0 * 1024.0; suffix = L"GB"; }
    else if (value >= 1024ull * 1024ull) { shown /= 1024.0 * 1024.0; suffix = L"MB"; }
    else if (value >= 1024ull) { shown /= 1024.0; suffix = L"KB"; }
    wchar_t buffer[64]{};
    swprintf_s(buffer, L"%.2f %s", shown, suffix);
    return buffer;
}

bool CopyTextToClipboard(HWND owner, const std::wstring& text) {
    if (text.empty() || !OpenClipboard(owner)) return false;
    EmptyClipboard();
    const std::size_t bytes = (text.size() + 1) * sizeof(wchar_t);
    HGLOBAL memory = GlobalAlloc(GMEM_MOVEABLE, bytes);
    if (memory == nullptr) { CloseClipboard(); return false; }
    void* target = GlobalLock(memory);
    if (target == nullptr) { GlobalFree(memory); CloseClipboard(); return false; }
    memcpy(target, text.c_str(), bytes);
    GlobalUnlock(memory);
    if (SetClipboardData(CF_UNICODETEXT, memory) == nullptr) {
        GlobalFree(memory);
        CloseClipboard();
        return false;
    }
    CloseClipboard();
    return true;
}

bool BrowseGame(HWND owner, std::wstring* selected) {
    wchar_t file[MAX_PATH]{};
    if (selected != nullptr && selected->size() < std::size(file)) wcscpy_s(file, selected->c_str());
    const wchar_t filter_traditional[] = L"God2 遊戲登入器 (Launcher.exe)\0Launcher.exe\0可執行檔 (*.exe)\0*.exe\0\0";
    const wchar_t filter_simplified[] = L"God2 游戏登录器 (Launcher.exe)\0Launcher.exe\0可执行文件 (*.exe)\0*.exe\0\0";
    const wchar_t filter_english[] = L"God2 game launcher (Launcher.exe)\0Launcher.exe\0Executables (*.exe)\0*.exe\0\0";
    const wchar_t* filter = g_ui_language.load() == UiLanguage::TraditionalChinese ? filter_traditional :
        (g_ui_language.load() == UiLanguage::SimplifiedChinese ? filter_simplified : filter_english);
    OPENFILENAMEW dialog{};
    dialog.lStructSize = sizeof(dialog);
    dialog.hwndOwner = owner;
    dialog.lpstrFilter = filter;
    dialog.lpstrFile = file;
    dialog.nMaxFile = static_cast<DWORD>(std::size(file));
    dialog.lpstrTitle = Text(UiString::SelectLauncherTitle);
    dialog.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR;
    if (!GetOpenFileNameW(&dialog)) return false;
    if (selected != nullptr) *selected = file;
    return true;
}

bool IsValidGamePath(const std::wstring& value) {
    if (value.empty()) return false;
    return GameProcessTracker::ResolveClientPaths(fs::path(value), nullptr, nullptr);
}

void ApplyCaptureStateToUi(HWND window, CaptureState state) {
    bool start_enabled = false;
    bool stop_enabled = false;
    switch (state) {
    case CaptureState::Idle:
    case CaptureState::Completed:
    case CaptureState::Cancelled:
    case CaptureState::Failed:
        start_enabled = true;
        break;
    case CaptureState::Cancelling:
    case CaptureState::Stopping:
        break;
    default:
        stop_enabled = CanRequestStop(state);
        break;
    }
    EnableWindow(GetDlgItem(window, IDC_START_CAPTURE), start_enabled ? TRUE : FALSE);
    EnableWindow(GetDlgItem(window, IDC_STOP_CAPTURE), stop_enabled ? TRUE : FALSE);
    EnableWindow(GetDlgItem(window, IDC_ENHANCED_CAPTURE), start_enabled ? TRUE : FALSE);
    EnableWindow(GetDlgItem(window, IDC_GPU_MODE), start_enabled ? TRUE : FALSE);
    SetText(window, IDC_STOP_CAPTURE, IsStartupCancellable(state) ? Text(UiString::CancelStart) : Text(UiString::Stop));
}

AccelerationMode SelectedAccelerationMode(HWND window) {
    return SendMessageW(GetDlgItem(window, IDC_GPU_MODE), CB_GETCURSEL, 0, 0) == 1 ?
        AccelerationMode::CpuOnly : AccelerationMode::Auto;
}

std::wstring FormatGpuCapability(const GpuCapabilityReport& capability) {
    if (capability.requested_mode == AccelerationMode::CpuOnly)
        return L"Detected: not probed | Capability: CPU | Current: CPU | Reason: Forced CPU Only";
    if (!capability.backend_initialized) {
        return L"Detected: " + Utf8ToWide(capability.gpu_detected ? capability.device : "none") +
            L" | Capability: CPU | Current: CPU | Reason: " +
            Utf8ToWide(capability.fallback_reason.empty() ? "GpuUnavailable" : capability.fallback_reason);
    }
    const std::wstring compute = std::to_wstring(capability.compute_major) + L"." +
        std::to_wstring(capability.compute_minor);
    return L"Detected: " + Utf8ToWide(capability.device) + L" | Capability: " +
        Utf8ToWide(GpuModeDisplayName(capability.tier)) + L" (CC " + compute +
        L") | Current: CPU idle | Reason: Auto waits for predicted benefit";
}

std::wstring FormatClassifiedCount(const CaptureSnapshot& snapshot) {
    if (snapshot.semantic_candidate_count == 0) return std::to_wstring(snapshot.classified_count);
    std::wstring value = std::to_wstring(snapshot.classified_count) +
        Localized(L" 已驗證 / ", L" 已验证 / ", L" verified / ") +
        std::to_wstring(snapshot.semantic_candidate_count) +
        Localized(L" 候選", L" 候选", L" candidates");
    if (snapshot.high_confidence_semantic_candidate_count > 0) {
        value += Localized(L"（", L"（", L" (") +
            std::to_wstring(snapshot.high_confidence_semantic_candidate_count) +
            Localized(L" 高信心）", L" 高信心）", L" high-confidence)");
    }
    return value;
}

std::wstring FormatUnknownCount(const CaptureSnapshot& snapshot) {
    if (snapshot.candidate_frame_count == 0) return std::to_wstring(snapshot.unknown_count);
    return std::to_wstring(snapshot.unknown_count) +
        Localized(L" 原始 / ", L" 原始 / ", L" raw / ") +
        std::to_wstring(snapshot.candidate_frame_count) +
        Localized(L" Frame 候選", L" Frame 候选", L" frame candidates");
}

std::wstring FormatPacketCount(const CaptureSnapshot& snapshot) {
    if (snapshot.raw_etl_packet_count == 0) return std::to_wstring(snapshot.packet_count);
    return std::to_wstring(snapshot.packet_count) +
        Localized(L" God2 / ", L" God2 / ", L" God2 / ") +
        std::to_wstring(snapshot.raw_etl_packet_count) + L" ETL";
}

void RefreshSnapshot(AppState* state) {
    const auto snapshot = state->controller->Snapshot();
    SetText(state->window, IDC_STATUS_TEXT, snapshot.status);
    SetText(state->window, IDC_BACKEND_TEXT, snapshot.backend);
    // This row is the permanent, localized operator-facing status for the
    // automatically enabled x86 DLL.  Derive it from the structured state on
    // every refresh so changing UI language cannot leave stale status text.
    SetText(state->window, IDC_CONSUMER_TEXT, ConsumerStateText(snapshot.consumer_state));
    SetText(state->window, IDC_GAME_PROCESS, snapshot.game_process);
    SetText(state->window, IDC_ANALYSIS_STATE_TEXT, snapshot.analysis);
    SetText(state->window, IDC_GPU_STATUS, snapshot.gpu);
    if (!snapshot.capture_scope.empty()) SetText(state->window, IDC_CAPTURE_SCOPE, snapshot.capture_scope);
    SetText(state->window, IDC_PID_TEXT, snapshot.process_id == 0 ? L"-" : std::to_wstring(snapshot.process_id));
    SetText(state->window, IDC_PACKET_COUNT, FormatPacketCount(snapshot));
    SetText(state->window, IDC_CAPTURE_SIZE, FormatBytes(snapshot.captured_size));
    SetText(state->window, IDC_CLASSIFIED_COUNT, FormatClassifiedCount(snapshot));
    SetText(state->window, IDC_UNKNOWN_COUNT, FormatUnknownCount(snapshot));
    const std::uint64_t recovered_candidates = snapshot.classified_count + snapshot.semantic_candidate_count;
    const std::uint64_t missing_evidence = snapshot.unknown_count + snapshot.candidate_frame_count;
    SetText(state->window, IDC_COVERAGE_STATUS,
        std::to_wstring(recovered_candidates) +
        Localized(L" 已觀察 · ", L" 已观察 · ", L" observed · ") +
        std::to_wstring(missing_evidence) +
        Localized(L" 待補證據", L" 待补证据", L" missing evidence"));
    SetText(state->window, IDC_MEMORY_USE, FormatBytes(snapshot.memory_bytes));
    SetText(state->window, IDC_START_TIME, snapshot.started_at.empty() ? L"-" : snapshot.started_at);
    if (!snapshot.evidence_zip_path.empty()) SetText(state->window, IDC_OUTPUT_PATH, snapshot.evidence_zip_path.wstring());
    else if (!snapshot.session_path.empty()) SetText(state->window, IDC_OUTPUT_PATH, snapshot.session_path.wstring());
    ApplyCaptureStateToUi(state->window, snapshot.state);
}

void PostControllerEvent(HWND window, ControllerEventKind kind, bool success, std::wstring_view message) noexcept {
    try {
        const wchar_t* stage = kind == ControllerEventKind::Probe ? L"BackendProbeResult" :
                               kind == ControllerEventKind::Started ? L"CaptureStartResult" : L"CaptureStopResult";
        AppendStartupLog(stage, std::wstring(success ? L"PASS: " : L"ERROR: ") + std::wstring(message));
        auto event = std::make_unique<ControllerEvent>(ControllerEvent{kind, success, std::wstring(message)});
        if (IsWindow(window) &&
            PostMessageW(window, kControllerMessage, 0, reinterpret_cast<LPARAM>(event.get()))) {
            event.release();
        }
    } catch (...) {
        AppendStartupLog(L"ControllerEventDispatchException", L"Event could not be queued");
    }
}

void BeginProbe(AppState* state) {
    if (g_probe_running.exchange(true)) return;
    EnableWindow(GetDlgItem(state->window, IDC_RETRY_PROBE), FALSE);
    LogStageBegin(L"ProbeCaptureBackend");
    const HWND window = state->window;
    auto controller = state->controller;
    const AccelerationMode acceleration_mode = SelectedAccelerationMode(window);
    try {
        std::thread([window, controller, acceleration_mode] {
            try {
                CaptureBackendManager manager;
                auto selection = manager.Probe();
                OptionalGpuAccelerator accelerator(acceleration_mode);
                controller->SetGpuProbeResult(FormatGpuCapability(accelerator.Capability()));
                const bool success = selection.backend != nullptr;
                const bool privacy_blocked = success && !selection.capabilities.available &&
                    selection.capabilities.probe_evidence.rfind("EvidenceBlockedPrivacyPolicy:", 0) == 0;
                const std::wstring backend = privacy_blocked ?
                    L"EvidenceBlockedPrivacyPolicy · x86 masked semantic only" :
                    (success ? Utf8ToWide(selection.capabilities.capture_backend_name) + L" (" +
                        Utf8ToWide(ToString(selection.capabilities.analysis_mode)) + L")" :
                        Localized(L"沒有可用後端", L"没有可用后端", L"No available backend"));
                controller->SetProbeResult(backend);
                AppendStartupLog(L"CaptureBackendProbe", backend);
                if (success) LogStageSuccess(L"ProbeCaptureBackend");
                else LogStageFailure(L"ProbeCaptureBackend", L"CaptureBackendManager::Probe", L"no backend",
                                     ERROR_NOT_SUPPORTED);
                g_probe_running = false;
                PostControllerEvent(window, ControllerEventKind::Probe, success,
                                    success ? backend : Localized(L"捕捉後端探測失敗；主視窗仍可操作，可按「重新檢查」。詳情請查看啟動日誌。", L"捕获后端探测失败；主窗口仍可操作，可点击“重新检查”。详情请查看启动日志。", L"Capture backend check failed; the main window remains usable. Click Check again and review the startup log."));
            } catch (const std::exception& exception) {
                AppendStartupLog(L"StageFailure", L"ProbeCaptureBackend");
                AppendStartupLog(L"ApiName", L"CaptureBackendManager::Probe");
                AppendStartupLog(L"ReturnValue", L"exception");
                AppendStartupLog(L"CapturedWin32Error", L"NotAvailable");
                try { AppendStartupLog(L"ExceptionMessage", Utf8ToWide(exception.what())); } catch (...) {}
                g_probe_running = false;
                PostControllerEvent(window, ControllerEventKind::Probe, false,
                                    Localized(L"捕捉後端探測發生錯誤；主視窗仍可操作，可按「重新檢查」。", L"捕获后端探测发生错误；主窗口仍可操作，可点击“重新检查”。", L"The backend check encountered an error; the main window remains usable. Click Check again."));
            } catch (...) {
                AppendStartupLog(L"StageFailure", L"ProbeCaptureBackend");
                AppendStartupLog(L"CapturedWin32Error", L"NotAvailable");
                g_probe_running = false;
                PostControllerEvent(window, ControllerEventKind::Probe, false,
                                    Localized(L"捕捉後端探測發生未知錯誤；主視窗仍可操作，可按「重新檢查」。", L"捕获后端探测发生未知错误；主窗口仍可操作，可点击“重新检查”。", L"The backend check encountered an unknown error; the main window remains usable. Click Check again."));
            }
        }).detach();
    } catch (const std::exception& exception) {
        g_probe_running = false;
        try { AppendStartupLog(L"ProbeThreadCreationError", Utf8ToWide(exception.what())); } catch (...) {}
        PostControllerEvent(window, ControllerEventKind::Probe, false,
                            Localized(L"無法建立捕捉後端探測工作；主視窗仍可操作，可按「重新檢查」。", L"无法创建捕获后端探测任务；主窗口仍可操作，可点击“重新检查”。", L"Could not create the backend check task; the main window remains usable. Click Check again."));
    } catch (...) {
        g_probe_running = false;
        AppendStartupLog(L"ProbeThreadCreationError", L"Unknown exception");
        PostControllerEvent(window, ControllerEventKind::Probe, false,
                            Localized(L"無法建立捕捉後端探測工作；主視窗仍可操作，可按「重新檢查」。", L"无法创建捕获后端探测任务；主窗口仍可操作，可点击“重新检查”。", L"Could not create the backend check task; the main window remains usable. Click Check again."));
    }
}

void HandleStart(AppState* state) {
    std::wstring path = GetText(state->window, IDC_GAME_PATH);
    if (!IsValidGamePath(path)) {
        if (!BrowseGame(state->window, &path)) {
            SetText(state->window, IDC_STATUS_TEXT, Text(UiString::InvalidLauncherStatus));
            return;
        }
        SetText(state->window, IDC_GAME_PATH, path);
    }
    if (!IsValidGamePath(path)) {
        MessageBoxW(state->window, Text(UiString::InvalidLauncher),
                    kWindowTitle, MB_OK | MB_ICONWARNING);
        return;
    }
    SetText(state->window, IDC_STATUS_TEXT, Text(UiString::Starting));
    const HWND window = state->window;
    const bool enhanced_capture = SendMessageW(GetDlgItem(state->window, IDC_ENHANCED_CAPTURE),
                                                 BM_GETCHECK, 0, 0) == BST_CHECKED;
    const AccelerationMode acceleration_mode = SelectedAccelerationMode(state->window);
    state->controller->StartAsync(path, enhanced_capture, acceleration_mode,
                                  [window](bool success, std::wstring message) {
        PostControllerEvent(window, ControllerEventKind::Started, success, std::move(message));
    });
    RefreshSnapshot(state);
}

void HandleStop(AppState* state) {
    SetText(state->window, IDC_STATUS_TEXT, Text(UiString::Stopping));
    const HWND window = state->window;
    state->controller->StopAsync([window](bool success, std::wstring message) {
        PostControllerEvent(window, ControllerEventKind::Stopped, success, std::move(message));
    });
    RefreshSnapshot(state);
}

void OpenFolder(HWND owner, const std::wstring& path) {
    if (path.empty() || !fs::exists(path)) {
        MessageBoxW(owner, Text(UiString::NoOutput), kWindowTitle, MB_OK | MB_ICONINFORMATION);
        return;
    }
    fs::path target(path);
    if (fs::is_regular_file(target)) target = target.parent_path();
    const auto result = reinterpret_cast<INT_PTR>(
        ShellExecuteW(owner, L"open", target.c_str(), nullptr, nullptr, SW_SHOWNORMAL));
    if (result <= 32) {
        AppendStartupLog(L"OpenOutputFolderError", L"ShellExecute=" + std::to_wstring(result));
        const std::wstring message = std::wstring(Text(UiString::OpenOutputFailed)) + std::to_wstring(result);
        MessageBoxW(owner, message.c_str(), kWindowTitle, MB_OK | MB_ICONERROR);
    }
}

std::wstring ObservationText(const ObservationWindowState& state) {
    std::wostringstream output;
    output << L"Capture Session\r\n" << state.session_path.wstring() << L"\r\n\r\n";
    if (state.entity_view) {
        output << Text(UiString::WorldHeading) << L"\r\n\r\n";
        constexpr std::size_t maximum_tail = 64 * 1024;
        const auto content = ReadUtf8Tail(state.session_path / L"gameplay" / L"entities.jsonl", maximum_tail);
        if (!content || content->empty()) return output.str() + Text(UiString::WorldEmpty);
        output << Utf8ToWide(*content);
        return output.str();
    }
    output << Text(UiString::AnalysisHeading) << L"\r\n\r\n";
    const auto summary = ReadUtf8File(state.session_path / L"session-summary.json");
    if (summary) output << Utf8ToWide(*summary) << L"\r\n";
    const auto frame_candidates = ReadUtf8File(state.session_path / L"reports" / L"god2-frame-candidates.json");
    if (frame_candidates) {
        output << L"\r\nGod2 frame candidates\r\n" << Utf8ToWide(*frame_candidates) << L"\r\n";
    }
    const auto semantic_candidates = ReadUtf8File(
        state.session_path / L"reports" / L"god2-opcode-semantic-candidates.json");
    if (semantic_candidates) {
        output << L"\r\nGod2 opcode semantic candidates\r\n" << Utf8ToWide(*semantic_candidates) << L"\r\n";
    }
    const auto gameplay_candidate_coverage = ReadUtf8File(
        state.session_path / L"reports" / L"gameplay-semantic-candidate-coverage.json");
    if (gameplay_candidate_coverage) {
        output << L"\r\nCandidate gameplay coverage (not verified)\r\n"
               << Utf8ToWide(*gameplay_candidate_coverage) << L"\r\n";
    }
    for (const auto& relative : RequiredGameplayFiles()) {
        output << relative.wstring() << L" : " << CountLines(state.session_path / relative) << L" records\r\n";
    }
    output << L"\r\nRecent candidate gameplay coverage\r\n";
    const auto gameplay_candidate_tail = ReadUtf8Tail(
        state.session_path / L"gameplay" / L"protocol-semantic-candidates.jsonl", 16 * 1024);
    if (gameplay_candidate_tail && !gameplay_candidate_tail->empty()) output << Utf8ToWide(*gameplay_candidate_tail);
    else output << Localized(L"目前尚無候選玩法覆蓋。", L"当前暂无候选玩法覆盖。",
                             L"No candidate gameplay coverage yet.");
    output << L"\r\nRecent opcode semantic candidates\r\n";
    const auto candidate_tail = ReadUtf8Tail(
        state.session_path / L"raw" / L"god2-opcode-semantic-candidates.jsonl", 16 * 1024);
    if (candidate_tail && !candidate_tail->empty()) output << Utf8ToWide(*candidate_tail);
    else output << Localized(L"目前尚無 opcode 語意候選。", L"当前暂无 opcode 语义候选。", L"No opcode semantic candidates yet.");
    output << L"\r\n" << Localized(L"最近封包／Opcode 證據", L"最近封包／Opcode 证据",
                                      L"Recent packet / opcode evidence") << L"\r\n";
    constexpr std::size_t maximum_packet_tail = 48 * 1024;
    const auto frames = ReadUtf8Tail(state.session_path / L"raw" / L"frames.jsonl", maximum_packet_tail);
    if (frames && !frames->empty()) output << Utf8ToWide(*frames);
    else output << Localized(L"目前尚無封包證據。", L"当前暂无封包证据。", L"No packet evidence yet.");
    output << L"\r\n\r\n" << Localized(L"最近未知封包", L"最近未知封包", L"Recent unknown packets") << L"\r\n";
    const auto unknown = ReadUtf8Tail(state.session_path / L"raw" / L"unknown-frames.jsonl", 16 * 1024);
    if (unknown && !unknown->empty()) output << Utf8ToWide(*unknown);
    else output << Localized(L"目前尚無未知封包。", L"当前暂无未知封包。", L"No unknown packets yet.");
    return output.str();
}

LRESULT CALLBACK ObservationWindowProcedure(HWND window, UINT message, WPARAM wparam, LPARAM lparam) {
    auto* state = reinterpret_cast<ObservationWindowState*>(GetWindowLongPtrW(window, GWLP_USERDATA));
    switch (message) {
    case WM_NCCREATE: {
        auto* created = reinterpret_cast<CREATESTRUCTW*>(lparam);
        state = static_cast<ObservationWindowState*>(created->lpCreateParams);
        SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(state));
        SetPropW(window, g_instance_property_name.c_str(), reinterpret_cast<HANDLE>(1));
        return DefWindowProcW(window, message, wparam, lparam);
    }
    case WM_CREATE:
        state->edit = CreateWindowExW(WS_EX_CLIENTEDGE, L"EDIT", L"", WS_CHILD | WS_VISIBLE | ES_MULTILINE |
                                      ES_READONLY | ES_AUTOVSCROLL | WS_VSCROLL | WS_HSCROLL,
                                      0, 0, 100, 100, window, nullptr, GetModuleHandleW(nullptr), nullptr);
        SendMessageW(state->edit, EM_SETLIMITTEXT, 2 * 1024 * 1024, 0);
        SendMessageW(state->edit, WM_SETFONT, reinterpret_cast<WPARAM>(GetStockObject(DEFAULT_GUI_FONT)), TRUE);
        SetWindowTextW(state->edit, ObservationText(*state).c_str());
        SetTimer(window, 1, 1000, nullptr);
        return 0;
    case WM_SIZE:
        if (state != nullptr && state->edit != nullptr)
            MoveWindow(state->edit, 0, 0, LOWORD(lparam), HIWORD(lparam), TRUE);
        return 0;
    case WM_TIMER:
        if (state != nullptr && state->edit != nullptr) SetWindowTextW(state->edit, ObservationText(*state).c_str());
        return 0;
    case WM_CLOSE:
        DestroyWindow(window);
        return 0;
    case WM_DESTROY:
        KillTimer(window, 1);
        delete state;
        SetWindowLongPtrW(window, GWLP_USERDATA, 0);
        return 0;
    default:
        return DefWindowProcW(window, message, wparam, lparam);
    }
}

void ShowObservationWindow(HWND owner, const fs::path& session_path, bool entity_view) {
    if (session_path.empty() || !fs::exists(session_path)) {
        MessageBoxW(owner, Text(UiString::NoSession), kWindowTitle, MB_OK | MB_ICONINFORMATION);
        return;
    }
    auto* state = new ObservationWindowState{session_path, entity_view, nullptr};
    const wchar_t* title = entity_view ? Text(UiString::WorldTitle) : Text(UiString::AnalysisTitle);
    const HWND window = CreateWindowExW(WS_EX_TOOLWINDOW, kObservationWindowClass, title,
                                        WS_OVERLAPPEDWINDOW | WS_VISIBLE, CW_USEDEFAULT, CW_USEDEFAULT,
                                        820, 560, owner, nullptr, GetModuleHandleW(nullptr), state);
    if (window == nullptr) {
        const DWORD create_error = GetLastError();
        delete state;
        AppendStartupLog(L"ObservationWindowError", L"CreateWindowExW=" + std::to_wstring(create_error));
        MessageBoxW(owner, Text(UiString::ObservationFailed), kWindowTitle, MB_OK | MB_ICONERROR);
    }
}

void ApplyUiLanguage(AppState* state) {
    const HWND window = state->window;
    SetWindowTextW(window, kWindowTitle);
    SetText(window, IDC_TITLE, Text(UiString::Title));
    SetText(window, IDC_SUBTITLE, Text(UiString::Subtitle));
    SetText(window, IDC_AUTHOR, Text(UiString::Author));
    SetText(window, IDC_LANGUAGE_LABEL, Text(UiString::Language));
    SetText(window, IDC_LAUNCHER_LABEL, Text(UiString::Launcher));
    SetText(window, IDC_OS_LABEL, Text(UiString::Os));
    SetText(window, IDC_ADMIN_LABEL, Text(UiString::Administrator));
    SetText(window, IDC_BACKEND_LABEL, Text(UiString::Backend));
    SetText(window, IDC_CONSUMER_LABEL, Text(UiString::Consumer));
    SetText(window, IDC_PROCESS_LABEL, Text(UiString::Process));
    SetText(window, IDC_PID_LABEL, Text(UiString::Pid));
    SetText(window, IDC_ANALYSIS_STATE_LABEL, Text(UiString::AnalysisStateLabel));
    SetText(window, IDC_GPU_LABEL, Text(UiString::GpuAcceleration));
    SetText(window, IDC_AI_LABEL, Text(UiString::AiBackend));
    SetText(window, IDC_COVERAGE_LABEL, Text(UiString::Coverage));
    SetText(window, IDC_STATUS_LABEL, Text(UiString::Status));
    SetText(window, IDC_SCOPE_LABEL, Text(UiString::Scope));
    SetText(window, IDC_PACKET_LABEL, Text(UiString::PacketCount));
    SetText(window, IDC_SIZE_LABEL, Text(UiString::CaptureSize));
    SetText(window, IDC_CLASSIFIED_LABEL, Text(UiString::Classified));
    SetText(window, IDC_UNKNOWN_LABEL, Text(UiString::Unknown));
    SetText(window, IDC_MEMORY_LABEL, Text(UiString::Memory));
    SetText(window, IDC_STARTED_LABEL, Text(UiString::StartedAt));
    SetText(window, IDC_OUTPUT_LABEL, Text(UiString::Output));
    SetText(window, IDC_BROWSE_GAME, Text(UiString::Browse));
    SetText(window, IDC_RETRY_PROBE, Text(UiString::Retry));
    SetText(window, IDC_START_CAPTURE, Text(UiString::Start));
    SetText(window, IDC_OPEN_OUTPUT, Text(UiString::OpenOutput));
    SetText(window, IDC_COPY_OUTPUT, Text(UiString::CopyOutput));
    SetText(window, IDC_OPEN_ANALYSIS, Text(UiString::OpenAnalysis));
    SetText(window, IDC_OPEN_WORLD, Text(UiString::OpenWorld));
    // Keep the compact auxiliary help affordance legible in every locale.
    // The localized, full help title remains in the dialog itself.
    SetText(window, IDC_HELP_BUTTON, L"?");
    SetText(window, IDC_ENHANCED_CAPTURE, Text(UiString::EnhancedCapture));
    SetText(window, IDC_ADMIN_TEXT, IsAdministrator() ? Text(UiString::AdminYes) : Text(UiString::AdminNo));
    const auto snapshot = state->controller->Snapshot();
    if (snapshot.state == CaptureState::Idle) {
        SetText(window, IDC_STATUS_TEXT, Text(UiString::Ready));
        SetText(window, IDC_GAME_PROCESS, Text(UiString::NotStarted));
        SetText(window, IDC_CAPTURE_SCOPE, Text(UiString::ScopeWaiting));
    }
    RefreshSnapshot(state);
    InvalidateRect(window, nullptr, TRUE);
}

void CreateMainControls(AppState* state) {
    const HWND window = state->window;
    state->background_brush = CreateSolidBrush(kUiBackground);
    state->surface_brush = CreateSolidBrush(kUiSurface);

    Control(window, L"STATIC", L"", SS_LEFT, 28, 10, 465, 28, IDC_TITLE);
    Control(window, L"STATIC", L"", SS_LEFT, 30, 37, 475, 16, IDC_SUBTITLE);
    Control(window, L"STATIC", L"", SS_LEFT, 30, 55, 480, 16, IDC_AUTHOR);
    Control(window, L"STATIC", L"", SS_RIGHT, 501, 15, 54, 19, IDC_LANGUAGE_LABEL);
    const HWND language = Control(window, L"COMBOBOX", L"", CBS_DROPDOWNLIST | WS_TABSTOP,
                                  560, 11, 104, 92, IDC_LANGUAGE);
    SendMessageW(language, CB_ADDSTRING, 0, reinterpret_cast<LPARAM>(L"繁體中文"));
    SendMessageW(language, CB_ADDSTRING, 0, reinterpret_cast<LPARAM>(L"简体中文"));
    SendMessageW(language, CB_ADDSTRING, 0, reinterpret_cast<LPARAM>(L"English"));
    SendMessageW(language, CB_SETCURSEL, static_cast<WPARAM>(g_ui_language.load()), 0);
    Control(window, L"BUTTON", L"?", BS_OWNERDRAW | WS_TABSTOP, 670, 11, 30, 27, IDC_HELP_BUTTON);

    // The launcher picker and internal auto-enable policy flag are not operator
    // actions.  Start opens the picker only when no exact launcher identity is
    // available, and Ultimate always enables the exact-build semantic probe.
    // The DLL itself is never hidden from the operator: IDC_CONSUMER_LABEL and
    // IDC_CONSUMER_TEXT below form its permanent visible status row.
    const HWND launcher_label = Control(window, L"STATIC", L"", SS_LEFT, 0, 0, 1, 1, IDC_LAUNCHER_LABEL);
    const HWND launcher_edit = Control(window, L"EDIT", L"", ES_AUTOHSCROLL | WS_TABSTOP, 0, 0, 1, 1,
                                       IDC_GAME_PATH, WS_EX_CLIENTEDGE);
    const HWND browse = Control(window, L"BUTTON", L"", BS_OWNERDRAW | WS_TABSTOP, 0, 0, 1, 1, IDC_BROWSE_GAME);
    const HWND enhanced = Control(window, L"BUTTON", L"", BS_AUTOCHECKBOX | WS_TABSTOP, 0, 0, 1, 1,
                                  IDC_ENHANCED_CAPTURE);
    ShowWindow(launcher_label, SW_HIDE);
    ShowWindow(launcher_edit, SW_HIDE);
    ShowWindow(browse, SW_HIDE);
    ShowWindow(enhanced, SW_HIDE);
    SendMessageW(enhanced, BM_SETCHECK, BST_CHECKED, 0);

    constexpr int kLabelX = 28;
    constexpr int kValueX = 142;
    constexpr int kLabelWidth = 108;
    constexpr int kValueWidth = 530;
    Control(window, L"STATIC", L"", SS_LEFT, kLabelX, 91, kLabelWidth, 18, IDC_OS_LABEL);
    Control(window, L"STATIC", L"-", SS_LEFT | SS_ENDELLIPSIS, kValueX, 91, kValueWidth, 18, IDC_OS_TEXT);
    Control(window, L"STATIC", L"", SS_LEFT, kLabelX, 114, kLabelWidth, 18, IDC_ADMIN_LABEL);
    Control(window, L"STATIC", L"-", SS_LEFT | SS_ENDELLIPSIS, kValueX, 114, 186, 18, IDC_ADMIN_TEXT);
    Control(window, L"STATIC", L"", SS_LEFT, 348, 114, 92, 18, IDC_BACKEND_LABEL);
    Control(window, L"STATIC", L"-", SS_LEFT | SS_ENDELLIPSIS, 445, 114, 227, 18, IDC_BACKEND_TEXT);
    const HWND retry = Control(window, L"BUTTON", L"", BS_OWNERDRAW | WS_TABSTOP, 0, 0, 1, 1, IDC_RETRY_PROBE);
    ShowWindow(retry, SW_HIDE);
    Control(window, L"STATIC", L"", SS_LEFT, kLabelX, 137, 132, 18, IDC_CONSUMER_LABEL);
    Control(window, L"STATIC", L"-", SS_LEFT | SS_ENDELLIPSIS, 166, 137, 506, 18, IDC_CONSUMER_TEXT);
    Control(window, L"STATIC", L"", SS_LEFT, kLabelX, 160, kLabelWidth, 18, IDC_PROCESS_LABEL);
    Control(window, L"STATIC", L"-", SS_LEFT | SS_ENDELLIPSIS, kValueX, 160, 286, 18, IDC_GAME_PROCESS);
    Control(window, L"STATIC", L"", SS_LEFT, 445, 160, 38, 18, IDC_PID_LABEL);
    Control(window, L"STATIC", L"-", SS_LEFT, 488, 160, 184, 18, IDC_PID_TEXT);
    Control(window, L"STATIC", L"", SS_LEFT, kLabelX, 183, kLabelWidth, 18, IDC_ANALYSIS_STATE_LABEL);
    Control(window, L"STATIC", L"-", SS_LEFT | SS_ENDELLIPSIS, kValueX, 183, kValueWidth, 18, IDC_ANALYSIS_STATE_TEXT);
    Control(window, L"STATIC", L"", SS_LEFT, kLabelX, 206, kLabelWidth, 18, IDC_GPU_LABEL);
    const HWND gpu_mode = Control(window, L"COMBOBOX", L"", CBS_DROPDOWNLIST | WS_TABSTOP,
                                  kValueX, 202, 86, 78, IDC_GPU_MODE);
    SendMessageW(gpu_mode, CB_ADDSTRING, 0, reinterpret_cast<LPARAM>(L"Auto"));
    SendMessageW(gpu_mode, CB_ADDSTRING, 0, reinterpret_cast<LPARAM>(L"CPU Only"));
    SendMessageW(gpu_mode, CB_SETCURSEL, 0, 0);
    Control(window, L"STATIC", L"Checking in background", SS_LEFT | SS_ENDELLIPSIS,
            238, 206, 434, 18, IDC_GPU_STATUS);
    Control(window, L"STATIC", L"", SS_LEFT, kLabelX, 229, kLabelWidth, 18, IDC_AI_LABEL);
    Control(window, L"STATIC", L"EvidenceBlockedModelUnavailable", SS_LEFT | SS_ENDELLIPSIS,
            kValueX, 229, kValueWidth, 18, IDC_AI_STATUS);
    Control(window, L"STATIC", L"", SS_LEFT, kLabelX, 252, kLabelWidth, 18, IDC_STATUS_LABEL);
    Control(window, L"STATIC", L"-", SS_LEFT | SS_ENDELLIPSIS, kValueX, 252, kValueWidth, 18, IDC_STATUS_TEXT);
    Control(window, L"STATIC", L"", SS_LEFT, kLabelX, 275, kLabelWidth, 18, IDC_SCOPE_LABEL);
    Control(window, L"STATIC", L"-", SS_LEFT | SS_ENDELLIPSIS, kValueX, 275, kValueWidth, 18, IDC_CAPTURE_SCOPE);
    Control(window, L"STATIC", L"", SS_LEFT, kLabelX, 298, kLabelWidth, 18, IDC_COVERAGE_LABEL);
    Control(window, L"STATIC", L"0 verified · missing-evidence queue pending", SS_LEFT | SS_ENDELLIPSIS,
            kValueX, 298, kValueWidth, 18, IDC_COVERAGE_STATUS);

    Control(window, L"BUTTON", L"", BS_OWNERDRAW | WS_TABSTOP, 26, 337, 316, 38, IDC_START_CAPTURE);
    Control(window, L"BUTTON", L"", BS_OWNERDRAW | WS_TABSTOP, 352, 337, 322, 38, IDC_STOP_CAPTURE);

    const std::tuple<int, int, int> metrics[] = {
        {IDC_PACKET_LABEL, IDC_PACKET_COUNT, 28}, {IDC_SIZE_LABEL, IDC_CAPTURE_SIZE, 246},
        {IDC_CLASSIFIED_LABEL, IDC_CLASSIFIED_COUNT, 464}, {IDC_UNKNOWN_LABEL, IDC_UNKNOWN_COUNT, 28},
        {IDC_MEMORY_LABEL, IDC_MEMORY_USE, 246}, {IDC_STARTED_LABEL, IDC_START_TIME, 464}
    };
    int y = 393;
    int metric_index = 0;
    for (const auto& [label_id, value_id, x] : metrics) {
        Control(window, L"STATIC", L"", SS_LEFT, x, y, 88, 18, label_id);
        Control(window, L"STATIC", L"0", SS_LEFT | SS_ENDELLIPSIS, x + 91, y, 116, 18, value_id);
        if (++metric_index == 3) y += 21;
    }

    Control(window, L"STATIC", L"", SS_LEFT, 26, 449, 108, 25, IDC_OUTPUT_LABEL);
    Control(window, L"EDIT", L"", ES_AUTOHSCROLL | ES_READONLY, 140, 448, 532, 25,
            IDC_OUTPUT_PATH, WS_EX_CLIENTEDGE);
    for (const int hidden_id : {IDC_OPEN_OUTPUT, IDC_COPY_OUTPUT, IDC_OPEN_ANALYSIS, IDC_OPEN_WORLD}) {
        const HWND hidden = Control(window, L"BUTTON", L"", BS_OWNERDRAW | WS_TABSTOP, 0, 0, 1, 1, hidden_id);
        ShowWindow(hidden, SW_HIDE);
    }

    const auto os = DetectOsVersion();
    SetText(window, IDC_OS_TEXT, Utf8ToWide(os.edition) + L" x64 (Build " + std::to_wstring(os.build) + L")");
    SetText(window, IDC_BACKEND_TEXT, Text(UiString::Probing));
    SetText(window, IDC_PID_TEXT, L"-");
    SetText(window, IDC_START_TIME, L"-");
    const auto local_data = LocalDataRoot();
    if (!local_data.empty()) SetText(window, IDC_OUTPUT_PATH, (local_data / L"Captures").wstring());

    state->font = CreateFontW(-13, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
                              OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
                              DEFAULT_PITCH | FF_DONTCARE, L"Segoe UI");
    state->title_font = CreateFontW(-21, 0, 0, 0, FW_SEMIBOLD, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
                                    OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
                                    DEFAULT_PITCH | FF_DONTCARE, L"Segoe UI");
    state->small_font = CreateFontW(-11, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
                                    OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
                                    DEFAULT_PITCH | FF_DONTCARE, L"Segoe UI");
    if (state->font != nullptr) ApplyFont(window, state->font);
    if (state->title_font != nullptr)
        SendMessageW(GetDlgItem(window, IDC_TITLE), WM_SETFONT, reinterpret_cast<WPARAM>(state->title_font), TRUE);
    if (state->small_font != nullptr) {
        SendMessageW(GetDlgItem(window, IDC_SUBTITLE), WM_SETFONT, reinterpret_cast<WPARAM>(state->small_font), TRUE);
        SendMessageW(GetDlgItem(window, IDC_AUTHOR), WM_SETFONT, reinterpret_cast<WPARAM>(state->small_font), TRUE);
    }
    state->tooltip = CreateWindowExW(WS_EX_TOPMOST, TOOLTIPS_CLASSW, nullptr,
        WS_POPUP | TTS_ALWAYSTIP | TTS_NOPREFIX, CW_USEDEFAULT, CW_USEDEFAULT, CW_USEDEFAULT, CW_USEDEFAULT,
        window, nullptr, GetModuleHandleW(nullptr), nullptr);
    if (state->tooltip != nullptr) {
        SendMessageW(state->tooltip, TTM_SETMAXTIPWIDTH, 0, 480);
        AddTooltip(state->tooltip, GetDlgItem(window, IDC_OPEN_ANALYSIS));
        AddTooltip(state->tooltip, GetDlgItem(window, IDC_OPEN_WORLD));
        AddTooltip(state->tooltip, GetDlgItem(window, IDC_ENHANCED_CAPTURE));
        AddTooltip(state->tooltip, GetDlgItem(window, IDC_BACKEND_TEXT));
        AddTooltip(state->tooltip, GetDlgItem(window, IDC_CONSUMER_TEXT));
        AddTooltip(state->tooltip, GetDlgItem(window, IDC_GPU_STATUS));
        SendMessageW(state->tooltip, TTM_SETDELAYTIME, TTDT_INITIAL, 350);
    }
    CaptureMainControlLayout(state);
    LayoutMainControls(state);
    ApplyThemeToChildren(window);
    ApplyUiLanguage(state);
}

LRESULT CALLBACK WindowProcedure(HWND window, UINT message, WPARAM wparam, LPARAM lparam) {
    auto* state = reinterpret_cast<AppState*>(GetWindowLongPtrW(window, GWLP_USERDATA));
    switch (message) {
    case WM_NCCREATE: {
        auto* created = static_cast<CREATESTRUCTW*>(reinterpret_cast<void*>(lparam));
        state = static_cast<AppState*>(created->lpCreateParams);
        state->window = window;
        SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(state));
        SetPropW(window, g_instance_property_name.c_str(), reinterpret_cast<HANDLE>(1));
        return DefWindowProcW(window, message, wparam, lparam);
    }
    case WM_CREATE:
        ApplyModernWindowStyle(window);
        CreateMainControls(state);
        SetTimer(window, kMetricsTimer, 1000, nullptr);
        AppendStartupLog(L"WindowCreated", L"true");
        PostMessageW(window, kBeginProbeMessage, 0, 0);
        return 0;
    case WM_ERASEBKGND:
        if (state != nullptr && state->background_brush != nullptr) {
            RECT bounds{};
            GetClientRect(window, &bounds);
            FillRect(reinterpret_cast<HDC>(wparam), &bounds, state->background_brush);
            return 1;
        }
        break;
    case WM_PAINT: {
        PAINTSTRUCT paint{};
        HDC dc = BeginPaint(window, &paint);
        HBRUSH accent = CreateSolidBrush(kUiAccent);
        RECT accent_bounds = ScaleMainDesignRect(RECT{16, 13, 20, 69},
            state != nullptr ? state->layout_scale : 1.0,
            state != nullptr ? state->layout_offset_x : 0,
            state != nullptr ? state->layout_offset_y : 0);
        FillRect(dc, &accent_bounds, accent);
        DeleteObject(accent);
        HBRUSH card = CreateSolidBrush(kUiSurface);
        HPEN border = CreatePen(PS_SOLID, 1, kUiBorder);
        HGDIOBJ previous_brush = SelectObject(dc, card);
        HGDIOBJ previous_pen = SelectObject(dc, border);
        const double scale = state != nullptr ? state->layout_scale : 1.0;
        const int offset_x = state != nullptr ? state->layout_offset_x : 0;
        const int offset_y = state != nullptr ? state->layout_offset_y : 0;
        const int radius = (std::max)(4, static_cast<int>(std::lround(10.0 * scale)));
        for (const RECT design : {RECT{14, 80, 686, 326}, RECT{14, 330, 686, 383},
                                  RECT{14, 387, 686, 441}, RECT{14, 445, 686, 480}}) {
            const RECT card_bounds = ScaleMainDesignRect(design, scale, offset_x, offset_y);
            RoundRect(dc, card_bounds.left, card_bounds.top, card_bounds.right,
                      card_bounds.bottom, radius, radius);
        }
        SelectObject(dc, previous_brush);
        SelectObject(dc, previous_pen);
        DeleteObject(card);
        DeleteObject(border);
        EndPaint(window, &paint);
        return 0;
    }
    case WM_SIZE:
        LayoutMainControls(state);
        return 0;
    case WM_DISPLAYCHANGE:
        FitMainWindowToCurrentWorkArea(window);
        return 0;
    case WM_SETTINGCHANGE:
        if (wparam == SPI_SETWORKAREA) FitMainWindowToCurrentWorkArea(window);
        break;
    case WM_DRAWITEM:
        if (lparam != 0) {
            DrawModernButton(*reinterpret_cast<DRAWITEMSTRUCT*>(lparam));
            return TRUE;
        }
        break;
    case WM_CTLCOLORSTATIC:
        if (state != nullptr) {
            const HDC dc = reinterpret_cast<HDC>(wparam);
            const int control_id = GetDlgCtrlID(reinterpret_cast<HWND>(lparam));
            SetBkMode(dc, TRANSPARENT);
            COLORREF text_color = kUiText;
            if (IsUiLabel(control_id) || control_id == IDC_SUBTITLE) text_color = kUiMutedText;
            if (control_id == IDC_AUTHOR || control_id == IDC_CAPTURE_SCOPE) text_color = kUiAccent;
            if (control_id == IDC_GPU_STATUS || control_id == IDC_COVERAGE_STATUS) text_color = kUiTeal;
            if (control_id == IDC_AI_STATUS) text_color = RGB(180, 83, 9);
            SetTextColor(dc, text_color);
            const bool header_control = control_id == IDC_TITLE || control_id == IDC_SUBTITLE ||
                control_id == IDC_AUTHOR || control_id == IDC_LANGUAGE_LABEL;
            return reinterpret_cast<LRESULT>(header_control ? state->background_brush : state->surface_brush);
        }
        break;
    case WM_CTLCOLOREDIT:
        if (state != nullptr && state->surface_brush != nullptr) {
            const HDC dc = reinterpret_cast<HDC>(wparam);
            SetBkColor(dc, kUiSurface);
            SetTextColor(dc, kUiText);
            return reinterpret_cast<LRESULT>(state->surface_brush);
        }
        break;
    case WM_CTLCOLORBTN:
        if (state != nullptr && state->surface_brush != nullptr) {
            SetBkMode(reinterpret_cast<HDC>(wparam), TRANSPARENT);
            return reinterpret_cast<LRESULT>(state->surface_brush);
        }
        break;
    case WM_NOTIFY:
        if (lparam != 0) {
            auto* notification = reinterpret_cast<NMHDR*>(lparam);
            if (notification->code == TTN_GETDISPINFOW) {
                auto* information = reinterpret_cast<NMTTDISPINFOW*>(lparam);
                const HWND control = reinterpret_cast<HWND>(notification->idFrom);
                switch (GetDlgCtrlID(control)) {
                case IDC_OPEN_ANALYSIS: information->lpszText = const_cast<wchar_t*>(Text(UiString::AnalysisTip)); break;
                case IDC_OPEN_WORLD: information->lpszText = const_cast<wchar_t*>(Text(UiString::WorldTip)); break;
                case IDC_ENHANCED_CAPTURE: information->lpszText = const_cast<wchar_t*>(Text(UiString::EnhancedCaptureTip)); break;
                case IDC_BACKEND_TEXT:
                case IDC_CONSUMER_TEXT:
                case IDC_GPU_STATUS:
                    if (state != nullptr) {
                        state->tooltip_text = GetText(window, GetDlgCtrlID(control));
                        information->lpszText = state->tooltip_text.data();
                    }
                    break;
                default: break;
                }
                return 0;
            }
        }
        break;
    case kBeginProbeMessage:
        BeginProbe(state);
        return 0;
    case WM_TIMER:
        if (wparam == kMetricsTimer) {
            state->controller->RefreshMetricsAsync();
            RefreshSnapshot(state);
        }
        return 0;
    case WM_COMMAND:
        switch (LOWORD(wparam)) {
        case IDC_LANGUAGE:
            if (HIWORD(wparam) == CBN_SELCHANGE) {
                const LRESULT selection = SendMessageW(GetDlgItem(window, IDC_LANGUAGE), CB_GETCURSEL, 0, 0);
                if (selection >= 0 && selection <= 2) {
                    g_ui_language.store(static_cast<UiLanguage>(selection));
                    ApplyUiLanguage(state);
                }
            }
            return 0;
        case IDC_GPU_MODE:
            if (HIWORD(wparam) == CBN_SELCHANGE) {
                SetText(window, IDC_GPU_STATUS, Text(UiString::Probing));
                BeginProbe(state);
            }
            return 0;
        case IDC_HELP_BUTTON: ShowHelp(window); return 0;
        case IDC_BROWSE_GAME: {
            std::wstring path = GetText(window, IDC_GAME_PATH);
            if (BrowseGame(window, &path)) SetText(window, IDC_GAME_PATH, path);
            return 0;
        }
        case IDC_START_CAPTURE: HandleStart(state); return 0;
        case IDC_STOP_CAPTURE: HandleStop(state); return 0;
        case IDC_RETRY_PROBE:
            SetText(window, IDC_BACKEND_TEXT, Text(UiString::Probing));
            BeginProbe(state);
            return 0;
        case IDC_OPEN_OUTPUT: OpenFolder(window, GetText(window, IDC_OUTPUT_PATH)); return 0;
        case IDC_COPY_OUTPUT:
            if (!CopyTextToClipboard(window, GetText(window, IDC_OUTPUT_PATH)))
                MessageBoxW(window, Text(UiString::NoClipboardPath), kWindowTitle, MB_OK | MB_ICONINFORMATION);
            return 0;
        case IDC_OPEN_ANALYSIS:
            ShowObservationWindow(window, state->controller->Snapshot().session_path, false);
            return 0;
        case IDC_OPEN_WORLD:
            ShowObservationWindow(window, state->controller->Snapshot().session_path, true);
            return 0;
        default: break;
        }
        break;
    case kControllerMessage: {
        std::unique_ptr<ControllerEvent> event(reinterpret_cast<ControllerEvent*>(lparam));
        RefreshSnapshot(state);
        if (event->kind == ControllerEventKind::Probe) {
            EnableWindow(GetDlgItem(window, IDC_RETRY_PROBE), TRUE);
            if (!event->success) SetText(window, IDC_STATUS_TEXT, Text(UiString::ProbeFailed));
        } else if (event->kind == ControllerEventKind::Started) {
            if (!event->success) MessageBoxW(window, event->message.c_str(), kWindowTitle, MB_OK | MB_ICONERROR);
        } else {
            const auto current = state->controller->Snapshot();
            const auto output = GetText(window, IDC_OUTPUT_PATH);
            if (event->success) CopyTextToClipboard(window, output);
            MessageBoxW(window, event->message.c_str(), kWindowTitle,
                        MB_OK | (event->success ? MB_ICONINFORMATION : MB_ICONERROR));
            if (state->close_after_stop && !current.capturing) DestroyWindow(window);
            else if (current.capturing) state->close_after_stop = false;
        }
        return 0;
    }
    case WM_CLOSE: {
        const auto snapshot = state->controller->Snapshot();
        if (snapshot.state == CaptureState::Cancelling || snapshot.state == CaptureState::Stopping) {
            MessageBoxW(window, Text(UiString::BusyStopping), kWindowTitle,
                        MB_OK | MB_ICONINFORMATION);
            return 0;
        }
        if (snapshot.state == CaptureState::CleanupRequired &&
            (snapshot.backend_state == BackendState::Stopped ||
             snapshot.backend_state == BackendState::EvidenceBlocked) &&
            snapshot.consumer_state == ConsumerState::Stopped &&
            (snapshot.analysis_state == AnalysisState::Completed ||
             snapshot.analysis_state == AnalysisState::Failed)) {
            const int answer = MessageBoxW(window,
                Localized(
                    L"取證後端已停止，但 Session 或 Evidence ZIP 尚未完成。系統 raw payload 不會保留；已遮罩語意證據仍在輸出資料夾。要直接關閉工具嗎？",
                    L"取证后端已停止，但 Session 或 Evidence ZIP 尚未完成。系统 raw payload 不会保留；已遮罩语义证据仍在输出文件夹。要直接关闭工具吗？",
                    L"The forensic backend is stopped, but Session finalization or the Evidence ZIP is incomplete. System raw payload is not retained; masked semantic evidence remains in the output folder. Close the tool anyway?"),
                kWindowTitle, MB_YESNO | MB_ICONWARNING | MB_DEFBUTTON2);
            if (answer == IDYES) DestroyWindow(window);
            return 0;
        }
        if (CanRequestStop(snapshot.state)) {
            const int answer = MessageBoxW(window, Text(UiString::ConfirmExit),
                                           kWindowTitle, MB_YESNO | MB_ICONWARNING | MB_DEFBUTTON2);
            if (answer != IDYES) return 0;
            state->close_after_stop = true;
            HandleStop(state);
            return 0;
        }
        DestroyWindow(window);
        return 0;
    }
    case WM_DESTROY:
        KillTimer(window, kMetricsTimer);
        if (state != nullptr && state->font != nullptr) DeleteObject(state->font);
        if (state != nullptr && state->title_font != nullptr) DeleteObject(state->title_font);
        if (state != nullptr && state->small_font != nullptr) DeleteObject(state->small_font);
        if (state != nullptr && state->background_brush != nullptr) DeleteObject(state->background_brush);
        if (state != nullptr && state->surface_brush != nullptr) DeleteObject(state->surface_brush);
        AppendStartupLog(L"StartupStage", L"MessageLoopEnded");
        RemovePropW(window, g_instance_property_name.c_str());
        PostQuitMessage(0);
        return 0;
    default:
        break;
    }
    return DefWindowProcW(window, message, wparam, lparam);
}

BOOL CALLBACK FindMainWindowForProcess(HWND window, LPARAM parameter) {
    auto* values = reinterpret_cast<std::pair<DWORD, HWND>*>(parameter);
    DWORD process_id = 0;
    GetWindowThreadProcessId(window, &process_id);
    wchar_t title[256]{};
    GetWindowTextW(window, title, static_cast<int>(std::size(title)));
    if (process_id == values->first && wcscmp(title, kWindowTitle) == 0) {
        values->second = window;
        return FALSE;
    }
    return TRUE;
}

HWND MainWindowForProcess(DWORD process_id) {
    std::pair<DWORD, HWND> values{process_id, nullptr};
    EnumWindows(FindMainWindowForProcess, reinterpret_cast<LPARAM>(&values));
    return values.second;
}

bool ProcessHasConsoleWindow(DWORD process_id) {
    HWND window = nullptr;
    wchar_t class_name[128]{};
    while ((window = FindWindowExW(nullptr, window, nullptr, nullptr)) != nullptr) {
        DWORD owner = 0;
        GetWindowThreadProcessId(window, &owner);
        if (owner != process_id) continue;
        GetClassNameW(window, class_name, static_cast<int>(std::size(class_name)));
        if (wcscmp(class_name, L"ConsoleWindowClass") == 0) return true;
    }
    return false;
}

} // namespace

int RunHeadlessEnhancedCapture(const fs::path& session_path,
                               std::string_view session_id,
                               const fs::path& game_path,
                               DWORD process_id,
                               DWORD observe_seconds,
                               const std::wstring& stop_event_name) {
    if (!IsAdministrator() || !IsApprovedSessionPath(session_path) ||
        session_id.empty() || process_id == 0 || observe_seconds < 4 ||
        observe_seconds > 600) {
        return ERROR_ACCESS_DENIED;
    }
    DWORD directory_error = ERROR_SUCCESS;
    if (!EnsureDirectoryTree(session_path / L"reports", &directory_error) ||
        !EnsureDirectoryTree(session_path / L"raw", &directory_error)) {
        return directory_error == ERROR_SUCCESS ? ERROR_PATH_NOT_FOUND :
                                                   static_cast<int>(directory_error);
    }
    const fs::path controller_report = session_path / L"reports" /
        L"headless-enhanced-capture.json";
    const auto write_controller = [&](std::string_view status, bool attached,
                                      bool detached, bool strict_unload,
                                      bool target_alive, std::string_view error) {
        return WriteUtf8FileAtomic(controller_report, MakeJsonObject({
            {"SchemaVersion", "god2-headless-enhanced-capture-v1"},
            {"UpdatedAtUtc", UtcNow()}, {"SessionId", std::string(session_id)},
            {"Status", std::string(status)}, {"TargetProcessId", std::to_string(process_id)},
            {"ObserveSeconds", std::to_string(observe_seconds)},
            {"Attached", attached ? "true" : "false"},
            {"Detached", detached ? "true" : "false"},
            {"StrictUnloadVerified", strict_unload ? "true" : "false"},
            {"TargetAliveAfterDetach", target_alive ? "true" : "false"},
            {"SharedRingHealthPath", "reports/semantic-shared-ring.json"},
            {"SegmentManifestPath", "reports/semantic-segment-manifest.json"},
            {"Error", std::string(error)}
        }, {"TargetProcessId", "ObserveSeconds", "Attached", "Detached",
            "StrictUnloadVerified", "TargetAliveAfterDetach"}) + "\n");
    };

    HANDLE cancel_event = OpenEventW(SYNCHRONIZE, FALSE, stop_event_name.c_str());
    if (cancel_event == nullptr) return static_cast<int>(GetLastError());
    EnhancedCaptureManager manager;
    manager.Configure(true, session_path, session_id, game_path, cancel_event);
    std::string error;
    const bool attached = manager.TryAttach(process_id, &error);
    if (!attached) {
        std::string stop_error;
        manager.Stop(&stop_error);
        CloseHandle(cancel_event);
        write_controller("EVIDENCE_BLOCKED_ATTACH_FAILED", false, false, false,
                         false, error.empty() ? stop_error : error);
        return ERROR_GEN_FAILURE;
    }
    if (!write_controller("ATTACHED", true, false, false, true, "")) {
        SetEvent(cancel_event);
        std::string ignored;
        manager.Stop(&ignored);
        CloseHandle(cancel_event);
        return ERROR_WRITE_FAULT;
    }

    for (DWORD second = 0; second < observe_seconds; ++second) {
        if (WaitForSingleObject(cancel_event, 1000) == WAIT_OBJECT_0) break;
        const auto state = manager.ObserveConsumerState();
        if (state != ConsumerState::Ready && state != ConsumerState::Starting) {
            error = "enhanced capture left Ready state before the bounded observation completed";
            break;
        }
    }
    std::string stop_error;
    const bool stopped = manager.Stop(&stop_error);
    CloseHandle(cancel_event);
    UniqueHandle target(OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION,
                                    FALSE, process_id));
    const bool target_alive = target.Get() != nullptr &&
        WaitForSingleObject(target.Get(), 0) == WAIT_TIMEOUT;
    const bool complete = stopped && target_alive && error.empty();
    if (!write_controller(complete ? "DETACHED" : "EVIDENCE_BLOCKED_STOP_FAILED",
                          true, stopped, stopped, target_alive,
                          error.empty() ? stop_error : error)) {
        return ERROR_WRITE_FAULT;
    }
    return complete ? ERROR_SUCCESS : ERROR_GEN_FAILURE;
}

int RunElevatedCaptureWorker(const std::vector<std::wstring>& arguments) {
    const auto session_value = ArgumentValue(arguments, L"--session");
    const auto backend_value = ArgumentValue(arguments, L"--backend");
    const auto pipe_value = ArgumentValue(arguments, L"--pipe");
    const auto nonce_value = ArgumentValue(arguments, L"--nonce");
    const auto cancel_event_value = ArgumentValue(arguments, L"--cancel-event");
    if (!session_value || !backend_value || !pipe_value || !nonce_value || !cancel_event_value ||
        !IsAdministrator()) return ERROR_ACCESS_DENIED;
    const fs::path session_path(*session_value);
    if (!IsApprovedSessionPath(session_path)) return ERROR_ACCESS_DENIED;
    UniqueHandle controller_cancel(OpenEventW(SYNCHRONIZE, FALSE, cancel_event_value->c_str()));
    if (controller_cancel.Get() == nullptr) return static_cast<int>(GetLastError());
    HANDLE pipe = INVALID_HANDLE_VALUE;
    DWORD pipe_error = ERROR_FILE_NOT_FOUND;
    for (int attempt = 0; attempt < 100 && pipe == INVALID_HANDLE_VALUE; ++attempt) {
        pipe = CreateFileW(pipe_value->c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, 0, nullptr);
        if (pipe == INVALID_HANDLE_VALUE) {
            pipe_error = GetLastError();
            Sleep(100);
        }
    }
    if (pipe == INVALID_HANDLE_VALUE) return static_cast<int>(pipe_error);
    auto backend = CreateBackend(WideToUtf8(*backend_value));
    if (!backend) {
        WritePipeMessage(pipe, "ERROR\t" + WideToUtf8(*nonce_value) + "\tunknown backend");
        CloseHandle(pipe);
        return ERROR_NOT_SUPPORTED;
    }
    CaptureRequest request;
    request.session_path = session_path;
    const auto capabilities = backend->Probe();
    const auto started = backend->Start(request);
    const bool privacy_blocked = IsRawPrivacyPolicyBlock(started);
    const bool raw_backend_started = started.success;
    if (!raw_backend_started && !privacy_blocked) {
        WritePipeMessage(pipe, "ERROR\t" + WideToUtf8(*nonce_value) + "\t" + started.message);
        CloseHandle(pipe);
        return static_cast<int>(started.exit_code);
    }
    bool startup_cleanup_required = false;
    if (WaitForSingleObject(controller_cancel.Get(), 0) == WAIT_OBJECT_0) {
        if (privacy_blocked) {
            WritePipeMessage(pipe, "CANCELLED_STOPPED\t" + WideToUtf8(*nonce_value) +
                                   "\tsemantic worker cancelled; raw backend remained EvidenceBlockedPrivacyPolicy");
            FlushFileBuffers(pipe);
            CloseHandle(pipe);
            return ERROR_CANCELLED;
        }
        const auto cancelled_stop = backend->Stop(request);
        if (cancelled_stop.success) {
            const std::string detail = cancelled_stop.message.empty() ?
                "backend stopped after immediate startup cancellation" : cancelled_stop.message;
            WritePipeMessage(pipe, "CANCELLED_STOPPED\t" + WideToUtf8(*nonce_value) + "\t" + detail);
            FlushFileBuffers(pipe);
            CloseHandle(pipe);
            return ERROR_CANCELLED;
        }
        const std::string detail = cancelled_stop.message.empty() ?
            "backend stop failed after immediate startup cancellation" : cancelled_stop.message;
        WritePipeMessage(pipe, "CLEANUP_REQUIRED\t" + WideToUtf8(*nonce_value) + "\t" + detail);
        FlushFileBuffers(pipe);
        startup_cleanup_required = true;
    }
    std::string worker_error;
    std::optional<CaptureWorkerIdentity> evidence_worker;
    if (raw_backend_started && !startup_cleanup_required &&
        backend->Name() == "EtwNetworkTraceBackend" && capabilities.supports_realtime_events) {
        evidence_worker = StartEtwConsumerProcess(session_path, controller_cancel.Get(),
                                                  *cancel_event_value, &worker_error);
    } else if (raw_backend_started && !startup_cleanup_required &&
               backend->Name() == "PktMonCaptureBackend" && capabilities.supports_drop_statistics) {
        evidence_worker = StartPktMonCounterProcess(session_path, &worker_error);
    }
    if (!startup_cleanup_required && WaitForSingleObject(controller_cancel.Get(), 0) == WAIT_OBJECT_0) {
        if (privacy_blocked) {
            WritePipeMessage(pipe, "CANCELLED_STOPPED\t" + WideToUtf8(*nonce_value) +
                                   "\tsemantic worker cancelled; raw backend remained EvidenceBlockedPrivacyPolicy");
            FlushFileBuffers(pipe);
            CloseHandle(pipe);
            return ERROR_CANCELLED;
        }
        const auto cancelled_stop = backend->Stop(request);
        std::string evidence_error;
        const bool evidence_stopped = !evidence_worker ||
            WaitForCaptureWorker(*evidence_worker, 30'000, &evidence_error);
        if (cancelled_stop.success) {
            std::string detail = "backend stopped after startup cancellation";
            if (cancelled_stop.exit_code != ERROR_SUCCESS && !cancelled_stop.message.empty())
                detail += "; " + cancelled_stop.message;
            if (!evidence_stopped && !evidence_error.empty()) detail += "; " + evidence_error;
            WritePipeMessage(pipe, "CANCELLED_STOPPED\t" + WideToUtf8(*nonce_value) + "\t" + detail);
            FlushFileBuffers(pipe);
            CloseHandle(pipe);
            return ERROR_CANCELLED;
        }
        std::string detail = cancelled_stop.message;
        if (!evidence_error.empty()) {
            if (!detail.empty()) detail += "; ";
            detail += evidence_error;
        }
        WritePipeMessage(pipe, "CLEANUP_REQUIRED\t" + WideToUtf8(*nonce_value) + "\t" + detail);
        FlushFileBuffers(pipe);
        // Keep the command channel alive so the controller can retry STOP.
        startup_cleanup_required = true;
    }
    const auto cleanup_after_control_loss = [&]() {
        SetEvent(controller_cancel.Get());
        if (raw_backend_started) {
            for (;;) {
                const auto abandoned_stop = backend->Stop(request);
                if (abandoned_stop.success) break;
                Sleep(1'000);
            }
        }
        if (evidence_worker) {
            std::string ignored;
            WaitForCaptureWorker(*evidence_worker, 30'000, &ignored);
        }
    };
    if (!startup_cleanup_required) {
        const bool post_capture_fallback = !worker_error.empty();
        const std::string start_detail = post_capture_fallback ?
            "POST_CAPTURE_FALLBACK:" + worker_error : started.message;
        const std::string ready_kind = privacy_blocked ? "SEMANTIC_READY\t" : "STARTED\t";
        if (!WritePipeMessage(pipe, ready_kind + WideToUtf8(*nonce_value) + "\t" + start_detail)) {
            cleanup_after_control_loss();
            CloseHandle(pipe);
            return ERROR_BROKEN_PIPE;
        }
    }
    for (;;) {
        const auto command = ReadPipeMessage(pipe);
        const std::string probe_prefix = "PROBE_GAME\t" + WideToUtf8(*nonce_value) + "\t";
        if (command && command->rfind(probe_prefix, 0) == 0) {
            const auto detection = GameProcessTracker::Detect(Utf8ToWide(command->substr(probe_prefix.size())),
                                                               session_path);
            std::string consumer_runtime = evidence_worker ? "Ready" : "NotApplicable";
            DWORD consumer_exit_code = evidence_worker ? STILL_ACTIVE : ERROR_SUCCESS;
            if (evidence_worker && evidence_worker->worker_type == "etw-consumer") {
                const auto runtime = QueryCaptureWorkerRuntime(*evidence_worker);
                if (runtime.identity_valid) {
                    consumer_runtime = runtime.running ? "Ready" : "Failed";
                    consumer_exit_code = runtime.exit_code;
                } else {
                    consumer_runtime = "Unknown";
                    consumer_exit_code = runtime.win32_error;
                }
            }
            const std::uint64_t created =
                (static_cast<std::uint64_t>(detection.process_creation_time.dwHighDateTime) << 32) |
                detection.process_creation_time.dwLowDateTime;
            const std::string response = "GAME_PROCESS\t" + WideToUtf8(*nonce_value) + "\t" +
                std::to_string(detection.process_id) + "\t" + WideToUtf8(detection.actual_path.wstring()) + "\t" +
                (detection.is_x86 ? "true" : "false") + "\t" +
                std::to_string(detection.parent_process_id) + "\t" + std::to_string(created) + "\t" +
                std::to_string(static_cast<int>(detection.state)) + "\t" +
                std::to_string(detection.win32_error) + "\t" +
                std::to_string(reinterpret_cast<std::uintptr_t>(detection.game_window)) + "\t" +
                consumer_runtime + "\t" + std::to_string(consumer_exit_code);
            if (!WritePipeMessage(pipe, response)) {
                cleanup_after_control_loss();
                CloseHandle(pipe);
                return ERROR_BROKEN_PIPE;
            }
            continue;
        }
        if (!command || *command != "STOP\t" + WideToUtf8(*nonce_value)) {
            cleanup_after_control_loss();
            CloseHandle(pipe);
            return ERROR_INVALID_DATA;
        }
        CaptureResult stopped;
        if (raw_backend_started) {
            stopped = backend->Stop(request);
            if (!stopped.success) {
                if (!WritePipeMessage(pipe, "ERROR\t" + WideToUtf8(*nonce_value) + "\t" + stopped.message)) {
                    CloseHandle(pipe);
                    return ERROR_BROKEN_PIPE;
                }
                FlushFileBuffers(pipe);
                continue;
            }
        } else {
            stopped = {false, ERROR_ACCESS_DISABLED_BY_POLICY,
                       "EvidenceBlockedPrivacyPolicy: raw backend was never started"};
        }
        const bool evidence_ok = !evidence_worker || WaitForCaptureWorker(*evidence_worker, 30'000, &worker_error);
        std::string warning;
        if (raw_backend_started && stopped.exit_code != ERROR_SUCCESS) warning = stopped.message;
        if (!evidence_ok) {
            if (!warning.empty()) warning += "; ";
            warning += worker_error;
        }
        const std::string stop_message = warning.empty() ? stopped.message : "WARNING:" + warning;
        const std::string stopped_kind = privacy_blocked ? "SEMANTIC_STOPPED\t" : "STOPPED\t";
        WritePipeMessage(pipe, stopped_kind + WideToUtf8(*nonce_value) + "\t" + stop_message);
        FlushFileBuffers(pipe);
        CloseHandle(pipe);
        return warning.empty() ? 0 : ERROR_GEN_FAILURE;
    }
}

int RunGuiApplication(HINSTANCE instance, int show_command,
                      GuiLiveSessionBinding* live_binding) {
    LiveBindingRegistration live_registration(live_binding);
    if (!live_registration.Registered()) return ERROR_BUSY;
    g_ui_language.store(DetectUiLanguage());
    LogStageBegin(L"AcquireSingleInstance");
    std::wstring instance_mutex_name = L"Local\\God2PacketCapture.Gui.MainInstance";
    wchar_t internal_test_mutex[128]{};
    const DWORD internal_test_mutex_length = GetEnvironmentVariableW(
        L"GOD2_PACKET_CAPTURE_INTERNAL_TEST_MUTEX", internal_test_mutex,
        static_cast<DWORD>(std::size(internal_test_mutex)));
    const bool internal_test_launch = wcsstr(GetCommandLineW(), L"--internal-gui-test-") != nullptr;
    if (internal_test_launch && internal_test_mutex_length != 0 && internal_test_mutex_length < std::size(internal_test_mutex))
        instance_mutex_name += L"." + std::wstring(internal_test_mutex, internal_test_mutex_length);
    g_instance_property_name = kInstanceTagProperty;
    if (internal_test_launch && internal_test_mutex_length != 0 && internal_test_mutex_length < std::size(internal_test_mutex))
        g_instance_property_name += L"." + std::wstring(internal_test_mutex, internal_test_mutex_length);
    SetLastError(ERROR_SUCCESS);
    HANDLE raw_instance_mutex = CreateMutexW(nullptr, FALSE, instance_mutex_name.c_str());
    const DWORD mutex_result = GetLastError();
    if (raw_instance_mutex == nullptr)
        throw StartupFailure(L"AcquireSingleInstance", L"CreateMutexW", "single-instance mutex creation failed",
                             mutex_result, L"NULL");
    UniqueHandle instance_mutex(raw_instance_mutex);
    if (mutex_result == ERROR_ALREADY_EXISTS) {
        AppendStartupLog(L"SingleInstance", L"AlreadyExists");
        HWND existing = nullptr;
        for (int attempt = 0; attempt < 30 && existing == nullptr; ++attempt) {
            existing = ExistingInstanceWindow();
            if (existing == nullptr) Sleep(100);
        }
        if (existing != nullptr) {
            if (IsIconic(existing)) ShowWindow(existing, SW_RESTORE);
            else ShowWindow(existing, SW_SHOW);
            SetForegroundWindow(existing);
            AppendStartupLog(L"SingleInstanceAction", L"ExistingWindowActivated");
        } else {
            AppendStartupLog(L"SingleInstanceAction", L"ExistingWindowNotReady; showing message");
            MessageBoxW(nullptr, Text(UiString::AlreadyRunning),
                        kWindowTitle, MB_OK | MB_ICONINFORMATION | MB_SETFOREGROUND);
        }
        LogStageSuccess(L"AcquireSingleInstance");
        return ERROR_ALREADY_EXISTS;
    }
    LogStageSuccess(L"AcquireSingleInstance");

    std::vector<std::wstring> process_arguments;
    int process_argument_count = 0;
    wchar_t** process_argument_values = CommandLineToArgvW(GetCommandLineW(), &process_argument_count);
    if (process_argument_values != nullptr) {
        for (int index = 1; index < process_argument_count; ++index)
            process_arguments.emplace_back(process_argument_values[index]);
        LocalFree(process_argument_values);
    }
    if (const auto ready_event_name = ArgumentValue(process_arguments, L"--internal-gui-test-ready-event")) {
        HANDLE ready_event = OpenEventW(EVENT_MODIFY_STATE, FALSE, ready_event_name->c_str());
        if (ready_event != nullptr) {
            SetEvent(ready_event);
            CloseHandle(ready_event);
        }
    }
    if (const auto delay_value = ArgumentValue(process_arguments, L"--internal-gui-test-delay-ms")) {
        wchar_t* end = nullptr;
        const unsigned long delay = wcstoul(delay_value->c_str(), &end, 10);
        if (end != delay_value->c_str() && *end == L'\0' && delay <= 10'000) Sleep(static_cast<DWORD>(delay));
    }

    LogStageBegin(L"InitializeCommonControls");
    DWORD common_controls_error = ERROR_SUCCESS;
    if (!InitializeCommonControlsForGui(&common_controls_error))
        throw StartupFailure(L"InitializeCommonControls", L"InitCommonControlsEx",
                             "common controls initialization failed", common_controls_error, L"FALSE");
    LogStageSuccess(L"InitializeCommonControls");

    LogStageBegin(L"RegisterMainWindowClass");
    WNDCLASSEXW window_class{};
    window_class.cbSize = sizeof(window_class);
    window_class.lpfnWndProc = WindowProcedure;
    window_class.hInstance = instance;
    window_class.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    window_class.hIcon = reinterpret_cast<HICON>(LoadImageW(instance,
        MAKEINTRESOURCEW(IDI_GOD2_PACKET_CAPTURE), IMAGE_ICON,
        GetSystemMetrics(SM_CXICON), GetSystemMetrics(SM_CYICON), LR_SHARED));
    window_class.hIconSm = reinterpret_cast<HICON>(LoadImageW(instance,
        MAKEINTRESOURCEW(IDI_GOD2_PACKET_CAPTURE), IMAGE_ICON,
        GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON), LR_SHARED));
    if (window_class.hIcon == nullptr) window_class.hIcon = LoadIconW(nullptr, IDI_APPLICATION);
    if (window_class.hIconSm == nullptr) window_class.hIconSm = window_class.hIcon;
    window_class.hbrBackground = reinterpret_cast<HBRUSH>(COLOR_WINDOW + 1);
    window_class.lpszClassName = kWindowClass;
    RegisterOrValidateWindowClass(window_class, L"RegisterMainWindowClass");
    LogStageSuccess(L"RegisterMainWindowClass");

    AppState state;
    LogStageBegin(L"CreateMainWindow");
    RECT startup_work_area{0, 0, GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN)};
    SystemParametersInfoW(SPI_GETWORKAREA, 0, &startup_work_area, 0);
    const RECT startup_bounds = FittedMainWindowBounds(startup_work_area);
    const HWND window = CreateWindowExW(0, kWindowClass, kWindowTitle,
                                         WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX,
                                         startup_bounds.left, startup_bounds.top,
                                         startup_bounds.right - startup_bounds.left,
                                         startup_bounds.bottom - startup_bounds.top,
                                         nullptr, nullptr, instance, &state);
    if (window == nullptr) {
        const DWORD create_error = GetLastError();
        throw StartupFailure(L"CreateMainWindow", L"CreateWindowExW", "main window creation failed",
                             create_error, L"NULL");
    }
    LogStageSuccess(L"CreateMainWindow");

    LogStageBegin(L"ShowMainWindow");
    ShowWindow(window, show_command == 0 ? SW_SHOWNORMAL : show_command);
    SetLastError(ERROR_SUCCESS);
    if (!UpdateWindow(window)) {
        const DWORD update_error = GetLastError();
        throw StartupFailure(L"ShowMainWindow", L"UpdateWindow", "main window update failed",
                             update_error, L"FALSE");
    }
    LogStageSuccess(L"ShowMainWindow");

    // Recovery packaging is deliberately asynchronous: a stale interrupted
    // session must never delay or suppress the visible GUI entry point.
    if (!internal_test_launch) {
        try {
            std::thread([] {
                EvidencePackageBuilder::RecoverIncompleteSessionsNoThrow();
            }).detach();
        } catch (const std::exception& exception) {
            AppendStartupLog(L"PartialRecoveryThreadWarning", Utf8ToWide(exception.what()));
        } catch (...) {
            AppendStartupLog(L"PartialRecoveryThreadWarning", L"Unknown exception");
        }
    }

    if (!g_startup_log_warning.empty())
        MessageBoxW(window, g_startup_log_warning.c_str(), kWindowTitle, MB_OK | MB_ICONWARNING);

    LogStageBegin(L"RegisterObservationWindowClass");
    WNDCLASSEXW observation_class = window_class;
    observation_class.lpfnWndProc = ObservationWindowProcedure;
    observation_class.lpszClassName = kObservationWindowClass;
    try {
        RegisterOrValidateWindowClass(observation_class, L"RegisterObservationWindowClass");
        LogStageSuccess(L"RegisterObservationWindowClass");
    } catch (const StartupFailure& failure) {
        LogStageFailure(failure.Stage(), failure.Api(), failure.ReturnValue(), failure.Win32Error(), failure.Result());
        AppendStartupLog(L"NonFatalStartupWarning", Utf8ToWide(failure.what()));
    }

    LogStageBegin(L"InitializeControllers");
    LogStageSuccess(L"InitializeControllers");
    LogStageBegin(L"MessageLoop");
    MSG message{};
    BOOL result = 0;
    while ((result = GetMessageW(&message, nullptr, 0, 0)) > 0) {
        // The main window is a modeless Win32 window rather than a dialog
        // resource.  Route dialog-navigation keys explicitly so Tab and
        // Shift+Tab move between the launcher path, enhancement option and
        // command buttons instead of remaining trapped on one control.
        if (IsDialogMessageW(window, &message)) continue;
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }
    if (result == -1) {
        const DWORD message_error = GetLastError();
        throw StartupFailure(L"MessageLoop", L"GetMessageW", "message retrieval failed", message_error, L"-1");
    }
    LogStageSuccess(L"MessageLoop");
    return static_cast<int>(message.wParam);
}

int RunGuiSmokeTest(const fs::path& report_path) {
    std::vector<std::string> failures;
    const bool delayed_game_probe_reply_contract =
        IsDelayedGameProbeResponse("GAME_PROCESS\tworker-nonce\t4016\tC:\\Game\\God2_opt.exe\ttrue",
                                   "worker-nonce") &&
        !IsDelayedGameProbeResponse("GAME_PROCESS\tdifferent-nonce\t4016", "worker-nonce") &&
        !IsDelayedGameProbeResponse("STOPPED\tworker-nonce\tStopped", "worker-nonce");
    if (!delayed_game_probe_reply_contract)
        failures.emplace_back("delayed GAME_PROCESS replies are not isolated from the STOPPED worker response");
    const auto executable = ExecutablePath();
    const bool version_v130_ultimate_contract = FileVersion(executable) == "1.3.0.0";
    if (!version_v130_ultimate_contract)
        failures.emplace_back("PE VERSIONINFO is not the required v1.3.0.0 Ultimate version");
    const auto executable_sha256 = CalculateFileSha256(executable);
    const bool executable_sha256_bound = executable_sha256 && executable_sha256->size() == 64U &&
        std::all_of(executable_sha256->begin(), executable_sha256->end(), [](char value) {
            return std::isxdigit(static_cast<unsigned char>(value)) != 0;
        });
    if (!executable_sha256_bound)
        failures.emplace_back("GUI smoke evidence could not bind itself to the exact executable SHA-256");
    const std::wstring isolated_mutex = Utf8ToWide(NewId());
    if (!SetEnvironmentVariableW(L"GOD2_PACKET_CAPTURE_INTERNAL_TEST_MUTEX", isolated_mutex.c_str()))
        failures.emplace_back("could not isolate GUI smoke-test mutex from a running production window");

    const auto contract_root = LocalDataRoot() / L"Temp" / (L"gui-startup-contract-" + Utf8ToWide(NewId()));
    DWORD directory_error = ERROR_SUCCESS;
    const bool logs_missing = EnsureDirectoryTree(contract_root / L"God2Classic" / L"PacketCapture" / L"Logs",
                                                   &directory_error);
    if (!logs_missing) failures.emplace_back("a fresh Logs directory tree could not be created");
    const bool logs_existing = logs_missing &&
        EnsureDirectoryTree(contract_root / L"God2Classic" / L"PacketCapture" / L"Logs", &directory_error);
    if (!logs_existing) failures.emplace_back("an existing Logs directory was treated as a failure");
    const auto existing_parent = contract_root / L"ExistingParents" / L"God2Classic" / L"PacketCapture";
    const bool parent_created = EnsureDirectoryTree(existing_parent, &directory_error);
    const bool existing_parents = parent_created && EnsureDirectoryTree(existing_parent, &directory_error) &&
        EnsureDirectoryTree(existing_parent / L"Logs", &directory_error);
    if (!existing_parents) failures.emplace_back("existing parent directories prevented Logs creation");
    std::string enhanced_payload_error;
    const bool enhanced_x86_payload_structure = ValidateEnhancedCapturePayloadStructure(
        contract_root / L"EnhancedPayload", &enhanced_payload_error);
    if (!enhanced_x86_payload_structure)
        failures.emplace_back("embedded x86 enhanced capture payload failed: " + enhanced_payload_error);
    const auto enhanced_status_root = contract_root / L"EnhancedStatus";
    bool enhanced_plaintext_status_contract =
        EnsureDirectoryTree(enhanced_status_root / L"reports", &directory_error);
    std::string enhanced_status_schema_observed = "UNAVAILABLE";
    std::string enhanced_initial_strict_unload_observed = "null";
    bool dll_twenty_five_domain_status_contract = false;
    bool enhanced_no_attach_failure_preserved = false;
    bool enhanced_non_admin_attach_fail_closed = true;
    if (enhanced_plaintext_status_contract) {
        auto enhanced_status_manager = std::make_unique<EnhancedCaptureManager>();
        enhanced_status_manager->Configure(true, enhanced_status_root,
                                           "gui-smoke-enhanced-status", {}, nullptr);
        const auto enhanced_status = ReadUtf8File(
            enhanced_status_root / L"reports" / L"enhanced-capture-status.json");
        Fields enhanced_status_fields;
        std::string enhanced_status_error;
        const bool enhanced_status_parsed = enhanced_status &&
            ParseFlatJson(*enhanced_status, enhanced_status_fields,
                          &enhanced_status_error) &&
            enhanced_status_fields.size() == 46U;
        constexpr std::array<std::string_view, 46> enhanced_status_keys{
            "SchemaVersion", "UpdatedAtUtc", "Requested", "Status", "Detail",
            "WasEverAttached", "ProbeReady", "StrictUnloadVerified", "FailureKind",
            "InjectionAttempted", "ModuleWasEverLoaded", "ModuleLoadStateVerified",
            "ModuleSnapshotVerified", "ModuleAbsent", "TargetProcessExited",
            "TargetIdentityVerified", "InjectorPresent", "ProbePresent",
            "InjectorPayloadValidated", "ProbePayloadValidated", "TargetExecutable",
            "TargetArchitecture", "ProbeScope", "ProbeDomainCount", "ProbeDomains",
            "ConfirmedExactBuildDomainCount", "ConfirmedExactBuildDomains",
            "CandidateOnlyDomainCount", "CandidateOnlyStatus",
            "DeepProbeCandidateMapSchema", "CandidatePromotionGateCount",
            "CandidateAbiSafetyGateCount", "CandidateActivationGateCount",
            "CandidateAbiSafetyContract", "SemanticEventContract",
            "SharedTransportContract", "InjectionSequence", "ReadinessHandshake",
            "PayloadStaging", "PayloadDacl", "PayloadMandatoryLabel",
            "SemanticIpcBinding", "PayloadLaunchMode", "UnloadPolicy", "Apis",
            "InputAndWindowHooks"};
        bool enhanced_status_exact_keys = enhanced_status_parsed;
        for (const auto key : enhanced_status_keys)
            enhanced_status_exact_keys = enhanced_status_exact_keys &&
                enhanced_status_fields.find(std::string(key)) !=
                    enhanced_status_fields.end();
        if (enhanced_status && enhanced_status->find(
                "\"SchemaVersion\":\"god2-enhanced-capture-status-v2\"") != std::string::npos)
            enhanced_status_schema_observed = "god2-enhanced-capture-status-v2";
        if (enhanced_status && enhanced_status->find(
                "\"StrictUnloadVerified\":false") != std::string::npos)
            enhanced_initial_strict_unload_observed = "false";
        else if (enhanced_status && enhanced_status->find(
                     "\"StrictUnloadVerified\":true") != std::string::npos)
            enhanced_initial_strict_unload_observed = "true";
        enhanced_plaintext_status_contract = enhanced_status &&
            enhanced_status_exact_keys &&
            enhanced_status->find("\"SchemaVersion\":\"god2-enhanced-capture-status-v2\"") != std::string::npos &&
            enhanced_status->find("\"StrictUnloadVerified\":false") != std::string::npos &&
            enhanced_status->find("\"InjectionAttempted\":false") != std::string::npos &&
            enhanced_status->find("\"ModuleWasEverLoaded\":false") != std::string::npos &&
            enhanced_status->find("\"ModuleLoadStateVerified\":true") != std::string::npos &&
            enhanced_status->find("\"ModuleSnapshotVerified\":false") != std::string::npos &&
            enhanced_status->find("\"ModuleAbsent\":true") != std::string::npos &&
            enhanced_status->find("\"TargetProcessExited\":false") != std::string::npos &&
            enhanced_status->find("\"TargetIdentityVerified\":false") != std::string::npos &&
            enhanced_status->find("\"InjectorPayloadValidated\":false") != std::string::npos &&
            enhanced_status->find("\"ProbePayloadValidated\":false") != std::string::npos &&
            enhanced_status->find("\"ProbeScope\":\"WinsockTransport+PostDecrypt+PreEncrypt+HandlerDecoded\"") != std::string::npos &&
            enhanced_status->find("\"Apis\":\"send,WSASend,recv,WSARecv,OverlappedWSARecvCompletionRoutine,WSAGetOverlappedResult,GetQueuedCompletionStatus,GetQueuedCompletionStatusEx,PostDecrypt,PreEncrypt,HandlerDecoded\"") != std::string::npos &&
            enhanced_status->find("\"InputAndWindowHooks\":\"Disabled\"") != std::string::npos &&
            enhanced_status->find("\"PayloadStaging\":\"ProtectedProgramDataRandomDirectory\"") != std::string::npos &&
            enhanced_status->find("\"PayloadDacl\":\"Protected:SYSTEM+AdministratorsFull;CurrentUserReadExecuteOnly;EveryoneAbsent\"") != std::string::npos &&
            enhanced_status->find("\"PayloadMandatoryLabel\":\"MediumNoWriteUp\"") != std::string::npos &&
            enhanced_status->find("\"SemanticIpcBinding\":\"DuplicatedTargetHandlesV2;NoNamedObjectAuthority\"") != std::string::npos &&
            enhanced_status->find("\"PayloadLaunchMode\":\"AlreadyElevatedCreateProcessW\"") != std::string::npos &&
            enhanced_status->find("\"UnloadPolicy\":\"QuiesceRestoreDrainAndProveModuleAbsent;OtherwiseEvidenceBlockedAndBoundedRetry;NeverReportResidentInactiveAsStopped\"") != std::string::npos;
        dll_twenty_five_domain_status_contract = enhanced_status &&
            enhanced_status->find("\"ProbeDomainCount\":25") != std::string::npos &&
            enhanced_status->find("\"ProbeDomains\":\"Network,Parser,Serializer,Handler,Object,Allocation,VTable,Factory,ManagerLookup,Registry,ResourceDecode,Mutation,TaintSeed,FormulaOperand,Quest,Map,Portal,NPC,Monster,Battle,Inventory,Item,Skill,Pet/Mount,Snapshot\"") != std::string::npos &&
            enhanced_status->find("\"ConfirmedExactBuildDomainCount\":4") != std::string::npos &&
            enhanced_status->find("\"ConfirmedExactBuildDomains\":\"Network,Parser,Serializer,Handler\"") != std::string::npos &&
            enhanced_status->find("\"CandidateOnlyDomainCount\":21") != std::string::npos &&
            enhanced_status->find("\"CandidateOnlyStatus\":\"EvidenceBlockedUnconfirmedProbe\"") != std::string::npos &&
            enhanced_status->find("\"DeepProbeCandidateMapSchema\":\"god2-deep-probe-candidate-map-v2\"") != std::string::npos &&
            enhanced_status->find("\"CandidatePromotionGateCount\":10") != std::string::npos &&
            enhanced_status->find("\"CandidateAbiSafetyGateCount\":4") != std::string::npos &&
            enhanced_status->find("\"CandidateActivationGateCount\":14") != std::string::npos &&
            enhanced_status->find("\"CandidateAbiSafetyContract\":\"ArgumentContract;ReturnValueLifetime;ThreadContext;ReentrancyRisk\"") != std::string::npos &&
            enhanced_status->find("\"SemanticEventContract\":\"God2SemanticEvent;SchemaVersion=2;StrictTypes;25EventTypes\"") != std::string::npos &&
            enhanced_status->find("\"SharedTransportContract\":\"VersionedBounded25DomainPriorityRing;Priority0AuthoritativeTo3Candidate;BatchPublishing;Sequence;PerDomainDropAndWriteFailureCounters;FirstLastDroppedSequence;Reason;ConsumerLag\"") != std::string::npos;
        std::string enhanced_stop_error;
        const bool enhanced_stop_ok = enhanced_status_manager->Stop(&enhanced_stop_error);
        const auto stopped_status = ReadUtf8File(
            enhanced_status_root / L"reports" / L"enhanced-capture-status.json");
        enhanced_no_attach_failure_preserved = !enhanced_stop_ok && !enhanced_stop_error.empty() && stopped_status &&
            stopped_status->find("\"Status\":\"EvidenceBlocked\"") != std::string::npos &&
            stopped_status->find("\"FailureKind\":\"NoVerifiedAttachment\"") != std::string::npos &&
            stopped_status->find("\"WasEverAttached\":false") != std::string::npos &&
            stopped_status->find("\"ProbeReady\":false") != std::string::npos;

        if (!IsAdministrator()) {
            const auto non_admin_root = enhanced_status_root / L"NonAdminAttach";
            enhanced_non_admin_attach_fail_closed =
                EnsureDirectoryTree(non_admin_root / L"reports", &directory_error);
            if (enhanced_non_admin_attach_fail_closed) {
                auto non_admin_manager = std::make_unique<EnhancedCaptureManager>();
                non_admin_manager->Configure(true, non_admin_root,
                                             "gui-smoke-non-admin", ExecutablePath(), nullptr);
                std::string non_admin_error;
                const bool attached = non_admin_manager->TryAttach(GetCurrentProcessId(), &non_admin_error);
                const auto non_admin_status = ReadUtf8File(
                    non_admin_root / L"reports" / L"enhanced-capture-status.json");
                enhanced_non_admin_attach_fail_closed = !attached && non_admin_status &&
                    non_admin_error.find(
                        "EvidenceBlockedAdministratorRequiredForSecurePayloadStaging") != std::string::npos &&
                    non_admin_status->find(
                        "\"FailureKind\":\"EvidenceBlockedAdministratorRequiredForSecurePayloadStaging\"") !=
                        std::string::npos;
            }
        }
    }
    if (!enhanced_plaintext_status_contract)
        failures.emplace_back("enhanced capture status still reports the obsolete network-only probe scope");
    if (!dll_twenty_five_domain_status_contract)
        failures.emplace_back("enhanced capture status omits the complete 25-domain, candidate-map, activation-gate, semantic-event, or shared-transport contract");
    if (!enhanced_no_attach_failure_preserved)
        failures.emplace_back("Stop overwrote or concealed the enhanced-capture no-attachment failure");
    if (!enhanced_non_admin_attach_fail_closed)
        failures.emplace_back("non-administrator enhanced attach did not fail closed before payload staging");
    PSECURITY_DESCRIPTOR payload_acl_fixture = nullptr;
    std::string payload_acl_error;
    const bool payload_acl_contract =
        BuildProtectedPayloadSecurityDescriptor(&payload_acl_fixture, &payload_acl_error);
    if (payload_acl_fixture != nullptr) LocalFree(payload_acl_fixture);
    if (!payload_acl_contract)
        failures.emplace_back("protected payload current-user RX/medium-IL ACL contract failed: " +
                              payload_acl_error);

    const auto launch_with_log_root = [&](const fs::path& log_root) {
        std::wstring child_command = QuoteArgument(executable.wstring()) +
            L" --internal-gui-test-log-root " + QuoteArgument(log_root.wstring());
        std::vector<wchar_t> mutable_child_command(child_command.begin(), child_command.end());
        mutable_child_command.push_back(L'\0');
        STARTUPINFOW child_startup{};
        child_startup.cb = sizeof(child_startup);
        PROCESS_INFORMATION child_process{};
        const BOOL created = CreateProcessW(executable.c_str(), mutable_child_command.data(), nullptr, nullptr, FALSE,
                                            CREATE_UNICODE_ENVIRONMENT, nullptr, executable.parent_path().c_str(),
                                            &child_startup, &child_process);
        if (!created) return false;
        CloseHandle(child_process.hThread);
        HWND child_window = nullptr;
        for (int attempt = 0; attempt < 100 && child_window == nullptr; ++attempt) {
            if (WaitForSingleObject(child_process.hProcess, 0) == WAIT_OBJECT_0) break;
            child_window = MainWindowForProcess(child_process.dwProcessId);
            if (child_window == nullptr) Sleep(100);
        }
        bool log_created = false;
        std::error_code enumerate_error;
        for (fs::directory_iterator iterator(log_root, enumerate_error), end;
             iterator != end && !enumerate_error; iterator.increment(enumerate_error)) {
            if (iterator->is_regular_file(enumerate_error) &&
                iterator->path().filename().wstring().rfind(L"startup-", 0) == 0 &&
                iterator->path().extension() == L".log") {
                log_created = true;
                break;
            }
        }
        if (child_window != nullptr) PostMessageW(child_window, WM_CLOSE, 0, 0);
        if (WaitForSingleObject(child_process.hProcess, 10'000) != WAIT_OBJECT_0)
            TerminateProcess(child_process.hProcess, ERROR_TIMEOUT);
        CloseHandle(child_process.hProcess);
        return child_window != nullptr && log_created && !enumerate_error;
    };

    const fs::path fresh_gui_log_root = contract_root / L"FreshGuiStartup" / L"Logs";
    const bool fresh_logs_gui_launch = !fs::exists(fresh_gui_log_root) && launch_with_log_root(fresh_gui_log_root);
    if (!fresh_logs_gui_launch) failures.emplace_back("GUI did not launch with an initially missing Logs directory");
    const bool existing_logs_gui_launch = launch_with_log_root(fresh_gui_log_root);
    if (!existing_logs_gui_launch) failures.emplace_back("GUI did not launch with an existing Logs directory");

    const fs::path launcher_contract_root = contract_root / L"LauncherFlow";
    const fs::path launcher_contract_path = launcher_contract_root / L"Launcher.exe";
    const fs::path game_contract_path = launcher_contract_root / L"God2_opt.exe";
    fs::path resolved_launcher;
    fs::path resolved_game;
    std::string launcher_contract_error;
    wchar_t windows_directory[MAX_PATH]{};
    const UINT windows_directory_length =
        GetWindowsDirectoryW(windows_directory, static_cast<UINT>(std::size(windows_directory)));
    const bool windows_directory_valid = windows_directory_length > 0 &&
        windows_directory_length < std::size(windows_directory);
    const fs::path known_x86_binary = windows_directory_valid ?
        fs::path(windows_directory) / L"SysWOW64" / L"notepad.exe" : fs::path{};
    std::error_code fixture_copy_error;
    WriteUtf8File(launcher_contract_path, "test launcher fixture\n");
    fs::copy_file(known_x86_binary, game_contract_path, fs::copy_options::overwrite_existing, fixture_copy_error);
    const bool launcher_flow_contract =
        windows_directory_valid && !fixture_copy_error &&
        GameProcessTracker::ResolveClientPaths(
            launcher_contract_path, &resolved_launcher, &resolved_game, &launcher_contract_error) &&
        resolved_launcher == launcher_contract_path && resolved_game == game_contract_path &&
        !GameProcessTracker::ResolveClientPaths(game_contract_path, nullptr, nullptr);
    if (!launcher_flow_contract)
        failures.emplace_back("Launcher.exe to God2_opt.exe client-entry contract failed");
    const fs::path x64_contract_root = contract_root / L"RejectX64Game";
    const fs::path x64_launcher = x64_contract_root / L"Launcher.exe";
    const fs::path x64_game = x64_contract_root / L"God2_opt.exe";
    std::error_code x64_copy_error;
    WriteUtf8File(x64_launcher, "test launcher fixture\n");
    fs::copy_file(executable, x64_game, fs::copy_options::overwrite_existing, x64_copy_error);
    const bool x86_game_contract = !x64_copy_error &&
        !GameProcessTracker::ResolveClientPaths(x64_launcher, nullptr, nullptr);
    if (!x86_game_contract) failures.emplace_back("x64 God2_opt.exe was not rejected by the 32-bit game contract");
    std::string wrong_build_version;
    std::optional<std::string> wrong_build_sha256;
    const bool exact_client_identity_negative_contract = !fixture_copy_error &&
        !MatchesExactClientFileIdentity(game_contract_path, &wrong_build_version, &wrong_build_sha256) &&
        wrong_build_sha256 && wrong_build_sha256->size() == 64 &&
        (wrong_build_version != kExactClientVersion || *wrong_build_sha256 != kExactClientSha256) &&
        !MatchesExactClientFileIdentity(x64_game);
    if (!exact_client_identity_negative_contract)
        failures.emplace_back("wrong-hash/version or x64 target was not blocked by the exact client identity gate");

    bool cold_start_second_instance = false;
    const std::wstring ready_event_name = L"Local\\God2PacketCapture.GuiSmoke.Ready." + Utf8ToWide(NewId());
    HANDLE ready_event = CreateEventW(nullptr, TRUE, FALSE, ready_event_name.c_str());
    PROCESS_INFORMATION delayed_process{};
    PROCESS_INFORMATION competing_process{};
    if (ready_event != nullptr) {
        std::wstring delayed_command = QuoteArgument(executable.wstring()) +
            L" --internal-gui-test-delay-ms 5000 --internal-gui-test-ready-event " + QuoteArgument(ready_event_name);
        std::vector<wchar_t> mutable_delayed_command(delayed_command.begin(), delayed_command.end());
        mutable_delayed_command.push_back(L'\0');
        STARTUPINFOW delayed_startup{};
        delayed_startup.cb = sizeof(delayed_startup);
        if (CreateProcessW(executable.c_str(), mutable_delayed_command.data(), nullptr, nullptr, FALSE,
                           CREATE_UNICODE_ENVIRONMENT, nullptr, executable.parent_path().c_str(),
                           &delayed_startup, &delayed_process)) {
            CloseHandle(delayed_process.hThread);
            if (WaitForSingleObject(ready_event, 10'000) == WAIT_OBJECT_0) {
                std::wstring competing_command = QuoteArgument(executable.wstring()) + L" --internal-gui-test-instance";
                std::vector<wchar_t> mutable_competing_command(competing_command.begin(), competing_command.end());
                mutable_competing_command.push_back(L'\0');
                STARTUPINFOW competing_startup{};
                competing_startup.cb = sizeof(competing_startup);
                if (CreateProcessW(executable.c_str(), mutable_competing_command.data(), nullptr, nullptr, FALSE,
                                   CREATE_UNICODE_ENVIRONMENT, nullptr, executable.parent_path().c_str(),
                                   &competing_startup, &competing_process)) {
                    CloseHandle(competing_process.hThread);
                    HWND already_running_message = nullptr;
                    for (int attempt = 0; attempt < 70 && already_running_message == nullptr; ++attempt) {
                        if (WaitForSingleObject(competing_process.hProcess, 0) == WAIT_OBJECT_0) break;
                        already_running_message = MainWindowForProcess(competing_process.dwProcessId);
                        if (already_running_message == nullptr) Sleep(100);
                    }
                    wchar_t message_class[32]{};
                    if (already_running_message != nullptr)
                        GetClassNameW(already_running_message, message_class, static_cast<int>(std::size(message_class)));
                    if (already_running_message != nullptr && wcscmp(message_class, L"#32770") == 0)
                        PostMessageW(already_running_message, WM_CLOSE, 0, 0);
                    DWORD competing_exit = STILL_ACTIVE;
                    const DWORD competing_wait = WaitForSingleObject(competing_process.hProcess, 10'000);
                    if (competing_wait == WAIT_OBJECT_0)
                        GetExitCodeProcess(competing_process.hProcess, &competing_exit);
                    cold_start_second_instance = already_running_message != nullptr &&
                        wcscmp(message_class, L"#32770") == 0 && competing_exit == ERROR_ALREADY_EXISTS;
                    if (competing_wait != WAIT_OBJECT_0)
                        TerminateProcess(competing_process.hProcess, ERROR_TIMEOUT);
                    CloseHandle(competing_process.hProcess);
                    competing_process.hProcess = nullptr;
                }
            }
            HWND delayed_window = nullptr;
            for (int attempt = 0; attempt < 120 && delayed_window == nullptr; ++attempt) {
                if (WaitForSingleObject(delayed_process.hProcess, 0) == WAIT_OBJECT_0) break;
                delayed_window = MainWindowForProcess(delayed_process.dwProcessId);
                if (delayed_window == nullptr) Sleep(100);
            }
            if (delayed_window != nullptr) PostMessageW(delayed_window, WM_CLOSE, 0, 0);
            if (WaitForSingleObject(delayed_process.hProcess, 10'000) != WAIT_OBJECT_0)
                TerminateProcess(delayed_process.hProcess, ERROR_TIMEOUT);
            CloseHandle(delayed_process.hProcess);
            delayed_process.hProcess = nullptr;
        }
        CloseHandle(ready_event);
    }
    if (!cold_start_second_instance)
        failures.emplace_back("a simultaneous cold-start second instance did not show the already-running message");

    DWORD common_error_183 = 0xA5A5A5A5;
    SetLastError(ERROR_ALREADY_EXISTS);
    const bool common_stale_183 = InitializeCommonControlsForGui(&common_error_183);
    if (!common_stale_183) failures.emplace_back("InitCommonControlsEx failed after a stale ERROR_ALREADY_EXISTS");
    DWORD common_error_5 = 0x5A5A5A5A;
    SetLastError(ERROR_ACCESS_DENIED);
    const bool common_stale_5 = InitializeCommonControlsForGui(&common_error_5);
    if (!common_stale_5) failures.emplace_back("InitCommonControlsEx failed after a stale ERROR_ACCESS_DENIED");
    const bool common_success_did_not_consume_error = common_stale_183 && common_stale_5 &&
        common_error_183 == 0xA5A5A5A5 && common_error_5 == 0x5A5A5A5A;
    if (!common_success_did_not_consume_error)
        failures.emplace_back("the successful common-controls path consumed or reported LastError");

    const std::wstring mutex_name = L"Local\\God2PacketCapture.GuiSmoke." + Utf8ToWide(NewId());
    SetLastError(ERROR_SUCCESS);
    HANDLE first_mutex = CreateMutexW(nullptr, FALSE, mutex_name.c_str());
    const DWORD first_mutex_error = GetLastError();
    SetLastError(ERROR_SUCCESS);
    HANDLE second_mutex = CreateMutexW(nullptr, FALSE, mutex_name.c_str());
    const DWORD second_mutex_error = GetLastError();
    const bool existing_mutex = first_mutex != nullptr && second_mutex != nullptr &&
        first_mutex_error == ERROR_SUCCESS && second_mutex_error == ERROR_ALREADY_EXISTS;
    if (second_mutex != nullptr) CloseHandle(second_mutex);
    if (first_mutex != nullptr) CloseHandle(first_mutex);
    if (!existing_mutex) failures.emplace_back("CreateMutexW ERROR_ALREADY_EXISTS contract was not preserved");

    DWORD capture_root_error = ERROR_SUCCESS;
    const bool capture_root_exists = EnsureDirectoryTree(LocalDataRoot() / L"Captures", &capture_root_error) &&
        EnsureDirectoryTree(LocalDataRoot() / L"Captures", &capture_root_error);
    CaptureSession first_session;
    CaptureSession second_session;
    std::string session_error;
    const bool existing_capture_root_session = capture_root_exists && first_session.Create(&session_error) &&
        second_session.Create(&session_error) && first_session.Path() != second_session.Path() &&
        fs::is_directory(first_session.Path()) && fs::is_directory(second_session.Path());
    if (!existing_capture_root_session)
        failures.emplace_back("an existing capture output directory prevented a new unique session");

    bool zero_enhanced_records_package = false;
    bool raw_backend_privacy_contract = false;
    bool raw_active_phrases_absent_when_privacy_blocked = false;
    bool three_language_privacy_help_contract = false;
    bool terminal_privacy_block_state_not_active = false;
    bool raw_payload_canary_absent_from_zip = false;
    CaptureSession zero_records_session;
    std::string zero_records_error;
    if (zero_records_session.Create(&zero_records_error)) {
        const auto zero_path = zero_records_session.Path();
        CaptureRequest privacy_request;
        privacy_request.session_path = zero_path;
        EtwNetworkTraceBackend etw_privacy_backend;
        PktMonCaptureBackend pktmon_privacy_backend;
        const auto etw_privacy_capabilities = etw_privacy_backend.Probe();
        const auto etw_privacy_start = etw_privacy_backend.Start(privacy_request);
        const auto etw_privacy_stop = etw_privacy_backend.Stop(privacy_request);
        const auto pktmon_privacy_start = pktmon_privacy_backend.Start(privacy_request);
        const auto pktmon_privacy_stop = pktmon_privacy_backend.Stop(privacy_request);
        const auto privacy_os = DetectOsVersion();
        const bool pktmon_privacy_result = BackendPolicyAllowsPktMon(privacy_os.major, privacy_os.minor) ?
            (IsRawPrivacyPolicyBlock(pktmon_privacy_start) &&
             IsRawPrivacyPolicyBlock(pktmon_privacy_stop)) :
            (!pktmon_privacy_start.success && !pktmon_privacy_stop.success &&
             pktmon_privacy_start.exit_code == ERROR_OLD_WIN_VERSION &&
             pktmon_privacy_stop.exit_code == ERROR_OLD_WIN_VERSION);
        const auto backend_policy = ReadUtf8File(
            zero_path / L"compatibility" / L"raw-network-capture-policy.json");
        raw_backend_privacy_contract = !etw_privacy_capabilities.available &&
            !etw_privacy_capabilities.supports_packet_payload &&
            !etw_privacy_capabilities.supports_realtime_events &&
            !etw_privacy_capabilities.supports_pcapng_conversion &&
            IsRawPrivacyPolicyBlock(etw_privacy_start) &&
            IsRawPrivacyPolicyBlock(etw_privacy_stop) &&
            SemanticWorkflowCanRun(BackendState::EvidenceBlocked) &&
            std::wstring(BackendStateText(BackendState::EvidenceBlocked)) ==
                L"EvidenceBlockedPrivacyPolicy" &&
            pktmon_privacy_result &&
            !fs::exists(zero_path / L"raw" / L"capture.etl") &&
            backend_policy &&
            backend_policy->find("\"Status\":\"EvidenceBlockedPrivacyPolicy\"") != std::string::npos &&
            backend_policy->find("\"Phase\":\"Active\"") == std::string::npos &&
            backend_policy->find("\"RawNetworkPayloadPersistence\":false") != std::string::npos &&
            backend_policy->find("\"PayloadBytesCaptured\":false") != std::string::npos &&
            backend_policy->find("\"NetshCaptureExecuted\":false") != std::string::npos &&
            backend_policy->find("\"PktMonCaptureExecuted\":false") != std::string::npos;
        const UiLanguage saved_language = g_ui_language.load();
        std::wstring policy_disabled_ui;
        constexpr std::array<UiString, 10> policy_status_strings = {
            UiString::WaitingLauncher, UiString::Capturing, UiString::Starting,
            UiString::Stopping, UiString::BusyStarting, UiString::BusyStopping,
            UiString::ConfirmExit, UiString::CaptureAlreadyRunning,
            UiString::StartCompleteWaiting, UiString::StartCompleteTracking
        };
        for (const UiLanguage language : {UiLanguage::TraditionalChinese,
                                          UiLanguage::SimplifiedChinese,
                                          UiLanguage::English}) {
            g_ui_language.store(language);
            for (const auto id : policy_status_strings) {
                policy_disabled_ui += Text(id);
                policy_disabled_ui.push_back(L'\n');
            }
            policy_disabled_ui += HelpContent();
            policy_disabled_ui.push_back(L'\n');
        }
        g_ui_language.store(saved_language);
        policy_disabled_ui += Utf8ToWide(etw_privacy_start.message);
        policy_disabled_ui.push_back(L'\n');
        policy_disabled_ui += Utf8ToWide(etw_privacy_stop.message);
        constexpr std::array<std::wstring_view, 9> forbidden_raw_active_phrases = {
            L"Capture has started", L"Capture active", L"A capture is active",
            L"Raw packet capture remains active", L"Raw packet capture is active",
            L"封包捕捉已開始", L"封包捕获已开始",
            L"原始封包捕捉正在進行", L"原始封包捕获正在进行"
        };
        raw_active_phrases_absent_when_privacy_blocked = std::none_of(
            forbidden_raw_active_phrases.begin(), forbidden_raw_active_phrases.end(),
            [&](std::wstring_view phrase) {
                return policy_disabled_ui.find(phrase) != std::wstring::npos;
            });
        constexpr std::array<std::wstring_view, 6> forbidden_legacy_help_phrases = {
            L"原始 ETL／PCAPNG 捕捉仍會繼續", L"原始 ETL／PCAPNG 捕获仍会继续",
            L"原始系統 ETL 會保留", L"原始系统 ETL 会保留",
            L"ETL/PCAPNG evidence is preserved", L"PCAPNG/ETL output to flush"
        };
        three_language_privacy_help_contract =
            policy_disabled_ui.find(L"系統層 raw ETW／PktMon 封包已停用，不落盤也不打包") != std::wstring::npos &&
            policy_disabled_ui.find(L"系统层 raw ETW／PktMon 封包已停用，不落盘也不打包") != std::wstring::npos &&
            policy_disabled_ui.find(L"System-wide raw ETW/PktMon packets are disabled, never persisted, and never packaged") != std::wstring::npos &&
            std::none_of(forbidden_legacy_help_phrases.begin(), forbidden_legacy_help_phrases.end(),
                [&](std::wstring_view phrase) {
                    return policy_disabled_ui.find(phrase) != std::wstring::npos;
                });
        terminal_privacy_block_state_not_active =
            SemanticWorkflowCanRun(BackendState::EvidenceBlocked) &&
            WorkflowNeedsFinalization(CaptureState::Capturing) &&
            !WorkflowNeedsFinalization(CaptureState::Completed) &&
            !WorkflowNeedsFinalization(CaptureState::Cancelled);
        const auto enhanced_status = MakeJsonObject({
            {"Requested", "true"}, {"Status", "EvidenceBlocked"},
            {"WasEverAttached", "false"}, {"ProbeReady", "false"},
            {"FailureKind", "SecuritySoftwareRemovalSuspected"},
            {"Detail", "injector removed before readiness"}
        }, {"Requested", "WasEverAttached", "ProbeReady"});
        constexpr std::string_view raw_payload_canary =
            "GOD2_AUTH_CANARY_ARBITRARY_PROCESS_20260808_DO_NOT_PERSIST";
        const bool fixture_written =
            WriteUtf8FileAtomic(zero_path / L"raw" / L"capture.etl", raw_payload_canary) &&
            WriteUtf8FileAtomic(zero_path / L"raw" / L"arbitrary-process.PCAPNG", raw_payload_canary) &&
            WriteUtf8FileAtomic(zero_path / L"raw" / L"etw-events.jsonl",
                MakeJsonObject({{"SessionId", "arbitrary-process"},
                                {"Payload", std::string(raw_payload_canary)}}) + "\n") &&
            WriteUtf8FileAtomic(zero_path / L"reports" / L"enhanced-capture-status.json",
                                enhanced_status + "\n");
        std::uint64_t removed_raw_artifacts = 0;
        const bool privacy_enforced = fixture_written && EnforceRawNetworkPrivacyPolicy(
            zero_path, &zero_records_error, &removed_raw_artifacts);
        const auto zero_package = privacy_enforced ? EvidencePackageBuilder::Build(zero_path) :
            EvidencePackageResult{};
        const bool zero_records_warning = std::any_of(
            zero_package.warnings.begin(), zero_package.warnings.end(), [](const auto& warning) {
                return warning.find("No x86 enhanced CaptureRecords were observed") != std::string::npos;
            });
        const bool zero_records_blocker = std::any_of(
            zero_package.blockers.begin(), zero_package.blockers.end(), [](const auto& blocker) {
                return blocker.find(
                    "x86 enhanced capture was requested but no verified DLL attachment records were acquired") !=
                    std::string::npos;
            });
        const auto privacy_report = ReadUtf8File(
            zero_path / L"reports" / L"raw-network-privacy-policy.json");
        const auto zip_canary = zero_package.success ?
            EvidenceZipContainsText(zero_package.zip_path, raw_payload_canary) : std::optional<bool>{};
        raw_payload_canary_absent_from_zip = privacy_enforced && removed_raw_artifacts == 3 &&
            !fs::exists(zero_path / L"raw" / L"capture.etl") &&
            !fs::exists(zero_path / L"raw" / L"arbitrary-process.PCAPNG") &&
            !fs::exists(zero_path / L"raw" / L"etw-events.jsonl") &&
            privacy_report &&
            privacy_report->find("\"PayloadBytesCapturedBySystemBackend\":false") != std::string::npos &&
            privacy_report->find("\"RemovedForbiddenArtifactCount\":3") != std::string::npos &&
            zip_canary && !*zip_canary;
        zero_enhanced_records_package = zero_package.success && zero_package.capture_records == 0 &&
            zero_package.acquisition_status == "EvidenceBlocked" &&
            zero_package.analysis_status == "EvidenceBlocked" &&
            zero_package.package_status == "EvidenceBlocked" &&
            zero_records_warning && zero_records_blocker && raw_payload_canary_absent_from_zip;
        if (!zero_enhanced_records_package && zero_records_error.empty()) {
            zero_records_error = zero_package.error.empty() ?
                "success=" + std::string(zero_package.success ? "true" : "false") +
                ",captureRecords=" + std::to_string(zero_package.capture_records) +
                ",acquisitionStatus=" + zero_package.acquisition_status +
                ",analysisStatus=" + zero_package.analysis_status +
                ",packageStatus=" + zero_package.package_status +
                ",warning=" + std::string(zero_records_warning ? "true" : "false") +
                ",blocker=" + std::string(zero_records_blocker ? "true" : "false") +
                ",privacy=" + std::string(raw_payload_canary_absent_from_zip ? "true" : "false") :
                zero_package.error;
        }
    }
    if (!raw_backend_privacy_contract)
        failures.emplace_back("system-wide raw backends did not expose EvidenceBlockedPrivacyPolicy without Active/success or ETL/PCAPNG payload artifacts");
    if (!raw_active_phrases_absent_when_privacy_blocked)
        failures.emplace_back("privacy-disabled GUI/backend status still claimed that raw packet capture started or was active");
    if (!three_language_privacy_help_contract)
        failures.emplace_back("one or more localized help texts still promised retained/flushed raw ETL or omitted the privacy-block statement");
    if (!terminal_privacy_block_state_not_active)
        failures.emplace_back("a terminal workflow with a privacy-blocked raw backend was still reported active");
    if (!raw_payload_canary_absent_from_zip)
        failures.emplace_back("arbitrary-process raw authentication CANARY reached disk after enforcement or the Evidence ZIP");
    if (!zero_enhanced_records_package)
        failures.emplace_back("zero x86 CaptureRecords were not blocked under the raw-payload privacy policy: " +
                              zero_records_error);

    bool near_realtime_analysis = false;
    bool near_realtime_failure_fallback = false;
    bool target_pid_analysis_filter = false;
    bool post_capture_pid_history_analysis = false;
    bool target_pid_history_report = false;
    bool etw_scope_privacy_decision_contract = false;
    bool raw_etl_evidence_blocked_report = false;
    bool injected_transport_evidence_report = false;
    bool injected_transport_unknown_summary = false;
    bool god2_frame_candidate_report = false;
    bool god2_opcode_semantic_report = false;
    bool candidate_gameplay_coverage_report = false;
    bool candidate_summary_metrics = false;
    {
        const auto live_session = LocalDataRoot() / L"Captures" /
            (L"gui-live-smoke-" + Utf8ToWide(NewId()));
        AnalysisController live_controller;
        BackendCapabilities live_capabilities;
        live_capabilities.analysis_mode = AnalysisMode::NearRealtime;
        std::string live_error;
        if (live_controller.Start(live_session, live_capabilities, &live_error)) {
            live_controller.SetTargetProcess(GetCurrentProcessId(), true);
            const auto event = MakeJsonObject({
                {"SourceFrameId", "gui-live-movement"}, {"ObservedAtUtc", UtcNow()},
                {"PacketDirection", "ServerToClient"}, {"MessageType", "MovementUpdate"},
                {"ProcessId", std::to_string(GetCurrentProcessId())},
                {"CharacterId", "gui-live-character"}, {"StartX", "1"}, {"StartY", "1"},
                {"EndX", "2"}, {"EndY", "1"}, {"EvidenceLevel", "Transmitted"}
            });
            const auto unrelated_event = MakeJsonObject({
                {"SourceFrameId", "gui-unrelated-movement"}, {"ObservedAtUtc", UtcNow()},
                {"PacketDirection", "ServerToClient"}, {"MessageType", "MovementUpdate"},
                {"ProcessId", std::to_string(GetCurrentProcessId() + 1)},
                {"CharacterId", "not-god2"}, {"StartX", "1"}, {"StartY", "1"},
                {"EndX", "9"}, {"EndY", "9"}, {"EvidenceLevel", "Transmitted"}
            });
            const bool fixture_written = WriteUtf8FileAtomic(
                live_session / L"raw" / L"etw-events.jsonl", event + "\n" + unrelated_event + "\n");
            Sleep(300);
            const bool finalized = live_controller.Finalize(live_session, false, &live_error);
            const auto summary = ReadUtf8File(live_session / L"session-summary.json");
            near_realtime_analysis = fixture_written && finalized &&
                CountLines(live_session / L"gameplay" / L"movement.jsonl") == 1 && summary &&
                summary->find("\"AnalysisMode\":\"NearRealtime\"") != std::string::npos;
            target_pid_analysis_filter = near_realtime_analysis;
        }
        if (!near_realtime_analysis) failures.emplace_back(
            "near-realtime analysis did not incrementally classify and flush an ETW event");
        if (!target_pid_analysis_filter) failures.emplace_back(
            "an event attributed to a non-God2 PID entered gameplay analysis");
    }
    {
        const auto recovery_session = LocalDataRoot() / L"Captures" /
            (L"gui-live-recovery-smoke-" + Utf8ToWide(NewId()));
        AnalysisController recovery_controller;
        BackendCapabilities recovery_capabilities;
        recovery_capabilities.analysis_mode = AnalysisMode::NearRealtime;
        std::string recovery_error;
        if (recovery_controller.Start(recovery_session, recovery_capabilities, &recovery_error)) {
            recovery_controller.SetTargetProcess(GetCurrentProcessId(), true);
            const auto event = MakeJsonObject({
                {"SourceFrameId", "gui-live-recovery-movement"}, {"ObservedAtUtc", UtcNow()},
                {"PacketDirection", "ServerToClient"}, {"MessageType", "MovementUpdate"},
                {"ProcessId", std::to_string(GetCurrentProcessId())},
                {"CharacterId", "gui-live-recovery-character"}, {"StartX", "2"}, {"StartY", "2"},
                {"EndX", "3"}, {"EndY", "2"}, {"EvidenceLevel", "Transmitted"}
            }) + "\n";
            bool locked_fixture_written = false;
            bool live_failure_observed = false;
            {
                UniqueHandle locked_writer(CreateFileW(
                    Win32ExtendedPathForFileIo(recovery_session / L"raw" / L"injected-packets.jsonl").c_str(),
                    GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr));
                if (locked_writer.Get() != INVALID_HANDLE_VALUE) {
                    DWORD written = 0;
                    locked_fixture_written = WriteFile(locked_writer.Get(), event.data(),
                        static_cast<DWORD>(event.size()), &written, nullptr) != FALSE && written == event.size() &&
                        FlushFileBuffers(locked_writer.Get()) != FALSE;
                    // Give the live reader enough time to exhaust its bounded sharing retry.
                    Sleep(400);
                    live_failure_observed = recovery_controller.NearRealtimeFailed();
                }
            }
            const bool finalized = recovery_controller.Finalize(recovery_session, false, &recovery_error);
            const auto summary = ReadUtf8File(recovery_session / L"session-summary.json");
            near_realtime_failure_fallback = locked_fixture_written && live_failure_observed && finalized && summary &&
                summary->find("\"AnalysisMode\":\"PostCapture\"") != std::string::npos &&
                CountLines(recovery_session / L"gameplay" / L"movement.jsonl") == 1;
        }
        if (!near_realtime_failure_fallback) failures.emplace_back(
            "a near-realtime sharing failure did not recover through automatic post-capture analysis: " +
            recovery_error);
    }
    {
        const auto fallback_session = LocalDataRoot() / L"Captures" /
            (L"gui-post-capture-smoke-" + Utf8ToWide(NewId()));
        AnalysisController fallback_controller;
        BackendCapabilities fallback_capabilities;
        fallback_capabilities.analysis_mode = AnalysisMode::PostCapture;
        std::string fallback_error;
        SessionStore fallback_store(fallback_session);
        if (fallback_store.Initialize(&fallback_error) &&
            fallback_controller.Start(fallback_session, fallback_capabilities, &fallback_error)) {
            fallback_controller.SetTargetProcess(GetCurrentProcessId(), true);
            // Simulate the game exiting before Stop.  Offline analysis must retain
            // the verified PID history rather than reverting to an unscoped scan.
            fallback_controller.SetTargetProcess(0, false);
            const auto scope_report = ReadUtf8File(fallback_session / L"reports" / L"target-process-scope.json");
            target_pid_history_report = scope_report &&
                scope_report->find("\"TargetProcessId\":" + std::to_string(GetCurrentProcessId())) != std::string::npos &&
                scope_report->find("\"CurrentProcessId\":0") != std::string::npos &&
                scope_report->find("\"TargetProcessStatus\":\"VerifiedHistoryRetained\"") != std::string::npos;
            const auto event = MakeJsonObject({
                {"SourceFrameId", "gui-fallback-movement"}, {"ObservedAtUtc", UtcNow()},
                {"PacketDirection", "ServerToClient"}, {"MessageType", "MovementUpdate"},
                {"ProcessId", std::to_string(GetCurrentProcessId())},
                {"CharacterId", "gui-fallback-character"}, {"StartX", "3"}, {"StartY", "4"},
                {"EndX", "4"}, {"EndY", "4"}, {"EvidenceLevel", "Transmitted"}
            });
            const auto unrelated_event = MakeJsonObject({
                {"SourceFrameId", "gui-fallback-unrelated"}, {"ObservedAtUtc", UtcNow()},
                {"PacketDirection", "ServerToClient"}, {"MessageType", "MovementUpdate"},
                {"ProcessId", std::to_string(GetCurrentProcessId() + 1)},
                {"CharacterId", "not-god2-fallback"}, {"StartX", "1"}, {"StartY", "1"},
                {"EndX", "8"}, {"EndY", "8"}, {"EvidenceLevel", "Transmitted"}
            });
            const bool fixture_written = WriteUtf8FileAtomic(
                fallback_session / L"raw" / L"etw-events.jsonl", event + "\n" + unrelated_event + "\n");
            const bool finalized = fixture_written &&
                fallback_controller.Finalize(fallback_session, true, &fallback_error);
            const auto summary = ReadUtf8File(fallback_session / L"session-summary.json");
            post_capture_pid_history_analysis = finalized &&
                CountLines(fallback_session / L"gameplay" / L"movement.jsonl") == 1 && summary &&
                summary->find("\"AnalysisMode\":\"PostCapture\"") != std::string::npos;
        }
        if (!post_capture_pid_history_analysis) failures.emplace_back(
            "PostCaptureFallback did not analyze only the retained verified God2 PID history");
        if (!target_pid_history_report) failures.emplace_back(
            "target-process-scope report overwrote verified God2 PID history after process exit");
    }
    {
        const auto privacy_scope_session = LocalDataRoot() / L"Captures" /
            (L"gui-etw-privacy-scope-smoke-" + Utf8ToWide(NewId()));
        AnalysisController privacy_scope_controller;
        BackendCapabilities privacy_scope_capabilities;
        privacy_scope_capabilities.analysis_mode = AnalysisMode::PostCapture;
        std::string privacy_scope_error;
        SessionStore privacy_scope_store(privacy_scope_session);
        if (privacy_scope_store.Initialize(&privacy_scope_error) &&
            privacy_scope_controller.Start(privacy_scope_session, privacy_scope_capabilities,
                                           &privacy_scope_error)) {
            const bool fixture_written = WriteUtf8FileAtomic(
                privacy_scope_session / L"raw" / L"etw-events.jsonl", "{}\n");
            const bool finalized = fixture_written && privacy_scope_controller.Finalize(
                privacy_scope_session, true, &privacy_scope_error);
            const auto scope_report = ReadUtf8File(
                privacy_scope_session / L"reports" / L"etw-process-scope-evidence-blocked.json");
            const std::string legacy_decision =
                "\"Decision\":\"ETW events were re" "tained but excluded from gameplay analysis\"";
            const bool privacy_scrubbed = finalized && EnforceRawNetworkPrivacyPolicy(
                privacy_scope_session, &privacy_scope_error);
            etw_scope_privacy_decision_contract = scope_report &&
                scope_report->find("\"Decision\":\"Raw ETW artifacts are excluded and removed by privacy policy; gameplay analysis uses sanitized semantic evidence only\"") != std::string::npos &&
                scope_report->find(legacy_decision) == std::string::npos &&
                privacy_scrubbed &&
                !fs::exists(privacy_scope_session / L"raw" / L"etw-events.jsonl");
        }
        if (!etw_scope_privacy_decision_contract) failures.emplace_back(
            "ETW process-scope report did not state that privacy policy removes raw artifacts, or the raw JSONL survived enforcement");
    }
    {
        const auto etl_blocked_session = LocalDataRoot() / L"Captures" /
            (L"gui-etl-blocked-smoke-" + Utf8ToWide(NewId()));
        AnalysisController etl_blocked_controller;
        BackendCapabilities etl_blocked_capabilities;
        etl_blocked_capabilities.analysis_mode = AnalysisMode::PostCapture;
        std::string etl_blocked_error;
        SessionStore etl_blocked_store(etl_blocked_session);
        if (etl_blocked_store.Initialize(&etl_blocked_error) &&
            etl_blocked_controller.Start(etl_blocked_session, etl_blocked_capabilities, &etl_blocked_error)) {
            etl_blocked_controller.SetTargetProcess(GetCurrentProcessId(), true);
            const bool etw_empty = WriteUtf8FileAtomic(etl_blocked_session / L"raw" / L"etw-events.jsonl", "");
            const bool etl_retained = WriteUtf8FileAtomic(etl_blocked_session / L"raw" / L"capture.etl",
                                                          "untrusted raw artifact");
            const bool finalized = etw_empty && etl_retained &&
                etl_blocked_controller.Finalize(etl_blocked_session, true, &etl_blocked_error);
            const auto blocked_report = ReadUtf8File(
                etl_blocked_session / L"reports" / L"raw-etl-evidence-blocked.json");
            const auto inventory_report = ReadUtf8File(
                etl_blocked_session / L"reports" / L"raw-etl-inventory.json");
            const auto summary = ReadUtf8File(etl_blocked_session / L"session-summary.json");
            const bool privacy_scrubbed = EnforceRawNetworkPrivacyPolicy(
                etl_blocked_session, &etl_blocked_error);
            raw_etl_evidence_blocked_report = finalized && blocked_report && inventory_report && summary &&
                blocked_report->find("\"Status\":\"EvidenceBlocked\"") != std::string::npos &&
                blocked_report->find("\"RawEtlBytes\":22") != std::string::npos &&
                blocked_report->find("\"EtwEventLines\":0") != std::string::npos &&
                inventory_report->find("\"Status\":\"Failed\"") != std::string::npos &&
                summary->find("\"Status\":\"CompletedWithWarnings\"") != std::string::npos &&
                privacy_scrubbed && !fs::exists(etl_blocked_session / L"raw" / L"capture.etl") &&
                !fs::exists(etl_blocked_session / L"raw" / L"etw-events.jsonl");
        }
        if (!raw_etl_evidence_blocked_report) failures.emplace_back(
            "diagnostic ETL was not classified EvidenceBlocked and removed before packaging");
    }
    {
        const auto injected_session = LocalDataRoot() / L"Captures" /
            (L"gui-injected-transport-smoke-" + Utf8ToWide(NewId()));
        AnalysisController injected_controller;
        BackendCapabilities injected_capabilities;
        injected_capabilities.analysis_mode = AnalysisMode::PostCapture;
        std::string injected_error;
        SessionStore injected_store(injected_session);
        if (injected_store.Initialize(&injected_error) &&
            injected_controller.Start(injected_session, injected_capabilities, &injected_error)) {
            injected_controller.SetTargetProcess(GetCurrentProcessId(), true);
            const auto injected_event = MakeJsonObject({
                {"SourceFrameId", "gui-injected-1"}, {"ObservedAtUtc", UtcNow()},
                {"PacketDirection", "ClientToServer"}, {"Transport", "InjectedWinsock"},
                {"CaptureSource", "OptInX86Dll"}, {"EvidenceLevel", "Transmitted"},
                {"ProcessId", std::to_string(GetCurrentProcessId())}, {"Api", "send"},
                {"PayloadHex", "0500011000"}, {"CapturedLength", "5"},
                {"TransferredLength", "5"}, {"frame208", "false"}
            });
            const bool fixture_written = WriteUtf8FileAtomic(
                injected_session / L"raw" / L"injected-packets.jsonl", injected_event + "\n");
            const bool finalized = fixture_written &&
                injected_controller.Finalize(injected_session, true, &injected_error);
            const auto transport_report = ReadUtf8File(
                injected_session / L"reports" / L"injected-transport-evidence.json");
            const auto summary = ReadUtf8File(injected_session / L"session-summary.json");
            injected_transport_evidence_report = finalized && transport_report &&
                transport_report->find("\"Status\":\"Observed\"") != std::string::npos &&
                transport_report->find("\"Records\":1") != std::string::npos &&
                transport_report->find("\"ClientToServer\":1") != std::string::npos;
            injected_transport_unknown_summary = finalized && summary &&
                summary->find("\"CaptureRecords\":1") != std::string::npos &&
                summary->find("\"ProtocolFrames\":0") != std::string::npos &&
                summary->find("\"Unknown\":1") != std::string::npos &&
                CountLines(injected_session / L"raw" / L"unknown-frames.jsonl") == 1;
            const auto candidate_report = ReadUtf8File(
                injected_session / L"reports" / L"god2-frame-candidates.json");
            god2_frame_candidate_report = finalized && candidate_report &&
                candidate_report->find("\"Status\":\"Candidate\"") != std::string::npos &&
                candidate_report->find("\"CandidateFrames\":1") != std::string::npos &&
                CountLines(injected_session / L"raw" / L"god2-frame-candidates.jsonl") == 1;
            const auto semantic_report = ReadUtf8File(
                injected_session / L"reports" / L"god2-opcode-semantic-candidates.json");
            god2_opcode_semantic_report = finalized && semantic_report &&
                semantic_report->find("\"Status\":\"EvidenceBlocked\"") != std::string::npos &&
                semantic_report->find("\"SemanticCandidateCount\":0") != std::string::npos &&
                semantic_report->find("No opcode or gameplay semantic candidate is emitted") != std::string::npos &&
                CountLines(injected_session / L"raw" / L"god2-opcode-semantic-candidates.jsonl") == 0;
            const auto candidate_gameplay_report = ReadUtf8File(
                injected_session / L"reports" / L"gameplay-semantic-candidate-coverage.json");
            const auto candidate_gameplay_csv = ReadUtf8File(
                injected_session / L"exports" / L"gameplay-semantic-candidate-coverage.csv");
            candidate_gameplay_coverage_report = finalized && candidate_gameplay_report &&
                candidate_gameplay_report->find("\"Status\":\"EvidenceBlocked\"") != std::string::npos &&
                candidate_gameplay_report->find("\"CandidateGameplayGroups\":{}") != std::string::npos &&
                candidate_gameplay_csv && candidate_gameplay_csv->find("UnknownProtocolCandidate") == std::string::npos &&
                CountLines(injected_session / L"gameplay" / L"protocol-semantic-candidates.jsonl") == 0;
            const auto candidate_metrics = ReadCandidateClassificationMetrics(injected_session);
            CaptureSnapshot metric_snapshot;
            metric_snapshot.classified_count = 0;
            metric_snapshot.unknown_count = 1;
            metric_snapshot.candidate_frame_count = candidate_metrics.candidate_frames;
            metric_snapshot.semantic_candidate_count = candidate_metrics.semantic_candidates;
            metric_snapshot.high_confidence_semantic_candidate_count =
                candidate_metrics.high_confidence_semantic_candidates;
            candidate_summary_metrics = finalized && summary &&
                summary->find("\"CandidateFrames\":1") != std::string::npos &&
                summary->find("\"OpcodeSemanticCandidates\":0") != std::string::npos &&
                summary->find("\"ClassificationStatus\":\"UnknownOnly\"") != std::string::npos &&
                FormatClassifiedCount(metric_snapshot) == L"0" &&
                FormatUnknownCount(metric_snapshot).find(L"Frame") != std::wstring::npos;
        }
        if (!injected_transport_evidence_report) failures.emplace_back(
            "x86 injected Winsock evidence did not produce a transport evidence report");
        if (!injected_transport_unknown_summary) failures.emplace_back(
            "x86 injected Winsock evidence was not counted in frames and unknown clusters");
        if (!god2_frame_candidate_report) failures.emplace_back(
            "x86 injected Winsock evidence did not produce God2 frame candidates");
        if (!god2_opcode_semantic_report) failures.emplace_back(
            "raw transport incorrectly produced opcode semantic candidates");
        if (!candidate_gameplay_coverage_report) failures.emplace_back(
            "raw transport incorrectly produced candidate gameplay coverage");
        if (!candidate_summary_metrics) failures.emplace_back(
            "raw transport candidate was not kept separate from verified classification metrics");
    }
    std::wstring command = QuoteArgument(executable.wstring()) + L" --internal-gui-test-instance";
    std::vector<wchar_t> mutable_command(command.begin(), command.end());
    mutable_command.push_back(L'\0');
    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};
    const BOOL child_created = CreateProcessW(executable.c_str(), mutable_command.data(), nullptr, nullptr, FALSE,
                                              CREATE_UNICODE_ENVIRONMENT, nullptr,
                                              executable.parent_path().c_str(), &startup, &process);
    const DWORD child_create_error = child_created ? ERROR_SUCCESS : GetLastError();
    if (!child_created) {
        failures.emplace_back("no-argument GUI process could not start");
        AppendStartupLog(L"GuiSmokeChildCreateError", std::to_wstring(child_create_error));
    }
    HWND window = nullptr;
    if (process.hProcess != nullptr) {
        CloseHandle(process.hThread);
        for (int attempt = 0; attempt < 100 && window == nullptr; ++attempt) {
            if (WaitForSingleObject(process.hProcess, 0) == WAIT_OBJECT_0) break;
            window = MainWindowForProcess(process.dwProcessId);
            if (window == nullptr) Sleep(100);
        }
        if (window == nullptr) failures.emplace_back("main window was not found within 10 seconds");
    }
    HWND start_button = window != nullptr ? GetDlgItem(window, IDC_START_CAPTURE) : nullptr;
    HWND stop_button = window != nullptr ? GetDlgItem(window, IDC_STOP_CAPTURE) : nullptr;
    if (start_button == nullptr) failures.emplace_back("start capture button was not found");
    if (stop_button == nullptr) failures.emplace_back("stop capture button was not found");
    if (start_button != nullptr && !IsWindowEnabled(start_button)) failures.emplace_back("start capture button is initially disabled");
    if (stop_button != nullptr && IsWindowEnabled(stop_button)) failures.emplace_back("stop capture button is initially enabled");
    bool capture_state_ui_contract = window != nullptr && start_button != nullptr && stop_button != nullptr;
    if (capture_state_ui_contract) {
        struct StateExpectation { CaptureState state; bool start_enabled; bool stop_enabled; };
        const StateExpectation expectations[] = {
            {CaptureState::Idle, true, false},
            {CaptureState::Preparing, false, true}, {CaptureState::WaitingForUac, false, true},
            {CaptureState::StartingElevatedWorker, false, true}, {CaptureState::StartingBackend, false, true},
            {CaptureState::StartingConsumer, false, true}, {CaptureState::WaitingForLauncher, false, true},
            {CaptureState::WaitingForGod2Process, false, true}, {CaptureState::ValidatingGod2Process, false, true},
            {CaptureState::StartingAnalysis, false, true}, {CaptureState::AttachingEnhancedCapture, false, true},
            {CaptureState::Capturing, false, true}, {CaptureState::PostCaptureFallback, false, true},
            {CaptureState::CleanupRequired, false, true}, {CaptureState::Cancelling, false, false},
            {CaptureState::Stopping, false, false}, {CaptureState::Completed, true, false},
            {CaptureState::Cancelled, true, false}, {CaptureState::Failed, true, false}
        };
        for (const auto& expectation : expectations) {
            ApplyCaptureStateToUi(window, expectation.state);
            if ((IsWindowEnabled(start_button) != FALSE) != expectation.start_enabled ||
                (IsWindowEnabled(stop_button) != FALSE) != expectation.stop_enabled) {
                capture_state_ui_contract = false;
                break;
            }
        }
        wchar_t stop_label[128]{};
        ApplyCaptureStateToUi(window, CaptureState::Preparing);
        GetWindowTextW(stop_button, stop_label, static_cast<int>(std::size(stop_label)));
        capture_state_ui_contract = capture_state_ui_contract &&
            wcscmp(stop_label, Text(UiString::CancelStart)) == 0;
        ApplyCaptureStateToUi(window, CaptureState::Capturing);
        GetWindowTextW(stop_button, stop_label, static_cast<int>(std::size(stop_label)));
        capture_state_ui_contract = capture_state_ui_contract && wcscmp(stop_label, Text(UiString::Stop)) == 0;
        ApplyCaptureStateToUi(window, CaptureState::Idle);
    }
    if (!capture_state_ui_contract) failures.emplace_back("CaptureState did not produce the required unified button states");

    const bool cancellable_state_contract =
        CanRequestStop(CaptureState::Preparing) && CanRequestStop(CaptureState::WaitingForUac) &&
        CanRequestStop(CaptureState::StartingElevatedWorker) && CanRequestStop(CaptureState::StartingConsumer) &&
        CanRequestStop(CaptureState::WaitingForGod2Process) && CanRequestStop(CaptureState::Capturing) &&
        CanRequestStop(CaptureState::PostCaptureFallback) && CanRequestStop(CaptureState::CleanupRequired) &&
        !CanRequestStop(CaptureState::Stopping) && !CanRequestStop(CaptureState::Completed);
    if (!cancellable_state_contract) failures.emplace_back("startup or capture cancellation state contract failed");
    const bool post_capture_fallback_contract =
        CanRequestStop(CaptureState::PostCaptureFallback) &&
        !IsTerminalCaptureState(CaptureState::PostCaptureFallback) &&
        IsTerminalCaptureState(CaptureState::Completed) &&
        IsTerminalCaptureState(CaptureState::Cancelled) &&
        IsTerminalCaptureState(CaptureState::Failed) &&
        post_capture_pid_history_analysis;
    if (!post_capture_fallback_contract)
        failures.emplace_back("PostCaptureFallback did not preserve the stoppable capture lifecycle");

    bool rapid_start_stop = false;
    SetEnvironmentVariableW(L"GOD2_PACKET_CAPTURE_INTERNAL_START_DELAY_MS", L"1000");
    {
        auto controller = std::make_shared<CaptureController>();
        UniqueHandle start_callback(CreateEventW(nullptr, TRUE, FALSE, nullptr));
        UniqueHandle stop_callback(CreateEventW(nullptr, TRUE, FALSE, nullptr));
        controller->StartAsync({}, false, AccelerationMode::Auto,
            [event = start_callback.Get()](bool, std::wstring) { SetEvent(event); });
        const auto preparing = controller->Snapshot();
        controller->StopAsync([event = stop_callback.Get()](bool, std::wstring) { SetEvent(event); });
        const bool start_returned = WaitForSingleObject(start_callback.Get(), 5'000) == WAIT_OBJECT_0;
        const bool stop_returned = WaitForSingleObject(stop_callback.Get(), 5'000) == WAIT_OBJECT_0;
        rapid_start_stop = preparing.state == CaptureState::Preparing && start_returned && stop_returned &&
            controller->Snapshot().state == CaptureState::Cancelled;
    }
    SetEnvironmentVariableW(L"GOD2_PACKET_CAPTURE_INTERNAL_START_DELAY_MS", nullptr);
    if (!rapid_start_stop) failures.emplace_back("rapid Start/Stop did not cancel Preparing without deadlock");

    const auto process_test_language = g_ui_language.load();
    g_ui_language.store(UiLanguage::English);
    const std::wstring access_denied_text = GameProcessStateText(GameProcessState::AccessDenied);
    const std::wstring window_detected_text = GameProcessStateText(GameProcessState::WindowDetected);
    const std::wstring not_probed_text = GameProcessStateText(GameProcessState::NotProbed);
    const std::wstring searching_text = GameProcessStateText(GameProcessState::Searching);
    g_ui_language.store(process_test_language);
    const bool process_detection_state_contract =
        access_denied_text != not_probed_text && access_denied_text != searching_text &&
        window_detected_text != not_probed_text && window_detected_text != searching_text;
    if (!process_detection_state_contract)
        failures.emplace_back("AccessDenied or window-detected game process state was mapped to not found");
    const bool etw_ready_contract = !IsEtwConsumerStartupExitContractValid(false, ERROR_SUCCESS) &&
        IsEtwConsumerStartupExitContractValid(false, ERROR_NOT_READY) &&
        IsEtwConsumerStartupExitContractValid(true, ERROR_SUCCESS) &&
        IsRecoverableEtwOpenTraceError(ERROR_WMI_INSTANCE_NOT_FOUND) &&
        IsRecoverableEtwOpenTraceError(ERROR_FILE_NOT_FOUND) &&
        IsRecoverableEtwOpenTraceError(ERROR_NOT_READY) &&
        !IsRecoverableEtwOpenTraceError(ERROR_ACCESS_DENIED) &&
        EtwConsumerProcessTraceExitCode(false, ERROR_SUCCESS) != ERROR_SUCCESS &&
        EtwConsumerProcessTraceExitCode(false, ERROR_WMI_INSTANCE_NOT_FOUND) != ERROR_SUCCESS &&
        EtwConsumerProcessTraceExitCode(true, ERROR_WMI_INSTANCE_NOT_FOUND) == ERROR_SUCCESS;
    if (!etw_ready_contract) failures.emplace_back("ETW consumer READY, early-exit, or retry contract failed");

    const bool subsystem_state_controls = window != nullptr &&
        GetDlgItem(window, IDC_BACKEND_TEXT) != nullptr && GetDlgItem(window, IDC_CONSUMER_TEXT) != nullptr &&
        GetDlgItem(window, IDC_GAME_PROCESS) != nullptr && GetDlgItem(window, IDC_ANALYSIS_STATE_TEXT) != nullptr &&
        GetDlgItem(window, IDC_GPU_MODE) != nullptr && GetDlgItem(window, IDC_GPU_STATUS) != nullptr &&
        GetDlgItem(window, IDC_AI_STATUS) != nullptr && GetDlgItem(window, IDC_COVERAGE_STATUS) != nullptr &&
        SendMessageW(GetDlgItem(window, IDC_GPU_MODE), CB_GETCOUNT, 0, 0) == 2;
    if (!subsystem_state_controls)
        failures.emplace_back("probe, bounded pipeline, process, recovery, GPU, AI/ML, and coverage states are not separated");

    const auto previous_language = g_ui_language.load();
    g_ui_language.store(UiLanguage::TraditionalChinese);
    const auto help_traditional = HelpContent();
    const std::wstring dll_tip_traditional = Text(UiString::EnhancedCaptureTip);
    g_ui_language.store(UiLanguage::SimplifiedChinese);
    const auto help_simplified = HelpContent();
    const std::wstring dll_tip_simplified = Text(UiString::EnhancedCaptureTip);
    g_ui_language.store(UiLanguage::English);
    const auto help_english = HelpContent();
    const std::wstring dll_tip_english = Text(UiString::EnhancedCaptureTip);
    g_ui_language.store(previous_language);
    const bool login_help_correct =
        help_traditional.find(L"Launcher.exe 啟動 God2_opt.exe 後，請在 God2_opt.exe 的遊戲登入畫面輸入帳號密碼") != std::wstring::npos &&
        help_simplified.find(L"Launcher.exe 启动 God2_opt.exe 后，请在 God2_opt.exe 的游戏登录界面输入账号密码") != std::wstring::npos &&
        help_english.find(L"After Launcher.exe starts God2_opt.exe, enter the account credentials in the God2_opt.exe game login screen") != std::wstring::npos &&
        help_traditional.find(L"在 Launcher.exe 登入帳號") == std::wstring::npos &&
        help_simplified.find(L"在 Launcher.exe 登录账号") == std::wstring::npos &&
        help_english.find(L"Sign in with Launcher.exe") == std::wstring::npos;
    if (!login_help_correct) failures.emplace_back("Traditional Chinese, Simplified Chinese, or English login help is incorrect");
    const bool dll_help_contract =
        help_traditional.find(L"「DLL 增強擷取」狀態列") != std::wstring::npos &&
        help_traditional.find(L"全部 25 個 Probe Domain") != std::wstring::npos &&
        help_traditional.find(L"十項 promotion gate 與四項 ABI 安全閘門") != std::wstring::npos &&
        help_simplified.find(L"“DLL 增强捕获”状态栏") != std::wstring::npos &&
        help_simplified.find(L"全部 25 个 Probe Domain") != std::wstring::npos &&
        help_simplified.find(L"十项 promotion gate 与四项 ABI 安全闸门") != std::wstring::npos &&
        help_english.find(L"permanent DLL enhanced capture row") != std::wstring::npos &&
        help_english.find(L"all 25 Probe Domains") != std::wstring::npos &&
        help_english.find(L"ten promotion gates, and four ABI-safety gates") != std::wstring::npos;
    if (!dll_help_contract)
        failures.emplace_back("the three-language help does not explain the visible auto-enabled 25-domain DLL contract");
    const bool dll_strict_unload_tip_contract =
        dll_tip_traditional.find(L"必須排空並證明 DLL 已卸載") != std::wstring::npos &&
        dll_tip_traditional.find(L"絕不誤報停止成功") != std::wstring::npos &&
        dll_tip_simplified.find(L"必须排空并证明 DLL 已卸载") != std::wstring::npos &&
        dll_tip_simplified.find(L"绝不误报停止成功") != std::wstring::npos &&
        dll_tip_english.find(L"draining and proving that the DLL is absent") != std::wstring::npos &&
        dll_tip_english.find(L"without falsely reporting success") != std::wstring::npos &&
        dll_tip_traditional.find(L"留到遊戲結束") == std::wstring::npos &&
        dll_tip_simplified.find(L"保留到游戏结束") == std::wstring::npos &&
        dll_tip_english.find(L"remains inactive until game exit") == std::wstring::npos;
    if (!dll_strict_unload_tip_contract)
        failures.emplace_back("the three-language DLL tooltip does not require a proven unload before stop success");
    RECT modern_bounds{};
    if (window != nullptr) GetWindowRect(window, &modern_bounds);
    const auto child_bounds = [&](int control_id) {
        RECT bounds{};
        const HWND control = window != nullptr ? GetDlgItem(window, control_id) : nullptr;
        if (control != nullptr && GetWindowRect(control, &bounds))
            MapWindowPoints(HWND_DESKTOP, window, reinterpret_cast<POINT*>(&bounds), 2);
        return bounds;
    };
    const auto aligned_top = [&](int left_id, int right_id) {
        const RECT left = child_bounds(left_id);
        const RECT right = child_bounds(right_id);
        return left.right > left.left && right.right > right.left && std::abs(left.top - right.top) <= 2;
    };
    const RECT os_label_bounds = child_bounds(IDC_OS_LABEL);
    const RECT admin_label_bounds = child_bounds(IDC_ADMIN_LABEL);
    const RECT consumer_label_bounds = child_bounds(IDC_CONSUMER_LABEL);
    const RECT process_label_bounds = child_bounds(IDC_PROCESS_LABEL);
    const RECT analysis_label_bounds = child_bounds(IDC_ANALYSIS_STATE_LABEL);
    const RECT status_label_bounds = child_bounds(IDC_STATUS_LABEL);
    const RECT scope_label_bounds = child_bounds(IDC_SCOPE_LABEL);
    RECT actual_client{};
    if (window != nullptr) GetClientRect(window, &actual_client);
    const auto fully_reachable = [&](int control_id) {
        const RECT bounds = child_bounds(control_id);
        return bounds.right > bounds.left && bounds.bottom > bounds.top &&
            bounds.left >= actual_client.left && bounds.top >= actual_client.top &&
            bounds.right <= actual_client.right && bounds.bottom <= actual_client.bottom;
    };
    const bool compact_alignment_contract =
        aligned_top(IDC_OS_LABEL, IDC_OS_TEXT) && aligned_top(IDC_ADMIN_LABEL, IDC_ADMIN_TEXT) &&
        aligned_top(IDC_BACKEND_LABEL, IDC_BACKEND_TEXT) && aligned_top(IDC_CONSUMER_LABEL, IDC_CONSUMER_TEXT) &&
        aligned_top(IDC_PROCESS_LABEL, IDC_GAME_PROCESS) && aligned_top(IDC_PID_LABEL, IDC_PID_TEXT) &&
        aligned_top(IDC_ANALYSIS_STATE_LABEL, IDC_ANALYSIS_STATE_TEXT) && aligned_top(IDC_GPU_LABEL, IDC_GPU_STATUS) &&
        aligned_top(IDC_AI_LABEL, IDC_AI_STATUS) && aligned_top(IDC_COVERAGE_LABEL, IDC_COVERAGE_STATUS) &&
        aligned_top(IDC_STATUS_LABEL, IDC_STATUS_TEXT) && aligned_top(IDC_SCOPE_LABEL, IDC_CAPTURE_SCOPE) &&
        aligned_top(IDC_PACKET_LABEL, IDC_PACKET_COUNT) && aligned_top(IDC_SIZE_LABEL, IDC_CAPTURE_SIZE) &&
        aligned_top(IDC_CLASSIFIED_LABEL, IDC_CLASSIFIED_COUNT) && aligned_top(IDC_OUTPUT_LABEL, IDC_OUTPUT_PATH) &&
        os_label_bounds.left == admin_label_bounds.left && os_label_bounds.left == consumer_label_bounds.left &&
        os_label_bounds.left == process_label_bounds.left && os_label_bounds.left == analysis_label_bounds.left &&
        os_label_bounds.left == status_label_bounds.left && os_label_bounds.left == scope_label_bounds.left;
    RECT current_work_area{0, 0, GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN)};
    SystemParametersInfoW(SPI_GETWORKAREA, 0, &current_work_area, 0);
    const RECT expected_window_bounds = FittedMainWindowBounds(current_work_area);
    const int expected_window_width = expected_window_bounds.right - expected_window_bounds.left;
    const int expected_window_height = expected_window_bounds.bottom - expected_window_bounds.top;
    const bool essential_controls_reachable = fully_reachable(IDC_HELP_BUTTON) &&
        fully_reachable(IDC_CONSUMER_LABEL) && fully_reachable(IDC_CONSUMER_TEXT) &&
        fully_reachable(IDC_START_CAPTURE) && fully_reachable(IDC_STOP_CAPTURE) &&
        fully_reachable(IDC_OUTPUT_LABEL) && fully_reachable(IDC_OUTPUT_PATH);
    const bool high_dpi_low_resolution_geometry =
        EssentialControlsFitSimulatedWorkArea(RECT{0, 0, 683, 384});
    const bool win7_system_dpi_manifest = EmbeddedManifestUsesWin7SystemDpiAwareness();
    const bool modern_visual_contract = window != nullptr && GetDlgItem(window, IDC_TITLE) != nullptr &&
        GetDlgItem(window, IDC_LANGUAGE) != nullptr && GetDlgItem(window, IDC_CAPTURE_SCOPE) != nullptr &&
        std::abs((modern_bounds.right - modern_bounds.left) - expected_window_width) <= 2 &&
        std::abs((modern_bounds.bottom - modern_bounds.top) - expected_window_height) <= 2 &&
        compact_alignment_contract && essential_controls_reachable &&
        high_dpi_low_resolution_geometry && win7_system_dpi_manifest;
    if (!modern_visual_contract) failures.emplace_back("the modern Windows UI layout contract failed");
    if (!essential_controls_reachable)
        failures.emplace_back("help, primary actions, or bottom output control is clipped");
    if (!high_dpi_low_resolution_geometry)
        failures.emplace_back("1366x768 at 200-percent effective-resolution geometry is not fully reachable");
    if (!win7_system_dpi_manifest)
        failures.emplace_back("embedded manifest is not Win7-compatible system-DPI-aware");
    const bool application_icon_contract = window != nullptr &&
        FindResourceW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(IDI_GOD2_PACKET_CAPTURE), RT_GROUP_ICON) != nullptr &&
        GetClassLongPtrW(window, GCLP_HICON) != 0 && GetClassLongPtrW(window, GCLP_HICONSM) != 0;
    if (!application_icon_contract) failures.emplace_back("the application icon resource or window icons are missing");
    const HWND language_control = window != nullptr ? GetDlgItem(window, IDC_LANGUAGE) : nullptr;
    const HWND help_button = window != nullptr ? GetDlgItem(window, IDC_HELP_BUTTON) : nullptr;
    const HWND enhanced_checkbox = window != nullptr ? GetDlgItem(window, IDC_ENHANCED_CAPTURE) : nullptr;
    const HWND dll_status_label = window != nullptr ? GetDlgItem(window, IDC_CONSUMER_LABEL) : nullptr;
    const HWND dll_status_value = window != nullptr ? GetDlgItem(window, IDC_CONSUMER_TEXT) : nullptr;
    const HWND author_control = window != nullptr ? GetDlgItem(window, IDC_AUTHOR) : nullptr;
    wchar_t author_text[256]{};
    if (author_control != nullptr) GetWindowTextW(author_control, author_text, static_cast<int>(std::size(author_text)));
    const bool author_identity_visible = author_control != nullptr && wcsstr(author_text, L"RayCat") != nullptr &&
        wcsstr(author_text, L"raycat66") != nullptr && wcsstr(author_text, GOD2_TOOL_DISPLAY_VERSION_W) != nullptr;
    if (!author_identity_visible) failures.emplace_back("release version, RayCat author, and Discord identity are not visible");
    const bool help_available = help_button != nullptr && IsWindowVisible(help_button);
    if (!help_available) failures.emplace_back("usage instructions button is not available");
    wchar_t dll_status_label_text[128]{};
    wchar_t dll_status_value_text[256]{};
    if (dll_status_label != nullptr)
        GetWindowTextW(dll_status_label, dll_status_label_text, static_cast<int>(std::size(dll_status_label_text)));
    if (dll_status_value != nullptr)
        GetWindowTextW(dll_status_value, dll_status_value_text, static_cast<int>(std::size(dll_status_value_text)));
    const bool dll_enhanced_status_visible = dll_status_label != nullptr && dll_status_value != nullptr &&
        IsWindowVisible(dll_status_label) && IsWindowVisible(dll_status_value) &&
        wcslen(dll_status_label_text) > 0 && wcsstr(dll_status_label_text, L"DLL") != nullptr &&
        wcslen(dll_status_value_text) > 0;
    if (!dll_enhanced_status_visible)
        failures.emplace_back("the permanent DLL enhanced capture label or status value is hidden or empty");
    const bool dll_auto_enable_policy = enhanced_checkbox != nullptr && !IsWindowVisible(enhanced_checkbox) &&
        SendMessageW(enhanced_checkbox, BM_GETCHECK, 0, 0) == BST_CHECKED;
    if (!dll_auto_enable_policy)
        failures.emplace_back("the internal DLL enhanced capture auto-enable policy is not locked on");
    const std::string detached_fixture =
        R"({"schemaVersion":2,"status":"DETACHED","code":0,"injectionAttempted":true,"moduleWasEverLoaded":true,"moduleLoadStateVerified":true,"moduleSnapshotVerified":true,"moduleAbsent":true,"targetProcessExited":false,"targetIdentityVerified":true,"probeReady":false,"stopSucceeded":true,"unloadSafe":true,"moduleUnloaded":true,"moduleResidentInactive":false,"strictUnloadVerified":true,"cleanupRetriable":false,"injectionMode":"PausePrimaryRemoteThreadResume","suspendedThreadCount":1,"primaryThreadId":1,"extraReferenceRequested":false,"extraReferenceLoaded":false,"extraReferenceNegativeFirstFreeLibraryStillPresent":false,"extraReferenceNegativeNotClaimedUnloaded":false,"extraReferenceReleasedThenModuleAbsent":false})";
    const std::string process_exited_fixture =
        R"({"schemaVersion":2,"status":"DETACHED_PROCESS_EXITED","code":0,"injectionAttempted":true,"moduleWasEverLoaded":true,"moduleLoadStateVerified":true,"moduleSnapshotVerified":false,"moduleAbsent":true,"targetProcessExited":true,"targetIdentityVerified":true,"probeReady":false,"stopSucceeded":false,"unloadSafe":false,"moduleUnloaded":false,"moduleResidentInactive":false,"strictUnloadVerified":true,"cleanupRetriable":false,"injectionMode":"PausePrimaryRemoteThreadResume","suspendedThreadCount":1,"primaryThreadId":1,"extraReferenceRequested":false,"extraReferenceLoaded":false,"extraReferenceNegativeFirstFreeLibraryStillPresent":false,"extraReferenceNegativeNotClaimedUnloaded":false,"extraReferenceReleasedThenModuleAbsent":false})";
    const auto detached_result = ParseEnhancedInjectorResultV2(detached_fixture);
    const auto process_exited_result = ParseEnhancedInjectorResultV2(
        process_exited_fixture);
    std::string quoted_boolean_spoof = detached_fixture;
    const auto boolean_position = quoted_boolean_spoof.find(
        "\"moduleUnloaded\":true");
    if (boolean_position != std::string::npos)
        quoted_boolean_spoof.replace(boolean_position,
            std::string_view("\"moduleUnloaded\":true").size(),
            "\"moduleUnloaded\":\"true\"");
    std::string duplicate_key_spoof = detached_fixture;
    if (!duplicate_key_spoof.empty())
        duplicate_key_spoof.insert(duplicate_key_spoof.size() - 1U,
            ",\"moduleAbsent\":true");
    std::string unknown_key_spoof = detached_fixture;
    if (!unknown_key_spoof.empty())
        unknown_key_spoof.insert(unknown_key_spoof.size() - 1U,
            ",\"UntrustedClaim\":true");
    std::string failure_code_spoof = detached_fixture;
    const auto code_position = failure_code_spoof.find("\"code\":0");
    if (code_position != std::string::npos)
        failure_code_spoof.replace(code_position,
            std::string_view("\"code\":0").size(), "\"code\":31");
    const auto failure_code_result = ParseEnhancedInjectorResultV2(
        failure_code_spoof);
    const bool dll_strict_unload_result_contract = detached_result &&
        IsVerifiedEnhancedDetachResult(*detached_result, false) &&
        process_exited_result &&
        IsVerifiedEnhancedDetachResult(*process_exited_result, true) &&
        !IsVerifiedEnhancedDetachResult(*process_exited_result, false) &&
        !ParseEnhancedInjectorResultV2(quoted_boolean_spoof) &&
        !ParseEnhancedInjectorResultV2(duplicate_key_spoof) &&
        !ParseEnhancedInjectorResultV2(unknown_key_spoof) &&
        failure_code_result &&
        !IsVerifiedEnhancedDetachResult(*failure_code_result, false);
    if (!dll_strict_unload_result_contract)
        failures.emplace_back("the DLL Stop result accepts resident-inactive or unproven unload as success");
    const bool dll_strict_unload_finalization_contract =
        CanCompleteCaptureStop(true, true, true, true, true, true) &&
        !CanCompleteCaptureStop(true, false, true, true, true, true) &&
        ConsumerStateText(ConsumerState::EvidenceBlocked) !=
            ConsumerStateText(ConsumerState::Stopped);
    if (!dll_strict_unload_finalization_contract)
        failures.emplace_back("a backend-only Stop can incorrectly complete while DLL unload remains unproven");
    const bool dll_consumer_state_lifecycle_contract =
        EnhancedCaptureConsumerState(true, false, false, false, false) ==
            ConsumerState::NotStarted &&
        EnhancedCaptureConsumerState(true, true, false, false, false) ==
            ConsumerState::Starting &&
        EnhancedCaptureConsumerState(true, false, true, false, false) ==
            ConsumerState::Ready &&
        EnhancedCaptureConsumerState(true, false, false, true, false) ==
            ConsumerState::Stopped &&
        EnhancedCaptureConsumerState(true, false, false, false, true) ==
            ConsumerState::EvidenceBlocked &&
        EnhancedCaptureConsumerStateAfterTargetDetection(
            ConsumerState::Ready, GameProcessState::Exited) ==
            ConsumerState::Stopping &&
        EnhancedCaptureConsumerStateAfterTargetDetection(
            ConsumerState::Ready, GameProcessState::Validated) ==
            ConsumerState::Ready &&
        EnhancedCaptureConsumerState(false, true, true, true, true) ==
            ConsumerState::NotStarted;
    if (!dll_consumer_state_lifecycle_contract)
        failures.emplace_back("the permanent DLL row does not preserve waiting, attaching, typed-ready, strict-stop, and EvidenceBlocked states");
    const auto cancellation_detach_fixture =
        contract_root / L"enhanced-cancel-detach-result.fixture.txt";
    const bool cancellation_fixture_written = WriteUtf8FileAtomic(
        cancellation_detach_fixture, detached_fixture + "\r\n");
    HANDLE cancellation_fixture_event =
        CreateEventW(nullptr, TRUE, TRUE, nullptr);
    const auto cancellation_detach_result = ReadExitedEnhancedMonitorResult(
        cancellation_fixture_event, 0, cancellation_detach_fixture);
    if (cancellation_fixture_event != nullptr)
        CloseHandle(cancellation_fixture_event);
    std::error_code cancellation_fixture_error;
    const bool cancellation_fixture_removed = fs::remove(
        cancellation_detach_fixture, cancellation_fixture_error) &&
        !cancellation_fixture_error;
    const bool dll_cancel_result_read_before_cleanup_contract =
        cancellation_fixture_written && cancellation_detach_result &&
        ParseEnhancedInjectorResultV2(*cancellation_detach_result) &&
        IsVerifiedEnhancedDetachResult(
            *ParseEnhancedInjectorResultV2(*cancellation_detach_result), false) &&
        cancellation_fixture_removed;
    if (!dll_cancel_result_read_before_cleanup_contract)
        failures.emplace_back("attach cancellation does not read safe-detach evidence before staging cleanup");
    bool shared_slot_reuse_domain_accounting = false;
    try {
        auto fixture_ring = std::make_unique<god2::shared::SemanticSharedRing>();
        fixture_ring->domains[5].consumer_lag = 1;
        fixture_ring->domains[7].consumer_lag = 1;
        fixture_ring->slots[0][0].domain = 5;
        const std::uint32_t consumed_domain = fixture_ring->slots[0][0].domain;
        // Model immediate producer reuse after release. Accounting must still
        // retire the snapshotted domain, never the replacement domain.
        fixture_ring->slots[0][0].domain = 7;
        shared_slot_reuse_domain_accounting =
            ConsumeSemanticDomainPending(fixture_ring.get(), consumed_domain) &&
            fixture_ring->domains[5].consumer_lag == 0 &&
            fixture_ring->domains[7].consumer_lag == 1;
    } catch (const std::bad_alloc&) {
        shared_slot_reuse_domain_accounting = false;
    }
    if (!shared_slot_reuse_domain_accounting)
        failures.emplace_back("shared-ring slot reuse changed per-domain pending accounting");
    const bool function_descriptions = dll_enhanced_status_visible && dll_auto_enable_policy &&
        GetDlgItem(window, IDC_AI_STATUS) != nullptr && GetDlgItem(window, IDC_COVERAGE_STATUS) != nullptr;
    if (!function_descriptions)
        failures.emplace_back("the exact-build probe policy or read-only Ultimate subsystem statuses are missing");
    const bool automated_observation_labels =
        start_button != nullptr && IsWindowVisible(start_button) &&
        stop_button != nullptr && IsWindowVisible(stop_button) &&
        !IsWindowVisible(GetDlgItem(window, IDC_BROWSE_GAME)) &&
        !IsWindowVisible(GetDlgItem(window, IDC_OPEN_OUTPUT)) &&
        !IsWindowVisible(GetDlgItem(window, IDC_COPY_OUTPUT)) &&
        !IsWindowVisible(GetDlgItem(window, IDC_OPEN_ANALYSIS)) &&
        !IsWindowVisible(GetDlgItem(window, IDC_OPEN_WORLD));
    if (!automated_observation_labels)
        failures.emplace_back("the GUI exposes actions beyond the two-action Ultimate workflow");
    bool localization_switch = language_control != nullptr &&
        SendMessageW(language_control, CB_GETCOUNT, 0, 0) == 3;
    bool dll_status_localization = localization_switch && dll_status_label != nullptr && dll_status_value != nullptr;
    const wchar_t* expected_start_labels[] = {
        L"開始完整恢復取證", L"开始完整恢复取证", L"Start full recovery forensics"};
    const wchar_t* expected_dll_labels[] = {
        L"DLL 增強擷取", L"DLL 增强捕获", L"DLL enhanced capture"};
    const wchar_t* expected_dll_idle_status[] = {
        L"自動啟用 · 等待已驗證的 God2_opt.exe",
        L"自动启用 · 等待已验证的 God2_opt.exe",
        L"Auto-enabled · waiting for verified God2_opt.exe"};
    for (int language_index = 0; language_index < 3 && localization_switch && dll_status_localization;
         ++language_index) {
        SendMessageW(language_control, CB_SETCURSEL, static_cast<WPARAM>(language_index), 0);
        SendMessageW(window, WM_COMMAND, MAKEWPARAM(IDC_LANGUAGE, CBN_SELCHANGE),
                     reinterpret_cast<LPARAM>(language_control));
        wchar_t start_label[128]{};
        wchar_t dll_label[128]{};
        wchar_t dll_status[256]{};
        GetWindowTextW(start_button, start_label, static_cast<int>(std::size(start_label)));
        GetWindowTextW(dll_status_label, dll_label, static_cast<int>(std::size(dll_label)));
        GetWindowTextW(dll_status_value, dll_status, static_cast<int>(std::size(dll_status)));
        localization_switch = wcscmp(start_label, expected_start_labels[language_index]) == 0;
        dll_status_localization = wcscmp(dll_label, expected_dll_labels[language_index]) == 0 &&
            wcscmp(dll_status, expected_dll_idle_status[language_index]) == 0;
    }
    if (!localization_switch) failures.emplace_back("Traditional Chinese, Simplified Chinese, and English switching failed");
    if (!dll_status_localization)
        failures.emplace_back("the DLL enhanced capture label/status is not complete in all three UI languages");
    const bool no_console_window = child_created && process.hProcess != nullptr &&
                                   !ProcessHasConsoleWindow(process.dwProcessId);
    if (child_created && !no_console_window) failures.emplace_back("GUI process owns a console window");

    bool second_instance_passed = false;
    if (window != nullptr && process.hProcess != nullptr) {
        std::vector<wchar_t> second_command(command.begin(), command.end());
        second_command.push_back(L'\0');
        STARTUPINFOW second_startup{};
        second_startup.cb = sizeof(second_startup);
        PROCESS_INFORMATION second_process{};
        const BOOL second_created = CreateProcessW(executable.c_str(), second_command.data(), nullptr, nullptr, FALSE,
                                                   CREATE_UNICODE_ENVIRONMENT, nullptr,
                                                   executable.parent_path().c_str(), &second_startup, &second_process);
        const DWORD second_create_error = second_created ? ERROR_SUCCESS : GetLastError();
        if (second_created) {
            CloseHandle(second_process.hThread);
            const DWORD second_wait = WaitForSingleObject(second_process.hProcess, 10'000);
            DWORD second_exit = STILL_ACTIVE;
            if (second_wait == WAIT_OBJECT_0) GetExitCodeProcess(second_process.hProcess, &second_exit);
            second_instance_passed = second_wait == WAIT_OBJECT_0 && second_exit == ERROR_ALREADY_EXISTS &&
                                     MainWindowForProcess(second_process.dwProcessId) == nullptr && IsWindow(window);
            if (second_wait != WAIT_OBJECT_0) TerminateProcess(second_process.hProcess, ERROR_TIMEOUT);
            CloseHandle(second_process.hProcess);
        } else {
            AppendStartupLog(L"GuiSmokeSecondInstanceCreateError", std::to_wstring(second_create_error));
        }
    }
    if (!second_instance_passed)
        failures.emplace_back("the second launch did not activate the single existing GUI instance and exit cleanly");

    bool window_stayed_open_60_seconds = false;
    if (process.hProcess != nullptr) {
        const DWORD first_wait = WaitForSingleObject(process.hProcess, 10'000);
        if (first_wait == WAIT_OBJECT_0) failures.emplace_back("GUI exited before 10 seconds");
        const DWORD second_wait = first_wait == WAIT_TIMEOUT ? WaitForSingleObject(process.hProcess, 50'000) : first_wait;
        if (second_wait == WAIT_OBJECT_0) failures.emplace_back("GUI exited before 60 seconds");
        window_stayed_open_60_seconds = window != nullptr && first_wait == WAIT_TIMEOUT && second_wait == WAIT_TIMEOUT;
        if (!window_stayed_open_60_seconds)
            failures.emplace_back("GUI did not satisfy the complete 60-second survival gate");
        if (window != nullptr && IsWindow(window)) PostMessageW(window, WM_CLOSE, 0, 0);
        if (WaitForSingleObject(process.hProcess, 10'000) != WAIT_OBJECT_0) {
            failures.emplace_back("GUI did not exit after its main window was closed");
            TerminateProcess(process.hProcess, ERROR_TIMEOUT);
        }
        CloseHandle(process.hProcess);
    }

    bool consecutive_launches = window != nullptr;
    for (int launch_index = 0; launch_index < 2 && consecutive_launches; ++launch_index) {
        std::vector<wchar_t> next_command(command.begin(), command.end());
        next_command.push_back(L'\0');
        STARTUPINFOW next_startup{};
        next_startup.cb = sizeof(next_startup);
        PROCESS_INFORMATION next_process{};
        const BOOL next_created = CreateProcessW(executable.c_str(), next_command.data(), nullptr, nullptr, FALSE,
                                                 CREATE_UNICODE_ENVIRONMENT, nullptr,
                                                 executable.parent_path().c_str(), &next_startup, &next_process);
        if (!next_created) {
            consecutive_launches = false;
            break;
        }
        CloseHandle(next_process.hThread);
        HWND next_window = nullptr;
        for (int attempt = 0; attempt < 100 && next_window == nullptr; ++attempt) {
            if (WaitForSingleObject(next_process.hProcess, 0) == WAIT_OBJECT_0) break;
            next_window = MainWindowForProcess(next_process.dwProcessId);
            if (next_window == nullptr) Sleep(100);
        }
        consecutive_launches = next_window != nullptr && GetDlgItem(next_window, IDC_START_CAPTURE) != nullptr &&
                               GetDlgItem(next_window, IDC_STOP_CAPTURE) != nullptr &&
                               !ProcessHasConsoleWindow(next_process.dwProcessId);
        if (next_window != nullptr) PostMessageW(next_window, WM_CLOSE, 0, 0);
        if (WaitForSingleObject(next_process.hProcess, 10'000) != WAIT_OBJECT_0)
            TerminateProcess(next_process.hProcess, ERROR_TIMEOUT);
        CloseHandle(next_process.hProcess);
    }
    if (!consecutive_launches) failures.emplace_back("three consecutive GUI launch and close cycles did not pass");

    const std::string portable_report_path = "SelfTests/" +
        WideToUtf8(report_path.filename().wstring());
    const bool portable_report_contract = portable_report_path.find(':') == std::string::npos &&
        portable_report_path.find("C:\\") == std::string::npos &&
        portable_report_path.find("Users/") == std::string::npos &&
        portable_report_path.find("Users\\") == std::string::npos;
    if (!portable_report_contract)
        failures.emplace_back("GUI smoke report path is not portable");

    const auto report = MakeJsonObject({
        {"Status", failures.empty() ? "BLOCKED" : "FAIL"},
        {"AutomatedStatus", failures.empty() ? "PASS" : "FAIL"}, {"TestedAtUtc", UtcNow()},
        {"SpecifiedRegressionTestCount", "23"},
        {"AutomatedSpecifiedCheckCount", "19"},
        {"ManualSpecifiedGateCount", "4"},
        {"AutomatedCheckCount", "72"},
        {"ReportPath", portable_report_path},
        {"ExecutableSha256", executable_sha256.value_or("UNAVAILABLE")},
        {"ExecutableSha256Bound", executable_sha256_bound ? "PASS" : "FAIL"},
        {"PortableReportPath", portable_report_contract ? "PASS" : "FAIL"},
        {"VersionV130UltimateContract", version_v130_ultimate_contract ? "PASS" : "FAIL"},
        {"FreshLogsDirectory", fresh_logs_gui_launch ? "PASS" : "FAIL"},
        {"ExistingLogsDirectory", existing_logs_gui_launch ? "PASS" : "FAIL"},
        {"ExistingParentDirectories", existing_parents ? "PASS" : "FAIL"},
        {"LauncherFlowContract", launcher_flow_contract ? "PASS" : "FAIL"},
        {"X86GameContract", x86_game_contract ? "PASS" : "FAIL"},
        {"ExactClientIdentityNegativeContract", exact_client_identity_negative_contract ? "PASS" : "FAIL"},
        {"ThreeLanguageUi", localization_switch ? "PASS" : "FAIL"},
        {"AuthorIdentityVisible", author_identity_visible ? "PASS" : "FAIL"},
        {"UsageInstructionsVisible", help_available ? "PASS" : "FAIL"},
        {"DllEnhancedCaptureStatusVisible", dll_enhanced_status_visible ? "PASS" : "FAIL"},
        {"DllEnhancedCaptureAutoEnabled", dll_auto_enable_policy ? "PASS" : "FAIL"},
        {"DllStrictUnloadResultContract", dll_strict_unload_result_contract ? "PASS" : "FAIL"},
        {"DllStrictUnloadFinalizationContract", dll_strict_unload_finalization_contract ? "PASS" : "FAIL"},
        {"DllEnhancedCaptureConsumerStateLifecycle", dll_consumer_state_lifecycle_contract ? "PASS" : "FAIL"},
        {"DllCancelResultReadBeforeCleanupContract", dll_cancel_result_read_before_cleanup_contract ? "PASS" : "FAIL"},
        {"DllStrictUnloadPolicy", "QuiesceRestoreDrainAndProveModuleAbsent;OtherwiseEvidenceBlockedAndBoundedRetry;NeverReportResidentInactiveAsStopped"},
        {"DllEnhancedCaptureStatusSchemaVersion", enhanced_status_schema_observed},
        {"DllEnhancedCaptureInitialStrictUnloadVerified", enhanced_initial_strict_unload_observed},
        {"ThreeLanguageDllStrictUnloadContract", dll_strict_unload_tip_contract ? "PASS" : "FAIL"},
        {"SharedTransportSlotReuseDomainAccounting", shared_slot_reuse_domain_accounting ? "PASS" : "FAIL"},
        {"ThreeLanguageDllEnhancedCaptureStatus", dll_status_localization ? "PASS" : "FAIL"},
        {"ThreeLanguageDllEnhancedCaptureHelp", dll_help_contract ? "PASS" : "FAIL"},
        {"UltimateReadOnlySubsystemStatuses", function_descriptions ? "PASS" : "FAIL"},
        {"TwoActionWorkflow", automated_observation_labels ? "PASS" : "FAIL"},
        {"EnhancedX86PayloadStructure", enhanced_x86_payload_structure ? "PASS" : "FAIL"},
        {"EnhancedPlaintextStatusContract", enhanced_plaintext_status_contract ? "PASS" : "FAIL"},
        {"DllTwentyFiveDomainStatusContract", dll_twenty_five_domain_status_contract ? "PASS" : "FAIL"},
        {"EnhancedNoAttachFailurePreserved", enhanced_no_attach_failure_preserved ? "PASS" : "FAIL"},
        {"EnhancedNonAdminAttachFailClosed", enhanced_non_admin_attach_fail_closed ? "PASS" : "FAIL"},
        {"ModernWindowsUiContract", modern_visual_contract ? "PASS" : "FAIL"},
        {"EssentialControlsReachable", essential_controls_reachable ? "PASS" : "FAIL"},
        {"HighDpiLowEffectiveResolutionGeometry", high_dpi_low_resolution_geometry ? "PASS" : "FAIL"},
        {"Win7SystemDpiAwareManifest", win7_system_dpi_manifest ? "PASS" : "FAIL"},
        {"ApplicationIconContract", application_icon_contract ? "PASS" : "FAIL"},
        {"CommonControlsWithStale183", common_stale_183 ? "PASS" : "FAIL"},
        {"CommonControlsWithStale5", common_stale_5 ? "PASS" : "FAIL"},
        {"CommonControlsSuccessDoesNotConsumeLastError", common_success_did_not_consume_error ? "PASS" : "FAIL"},
        {"ExistingMutexContract", existing_mutex ? "PASS" : "FAIL"},
        {"NoArgumentLaunch", window != nullptr ? "PASS" : "FAIL"},
        {"NoConsoleWindow", no_console_window ? "PASS" : "FAIL"},
        {"WindowStayedOpen60Seconds", window_stayed_open_60_seconds ? "PASS" : "FAIL"},
        {"StartButtonVisible", start_button != nullptr ? "PASS" : "FAIL"},
        {"StopButtonVisible", stop_button != nullptr ? "PASS" : "FAIL"},
        {"SecondInstance", second_instance_passed ? "PASS" : "FAIL"},
        {"ColdStartSecondInstance", cold_start_second_instance ? "PASS" : "FAIL"},
        {"ExistingCaptureRootCreatesNewSession", existing_capture_root_session ? "PASS" : "FAIL"},
        {"ZeroEnhancedCaptureRecordsPackage", zero_enhanced_records_package ? "PASS" : "FAIL"},
        {"RawBackendPrivacyContract", raw_backend_privacy_contract ? "PASS" : "FAIL"},
        {"RawActivePhrasesAbsentWhenPrivacyBlocked", raw_active_phrases_absent_when_privacy_blocked ? "PASS" : "FAIL"},
        {"ThreeLanguagePrivacyHelpContract", three_language_privacy_help_contract ? "PASS" : "FAIL"},
        {"TerminalPrivacyBlockStateNotActive", terminal_privacy_block_state_not_active ? "PASS" : "FAIL"},
        {"RawPayloadCanaryAbsentFromEvidenceZip", raw_payload_canary_absent_from_zip ? "PASS" : "FAIL"},
        {"ThreeConsecutiveLaunches", consecutive_launches ? "PASS" : "FAIL"},
        {"NearRealtimeAnalysis", near_realtime_analysis ? "PASS" : "FAIL"},
        {"NearRealtimeFailureFallback", near_realtime_failure_fallback ? "PASS" : "FAIL"},
        {"TargetPidAnalysisFilter", target_pid_analysis_filter ? "PASS" : "FAIL"},
        {"TargetPidHistoryReport", target_pid_history_report ? "PASS" : "FAIL"},
        {"EtwScopePrivacyDecisionContract", etw_scope_privacy_decision_contract ? "PASS" : "FAIL"},
        {"RawEtlEvidenceBlockedReport", raw_etl_evidence_blocked_report ? "PASS" : "FAIL"},
        {"InjectedTransportEvidenceReport", injected_transport_evidence_report ? "PASS" : "FAIL"},
        {"InjectedTransportUnknownSummary", injected_transport_unknown_summary ? "PASS" : "FAIL"},
        {"God2FrameCandidateReport", god2_frame_candidate_report ? "PASS" : "FAIL"},
        {"God2OpcodeSemanticReport", god2_opcode_semantic_report ? "PASS" : "FAIL"},
        {"CandidateGameplayCoverageReport", candidate_gameplay_coverage_report ? "PASS" : "FAIL"},
        {"CandidateSummaryMetrics", candidate_summary_metrics ? "PASS" : "FAIL"},
        {"CaptureStateUiContract", capture_state_ui_contract ? "PASS" : "FAIL"},
        {"StartupAndCaptureCancellationContract", cancellable_state_contract ? "PASS" : "FAIL"},
        {"RapidStartStopCancellation", rapid_start_stop ? "PASS" : "FAIL"},
        {"StructuredGameProcessStateContract", process_detection_state_contract ? "PASS" : "FAIL"},
        {"EtwConsumerReadyEarlyExitAndRetryContract", etw_ready_contract ? "PASS" : "FAIL"},
        {"PostCaptureFallbackContract", post_capture_fallback_contract ? "PASS" : "FAIL"},
        {"GpuAndSubsystemStateControls", subsystem_state_controls ? "PASS" : "FAIL"},
        {"LoginHelpTextCorrect", login_help_correct ? "PASS" : "FAIL"},
        {"ElevatedWorkerPidLiveGate", "NOT_RUN_REQUIRES_ELEVATED_GOD2_OPT_PROCESS"},
        {"EtwConsumerResidenceLiveGate", "NOT_RUN_REQUIRES_ACTIVE_ETW_SESSION"},
        {"CancellationResidualResourceLiveGate", "NOT_RUN_REQUIRES_ELEVATION_AND_ACTIVE_CAPTURE"},
        {"IndependentPidTrackingOnBackendFailureLiveGate", "NOT_RUN_REQUIRES_GOD2_OPT_PROCESS"},
        {"StartCaptureLiveGate", "NOT_RUN_REQUIRES_LAUNCHER_LOGIN_GAME_START_AND_ELEVATION"},
        {"StopCaptureFlushLiveGate", "NOT_RUN_REQUIRES_ACTIVE_CAPTURE"},
        {"Windows10ExplorerDoubleClick", "NOT_RUN_MANUAL_GATE"},
        {"Windows11ExplorerDoubleClick", "NOT_RUN_MANUAL_GATE"},
        {"ReleaseStatus", "RELEASE_BLOCKED"},
        {"FailureCount", std::to_string(failures.size())}, {"Failures", JoinList(failures)}
    }, {"SpecifiedRegressionTestCount", "AutomatedSpecifiedCheckCount", "ManualSpecifiedGateCount",
        "AutomatedCheckCount", "DllEnhancedCaptureInitialStrictUnloadVerified", "FailureCount"});
    SetEnvironmentVariableW(L"GOD2_PACKET_CAPTURE_INTERNAL_TEST_MUTEX", nullptr);
    return WriteUtf8FileAtomic(report_path, report + "\n") && failures.empty() ? 0 : ERROR_GEN_FAILURE;
}

} // namespace god2

int WINAPI wWinMain(_In_ HINSTANCE instance, _In_opt_ HINSTANCE previous_instance,
                    _In_ PWSTR command_line, _In_ int show_command) {
    (void)previous_instance;
    (void)command_line;
    god2::g_ui_language.store(god2::DetectUiLanguage());
    try {
        god2::InitializeStartupLog();
        SetUnhandledExceptionFilter(god2::UnhandledExceptionLogger);
        int argc = 0;
        wchar_t** argv = CommandLineToArgvW(GetCommandLineW(), &argc);
        const bool internal_gui_test = argv != nullptr && argc > 2 &&
            (wcscmp(argv[1], L"--internal-gui-test-log-root") == 0 ||
             wcscmp(argv[1], L"--internal-gui-test-delay-ms") == 0);
        const bool internal_gui_instance = argv != nullptr && argc > 1 &&
            wcscmp(argv[1], L"--internal-gui-test-instance") == 0;
        if (argv != nullptr && argc > 1 && wcsncmp(argv[1], L"--internal-", 11) == 0 &&
            !internal_gui_test && !internal_gui_instance) {
            const int result = god2::RunInternalCommandLine(argc, argv);
            LocalFree(argv);
            return result;
        }
        if (argv != nullptr) LocalFree(argv);
        return god2::RunGuiApplication(instance, show_command);
    } catch (const god2::StartupFailure& failure) {
        god2::LogStageFailure(failure.Stage(), failure.Api(), failure.ReturnValue(),
                              failure.Win32Error(), failure.Result());
        god2::AppendStartupLog(L"ExceptionType", L"StartupFailure");
        god2::AppendStartupLog(L"ExceptionMessage", god2::Utf8ToWide(failure.what()));
        const std::wstring log_path = god2::g_log_path.empty() ?
            god2::Localized(L"（啟動日誌無法建立）", L"（无法创建启动日志）", L"(startup log unavailable)") :
            god2::g_log_path.wstring();
        const std::wstring message = std::wstring(god2::Localized(
            L"God2 語意恢復引擎啟動失敗。\n\n階段：", L"God2 语义恢复引擎启动失败。\n\n阶段：", L"God2 Semantic Recovery Engine failed to start.\n\nStage: ")) + failure.Stage() +
            god2::Localized(L"\nAPI：", L"\nAPI：", L"\nAPI: ") + failure.Api() +
            god2::Localized(L"\n返回值：", L"\n返回值：", L"\nReturn value: ") + failure.ReturnValue() +
            god2::Localized(L"\nWin32：", L"\nWin32：", L"\nWin32: ") + std::to_wstring(failure.Win32Error()) + L" (" +
            god2::ErrorText(failure.Win32Error()) + L")" + god2::Localized(L"\n日誌：", L"\n日志：", L"\nLog: ") + log_path;
        MessageBoxW(nullptr, message.c_str(), god2::kWindowTitle, MB_OK | MB_ICONERROR);
        return failure.Win32Error() == ERROR_SUCCESS ? ERROR_GEN_FAILURE : static_cast<int>(failure.Win32Error());
    } catch (const std::exception& exception) {
        god2::AppendStartupLog(L"StageFailure", L"UnhandledStartupException");
        god2::AppendStartupLog(L"ApiName", L"C++ exception");
        god2::AppendStartupLog(L"ReturnValue", L"exception");
        god2::AppendStartupLog(L"CapturedWin32Error", L"NotAvailable");
        god2::AppendStartupLog(L"HRESULT", L"NotAvailable");
        god2::AppendStartupLog(L"ExceptionType", L"std::exception");
        god2::AppendStartupLog(L"ExceptionMessage", god2::Utf8ToWide(exception.what()));
        const std::wstring message = std::wstring(god2::Localized(
            L"God2 語意恢復引擎啟動失敗。\n\n錯誤：", L"God2 语义恢复引擎启动失败。\n\n错误：", L"God2 Semantic Recovery Engine failed to start.\n\nError: ")) + god2::Utf8ToWide(exception.what()) +
            god2::Localized(L"\nWin32：未提供（這不是 Win32 API 失敗）\n日誌：", L"\nWin32：未提供（这不是 Win32 API 失败）\n日志：", L"\nWin32: not supplied (not a Win32 API failure)\nLog: ") +
            (god2::g_log_path.empty() ? god2::Localized(L"（啟動日誌無法建立）", L"（无法创建启动日志）", L"(startup log unavailable)") : god2::g_log_path.wstring());
        MessageBoxW(nullptr, message.c_str(), god2::kWindowTitle, MB_OK | MB_ICONERROR);
        return ERROR_UNHANDLED_EXCEPTION;
    } catch (...) {
        god2::AppendStartupLog(L"StageFailure", L"UnhandledStartupException");
        god2::AppendStartupLog(L"ApiName", L"unknown exception");
        god2::AppendStartupLog(L"ReturnValue", L"exception");
        god2::AppendStartupLog(L"CapturedWin32Error", L"NotAvailable");
        god2::AppendStartupLog(L"HRESULT", L"NotAvailable");
        god2::AppendStartupLog(L"ExceptionType", L"unknown");
        god2::AppendStartupLog(L"ExceptionMessage", L"Unknown exception");
        const std::wstring message = std::wstring(god2::Localized(
            L"God2 語意恢復引擎發生未知啟動錯誤。\n\nWin32：未提供\n日誌：", L"God2 语义恢复引擎发生未知启动错误。\n\nWin32：未提供\n日志：", L"God2 Semantic Recovery Engine encountered an unknown startup error.\n\nWin32: not supplied\nLog: ")) +
            (god2::g_log_path.empty() ? god2::Localized(L"（啟動日誌無法建立）", L"（无法创建启动日志）", L"(startup log unavailable)") : god2::g_log_path.wstring());
        MessageBoxW(nullptr, message.c_str(), god2::kWindowTitle, MB_OK | MB_ICONERROR);
        return ERROR_UNHANDLED_EXCEPTION;
    }
}
