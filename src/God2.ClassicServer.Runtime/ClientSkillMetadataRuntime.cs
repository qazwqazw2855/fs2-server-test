using System.Collections.ObjectModel;
using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Runtime;

public enum ClientSkillTargetShape
{
    Unknown = 0,
    SingleTarget = 1
}

public sealed record ClientSkillMetadata(
    int OfficialClientItemId,
    int? OfficialDisplayId,
    string NameZhTw,
    string? SkillModeZhTw,
    string? SkillCategoryZhTw,
    int? SkillTier,
    int? MpCost,
    int? AttackRange,
    string? TargetScopeZhTw,
    ClientSkillTargetShape TargetShape,
    string TargetShapeEvidenceStatus,
    string? OfficialEffectTextZhTw,
    string EvidenceStatus,
    bool LiveVerified);

public interface IClientSkillMetadataRuntime
{
    OperationResult<ClientSkillMetadata> ResolveClientSkillMetadata(int officialClientItemId);

    IReadOnlyList<ClientSkillMetadata> ResolveClientSkillMetadataForFormalSkill(long skillId);
}

public sealed class ClientSkillMetadataCatalog
{
    private readonly IReadOnlyDictionary<int, ClientSkillMetadata> _byClientItemId;
    private readonly IReadOnlyDictionary<long, IReadOnlyList<ClientSkillMetadata>> _byFormalSkillId;

    public ClientSkillMetadataCatalog(
        IEnumerable<ClientSkillMetadata> records,
        IEnumerable<(long SkillId, int OfficialClientItemId)> mappings)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(mappings);
        var values = records.ToArray();
        if (values.Any(value =>
                value.OfficialClientItemId <= 0 ||
                string.IsNullOrWhiteSpace(value.NameZhTw) ||
                string.IsNullOrWhiteSpace(value.TargetShapeEvidenceStatus) ||
                value.MpCost is < 0 ||
                value.AttackRange is < 0 ||
                (value.TargetShape == ClientSkillTargetShape.SingleTarget && !value.LiveVerified)))
        {
            throw new InvalidOperationException("Client skill metadata is invalid.");
        }

        var byClientItemId = values.ToDictionary(value => value.OfficialClientItemId);
        _byClientItemId = new ReadOnlyDictionary<int, ClientSkillMetadata>(byClientItemId);

        var mapped = mappings
            .Where(mapping => mapping.SkillId > 0 && byClientItemId.ContainsKey(mapping.OfficialClientItemId))
            .Distinct()
            .GroupBy(mapping => mapping.SkillId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ClientSkillMetadata>)group
                    .Select(mapping => byClientItemId[mapping.OfficialClientItemId])
                    .OrderBy(value => value.OfficialClientItemId)
                    .ToArray());
        _byFormalSkillId = new ReadOnlyDictionary<long, IReadOnlyList<ClientSkillMetadata>>(mapped);
    }

    public int RecordCount => _byClientItemId.Count;

    public int FormalSkillMappingCount => _byFormalSkillId.Sum(pair => pair.Value.Count);

    public OperationResult<ClientSkillMetadata> Resolve(int officialClientItemId) =>
        _byClientItemId.TryGetValue(officialClientItemId, out var metadata)
            ? OperationResult<ClientSkillMetadata>.Success(metadata)
            : OperationResult<ClientSkillMetadata>.Failure(
                "skill.client_metadata_missing",
                "Official client skill metadata was not found.",
                officialClientItemId.ToString());

    public IReadOnlyList<ClientSkillMetadata> ResolveForFormalSkill(long skillId) =>
        _byFormalSkillId.GetValueOrDefault(skillId) ?? [];
}

public sealed class ClientSkillMetadataEngine : IClientSkillMetadataRuntime
{
    private readonly ClientSkillMetadataCatalog _catalog;

    public ClientSkillMetadataEngine(ClientSkillMetadataCatalog catalog) =>
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    public OperationResult<ClientSkillMetadata> ResolveClientSkillMetadata(int officialClientItemId) =>
        _catalog.Resolve(officialClientItemId);

    public IReadOnlyList<ClientSkillMetadata> ResolveClientSkillMetadataForFormalSkill(long skillId) =>
        _catalog.ResolveForFormalSkill(skillId);
}
