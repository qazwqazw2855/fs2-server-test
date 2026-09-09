using System.Collections.ObjectModel;

namespace God2.ClassicServer.Runtime;

public sealed record PartyMemberState(
    Guid CharacterId,
    int MapId,
    int X,
    int Y,
    bool Online,
    long JoinedSequence);

public sealed record PartySnapshot(
    Guid PartyId,
    Guid LeaderId,
    IReadOnlyList<PartyMemberState> Members,
    IReadOnlyList<Guid> PendingInvites,
    IReadOnlyList<int> SharedQuestIds,
    bool Disbanded,
    long Version);

public enum PartyCommandType
{
    Invite,
    Accept,
    Reject,
    TransferLeader,
    Kick,
    Leave,
    Disband,
    UpdatePresence,
    ShareQuest
}

public enum PartyResultCode
{
    Success,
    DuplicateCompleted,
    ReplayConflict,
    NotLeader,
    NotMember,
    AlreadyMember,
    InviteMissing,
    DuplicateInvite,
    PartyFull,
    InvalidTarget,
    OfflineMember,
    DifferentMap,
    OutOfRange,
    Disbanded
}

public sealed record PartyCommand(
    string IdempotencyKey,
    PartyCommandType Type,
    Guid ActorId,
    Guid? TargetId = null,
    int? MapId = null,
    int? X = null,
    int? Y = null,
    bool? Online = null,
    int? QuestId = null);

public sealed record PartyResult(PartyResultCode ResultCode, PartySnapshot Snapshot, bool Mutated);

public sealed class PartyRuntime
{
    private readonly object _sync = new();
    private readonly int _capacity;
    private readonly Dictionary<Guid, PartyMemberState> _members = [];
    private readonly HashSet<Guid> _pendingInvites = [];
    private readonly HashSet<int> _sharedQuests = [];
    private readonly Dictionary<string, (string Fingerprint, PartyResult Result)> _completed = new(StringComparer.Ordinal);
    private Guid _leaderId;
    private bool _disbanded;
    private long _version;
    private long _joinSequence;

    public PartyRuntime(Guid partyId, PartyMemberState leader, int capacity = 4)
    {
        if (partyId == Guid.Empty || leader.CharacterId == Guid.Empty || capacity < 2)
        {
            throw new ArgumentException("Party identity, leader, and capacity are required.");
        }

        PartyId = partyId;
        _capacity = capacity;
        _leaderId = leader.CharacterId;
        _joinSequence = Math.Max(1, leader.JoinedSequence);
        _members[leader.CharacterId] = leader with { JoinedSequence = _joinSequence };
    }

    public PartyRuntime(PartySnapshot restored, int capacity = 4)
    {
        ArgumentNullException.ThrowIfNull(restored);
        if (restored.PartyId == Guid.Empty || capacity < 2 ||
            !restored.Disbanded && (restored.LeaderId == Guid.Empty || restored.Members.All(member => member.CharacterId != restored.LeaderId)))
        {
            throw new ArgumentException("Restored party snapshot is invalid.", nameof(restored));
        }
        PartyId = restored.PartyId;
        _capacity = capacity;
        _leaderId = restored.LeaderId;
        _members = restored.Members.ToDictionary(member => member.CharacterId);
        _pendingInvites = restored.PendingInvites.ToHashSet();
        _sharedQuests = restored.SharedQuestIds.ToHashSet();
        _disbanded = restored.Disbanded;
        _version = restored.Version;
        _joinSequence = restored.Members.Select(member => member.JoinedSequence).DefaultIfEmpty().Max();
    }

    public Guid PartyId { get; }

