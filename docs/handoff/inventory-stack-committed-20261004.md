# Committed inventory stack verification — 2026-10-04

- Targeted test: 1 passed, 0 failed, 0 skipped.
- Dedicated disabled character and temporary DB user; cleanup reported remaining_stack_fixture=0.
- Each grant uses a new physical connection with pooling disabled.
- Connection-local temporary items provide maximum_stack=5; formal item content is unchanged.
- Real inventory, identity reservations, receipts and audits are committed through GrantInTransactionAsync.
- Eight identical merge requests produce one grant and seven replays.
- Two distinct requests compete for the same version: one grants, one returns VersionConflict.
- The rejected request retries using the current version and merges successfully.
- Final stacks are 5 and 3; inventory version and mutation sequence are 4.
- Original stack identity is preserved; spill allocates one new identity.
- Older requests replay across connections after later mutations without changing state.
- Changed content under an existing idempotency key is rejected.
- Final durable receipts: 4; audits: 5; audited quantity increase: 8.
- The test owns commit/rollback around GrantInTransactionAsync; it does not test stack behavior through GrantAsync.
- Full suites have not yet been rerun; previous full result remains 408 at source 1c25881.
- No formal stack-rule approval, TCP merchant dispatch, production deployment or Client acceptance.

## Full verification

- Tested source: dd31e942d1ea6e3f4a6893b21fe0250d7cf6a080.
- Six suites: 409 passed, 0 failed, 0 skipped; DB integration enabled.
- Core 7, Application 114, Session 20, Protocol 111, Network 111, Persistence Integration 46.
- All nested runners and the outer stack fixture reported successful cleanup.
- Supersedes the earlier full-suite verification gap.
- Formal content approval, merchant TCP dispatch and Client acceptance remain incomplete.
- Production 6001 was not deployed.
