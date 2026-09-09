using System.Collections.ObjectModel;
using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Runtime;

public sealed class ServerOwnedPartyDirectory
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, PartyRuntime> _parties = [];
    private readonly Dictionary<Guid, Guid> _partyByCharacter = [];
    private readonly int _capacity;

    public ServerOwnedPartyDirectory(int capacity = 4)
    {
        if (capacity is < 2 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
    }

    public OperationResult<PartySnapshot> CreateParty(Guid partyId, PartyMemberState leader)
    {
        if (partyId == Guid.Empty || leader.CharacterId == Guid.Empty)
        {
            return OperationResult<PartySnapshot>.Failure(
                "party.invalid_identity",
                "Party and leader identities are required.");
        }

        lock (_sync)
        {
            if (_parties.TryGetValue(partyId, out var existing))
            {
                return existing.Snapshot.LeaderId == leader.CharacterId
                    ? OperationResult<PartySnapshot>.Success(existing.Snapshot)
                    : OperationResult<PartySnapshot>.Failure(
                        "party.identity_conflict",
                        "The party identifier is already owned by another leader.");
            }

            if (_partyByCharacter.ContainsKey(leader.CharacterId))
            {
                return OperationResult<PartySnapshot>.Failure(
                    "party.already_joined",
                    "The leader already belongs to a party.");
            }

            var party = new PartyRuntime(partyId, leader, _capacity);
            _parties.Add(partyId, party);
            _partyByCharacter.Add(leader.CharacterId, partyId);
            return OperationResult<PartySnapshot>.Success(party.Snapshot);
        }
    }

    public OperationResult<PartyResult> Execute(Guid partyId, PartyCommand command)
    {
        lock (_sync)
        {
            if (!_parties.TryGetValue(partyId, out var party))
            {
                return OperationResult<PartyResult>.Failure(
                    "party.not_found",
                    "The party does not exist.");
            }

            if (command.Type == PartyCommandType.Accept &&
                _partyByCharacter.TryGetValue(command.ActorId, out var currentPartyId) &&
                currentPartyId != partyId)
            {
                return OperationResult<PartyResult>.Failure(
                    "party.already_joined",
                    "The invited character already belongs to another party.");
            }

            var previous = party.Snapshot;
            var result = party.Execute(command);
            if (result.ResultCode is PartyResultCode.Success or PartyResultCode.DuplicateCompleted)
            {
                SynchronizeMembership(partyId, previous, result.Snapshot);
            }

            return OperationResult<PartyResult>.Success(result);
        }
    }

    public Guid? ResolvePartyId(Guid characterId)
    {
        lock (_sync)
        {
            return _partyByCharacter.GetValueOrDefault(characterId) is var partyId && partyId != Guid.Empty
                ? partyId
                : null;
        }
    }

    public OperationResult<PartySnapshot> FindByCharacter(Guid characterId)
    {
        lock (_sync)
        {
            if (!_partyByCharacter.TryGetValue(characterId, out var partyId) ||
                !_parties.TryGetValue(partyId, out var party))
            {
                return OperationResult<PartySnapshot>.Failure(
                    "party.not_joined",
                    "The character does not belong to a party.");
            }

            return OperationResult<PartySnapshot>.Success(party.Snapshot);
        }
    }

    private void SynchronizeMembership(Guid partyId, PartySnapshot previous, PartySnapshot current)
    {
        var currentMembers = current.Members.Select(member => member.CharacterId).ToHashSet();
        foreach (var removed in previous.Members.Where(member => !currentMembers.Contains(member.CharacterId)))
        {
            _partyByCharacter.Remove(removed.CharacterId);
        }

        foreach (var member in current.Members)
        {
            _partyByCharacter[member.CharacterId] = partyId;
        }
    }
}

public enum ServerChatChannel
{
    World,
    Party,
    Whisper
}

public enum ServerChatResultCode
{
    Success,
    DuplicateCompleted,
    ReplayConflict,
    SenderOffline,
    TargetOffline,
    PartyRequired,
    EmptyMessage,
    MessageTooLong,
    InvalidCharacters,
    RateLimited
}

public sealed record ServerChatParticipant(Guid CharacterId, string DisplayName, bool Online);

public sealed record ServerChatCommand(
    string IdempotencyKey,
    Guid SenderId,
    ServerChatChannel Channel,
    string Text,
    Guid? TargetId = null);