    public PartySnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return SnapshotCore();
            }
        }
    }

    public PartyResult Execute(PartyCommand command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.IdempotencyKey);
        lock (_sync)
        {
            var fingerprint = $"{command.Type}:{command.ActorId:N}:{command.TargetId:N}:{command.MapId}:{command.X}:{command.Y}:{command.Online}:{command.QuestId}";
            if (_completed.TryGetValue(command.IdempotencyKey, out var replay))
            {
                return replay.Fingerprint == fingerprint
                    ? replay.Result with { ResultCode = PartyResultCode.DuplicateCompleted, Mutated = false }
                    : new PartyResult(PartyResultCode.ReplayConflict, SnapshotCore(), false);
            }

            if (_disbanded)
            {
                return Complete(command.IdempotencyKey, fingerprint, PartyResultCode.Disbanded, false);
            }

            (PartyResultCode Code, bool Mutated) result = command.Type switch
            {
                PartyCommandType.Invite => Invite(command),
                PartyCommandType.Accept => Accept(command),
                PartyCommandType.Reject => Reject(command),
                PartyCommandType.TransferLeader => Transfer(command),
                PartyCommandType.Kick => Kick(command),
                PartyCommandType.Leave => Leave(command),
                PartyCommandType.Disband => Disband(command),
                PartyCommandType.UpdatePresence => UpdatePresence(command),
                PartyCommandType.ShareQuest => ShareQuest(command),
                _ => (PartyResultCode.InvalidTarget, false)
            };
            return Complete(command.IdempotencyKey, fingerprint, result.Code, result.Mutated);
        }
    }

    public PartyResultCode ValidateSupportTarget(Guid actorId, Guid targetId, int maximumDistance)
    {
        lock (_sync)
        {
            if (!_members.TryGetValue(actorId, out var actor) || !_members.TryGetValue(targetId, out var target))
            {
                return PartyResultCode.NotMember;
            }

            if (!target.Online)
            {
                return PartyResultCode.OfflineMember;
            }

            if (actor.MapId != target.MapId)
            {
                return PartyResultCode.DifferentMap;
            }

            var x = (long)actor.X - target.X;
            var y = (long)actor.Y - target.Y;
            return checked((x * x) + (y * y)) <= checked((long)maximumDistance * maximumDistance)
                ? PartyResultCode.Success
                : PartyResultCode.OutOfRange;
        }
    }

    public IReadOnlyDictionary<Guid, long> DistributeExperience(long totalExperience)
    {
        if (totalExperience < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalExperience));
        }

        lock (_sync)
        {
            var leader = _members[_leaderId];
            var eligible = _members.Values
                .Where(member => member.Online && member.MapId == leader.MapId)
                .OrderBy(member => member.CharacterId)
                .ToArray();
            if (eligible.Length == 0)
            {
                return new ReadOnlyDictionary<Guid, long>(new Dictionary<Guid, long>());
            }

            var share = totalExperience / eligible.Length;
            var remainder = totalExperience % eligible.Length;
            return new ReadOnlyDictionary<Guid, long>(eligible
                .Select((member, index) => (member.CharacterId, Amount: share + (index < remainder ? 1 : 0)))
                .ToDictionary(value => value.CharacterId, value => value.Amount));
        }
    }

    public IReadOnlyList<Guid> SharedEncounterParticipants(int mapId) =>
        Snapshot.Members.Where(member => member.Online && member.MapId == mapId).Select(member => member.CharacterId).ToArray();

    private (PartyResultCode Code, bool Mutated) Invite(PartyCommand command)
    {
        if (command.ActorId != _leaderId)
        {
            return (PartyResultCode.NotLeader, false);
        }

        if (command.TargetId is null || command.TargetId == Guid.Empty || command.TargetId == command.ActorId)
        {
            return (PartyResultCode.InvalidTarget, false);
        }

        if (_members.ContainsKey(command.TargetId.Value))
        {
            return (PartyResultCode.AlreadyMember, false);
        }

        if (_members.Count >= _capacity)
        {
            return (PartyResultCode.PartyFull, false);
        }

        return _pendingInvites.Add(command.TargetId.Value)
            ? (PartyResultCode.Success, Mutate())
            : (PartyResultCode.DuplicateInvite, false);
    }

    private (PartyResultCode Code, bool Mutated) Accept(PartyCommand command)
    {
        if (!_pendingInvites.Remove(command.ActorId))
        {
            return (PartyResultCode.InviteMissing, false);
        }

        if (_members.Count >= _capacity)
        {
            return (PartyResultCode.PartyFull, false);
        }

        var leader = _members[_leaderId];
        _members[command.ActorId] = new PartyMemberState(command.ActorId, leader.MapId, leader.X, leader.Y, true, ++_joinSequence);
        return (PartyResultCode.Success, Mutate());
    }

    private (PartyResultCode Code, bool Mutated) Reject(PartyCommand command) =>
        _pendingInvites.Remove(command.ActorId)
            ? (PartyResultCode.Success, Mutate())
            : (PartyResultCode.InviteMissing, false);

    private (PartyResultCode Code, bool Mutated) Transfer(PartyCommand command)
    {
        if (command.ActorId != _leaderId)
        {
            return (PartyResultCode.NotLeader, false);
        }

        if (command.TargetId is null || !_members.ContainsKey(command.TargetId.Value))
        {
            return (PartyResultCode.NotMember, false);
        }

        _leaderId = command.TargetId.Value;
        return (PartyResultCode.Success, Mutate());
    }

    private (PartyResultCode Code, bool Mutated) Kick(PartyCommand command)
    {
        if (command.ActorId != _leaderId)
        {
            return (PartyResultCode.NotLeader, false);
        }

        if (command.TargetId is null || command.TargetId == _leaderId || !_members.Remove(command.TargetId.Value))
        {
            return (PartyResultCode.InvalidTarget, false);
        }

        return (PartyResultCode.Success, Mutate());
    }

    private (PartyResultCode Code, bool Mutated) Leave(PartyCommand command)
    {
        if (!_members.Remove(command.ActorId))
        {
            return (PartyResultCode.NotMember, false);
        }

        if (_members.Count == 0)
        {
            _disbanded = true;
        }
        else if (command.ActorId == _leaderId)
        {
            _leaderId = _members.Values.OrderBy(member => member.JoinedSequence).First().CharacterId;
        }

        return (PartyResultCode.Success, Mutate());
    }

    private (PartyResultCode Code, bool Mutated) Disband(PartyCommand command)
    {
        if (command.ActorId != _leaderId)
        {
            return (PartyResultCode.NotLeader, false);
        }

        _members.Clear();
        _pendingInvites.Clear();
        _disbanded = true;
        return (PartyResultCode.Success, Mutate());
    }

    private (PartyResultCode Code, bool Mutated) UpdatePresence(PartyCommand command)
    {
        if (!_members.TryGetValue(command.ActorId, out var member) || command.MapId is null || command.X is null || command.Y is null || command.Online is null)
        {
            return (PartyResultCode.NotMember, false);
        }

        _members[command.ActorId] = member with
        {
            MapId = command.MapId.Value,
            X = command.X.Value,
            Y = command.Y.Value,
            Online = command.Online.Value
        };
        return (PartyResultCode.Success, Mutate());
    }

    private (PartyResultCode Code, bool Mutated) ShareQuest(PartyCommand command)
    {
        if (!_members.ContainsKey(command.ActorId))
        {
            return (PartyResultCode.NotMember, false);
        }

        if (command.QuestId is null || command.QuestId <= 0)
        {
            return (PartyResultCode.InvalidTarget, false);
        }

        return _sharedQuests.Add(command.QuestId.Value)
            ? (PartyResultCode.Success, Mutate())
            : (PartyResultCode.Success, false);
    }

    private bool Mutate()
    {
        _version++;
        return true;
    }

    private PartyResult Complete(string key, string fingerprint, PartyResultCode code, bool mutated)
    {
        var result = new PartyResult(code, SnapshotCore(), mutated);
        _completed[key] = (fingerprint, result);
        return result;
    }

    private PartySnapshot SnapshotCore() =>
        new(
            PartyId,
            _leaderId,
            _members.Values.OrderBy(member => member.JoinedSequence).ToArray(),
            _pendingInvites.Order().ToArray(),
            _sharedQuests.Order().ToArray(),
            _disbanded,
            _version);
}

