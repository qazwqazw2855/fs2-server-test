# Character Runtime

Phase 7 adds character list and character-management runtime services beneath the protocol boundary.

Implemented:

- `CharacterListQuery`
- `CharacterRuntimeService`
- `ICharacterRepository` and `InMemoryCharacterRepository`
- Character ownership validation
- Character create validation
- Character delete ownership isolation
- Character rename validation and uniqueness
- Character select and session character binding
- Character selection ready stage transition

Character list fields:

- CharacterId
- AccountId
- Name
- Class
- Gender
- Level
- Appearance
- MapId
- PositionX
- PositionY
- Status
- CreatedAtUtc
- LastPlayedAtUtc

Conservative unknown handling:

- Class, gender, appearance, spawn map, and spawn coordinates allow `Unknown` or neutral values until official defaults are recovered.
- Create/Delete/Rename service behavior is implemented, but official packet-level PASS is not claimed until request/response packet evidence exists.
- Character Select reaches `CharacterSelected` on the same connection and session, which means Character Selection Ready only. It does not claim World Entry packet PASS.