public sealed record ServerChatMessage(
    long Sequence,
    ServerChatChannel Channel,
    Guid SenderId,
    string SenderDisplayName,
    Guid? TargetId,
    Guid? PartyId,
    string Text,
    DateTimeOffset SentAtUtc);

public sealed record ServerChatDelivery(Guid RecipientId, ServerChatMessage Message);

public sealed record ServerChatResult(
    ServerChatResultCode ResultCode,
    ServerChatMessage? Message,
    IReadOnlyList<ServerChatDelivery> Deliveries,
    bool Mutated);

public sealed class ServerOwnedChatRuntime
{
    private readonly object _sync = new();
    private readonly Func<Guid, Guid?> _partyResolver;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Dictionary<Guid, ServerChatParticipant> _participants = [];
    private readonly Dictionary<Guid, List<ServerChatMessage>> _inboxes = [];
    private readonly Dictionary<Guid, Queue<DateTimeOffset>> _acceptedMessageTimes = [];
    private readonly Dictionary<string, (string Fingerprint, ServerChatResult Result)> _completed = new(StringComparer.Ordinal);
    private readonly Queue<string> _completedOrder = [];
    private readonly int _maximumTextLength;
    private readonly int _burstLimit;
    private readonly TimeSpan _burstWindow;
    private readonly int _maximumInboxMessagesPerParticipant;
    private readonly int _maximumCompletedCommands;
    private long _sequence;

