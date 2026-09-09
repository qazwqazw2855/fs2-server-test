# God2 Client Instrumentation

Authorized, local-only dynamic instrumentation for the official x86 `God2_opt.exe` login flow.

This tool does not modify the on-disk client binary, launcher, or unified server. General logs contain only metadata. Raw payload bytes are written only under the LoginTrial sensitive artifact directory.

## Main Commands

- Build both x86 configurations:
  `powershell -ExecutionPolicy Bypass -File tools/God2.ClientInstrumentation/scripts/Build-Instrumentation.ps1 -Configuration Both`
- Prepare a run manifest:
  `powershell -ExecutionPolicy Bypass -File tools/God2.ClientInstrumentation/scripts/Prepare-LoginTrial.ps1`
- Ensure local test accounts:
  `powershell -ExecutionPolicy Bypass -File tools/God2.ClientInstrumentation/scripts/Ensure-TestAccounts.ps1 -RunDir <run-dir>`
- Start DB-independent instrumentation endpoint:
  `powershell -ExecutionPolicy Bypass -File tools/God2.ClientInstrumentation/scripts/Start-InstrumentationEndpoint.ps1 -RunDir <run-dir>`
- Smoke-test DB-independent endpoint:
  `powershell -ExecutionPolicy Bypass -File tools/God2.ClientInstrumentation/scripts/Invoke-InstrumentationEndpointSmoke.ps1 -RunDir <run-dir>`
- Start instrumented client:
  `powershell -ExecutionPolicy Bypass -File tools/God2.ClientInstrumentation/scripts/Start-LoginTrial.ps1 -RunDir <run-dir>`
- Wait for one controlled sample:
  `powershell -ExecutionPolicy Bypass -File tools/God2.ClientInstrumentation/scripts/Wait-LoginSample.ps1 -RunDir <run-dir> -Sample A`
- Stop and analyze:
  `powershell -ExecutionPolicy Bypass -File tools/God2.ClientInstrumentation/scripts/Stop-LoginTrial.ps1 -RunDir <run-dir>`
  `powershell -ExecutionPolicy Bypass -File tools/God2.ClientInstrumentation/scripts/Analyze-LoginTrial.ps1 -RunDir <run-dir>`

## Trace Files

- `general.log`: safe metadata only.
- `sensitive/trace.bin`: binary trace records with raw payload bytes.
- `metadata.jsonl`: safe per-event metadata used by sample watchers.
- `markers.jsonl`: A/B/C/D sample timing markers.

`Ensure-TestAccounts.ps1` is only for the later formal MariaDB-backed trial. The first instrumentation gate can use the local endpoint mode, which performs no formal authentication and does not replay a login success response.

## Generic Probe Plan

`GenericProbePlan/1` is a data-driven x86 FunctionStart observer. A plan can
rotate up to three exact-build candidates without rebuilding the DLL and can
capture ECX, EDX, and up to eight stack words while following return, caller,
and up to four consumer instructions.

The JSON source contract is
`tools/God2.PacketCapture/ultimate/assets/schemas/generic-probe-plan-v1.schema.json`.
Start from `tools/God2.ClientInstrumentation/generic-probe-plan.example.json`,
replace the RVA and eight signature bytes with a verified candidate, then use:

```powershell
powershell -ExecutionPolicy Bypass -File Automation/Invoke-GenericProbePlan.ps1 `
  -Action Apply -TargetProcessId <pid> -Module <dll-base> -PlanPath <plan.json>

powershell -ExecutionPolicy Bypass -File Automation/Invoke-GenericProbePlan.ps1 `
  -Action Query -TargetProcessId <pid> -Module <dll-base>

powershell -ExecutionPolicy Bypass -File Automation/Invoke-GenericProbePlan.ps1 `
  -Action Clear -TargetProcessId <pid> -Module <dll-base>
```

Apply is fail-closed on client identity, PID/creation time, executable RVA,
exact target bytes, debug-register conflicts, schema bounds, and lane mapping.
Plan rotation is rejected until the previous queue is drained. Raw register,
stack, and pointer values never enter the semantic wire; the flush worker emits
session-scoped `ValueToken` and `ObjectToken` values instead.

`GENERIC_FORMAL_GATE=PASS` proves the Generic Probe queue has P0/P1 loss 0,
sequence gaps 0, pending 0, and active invocations 0. Formal session acceptance
must also retain the existing shared semantic ring's P0/P1 loss and drain gates.
