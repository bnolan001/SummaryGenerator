# SummaryGenerator

SummaryGenerator is a local web application for turning PDF course materials into structured Markdown study notes. It extracts PDF text and runs summarization with a locally stored GGUF model through LLamaSharp and `llama.cpp`; document text is not sent to a hosted AI service.

The app is designed for Professional Military Education (PME), with built-in prompt profiles for PME students, undergraduate students, and corporate executives. It also supports custom prompts, a sequential processing queue, model downloads from Hugging Face, and optional text-to-speech WAV generation.

## Requirements

- .NET 10 SDK
- Windows recommended; CPU inference is included. GPU acceleration depends on the installed backend and detected hardware.
- Internet access for the first download of a model. After download, PDF extraction and inference run locally.

The default PDF upload limit is 50 MB. Model downloads and generated files are stored under the application directory unless paths are changed in configuration.

## Build and run

From the repository root:

```powershell
dotnet build SummaryGenerator.slnx --configuration Release
dotnet run --project SummaryGenerator\SummaryGenerator.csproj --configuration Release --launch-profile http
```

Open [http://localhost:5204](http://localhost:5204). The `http` launch profile also opens the browser automatically.

## Summarize a PDF

1. In **Model download status**, download a model that fits your hardware. The status shows whether its file is present; **Runs well on this PC** is an estimate based on model size and detected system memory, not a guarantee of inference speed.
2. In **Queue a document**, choose a model and summary prompt profile.
3. Optionally enter both a custom prompt name and prompt text. Saved custom prompts appear in the prompt list for later uploads; using an existing name updates that prompt.
4. Select one PDF and choose **Queue PDF**. Repeat to queue more documents. The default queue accepts up to 25 active or queued tasks and processes them sequentially.
5. Follow progress in **Queue status**. When complete, use **Download .md** or **View rendered .md** to retrieve the summary.

If a selected model has not been downloaded yet, the app downloads it when processing begins. Models are stored in `SummaryGenerator\Downloads`; summaries are written to `SummaryGenerator\Output`. These directories are created automatically.

## Available models

The default catalog includes Phi-4 Mini (3.8B), Qwen 2.5 (7B and 14B), Llama 3.3 (8B), and Gemma 4 (8B, 12B, and 26B MoE). The displayed approximate model sizes range from 2.3 GB to 15.8 GB. Larger models need additional memory for inference, especially at larger context sizes; use the hardware-fit labels as guidance and start with a smaller model if unsure.

The catalog, model repositories, filenames, and default selections are configured in `SummaryGenerator\appsettings.json` under `Models`.

## Hardware and summarization settings

Hardware auto-tuning is enabled by default. It chooses inference settings such as thread count, context size, batch size, and GPU offload based on detected hardware. To adjust behavior, edit the `Summarization` section in `SummaryGenerator\appsettings.json`:

| Setting | Default | Purpose |
| --- | --- | --- |
| `HardwareAutoTune` | `true` | Use detected hardware to choose inference parameters. |
| `ContextSize` | `0` | Fallback context size when selected model has no preferred size; hardware auto-tuning and safe/model-specific limits may cap it. |
| `Threads` | `0` | `0` uses the hardware-based thread count; a positive value overrides it. |
| `GpuLayerCount` | `-1` | With auto-tuning enabled, `-1` lets hardware detection choose GPU offload; with auto-tuning disabled, it resolves to CPU-only. `0` forces CPU-only; a positive value sets the offload layer count. |
| `IntegratedGpuMaxModelPercentOfRam` | `40` | Limits automatic model offload on integrated GPUs as a percentage of detected system memory. |
| `MaxTokens` | `2048` | Maximum generation length for a summarization call. |

Change one setting at a time and test with representative PDFs. Increasing context size or choosing a larger model can substantially increase memory use. Keep access tokens and other secrets out of committed configuration files.

## Audio generation

To create a WAV from a completed summary, select a voice and choose **Generate Audio** for that task. You can also upload a Markdown file in **Queue Markdown for Audio Generation**. Audio uses installed `System.Speech` voices and is saved under `Output`.

## Configuration and stored files

Default paths and limits are in `SummaryGenerator\appsettings.json`:

- `Downloads`: Hugging Face model files
- `Output`: generated Markdown and WAV files
- `Uploads`: temporary uploads
- `Logs`: rolling application logs, retained for up to seven days
- `CustomPrompts\custom-prompts.json`: saved custom prompt profiles

Configuration can be overridden with standard .NET environment variables. Use double underscores for nested keys, for example:

```powershell
$env:Summarization__HardwareAutoTune = "true"
$env:Storage__OutputPath = "D:\SummaryGenerator\Output"
```

For gated Hugging Face repositories, set the optional `HuggingFace:AccessToken` through user secrets or the `HuggingFace__AccessToken` environment variable rather than saving it in source control.

## Troubleshooting

- **Model download fails:** Check internet access and the repository/file configured for that model. A Hugging Face token is only needed for gated repositories.
- **PDF produces no summary:** Confirm the PDF contains extractable text; image-only/scanned PDFs need OCR before upload.
- **Model runs slowly or runs out of memory:** Choose a smaller model, reduce its configured context size, or review the hardware tuning settings.
- **More detail is needed:** Check the latest file under `Logs` for application and inference diagnostics.