    public ServerOwnedChatRuntime(
        Func<Guid, Guid?> partyResolver,
        int maximumTextLength = 120,
        int burstLimit = 5,
        TimeSpan? burstWindow = null,
        Func<DateTimeOffset>? utcNow = null,
        int maximumInboxMessagesPerParticipant = 200,
        int maximumCompletedCommands = 10_000)
    {
        _partyResolver = partyResolver ?? throw new ArgumentNullException(nameof(partyResolver));
        if (maximumTextLength is < 1 or > 1_000 || burstLimit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumTextLength));
        }

        _maximumTextLength = maximumTextLength;
        _burstLimit = burstLimit;
        _burstWindow = burstWindow ?? TimeSpan.FromSeconds(10);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        if (_burstWindow <= TimeSpan.Zero ||
            maximumInboxMessagesPerParticipant < 1 ||
            maximumCompletedCommands < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumInboxMessagesPerParticipant));
        }

        _maximumInboxMessagesPerParticipant = maximumInboxMessagesPerParticipant;
        _maximumCompletedCommands = maximumCompletedCommands;
    }

    public int ParticipantCount
    {
        get
        {
            lock (_sync)
            {
                return _participants.Count;
            }
        }
    }

    public int CompletedCommandCount
    {
        get
        {
            lock (_sync)
            {
                return _completed.Count;
            }
        }
    }

    public void RegisterOrUpdate(ServerChatParticipant participant)
    {
        if (participant.CharacterId == Guid.Empty || string.IsNullOrWhiteSpace(participant.DisplayName))
        {
            throw new ArgumentException("Chat participant identity and display name are required.", nameof(participant));
        }

        lock (_sync)
        {
            _participants[participant.CharacterId] = participant with { DisplayName = participant.DisplayName.Trim() };
            _inboxes.TryAdd(participant.CharacterId, []);
        }
    }

    public void Unregister(Guid characterId)
    {
        if (characterId == Guid.Empty)
        {
            return;
        }

        lock (_sync)
        {
            _participants.Remove(characterId);
            _inboxes.Remove(characterId);
            _acceptedMessageTimes.Remove(characterId);
        }
    }

    public ServerChatResult Send(ServerChatCommand command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.IdempotencyKey);
        var normalizedText = command.Text?.Trim() ?? string.Empty;
        var fingerprint = $"{command.SenderId:N}:{command.Channel}:{command.TargetId:N}:{normalizedText}";

        lock (_sync)
        {
            if (_completed.TryGetValue(command.IdempotencyKey, out var replay))
            {
                return replay.Fingerprint == fingerprint
                    ? replay.Result with { ResultCode = ServerChatResultCode.DuplicateCompleted, Mutated = false }
                    : new ServerChatResult(ServerChatResultCode.ReplayConflict, null, [], false);
            }

            if (!_participants.TryGetValue(command.SenderId, out var sender) || !sender.Online)
            {
                return Complete(command.IdempotencyKey, fingerprint, ServerChatResultCode.SenderOffline);
            }

            if (normalizedText.Length == 0)
            {
                return Complete(command.IdempotencyKey, fingerprint, ServerChatResultCode.EmptyMessage);
            }

            if (normalizedText.Length > _maximumTextLength)
            {
                return Complete(command.IdempotencyKey, fingerprint, ServerChatResultCode.MessageTooLong);
            }

            if (normalizedText.Any(character => char.IsControl(character)))
            {
                return Complete(command.IdempotencyKey, fingerprint, ServerChatResultCode.InvalidCharacters);
            }

            var recipients = ResolveRecipients(command, out var failure, out var partyId);
            if (failure is not null)
            {
                return Complete(command.IdempotencyKey, fingerprint, failure.Value);
            }

            var acceptedTimes = _acceptedMessageTimes.GetValueOrDefault(command.SenderId);
            if (acceptedTimes is null)
            {
                acceptedTimes = new Queue<DateTimeOffset>();
                _acceptedMessageTimes.Add(command.SenderId, acceptedTimes);
            }
            var now = _utcNow();
            var cutoff = now - _burstWindow;
            while (acceptedTimes.TryPeek(out var submittedAt) && submittedAt <= cutoff)
            {
                acceptedTimes.Dequeue();
            }
            if (acceptedTimes.Count >= _burstLimit)
            {
                return Complete(command.IdempotencyKey, fingerprint, ServerChatResultCode.RateLimited);
            }

            acceptedTimes.Enqueue(now);
            var message = new ServerChatMessage(
                ++_sequence,
                command.Channel,
                command.SenderId,
                sender.DisplayName,
                command.TargetId,
                partyId,
                normalizedText,
                now);
            var deliveries = recipients
                .Order()
                .Select(recipientId => new ServerChatDelivery(recipientId, message))
                .ToArray();
            foreach (var delivery in deliveries)
            {
                var inbox = _inboxes[delivery.RecipientId];
                inbox.Add(message);
                if (inbox.Count > _maximumInboxMessagesPerParticipant)
                {
                    inbox.RemoveRange(0, inbox.Count - _maximumInboxMessagesPerParticipant);
                }
            }

            var result = new ServerChatResult(ServerChatResultCode.Success, message, deliveries, true);
            return RetainCompleted(command.IdempotencyKey, fingerprint, result);
        }
    }

    public IReadOnlyList<ServerChatMessage> Inbox(Guid characterId)
    {
        lock (_sync)
        {
            return _inboxes.TryGetValue(characterId, out var messages)
                ? Array.AsReadOnly(messages.ToArray())
                : [];
        }
    }

    private HashSet<Guid> ResolveRecipients(
        ServerChatCommand command,
        out ServerChatResultCode? failure,
        out Guid? partyId)
    {
        failure = null;
        partyId = null;
        if (command.Channel == ServerChatChannel.World)
        {
            return _participants.Values.Where(value => value.Online).Select(value => value.CharacterId).ToHashSet();
        }

        if (command.Channel == ServerChatChannel.Party)
        {
            partyId = _partyResolver(command.SenderId);
            if (partyId is null)
            {
                failure = ServerChatResultCode.PartyRequired;
                return [];
            }

            var resolvedPartyId = partyId.Value;
            return _participants.Values
                .Where(value => value.Online && _partyResolver(value.CharacterId) == resolvedPartyId)
                .Select(value => value.CharacterId)
                .ToHashSet();
        }

        if (command.TargetId is null ||
            !_participants.TryGetValue(command.TargetId.Value, out var target) ||
            !target.Online)
        {
            failure = ServerChatResultCode.TargetOffline;
            return [];
        }

        return [command.SenderId, target.CharacterId];
    }

    private ServerChatResult Complete(string key, string fingerprint, ServerChatResultCode code)
    {
        var result = new ServerChatResult(code, null, [], false);
        return RetainCompleted(key, fingerprint, result);
    }

    private ServerChatResult RetainCompleted(string key, string fingerprint, ServerChatResult result)
    {
        _completed[key] = (fingerprint, result);
        _completedOrder.Enqueue(key);
        while (_completedOrder.Count > _maximumCompletedCommands &&
               _completedOrder.TryDequeue(out var expiredKey))
        {
            _completed.Remove(expiredKey);
        }

        return result;
    }
}

public enum ServerPkDuelState
{
    Requested,
    Active,
    Rejected,
    Cancelled,
    Completed
}

public enum ServerPkCommandType
{
    Request,
    Accept,
    Reject,
    Cancel,
    ReportDefeat
}

