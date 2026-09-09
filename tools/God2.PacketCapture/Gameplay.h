#pragma once

#include "Core.h"
#include "Storage.h"

#include <deque>
#include <unordered_set>

namespace god2 {

enum class MovementDirection {
    North,
    NorthEast,
    East,
    SouthEast,
    South,
    SouthWest,
    West,
    NorthWest,
    Idle,
    UnknownDirection
};

struct DirectionResult {
    MovementDirection direction = MovementDirection::UnknownDirection;
    EvidenceLevel evidence = EvidenceLevel::Unknown;
    std::string source = "Unavailable";
};

std::string ToString(MovementDirection value);
std::string DirectionChinese(MovementDirection value);
DirectionResult ResolveDirection(const Frame& frame);

class ProtocolParserRegistry {
public:
    ProtocolParserRegistry();
    bool Parse(std::string_view json, Frame& frame, std::string* error = nullptr) const;
    const std::vector<FeatureMetadata>& Features() const { return features_; }

private:
    std::vector<FeatureMetadata> features_;
};

class GameplayClassifierRegistry {
public:
    using Handler = std::function<bool(const Frame&, std::string*)>;

    void Register(const FeatureMetadata& metadata, const std::vector<std::string>& message_types, Handler handler);
    bool Dispatch(const Frame& frame, std::string* error = nullptr) const;
    bool Contains(std::string_view message_type) const;
    const std::vector<FeatureMetadata>& Features() const { return features_; }

private:
    std::unordered_map<std::string, Handler> handlers_;
    std::vector<FeatureMetadata> features_;
};

class EventCorrelatorRegistry {
public:
    std::string Begin(std::string_view key, std::string_view transmitted = {});
    std::string Resolve(std::string_view key, std::string_view transmitted = {});
    void End(std::string_view key);
    bool IsActive(std::string_view key) const;
    FeatureMetadata Metadata() const;

private:
    struct CorrelationState {
        std::string correlation_id;
        bool active = true;
    };

    void Remember(std::string key, std::string correlation_id, bool active);
    static constexpr std::size_t kMaximumCorrelations = 4096;
    std::unordered_map<std::string, CorrelationState> correlations_;
    std::deque<std::string> insertion_order_;
};

class FormulaAnalyzerRegistry {
public:
    DirectionResult AnalyzeDirection(const Frame& frame) const { return ResolveDirection(frame); }
    FeatureMetadata Metadata() const;
};

class ExportRegistry {
public:
    const std::vector<std::pair<std::string, fs::path>>& Entries() const { return entries_; }
    FeatureMetadata Metadata() const;

private:
    std::vector<std::pair<std::string, fs::path>> entries_ = RequiredExports();
};

template <typename T>
class BoundedEventBuffer {
public:
    explicit BoundedEventBuffer(std::size_t capacity) : capacity_(std::max<std::size_t>(1, capacity)) {}

    void Push(T value) {
        if (values_.size() == capacity_) values_.pop_front();
        values_.push_back(std::move(value));
        high_watermark_ = std::max(high_watermark_, values_.size());
    }
    std::optional<T> Pop() {
        if (values_.empty()) return std::nullopt;
        T value = std::move(values_.front());
        values_.pop_front();
        return value;
    }
    std::size_t Size() const { return values_.size(); }
    std::size_t Capacity() const { return capacity_; }
    std::size_t HighWatermark() const { return high_watermark_; }

private:
    std::size_t capacity_;
    std::size_t high_watermark_ = 0;
    std::deque<T> values_;
};

struct AnalysisStatistics {
    std::uint64_t frames = 0;
    std::uint64_t movement = 0;
    std::uint64_t mount = 0;
    std::uint64_t quest = 0;
    std::uint64_t skill = 0;
    std::uint64_t consumable = 0;
    std::uint64_t entity = 0;
    std::uint64_t character_lifecycle = 0;
    std::uint64_t party = 0;
    std::uint64_t economy = 0;
    std::uint64_t progression = 0;
    std::uint64_t formula = 0;
    std::uint64_t protocol_decoded = 0;
    std::uint64_t unknown = 0;
};

class GameplayAnalysisEngine {
public:
    explicit GameplayAnalysisEngine(SessionStore& store);

    bool ProcessJsonLine(std::string_view json, bool persist_raw, std::string* error = nullptr);
    bool ProcessFrame(const Frame& frame, bool persist_raw, std::string* error = nullptr);
    bool PersistRawEvidence(const Frame& frame, std::string* error = nullptr);
    bool AnalyzeJsonl(const fs::path& input, bool persist_raw, std::string* error = nullptr);
    const AnalysisStatistics& Statistics() const { return statistics_; }
    const GameplayClassifierRegistry& Classifiers() const { return classifiers_; }
    const ProtocolParserRegistry& Parsers() const { return parsers_; }
    const EventCorrelatorRegistry& Correlators() const { return correlators_; }

private:
    void RegisterHandlers();
    void RememberKnownSkill(const std::string& skill_id);
    bool HandleMovement(const Frame& frame, std::string* error);
    bool HandleMount(const Frame& frame, std::string* error);
    bool HandleQuest(const Frame& frame, std::string* error);
    bool HandleQuestRelationship(const Frame& frame, std::string* error);
    bool HandleSkill(const Frame& frame, std::string* error);
    bool HandleConsumable(const Frame& frame, std::string* error);
    bool HandleEntity(const Frame& frame, std::string* error);
    bool HandleCharacterLifecycle(const Frame& frame, std::string* error);
    bool HandleParty(const Frame& frame, std::string* error);
    bool HandleEconomy(const Frame& frame, std::string* error);
    bool HandleProgression(const Frame& frame, std::string* error);
    bool HandleFormula(const Frame& frame, std::string* error);
    bool HandleDecodedProtocol(const Frame& frame, std::string* error);
    bool UpdateGameplayCoverage(const Frame& frame, std::string* error);
    bool HandleUnknown(const Frame& frame, std::string* error);

    SessionStore& store_;
    ProtocolParserRegistry parsers_;
    GameplayClassifierRegistry classifiers_;
    EventCorrelatorRegistry correlators_;
    FormulaAnalyzerRegistry formulas_;
    ExportRegistry exports_;
    AnalysisStatistics statistics_;
    static constexpr std::size_t kMaximumKnownSkills = 4096;
    std::unordered_set<std::string> known_skill_ids_;
    std::deque<std::string> known_skill_order_;
};

const std::set<std::string>& QuestTypes();
const std::set<std::string>& QuestObjectiveTypes();
const std::map<std::string, std::string>& SkillFacetCatalog();
const std::set<std::string>& ConsumableTypes();
const std::vector<std::string>& CoverageMarkers();
bool IsCoverageMarker(std::string_view marker);

} // namespace god2
