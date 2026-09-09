using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime;

public enum AccountStatus
{
    Active,
    Disabled,
    Locked
}

public enum LoginResultCode
{
    Success,
    InvalidCredentials,
    AccountNotFound,
    AccountDisabled,
    AccountLocked,
    AlreadyOnline,
    ServerFull,
    ProtocolError,
    InternalError
}

public enum CharacterOperationResultCode
{
    Success,
    NotAuthenticated,
    InvalidStage,
    CharacterNotFound,
    OwnershipRejected,
    InvalidName,
    InvalidClass,
    InvalidGender,
    InvalidLifeSkill,
    InvalidRequestId,
    DuplicateName,
    ReplayConflict,
    CharacterLimitReached,
    CreationProfileUnavailable,
    NeedsProtocolRecovery,
    InternalError
}

public sealed record RuntimeSession(
    string SessionId,
    string ConnectionId,
    string RemoteEndpoint,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastActivityAtUtc,
    long? AccountId,
    long? CharacterId,
    ProtocolStage ProtocolStage,
    bool IsAuthenticated,
    bool IsClosing)
{
    public static RuntimeSession Connected(string connectionId, string remoteEndpoint, DateTimeOffset now) =>
        new(Guid.NewGuid().ToString("N"), connectionId, remoteEndpoint, now, now, null, null, ProtocolStage.Connected, false, false);

    public RuntimeSession Touch(DateTimeOffset now) => this with { LastActivityAtUtc = now };
}

public sealed record AccountRecord(
    long AccountId,
    string Username,
    string PasswordHash,
    AccountStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastLoginAtUtc,
    int FailedLoginCount,
    DateTimeOffset? LockedUntilUtc,
    string? CurrentSessionId,
    string ConcurrencyToken);

public sealed record LoginRequest(string Username, string Password, string RequestId);

public sealed record LoginResult(LoginResultCode Code, RuntimeSession Session, IReadOnlyList<CharacterSummary> Characters)
{
    public bool Succeeded => Code == LoginResultCode.Success;
}

public sealed record CharacterSummary(
    long CharacterId,
    long AccountId,
    string Name,
    string Class,
    string Gender,
    string LifeSkill,
    int Level,
    string Appearance,
    int MapId,
    int PositionX,
    int PositionY,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastPlayedAtUtc,
    long? CurrentHitPoints = null,
    long? CurrentMagicPoints = null,
    long? MaximumHitPoints = null,
    long? MaximumMagicPoints = null,
    long? RemainingStatPoints = null,
    long? Constitution = null,
    long? Strength = null,
    long? Intelligence = null,
    long? Speed = null);

public sealed record CharacterCreateRequest(string Name, string Class, string Gender, string LifeSkill, string Appearance, string RequestId);

public sealed record CharacterRenameRequest(long CharacterId, string NewName, string RequestId);

public static class OfficialCharacterIdentityPolicy
{
    public const int MaximumLifecycleRequestIdLength = 256;
    private static readonly Regex ProductionNamePattern = new(
        $"^[A-Za-z0-9_]{{3,{OfficialClientWorldProtocolFrames.MaximumProductionCharacterNameLength}}}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsProductionName(string? name) =>
        name is not null && ProductionNamePattern.IsMatch(name);

    public static bool HasProductionCharacterListProfile(string? characterClass) =>
        string.Equals(characterClass, "Swordsman", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(characterClass, "Class1", StringComparison.OrdinalIgnoreCase);

    public static bool IsLifecycleRequestId(string? requestId) =>
        requestId is { Length: > 0 and <= MaximumLifecycleRequestIdLength } &&
        !string.IsNullOrWhiteSpace(requestId);
}

public sealed record CharacterDeleteRequest(long CharacterId, string RequestId);

public sealed record CharacterSelectRequest(long CharacterId, string RequestId);

public sealed record CharacterCreationSpawn(
    int MapId,
    int PositionX,
    int PositionY,
    string EvidenceStatus,
    string EvidenceReference)
{
    public bool IsProductionEligible =>
        MapId > 0 &&
        PositionX >= 0 &&
        PositionY >= 0 &&
        EvidenceStatus is "Verified" or "Recovered" &&
        !string.IsNullOrWhiteSpace(EvidenceReference);
}

public interface ICharacterCreationAuthority
{
    Task<OperationResult<CharacterCreationSpawn>> ResolveAsync(
        CharacterCreateRequest request,
        CancellationToken cancellationToken);
}

public sealed class EvidenceBlockedCharacterCreationAuthority : ICharacterCreationAuthority
{
    public static EvidenceBlockedCharacterCreationAuthority Instance { get; } = new();

    private EvidenceBlockedCharacterCreationAuthority()
    {
    }

    public Task<OperationResult<CharacterCreationSpawn>> ResolveAsync(
        CharacterCreateRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<CharacterCreationSpawn>.Failure(
            "character.creation_profile_evidence_blocked",
            "No production-enabled, evidence-backed character creation spawn profile is available."));
}

public sealed record CharacterOperationResult(CharacterOperationResultCode Code, RuntimeSession Session, CharacterSummary? Character = null)
{
    public bool Succeeded => Code == CharacterOperationResultCode.Success;
}

public sealed class UnifiedRuntimeComposition
{
    private UnifiedRuntimeComposition(
        IAccountRepository accountRepository,
        ICharacterRepository characterRepository,
        CharacterRuntimeRegistry characterRuntimeRegistry,
        DuplicateLoginGuard duplicateLoginGuard,
        ReplayGuard replayGuard,
        LoginAttemptRateLimiter loginAttemptRateLimiter,
        LoginAttemptAudit loginAttemptAudit,
        SessionStore sessionStore,
        InMemorySessionAuthority sessionAuthority,
        AuthenticationService authenticationService,
        CharacterListQuery characterListQuery,
        CharacterRuntimeService characterRuntimeService,
        PacketFactory packetFactory,
        OfficialDecryptedPacketCorpusCatalog decryptedPacketCorpus,
        ProtocolConnectionRuntime protocolConnectionRuntime,
        RuntimeCommandDispatcher commandDispatcher)
    {
        AccountRepository = accountRepository;
        CharacterRepository = characterRepository;
        CharacterRuntimeRegistry = characterRuntimeRegistry;
        DuplicateLoginGuard = duplicateLoginGuard;
        ReplayGuard = replayGuard;
        LoginAttemptRateLimiter = loginAttemptRateLimiter;
        LoginAttemptAudit = loginAttemptAudit;
        SessionStore = sessionStore;
        SessionAuthority = sessionAuthority;
        AuthenticationService = authenticationService;
        CharacterListQuery = characterListQuery;
        CharacterRuntimeService = characterRuntimeService;
        PacketFactory = packetFactory;
        DecryptedPacketCorpus = decryptedPacketCorpus;
        ProtocolConnectionRuntime = protocolConnectionRuntime;
        CommandDispatcher = commandDispatcher;
    }

    public IAccountRepository AccountRepository { get; }

    public ICharacterRepository CharacterRepository { get; }

    public CharacterRuntimeRegistry CharacterRuntimeRegistry { get; }

    public DuplicateLoginGuard DuplicateLoginGuard { get; }

    public ReplayGuard ReplayGuard { get; }

    public LoginAttemptRateLimiter LoginAttemptRateLimiter { get; }

    public LoginAttemptAudit LoginAttemptAudit { get; }

    public SessionStore SessionStore { get; }

    public InMemorySessionAuthority SessionAuthority { get; }

    public AuthenticationService AuthenticationService { get; }

    public CharacterListQuery CharacterListQuery { get; }

    public CharacterRuntimeService CharacterRuntimeService { get; }

    public PacketFactory PacketFactory { get; }

    public OfficialDecryptedPacketCorpusCatalog DecryptedPacketCorpus { get; }

    public ProtocolConnectionRuntime ProtocolConnectionRuntime { get; }

    public RuntimeCommandDispatcher CommandDispatcher { get; }

    public static UnifiedRuntimeComposition CreateProduction(
        int maximumSessions,
        IProductionAccountRepository accountRepository,
        IProductionCharacterRepository characterRepository,
        IProductionCharacterCreationAuthority? characterCreationAuthority = null,
        string? officialLiveMovementEvidenceJsonlPath = null) =>
        CreateCore(
            maximumSessions,
            accountRepository,
            characterRepository,
            characterCreationAuthority,
            officialLiveMovementEvidenceJsonlPath);

    internal static UnifiedRuntimeComposition CreateForTesting(
        int maximumSessions,
        IAccountRepository accountRepository,
        ICharacterRepository characterRepository,
        ICharacterCreationAuthority? characterCreationAuthority = null,
        string? officialLiveMovementEvidenceJsonlPath = null) =>
        CreateCore(
            maximumSessions,
            accountRepository,
            characterRepository,
            characterCreationAuthority,
            officialLiveMovementEvidenceJsonlPath);

    private static UnifiedRuntimeComposition CreateCore(
        int maximumSessions,
        IAccountRepository accountRepository,
        ICharacterRepository characterRepository,
        ICharacterCreationAuthority? characterCreationAuthority,
        string? officialLiveMovementEvidenceJsonlPath)
    {
        var characterRuntimeRegistry = new CharacterRuntimeRegistry();
        var duplicateLoginGuard = new DuplicateLoginGuard();
        var replayGuard = new ReplayGuard();
        var loginAttemptRateLimiter = new LoginAttemptRateLimiter();
        var loginAttemptAudit = new LoginAttemptAudit();
        var sessionStore = new SessionStore(closeObservers:
        [
            new LoginSessionCleanupObserver(duplicateLoginGuard, accountRepository),
            new ReplaySessionCleanupObserver(replayGuard),
            characterRuntimeRegistry
        ]);
        var sessionAuthority = new InMemorySessionAuthority(sessionStore);
        var characterListQuery = new CharacterListQuery(characterRepository);
        var authenticationService = new AuthenticationService(
            accountRepository,
            new PasswordVerifier(),
            sessionStore,
            duplicateLoginGuard,
            replayGuard,
            loginAttemptRateLimiter,
            loginAttemptAudit,
            maximumSessions: maximumSessions);
        var characterRuntimeService = new CharacterRuntimeService(
            characterRepository,
            sessionStore,
            replayGuard,
            characterRuntimeRegistry,
            characterCreationAuthority);
        var packetFactory = new PacketFactory(new PacketDeserializer(ProtocolRegistry.Official));
        var decryptedPacketCorpus = new OfficialDecryptedPacketCorpusCatalog();
        var protocolConnectionRuntime = RuntimeProtocolConnectionRuntimeFactory.Create(
            packetFactory,
            decryptedPacketCorpus,
            preserveRawEvidence: true,
            officialLiveMovementEvidenceJsonlPath: officialLiveMovementEvidenceJsonlPath);
        var commandDispatcher = new RuntimeCommandDispatcher();

        return new UnifiedRuntimeComposition(
            accountRepository,
            characterRepository,
            characterRuntimeRegistry,
            duplicateLoginGuard,
            replayGuard,
            loginAttemptRateLimiter,
            loginAttemptAudit,
            sessionStore,
            sessionAuthority,
            authenticationService,
            characterListQuery,
            characterRuntimeService,
            packetFactory,
            decryptedPacketCorpus,
            protocolConnectionRuntime,
            commandDispatcher);
    }
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class FixedClock : IClock
{
    public FixedClock(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTimeOffset UtcNow { get; set; }
}

public interface IPasswordVerifier
{
    bool Verify(string password, string storedHash);

    bool Verify(ReadOnlySpan<char> password, string storedHash);
}

public sealed class PasswordVerifier : IPasswordVerifier
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;
    private const int MinimumIterations = 50_000;
    private const int MaximumIterations = 1_000_000;
    private const int MaximumStoredHashLength = 256;

    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        try
        {
            return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(hash);
        }
    }

    public bool Verify(string password, string storedHash) =>
        Verify(password.AsSpan(), storedHash);

    public bool Verify(ReadOnlySpan<char> password, string storedHash)
    {
        if (password.IsEmpty ||
            string.IsNullOrWhiteSpace(storedHash) ||
            storedHash.Length > MaximumStoredHashLength)
        {
            return false;
        }

        var parts = storedHash.Split('$', StringSplitOptions.None);
        if (parts.Length != 4 ||
            !string.Equals(parts[0], "pbkdf2-sha256", StringComparison.Ordinal) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) ||
            iterations is < MinimumIterations or > MaximumIterations)
        {
            return false;
        }

        byte[] salt = [];
        byte[] expected = [];
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
            if (salt.Length != SaltSize || expected.Length != HashSize)
            {
                return false;
            }

            Span<byte> actual = stackalloc byte[HashSize];
            try
            {
                Rfc2898DeriveBytes.Pbkdf2(password, salt, actual, iterations, HashAlgorithmName.SHA256);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(actual);
            }
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or CryptographicException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(expected);
        }
    }
}

