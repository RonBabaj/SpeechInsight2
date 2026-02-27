# SpeechInsight

SpeechInsight is a **Blazor WebAssembly** + **ASP.NET Core** app for **audio transcription and analysis**. You can upload an audio file or record from the browser microphone; the app transcribes it (with optional speaker separation) using the OpenAI speech-to-text APIs and returns structured analysis: duration, word count, detected language, and a confidence score.

---

## Prerequisites

- **.NET 10 SDK** (or the latest .NET runtime you have installed)
- **OpenAI API key** (with access to Whisper / speech-to-text and, optionally, the diarization model)

---

## Architecture

- **Client (Blazor WASM)** – Single-page UI on port **5190**. Users pick a file or record from the microphone, choose the model, and upload; the app shows analysis metrics, transcription, and export actions. All HTTP calls go through **AudioApiClient**; no URL building or `HttpClient` usage in components. Recent results are kept in **localStorage** (per browser).
- **API (ASP.NET Core)** – REST API on port **5200**. Receives the file (or recording blob), validates it, runs the **analysis pipeline** (duration, transcription via OpenAI, word count, language, confidence), and returns a structured DTO. Config is in `appsettings.json`; the API key is read from **.env** via DotNetEnv. Controllers delegate to **IAudioAnalysisService**; business logic lives in services only.

---

## Project structure

```
SpeechInsight2/
├── SpeechInsight.sln
├── README.md
├── .gitignore
├── Api/
│   ├── Program.cs                    # Host, CORS, DI, DotNetEnv, provider + analysis registration
│   ├── appsettings.json              # Transcription limits & model names
│   ├── .env.example                  # Template for OPENAI_API_KEY
│   ├── Controllers/
│   │   ├── AudioController.cs        # /api/audio/* — validates input, delegates to IAudioAnalysisService
│   │   └── HealthController.cs       # /api/health
│   ├── Models/                       # Response DTOs (no anonymous objects)
│   │   ├── AnalyzeDetailsResponseDto.cs
│   │   ├── TranscriptionSegmentDto.cs
│   │   └── AudioLimitsDto.cs
│   ├── Options/
│   │   └── TranscriptionOptions.cs
│   └── Services/
│       ├── ITranscriptionService.cs
│       ├── ITranscriptionDetailsService, OpenAITranscriptionService.cs  # OpenAI HTTP, parsing, language
│       ├── IAudioDurationService.cs  # Duration from audio stream (WAV supported)
│       ├── AudioDurationService.cs   # WAV header parsing; other formats → null (use provider duration)
│       ├── TranscriptionTextAnalyzer.cs  # Word count, language heuristic, confidence heuristic
│       ├── IAudioAnalysisService.cs  # Orchestrates full pipeline
│       └── AudioAnalysisService.cs   # Duration → transcription → word count, language, confidence → DTO
└── Client/
    ├── Program.cs                    # Blazor host, HttpClient, AudioApiClient registration
    ├── App.razor, _Imports.razor
    ├── Models/                       # DTOs mirroring API (AnalyzeDetailsResponseDto, etc.)
    ├── Pages/
    │   ├── Home.razor
    │   └── Upload.razor              # File picker, record, model choice, stats, transcript, recent, export
    ├── Components/
    │   ├── AudioUploadCard.razor
    │   ├── AnalysisPanel.razor       # Duration, words, chars, segments, language, confidence, model
    │   ├── TranscriptionResult.razor
    │   └── ProcessingState.razor
    ├── Layout/
    │   └── MainLayout.razor
    ├── Services/
    │   ├── AudioApiClient.cs         # All API calls; multipart upload, typed DTOs, AudioApiException
    │   └── AudioApiException.cs
    └── wwwroot/
        ├── index.html
        ├── css/app.css
        └── js/app.js                 # Copy, download, localStorage, theme, microphone recording
```

---

## Setup

1. **API – OpenAI key**

   In the **Api** folder, create a `.env` file (e.g. copy from `.env.example`):

   ```env
   OPENAI_API_KEY=your_openai_api_key_here
   ```

   The API loads it with `DotNetEnv.Env.Load()` when the process starts (run the API from the **Api** directory so the working directory is correct).

2. **Restore and build**

   From the solution root:

   ```bash
   dotnet restore
   dotnet build
   ```