public enum ServerPkResultCode
{
    Success,
    DuplicateCompleted,
    ReplayConflict,
    ParticipantUnavailable,
    InvalidTarget,
    SafeMap,
    DifferentMap,
    AlreadyInDuel,
    DuelNotFound,
    InvalidState,
    NotAuthorized
}

public sealed record ServerPkParticipant(Guid CharacterId, int MapId, bool Online, bool Alive);

public sealed record ServerPkDuelSnapshot(
    Guid DuelId,
    Guid ChallengerId,
    Guid TargetId,
    int MapId,
    ServerPkDuelState State,
    Guid? WinnerId,
    long Version);

public sealed record ServerPkCommand(
    string IdempotencyKey,
    ServerPkCommandType Type,
    Guid DuelId,
    Guid ActorId,
    Guid? TargetId = null);

public sealed record ServerPkResult(ServerPkResultCode ResultCode, ServerPkDuelSnapshot? Duel, bool Mutated);

public sealed class ServerOwnedPkRuntime
{
    private readonly object _sync = new();
    private readonly HashSet<int> _safeMapIds;
    private readonly Dictionary<Guid, ServerPkParticipant> _participants = [];
    private readonly Dictionary<Guid, ServerPkDuelSnapshot> _duels = [];
    private readonly Dictionary<Guid, Guid> _activeDuelByCharacter = [];
    private readonly Dictionary<string, (string Fingerprint, ServerPkResult Result)> _completed = new(StringComparer.Ordinal);

    public ServerOwnedPkRuntime(IEnumerable<int>? safeMapIds = null)
    {
        _safeMapIds = safeMapIds?.ToHashSet() ?? [];
    }

    public void RegisterOrUpdate(ServerPkParticipant participant)
    {
        if (participant.CharacterId == Guid.Empty)
        {
            throw new ArgumentException("PK participant identity is required.", nameof(participant));
        }

        lock (_sync)
        {
            _participants[participant.CharacterId] = participant;
        }
    }