public interface IAccountRepository
{
    Task<AccountRecord?> FindByUsernameAsync(string username, CancellationToken cancellationToken);

    Task<AccountRecord?> FindByIdAsync(long accountId, CancellationToken cancellationToken);

    Task<OperationResult> ReplaceCurrentSessionAsync(
        long accountId,
        string? expectedCurrentSessionId,
        string replacementSessionId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<OperationResult> MarkLoginFailureAsync(long accountId, int lockThreshold, TimeSpan lockDuration, DateTimeOffset now, CancellationToken cancellationToken);

    Task ClearCurrentSessionAsync(long accountId, string sessionId, CancellationToken cancellationToken);
}

public interface IProductionAccountRepository : IAccountRepository
{
}

internal sealed class InMemoryAccountRepository : IAccountRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<long, AccountRecord> _accountsById = [];
    private readonly Dictionary<string, long> _accountIdsByName = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryAccountRepository(IEnumerable<AccountRecord>? accounts = null)
    {
        foreach (var account in accounts ?? [])
        {
            Add(account);
        }
    }

    public void Add(AccountRecord account)
    {
        lock (_gate)
        {
            _accountsById[account.AccountId] = account;
            _accountIdsByName[account.Username] = account.AccountId;
        }
    }

    public Task<AccountRecord?> FindByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(
                _accountIdsByName.TryGetValue(username, out var id) && _accountsById.TryGetValue(id, out var account)
                    ? account
                    : null);
        }
    }

    public Task<AccountRecord?> FindByIdAsync(long accountId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_accountsById.GetValueOrDefault(accountId));
        }
    }

    public Task<OperationResult> ReplaceCurrentSessionAsync(
        long accountId,
        string? expectedCurrentSessionId,
        string replacementSessionId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_accountsById.TryGetValue(accountId, out var account))
            {
                return Task.FromResult(OperationResult.Failure("account.not_found", "Account not found."));
            }

            if (!string.Equals(account.CurrentSessionId, expectedCurrentSessionId, StringComparison.Ordinal))
            {
                return Task.FromResult(OperationResult.Failure(
                    "account.session_conflict",
                    "Account session ownership changed before the replacement could be committed."));
            }

            var updated = account with
            {
                LastLoginAtUtc = now,
                FailedLoginCount = 0,
                LockedUntilUtc = null,
                CurrentSessionId = replacementSessionId,
                ConcurrencyToken = Guid.NewGuid().ToString("N")
            };
            _accountsById[accountId] = updated;
            return Task.FromResult(OperationResult.Success);
        }
    }

    public Task<OperationResult> MarkLoginFailureAsync(long accountId, int lockThreshold, TimeSpan lockDuration, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_accountsById.TryGetValue(accountId, out var account))
            {
                return Task.FromResult(OperationResult.Failure("account.not_found", "Account not found."));
            }

            var failedCount = account.FailedLoginCount + 1;
            var lockedUntil = failedCount >= lockThreshold ? now.Add(lockDuration) : account.LockedUntilUtc;
            _accountsById[accountId] = account with
            {
                FailedLoginCount = failedCount,
                LockedUntilUtc = lockedUntil,
                ConcurrencyToken = Guid.NewGuid().ToString("N")
            };
            return Task.FromResult(OperationResult.Success);
        }
    }

    public Task ClearCurrentSessionAsync(long accountId, string sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_accountsById.TryGetValue(accountId, out var account) &&
                string.Equals(account.CurrentSessionId, sessionId, StringComparison.Ordinal))
            {
                _accountsById[accountId] = account with
                {
                    CurrentSessionId = null,
                    ConcurrencyToken = Guid.NewGuid().ToString("N")
                };
            }
        }

        return Task.CompletedTask;
    }
}

public interface ISessionStore
{
    RuntimeSession Create(string connectionId, string remoteEndpoint);

    OperationResult<RuntimeSession> Get(string sessionId);

    OperationResult<RuntimeSession> Authenticate(RuntimeSession session, long accountId);

    OperationResult<RuntimeSession> BindCharacter(RuntimeSession session, long characterId);

    OperationResult<RuntimeSession> Transition(RuntimeSession session, ProtocolStage stage);

    Task CloseAsync(RuntimeSession session, string reason, CancellationToken cancellationToken);

    IReadOnlyList<RuntimeSession> ActiveSessions { get; }
}

public interface ISessionCloseObserver
{
    Task SessionClosedAsync(RuntimeSession session, string reason, CancellationToken cancellationToken);
}

public sealed class SessionStore : ISessionStore
{
    private sealed class SessionEntry
    {
        public SessionEntry(RuntimeSession session)
        {
            Current = session;
        }

        public object Gate { get; } = new();

        public RuntimeSession Current { get; set; }

        public bool CleanupStarted { get; set; }
    }

    private readonly ConcurrentDictionary<string, SessionEntry> _sessionsById = [];
    private readonly ConcurrentQueue<SessionCleanupFailure> _cleanupFailures = [];
    private readonly ConcurrentQueue<string> _closedSessionOrder = [];
    private readonly IReadOnlyList<ISessionCloseObserver> _closeObservers;
    private readonly IClock _clock;
    private readonly int _maximumRetainedClosedSessions;

    public SessionStore(
        IClock? clock = null,
        IEnumerable<ISessionCloseObserver>? closeObservers = null,
        int maximumRetainedClosedSessions = 1_024)
    {
        _clock = clock ?? new SystemClock();
        _closeObservers = closeObservers?.ToArray() ?? [];
        _maximumRetainedClosedSessions = Math.Max(0, maximumRetainedClosedSessions);
    }

    public IReadOnlyList<RuntimeSession> ActiveSessions => _sessionsById.Values
        .Select(Snapshot)
        .Where(session => !session.IsClosing && session.ProtocolStage != ProtocolStage.Closed)
        .ToArray();

    public IReadOnlyList<SessionCleanupFailure> CleanupFailures => _cleanupFailures.ToArray();

    public RuntimeSession Create(string connectionId, string remoteEndpoint)
    {
        var session = RuntimeSession.Connected(connectionId, remoteEndpoint, _clock.UtcNow) with { ProtocolStage = ProtocolStage.Login };
        _sessionsById[session.SessionId] = new SessionEntry(session);
        return session;
    }

    public OperationResult<RuntimeSession> Get(string sessionId)
    {
        return _sessionsById.TryGetValue(sessionId, out var entry)
            ? OperationResult<RuntimeSession>.Success(Snapshot(entry))
            : OperationResult<RuntimeSession>.Failure("session.not_found", "Session was not found.", sessionId);
    }

    public OperationResult<RuntimeSession> Authenticate(RuntimeSession session, long accountId) =>
        Update(session.SessionId, current =>
        {
            if (current.IsClosing || current.ProtocolStage is not (ProtocolStage.Connected or ProtocolStage.Login))
            {
                return OperationResult<RuntimeSession>.Failure(
                    "session.stage_invalid",
                    "Login is only accepted from an active Connected/Login stage.",
                    current.ProtocolStage.ToString());
            }

            return OperationResult<RuntimeSession>.Success(current with
            {
                AccountId = accountId,
                ProtocolStage = ProtocolStage.Authenticated,
                IsAuthenticated = true,
                LastActivityAtUtc = _clock.UtcNow
            });
        });

