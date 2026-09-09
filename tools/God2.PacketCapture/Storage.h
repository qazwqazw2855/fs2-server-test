#pragma once

#include "Core.h"
#include "third_party/sqlite3.h"

#include <mutex>

namespace god2 {

class Database {
public:
    Database() = default;
    ~Database();
    Database(const Database&) = delete;
    Database& operator=(const Database&) = delete;

    bool Open(const fs::path& path, std::string* error = nullptr);
    bool Migrate(std::string* error = nullptr);
    bool Execute(std::string_view sql, std::string* error = nullptr);
    bool Begin(std::string* error = nullptr);
    bool Commit(std::string* error = nullptr);
    bool Rollback();
    bool Insert(std::string_view table, const Fields& values, std::string* error = nullptr);
    bool ExportCsv(std::string_view table, const fs::path& path, std::string* error = nullptr);
    std::int64_t ScalarInt64(std::string_view sql, std::int64_t fallback = 0) const;
    int SchemaVersion() const;
    sqlite3* Handle() const { return database_; }

private:
    sqlite3* database_ = nullptr;
};

class SessionStore {
public:
    explicit SessionStore(fs::path session_path);

    bool Initialize(std::string* error = nullptr);
    bool BeginAnalysis(std::string_view reason, std::string* error = nullptr);
    bool FinishAnalysis(std::string_view status, std::string* error = nullptr);
    bool PersistRawFrame(const Frame& frame, std::string* error = nullptr);
    bool WriteObservation(std::string_view table,
                          const fs::path& jsonl_relative_path,
                          const Fields& values,
                          std::string* error = nullptr);
    bool WriteJsonl(const fs::path& relative_path, std::string_view json, std::string* error = nullptr);
    bool ExportAll(std::string* error = nullptr);
    bool WriteCompatibilityReports(const BackendSelection& selection, std::string* error = nullptr);

    const fs::path& Path() const { return session_path_; }
    const std::string& SessionId() const { return session_id_; }
    const std::string& AnalysisRunId() const { return analysis_run_id_; }
    Database& Db() { return database_; }

private:
    bool RebuildRawFramesJsonl(std::string* error = nullptr);
    bool ImportLegacyJsonlRecords(std::string* error = nullptr);
    bool RebuildGameplayJsonl(std::string* error = nullptr);
    bool RebuildJsonlFromLedger(const fs::path& relative_path, std::string* error = nullptr);
    bool AppendJsonlLine(const fs::path& relative_path,
                         std::string_view json,
                         std::uintmax_t* previous_size,
                         std::string* error);

    fs::path session_path_;
    std::string session_id_;
    std::string analysis_run_id_;
    Database database_;
    std::mutex write_mutex_;
};

std::vector<std::pair<std::string, fs::path>> RequiredExports();
std::vector<fs::path> RequiredGameplayFiles();

} // namespace god2
