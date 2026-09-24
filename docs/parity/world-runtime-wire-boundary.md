# World Runtime wire evidence boundary

This note records the offline review after the isolated World Login and Portal destination checks. It does not promote a gameplay codec or claim real-client acceptance.

| V2 path | Current state | Existing evidence | Remaining boundary |
|---|---|---|---|
| World Login | Formal map identity and position are checked before presence; the verified bootstrap is 1772 bytes. | Headless LoginProbe entered World on one enabled map. | Windows client render and entry on other maps are unverified. |
| Portal transition | The enabled route catalog has five routes. Destination build and formal map bounds are checked before writing a transition. | The read-only DB integration check covers destination bounds for all five enabled routes. | Client trigger events and visual transfer remain unverified; Portal 1 source center conflicts with source map bounds. |
| Inventory | V2 reads an authoritative snapshot and blocks wire dispatch. | Classic `OfficialInventoryBootstrapWireCodec` is build-locked and only handles empty inventory or one verified merchant item in authority slot 0, projecting a specific visible slot. | Its serializer also needs a wallet balance and the verified purchase response. The V2 snapshot alone does not establish a general login inventory bootstrap, item use, or arbitrary item layout. |
| Other players | V2 queues presence and movement replication events, but blocks wire dispatch. | Classic `PlayerSpawnS2C128` retains an opaque region and documents dynamic identity fields as only partially verified. | No verified general peer spawn, movement, or despawn dispatch for the current V2 runtime. |

The V2 inventory state and the Classic merchant example may share one item layout. That correspondence does not prove the client will accept the merchant purchase response as a login restore for every inventory state. Do not wire the Classic codec into V2 or synthesize player replication frames until the corresponding client sequence and state are evidenced.

Next offline work can audit exact-current traces or static call sites for inventory login and peer visibility. The related runtime change should be tested with the original client when it is available; DB and headless tests only establish server-side consistency.
