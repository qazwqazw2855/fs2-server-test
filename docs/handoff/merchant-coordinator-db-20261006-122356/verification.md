# Merchant coordinator MariaDB verification — 2026-10-06

- Filtered MerchantCommandIntegrationTests: 1/1 passed, zero skipped.
- Real MariaDB purchase writer and inventory snapshot repository used.
- Blocked evidence gate leaves wallet 100 and creates no purchase receipt.
- Approved synthetic fixture purchase commits wallet 100 -> 60.
- Coordinator reads committed inventory and produces encoded 57-byte BUY response.
- A second fresh BUY is blocked before writing because inventory is nonempty.
- Existing durable BUY/SELL replay and interaction checks remain covered.
- Final wallet 64, inventory empty, inventory/mutation/wallet versions 2.
- Runner reported remaining_shop_fixture=0 and exit code 0.

## Limits

- Dedicated disabled fixture character; synthetic merchant and authority.
- This is coordinator plus MariaDB, not TCP plus MariaDB end-to-end.
- One filtered integration case, not a full integration suite.
- No formal evidence promotion, test001 wallet change or production deployment.
- Raw local evidence: command.log.
