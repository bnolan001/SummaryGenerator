# Branch Review Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the "do now" findings from the `hardware-profiling` branch review so large PDFs summarize reliably on any hardware, GPU detection is accurate, `ContextSize: 0` means auto, scratchpad stripping can't delete real content, and logging is clean.

**Architecture:** Replace the character-estimate map-reduce with a token-counted one. A pure `TokenBudget` computes output/prompt limits that mathematically guarantee every reduce level shrinks. A new `MapReduceSummarizer` orchestrates single-shot / map / reduce against an `ISummaryModel` seam, so the stall and the context bound are unit-testable with a fake model. `LlamaSharpSummarizer` becomes a thin adapter (load weights → build `LlamaSummaryModel` → call orchestrator). Remaining items are small, independent edits.

**Tech Stack:** .NET 10, ASP.NET Core Razor Pages, LLamaSharp 0.27.0 (`LLamaWeights.Tokenize(string, bool addBos, bool special, Encoding)`), Serilog, xunit 2.9.3.

**Spec:** Branch review findings (conversation, 2026-10-25) — items: Standards 1/Spec 6 (reduce stall), Spec 7 (token bound), Spec 8 (safe scratchpad strip), Standards 2 (Arc match), Spec 1–2 (`ContextSize: 0`), Spec 4 (skip Microsoft adapters), Spec 5 + Standards 5–6 (logging), Standards 7 (doc comments), Standards 9 (duplicated separator). Underlying design: `docs/superpowers/specs/2026-10-24-hardware-adaptive-performance-design.md`; GitHub Issue #1.

## Global Constraints