public sealed record MountDefinition(
    int MountId,
    int SpeedBonusBasisPoints,
    int InitialLoyalty,
    int MinimumRideLoyalty,
    IReadOnlySet<int> RestrictedMapIds,
    string EvidenceConfidence);

public sealed record MountSnapshot(
    long CharacterId,
    IReadOnlySet<int> OwnedMountIds,
    IReadOnlyDictionary<int, int> Loyalty,
    int? EquippedMountId,
    bool Mounted,
    int SpeedModifierBasisPoints,
    long Version);

public enum MountCommandType
{
    Acquire,
    Equip,
    Unequip,
    MountOn,
    MountOff,
    DecreaseLoyalty
}

public enum MountResultCode
{
    Success,
    DuplicateCompleted,
    ReplayConflict,
    InvalidMount,
    NotOwned,
    AlreadyApplied,
    NotEquipped,
    LoyaltyTooLow,
    MapRestricted,
    BattleRestricted,
    InvalidAmount
}

public sealed record MountCommand(
    string IdempotencyKey,
    MountCommandType Type,
    int MountId,
    int MapId,
    bool InBattle,
    int Amount = 0);

public sealed record MountResult(MountResultCode ResultCode, MountSnapshot Snapshot, bool Mutated);

public sealed class MountRuntime
{
    private readonly object _sync = new();
    private readonly long _characterId;
    private readonly IReadOnlyDictionary<int, MountDefinition> _catalog;
    private readonly HashSet<int> _owned;
    private readonly Dictionary<int, int> _loyalty;
    private readonly Dictionary<string, (string Fingerprint, MountResult Result)> _completed = new(StringComparer.Ordinal);
    private int? _equipped;
    private bool _mounted;
    private long _version;

