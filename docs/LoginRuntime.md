# Login Runtime

Phase 6 adds the login runtime services beneath the protocol boundary.

Implemented:

- `AuthenticationService`
- `IAccountRepository` and `InMemoryAccountRepository`
- `PasswordVerifier` using PBKDF2-SHA256 hashes
- `SessionStore`
- `DuplicateLoginGuard`
- `ReplayGuard`
- `LoginAttemptRateLimiter`
- `LoginAttemptAudit`
- `CharacterListQuery`

Supported results:

- Success
- Invalid Credentials
- Account Not Found
- Account Disabled
- Account Locked
- Already Online
- Server Full
- Protocol Error
- Internal Error

Security behavior:

- Plaintext passwords exist only in the `LoginRequest` protocol/runtime boundary object.
- Password verification uses constant-time hash comparison.
- Duplicate login, replay request id, rate limit, lockout, and session ownership are enforced.
- Session id is generated with `Guid.NewGuid().ToString("N")` and is not derived from username, connection id, or account id.

Protocol boundary:

- The service runtime is ready for official login packet handlers.
- Login packet decode/response serialization is marked `PARTIAL - NEEDS PROTOCOL RECOVERY` until official raw packet evidence is recovered.