    public ServerPkResult Execute(ServerPkCommand command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.IdempotencyKey);
        var fingerprint = $"{command.Type}:{command.DuelId:N}:{command.ActorId:N}:{command.TargetId:N}";
        lock (_sync)
        {
            if (_completed.TryGetValue(command.IdempotencyKey, out var replay))
            {
                return replay.Fingerprint == fingerprint
                    ? replay.Result with { ResultCode = ServerPkResultCode.DuplicateCompleted, Mutated = false }
                    : new ServerPkResult(ServerPkResultCode.ReplayConflict, null, false);
            }

            var result = command.Type == ServerPkCommandType.Request
                ? Request(command)
                : MutateExisting(command);
            _completed[command.IdempotencyKey] = (fingerprint, result);
            return result;
        }
    }

    private ServerPkResult Request(ServerPkCommand command)
    {
        if (command.DuelId == Guid.Empty || command.TargetId is null || command.TargetId == command.ActorId)
        {
            return new ServerPkResult(ServerPkResultCode.InvalidTarget, null, false);
        }
        if (_duels.TryGetValue(command.DuelId, out var existing))
        {
            return new ServerPkResult(ServerPkResultCode.InvalidState, existing, false);
        }
        if (!TryAvailable(command.ActorId, out var challenger) || !TryAvailable(command.TargetId.Value, out var target))
        {
            return new ServerPkResult(ServerPkResultCode.ParticipantUnavailable, null, false);
        }
        if (challenger.MapId != target.MapId)
        {
            return new ServerPkResult(ServerPkResultCode.DifferentMap, null, false);
        }
        if (_safeMapIds.Contains(challenger.MapId))
        {
            return new ServerPkResult(ServerPkResultCode.SafeMap, null, false);
        }
        if (_activeDuelByCharacter.ContainsKey(challenger.CharacterId) || _activeDuelByCharacter.ContainsKey(target.CharacterId))
        {
            return new ServerPkResult(ServerPkResultCode.AlreadyInDuel, null, false);
        }

        var duel = new ServerPkDuelSnapshot(
            command.DuelId,
            challenger.CharacterId,
            target.CharacterId,
            challenger.MapId,
            ServerPkDuelState.Requested,
            null,
            1);
        _duels.Add(duel.DuelId, duel);
        _activeDuelByCharacter.Add(challenger.CharacterId, duel.DuelId);
        _activeDuelByCharacter.Add(target.CharacterId, duel.DuelId);
        return new ServerPkResult(ServerPkResultCode.Success, duel, true);
    }

    private ServerPkResult MutateExisting(ServerPkCommand command)
    {
        if (!_duels.TryGetValue(command.DuelId, out var duel))
        {
            return new ServerPkResult(ServerPkResultCode.DuelNotFound, null, false);
        }

        ServerPkDuelSnapshot? changed = null;
        switch (command.Type)
        {
            case ServerPkCommandType.Accept:
                if (duel.State != ServerPkDuelState.Requested)
                {
                    return new ServerPkResult(ServerPkResultCode.InvalidState, duel, false);
                }
                if (command.ActorId != duel.TargetId)
                {
                    return new ServerPkResult(ServerPkResultCode.NotAuthorized, duel, false);
                }
                if (!TryAvailable(duel.ChallengerId, out var challenger) ||
                    !TryAvailable(duel.TargetId, out var target) ||
                    challenger.MapId != duel.MapId || target.MapId != duel.MapId)
                {
                    return new ServerPkResult(ServerPkResultCode.ParticipantUnavailable, duel, false);
                }
                changed = duel with { State = ServerPkDuelState.Active, Version = duel.Version + 1 };
                break;
            case ServerPkCommandType.Reject:
                if (duel.State != ServerPkDuelState.Requested || command.ActorId != duel.TargetId)
                {
                    return new ServerPkResult(ServerPkResultCode.NotAuthorized, duel, false);
                }
                changed = duel with { State = ServerPkDuelState.Rejected, Version = duel.Version + 1 };
                break;
            case ServerPkCommandType.Cancel:
                if (duel.State != ServerPkDuelState.Requested || command.ActorId != duel.ChallengerId)
                {
                    return new ServerPkResult(ServerPkResultCode.NotAuthorized, duel, false);
                }
                changed = duel with { State = ServerPkDuelState.Cancelled, Version = duel.Version + 1 };
                break;
            case ServerPkCommandType.ReportDefeat:
                if (duel.State != ServerPkDuelState.Active || command.TargetId is null ||
                    (command.ActorId != duel.ChallengerId && command.ActorId != duel.TargetId) ||
                    (command.TargetId != duel.ChallengerId && command.TargetId != duel.TargetId) ||
                    command.ActorId == command.TargetId)
                {
                    return new ServerPkResult(ServerPkResultCode.NotAuthorized, duel, false);
                }
                changed = duel with
                {
                    State = ServerPkDuelState.Completed,
                    WinnerId = command.ActorId,
                    Version = duel.Version + 1
                };
                break;
        }

        if (changed is null)
        {
            return new ServerPkResult(ServerPkResultCode.InvalidState, duel, false);
        }

        _duels[duel.DuelId] = changed;
        if (changed.State is ServerPkDuelState.Rejected or ServerPkDuelState.Cancelled or ServerPkDuelState.Completed)
        {
            _activeDuelByCharacter.Remove(duel.ChallengerId);
            _activeDuelByCharacter.Remove(duel.TargetId);
        }
        return new ServerPkResult(ServerPkResultCode.Success, changed, true);
    }

    private bool TryAvailable(Guid characterId, out ServerPkParticipant participant)
    {
        if (_participants.TryGetValue(characterId, out var found) && found.Online && found.Alive)
        {
            participant = found;
            return true;
        }

        participant = default!;
        return false;
    }
}

public sealed class ServerOwnedMultiplayerRuntime
{
    public ServerOwnedMultiplayerRuntime(
        IPetLifecycleCoordinator petLifecycle,
        int partyCapacity = 4,
        IEnumerable<int>? pkSafeMapIds = null,
        IPlayerSocialRepository? playerSocialRepository = null)
    {
        PetLifecycle = petLifecycle ?? throw new ArgumentNullException(nameof(petLifecycle));
        Parties = new ServerOwnedPartyDirectory(partyCapacity);
        Chat = new ServerOwnedChatRuntime(Parties.ResolvePartyId);
        Pk = new ServerOwnedPkRuntime(pkSafeMapIds);
        NameCards = new ServerOwnedNameCardRuntime(playerSocialRepository ?? new InMemoryPlayerSocialRepository());
    }

    public ServerOwnedPartyDirectory Parties { get; }

    public ServerOwnedChatRuntime Chat { get; }

    public ServerOwnedPkRuntime Pk { get; }

    public ServerOwnedNameCardRuntime NameCards { get; }

    public IPetLifecycleCoordinator PetLifecycle { get; }
}
