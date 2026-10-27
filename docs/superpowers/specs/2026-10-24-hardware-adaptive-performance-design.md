# Hardware-Adaptive Performance Tuning

## Intent and why

The summarizer currently runs with static `Summarization` settings in
`appsettings.json` (fixed `GpuLayerCount`, `ContextSize`, etc.) and a
default model (`Gemma 4 12B`) that was chosen without regard to the
machine running it. On the author's laptop (8 logical cores, 32GB RAM,
integrated AMD/Intel graphics only — no discrete GPU) this produces
10-20 minute runs for PDFs longer than a few pages.

Two things are happening at once:
1. The hardware/model combination is mismatched (a 12B model with 20
   GPU layers forced onto an iGPU that shares system RAM, on a machine
   with no real discrete GPU to absorb that offload).
2. A few implementation inefficiencies waste time regardless of
   hardware (reloading model weights from disk on every queued task,
   a chunking strategy that doesn't scale with context size).

The project exists to fix both, and to make the fix **self-applying**:
this repo is going to be shared by cloning/pulling it, and the people
who run it won't be editing C# or `appsettings.json`. The app should
tune itself to whatever machine it's running on, out of the box.

## Goals

- Materially reduce wall-clock time per PDF on the author's laptop
  (CPU-only / iGPU-only class hardware) without gutting output quality.
- When someone else clones the repo and runs it on different hardware
  (more/fewer cores, more/less RAM, with or without a real discrete
  GPU), the app picks sensible `Threads` / `GpuLayerCount` / `ContextSize`
  / batch size automatically, with no config editing required.
- Keep the existing manual-override path working: anyone who wants to
  hand-tune `appsettings.json` still can, and their explicit values win.
- Surface which model best fits the detected hardware in the UI, as a
  hint only.

## Non-goals

- Not building an installer/packaged distribution. Sharing happens via
  `git clone`/`git pull`; "zero-config" means "the checked-in defaults
  self-tune," not "there's a setup wizard."
- Not auto-switching the user's manually selected model. The user picks
  a model in the UI; the app never silently swaps it for them, even if
  it's a poor fit for their hardware (confirmed: recommend-only, via
  a badge).
- Not attempting precise VRAM measurement or vendor-specific tuning
  tables. GPU detection is best-effort and conservative, not exact.
- Not re-architecting the queue/worker model, PDF extraction, or output
  writing. Scope is limited to model-serving configuration + the two
  named inefficiencies.
- Not adding telemetry/benchmarking UI. No dashboards; this is about
  picking good defaults, not reporting on them.

## Anti-goals (would be a failure even if it technically works)

- Defaults that crash or silently hang on a lower-spec machine than the
  author's (e.g. a 4-core/8GB laptop) because a tier was tuned only for
  32GB/8-core. Conservative tiers for the low end matter more than
  aggressive tiers for the high end.
- Any change that makes output quality worse on the author's current
  setup in the name of speed (e.g. forcing a smaller model, shrinking
  context below what a document needs) without the user asking for it.
- Overriding an explicit `appsettings.json` value the user set on
  purpose. Auto-tune must be easy to turn off per-value or globally.

## Constraints

- Target framework is `net10.0`, Windows-first (the author's dev/run
  environment), but `Dockerfile` targets Linux — GPU detection must
  degrade gracefully (treat as "no GPU detected" → CPU-only tuning) on
  any platform where the Windows-specific probe doesn't apply, rather
  than throwing.
- No new heavy dependencies for hardware detection. RAM detection uses
  `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes` (built into the BCL,
  cross-platform). GPU detection on Windows uses `System.Management`
  (WMI, `Win32_VideoController`) — a single, well-known package.
- `LLamaSharp` already ships CPU, Vulkan (incl. Windows), and CUDA12
  backends side by side in the `.csproj`; backend *selection* at the
  native-library level is already automatic. This project only tunes
  the *parameters* (`GpuLayerCount`, `Threads`, `ContextSize`, batch
  size) that LLamaSharp uses once a backend is loaded — it does not
  change backend selection itself.
- Must preserve existing behavior for anyone who already has explicit
  values in their `appsettings.json`/environment config: auto-tune only
  fills in values the user hasn't pinned.

## Decided design

### 1. `HardwareProfileService` (new, `Services/HardwareProfileService.cs`)

Computed once, cached for the process lifetime (singleton). Exposes a
`HardwarePlan` with:

- `LogicalCores` — `Environment.ProcessorCount`.
- `TotalMemoryBytes` — `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes`.
- `GpuTier` — `None`, `Integrated`, or `Discrete`, detected via WMI on
  Windows (`Win32_VideoController`). **Scan every returned adapter**
  (not just the first), skipping any named "Basic Render"/"Microsoft";
  classify each by name pattern (`Discrete` for `NVIDIA`, `Radeon RX`,
  `Radeon Pro`, `Arc A`/`Arc B`; `Integrated` for e.g. `Radeon
  Graphics`, `Iris`, `UHD`). `GpuTier = Discrete` if **any** adapter
  matches a discrete pattern, else `Integrated` if any adapter was
  found at all, else `None`. This matters on hybrid laptops (Intel
  iGPU + NVIDIA dGPU, common on gaming/workstation hardware) that
  report both adapters, often iGPU first — picking only the first
  adapter would misclassify these as integrated-only and silently
  disable GPU offload on exactly the hardware this tier exists to
  serve. (My call — the author's own laptop has no dGPU and can't
  surface this case in testing, so "scan all, discrete wins" is the
  safer default rather than "first adapter wins".) On non-Windows, or
  if the WMI query fails for any reason, `GpuTier = None`.
- `RecommendedThreads` = `max(1, LogicalCores - 2)` (same formula
  already used in `LlamaSharpSummarizer.ResolveThreadCount`, moved here
  so it's computed once and shared).
- `RecommendedGpuLayerCount`:
  - `GpuTier == None` → `0`
  - `GpuTier == Integrated` → `0` (older iGPUs such as Intel UHD/Iris Xe
    or AMD Vega are assumed net-negative unless the user opts in by
    setting an explicit value)
  - `GpuTier == ModernIntegrated` → all layers (`999`), **revised
    2026-10-26 after benchmarking**. Applies to Intel Arc integrated and
    AMD Radeon 7xxM/8xxM/8050S/8060S. Applied only when the model file is
    at most `Summarization:IntegratedGpuMaxModelPercentOfRam` (default 40)
    percent of total RAM, since the iGPU borrows system memory; larger
    models stay on CPU. Measured on an Intel Arc 140V (32 GB RAM, Vulkan),
    full offload compared to 0 layers: Phi-4 Mini read the prompt at
    141 vs 96 tok/s and generated 9.9 vs 2.8 tok/s; Gemma 12B read at
    84 vs 49 tok/s and generated 4.8 vs 3.3 tok/s. A CPU-only backend read
    at just 20 tok/s.
  - `GpuTier == Discrete` → `32` (a reasonable default partial-offload
    count; still capped at runtime by however many layers the loaded
    model actually has)
- `RecommendedContextSize` tiers off `TotalMemoryBytes`:
  `<=8GB → 4096`, `<=16GB → 8192`, `<=32GB → 16384`, `>32GB → 32768`
  (still subject to the existing `ResolveContextSize` clamping against
  the chosen model's `PreferredContextSize`/`MaxContextSize`).
- `RecommendedBatchSize`: `256` when `GpuTier == None`, `512` otherwise.

### 2. `Summarization:HardwareAutoTune` (new config flag, default `true`)

In `SummarizationOptions`: add `bool HardwareAutoTune { get; set; } = true`.

Semantics, implemented in `LlamaSharpSummarizer` where `ModelParams` is
built: for each of `Threads`, `GpuLayerCount`, `ContextSize` (its
component feeding `ResolveContextSize`'s default), and a new
`BatchSize` option, use the hardware-recommended value **only when
`HardwareAutoTune` is true AND the config value is left at its
sentinel ("not set")**. Sentinel convention: `Threads = 0` (already the
existing "auto" sentinel — no change needed there), `GpuLayerCount =
-1` (new sentinel; `0` now means "explicitly force CPU-only", distinct
from "let auto-tune decide"), `BatchSize = 0` (new option, `0` = auto).
`ContextSize`/`MinimumContextSize` keep their existing numeric meaning;
auto-tune only supplies `ContextSize` when it's at the default shipped
value (treat the shipped `32768` default in `SummarizationOptions` as
also meaning "unset" for this purpose, since `appsettings.json` always
provides an explicit value today — ship the checked-in
`appsettings.json` value as `0` going forward to make "unset" explicit
and unambiguous, replacing the current literal `8192`).

**Resolved (confirmed with user):** as written above, this would never
actually engage — every shipped model in `appsettings.json` has
`PreferredContextSize` set, and `ResolveContextSize`'s existing
precedence (`modelDetails.PreferredContextSize.GetValueOrDefault(_options.ContextSize)`)
always prefers the model's value, so the config-level `ContextSize`
fallback is dead code for every real model today. To make RAM-based
tuning actually take effect, `ResolveContextSize` must change: cap the
model's preferred size with the RAM recommendation —
`desired = Math.Min(modelDetails.PreferredContextSize.GetValueOrDefault(_options.ContextSize), hardwarePlan.RecommendedContextSize)`
— then continue through the existing `MinimumContextSize` floor and
`MaxSafeContextSize`/`MaxContextSize` clamps unchanged. This is an
authorized change to `ResolveContextSize`'s precedence, not just a
fallback value.

Ship `appsettings.json` with `GpuLayerCount: -1`, `BatchSize: 0`,
`ContextSize: 0`, `HardwareAutoTune: true` as the new committed
defaults, so a fresh clone self-tunes immediately. The existing
`Threads: 0` stays as-is (already means auto).

### 3. Model weights caching (`LlamaSharpSummarizer`)

Today, `SummarizeAsync` calls `LLamaWeights.LoadFromFile` and builds a
fresh `StatelessExecutor` on every invocation — reloading the full GGUF
file from disk for every queued PDF/audio task even when the same model
is used repeatedly.

Add a small internal cache keyed by `(modelPath, resolvedContextSize,
GpuLayerCount)` (the three `ModelParams` fields that affect whether
previously-loaded weights are reusable) holding the loaded
`LLamaWeights` + `StatelessExecutor`. On a cache miss, dispose the
previous entry (bounding memory to one loaded model at a time — this
app processes one task at a time per the existing `QueueOptions`) and
load fresh. Guard with a `SemaphoreSlim` so concurrent calls (if
`WorkerCount` is ever raised above 1) don't double-load.

### 4. Context-scaled map-reduce chunking (`LlamaSharpSummarizer`)

Replace the fixed `chunkSize = 12000` (chars) with a value derived from
`resolvedContextSize`: `chunkChars = (int)((resolvedContextSize -
_options.ContextSafetyReserveTokens) * 3.3) `, clamped to a sane floor
(e.g. `4000`) so very small context sizes don't produce degenerate
chunks. `3.3` is an approximate chars-per-token ratio for English
prose; it's a heuristic, not exact — document it as such in a code
comment.

### 5. FlashAttention auto-enable

When `HardwareAutoTune` is true and `FlashAttention` is left at its
sentinel, enable it only when `GpuTier != None` (CPU-only flash
attention support/benefit is inconsistent in llama.cpp). Sentinel for a
bool option: add `bool? FlashAttention` is overkill for one flag —
simpler: keep `FlashAttention` as today's plain `bool` (default
`false`, meaning "off unless the user turns it on"), and do **not**
auto-enable it. This is a deliberate scope cut: flash attention
correctness varies enough by backend/model that auto-enabling it risks
the anti-goal of breaking a working setup. Leave it manual.

### 6. UI recommendation badge (`Pages/Index.cshtml` + `.cs`)

`IndexModel` gets the `HardwareProfileService` injected. When building
`ModelChoices`, compute the best-fit model the same way
`RecommendedContextSize`'s RAM tier works, matched against each
`ModelDetails`' `PreferredContextSize` (as a rough proxy for model
size, since `ModelDetails` doesn't currently carry a parameter-count or
RAM-requirement field) — pick the entry whose `PreferredContextSize` is
the largest one `<=` the RAM-tier's context ceiling. **Tie-break (my
call):** several shipped models share the same `PreferredContextSize`
(e.g. Phi-4 Mini 3.8B, Gemma 12B, and Gemma 26B-MoE are all `32768`)
despite very different RAM footprints, so `PreferredContextSize` alone
can't disambiguate safely — recommending the 26B MoE model to a modest
machine would defeat the point of the hint. Add a small explicit
`ApproxSizeGb` (or similar) field to `ModelDetails` and `appsettings.json`
for each catalog entry (rough quantized-file-size estimate, already
knowable from each model's actual `.gguf` size on Hugging Face), and use
it as the tie-break (and primary signal, if simpler) instead of
`PreferredContextSize` for picking the badge. This is a small addition
within the existing "UI recommendation badge" scope, not a new feature.
Does not change `SelectedModelName` or the existing `Default`-flag
preselection behavior.

## What's left to the builder

- Exact WMI query shape/error handling and which discrete-vendor name
  substrings to match — reasonable engineering judgment, not a design
  decision.
- Exact constants chosen above (`32` layers for discrete, `3.3`
  chars/token, RAM tier breakpoints, `4000`-char chunk floor) are the
  author's best-effort defaults; treat them as a starting point, not a
  contract — adjust if testing shows a clearly better number, and note
  any change.
- Whether the model-weights cache lives inside `LlamaSharpSummarizer`
  directly or as a separate injected service is an implementation
  detail; either is fine as long as it's a singleton-scoped cache
  holding at most one loaded model.
- Test strategy for hardware detection (e.g. abstracting the WMI call
  behind an interface for unit testing vs. integration-testing the
  computed tiers directly) — follow whatever pattern
  `SummaryGenerator.Tests` already uses for other service seams.