- Keep LLamaSharp at **0.27.0**. After any build, run `git --no-pager diff --stat -- *.csproj`; revert any auto-bumped package versions (`git checkout -- <csproj>`). Never commit csproj version changes.
- If a native `llama.cpp:46: fatal error` appears, delete `bin/` + `obj/` of both projects and rebuild before debugging (stale-native-DLL trap).
- Existing style: block-scoped namespaces (`namespace X { }`), primary constructors where already used, nullable enabled, implicit usings. Models in `SummaryGenerator/Models`, services in `SummaryGenerator/Services`, tests in `SummaryGenerator.Tests/Unit` named `<Class><Method>Tests.cs`.
- Explicit (non-sentinel) user config always wins over hardware recommendations, except the RAM context cap (Issue #1).
- No test may load a real GGUF model (repo convention).
- Test command: `dotnet test SummaryGenerator.Tests --nologo --filter "FullyQualifiedName~<TestClass>"`; full suite: `dotnet test SummaryGenerator.Tests --nologo`. Baseline: 59 passing.
- Commits go on branch `hardware-profiling` (PR #2) with trailer `Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>`.

## Review Focus

1. **Dense text (≈2 tokens/char: tables, numbers, citations)** — must still never exceed context. Pinned in Task 4 (`SplitToTokenBudget_DenseText_NoChunkExceedsBudget`) and Task 5 (`SummarizeAsync_DenseDocument_EveryPromptFitsContext`).
2. **Minimum context (4096) with `MaxTokens` 2048 and every summary at max length** — reduce must terminate. Pinned in Task 5 (`SummarizeAsync_MinimumContext_MaxLengthSummaries_Completes`).
3. **Model emits nothing / empty summaries** — reduce must still terminate and return. Pinned in Task 5 (`SummarizeAsync_EmptyIntermediateSummaries_StillCompletes`).
4. **Your own laptop's iGPU (`Intel(R) Arc(TM) 140V GPU (16GB)`)** — must stay `Integrated`. Pinned in Task 2.
5. **Scratchpad closing tag placed after the sections** — sections must survive. Pinned in Task 1.

---

### Task 1: Safe scratchpad stripping

**Files:**
- Modify: `SummaryGenerator/Services/SummaryOutputCleaner.cs` (`RemoveReasoningArtifacts`, `ScratchpadBlockRegex`)
- Test: `SummaryGenerator.Tests/Unit/SummaryOutputCleanerTests.cs`

**Rule:** never delete a Markdown heading line. A closed `<scratchpad>…</scratchpad>` block is removed only if it contains no line starting with `#`. Otherwise fall through to the existing "unclosed: remove up to next heading" regex, then strip any leftover `<scratchpad>`/`</scratchpad>` tags.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public void Clean_ScratchpadClosedAfterSections_KeepsSections()
{
    var raw = "<scratchpad>\n- Theme\n# 1. Executive Summary\nBody\n</scratchpad>";
    Assert.Equal("# 1. Executive Summary\nBody", SummaryOutputCleaner.Clean(raw, []));
}

[Fact]
public void Clean_ScratchpadBlockBetweenSections_IsRemoved()
{
    var raw = "# 1. Executive Summary\nBody\n<scratchpad>note</scratchpad>\n# 2. Key Terms\nTerms";
    Assert.Equal("# 1. Executive Summary\nBody\n\n# 2. Key Terms\nTerms", SummaryOutputCleaner.Clean(raw, []));
}
```

- [ ] **Step 2: Run** `dotnet test SummaryGenerator.Tests --nologo --filter "FullyQualifiedName~SummaryOutputCleanerTests"` — expect `Clean_ScratchpadClosedAfterSections_KeepsSections` FAIL (sections deleted).
- [ ] **Step 3: Implement** — change `ScratchpadBlockRegex` to `<scratchpad>(?:(?!^[ \t]*#).)*?</scratchpad>` with `Singleline | Multiline | IgnoreCase`. Update the method comment to state the "never delete a heading" rule.
- [ ] **Step 4: Run** same filter — expect all 11 PASS.
- [ ] **Step 5: Commit** `Make scratchpad stripping never delete section headings`

---

### Task 2: GPU classification accuracy (Intel Arc, Microsoft adapters, doc comments)

**Files:**
- Modify: `SummaryGenerator/Services/HardwareProfileService.cs` (make class `partial`; `DiscretePatterns`, `ClassifyGpu`; move misplaced `<summary>` blocks)
- Test: `SummaryGenerator.Tests/Unit/HardwareProfileServiceClassifyGpuTests.cs`

**Interfaces:** `public static GpuTier ClassifyGpu(IEnumerable<string> adapterNames)` — signature unchanged.

- [ ] **Step 1: Write failing tests**

```csharp
[Theory]
[InlineData("Intel(R) Arc(TM) A770 Graphics")]
[InlineData("Intel(R) Arc(TM) A370M Graphics")]
[InlineData("Intel(R) Arc(TM) B580 Graphics")]
[InlineData("Intel(R) Arc(TM) Pro A60 Graphics")]
public void ClassifyGpu_IntelArcDiscreteCard_ReturnsDiscrete(string name)
    => Assert.Equal(GpuTier.Discrete, HardwareProfileService.ClassifyGpu([name]));

[Theory]
[InlineData("Intel(R) Arc(TM) 140V GPU (16GB)")]
[InlineData("Intel(R) Arc(TM) 130V GPU")]
[InlineData("Intel(R) Arc(TM) 140T GPU")]
[InlineData("Intel(R) Arc(TM) Graphics")]
public void ClassifyGpu_IntelArcIntegrated_ReturnsIntegrated(string name)
    => Assert.Equal(GpuTier.Integrated, HardwareProfileService.ClassifyGpu([name]));

[Fact]
public void ClassifyGpu_OnlyMicrosoftVirtualAdapters_ReturnsNone()
    => Assert.Equal(GpuTier.None, HardwareProfileService.ClassifyGpu(
        ["Microsoft Remote Display Adapter", "Microsoft Hyper-V Video", "Microsoft Basic Display Adapter"]));

[Fact]
public void ClassifyGpu_MicrosoftAdapterAlongsideIntegrated_ReturnsIntegrated()
    => Assert.Equal(GpuTier.Integrated, HardwareProfileService.ClassifyGpu(
        ["Microsoft Remote Display Adapter", "Intel(R) UHD Graphics 620"]));
```

- [ ] **Step 2: Run** filter `HardwareProfileServiceClassifyGpuTests` — expect Arc-discrete and Microsoft-only tests FAIL.
- [ ] **Step 3: Implement**
  - Remove `"Arc A"`, `"Arc B"` from `DiscretePatterns`; add `[GeneratedRegex(@"\bArc(?:\(TM\))?\s+(?:Pro\s+)?[AB]\d{2,3}", RegexOptions.IgnoreCase)] private static partial Regex IntelArcDiscreteRegex();` and treat a name as discrete if it matches a pattern OR this regex.
  - Replace the `"Basic Render"` filter with an ignored-name filter: skip names containing `"Microsoft"` (covers Basic Render/Display, Remote Display, Hyper-V).
  - Move the `ClassifyGpu` `<summary>` from `DiscretePatterns` onto `ClassifyGpu`, and the `ComputePlan` `<summary>` from `BytesPerGb` onto `ComputePlan`.
- [ ] **Step 4: Run** filter — all PASS (including existing 5).
- [ ] **Step 5: Commit** `Detect Intel Arc discrete cards exactly and ignore Microsoft virtual adapters`

---

### Task 3: `ContextSize: 0` means auto

**Files:**
- Modify: `SummaryGenerator/Services/InferenceParameterResolver.cs` (`ResolveContextSize`)
- Modify: `SummaryGenerator/Models/SummarizationOptions.cs` (`ContextSize` default `32768` → `0`, add XML doc stating 0 = auto)
- Modify: `SummaryGenerator/appsettings.json` (`"ContextSize": 8192` → `0`)
- Test: `SummaryGenerator.Tests/Unit/InferenceParameterResolverContextSizeTests.cs`

**Behavior:** when the model has no `PreferredContextSize` and `options.ContextSize == 0`: auto-tune on → `plan.RecommendedContextSize`; auto-tune off → `8192` (`private const uint ContextSizeWhenUnsetAndAutoTuneOff = 8192`, the previously shipped value). Then existing floor/caps apply unchanged. Explicit non-zero `ContextSize` behaves exactly as today.

- [ ] **Step 1: Write failing tests** (reuse the file's existing option/plan/model construction helpers)

```csharp
[Fact]
public void ResolveContextSize_SentinelZero_AutoTuneOn_UsesRecommendation()
// options: ContextSize=0, MinimumContextSize=4096; model: no Preferred/Max; plan.RecommendedContextSize=16384
// Assert.Equal(16384u, InferenceParameterResolver.ResolveContextSize(options, model, true, plan));

[Fact]
public void ResolveContextSize_SentinelZero_AutoTuneOff_Uses8192()
// same inputs, hardwareAutoTune=false → Assert.Equal(8192u, ...)
```

- [ ] **Step 2: Run** filter `InferenceParameterResolverContextSizeTests` — expect both FAIL (return 4096).
- [ ] **Step 3: Implement** in `ResolveContextSize` and update the option default + appsettings value.
- [ ] **Step 4: Run** filter — all PASS (existing 5 unchanged).
- [ ] **Step 5: Commit** `Treat ContextSize 0 as auto and ship it as the default`

---

### Task 4: `TokenBudget` and token-bounded chunking

**Files:**
- Create: `SummaryGenerator/Services/TokenBudget.cs`
- Modify: `SummaryGenerator/Services/DocumentChunker.cs` (add `SplitToTokenBudget`; rename `GroupByCharBudget` → `GroupByBudget` and its parameters, update its one call site in `LlamaSharpSummarizer.cs`, rename test file/class to `DocumentChunkerGroupByBudgetTests`)
- Test: `SummaryGenerator.Tests/Unit/TokenBudgetTests.cs`, `SummaryGenerator.Tests/Unit/DocumentChunkerSplitToTokenBudgetTests.cs`

**Interfaces (produced):**
```csharp
public sealed record TokenBudget(uint ContextSize, int FinalOutputTokens, int IntermediateOutputTokens,
                                 int MaxFinalPromptTokens, int MaxIntermediatePromptTokens)
{
    public const int SafetyMarginTokens = 64;
    public const int MinimumUsableTokens = 256;
    public static TokenBudget Compute(uint contextSize, int maxOutputTokens, int promptOverheadTokens);
}
public static List<string> DocumentChunker.SplitToTokenBudget(string text, Func<string, int> countTokens, int maxTokens);
public static List<List<T>> DocumentChunker.GroupByBudget<T>(IReadOnlyList<T> items, Func<T, int> sizeSelector, int maxSizePerGroup);
```

**`Compute` math (this is what guarantees reduce progress):**
```
C = contextSize − promptOverheadTokens − SafetyMarginTokens
if C < MinimumUsableTokens → throw InvalidOperationException("Context size {contextSize} is too small for the prompt template ({overhead} tokens).")
FinalOutputTokens           = min(maxOutputTokens, C / 2)
IntermediateOutputTokens    = min(maxOutputTokens, C / 3 − 8)
MaxFinalPromptTokens        = contextSize − FinalOutputTokens − SafetyMarginTokens
MaxIntermediatePromptTokens = contextSize − IntermediateOutputTokens − SafetyMarginTokens
```
Invariants: overhead + 2·IntermediateOutputTokens + 16 ≤ MaxIntermediatePromptTokens (any two intermediate summaries plus separators fit one combine prompt) and overhead + IntermediateOutputTokens ≤ MaxFinalPromptTokens (any single summary fits the final prompt).

**`SplitToTokenBudget` algorithm:** if `countTokens(text) ≤ maxTokens` return `[text]`. Else `charBudget = max(1, text.Length * maxTokens / countTokens(text))` (strictly < `text.Length`, so it always progresses), split with existing `SplitIntoChunks(text, charBudget)`; any chunk still over budget is recursively split the same way. A 1-char chunk still over budget → `InvalidOperationException`. Concatenation of the result equals the input.

- [ ] **Step 1: Write failing tests**

```csharp
// TokenBudgetTests
[Fact] public void Compute_MinimumContext_ClampsOutputsSoTwoSummariesFit()
// TokenBudget.Compute(4096, 2048, 200) → FinalOutputTokens 1916, IntermediateOutputTokens 1269,
//   MaxFinalPromptTokens 2116, MaxIntermediatePromptTokens 2763
[Fact] public void Compute_LargeContext_KeepsConfiguredMaxOutput()
// Compute(16384, 2048, 200) → FinalOutputTokens 2048, IntermediateOutputTokens 2048
[Theory] [InlineData(4096u)] [InlineData(8192u)] [InlineData(16384u)] [InlineData(32768u)]
public void Compute_Invariants_HoldForAllTiers(uint ctx)
// overhead 300, maxOutput 2048: assert both invariants above
[Fact] public void Compute_ContextTooSmallForTemplate_Throws()
// Assert.Throws<InvalidOperationException>(() => TokenBudget.Compute(512, 2048, 300))

// DocumentChunkerSplitToTokenBudgetTests  (fake counters: s => s.Length, s => s.Length * 2)
[Fact] public void SplitToTokenBudget_FitsAlready_ReturnsSingleChunk()
[Fact] public void SplitToTokenBudget_NoChunkExceedsBudget()            // 50_000 chars of sentences, counter len, max 3000
[Fact] public void SplitToTokenBudget_DenseText_NoChunkExceedsBudget()  // counter len*2, max 3000
[Fact] public void SplitToTokenBudget_ConcatenationEqualsInput()
```

- [ ] **Step 2: Run** filters `TokenBudgetTests` and `DocumentChunker` — expect compile failure (types/methods missing).
- [ ] **Step 3: Implement** `TokenBudget.cs`, `SplitToTokenBudget`, and the `GroupByBudget` rename (keep `SplitIntoChunks` and, for now, `EstimateCharBudget` — removed in Task 6).
- [ ] **Step 4: Run** filters — all PASS; full suite PASS.
- [ ] **Step 5: Commit** `Add TokenBudget and token-bounded document chunking`

---

### Task 5: `MapReduceSummarizer` orchestrator behind an `ISummaryModel` seam

**Files:**
- Create: `SummaryGenerator/Services/ISummaryModel.cs`, `SummaryGenerator/Services/MapReduceSummarizer.cs`
- Test: `SummaryGenerator.Tests/Unit/MapReduceSummarizerTests.cs` (contains a private `FakeSummaryModel`)

**Interfaces:**
- Consumes: `TokenBudget.Compute`, `DocumentChunker.SplitToTokenBudget`, `DocumentChunker.GroupByBudget` (Task 4).
- Produces:
```csharp
public interface ISummaryModel
{
    int CountPromptTokens(string instruction, string content);   // tokens of the fully templated prompt
    Task<string> GenerateAsync(string instruction, string content, int maxOutputTokens, CancellationToken cancellationToken); // returns cleaned text
}
public sealed class MapReduceSummarizer(ISummaryModel model, ILogger logger)
{
    public const string ChunkInstruction   = "Summarize the following section of the document, focusing on key operational and strategic points:";
    public const string CombineInstruction = "Combine the following partial summaries into a single consolidated summary, preserving all key points:";
    public static readonly string SummarySeparator = Environment.NewLine + "---" + Environment.NewLine;
    public Task<string> SummarizeAsync(string documentText, string systemPrompt, uint contextSize, int maxOutputTokens, CancellationToken cancellationToken);
}
```

**Algorithm:**
1. `overhead = max(CountPromptTokens(i, ""))` over `{systemPrompt, ChunkInstruction, CombineInstruction}`; `budget = TokenBudget.Compute(contextSize, maxOutputTokens, overhead)`.
2. If `CountPromptTokens(systemPrompt, doc) ≤ budget.MaxFinalPromptTokens` → one `GenerateAsync(systemPrompt, doc, budget.FinalOutputTokens)`.
3. Map: `SplitToTokenBudget(doc, t => CountPromptTokens(ChunkInstruction, t), budget.MaxIntermediatePromptTokens)`; each chunk → `GenerateAsync(ChunkInstruction, chunk, budget.IntermediateOutputTokens)`.
4. Reduce loop: `combined = string.Join(SummarySeparator, summaries)`; if `CountPromptTokens(systemPrompt, combined) ≤ MaxFinalPromptTokens` → final `GenerateAsync(systemPrompt, combined, FinalOutputTokens)` and return. Else group with `combineOverhead = CountPromptTokens(CombineInstruction, "")`, `separatorTokens = CountPromptTokens(CombineInstruction, SummarySeparator) − combineOverhead`, `GroupByBudget(summaries, s => CountPromptTokens(CombineInstruction, s) − combineOverhead + separatorTokens, budget.MaxIntermediatePromptTokens − combineOverhead)`. Singleton groups pass through; multi-item groups → `GenerateAsync(CombineInstruction, string.Join(SummarySeparator, group), IntermediateOutputTokens)`. If the next level's count is not strictly smaller, throw `InvalidOperationException("Reduce step made no progress …")` (replaces `MaxReduceDepth`). Log each level at Information (count, tokens, budget).

**Fake model for tests:** tokens = `instruction.Length + content.Length` (1 char = 1 token); `GenerateAsync` records `(instruction, promptTokens, maxOutputTokens)` and returns `new string('x', n)` where `n` is configurable per test (default `maxOutputTokens`, i.e. worst case). A dense variant multiplies token counts by 2.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public async Task SummarizeAsync_ShortDocument_MakesSingleFinalCall()
// ctx 16384, doc 1_000 chars → 1 call, instruction == systemPrompt, maxOutputTokens == budget.FinalOutputTokens
[Fact] public async Task SummarizeAsync_LargeDocument_EveryPromptFitsContext()
// ctx 16384, doc 400_000 chars → every call: promptTokens + maxOutputTokens + TokenBudget.SafetyMarginTokens ≤ 16384; last call uses systemPrompt
[Fact] public async Task SummarizeAsync_MinimumContext_MaxLengthSummaries_Completes()
// ctx 4096, MaxTokens 2048, doc 400_000 chars, fake always returns maxOutputTokens chars → completes, all prompts fit
[Fact] public async Task SummarizeAsync_DenseDocument_EveryPromptFitsContext()
// dense fake (2 tokens/char), ctx 8192, doc 200_000 chars → all prompts fit
[Fact] public async Task SummarizeAsync_EmptyIntermediateSummaries_StillCompletes()
// fake returns "" for Chunk/Combine instructions → returns the final call's output, no exception
[Fact] public async Task SummarizeAsync_IntermediateCallsUseIntermediateOutputTokens()
// ctx 4096 large doc → every Chunk/Combine call's maxOutputTokens == TokenBudget.Compute(4096, 2048, fakeOverhead).IntermediateOutputTokens,
//   where fakeOverhead = max instruction length (fake counts 1 token/char)
```

- [ ] **Step 2: Run** filter `MapReduceSummarizerTests` — expect compile failure.
- [ ] **Step 3: Implement** `ISummaryModel.cs` and `MapReduceSummarizer.cs`.
- [ ] **Step 4: Run** filter — all PASS; full suite PASS.
- [ ] **Step 5: Commit** `Add token-bounded MapReduceSummarizer with guaranteed reduce progress`

---

### Task 6: Wire orchestrator into `LlamaSharpSummarizer`

**Files:**
- Create: `SummaryGenerator/Services/PromptTemplate.cs` — `public static string Build(string modelName, string instruction, string content)` (moved verbatim from `LlamaSharpSummarizer.BuildPrompt`)
- Create: `SummaryGenerator/Services/LlamaSummaryModel.cs` — `internal sealed class LlamaSummaryModel(LLamaWeights weights, StatelessExecutor executor, string modelName, SummarizationOptions options) : ISummaryModel`
- Modify: `SummaryGenerator/Services/LlamaSharpSummarizer.cs` — delete `BuildPrompt`, `ReduceSummariesAsync`, `MaxReduceDepth`, `RunInferenceAsync`, `CleanSummary`, the shared `InferenceParams`/sampling setup, and the char-budget branch; after loading weights, `var summary = await new MapReduceSummarizer(new LlamaSummaryModel(weights, executor, modelDetails.Name, _options), logger).SummarizeAsync(documentText, systemPrompt, resolvedContextSize, _options.MaxTokens, cancellationToken)`. Keep validation, parameter resolution, the "Resolved inference parameters" log, a completion log, and the existing catch blocks.
- Modify: `SummaryGenerator/Services/DocumentChunker.cs` — delete `EstimateCharBudget` and `CharsPerToken`; delete `SummaryGenerator.Tests/Unit/DocumentChunkerEstimateCharBudgetTests.cs`.

**`LlamaSummaryModel` details:**
- `CountPromptTokens` = `weights.Tokenize(PromptTemplate.Build(modelName, instruction, content), true, true, Encoding.UTF8).Length`.
- `GenerateAsync` builds a new `InferenceParams` per call (`MaxTokens = maxOutputTokens`, `AntiPrompts = options.StopPhrases`, `DefaultSamplingPipeline` from `Temperature/TopP/RepeatPenalty`, `OverflowStrategy = TruncateAndReprefill`), streams `executor.InferAsync`, returns `SummaryOutputCleaner.Clean(raw, options.StopPhrases)`.

- [ ] **Step 1: Build** `dotnet build SummaryGenerator.Tests --nologo -v q` — 0 errors; csproj diff empty.
- [ ] **Step 2: Run full suite** — all PASS (3 EstimateCharBudget tests removed).
- [ ] **Step 3: Live verification (real model, real app)** — from `SummaryGenerator/` run `dotnet run --no-build --urls http://localhost:5299`, upload via the UI:
  - `Uploads\…China's Approach to Global Governance…_cleaned.pdf` (26,391 chars) with **Phi-4 Mini** → output `.md` produced, starts with `# 1. Executive Summary`, no `<scratchpad>`.
  - `Uploads\…R42784.155.pdf` (402,125 chars) with **Phi-4 Mini** → log shows map + reduce levels with token counts within budget; output produced; no NoKvSlot.
  Stop the app by PID afterward (`(Get-NetTCPConnection -LocalPort 5299 -State Listen).OwningProcess` → `Stop-Process -Id`).
- [ ] **Step 4: Commit** `Use token-counted map-reduce in LlamaSharpSummarizer`

---

### Task 7: Logging cleanup

**Files:**
- Create: `SummaryGenerator/Services/LogPathResolver.cs` — `public static void AnchorFileSinkPaths(IConfiguration configuration, string contentRootPath)`
- Modify: `SummaryGenerator/Program.cs` — call `LogPathResolver.AnchorFileSinkPaths(builder.Configuration, builder.Environment.ContentRootPath)` before `UseSerilog`; delete `app.UseSerilogRequestLogging();`
- Modify: `SummaryGenerator/appsettings.json`, `SummaryGenerator/appsettings.Development.json` — delete the unused `Logging` sections (Serilog `MinimumLevel` is the single source of truth)
- Test: `SummaryGenerator.Tests/Unit/LogPathResolverTests.cs`

**Behavior:** for each child of `Serilog:WriteTo` whose `Name` is `File` and whose `Args:path` is relative (`!Path.IsPathRooted`), set `Args:path` to `Path.Combine(contentRootPath, path)`. Absolute paths and other sinks untouched.

- [ ] **Step 1: Write failing tests** (config via `new ConfigurationBuilder().AddInMemoryCollection(...)`)

```csharp
[Fact] public void AnchorFileSinkPaths_RelativeFilePath_IsCombinedWithContentRoot()
// WriteTo:0 Console, WriteTo:1 File path "Logs/log-.txt", root @"C:\app"
// → config["Serilog:WriteTo:1:Args:path"] == Path.Combine(@"C:\app", "Logs/log-.txt")
[Fact] public void AnchorFileSinkPaths_AbsolutePath_IsUnchanged()
[Fact] public void AnchorFileSinkPaths_NoSerilogSection_DoesNothing()
```

- [ ] **Step 2: Run** filter `LogPathResolverTests` — compile failure.
- [ ] **Step 3: Implement** helper, `Program.cs` edits, appsettings edits.
- [ ] **Step 4: Run** filter + full suite — PASS. Start the app from the **repo root** (`dotnet run --project SummaryGenerator --no-build --urls http://localhost:5299`), load `/` a few times, stop it by PID: `SummaryGenerator\Logs\log-<date>.txt` receives the startup lines, no `HTTP GET /` lines are written, and no `Logs` folder appears in the repo root.
- [ ] **Step 5: Commit** `Anchor log file to content root and drop per-request logging`

---

### Final: Branch verification

- [ ] Full suite green; `git --no-pager diff main...HEAD --stat -- *.csproj` shows only the package additions from earlier commits (no version bumps).
- [ ] Push to `hardware-profiling`; add a PR #2 comment listing the review findings fixed and the deferred ones (VRAM-aware layers, weight caching, UI badge, config-driven tuning constants).