    public OperationResult<RuntimeSession> BindCharacter(RuntimeSession session, long characterId) =>
        Update(session.SessionId, current =>
        {
            if (current.IsClosing || !current.IsAuthenticated || current.AccountId is null)
            {
                return OperationResult<RuntimeSession>.Failure("session.not_authenticated", "Character selection requires an active authenticated account.");
            }

            if (current.ProtocolStage is not (ProtocolStage.Authenticated or ProtocolStage.CharacterList))
            {
                return OperationResult<RuntimeSession>.Failure(
                    "session.stage_invalid",
                    "Character selection is only accepted from Authenticated/CharacterList stage.",
                    current.ProtocolStage.ToString());
            }

            return OperationResult<RuntimeSession>.Success(current with
            {
                CharacterId = characterId,
                ProtocolStage = ProtocolStage.CharacterSelected,
                LastActivityAtUtc = _clock.UtcNow
            });
        });

    public OperationResult<RuntimeSession> Transition(RuntimeSession session, ProtocolStage stage) =>
        Update(session.SessionId, current =>
        {
            if (current.IsClosing || !IsAllowedTransition(current.ProtocolStage, stage))
            {
                return OperationResult<RuntimeSession>.Failure(
                    "session.stage_transition_invalid",
                    "Protocol stage cannot transition to the requested stage.",
                    $"{current.ProtocolStage}->{stage}");
            }

            return OperationResult<RuntimeSession>.Success(current with
            {
                ProtocolStage = stage,
                LastActivityAtUtc = _clock.UtcNow
            });
        });

    private static bool IsAllowedTransition(ProtocolStage current, ProtocolStage next)
    {
        if (current == next)
        {
            return true;
        }

        return current switch
        {
            ProtocolStage.Connected => next is ProtocolStage.Login or ProtocolStage.Closing or ProtocolStage.Closed,
            ProtocolStage.Login => next is ProtocolStage.Authenticated or ProtocolStage.Closing or ProtocolStage.Closed,
            ProtocolStage.Authenticated => next is ProtocolStage.CharacterList or ProtocolStage.Closing or ProtocolStage.Closed,
            ProtocolStage.CharacterList => next is ProtocolStage.CharacterSelected or ProtocolStage.Closing or ProtocolStage.Closed,
            ProtocolStage.CharacterSelected => next is ProtocolStage.WorldEntering or ProtocolStage.Closing or ProtocolStage.Closed,
            ProtocolStage.WorldEntering => next is ProtocolStage.WorldAuthenticated or ProtocolStage.MapBinding or ProtocolStage.InWorld or ProtocolStage.Closing or ProtocolStage.Closed,
            ProtocolStage.WorldAuthenticated => next is ProtocolStage.MapBinding or ProtocolStage.Closing or ProtocolStage.Closed,
            ProtocolStage.MapBinding => next is ProtocolStage.PlayerAttached or ProtocolStage.Closing or ProtocolStage.Closed,
            ProtocolStage.PlayerAttached => next is ProtocolStage.ContentLoading or ProtocolStage.Closing or ProtocolStage.Closed,
            ProtocolStage.ContentLoading => next is ProtocolStage.InWorld or ProtocolStage.Closing or ProtocolStage.Closed,
            ProtocolStage.InWorld => next is ProtocolStage.Closing or ProtocolStage.Closed,
            ProtocolStage.Closing => next is ProtocolStage.Closed,
            ProtocolStage.Closed => false,
            _ => false
        };
    }

    public async Task CloseAsync(RuntimeSession session, string reason, CancellationToken cancellationToken)
    {
        if (!_sessionsById.TryGetValue(session.SessionId, out var entry))
        {
            return;
        }

        RuntimeSession closed;
        lock (entry.Gate)
        {
            if (entry.CleanupStarted)
            {
                return;
            }

            entry.CleanupStarted = true;
            closed = entry.Current with
            {
                IsClosing = true,
                ProtocolStage = ProtocolStage.Closed,
                LastActivityAtUtc = _clock.UtcNow
            };
            entry.Current = closed;
        }

        foreach (var observer in _closeObservers)
        {
            try
            {
                await observer.SessionClosedAsync(closed, reason, cancellationToken);
            }
            catch (Exception exception)
            {
                _cleanupFailures.Enqueue(new SessionCleanupFailure(
                    closed.SessionId,
                    observer.GetType().Name,
                    exception.GetType().Name,
                    exception.Message,
                    _clock.UtcNow));
                while (_cleanupFailures.Count > 1_024)
                {
                    _cleanupFailures.TryDequeue(out _);
                }
            }
        }

        _closedSessionOrder.Enqueue(closed.SessionId);
        while (_closedSessionOrder.Count > _maximumRetainedClosedSessions &&
               _closedSessionOrder.TryDequeue(out var expiredSessionId))
        {
            if (_sessionsById.TryGetValue(expiredSessionId, out var expiredEntry) &&
                Snapshot(expiredEntry).ProtocolStage == ProtocolStage.Closed)
            {
                _sessionsById.TryRemove(expiredSessionId, out _);
            }
        }
    }

    private OperationResult<RuntimeSession> Update(
        string sessionId,
        Func<RuntimeSession, OperationResult<RuntimeSession>> mutation)
    {
        if (!_sessionsById.TryGetValue(sessionId, out var entry))
        {
            return OperationResult<RuntimeSession>.Failure("session.not_found", "Session was not found.", sessionId);
        }

        lock (entry.Gate)
        {
            var result = mutation(entry.Current);
            if (result.Succeeded && result.Value is not null)
            {
                entry.Current = result.Value;
            }

            return result;
        }
    }

    private static RuntimeSession Snapshot(SessionEntry entry)
    {
        lock (entry.Gate)
        {
            return entry.Current;
        }
    }
}

public sealed record SessionCleanupFailure(
    string SessionId,
    string Observer,
    string ExceptionType,
    string Message,
    DateTimeOffset TimestampUtc);

public enum DuplicateLoginAdmissionResult
{
    Entered,
    AlreadyOnline,
    CapacityReached
}

public sealed class DuplicateLoginGuard
{
    private readonly object _gate = new();
    private readonly Dictionary<long, string> _sessionsByAccountId = [];

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _sessionsByAccountId.Count;
            }
        }
    }

    public bool TryEnter(long accountId, string sessionId) =>
        TryEnter(accountId, sessionId, int.MaxValue) == DuplicateLoginAdmissionResult.Entered;

    public DuplicateLoginAdmissionResult TryEnter(long accountId, string sessionId, int maximumAccounts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (maximumAccounts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAccounts));
        }

        lock (_gate)
        {
            if (_sessionsByAccountId.ContainsKey(accountId))
            {
                return DuplicateLoginAdmissionResult.AlreadyOnline;
            }

            if (_sessionsByAccountId.Count >= maximumAccounts)
            {
                return DuplicateLoginAdmissionResult.CapacityReached;
            }

            _sessionsByAccountId.Add(accountId, sessionId);
            return DuplicateLoginAdmissionResult.Entered;
        }
    }

    public bool IsOnline(long accountId)
    {
        lock (_gate)
        {
            return _sessionsByAccountId.ContainsKey(accountId);
        }
    }

    public bool TryTransfer(long accountId, string currentSessionId, string replacementSessionId)
    {
        if (string.IsNullOrWhiteSpace(currentSessionId) || string.IsNullOrWhiteSpace(replacementSessionId))
        {
            return false;
        }

        lock (_gate)
        {
            if (!_sessionsByAccountId.TryGetValue(accountId, out var current) ||
                !string.Equals(current, currentSessionId, StringComparison.Ordinal))
            {
                return false;
            }

            _sessionsByAccountId[accountId] = replacementSessionId;
            return true;
        }
    }

    public void Leave(long accountId, string sessionId)
    {
        lock (_gate)
        {
            if (_sessionsByAccountId.TryGetValue(accountId, out var currentSessionId) &&
                string.Equals(currentSessionId, sessionId, StringComparison.Ordinal))
            {
                _sessionsByAccountId.Remove(accountId);
            }
        }
    }
}

public sealed class CharacterRuntimeRegistry : ISessionCloseObserver
{
    private readonly ConcurrentDictionary<string, long> _charactersBySessionId = [];

    public IReadOnlyDictionary<string, long> ActiveCharactersBySession =>
        new Dictionary<string, long>(_charactersBySessionId, StringComparer.Ordinal);

    public void Register(RuntimeSession session)
    {
        if (session.CharacterId is not null)
        {
            _charactersBySessionId[session.SessionId] = session.CharacterId.Value;
        }
    }

    public bool IsRegistered(string sessionId) => _charactersBySessionId.ContainsKey(sessionId);

    public Task SessionClosedAsync(RuntimeSession session, string reason, CancellationToken cancellationToken)
    {
        _charactersBySessionId.TryRemove(session.SessionId, out _);
        return Task.CompletedTask;
    }
}

public sealed class LoginSessionCleanupObserver : ISessionCloseObserver
{
    private readonly DuplicateLoginGuard _duplicateLoginGuard;
    private readonly IAccountRepository _accountRepository;

    public LoginSessionCleanupObserver(DuplicateLoginGuard duplicateLoginGuard, IAccountRepository accountRepository)
    {
        _duplicateLoginGuard = duplicateLoginGuard;
        _accountRepository = accountRepository;
    }

    public async Task SessionClosedAsync(RuntimeSession session, string reason, CancellationToken cancellationToken)
    {
        if (session.AccountId is null)
        {
            return;
        }

        _duplicateLoginGuard.Leave(session.AccountId.Value, session.SessionId);
        await _accountRepository.ClearCurrentSessionAsync(session.AccountId.Value, session.SessionId, cancellationToken);
    }
}

public sealed class ReplayGuard
{
    private sealed record ReplayEntry(string Key, DateTimeOffset Timestamp);

    private const string GlobalScope = "global";
    private readonly ConcurrentDictionary<string, DateTimeOffset> _seenRequestIds = [];
    private readonly ConcurrentQueue<ReplayEntry> _expiryOrder = [];
    private readonly object _expirySweepGate = new();
    private readonly IClock _clock;
    private int _entryCount;
    private long _operationCount;

