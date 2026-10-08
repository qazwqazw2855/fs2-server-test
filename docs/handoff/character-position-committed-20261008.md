# Committed character position verification — 2026-10-08

- Tested source: 1ddb94a plus the new committed-position test and runner.
- Persistence integration test passed: 1/1, zero failed or skipped.
- An independent fixture account remained Disabled with a non-login password marker.
- Its character had enabled=1 to satisfy the production writer predicate.
- The writer committed position (16,14), advanced runtime version from 0 to 1 and replaced the concurrency token.
- A new repository connection read the committed position, version and token.
- A request using the original version/token was rejected without modifying the committed state.
- The map identity remained unchanged.
- Temporary fixture account, character and DB user were removed; remaining_position_fixture=0.
- Runner exit code: 0.
- Log: /tmp/god2-position-committed-20261008-132754.log
- This verifies the DB writer and repository read path, not TCP relogin, separate-process recovery or original-client acceptance.
- Production Host settings and port 6001 were not changed.
