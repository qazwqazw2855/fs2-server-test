# Offline LoginServer.csvZ round-trip verification

Run from the repository root using .NET 10. No game, Launcher, database,
network connection or generated csvZ output is involved. The two inputs are
opened read-only; generated payloads and literal streams stay in memory.

The tool links the existing `God2PackedFile.cs` decoder and `ContentHash.cs`
without referencing server or persistence projects. Its literal writer follows
`RecoveryPipelineTests.PackLiterals`, retaining source header byte 6 (`0x5A`)
instead of the fixture writer's `0x00`.

For an entirely offline build, create a temporary NuGet configuration containing
`<configuration><packageSources><clear /></packageSources></configuration>` and run:

```sh
dotnet restore tools/God2.LoginServerRoundTrip/God2.LoginServerRoundTrip.csproj --configfile /tmp/god2-roundtrip-offline-nuget.config
dotnet build tools/God2.LoginServerRoundTrip/God2.LoginServerRoundTrip.csproj -c Release --no-restore
dotnet tools/God2.LoginServerRoundTrip/bin/Release/net10.0/God2.LoginServerRoundTrip.dll --self-test
dotnet tools/God2.LoginServerRoundTrip/bin/Release/net10.0/God2.LoginServerRoundTrip.dll
```

There are no package dependencies. Builds create only normal ignored bin/obj
outputs. No-argument verification pins the two known source hashes, packed and
decoded lengths, header and marker:

- `src/God2.V2Launcher/Resources/LoginServer.v2.csvZ`: 240 packed / 207 decoded bytes.
- `/srv/god2-reference/client-legacy/God2/LoginServer.csvZ`: 226 packed / 208 decoded bytes.

It checks decode → literal encode → decode byte equality, then replaces only
observed ASCII IP/port fields with `127.0.0.1` / `6002`. Exact comma/CRLF boundaries,
occurrence counts and lengths are required; ` asdf `, non-ASCII bytes and every
surrounding segment are preserved. Shorter IP strings may reduce payload length;
subsequent offsets are measured against the current payload. The patched payload
also undergoes round-trip verification. Packed hashes need not match.

Unknown source hashes, headers, markers, lengths, pattern counts, endpoints,
truncated input or mismatched output fail with exit code 1. Self-tests exercise
these failures, literal group boundaries, non-ASCII preservation and isolated
IP/port edits. No new config file is emitted, even on success.

**Format verification only:** this does not prove that the `6F2639…` original
client accepts new files, determine what `asdf` means, prove endpoint precedence,
or establish Taiwan official protocol evidence. Client acceptance remains a
separate investigation.
