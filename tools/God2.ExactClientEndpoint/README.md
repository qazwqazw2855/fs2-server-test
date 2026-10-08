# Exact Client Endpoint static analysis

This independent .NET 10 tool reads only:
`/srv/god2-reference/client-current-6f2639/God2_opt.exe`.
Before analysis it requires SHA256:
`6F2639A0A7AD25053D0364108147173EB68BD04F57E6942491F42633F40052BC`.
It does not use the older `6B127086…` client's RVAs or anchors.

From the repository root, with Iced 1.21.0 already cached:

```sh
dotnet restore tools/God2.ExactClientEndpoint/God2.ExactClientEndpoint.csproj --configfile tools/God2.ExactClientEndpoint/NuGet.Config
dotnet build tools/God2.ExactClientEndpoint/God2.ExactClientEndpoint.csproj -c Release --no-restore
dotnet tools/God2.ExactClientEndpoint/bin/Release/net10.0/God2.ExactClientEndpoint.dll --self-test
dotnet tools/God2.ExactClientEndpoint/bin/Release/net10.0/God2.ExactClientEndpoint.dll
```

NuGet sources are cleared for offline restore. The tool writes results only to
stdout, creates no analysis reports, and never runs the Client or connects to
any network. Unsupported arguments or invalid input produce `SAFE_STOP` and
exit code 1. Successful analysis may still report UNKNOWN.

## Current evidence

- **VERIFIED PE import:** `shell32.dll!CommandLineToArgvW`; name file offset
  `0x1B34C7`, name RVA `0x6460C7`, IAT RVA `0x645FC5`.
  Import presence does not prove an executed call or parameter use.
- The entry control flow is statically traced through `push`, direct `call`
  and two `ret` instructions to RVA `0x645001`, file offset `0x1B2401`, `pusha`.
  The two return targets derive from modeled known stack values.
- **UNKNOWN:** `pusha` effects and subsequent flow, actual
  `CommandLineToArgvW` invocation, LoginServer/God2Con consumers, socket endpoint
  construction, and endpoint selection/overwrite precedence.
- The `connect` string hit is inside `CoDisconnectObject`, not socket-call proof.

The trace accepts only explicitly modeled instructions. Limits are 64 decoded
instructions, 16 control transfers and 16 modeled stack values. Repeated states,
unbacked or overlapping targets, unknown stack values, indirect control and
unmodeled instructions stop the trace. There is no emulation, unpacking, runtime
memory inspection or broad linear disassembly.

Self-tests cover synthetic control flow and parser boundaries; synthetic bytes
are not Client evidence. This tool provides **static evidence only**, cannot
prove runtime behavior, does not interpret `asdf 6001`, and does not establish
Taiwan official-server protocol evidence. Endpoint precedence remains UNKNOWN.
