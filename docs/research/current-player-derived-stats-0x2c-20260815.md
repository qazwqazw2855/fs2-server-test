# Current official player derived-stat record (`S2C 0x2C`)

The exact current client proves a canonical 61-byte application record for
`S2C 0x2C`. This replaces the earlier public-beta-only 31-byte hypothesis.
World handler RVA `0x0008FA51` and Battle handler RVA `0x00146F5E` both pass
the payload to the shared current consumer at RVA `0x00117CD0`, which writes
the local player-panel object and refreshes it through RVA `0x001178C0`.

The current field order is vitality, strength, intelligence, speed; gold,
wood, water, fire, earth; positive and negative masks; physical defense,
physical attack, magical defense and magical attack. The current client uses
four-byte field spacing. Modifiers and masks consume the low 16 bits, elements
and physical/magical values consume all 32 bits. Attribute sign-mask order is
not field order: bit 0 is intelligence, bit 1 vitality, bit 2 strength and bit
3 speed; bits 4..8 color the five elements.

The public-beta package was used only to corroborate names and ordering. Its
offsets and 16-bit combat values are not reused. The machine-readable current
map is `protocol/evidence/current-build/player-derived-stats-0x2c.json` and the
build-locked codec is `OfficialPlayerDerivedStatsWireCodec`.

This finding also closes an important PK ambiguity: `0x2C` has no actor ID and
writes only the local panel object at client state `+0x430A8`. It can refresh
the player's own four modifiers, five elements, physical/magical attack and
defense after equipment or immortal changes, but it cannot reveal an
opponent's hidden values.

Runtime emission remains disabled. Static evidence establishes every consumed
field, but no current live `0x2C` frame exists in the repository. The next gate
is an official-client acceptance test of the canonical 61-byte record, followed
by wiring it to authoritative login/equipment/immortal projections if accepted.