    public MountRuntime(long characterId, IEnumerable<MountDefinition> definitions, MountSnapshot? restored = null)
    {
        _characterId = characterId;
        _catalog = definitions.ToDictionary(definition => definition.MountId);
        if (restored is not null && restored.CharacterId != characterId)
        {
            throw new ArgumentException("Mount snapshot identity does not match.", nameof(restored));
        }

        _owned = restored?.OwnedMountIds.ToHashSet() ?? [];
        _loyalty = restored?.Loyalty.ToDictionary(pair => pair.Key, pair => pair.Value) ?? [];
        _equipped = restored?.EquippedMountId;
        _mounted = restored?.Mounted ?? false;
        _version = restored?.Version ?? 0;
    }

    public MountSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return SnapshotCore();
            }
        }
    }

    public MountResult Execute(MountCommand command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.IdempotencyKey);
        lock (_sync)
        {
            var fingerprint = $"{command.Type}:{command.MountId}:{command.MapId}:{command.InBattle}:{command.Amount}";
            if (_completed.TryGetValue(command.IdempotencyKey, out var replay))
            {
                return replay.Fingerprint == fingerprint
                    ? replay.Result with { ResultCode = MountResultCode.DuplicateCompleted, Mutated = false }
                    : new MountResult(MountResultCode.ReplayConflict, SnapshotCore(), false);
            }

            var result = Apply(command);
            var completed = new MountResult(result.Code, SnapshotCore(), result.Mutated);
            _completed[command.IdempotencyKey] = (fingerprint, completed);
            return completed;
        }
    }

    private (MountResultCode Code, bool Mutated) Apply(MountCommand command)
    {
        if (!_catalog.TryGetValue(command.MountId, out var definition))
        {
            return (MountResultCode.InvalidMount, false);
        }

        switch (command.Type)
        {
            case MountCommandType.Acquire:
                if (!_owned.Add(command.MountId))
                {
                    return (MountResultCode.AlreadyApplied, false);
                }
                _loyalty[command.MountId] = definition.InitialLoyalty;
                return (MountResultCode.Success, Mutate());
            case MountCommandType.Equip:
                if (!_owned.Contains(command.MountId))
                {
                    return (MountResultCode.NotOwned, false);
                }
                if (_equipped == command.MountId)
                {
                    return (MountResultCode.AlreadyApplied, false);
                }
                _equipped = command.MountId;
                _mounted = false;
                return (MountResultCode.Success, Mutate());
            case MountCommandType.Unequip:
                if (_equipped != command.MountId)
                {
                    return (MountResultCode.NotEquipped, false);
                }
                _equipped = null;
                _mounted = false;
                return (MountResultCode.Success, Mutate());
            case MountCommandType.MountOn:
                if (_equipped != command.MountId)
                {
                    return (MountResultCode.NotEquipped, false);
                }
                if (command.InBattle)
                {
                    return (MountResultCode.BattleRestricted, false);
                }
                if (definition.RestrictedMapIds.Contains(command.MapId))
                {
                    return (MountResultCode.MapRestricted, false);
                }
                if (_loyalty.GetValueOrDefault(command.MountId) < definition.MinimumRideLoyalty)
                {
                    return (MountResultCode.LoyaltyTooLow, false);
                }
                if (_mounted)
                {
                    return (MountResultCode.AlreadyApplied, false);
                }
                _mounted = true;
                return (MountResultCode.Success, Mutate());
            case MountCommandType.MountOff:
                if (!_mounted)
                {
                    return (MountResultCode.AlreadyApplied, false);
                }
                _mounted = false;
                return (MountResultCode.Success, Mutate());
            case MountCommandType.DecreaseLoyalty:
                if (!_owned.Contains(command.MountId))
                {
                    return (MountResultCode.NotOwned, false);
                }
                if (command.Amount <= 0)
                {
                    return (MountResultCode.InvalidAmount, false);
                }
                _loyalty[command.MountId] = Math.Max(0, _loyalty.GetValueOrDefault(command.MountId) - command.Amount);
                if (_loyalty[command.MountId] < definition.MinimumRideLoyalty)
                {
                    _mounted = false;
                }
                return (MountResultCode.Success, Mutate());
            default:
                return (MountResultCode.InvalidMount, false);
        }
    }

    private bool Mutate()
    {
        _version++;
        return true;
    }

    private MountSnapshot SnapshotCore()
    {
        var speed = _mounted && _equipped is not null && _catalog.TryGetValue(_equipped.Value, out var definition)
            ? definition.SpeedBonusBasisPoints
            : 10_000;
        return new MountSnapshot(
            _characterId,
            new HashSet<int>(_owned),
            new ReadOnlyDictionary<int, int>(new Dictionary<int, int>(_loyalty)),
            _equipped,
            _mounted,
            speed,
            _version);
    }
}

