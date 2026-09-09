# God2 Semantic Recovery Engine

Version 1.3.0 Ultimate Private Edition is a native x64, statically linked semantic-recovery and packet-evidence
tool. The release artifact is a single `God2SemanticRecoveryEngine.exe`; SQLite and the exact-target x86 probe
are embedded, so the player does not need to install a runtime, script, driver, CUDA toolkit, or command-line
tool.

The compact GUI deliberately has two primary actions: `開始完整恢復取證` and
`停止並產生 Codex 恢復包`. Recovery, probe, pipeline, GPU, AI/ML, and coverage state are read-only. The
generated recovery bundle is integrity-manifested and intended for the separate read-only server/database
importer; the client tool never writes a production server database.

Standard capture does not modify the game process. Ultimate capture can use the embedded x86 DLL only after it
verifies the exact `God2_opt.exe` path, x86 architecture, version 1.0.0.1, and approved SHA-256. It requests normal UAC,
briefly pauses the verified game's primary thread, loads the embedded x86 DLL through a target-resolved
`LoadLibraryW` remote thread, and restores its suspend count on both success and failure paths. The DLL hooks only
Winsock boundaries plus the explicitly verified packet-decode, outbound-frame-builder, and battle-record-length
choke points. Broad process-memory scans, keyboard, mouse, window, arbitrary-process, version-parser, and login
dispatch probes are disabled in this product mode. Every unknown hook remains
`EvidenceBlockedUnconfirmedProbe`; failure of one domain is isolated while bounded capture continues.

The GUI reports enhancement active only after `God2TraceProbeWaitReady` confirms that the trace file, flush
worker, and Winsock hooks initialized successfully. Stop restores the installed hooks, drains active hook calls,
flushes evidence, and calls `God2TraceProbeCanUnload`. If the game cached a dynamically remapped Winsock pointer,
the stopped DLL remains loaded but inert until game exit; forced unload is intentionally avoided because that
cached pointer would otherwise become invalid.

`God2_opt.exe` is created by `Launcher.exe`. After Launcher.exe starts God2_opt.exe, enter the account
credentials in the God2_opt.exe game login screen; credentials are not entered in Launcher.exe. Because only
the creating process can add `CREATE_SUSPENDED` to that `CreateProcess` call, the production GUI does not bypass or replace the launcher.
It uses the login-safe equivalent sequence: detect the exact x86 process, suspend its primary thread, inject,
and resume. The separate instrumentation test launcher retains a true `CREATE_SUSPENDED` launch path for
isolated client/probe testing where the launcher contract is not required.

The raw-backend privacy guard, x86 masked semantic consumer, game-process detection, and analysis are tracked
independently. System-wide raw ETW/PktMon capture is unavailable under `EvidenceBlockedPrivacyPolicy`; it never
starts and does not persist ETL/PCAPNG packet artifacts. If the exact-build x86 semantic consumer fails, the
workflow reports `EvidenceBlocked`/`PostCaptureFallback` and finalizes only already-sanitized semantic evidence.
Startup states remain cancellable from the GUI.

The supported systems are Windows 10 x64 and Windows 11 x64. Runtime probing exposes the unavailable raw
backend state and the independently available exact-build x86 masked semantic workflow; it never promotes the
privacy guard to an active packet-capture backend.

Version 1.3.0 Ultimate extends the optional, capability-dispatched NVIDIA GPU path with verified feature
projection for pattern correlation, similarity scoring, feature extraction, clustering, graph scoring, and
semantic-candidate scoring in addition to checksum work. `Auto` dynamically loads the installed NVIDIA Driver
API when a compatible SM 6.1-or-newer device is
available, then applies an Adaptive GPU Benefit Gate before dispatch: GPU availability alone never selects the
GPU. The gate evaluates architecture tier, compute capability, record/byte/average-payload workload shape,
operation arithmetic intensity, transfer and launch cost, batch/queue shape, GPU load, Live/PostCapture policy,
and bounded session-observed calibration. Small or predicted-slower work stays on CPU even on a Maximum-tier
device. `CPU Only` keeps the complete deterministic CPU path. No CUDA toolkit or NVIDIA runtime binary is
redistributed, and a missing driver, unsupported device, initialization error, GPU memory pressure, or batch
failure automatically falls back to CPU without dropping evidence. The capture DLL, injection lifecycle,
packet boundaries, ordering, storage commits, evidence manifests, and all authority decisions remain CPU-only.
Every GPU-produced candidate feature is independently recomputed and verified by the CPU before the existing
frame and semantic analysis may consume it.

The operation model has independent cost classes for checksum, pattern correlation, similarity scoring,
feature extraction, clustering, graph scoring, semantic candidate scoring, and future AI inference. CPU and
GPU outputs are compared field-for-field before they can be used. Deterministic local ML can rank recovery
candidates only as `HYPOTHESIS`; because no licensed, hash-bound production model is bundled, model-backed
inference is reported as `EvidenceBlockedModelUnavailable` rather than a fabricated success.

Evidence ZIPs separately record GPU availability, eligibility, gate selection, actual selected backend,
selection reason, workload, predictions, calibration, actual CPU/GPU/wall times, scheduler/failure diagnostics,
and equivalence under `PrimarySession/performance/`. `GpuAvailableButCpuPreferred` is a normal successful state,
not a GPU failure. The GUI intentionally exposes only `Auto` and `CPU Only`; thresholds, batch, concurrency,
memory and utilization controls remain adaptive internal policy.

Build with the Visual Studio x64 developer environment:

```text
msbuild God2.PacketCapture.vcxproj /m /p:Configuration=Release /p:Platform=x64
```

Normal no-argument startup opens the GUI. Hidden `--internal-*` modes are reserved for the single EXE's
privileged worker, offline reanalysis, and automated verification; players do not need a separate CLI.
