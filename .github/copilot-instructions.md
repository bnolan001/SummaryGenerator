# Copilot instructions
# C# .NET Local PDF Document Summarizer for Professional Military Education (PME)

## Project Overview

This project is an offline-capable, privacy-focused C# .NET application designed to summarize complex PDF documents and course materials from Professional Military Education (PME) programs.

### Key Features
* **Embedded AI Execution:** Powered by `LLamaSharp` and native `llama.cpp` bindings—runs entirely in-process without requiring external daemons, background services, or tools like Ollama or LM Studio.
* **Direct Hugging Face Downloads:** Built-in model downloading via public CDN endpoints with support for optional personal access tokens for gated or private repositories.
* **Sequential Queue Processing:** High-throughput background processing thread using `ConcurrentQueue<T>` and `SemaphoreSlim`, allowing users to queue multiple large PDF manuals while executing inferences sequentially to protect system RAM.
* **In-Memory Text Extraction:** Utilizes `PdfPig` for fast, lightweight PDF text parsing and automatic cleaning of running headers, footers, and page markers.
* **Structured Markdown Output:** System prompt engineered specifically for tactical, strategic, and leadership courseware, producing clean Markdown files with executive summaries, key terms, operational takeaways, and study notes.

---

## Technical Stack & Dependencies

| Component | Library / Framework | License | Purpose |
| :--- | :--- | :--- | :--- |
| **Runtime** | .NET 10.0 SDK (or later) | MIT / Open Source | Core runtime and cross-platform framework |
| **LLM Engine** | `LLamaSharp` (v0.27.0+) | MIT | C# bindings for in-process `llama.cpp` local inference |
| **CPU Backend** | `LLamaSharp.Backend.Cpu` | MIT | Native CPU execution backend for Intel/AMD x64 processors |
| **PDF Extraction** | `PdfPig` (v0.1.9+) | Apache 2.0 | Pure C# text extraction from PDF documents |
| **Output Format** | CommonMark / Standard Markdown | Open Standard | Human-readable, structured study material |

---

## Recommended Models

The application is configured to download GGUF-formatted models directly from Hugging Face. The table below lists the primary recommended models optimized for document summarization on laptop CPUs.

| Model Name | Repository ID (`repo`) | Filename (`fileName`) | Size (RAM) | Context Window | Best For |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Phi-4 Mini Reasoning (3.8B)** | `unsloth/Phi-4-mini-reasoning-GGUF` | `Phi-4-mini-reasoning.Q4_K_M.gguf` | ~2.45 GB | 128,000 tokens | **Default / Recommended:** High speed, exceptional reasoning density, fits easily on standard 16GB Intel laptops. |
| **Qwen 2.5 Instruct (7B)** | `Qwen/Qwen2.5-7B-Instruct-GGUF` | `qwen2.5-7b-instruct-q4_k_m.gguf` | ~4.80 GB | 32,000 tokens | Superior factual precision, technical terminology extraction, and non-gated public availability. |
| **Llama 3.1 Instruct (8B)** | `lmstudio-community/Meta-Llama-3.1-8B-Instruct-GGUF` | `Meta-Llama-3.1-8B-Instruct-Q4_K_M.gguf` | ~4.70 GB | 128,000 tokens | Industry standard for natural prose, highly structured Markdown tables, and analytical lists. |

---

## Project Structure

```text
MilitaryPdfSummarizer/
├── MilitaryPdfSummarizer.csproj
├── Program.cs
├── Services/
│   ├── HuggingFaceDownloader.cs
│   ├── PdfTextExtractor.cs
│   ├── LlamaSharpSummarizer.cs
│   └── QueueProcessor.cs
├── Models/
│   ├── RecommendedModel.cs
│   └── ProcessingTask.cs
├── Downloads/           # Directory where downloaded .gguf files reside
└── Output/              # Directory where generated .md summaries are saved
```

---

## How to Build and Run

1. **Open Terminal / Command Prompt** inside the project folder.
2. **Build the Application:**
   ```bash
   dotnet build -c Release
   ```
3. **Execute the Application:**
   ```bash
   dotnet run -c Release
   ```
4. **Queue Documents:** Drag and drop any military course PDF into the console window and press **Enter**. The system will automatically extract text, process it through the local GGUF model, and place the formatted `.md` file into the `Output/` folder.

---

## Operating Guidelines & Memory Tuning

* **Thread Allocation:** The `Threads` parameter in `ModelParams` is set to `Environment.ProcessorCount - 2`. On Intel laptop CPUs, leaving 2 logical cores free keeps system UI and background threads responsive.
* **Context Size:** If you plan to summarize massive 100+ page manuals, increase `ContextSize` to `32768` or `65536`. Keep in mind that higher context sizes scale RAM consumption proportionally.
* **GPU Offloading:** If deploying to workstations with dedicated NVIDIA GPUs, swap out `LLamaSharp.Backend.Cpu` for `LLamaSharp.Backend.Cuda12` in your `.csproj` and adjust `GpuLayerCount = 20` (or higher) to offload layers directly to VRAM.

## Project structure
- App entry point: `SummaryGenerator/Program.cs`
- Pages live in `SummaryGenerator/Pages/`
- Models live in `SummaryGenerator/Models/`
- Services live in `SummaryGenerator/Services/`
- External integrations live in `SummaryGenerator/Repositories/`

## Conventions
- Keep changes small and aligned with existing Razor Pages patterns.
- Preserve nullable reference types and implicit usings.
- Prefer dependency injection over direct instantiation.
- Keep configuration-driven values in `appsettings.json` / environment-specific appsettings files.
- Avoid editing generated output under `bin/`, `obj/`, or `.vs/`.

## When generating code
- Follow existing namespaces and file organization.
- Use plain ASP.NET Core / Razor Pages patterns unless the repo already uses a different approach.
- If adding new configuration, wire it through `appsettings.json` and the page model or service that consumes it.