    public ReplayGuard(TimeSpan? timeToLive = null, int maximumEntries = 100_000, IClock? clock = null)
    {
        TimeToLive = timeToLive ?? TimeSpan.FromMinutes(10);
        if (TimeToLive <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeToLive));
        }

        MaximumEntries = Math.Max(1, maximumEntries);
        _clock = clock ?? new SystemClock();
    }

    public TimeSpan TimeToLive { get; }

    public int MaximumEntries { get; }

    public int Count => Volatile.Read(ref _entryCount);

    public bool TryAccept(string requestId) => TryAccept(GlobalScope, requestId);

    public bool TryAccept(string scope, string requestId)
    {
        if (string.IsNullOrWhiteSpace(scope) || string.IsNullOrWhiteSpace(requestId))
        {
            return false;
        }

        var now = _clock.UtcNow;
        if (Interlocked.Increment(ref _operationCount) % 64 == 0 || Count >= MaximumEntries)
        {
            RemoveExpired(now);
        }

        var key = ScopedKey(scope, requestId);
        while (true)
        {
            if (!_seenRequestIds.TryGetValue(key, out var existing))
            {
                if (!TryReserveEntrySlot())
                {
                    return false;
                }

                if (_seenRequestIds.TryAdd(key, now))
                {
                    _expiryOrder.Enqueue(new ReplayEntry(key, now));
                    return true;
                }

                Interlocked.Decrement(ref _entryCount);
                continue;
            }

            if (now - existing <= TimeToLive)
            {
                return false;
            }

            if (_seenRequestIds.TryUpdate(key, now, existing))
            {
                _expiryOrder.Enqueue(new ReplayEntry(key, now));
                return true;
            }
        }
    }

    public void RemoveScope(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return;
        }

        var prefix = $"{scope}\u001f";
        foreach (var key in _seenRequestIds.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)))
        {
            if (_seenRequestIds.TryRemove(key, out _))
            {
                Interlocked.Decrement(ref _entryCount);
            }
        }
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        lock (_expirySweepGate)
        {
            while (_expiryOrder.TryPeek(out var candidate) && now - candidate.Timestamp > TimeToLive)
            {
                if (!_expiryOrder.TryDequeue(out candidate) || candidate is null)
                {
                    continue;
                }

                RemoveReplayEntry(candidate.Key, candidate.Timestamp);
            }

            // Concurrent producers can enqueue timestamps in a different order than the
            // clock values they captured. At capacity, scan the bounded dictionary so an
            // unexpired queue head cannot hide reclaimable entries behind it.
            if (Count >= MaximumEntries)
            {
                foreach (var entry in _seenRequestIds)
                {
                    if (now - entry.Value > TimeToLive)
                    {
                        RemoveReplayEntry(entry.Key, entry.Value);
                    }
                }
            }
        }
    }

    private void RemoveReplayEntry(string key, DateTimeOffset timestamp)
    {
        // Conditional key/value removal prevents an expiry sweep from deleting a
        // newer timestamp installed by a concurrent replay-window renewal.
        if (((ICollection<KeyValuePair<string, DateTimeOffset>>)_seenRequestIds).Remove(
                new KeyValuePair<string, DateTimeOffset>(key, timestamp)))
        {
            Interlocked.Decrement(ref _entryCount);
        }
    }

    private bool TryReserveEntrySlot()
    {
        while (true)
        {
            var current = Volatile.Read(ref _entryCount);
            if (current >= MaximumEntries)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _entryCount, current + 1, current) == current)
            {
                return true;
            }
        }
    }

    private static string ScopedKey(string scope, string requestId) => $"{scope}\u001f{requestId}";
}

public sealed class ReplaySessionCleanupObserver : ISessionCloseObserver
{
    private readonly ReplayGuard _replayGuard;

    public ReplaySessionCleanupObserver(ReplayGuard replayGuard)
    {
        _replayGuard = replayGuard;
    }

    public Task SessionClosedAsync(RuntimeSession session, string reason, CancellationToken cancellationToken)
    {
        _replayGuard.RemoveScope(session.SessionId);
        return Task.CompletedTask;
    }
}

public sealed class LoginAttemptRateLimiter
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _attempts = new(StringComparer.OrdinalIgnoreCase);
    private long _operationCount;

    public LoginAttemptRateLimiter(int limit = 5, TimeSpan? window = null, int maximumKeys = 10_000)
    {
        Limit = Math.Max(1, limit);
        Window = window ?? TimeSpan.FromMinutes(1);
        MaximumKeys = Math.Max(1, maximumKeys);
    }

    public int Limit { get; }

    public TimeSpan Window { get; }

    public int MaximumKeys { get; }

    public int KeyCount
    {
        get
        {
            lock (_gate)
            {
                return _attempts.Count;
            }
        }
    }

    public bool TryConsume(string remoteEndpoint, string username, DateTimeOffset now) =>
        TryConsumeNormalized($"{NormalizeRemoteAddress(remoteEndpoint)}\u001f{username.Trim().ToUpperInvariant()}", now);

    public bool TryConsume(string key, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return TryConsumeNormalized(key, now);
    }

    private bool TryConsumeNormalized(string key, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (Interlocked.Increment(ref _operationCount) % 64 == 0 || _attempts.Count >= MaximumKeys)
            {
                RemoveExpiredKeys(now);
            }

            if (!_attempts.TryGetValue(key, out var queue))
            {
                if (_attempts.Count >= MaximumKeys)
                {
                    return false;
                }

                queue = new Queue<DateTimeOffset>();
                _attempts[key] = queue;
            }

            while (queue.Count > 0 && now - queue.Peek() > Window)
            {
                queue.Dequeue();
            }

            if (queue.Count >= Limit)
            {
                return false;
            }

            queue.Enqueue(now);
            return true;
        }
    }

    private void RemoveExpiredKeys(DateTimeOffset now)
    {
        foreach (var entry in _attempts.ToArray())
        {
            while (entry.Value.Count > 0 && now - entry.Value.Peek() > Window)
            {
                entry.Value.Dequeue();
            }

            if (entry.Value.Count == 0)
            {
                _attempts.Remove(entry.Key);
            }
        }
    }

    private static string NormalizeRemoteAddress(string remoteEndpoint)
    {
        if (IPEndPoint.TryParse(remoteEndpoint, out var endpoint))
        {
            return endpoint.Address.IsIPv4MappedToIPv6
                ? endpoint.Address.MapToIPv4().ToString()
                : endpoint.Address.ToString();
        }

        return remoteEndpoint.Trim();
    }
}

public sealed record LoginAttemptAuditEntry(DateTimeOffset Timestamp, string UsernameHash, string ConnectionId, LoginResultCode Result);

public sealed class LoginAttemptAudit
{
    private readonly ConcurrentQueue<LoginAttemptAuditEntry> _entries = [];
    private readonly int _maximumEntries;
    private int _count;

    public LoginAttemptAudit(int maximumEntries = 10_000)
    {
        _maximumEntries = Math.Max(1, maximumEntries);
    }

    public IReadOnlyList<LoginAttemptAuditEntry> Entries => _entries.ToArray();

    public void Write(LoginAttemptAuditEntry entry)
    {
        _entries.Enqueue(entry);
        Interlocked.Increment(ref _count);
        while (Volatile.Read(ref _count) > _maximumEntries && _entries.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _count);
        }
    }
}

public sealed class AuthenticationService
{
    private readonly IAccountRepository _accounts;
    private readonly IPasswordVerifier _passwordVerifier;
    private readonly ISessionStore _sessions;
    private readonly DuplicateLoginGuard _duplicateLoginGuard;
    private readonly ReplayGuard _replayGuard;
    private readonly LoginAttemptRateLimiter _rateLimiter;
    private readonly LoginAttemptAudit _audit;
    private readonly IClock _clock;
    private readonly int _maximumSessions;

    public AuthenticationService(
        IAccountRepository accounts,
        IPasswordVerifier passwordVerifier,
        ISessionStore sessions,
        DuplicateLoginGuard duplicateLoginGuard,
        ReplayGuard replayGuard,
        LoginAttemptRateLimiter rateLimiter,
        LoginAttemptAudit audit,
        IClock? clock = null,
        int maximumSessions = 1000)
    {
        _accounts = accounts;
        _passwordVerifier = passwordVerifier;
        _sessions = sessions;
        _duplicateLoginGuard = duplicateLoginGuard;
        _replayGuard = replayGuard;
        _rateLimiter = rateLimiter;
        _audit = audit;
        _clock = clock ?? new SystemClock();
        _maximumSessions = Math.Max(1, maximumSessions);
    }

    public Task<LoginResult> LoginAsync(LoginRequest request, RuntimeSession session, CharacterListQuery characterListQuery, CancellationToken cancellationToken) =>
        LoginCoreAsync(
            request.Username,
            request.RequestId,
            session,
            characterListQuery,
            account => _passwordVerifier.Verify(request.Password, account.PasswordHash),
            cancellationToken);

    public Task<LoginResult> LoginSensitiveAsync(
        string username,
        ReadOnlyMemory<char> password,
        string requestId,
        RuntimeSession session,
        CharacterListQuery characterListQuery,
        CancellationToken cancellationToken) =>
        LoginCoreAsync(
            username,
            requestId,
            session,
            characterListQuery,
            account => _passwordVerifier.Verify(password.Span, account.PasswordHash),
            cancellationToken);

