# Feed Face Distance Inspection Implementation Plan

**Goal:** Add opt-in fixed-position distance inspection before loading, without changing the other three faces.

**Architecture:** A small tested cycle/operation gate tracks pending inspection and prevents concurrent loading or table motion. A hardware adapter samples through the existing serial protocol. The master flow inspects before SetupForFeed; the loading consumer independently checks the gate, including stale FeedStatus after restart. A separate UI partial adds settings without rewriting the designer.

**Tech Stack:** C# WinForms, .NET Framework, PowerShell regression scripts, MSBuild Debug/x64.

- [ ] Add failing C# gate/measurement tests and integration contracts in tests/feed-distance-inspection-regression.ps1.
- [ ] Add Class/FeedDistanceInspectionState.cs: request generations, exclusive operation scopes, pending completion and numeric validation. Run behavioral tests.
- [ ] Add Class/FeedDistanceInspection.cs and cancellable serial distance reading; log invalid samples and resource-cleanup failure. No fake MES ID.
- [ ] Connect schedule requests, master pre-feed inspection, loading consumer gate and turntable command interlock. Recheck cancellation, state-reset ordering and stale-ready races.
- [ ] Add product settings and parameter5 controls (delegated bounded UI/config task); include new files in UI.csproj.
- [ ] Review changes against approved spec, run regressions and Debug/x64 build. Do not start hardware software. Preserve pre-existing camera edits; no version bump or feature commit requested.

Regression command: powershell -NoProfile -ExecutionPolicy Bypass -File tests/feed-distance-inspection-regression.ps1

Build command: MSBuild.exe WLTmini.sln /t:Build /p:Configuration=Debug /p:Platform=x64 /m /v:minimal /nologo

Required behavior cases: disabled and no-pending skip, duplicate completion, failed/stopped retry, new request during active inspection, inspection versus loading admission, motion exclusion, single enabled face, invalid/NaN/infinite/nonpositive measurements, inclusive tolerance boundary. Integration checks distinguish executable state tests from source contracts. Hardware timing/serial framing and optical geometry remain machine validation items.