---

## Run

Run both projects (two terminals).

**Terminal 1 – API (port 5200):**

```bash
cd Api
dotnet run
```

**Terminal 2 – UI (port 5190):**

```bash
cd Client
dotnet run
```

Then open **http://localhost:5190** in the browser.

---

## User flow

1. **Model** – Choose **“With speakers (recommended)”** (diarized) or **“Basic (Whisper — cheaper)”**.
2. **Source** – Either **“Choose File”** (e.g. .mp3, .wav, .m4a, .webm) or **“Record from microphone”** (browser records; stop to get a clip, then upload). Invalid type/size or no source shows an error before upload.
3. **Upload** – Click **Upload and Analyze**. Use **Cancel** to abort. The same backend endpoint is used for both file uploads and recordings.
4. **Result** – **Analysis** card: duration (mm:ss), word count, characters, segments, detected language, confidence (%), model. **Transcription** card: full text with speaker labels when diarized. If the clip exceeds the recommended duration, a warning is shown.
5. **Export** – **Copy**, **Download .txt**, or **Download .json** (full details).
6. **Recent** – The last 3 analyses are listed; click a file name to view that result again. Data is in the browser’s **localStorage**. **Clear recent** removes stored entries.
7. **Theme** – Use the **Dark** / **Light** toggle in the header; preference is stored in localStorage.

---

## Analysis pipeline & metrics

All analysis is computed **server-side**. The pipeline (see `AudioAnalysisService` and related services) does the following.

### Duration (`durationSeconds`)

- **From audio when possible:** For **WAV** uploads, duration is read from the file stream (RIFF header, byte rate, data chunk size) in `AudioDurationService`. The stream position is restored so the same stream can be sent to the transcription provider.
- **Fallback:** For other formats (MP3, WebM, M4A, etc.) or when WAV parsing fails, duration comes from the **transcription provider** (OpenAI response: `duration` / `usage.seconds` or derived from segment end times). Duration is **never** estimated from word count.

### Word count (`wordCount`)

- **Tokenization:** Words are counted in `TranscriptionTextAnalyzer.CountWords`: split on whitespace, trim, remove empty tokens.
- **Excluded from count:** Filler-style artifacts such as `um`, `uh`, `eh`, `[inaudible]`, `[silence]`, and `...` are excluded so the number reflects meaningful words. Logic is reusable and testable.

### Detected language (`detectedLanguage`)

- **Provider first:** When the transcription API returns a `language` field (e.g. Whisper `verbose_json` / `diarized_json`), that value is used and exposed as `detectedLanguage` (e.g. `en`, `he`, `ru`).
- **Heuristic fallback:** If the provider does not supply language, `TranscriptionTextAnalyzer.DetectLanguageHeuristic` runs: it counts character scripts (Hebrew, Cyrillic, Latin) in the transcription and returns an ISO 639-1 style code when one script clearly dominates (e.g. Latin majority → `en`). Otherwise the API returns `null` and the UI shows “Unavailable”.

### Confidence score (`confidenceScore`)

- **Source:** The Whisper API does not expose a confidence value. The app uses a **heuristic** implemented in `TranscriptionTextAnalyzer.ComputeConfidenceHeuristic`. The score is **evidence-based**: it is computed from real data for each run (transcription length, duration, word count), not a fixed mock.
- **Patterns that lower the score:**
  - **Empty or invalid:** No text or invalid duration → 0.
  - **Very short transcription:** Length &lt; 10 characters → score multiplied by 0.6 (likely incomplete or failed capture).
  - **Very low words-per-minute** (&lt; 20): Suggests long silence or missed speech → score multiplied by 0.7.
  - **Very high words-per-minute** (&gt; 250): Suggests possible artifacts or misalignment → score multiplied by 0.85.
- **Otherwise** the score starts at 1.0 and is clamped to [0, 1]. The result is shown in the UI as a percentage (e.g. 80%). If the provider later exposes a confidence value, the pipeline can be extended to prefer it over the heuristic.

---

## API reference

