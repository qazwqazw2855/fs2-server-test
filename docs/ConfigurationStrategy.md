# Configuration Strategy

Configuration lives under `config/` as UTF-8 JSON files and maps to strongly typed options.

Environment overrides are supported for database host, database name, database user, database password variable name, database port, network bind IP, listener ports, and language.

Database passwords are never stored in configuration. `database.json` stores only the environment variable name.
