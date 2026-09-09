#pragma once

#include "Core.h"

namespace god2 {

// Live validation supplies an unpredictable in-memory challenge.  The GUI
// confirms the exact session it created and keeps a non-delete-share directory
// handle open so the runner never has to discover a session by enumeration.
struct GuiLiveSessionBinding {
    std::string challenge_nonce;
    std::string confirmation_nonce;
    fs::path session_path;
    std::string session_id;
    std::wstring final_directory_path;
    HANDLE session_directory_handle = nullptr;
    DWORD volume_serial_number = 0;
    DWORD file_index_high = 0;
    DWORD file_index_low = 0;
    std::string error;
};

int RunGuiApplication(HINSTANCE instance, int show_command,
                      GuiLiveSessionBinding* live_binding = nullptr);
int RunGuiSmokeTest(const fs::path& report_path);
int RunElevatedCaptureWorker(const std::vector<std::wstring>& arguments);
int RunHeadlessEnhancedCapture(const fs::path& session_path,
                               std::string_view session_id,
                               const fs::path& game_path,
                               DWORD process_id,
                               DWORD observe_seconds,
                               const std::wstring& stop_event_name);
int RunInternalCommandLine(int argc, wchar_t** argv);

} // namespace god2