public sealed record PetDefinition(int PetId, int WildEncounterLevel, int InitialLoyalty, IReadOnlyList<int> SkillIds, string EvidenceConfidence);

public static class PetLevelRules
{
    public const int PlayerInitialLevel = 1;
    public const int MaximumLevel = 99;

    public static long RequiredExperienceForNextLevel(int level)
    {
        if (level is < PlayerInitialLevel or >= MaximumLevel)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }
        return checked(100L * level * level);
    }

    public static int ResolvePlayerLevel(long experience)
    {
        var remaining = Math.Max(0, experience);
        var level = PlayerInitialLevel;
        while (level < MaximumLevel)
        {
            var required = RequiredExperienceForNextLevel(level);
            if (remaining < required)
            {
                break;
            }
            remaining -= required;
            level++;
        }
        return level;
    }
}

public enum PetActivityState
{
    Withdrawn,
    Deployed,
    Walking,
    Dead
}

public sealed record PetState(int PetId, int Level, long Experience, int Loyalty, PetActivityState State, long Version);

public sealed record PetSnapshot(long CharacterId, IReadOnlyDictionary<int, PetState> Pets, int? ActivePetId, long Version);

public enum PetCommandType
{
    Acquire,
    Deploy,
    Withdraw,
    Walk,
    Recall,
    MarkDead,
    GrantExperience,
    DecreaseLoyalty
}

public enum PetAcquisitionOrigin
{
    LevelOnePet,
    CapturedWild
}

public enum PetResultCode
{
    Success,
    DuplicateCompleted,
    ReplayConflict,
    InvalidPet,
    NotOwned,
    AlreadyApplied,
    ActiveSlotOccupied,
    PetDead,
    InvalidAmount
}

public sealed record PetCommand(
    string IdempotencyKey,
    PetCommandType Type,
    int PetId,
    long Amount = 0,
    PetAcquisitionOrigin AcquisitionOrigin = PetAcquisitionOrigin.LevelOnePet,
    int? WildEncounterLevel = null);

public sealed record PetResult(PetResultCode ResultCode, PetSnapshot Snapshot, bool Mutated);

public sealed class PetRuntime
{
    private readonly object _sync = new();
    private readonly long _characterId;
    private readonly IReadOnlyDictionary<int, PetDefinition> _catalog;
    private readonly Dictionary<int, PetState> _pets;
    private readonly Dictionary<string, (string Fingerprint, PetResult Result)> _completed = new(StringComparer.Ordinal);
    private int? _activePetId;
    private long _version;

    public PetRuntime(long characterId, IEnumerable<PetDefinition> definitions, PetSnapshot? restored = null)
    {
        _characterId = characterId;
        _catalog = definitions.ToDictionary(definition => definition.PetId);
        if (restored is not null && restored.CharacterId != characterId)
        {
            throw new ArgumentException("Pet snapshot identity does not match.", nameof(restored));
        }
        _pets = restored?.Pets.ToDictionary(pair => pair.Key, pair => pair.Value) ?? [];
        _activePetId = restored?.ActivePetId;
        _version = restored?.Version ?? 0;
    }