    private async Task<LoginResult> LoginCoreAsync(
        string username,
        string requestId,
        RuntimeSession session,
        CharacterListQuery characterListQuery,
        Func<AccountRecord, bool> verifyPassword,
        CancellationToken cancellationToken)
    {
        if (!_replayGuard.TryAccept(session.SessionId, requestId))
        {
            return Audit(username, session, LoginResultCode.ProtocolError, session, []);
        }

        if (!_rateLimiter.TryConsume(session.RemoteEndpoint, username, _clock.UtcNow))
        {
            return Audit(username, session, LoginResultCode.ProtocolError, session, []);
        }

        var account = await _accounts.FindByUsernameAsync(username, cancellationToken);
        if (account is null)
        {
            return Audit(username, session, LoginResultCode.AccountNotFound, session, []);
        }

        if (account.Status == AccountStatus.Disabled)
        {
            return Audit(username, session, LoginResultCode.AccountDisabled, session, []);
        }

        if (account.Status == AccountStatus.Locked || account.LockedUntilUtc > _clock.UtcNow)
        {
            return Audit(username, session, LoginResultCode.AccountLocked, session, []);
        }

        if (!verifyPassword(account))
        {
            await _accounts.MarkLoginFailureAsync(account.AccountId, lockThreshold: 5, TimeSpan.FromMinutes(15), _clock.UtcNow, cancellationToken);
            return Audit(username, session, LoginResultCode.InvalidCredentials, session, []);
        }

        var admission = _duplicateLoginGuard.TryEnter(
            account.AccountId,
            session.SessionId,
            _maximumSessions);
        if (admission == DuplicateLoginAdmissionResult.CapacityReached)
        {
            return Audit(username, session, LoginResultCode.ServerFull, session, []);
        }

        if (admission == DuplicateLoginAdmissionResult.AlreadyOnline)
        {
            return Audit(username, session, LoginResultCode.AlreadyOnline, session, []);
        }

        var authenticated = _sessions.Authenticate(session, account.AccountId);
        if (!authenticated.Succeeded || authenticated.Value is null)
        {
            _duplicateLoginGuard.Leave(account.AccountId, session.SessionId);
            return Audit(username, session, LoginResultCode.ProtocolError, session, []);
        }

        var sessionResult = _sessions.Transition(authenticated.Value, ProtocolStage.CharacterList);
        if (!sessionResult.Succeeded || sessionResult.Value is null)
        {
            _duplicateLoginGuard.Leave(account.AccountId, session.SessionId);
            await CloseLatestSessionStateAsync(session.SessionId, "login_stage_transition_failed", cancellationToken);
            return Audit(username, session, LoginResultCode.ProtocolError, session, []);
        }

        var persisted = await _accounts.ReplaceCurrentSessionAsync(
            account.AccountId,
            account.CurrentSessionId,
            session.SessionId,
            _clock.UtcNow,
            cancellationToken);
        if (!persisted.Succeeded)
        {
            _duplicateLoginGuard.Leave(account.AccountId, session.SessionId);
            await CloseLatestSessionStateAsync(session.SessionId, "login_persistence_failed", cancellationToken);
            return Audit(username, session, LoginResultCode.InternalError, session, []);
        }

        var characters = await characterListQuery.ListAsync(sessionResult.Value, cancellationToken);
        return Audit(username, sessionResult.Value, LoginResultCode.Success, sessionResult.Value, characters);
    }

    public async Task<OperationResult<RuntimeSession>> TransferToWorldSessionAsync(
        RuntimeSession loginSession,
        RuntimeSession worldSession,
        CancellationToken cancellationToken)
    {
        if (!loginSession.IsAuthenticated || loginSession.AccountId is null ||
            loginSession.ProtocolStage != ProtocolStage.CharacterList ||
            worldSession.IsAuthenticated || worldSession.ProtocolStage != ProtocolStage.Login)
        {
            return OperationResult<RuntimeSession>.Failure(
                "session.world_transfer_invalid",
                "The login and world sessions are not in transferable states.");
        }

        var accountId = loginSession.AccountId.Value;
        var authenticated = _sessions.Authenticate(worldSession, accountId);
        if (!authenticated.Succeeded || authenticated.Value is null)
        {
            return OperationResult<RuntimeSession>.Failure(
                "session.world_transfer_authentication_failed",
                "The world session could not inherit the authenticated account.");
        }

        var characterList = _sessions.Transition(authenticated.Value, ProtocolStage.CharacterList);
        if (!characterList.Succeeded || characterList.Value is null)
        {
            await CloseLatestSessionStateAsync(worldSession.SessionId, "world_transfer_stage_failed", cancellationToken);
            return OperationResult<RuntimeSession>.Failure(
                "session.world_transfer_stage_failed",
                "The world session could not enter the character-list transfer stage.");
        }

        if (!_duplicateLoginGuard.TryTransfer(accountId, loginSession.SessionId, worldSession.SessionId))
        {
            await CloseLatestSessionStateAsync(worldSession.SessionId, "world_transfer_owner_mismatch", cancellationToken);
            return OperationResult<RuntimeSession>.Failure(
                "session.world_transfer_owner_mismatch",
                "The authenticated login session no longer owns the account lease.");
        }

        OperationResult persisted;
        try
        {
            persisted = await _accounts.ReplaceCurrentSessionAsync(
                accountId,
                loginSession.SessionId,
                worldSession.SessionId,
                _clock.UtcNow,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _duplicateLoginGuard.TryTransfer(accountId, worldSession.SessionId, loginSession.SessionId);
            await CloseLatestSessionStateAsync(worldSession.SessionId, "world_transfer_persistence_exception", cancellationToken);
            return OperationResult<RuntimeSession>.Failure(
                "session.world_transfer_persistence_failed",
                "The account session lease could not be transferred to the world connection.",
                exception.GetType().Name);
        }

        if (!persisted.Succeeded)
        {
            _duplicateLoginGuard.TryTransfer(accountId, worldSession.SessionId, loginSession.SessionId);
            await CloseLatestSessionStateAsync(worldSession.SessionId, "world_transfer_persistence_failed", cancellationToken);
            return OperationResult<RuntimeSession>.Failure(
                "session.world_transfer_persistence_failed",
                "The account session lease could not be transferred to the world connection.");
        }

        return OperationResult<RuntimeSession>.Success(characterList.Value);
    }

    private async Task CloseLatestSessionStateAsync(
        string sessionId,
        string reason,
        CancellationToken cancellationToken)
    {
        var latest = _sessions.Get(sessionId);
        if (latest.Succeeded && latest.Value is not null)
        {
            await _sessions.CloseAsync(latest.Value, reason, cancellationToken);
        }
    }

    private LoginResult Audit(string username, RuntimeSession originalSession, LoginResultCode code, RuntimeSession session, IReadOnlyList<CharacterSummary> characters)
    {
        var usernameHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(username.Trim().ToUpperInvariant())));
        _audit.Write(new LoginAttemptAuditEntry(_clock.UtcNow, usernameHash, originalSession.ConnectionId, code));
        return new LoginResult(code, session, characters);
    }
}

public interface ICharacterRepository
{
    Task<IReadOnlyList<CharacterSummary>> ListByAccountAsync(long accountId, CancellationToken cancellationToken);

