# Quest profile payload boundary — 2026-10-03

- Read-only DB inspection found 15,123 profiles, all with valid JSON arrays.
- Array lengths: 2=14,594; 3=441; 4=2; 5=1; 12=5; 19=80.
- All 167 enabled profiles have three-element arrays.
- Enabled samples contain ID, display name and an unidentified number; they do not establish executable objective semantics.
- 85 profiles contain start/end NPC candidates; all are disabled.
- NPC-bound samples include narrative instructions and reward text, but sampled QuestId values are NULL.
- ClientQuestId, NPC client handles and formal server identities require explicit mapping.
- Reward text does not establish item IDs, quantities, currency amounts, selection branches or timeout outcomes.
- Formal quest objectives and rewards remain empty; enabled formal repeatability remains unknown.
- Legacy recovery tools use JSON_VALID(StepsJson) in semantic/Derived classification. JSON validity alone is insufficient for V2 objective approval.
- No runtime gate, catalog row or recovery tool was changed by this inspection.
- Next quest work requires source-column definitions, identity mapping and verified objective/reward semantics.
