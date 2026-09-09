namespace God2.ClassicServer.Runtime;

public enum PlayerRelationshipKind
{
    NameCard
}

public enum SocialInvitationKind
{
    NameCard
}

public enum SocialInvitationState
{
    Pending,
    Accepted,
    Rejected,
    Cancelled,
    Expired
}

public enum SocialOperationResultCode
{
    Success,
    DuplicateCompleted,
    ReplayConflict,
    InvalidIdentity,
    InvalidRequestId,
    SameCharacter,
    SenderOffline,
    TargetOffline,
    BlockedByActor,
    BlockedByTarget,
    InvitationPending,
    InvitationNotFound,
    NotRecipient,
    InvalidState,
    AlreadyConnected
}

public sealed record SocialParticipant(long CharacterId, string DisplayName, bool Online);

public sealed record SocialInvitationSnapshot(
    Guid InvitationId,
    SocialInvitationKind Kind,
    long RequesterId,
    long TargetId,
    SocialInvitationState State,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? RespondedAtUtc,
    long Version);

public sealed record PlayerRelationshipSnapshot(
    PlayerRelationshipKind Kind,
    long CharacterId,
    long RelatedCharacterId,
    DateTimeOffset CreatedAtUtc,
    long Version);

public sealed record SocialOperationResult(
    SocialOperationResultCode ResultCode,
    SocialInvitationSnapshot? Invitation,
    PlayerRelationshipSnapshot? Relationship,
    bool Mutated,
    bool? Blocked = null);

public sealed record NameCardRequestCommand(
    string IdempotencyKey,
    Guid InvitationId,
    long RequesterId,
    long TargetId);

public sealed record NameCardResponseCommand(
    string IdempotencyKey,
    Guid InvitationId,
    long ActorId,
    bool Accept);

public sealed record SocialBlockCommand(
    string IdempotencyKey,
    long ActorId,
    long TargetId,
    bool Blocked);

