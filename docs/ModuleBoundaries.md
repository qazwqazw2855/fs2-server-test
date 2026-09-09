# Module Boundaries

- Domain contains pure game model primitives and rules.
- Application contains options, use-case contracts, startup orchestration, and shutdown coordination.
- Protocol contains packet metadata, packet boundaries, confidence states, and handler contracts.
- Persistence contains MariaDB connection and migration abstractions.
- Infrastructure contains path, JSON configuration, environment override, and localization loading.
- Runtime contains listener lifecycle, static data preload/cache skeletons, and session authority runtime state.
- ConsoleHost is the only composition root and executable entrypoint.