    Task<CharacterSummary?> FindByIdAsync(long characterId, CancellationToken cancellationToken);

    Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken);

    Task<OperationResult<CharacterSummary>> CreateAsync(long accountId, CharacterCreateRequest request, int mapId, int positionX, int positionY, int maximumCharacters, CancellationToken cancellationToken);

    Task<OperationResult<CharacterSummary>> DeleteAsync(long accountId, long characterId, string requestId, CancellationToken cancellationToken);

    Task<OperationResult<CharacterSummary>> RenameAsync(long accountId, long characterId, string newName, string requestId, CancellationToken cancellationToken);

    Task<OperationResult<CharacterSummary>> TouchPlayedAsync(long characterId, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IProductionCharacterRepository : ICharacterRepository
{
}

public interface IProductionCharacterCreationAuthority : ICharacterCreationAuthority
{
}

internal sealed class InMemoryCharacterRepository : ICharacterRepository
{
    private sealed record LifecycleReplay(string Operation, string PayloadSignature, CharacterSummary Result);

    private readonly object _gate = new();
    private readonly Dictionary<long, CharacterSummary> _charactersById = [];
    private readonly Dictionary<(long AccountId, string RequestId), LifecycleReplay> _lifecycleReplays = [];
    private long _nextId = 1;

    public InMemoryCharacterRepository(IEnumerable<CharacterSummary>? characters = null)
    {
        foreach (var character in characters ?? [])
        {
            _charactersById[character.CharacterId] = character;
            _nextId = Math.Max(_nextId, character.CharacterId + 1);
        }
    }

    public Task<IReadOnlyList<CharacterSummary>> ListByAccountAsync(long accountId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<CharacterSummary> result = _charactersById.Values
                .Where(character => character.AccountId == accountId && !string.Equals(character.Status, "Deleted", StringComparison.OrdinalIgnoreCase))
                .OrderBy(character => character.CreatedAtUtc)
                .ThenBy(character => character.CharacterId)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    public Task<CharacterSummary?> FindByIdAsync(long characterId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var character = _charactersById.GetValueOrDefault(characterId);
            return Task.FromResult(character is not null &&
                !string.Equals(character.Status, "Deleted", StringComparison.OrdinalIgnoreCase)
                    ? character
                    : null);
        }
    }

    public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_charactersById.Values.Any(character =>
                !string.Equals(character.Status, "Deleted", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(character.Name, name, StringComparison.OrdinalIgnoreCase)));
        }
    }

    public Task<OperationResult<CharacterSummary>> CreateAsync(long accountId, CharacterCreateRequest request, int mapId, int positionX, int positionY, int maximumCharacters, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!OfficialCharacterIdentityPolicy.IsLifecycleRequestId(request.RequestId))
            {
                return Task.FromResult(OperationResult<CharacterSummary>.Failure(
                    "character.request_id_invalid",
                    "Character lifecycle request id is missing or exceeds the supported length."));
            }

            var replay = FindReplay(
                accountId,
                request.RequestId,
                "Create",
                PayloadSignature(request.Name, request.Class, request.Gender, request.LifeSkill, request.Appearance, mapId, positionX, positionY, maximumCharacters));
            if (replay is not null)
            {
                return Task.FromResult(replay);
            }

            if (_charactersById.Values.Count(character =>
                    character.AccountId == accountId &&
                    !string.Equals(character.Status, "Deleted", StringComparison.OrdinalIgnoreCase)) >= maximumCharacters)
            {
                return Task.FromResult(OperationResult<CharacterSummary>.Failure(
                    "character.limit_reached",
                    "The account has reached the active character limit."));
            }

            if (_charactersById.Values.Any(character =>
                !string.Equals(character.Status, "Deleted", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(character.Name, request.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(OperationResult<CharacterSummary>.Failure("character.name_duplicate", "Character name already exists.", request.Name));
            }

            var now = DateTimeOffset.UtcNow;
            var character = new CharacterSummary(
                _nextId++,
                accountId,
                request.Name,
                request.Class,
                request.Gender,
                request.LifeSkill,
                1,
                request.Appearance,
                mapId,
                positionX,
                positionY,
                "Active",
                now,
                null);
            _charactersById[character.CharacterId] = character;
            StoreReplay(
                accountId,
                request.RequestId,
                "Create",
                PayloadSignature(request.Name, request.Class, request.Gender, request.LifeSkill, request.Appearance, mapId, positionX, positionY, maximumCharacters),
                character);
            return Task.FromResult(OperationResult<CharacterSummary>.Success(character));
        }
    }

    public Task<OperationResult<CharacterSummary>> DeleteAsync(long accountId, long characterId, string requestId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!OfficialCharacterIdentityPolicy.IsLifecycleRequestId(requestId))
            {
                return Task.FromResult(OperationResult<CharacterSummary>.Failure(
                    "character.request_id_invalid",
                    "Character lifecycle request id is missing or exceeds the supported length."));
            }

            var replay = FindReplay(accountId, requestId, "Delete", PayloadSignature(characterId));
            if (replay is not null)
            {
                return Task.FromResult(replay);
            }

            if (!_charactersById.TryGetValue(characterId, out var character) ||
                string.Equals(character.Status, "Deleted", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(OperationResult<CharacterSummary>.Failure("character.not_found", "Character not found."));
            }

            if (character.AccountId != accountId)
            {
                return Task.FromResult(OperationResult<CharacterSummary>.Failure(
                    "character.ownership_rejected",
                    "Character ownership does not match the authenticated account."));
            }

            var deleted = character with { Status = "Deleted" };
            _charactersById[characterId] = deleted;
            StoreReplay(accountId, requestId, "Delete", PayloadSignature(characterId), deleted);
            return Task.FromResult(OperationResult<CharacterSummary>.Success(deleted));
        }
    }

    public Task<OperationResult<CharacterSummary>> RenameAsync(long accountId, long characterId, string newName, string requestId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!OfficialCharacterIdentityPolicy.IsLifecycleRequestId(requestId))
            {
                return Task.FromResult(OperationResult<CharacterSummary>.Failure(
                    "character.request_id_invalid",
                    "Character lifecycle request id is missing or exceeds the supported length."));
            }

            var replay = FindReplay(accountId, requestId, "Rename", PayloadSignature(characterId, newName));
            if (replay is not null)
            {
                return Task.FromResult(replay);
            }

            if (!_charactersById.TryGetValue(characterId, out var character) ||
                string.Equals(character.Status, "Deleted", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(OperationResult<CharacterSummary>.Failure("character.not_found", "Character not found."));
            }

            if (character.AccountId != accountId)
            {
                return Task.FromResult(OperationResult<CharacterSummary>.Failure(
                    "character.ownership_rejected",
                    "Character ownership does not match the authenticated account."));
            }

            if (_charactersById.Values.Any(other =>
                other.CharacterId != characterId &&
                !string.Equals(other.Status, "Deleted", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(other.Name, newName, StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(OperationResult<CharacterSummary>.Failure("character.name_duplicate", "Character name already exists.", newName));
            }

            var updated = character with { Name = newName };
            _charactersById[characterId] = updated;
            StoreReplay(accountId, requestId, "Rename", PayloadSignature(characterId, newName), updated);
            return Task.FromResult(OperationResult<CharacterSummary>.Success(updated));
        }
    }

    private OperationResult<CharacterSummary>? FindReplay(
        long accountId,
        string requestId,
        string operation,
        string payloadSignature)
    {
        if (!_lifecycleReplays.TryGetValue((accountId, requestId), out var replay))
        {
            return null;
        }

        return string.Equals(replay.Operation, operation, StringComparison.Ordinal) &&
            string.Equals(replay.PayloadSignature, payloadSignature, StringComparison.Ordinal)
                ? OperationResult<CharacterSummary>.Success(replay.Result)
                : OperationResult<CharacterSummary>.Failure(
                    "character.replay_conflict",
                    "The character lifecycle idempotency key was reused with a different operation or payload.");
    }

    private void StoreReplay(
        long accountId,
        string requestId,
        string operation,
        string payloadSignature,
        CharacterSummary result) =>
        _lifecycleReplays[(accountId, requestId)] = new LifecycleReplay(operation, payloadSignature, result);

    private static string PayloadSignature(params object?[] values)
    {
        var builder = new StringBuilder();
        foreach (var value in values)
        {
            var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            builder.Append(text.Length).Append(':').Append(text).Append(';');
        }

        return builder.ToString();
    }

    public Task<OperationResult<CharacterSummary>> TouchPlayedAsync(long characterId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_charactersById.TryGetValue(characterId, out var character) ||
                string.Equals(character.Status, "Deleted", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(OperationResult<CharacterSummary>.Failure("character.not_found", "Character not found."));
            }

            var updated = character with { LastPlayedAtUtc = now };
            _charactersById[characterId] = updated;
            return Task.FromResult(OperationResult<CharacterSummary>.Success(updated));
        }
    }
}

public sealed class CharacterListQuery
{
    private readonly ICharacterRepository _characters;

    public CharacterListQuery(ICharacterRepository characters)
    {
        _characters = characters;
    }

    public async Task<IReadOnlyList<CharacterSummary>> ListAsync(RuntimeSession session, CancellationToken cancellationToken)
    {
        if (!session.IsAuthenticated || session.AccountId is null)
        {
            return [];
        }

        return await _characters.ListByAccountAsync(session.AccountId.Value, cancellationToken);
    }
}

public sealed class CharacterRuntimeService
{
    // The current official-client character-list serializer is proven only for
    // zero or one active character. Keep repository admission aligned with that
    // production wire invariant until a multi-slot layout is evidence-pinned.
    private const int CharacterLimit = 1;
    private static readonly HashSet<string> AllowedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Unknown",
        "Swordsman",
        "Warrior",
        "Mage",
        "Taoist",
        "Pharmacist",
        "Warlock",
        "Class1",
        "Class2",
        "Class3",
        "Class4"
    };
    private static readonly HashSet<string> AllowedGenders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Unknown",
        "Male",
        "Female",
        "Gender1",
        "Gender2"
    };
    private static readonly HashSet<string> AllowedLifeSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Unknown",
        "LifeSkill1",
        "LifeSkill2",
        "LifeSkill3",
        "LifeSkill4"
    };
    private readonly ICharacterRepository _characters;
    private readonly ISessionStore _sessions;
    private readonly ReplayGuard _replayGuard;
    private readonly ICharacterCreationAuthority _creationAuthority;
    private readonly CharacterRuntimeRegistry? _characterRuntimeRegistry;
    private readonly IClock _clock;

    public CharacterRuntimeService(
        ICharacterRepository characters,
        ISessionStore sessions,
        ReplayGuard replayGuard,
        CharacterRuntimeRegistry? characterRuntimeRegistry = null,
        ICharacterCreationAuthority? creationAuthority = null,
        IClock? clock = null)
    {
        _characters = characters;
        _sessions = sessions;
        _replayGuard = replayGuard;
        _characterRuntimeRegistry = characterRuntimeRegistry;
        _creationAuthority = creationAuthority ?? EvidenceBlockedCharacterCreationAuthority.Instance;
        _clock = clock ?? new SystemClock();
    }

    public async Task<CharacterOperationResult> CreateAsync(RuntimeSession session, CharacterCreateRequest request, CancellationToken cancellationToken)
    {
        var auth = ValidateAuthenticatedCharacterStage(session);
        if (auth != CharacterOperationResultCode.Success)
        {
            return new CharacterOperationResult(auth, session);
        }

        if (!OfficialCharacterIdentityPolicy.IsLifecycleRequestId(request.RequestId))
        {
            return new CharacterOperationResult(CharacterOperationResultCode.InvalidRequestId, session);
        }

        if (!IsValidName(request.Name))
        {
            return new CharacterOperationResult(CharacterOperationResultCode.InvalidName, session);
        }

        if (!AllowedClasses.Contains(request.Class))
        {
            return new CharacterOperationResult(CharacterOperationResultCode.InvalidClass, session);
        }

        if (!AllowedGenders.Contains(request.Gender))
        {
            return new CharacterOperationResult(CharacterOperationResultCode.InvalidGender, session);
        }

        if (!AllowedLifeSkills.Contains(request.LifeSkill))
        {
            return new CharacterOperationResult(CharacterOperationResultCode.InvalidLifeSkill, session);
        }

        var spawn = await _creationAuthority.ResolveAsync(request, cancellationToken);
        if (!spawn.Succeeded || spawn.Value is null || !spawn.Value.IsProductionEligible)
        {
            return new CharacterOperationResult(CharacterOperationResultCode.CreationProfileUnavailable, session);
        }

        var create = await _characters.CreateAsync(
            session.AccountId!.Value,
            request,
            spawn.Value.MapId,
            spawn.Value.PositionX,
            spawn.Value.PositionY,
            CharacterLimit,
            cancellationToken);
        return create.Succeeded && create.Value is not null
            ? new CharacterOperationResult(CharacterOperationResultCode.Success, session, create.Value)
            : new CharacterOperationResult(MapRepositoryError(create.Error.Code), session);
    }

    public async Task<CharacterOperationResult> DeleteAsync(RuntimeSession session, CharacterDeleteRequest request, CancellationToken cancellationToken)
    {
        var auth = ValidateAuthenticatedCharacterStage(session);
        if (auth != CharacterOperationResultCode.Success)
        {
            return new CharacterOperationResult(auth, session);
        }

        if (!OfficialCharacterIdentityPolicy.IsLifecycleRequestId(request.RequestId))
        {
            return new CharacterOperationResult(CharacterOperationResultCode.InvalidRequestId, session);
        }

        var delete = await _characters.DeleteAsync(
            session.AccountId!.Value,
            request.CharacterId,
            request.RequestId,
            cancellationToken);
        return delete.Succeeded && delete.Value is not null
            ? new CharacterOperationResult(CharacterOperationResultCode.Success, session, delete.Value)
            : new CharacterOperationResult(MapRepositoryError(delete.Error.Code), session);
    }

    public async Task<CharacterOperationResult> RenameAsync(RuntimeSession session, CharacterRenameRequest request, CancellationToken cancellationToken)
    {
        var auth = ValidateAuthenticatedCharacterStage(session);
        if (auth != CharacterOperationResultCode.Success)
        {
            return new CharacterOperationResult(auth, session);
        }

        if (!OfficialCharacterIdentityPolicy.IsLifecycleRequestId(request.RequestId))
        {
            return new CharacterOperationResult(CharacterOperationResultCode.InvalidRequestId, session);
        }

        if (!IsValidName(request.NewName))
        {
            return new CharacterOperationResult(CharacterOperationResultCode.InvalidName, session);
        }

        var rename = await _characters.RenameAsync(
            session.AccountId!.Value,
            request.CharacterId,
            request.NewName,
            request.RequestId,
            cancellationToken);
        return rename.Succeeded && rename.Value is not null
            ? new CharacterOperationResult(CharacterOperationResultCode.Success, session, rename.Value)
            : new CharacterOperationResult(MapRepositoryError(rename.Error.Code), session);
    }

    public async Task<CharacterOperationResult> SelectAsync(RuntimeSession session, CharacterSelectRequest request, CancellationToken cancellationToken)
    {
        var auth = ValidateAuthenticatedCharacterStage(session);
        if (auth != CharacterOperationResultCode.Success)
        {
            return new CharacterOperationResult(auth, session);
        }

        if (!OfficialCharacterIdentityPolicy.IsLifecycleRequestId(request.RequestId))
        {
            return new CharacterOperationResult(CharacterOperationResultCode.InvalidRequestId, session);
        }

        if (!_replayGuard.TryAccept(session.SessionId, request.RequestId))
        {
            return new CharacterOperationResult(CharacterOperationResultCode.InvalidStage, session);
        }

        var character = await _characters.FindByIdAsync(request.CharacterId, cancellationToken);
        if (character is null)
        {
            return new CharacterOperationResult(CharacterOperationResultCode.CharacterNotFound, session);
        }

        if (character.AccountId != session.AccountId)
        {
            return new CharacterOperationResult(CharacterOperationResultCode.OwnershipRejected, session);
        }

        var touched = await _characters.TouchPlayedAsync(request.CharacterId, _clock.UtcNow, cancellationToken);
        if (!touched.Succeeded || touched.Value is null)
        {
            return new CharacterOperationResult(CharacterOperationResultCode.InternalError, session);
        }

        var bind = _sessions.BindCharacter(session, request.CharacterId);
        if (!bind.Succeeded || bind.Value is null)
        {
            return new CharacterOperationResult(CharacterOperationResultCode.InvalidStage, session);
        }

        _characterRuntimeRegistry?.Register(bind.Value);
        return new CharacterOperationResult(CharacterOperationResultCode.Success, bind.Value, touched.Value);
    }

    public CharacterOperationResult ProtocolRecoveryRequired(RuntimeSession session) =>
        new(CharacterOperationResultCode.NeedsProtocolRecovery, session);

    private static CharacterOperationResultCode ValidateAuthenticatedCharacterStage(RuntimeSession session)
    {
        if (!session.IsAuthenticated || session.AccountId is null)
        {
            return CharacterOperationResultCode.NotAuthenticated;
        }

        return session.ProtocolStage is ProtocolStage.Authenticated or ProtocolStage.CharacterList
            ? CharacterOperationResultCode.Success
            : CharacterOperationResultCode.InvalidStage;
    }

    private static bool IsValidName(string name) => OfficialCharacterIdentityPolicy.IsProductionName(name);

    private static CharacterOperationResultCode MapRepositoryError(string code) => code switch
    {
        "character.not_found" => CharacterOperationResultCode.CharacterNotFound,
        "character.ownership_rejected" => CharacterOperationResultCode.OwnershipRejected,
        "character.name_invalid" => CharacterOperationResultCode.InvalidName,
        "character.class_profile_evidence_blocked" => CharacterOperationResultCode.CreationProfileUnavailable,
        "character.name_duplicate" => CharacterOperationResultCode.DuplicateName,
        "character.replay_conflict" => CharacterOperationResultCode.ReplayConflict,
        "character.request_id_invalid" => CharacterOperationResultCode.InvalidRequestId,
        "character.limit_reached" => CharacterOperationResultCode.CharacterLimitReached,
        "character.creation_profile_evidence_blocked" => CharacterOperationResultCode.CreationProfileUnavailable,
        _ => CharacterOperationResultCode.InternalError
    };
}

