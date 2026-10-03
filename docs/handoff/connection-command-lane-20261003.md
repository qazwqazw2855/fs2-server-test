# Connection command lane — 2026-10-03

- Network suite: 106 passed, 0 failed, 0 skipped.
- Real merchant command DB closed-loop test: 1 passed; fixture cleaned.
- MerchantCommandService requires an explicitly supplied command lane.
- The lane covers interaction checks through writer completion.
- Coordinated state updates use the same lane; close rejects new/queued commands and waits for the active operation before cleanup.
- Tests verify serialization, operation failure recovery, close waiting for a dispatched writer, cleanup once and post-close rejection.
- Cleanup failure remains observable and leaves the lane closed.
- Callbacks must not recursively enter or close the same lane.
- TCP state updates and cleanup are not wired yet.
- Registry mutations and NPC snapshot publication outside the lane remain uncoordinated.
- No production deployment, official content promotion or Client acceptance.
- Full six-suite verification has not been rerun for this change.
