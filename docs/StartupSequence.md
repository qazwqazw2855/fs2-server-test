# Startup Sequence

1. Configuration
2. Logging
3. MariaDB Connection
4. Schema and Migration
5. Static Data Validation
6. Static Data Preload
7. Runtime Cache
8. Session Authority
9. Network Host
10. Game Service Ready

`Server Ready` is printed only after every step succeeds. The default migration implementation intentionally fails until a real MariaDB schema verifier is bound.
