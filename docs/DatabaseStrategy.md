# Database Strategy

MariaDB is the formal data authority for static and dynamic game data. The configured database name is `god2`, and the Server reads its connection settings directly from the server root `config/database.json` file.

This foundation provides:

- MariaDB TCP connection probe abstraction.
- Migration runner contract.
- Unit of Work and idempotency store contracts.

It does not use JSON, CSV, markdown, hardcoded arrays, or memory repositories as formal game data authority.
