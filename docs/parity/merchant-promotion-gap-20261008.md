# Merchant promotion gap — 2026-10-08

BUY and SELL production evidence gates remain Blocked.

## Classic bounded profile
Build god2-opt-6b127086e0c0; handle 3954; client item 6901;
canonical item 253231541; quantity 1; purchase index 7;
sale client slot 4 mapped to authority slot 0.

Classic examples provide regression coverage only.
Original attempt-759 trace and Stage 4-6 exports, hashes,
capture metadata and provenance remain unverified.

## Separate CN official batch
Supplemental CN_OFFICIAL observation on 2026-10-04:
handle 1504; item 6906; purchase index 13; sale slot value 12;
two quantity-1 purchases and one quantity-2 sale.

Capture-specific decoding passed checksum.
Canonical NPC/merchant identity, authority slot mapping,
cross-build applicability and pricing semantics remain unresolved.
Quantity-2 sale is outside the current quantity-1 V2 boundary.
Do not merge this batch with the Classic profile.

## Implementation verification
SELL journal, separate-process read-only recovery and failure/cancellation
tests passed. Host journal wiring and startup/configuration checks passed.
Synthetic fixtures do not approve official protocol or content promotion.

## Next evidence work
Locate and verify original attempt-759 trace and Stage 4-6 exports,
or collect a separately identified TW official capture for the exact client.
Review protocol and content mappings independently before gate approval.
Production deployment and original-client acceptance remain pending.