Base URL when running locally: **http://localhost:5200**.

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/health` | Health check. Returns `{ "status": "ok", "timestamp": "..." }`. |
| GET | `/api/audio/limits` | Returns `maxFileSizeBytes`, `maxDurationSeconds`, `allowedExtensions`. |
| POST | `/api/audio/analyze` | Plain-text transcription. Body: `multipart/form-data` with field **`audioFile`**. Response: `text/plain`. |
| POST | `/api/audio/analyze/details?diarize=true` | Full analysis. Same body. Response: JSON with transcription and analysis fields. |

**Request (analyze/details)**  
- Content-Type: `multipart/form-data`  
- Field name: **`audioFile`** (required)  
- Query: `diarize` (optional, default `true`) – when `true`, uses the diarization model and returns segments with speaker labels when available.

**Response (analyze/details, 200)**  
- `text` – Full transcript.  
- `model` – Model used (e.g. `whisper-1`, `gpt-4o-transcribe-diarize`).  
- `durationSeconds` – Audio duration: from WAV when possible, otherwise from provider.  
- `segments` – Array of `{ speaker, startSeconds, endSeconds, text }` (when available).  
- `diarized` – Whether the diarization model was used.  
- `durationExceedsRecommended` – `true` when duration &gt; configured `MaxDurationSeconds`.  
- `wordCount` – Server-computed word count (tokenization, filler artifacts excluded).  
- `detectedLanguage` – Language code from provider or heuristic (e.g. `en`, `he`, `ru`), or `null`.  
- `confidenceScore` – Heuristic confidence 0–1 (see **Analysis pipeline & metrics** above), or `null`.

**Error responses**  
- 400 – Invalid or missing file, wrong type, or file too large. Body: `{ "message": "...", "detail": "..." }`.  
- 401/403/429/500/502 – Body includes a `message` (and optionally `detail`). The UI maps these to short, user-friendly text.

---

## Configuration

**API – `Api/appsettings.json`** (section `Transcription`):

| Option | Description | Default |
|--------|-------------|---------|
| `Provider` | Transcription provider (e.g. `OpenAI`). Used for DI; only `OpenAI` is implemented. | `OpenAI` |
| `MaxFileSizeBytes` | Max upload size in bytes. | 25 MB |
| `MaxDurationSeconds` | Recommended max duration; responses longer than this set `durationExceedsRecommended`. | 900 |
| `AllowedExtensions` | Allowed file extensions (e.g. `.mp3`, `.wav`, `.m4a`, `.webm`, `.mp4`, `.mpeg`, `.mpga`). | (see file) |
| `DefaultModel` | Model for non-diarized requests. | `whisper-1` |
| `DiarizeModel` | Model for diarized requests. | `gpt-4o-transcribe-diarize` |

**Client**  
- Allowed types and max size (25 MB) are enforced in the UI to match the API.  
- The API base URL is set in `Client/Program.cs` (e.g. `http://localhost:5200`).

---

## Local storage (client)

- **SpeechInsight_Recent** – JSON array of up to 3 items: `fileName`, `durationSeconds`, `wordCount`, `transcription`, full `details` (same shape as analyze/details response). Saved after each successful analysis; loaded on startup. Cleared via **Clear recent**.
- **SpeechInsight_Model** – Last chosen model (`diarize` / `basic`).  
- **SpeechInsight_Theme** – `light` or `dark` for the UI theme.

---

## Ports

| App | URL |
|-----|-----|
| UI  | http://localhost:5190 |
| API | http://localhost:5200 |

---

## Troubleshooting

- **CORS errors** – Ensure the API is running and allows `http://localhost:5190` (configured in `Api/Program.cs`).
- **500 / “OPENAI_API_KEY”** – Create `Api/.env` with `OPENAI_API_KEY=...` and run the API from the **Api** folder so `DotNetEnv.Env.Load()` finds the file.
- **429 / quota** – Your OpenAI account has no quota or rate limit; check plan and billing.
- **Empty or wrong transcription** – Use an allowed format (e.g. mp3, wav, m4a, webm) and stay under the max file size. For long files, the UI may show a “longer than recommended” warning; you can still use the result.
- **Microphone not working** – Grant microphone permission when prompted. Recording produces WebM; the same analyze/details endpoint is used as for file uploads.
- **Recent not persisting** – Recent is stored in the browser’s localStorage for this origin. Private/incognito or clearing site data will remove it. Use **Clear recent** to wipe it manually.

---

## License / status

Initial version for demo and local use. Adjust config and CORS for your environment before any production or shared deployment.