public interface IPlayerSocialRepository
{
    Task<SocialOperationResult> RequestNameCardAsync(
        NameCardRequestCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<SocialOperationResult> RespondToNameCardAsync(
        NameCardResponseCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<SocialOperationResult> SetBlockAsync(
        SocialBlockCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PlayerRelationshipSnapshot>> ListRelationshipsAsync(
        long characterId,
        PlayerRelationshipKind kind,
        CancellationToken cancellationToken);

    Task<bool> IsBlockedAsync(long actorId, long targetId, CancellationToken cancellationToken);
}

public sealed class InMemoryPlayerSocialRepository : IPlayerSocialRepository
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, SocialInvitationSnapshot> _invitations = [];
    private readonly Dictionary<(long Low, long High, PlayerRelationshipKind Kind), PlayerRelationshipSnapshot> _relationships = [];
    private readonly HashSet<(long ActorId, long TargetId)> _blocks = [];
    private readonly Dictionary<(long ActorId, string Key), (string Fingerprint, SocialOperationResult Result)> _completed = [];

    public Task<SocialOperationResult> RequestNameCardAsync(
        NameCardRequestCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fingerprint = $"NameCardRequest:{command.InvitationId:N}:{command.RequesterId}:{command.TargetId}";
        lock (_sync)
        {
            if (TryReplay(command.RequesterId, command.IdempotencyKey, fingerprint, out var replay))
            {
                return Task.FromResult(replay);
            }

            var pair = Pair(command.RequesterId, command.TargetId, PlayerRelationshipKind.NameCard);
            SocialOperationResult result;
            if (_blocks.Contains((command.RequesterId, command.TargetId)))
            {
                result = Result(SocialOperationResultCode.BlockedByActor);
            }
            else if (_blocks.Contains((command.TargetId, command.RequesterId)))
            {
                result = Result(SocialOperationResultCode.BlockedByTarget);
            }
            else if (_relationships.ContainsKey(pair))
            {
                result = Result(SocialOperationResultCode.AlreadyConnected);
            }
            else if (_invitations.Values.Any(value =>
                         value.Kind == SocialInvitationKind.NameCard &&
                         value.State == SocialInvitationState.Pending &&
                         ((value.RequesterId == command.RequesterId && value.TargetId == command.TargetId) ||
                          (value.RequesterId == command.TargetId && value.TargetId == command.RequesterId))))
            {
                result = Result(SocialOperationResultCode.InvitationPending);
            }
            else if (_invitations.ContainsKey(command.InvitationId))
            {
                result = Result(SocialOperationResultCode.ReplayConflict);
            }
            else
            {
                var invitation = new SocialInvitationSnapshot(
                    command.InvitationId,
                    SocialInvitationKind.NameCard,
                    command.RequesterId,
                    command.TargetId,
                    SocialInvitationState.Pending,
                    now,
                    null,
                    1);
                _invitations.Add(invitation.InvitationId, invitation);
                result = new SocialOperationResult(SocialOperationResultCode.Success, invitation, null, true);
            }

            Complete(command.RequesterId, command.IdempotencyKey, fingerprint, result);
            return Task.FromResult(result);
        }
    }

    public Task<SocialOperationResult> RespondToNameCardAsync(
        NameCardResponseCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fingerprint = $"NameCardResponse:{command.InvitationId:N}:{command.ActorId}:{command.Accept}";
        lock (_sync)
        {
            if (TryReplay(command.ActorId, command.IdempotencyKey, fingerprint, out var replay))
            {
                return Task.FromResult(replay);
            }

            SocialOperationResult result;
            if (!_invitations.TryGetValue(command.InvitationId, out var invitation))
            {
                result = Result(SocialOperationResultCode.InvitationNotFound);
            }
            else if (invitation.TargetId != command.ActorId)
            {
                result = new SocialOperationResult(SocialOperationResultCode.NotRecipient, invitation, null, false);
            }
            else if (invitation.State != SocialInvitationState.Pending)
            {
                result = new SocialOperationResult(SocialOperationResultCode.InvalidState, invitation, null, false);
            }
            else if (_blocks.Contains((command.ActorId, invitation.RequesterId)))
            {
                result = new SocialOperationResult(SocialOperationResultCode.BlockedByActor, invitation, null, false);
            }
            else if (_blocks.Contains((invitation.RequesterId, command.ActorId)))
            {
                result = new SocialOperationResult(SocialOperationResultCode.BlockedByTarget, invitation, null, false);
            }
            else
            {
                var changed = invitation with
                {
                    State = command.Accept ? SocialInvitationState.Accepted : SocialInvitationState.Rejected,
                    RespondedAtUtc = now,
                    Version = invitation.Version + 1
                };
                _invitations[changed.InvitationId] = changed;
                PlayerRelationshipSnapshot? relationship = null;
                if (command.Accept)
                {
                    var pair = Pair(changed.RequesterId, changed.TargetId, PlayerRelationshipKind.NameCard);
                    relationship = new PlayerRelationshipSnapshot(
                        PlayerRelationshipKind.NameCard,
                        changed.RequesterId,
                        changed.TargetId,
                        now,
                        1);
                    _relationships[pair] = relationship;
                }

                result = new SocialOperationResult(
                    SocialOperationResultCode.Success,
                    changed,
                    relationship,
                    true);
            }

            Complete(command.ActorId, command.IdempotencyKey, fingerprint, result);
            return Task.FromResult(result);
        }
    }

    public Task<SocialOperationResult> SetBlockAsync(
        SocialBlockCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fingerprint = $"SetBlock:{command.ActorId}:{command.TargetId}:{command.Blocked}";
        lock (_sync)
        {
            if (TryReplay(command.ActorId, command.IdempotencyKey, fingerprint, out var replay))
            {
                return Task.FromResult(replay);
            }

            var mutated = command.Blocked
                ? _blocks.Add((command.ActorId, command.TargetId))
                : _blocks.Remove((command.ActorId, command.TargetId));
            if (command.Blocked)
            {
                mutated |= _relationships.Remove(Pair(command.ActorId, command.TargetId, PlayerRelationshipKind.NameCard));
                foreach (var invitation in _invitations.Values
                             .Where(value => value.State == SocialInvitationState.Pending &&
                                             ((value.RequesterId == command.ActorId && value.TargetId == command.TargetId) ||
                                              (value.RequesterId == command.TargetId && value.TargetId == command.ActorId)))
                             .ToArray())
                {
                    _invitations[invitation.InvitationId] = invitation with
                    {
                        State = SocialInvitationState.Cancelled,
                        RespondedAtUtc = now,
                        Version = invitation.Version + 1
                    };
                    mutated = true;
                }
            }

            var result = new SocialOperationResult(
                SocialOperationResultCode.Success,
                null,
                null,
                mutated,
                command.Blocked);
            Complete(command.ActorId, command.IdempotencyKey, fingerprint, result);
            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<PlayerRelationshipSnapshot>> ListRelationshipsAsync(
        long characterId,
        PlayerRelationshipKind kind,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            IReadOnlyList<PlayerRelationshipSnapshot> result = _relationships.Values
                .Where(value => value.Kind == kind &&
                                (value.CharacterId == characterId || value.RelatedCharacterId == characterId))
                .Select(value => value.CharacterId == characterId
                    ? value
                    : value with { CharacterId = characterId, RelatedCharacterId = value.CharacterId })
                .OrderBy(value => value.RelatedCharacterId)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    public Task<bool> IsBlockedAsync(long actorId, long targetId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            return Task.FromResult(_blocks.Contains((actorId, targetId)));
        }
    }

    private bool TryReplay(
        long actorId,
        string key,
        string fingerprint,
        out SocialOperationResult result)
    {
        if (_completed.TryGetValue((actorId, key), out var completed))
        {
            result = completed.Fingerprint == fingerprint
                ? completed.Result with { ResultCode = SocialOperationResultCode.DuplicateCompleted, Mutated = false }
                : Result(SocialOperationResultCode.ReplayConflict);
            return true;
        }

        result = default!;
        return false;
    }

    private void Complete(long actorId, string key, string fingerprint, SocialOperationResult result) =>
        _completed[(actorId, key)] = (fingerprint, result);

    private static SocialOperationResult Result(SocialOperationResultCode code) =>
        new(code, null, null, false);

    private static (long Low, long High, PlayerRelationshipKind Kind) Pair(
        long first,
        long second,
        PlayerRelationshipKind kind) =>
        (Math.Min(first, second), Math.Max(first, second), kind);
}

public sealed class ServerOwnedNameCardRuntime
{
    public const int MaximumIdempotencyKeyLength = 96;

    private readonly object _sync = new();
    private readonly IPlayerSocialRepository _repository;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Dictionary<long, SocialParticipant> _participants = [];

    public ServerOwnedNameCardRuntime(
        IPlayerSocialRepository repository,
        Func<DateTimeOffset>? utcNow = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public void RegisterOrUpdate(SocialParticipant participant)
    {
        if (participant.CharacterId <= 0 || string.IsNullOrWhiteSpace(participant.DisplayName))
        {
            throw new ArgumentException("Social participant identity and display name are required.", nameof(participant));
        }

        lock (_sync)
        {
            _participants[participant.CharacterId] = participant with { DisplayName = participant.DisplayName.Trim() };
        }
    }

    public async Task<SocialOperationResult> RequestAsync(
        NameCardRequestCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!ValidKey(command.IdempotencyKey))
        {
            return Failure(SocialOperationResultCode.InvalidRequestId);
        }
        if (command.InvitationId == Guid.Empty || command.RequesterId <= 0 || command.TargetId <= 0)
        {
            return Failure(SocialOperationResultCode.InvalidIdentity);
        }
        if (command.RequesterId == command.TargetId)
        {
            return Failure(SocialOperationResultCode.SameCharacter);
        }
        if (!Online(command.RequesterId))
        {
            return Failure(SocialOperationResultCode.SenderOffline);
        }
        if (!Online(command.TargetId))
        {
            return Failure(SocialOperationResultCode.TargetOffline);
        }

        return await _repository.RequestNameCardAsync(command, _utcNow(), cancellationToken);
    }

    public async Task<SocialOperationResult> RespondAsync(
        NameCardResponseCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!ValidKey(command.IdempotencyKey))
        {
            return Failure(SocialOperationResultCode.InvalidRequestId);
        }
        if (command.InvitationId == Guid.Empty || command.ActorId <= 0)
        {
            return Failure(SocialOperationResultCode.InvalidIdentity);
        }
        if (!Online(command.ActorId))
        {
            return Failure(SocialOperationResultCode.SenderOffline);
        }

        return await _repository.RespondToNameCardAsync(command, _utcNow(), cancellationToken);
    }

    public async Task<SocialOperationResult> SetBlockAsync(
        SocialBlockCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!ValidKey(command.IdempotencyKey))
        {
            return Failure(SocialOperationResultCode.InvalidRequestId);
        }
        if (command.ActorId <= 0 || command.TargetId <= 0)
        {
            return Failure(SocialOperationResultCode.InvalidIdentity);
        }
        if (command.ActorId == command.TargetId)
        {
            return Failure(SocialOperationResultCode.SameCharacter);
        }

        return await _repository.SetBlockAsync(command, _utcNow(), cancellationToken);
    }

    public Task<IReadOnlyList<PlayerRelationshipSnapshot>> ListNameCardsAsync(
        long characterId,
        CancellationToken cancellationToken = default) =>
        _repository.ListRelationshipsAsync(characterId, PlayerRelationshipKind.NameCard, cancellationToken);

    private bool Online(long characterId)
    {
        lock (_sync)
        {
            return _participants.TryGetValue(characterId, out var participant) && participant.Online;
        }
    }

    private static bool ValidKey(string? key) =>
        !string.IsNullOrWhiteSpace(key) && key.Length <= MaximumIdempotencyKeyLength;

    private static SocialOperationResult Failure(SocialOperationResultCode code) =>
        new(code, null, null, false);
}
