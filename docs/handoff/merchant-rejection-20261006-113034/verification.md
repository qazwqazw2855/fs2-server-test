# Isolated merchant rejection probe

Date: 2026-10-06
Endpoint: 127.0.0.1:6002
Character: test001 / ID=1

- Login and World handshake passed.
- Received 1772-byte World bootstrap and NPC 3793.
- Sent synthetic BUY fixture without opening an NPC interaction.
- Received EOF without a transaction response.
- No BUY preparation log appeared.

Server evidence:

```text
2026-10-06T11:30:37.9183820+00:00 [2] Merchant transaction rejected: handle=5042; reason=NoCurrentNpcInteraction; closing connection.
```

This validates ingress rejection only. Successful preparation and purchase execution were not exercised.
Raw logs are retained locally alongside this summary.