public sealed record PacketRuntimeContext(
    string ConnectionId,
    string SessionId,
    ProtocolStage Stage,
    string RawEvidenceDirectory);

public sealed record PacketRuntimeResult(PacketRouteStatus Status, PacketEnvelope? Packet, OperationError Error)
{
    public string EvidenceSignatureId { get; init; } = string.Empty;

    public static PacketRuntimeResult Handled(PacketEnvelope packet) => new(PacketRouteStatus.Handled, packet, OperationError.None);

    public static PacketRuntimeResult Evidence(PacketEnvelope packet, string evidenceSignatureId) =>
        new(PacketRouteStatus.CapturedEvidence, packet, OperationError.None)
        {
            EvidenceSignatureId = evidenceSignatureId
        };

    public static PacketRuntimeResult Captured(PacketEnvelope packet) => new(PacketRouteStatus.CapturedUnknown, packet, OperationError.None);

    public static PacketRuntimeResult Rejected(PacketRouteStatus status, PacketEnvelope? packet, OperationError error) => new(status, packet, error);
}

public sealed class ProtocolConnectionRuntime
{
    private readonly PacketFactory _packetFactory;
    private readonly IPacketRouter _router;
    private readonly ConcurrentDictionary<string, MonotonicPacketSequenceValidator> _sequenceValidatorsByConnectionId = [];
    private readonly IPacketChecksumBoundary _checksumBoundary;
    private readonly IPacketEncryptionBoundary _encryptionBoundary;
    private readonly IUnknownPacketCaptureSink _unknownPacketCapture;
    private readonly IProtocolAuditLog _audit;
    private readonly OfficialCurrentBuildPacketEvidenceCatalog _currentBuildEvidence;
    private readonly OfficialDecryptedPacketCorpusCatalog _decryptedPacketCorpus;
    private readonly OfficialLiveMovementEvidenceRecorder _officialLiveMovementEvidenceRecorder;
    private readonly bool _preserveRawEvidence;
    private readonly int _maximumRawEvidenceFiles;
    private readonly long _maximumRawEvidenceBytes;
    private readonly object _rawEvidenceGate = new();
    private int _rawEvidenceFiles;
    private long _rawEvidenceBytes;

    public ProtocolConnectionRuntime(
        PacketFactory? packetFactory = null,
        IPacketRouter? router = null,
        IPacketChecksumBoundary? checksumBoundary = null,
        IPacketEncryptionBoundary? encryptionBoundary = null,
        IUnknownPacketCaptureSink? unknownPacketCapture = null,
        IProtocolAuditLog? audit = null,
        OfficialCurrentBuildPacketEvidenceCatalog? currentBuildEvidence = null,
        OfficialDecryptedPacketCorpusCatalog? decryptedPacketCorpus = null,
        IOfficialLiveMovementEvidenceSink? officialLiveMovementEvidenceSink = null,
        bool preserveRawEvidence = false,
        int maximumRawEvidenceFiles = 1_024,
        long maximumRawEvidenceBytes = 4 * 1024 * 1024)
    {
        _packetFactory = packetFactory ?? new PacketFactory(new PacketDeserializer(ProtocolRegistry.Official));
        _router = router ?? new InMemoryPacketRouter(new PacketRegistry());
        _checksumBoundary = checksumBoundary ?? new NoRecoveredChecksumBoundary();
        _encryptionBoundary = encryptionBoundary ?? new NoRecoveredEncryptionBoundary();
        _unknownPacketCapture = unknownPacketCapture ?? new InMemoryUnknownPacketCaptureSink();
        _audit = audit ?? new InMemoryProtocolAuditLog();
        _currentBuildEvidence = currentBuildEvidence ?? new OfficialCurrentBuildPacketEvidenceCatalog();
        _decryptedPacketCorpus = decryptedPacketCorpus ?? new OfficialDecryptedPacketCorpusCatalog();
        _officialLiveMovementEvidenceRecorder = new OfficialLiveMovementEvidenceRecorder(
            officialLiveMovementEvidenceSink ?? NullOfficialLiveMovementEvidenceSink.Instance);
        _preserveRawEvidence = preserveRawEvidence;
        _maximumRawEvidenceFiles = Math.Max(1, maximumRawEvidenceFiles);
        _maximumRawEvidenceBytes = Math.Max(1, maximumRawEvidenceBytes);
    }

