cmake_minimum_required(VERSION 3.24)
project(God2SemanticRecoveryEngine VERSION 1.3.0 LANGUAGES C CXX)

find_package(minizip CONFIG REQUIRED)

add_executable(God2SemanticRecoveryEngine WIN32
    ActionGrouping.cpp
    AutomaticSemantic.cpp
    CaptureBackends.cpp
    Core.cpp
    EtwConsumer.cpp
    EvidencePackage.cpp
    EmbeddedResources.rc
    Gameplay.cpp
    GpuAcceleration.cpp
    Gui.cpp
    Main.cpp
    Rtx5070Validation.cpp
    SemanticRecovery.cpp
    Storage.cpp
    Transport.cpp
    UltimateRecovery.cpp
    third_party/sqlite3.c
)

add_custom_target(SyncEmbeddedInstrumentationPayloads
    COMMAND powershell.exe -NoProfile -ExecutionPolicy Bypass
        -File "${CMAKE_CURRENT_SOURCE_DIR}/scripts/Sync-EmbeddedInstrumentationPayloads.ps1"
        -RepositoryRoot "${CMAKE_CURRENT_SOURCE_DIR}/../.."
        -Configuration $<IF:$<CONFIG:Debug>,Debug,Release>
    VERBATIM
)
add_dependencies(God2SemanticRecoveryEngine SyncEmbeddedInstrumentationPayloads)


target_compile_features(God2SemanticRecoveryEngine PRIVATE cxx_std_20)
target_compile_definitions(God2SemanticRecoveryEngine PRIVATE
    _WIN32_WINNT=0x0601
    WINVER=0x0601
    NTDDI_VERSION=0x06010000
    UNICODE
    _UNICODE
    WIN32_LEAN_AND_MEAN
    NOMINMAX
    SQLITE_THREADSAFE=1
    SQLITE_OMIT_LOAD_EXTENSION
    SQLITE_DEFAULT_MEMSTATUS=0
)
target_compile_options(God2SemanticRecoveryEngine PRIVATE
    $<$<COMPILE_LANGUAGE:CXX>:/utf-8>
    $<$<COMPILE_LANGUAGE:CXX>:/W4>
    $<$<COMPILE_LANGUAGE:CXX>:/WX>
    # Match the checked-in Visual Studio project: first-party C++ is strict,
    # while the unmodified SQLite amalgamation is compiled as third-party C.
    $<$<COMPILE_LANGUAGE:C>:/utf-8>
    $<$<COMPILE_LANGUAGE:C>:/W0>
    $<$<COMPILE_LANGUAGE:C>:/WX->
    $<$<COMPILE_LANGUAGE:C>:/wd4996>
)
# Visual Studio represents C and C++ files in one ClCompile item definition,
# so the target-level COMPILE_LANGUAGE expression alone cannot override /WX
# for the C amalgamation.  Apply the third-party policy again at source scope;
# the trailing /WX- wins for sqlite3.c without weakening first-party C++.
set_source_files_properties(third_party/sqlite3.c PROPERTIES
    COMPILE_OPTIONS "/utf-8;/W0;/WX-;/wd4996"
)
set_property(TARGET God2SemanticRecoveryEngine PROPERTY
    MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
target_link_libraries(God2SemanticRecoveryEngine PRIVATE
    user32 gdi32 comctl32 comdlg32 shell32 advapi32 version ole32 ws2_32 psapi bcrypt
    $<IF:$<TARGET_EXISTS:MINIZIP::minizip>,MINIZIP::minizip,MINIZIP::minizipstatic>
)
target_link_options(God2SemanticRecoveryEngine PRIVATE
    "/MANIFESTDEPENDENCY:type='win32' name='Microsoft.Windows.Common-Controls' version='6.0.0.0' processorArchitecture='amd64' publicKeyToken='6595b64144ccf1df' language='*'"
)
