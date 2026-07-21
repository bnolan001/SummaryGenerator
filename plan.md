# Plan: SummaryGenerator v1 implementation

## Confirmed scope and constraints
- Target architecture: **Razor Pages web app** (no console-first flow).
- Release scope: **End-to-end MVP plus in-app Hugging Face model download**.
- Delivery: generate summary and provide **browser `.md` download + server-side `.md` save**.
- Security model: **local single-user**, no auth in v1.
- Platform: **Windows x64 only**.
- Throughput target: **queued multi-document processing from day one**.
- Acceptance criteria: **functional completeness** (workflow correctness over performance SLAs).
- Validation: **targeted unit/integration tests** for core services and page handlers.

## Phase 1 — Foundation and configuration
1. Normalize solution architecture around existing folders:
   - `Models/` for task/model DTOs.
   - `Services/` for extraction, inference, queue orchestration, and file output.
   - `Repositories/HuggingFace/` for model download provider.
2. Add/confirm configuration schema in `appsettings.json`:
   - model choices, default model, model storage path (`Downloads`),
   - output path (`Output`),
   - queue limits and worker settings (max concurrent workers = 1 for sequential inference),
   - optional Hugging Face token setting.
3. Wire configuration via options classes and dependency injection in `Program.cs`.

### Phase 1 status
- ✅ Completed: configuration schema and options-based DI wiring are now in place.

## Phase 2 — Core domain and service layer
1. Implement model/task contracts:
   - recommended model metadata,
   - processing task lifecycle/status,
   - summary result metadata (source PDF, output path, timestamps, status, error).
2. Implement PDF text extraction service (PdfPig):
   - extract full text in memory,
   - remove common headers/footers/page markers using deterministic cleanup rules.
3. Implement LLM summarization service (LLamaSharp):
   - load GGUF model from configured path,
   - build PME-focused system prompt for structured Markdown output,
   - run inference with configurable context/thread settings.
4. Extend Hugging Face repository/service:
   - download GGUF by selected model metadata,
   - expose progress and explicit failure results,
   - persist downloaded files under configured `Downloads` folder.
5. Implement queue processor service:
   - `ConcurrentQueue<T>` + `SemaphoreSlim`,
   - background worker that processes one inference at a time,
   - per-task status transitions: queued → processing → completed/failed.
6. Implement markdown output service:
   - deterministic file naming,
   - save `.md` under configured `Output`,
   - return stream/path for browser download.

### Phase 2 status
- ✅ Completed: core contracts and service implementations are in place, including queue processing, model download, PDF extraction, summarization, and markdown output writing.

## Phase 3 — Razor Pages workflow
1. Update Index page to support:
   - PDF upload,
   - model selection from config,
   - optional model download trigger when model file missing.
2. Add queue/status UI:
   - active queue list with per-task state and error messages,
   - completed jobs with download links.
3. Add page handlers:
   - enqueue processing job,
   - trigger/await model download,
   - download generated markdown file.
4. Ensure non-blocking UX:
   - request returns quickly after enqueue,
   - polling or refresh endpoint to view updated task statuses.

### Phase 3 status
- ✅ Completed: upload/enqueue handlers, model download trigger, queue status UI, status polling endpoint, and markdown download handler are implemented.

## Phase 4 — Reliability and error handling
1. Validate uploads (PDF only, size limits, safe temp handling).
2. Add explicit error flows for:
   - model file missing/download failure,
   - unreadable/corrupt PDF,
   - inference/runtime failures,
   - output write failures.
3. Add structured logging around queue events, model operations, and task failures.

### Phase 4 status
- ✅ Completed: upload validation/limits, safer upload handling and cleanup, explicit failure mapping across extraction/summarization/output, and expanded structured logging are implemented.

## Phase 5 — Validation and acceptance
1. Unit tests:
   - text cleanup logic,
   - output filename/path generation,
   - queue state transitions.
2. Integration-style tests (where practical):
   - page handler flow for enqueue + status updates,
   - file download handler behavior.
3. Manual acceptance pass:
   - upload multiple PDFs,
   - verify sequential processing,
   - verify each completed task yields valid `.md` saved server-side and downloadable.

## Implementation order (execution sequence)
1. Configuration + DI wiring.
2. Core services (extractor, summarizer, downloader, output writer).
3. Queue processor and task state model.
4. Razor Page handlers/UI integration.
5. Tests and acceptance sweep.

## Definition of done (v1)
- A local user can select/download a model, queue multiple PDFs, and have them processed sequentially.
- Each completed task produces a structured markdown summary.
- Summary is saved to server output folder and downloadable via browser.
- Failures are visible per task with actionable error messages.
