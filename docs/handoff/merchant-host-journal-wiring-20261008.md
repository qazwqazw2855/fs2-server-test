# Host merchant journal wiring — 2026-10-08

Host wires both BUY and SELL through durable journal decorators when
GOD2_ENABLE_MERCHANT_EXECUTION=1. Default execution wiring is disabled.

GOD2_ENABLE_MERCHANT_SALE_EXECUTION=1 additionally enables the SELL path
and requires the common execution flag. Economy and item identity
repositories are provided with execution wiring.

Both evidence gates remain Blocked. These flags do not approve official
transactions. Restricted inventory bootstrap remains at its default.

Validation:
- Host Release build passed.
- SELL flag without common execution flag: rejected, exit 2.
- Execution with incomplete DB configuration: rejected, exit 2.
- Default disabled / BUY wiring / BUY+SELL wiring: 3/3 startup checks passed.
- Startup checks used synthetic DB credentials and random loopback ports.
- No login or transaction requests were sent.
- All test processes were stopped.

Production deployment and official client acceptance remain pending.