    public PetSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return SnapshotCore();
            }
        }
    }

    public PetResult Execute(PetCommand command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.IdempotencyKey);
        lock (_sync)
        {
            var fingerprint = $"{command.Type}:{command.PetId}:{command.Amount}:{command.AcquisitionOrigin}:{command.WildEncounterLevel}";
            if (_completed.TryGetValue(command.IdempotencyKey, out var replay))
            {
                return replay.Fingerprint == fingerprint
                    ? replay.Result with { ResultCode = PetResultCode.DuplicateCompleted, Mutated = false }
                    : new PetResult(PetResultCode.ReplayConflict, SnapshotCore(), false);
            }

            var result = Apply(command);
            var completed = new PetResult(result.Code, SnapshotCore(), result.Mutated);
            _completed[command.IdempotencyKey] = (fingerprint, completed);
            return completed;
        }
    }

    private (PetResultCode Code, bool Mutated) Apply(PetCommand command)
    {
        if (!_catalog.TryGetValue(command.PetId, out var definition))
        {
            return (PetResultCode.InvalidPet, false);
        }

        if (command.Type == PetCommandType.Acquire)
        {
            if (_pets.ContainsKey(command.PetId))
            {
                return (PetResultCode.AlreadyApplied, false);
            }
            var initialLevel = command.AcquisitionOrigin switch
            {
                PetAcquisitionOrigin.LevelOnePet when command.WildEncounterLevel is null => PetLevelRules.PlayerInitialLevel,
                PetAcquisitionOrigin.CapturedWild when command.WildEncounterLevel is >= 1 and <= PetLevelRules.MaximumLevel => command.WildEncounterLevel.Value,
                _ => 0
            };
            if (initialLevel == 0)
            {
                return (PetResultCode.InvalidAmount, false);
            }
            _pets[command.PetId] = new PetState(command.PetId, initialLevel, 0, definition.InitialLoyalty, PetActivityState.Withdrawn, 1);
            return (PetResultCode.Success, Mutate());
        }

        if (!_pets.TryGetValue(command.PetId, out var pet))
        {
            return (PetResultCode.NotOwned, false);
        }

        switch (command.Type)
        {
            case PetCommandType.Deploy:
                if (pet.State == PetActivityState.Dead)
                {
                    return (PetResultCode.PetDead, false);
                }
                if (_activePetId is not null && _activePetId != command.PetId)
                {
                    return (PetResultCode.ActiveSlotOccupied, false);
                }
                _activePetId = command.PetId;
                return Update(pet with { State = PetActivityState.Deployed, Version = pet.Version + 1 });
            case PetCommandType.Withdraw:
                _activePetId = _activePetId == command.PetId ? null : _activePetId;
                return Update(pet with { State = PetActivityState.Withdrawn, Version = pet.Version + 1 });
            case PetCommandType.Walk:
                if (pet.State == PetActivityState.Dead)
                {
                    return (PetResultCode.PetDead, false);
                }
                return Update(pet with { State = PetActivityState.Walking, Version = pet.Version + 1 });
            case PetCommandType.Recall:
                return Update(pet with { State = PetActivityState.Withdrawn, Version = pet.Version + 1 });
            case PetCommandType.MarkDead:
                _activePetId = _activePetId == command.PetId ? null : _activePetId;
                return Update(pet with { State = PetActivityState.Dead, Version = pet.Version + 1 });
            case PetCommandType.GrantExperience:
                if (command.Amount <= 0)
                {
                    return (PetResultCode.InvalidAmount, false);
                }
                var experience = checked(pet.Experience + command.Amount);
                var level = Math.Max(pet.Level, PetLevelRules.ResolvePlayerLevel(experience));
                return Update(pet with { Experience = experience, Level = level, Version = pet.Version + 1 });
            case PetCommandType.DecreaseLoyalty:
                if (command.Amount <= 0)
                {
                    return (PetResultCode.InvalidAmount, false);
                }
                return Update(pet with { Loyalty = Math.Max(0, pet.Loyalty - (int)Math.Min(int.MaxValue, command.Amount)), Version = pet.Version + 1 });
            default:
                return (PetResultCode.InvalidPet, false);
        }
    }

    private (PetResultCode Code, bool Mutated) Update(PetState state)
    {
        _pets[state.PetId] = state;
        return (PetResultCode.Success, Mutate());
    }

    private bool Mutate()
    {
        _version++;
        return true;
    }

    private PetSnapshot SnapshotCore() =>
        new(
            _characterId,
            new ReadOnlyDictionary<int, PetState>(new Dictionary<int, PetState>(_pets)),
            _activePetId,
            _version);
}