    public async Task<PacketRuntimeResult> DecodeAndRouteAsync(PacketRuntimeContext context, ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
    {
        var decoded = _packetFactory.CreateFromFrame(frame);
        if (!decoded.Succeeded || decoded.Value is null)
        {
            return Reject(context, PacketRouteStatus.ProtocolError, null, decoded.Error);
        }

        var packet = decoded.Value;
        var evidenceMatch = _currentBuildEvidence.MatchClientFrame(context.Stage, frame.Span);
        if (evidenceMatch is not null)
        {
            var captured = await CaptureUnknownAsync(context, packet, cancellationToken);
            if (!captured.Succeeded)
            {
                return Reject(context, PacketRouteStatus.ProtocolError, packet, captured.Error);
            }

            Audit(
                context,
                "Current Build Evidence",
                packet,
                $"Recognized capture-backed evidence signature {evidenceMatch.Signature.Id}; gameplay mutation remains evidence-blocked.");
            return PacketRuntimeResult.Evidence(packet, evidenceMatch.Signature.Id);
        }

        var decrypted = _encryptionBoundary.Decrypt(packet);
        if (!decrypted.Succeeded || decrypted.Value is null)
        {
            return Reject(context, PacketRouteStatus.ProtocolError, packet, decrypted.Error);
        }

        packet = decrypted.Value;
        var checksum = _checksumBoundary.Validate(packet);
        if (!checksum.Succeeded)
        {
            return Reject(context, PacketRouteStatus.ProtocolError, packet, checksum.Error);
        }

        var plaintextFrame = SerializeFrame(packet);
        await TryRecordOfficialLiveMovementEvidenceAsync(context, plaintextFrame, cancellationToken);
        OfficialDecryptedPacketMatch? corpusMatch;
        try
        {
            corpusMatch = _decryptedPacketCorpus.MatchExactPlaintext(
                PacketDirection.ClientToServer,
                plaintextFrame);
        }
        finally
        {
            Array.Clear(plaintextFrame);
        }
        if (corpusMatch is not null)
        {
            Audit(
                context,
                "Decrypted Corpus Match",
                packet,
                $"Matched verified plaintext corpus sample {corpusMatch.Definition.Id}; semantic authority remains {corpusMatch.Definition.Status}.");
        }

        var sequence = ValidateSequence(context, packet);
        if (!sequence.Succeeded)
        {
            return Reject(context, PacketRouteStatus.ProtocolError, packet, sequence.Error);
        }

        if (!IsAllowedAtStage(context.Stage, packet.Knowledge?.Family))
        {
            return Reject(
                context,
                PacketRouteStatus.StageRejected,
                packet,
                new OperationError("packet.stage_rejected", "Packet family is not allowed in the current protocol stage.", packet.Knowledge?.Family ?? "Unknown"));
        }

        if (packet.Confidence == ProtocolConfidence.Unknown || packet.Knowledge is null)
        {
            var captured = await CaptureUnknownAsync(context, packet, cancellationToken);
            if (!captured.Succeeded)
            {
                return Reject(context, PacketRouteStatus.ProtocolError, packet, captured.Error);
            }

            return corpusMatch is null
                ? PacketRuntimeResult.Captured(packet)
                : PacketRuntimeResult.Evidence(packet, $"decrypted-corpus:{corpusMatch.Definition.Id}");
        }

        var routed = await _router.RouteAsync(packet, cancellationToken);
        if (!routed.Succeeded)
        {
            if (packet.Knowledge.Family == "Heartbeat")
            {
                Audit(context, "Heartbeat", packet, "Heartbeat accepted at protocol boundary.");
                return PacketRuntimeResult.Handled(packet);
            }

            var captured = await CaptureUnknownAsync(context, packet, cancellationToken);
            if (!captured.Succeeded)
            {
                return Reject(context, PacketRouteStatus.ProtocolError, packet, captured.Error);
            }

            return PacketRuntimeResult.Rejected(PacketRouteStatus.HandlerMissing, packet, routed.Error);
        }

        Audit(context, "Packet Routed", packet, "Packet routed through registered handler.");
        return PacketRuntimeResult.Handled(packet);
    }

    public OperationResult ValidateSequence(PacketRuntimeContext context, PacketEnvelope packet) =>
        _sequenceValidatorsByConnectionId
            .GetOrAdd(context.ConnectionId, _ => new MonotonicPacketSequenceValidator())
            .Validate(packet);

    public int ConnectionStateCount => _sequenceValidatorsByConnectionId.Count;

    public OfficialDecryptedPacketCorpusCatalog DecryptedPacketCorpus => _decryptedPacketCorpus;

    public void RemoveConnection(string connectionId)
    {
        if (!string.IsNullOrWhiteSpace(connectionId))
        {
            _sequenceValidatorsByConnectionId.TryRemove(connectionId, out _);
        }
    }

    private static byte[] SerializeFrame(PacketEnvelope packet)
    {
        var frame = new byte[packet.Header.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)packet.Header.Length));
        packet.Payload.Span.CopyTo(frame.AsSpan(2));
        return frame;
    }

    private async ValueTask TryRecordOfficialLiveMovementEvidenceAsync(
        PacketRuntimeContext context,
        ReadOnlyMemory<byte> plaintextFrame,
        CancellationToken cancellationToken)
    {
        if (!IsOfficialLiveMovementEvidenceCandidate(plaintextFrame.Span))
        {
            return;
        }

        await _officialLiveMovementEvidenceRecorder.RecordDecodedFrameAsync(
            PacketDirection.ClientToServer,
            plaintextFrame,
            new OfficialLiveMovementEvidenceContext(
                context.SessionId,
                "formal-runtime-c2s",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                context.ConnectionId),
            cancellationToken);
    }

    private static bool IsOfficialLiveMovementEvidenceCandidate(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 3)
        {
            return false;
        }

        return frame[2] switch
        {
            OfficialLiveMovementWorldBatchWireCodec.ClientMovementOpcode
                when frame.Length == OfficialLiveMovementWorldBatchWireCodec.ClientMovementFrameLength => true,
            OfficialLiveMovementWorldBatchWireCodec.ClientMovementCompanionOpcode
                when frame.Length == OfficialLiveMovementWorldBatchWireCodec.ClientMovementFrameLength => true,
            OfficialLiveMovementWorldBatchWireCodec.ClientStableSideband1DOpcode
                when frame.Length == OfficialLiveMovementWorldBatchWireCodec.ClientStableSidebandFrameLength => true,
            OfficialLiveMovementWorldBatchWireCodec.ClientStableSideband72Opcode
                when frame.Length == OfficialLiveMovementWorldBatchWireCodec.ClientStableSidebandFrameLength => true,
            OfficialLiveMovementWorldBatchWireCodec.ClientMovementSidebandOpcode
                when frame.Length == OfficialLiveMovementWorldBatchWireCodec.ClientMovementSidebandFrameLength => true,
            OfficialLiveMovementWorldBatchWireCodec.ClientHeartbeatOrTick30Opcode
                when frame.Length == OfficialLiveMovementWorldBatchWireCodec.ClientStableSidebandFrameLength => true,
            _ => false
        };
    }

    private static bool IsAllowedAtStage(ProtocolStage stage, string? family)
    {
        if (family is null)
        {
            return true;
        }

        return stage switch
        {
            ProtocolStage.Connected or ProtocolStage.Login => family is "Heartbeat" or "Login",
            ProtocolStage.Authenticated or ProtocolStage.CharacterList => family is "Heartbeat" or "CharacterList" or "CharacterCreate" or "CharacterDelete" or "CharacterRename" or "CharacterSelect" or "Logout",
            ProtocolStage.CharacterSelected or ProtocolStage.WorldEntering or ProtocolStage.WorldAuthenticated or ProtocolStage.MapBinding or ProtocolStage.PlayerAttached or ProtocolStage.ContentLoading => family is "Heartbeat" or "WorldTransfer" or "WorldEnter" or "MapLoad" or "PlayerInitialize" or "Logout",
            ProtocolStage.InWorld => family is "Heartbeat" or "Movement" or "NPC" or "Merchant" or "Item" or "Quest" or "Battle" or "Immortal" or "BattlePet" or "Logout",
            ProtocolStage.Closing or ProtocolStage.Closed => false,
            _ => false
        };
    }

    private PacketRuntimeResult Reject(PacketRuntimeContext context, PacketRouteStatus status, PacketEnvelope? packet, OperationError error)
    {
        Audit(context, status.ToString(), packet, error.Message);
        return PacketRuntimeResult.Rejected(status, packet, error);
    }

    private async Task<OperationResult> CaptureUnknownAsync(
        PacketRuntimeContext context,
        PacketEnvelope packet,
        CancellationToken cancellationToken)
    {
        try
        {
            var capturedAt = DateTimeOffset.UtcNow;
            var evidencePath = string.Empty;
            if (_preserveRawEvidence)
            {
                if (!TryReserveRawEvidence(packet.Payload.Length))
                {
                    return OperationResult.Failure(
                        "evidence.capture_quota_exceeded",
                        "The bounded raw protocol evidence quota has been exhausted.");
                }

                evidencePath = Path.Combine(
                    context.RawEvidenceDirectory,
                    $"{capturedAt:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.sensitive.bin");
                try
                {
                    Directory.CreateDirectory(context.RawEvidenceDirectory);
                    await using var evidenceStream = new FileStream(
                        evidencePath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 4096,
                        FileOptions.Asynchronous | FileOptions.WriteThrough);
                    await evidenceStream.WriteAsync(packet.Payload, cancellationToken);
                }
                catch
                {
                    ReleaseRawEvidence(packet.Payload.Length);
                    throw;
                }
            }

            _unknownPacketCapture.Capture(new UnknownPacketCapture(
                capturedAt,
                context.ConnectionId,
                context.SessionId,
                packet.Header.OpcodeCandidate,
                packet.Payload.Length,
                PacketEvidenceHash.Sha256Hex(packet.Payload.Span),
                evidencePath,
                context.Stage));
            Audit(
                context,
                "Unknown Packet Captured",
                packet,
                _preserveRawEvidence
                    ? "Sensitive raw payload preserved within the bounded formal protocol evidence quota."
                    : "Only payload metadata and SHA-256 were retained; raw bytes were not written to disk.");
            return OperationResult.Success;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Failure(
                "evidence.capture_failed",
                "Unknown packet evidence could not be preserved.",
                exception.GetType().Name);
        }
    }

    private bool TryReserveRawEvidence(int payloadLength)
    {
        lock (_rawEvidenceGate)
        {
            if (_rawEvidenceFiles >= _maximumRawEvidenceFiles ||
                payloadLength > _maximumRawEvidenceBytes - _rawEvidenceBytes)
            {
                return false;
            }

            _rawEvidenceFiles++;
            _rawEvidenceBytes += payloadLength;
            return true;
        }
    }

    private void ReleaseRawEvidence(int payloadLength)
    {
        lock (_rawEvidenceGate)
        {
            _rawEvidenceFiles = Math.Max(0, _rawEvidenceFiles - 1);
            _rawEvidenceBytes = Math.Max(0, _rawEvidenceBytes - payloadLength);
        }
    }

    private void Audit(PacketRuntimeContext context, string eventName, PacketEnvelope? packet, string message)
    {
        _audit.Write(new ProtocolAuditEntry(
            DateTimeOffset.UtcNow,
            context.ConnectionId,
            context.SessionId,
            eventName,
            packet?.Header.OpcodeCandidate ?? string.Empty,
            packet?.Payload.Length ?? 0,
            context.Stage,
            message));
    }
}
