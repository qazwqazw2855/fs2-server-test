# Shutdown Sequence

Shutdown is coordinated through `ShutdownCoordinator` and runs only once.

Current order:

1. Stop accepting new logins through Session Authority.
2. Stop Network Host listeners.
3. Return the process exit code.

Future persistence queue, audit flush, autosave, and transaction drain participants must join the same coordinator.
