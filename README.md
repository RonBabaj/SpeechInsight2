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

## Flow and features

This section walks through the app from first load to results and explains what each part does.

### Landing (Home)

- At **http://localhost:5190** you see the **SpeechInsight** title, tagline, and three short cards: **Upload audio**, **Transcribe**, **Export**.
- **Upload & transcribe** sends you to the main workflow. The header has **Home**, **Upload**, and a **Dark** / **Light** theme toggle (preference is saved in the browser).

### Choosing input and model (Upload page)

- **Model** – Choose **“With speakers (recommended)”** (diarized) or **“Basic (Whisper — cheaper)”**.
- **Source (pick one)**
  - **Choose File** – Pick an audio file from your device. Allowed types: .mp3, .mpga, .m4a, .wav, .webm, .mp4, .mpeg, up to 25 MB. Wrong type or oversized file shows an error under the file input; you can't submit until it's valid.
  - **Record from microphone** – Click **Record from microphone**. The browser asks for mic permission; after you allow, recording starts. Click **Stop recording** when done. The clip appears as "Recording ready (X.X KB)". Recording is WebM; the same backend endpoint is used as for file uploads. You can't mix: if you have a file selected, Record is disabled; if you record, the file picker is disabled until you clear the recording by uploading or refreshing.

- **Upload and Analyze** is enabled only when you have a valid file or a recording. Click it to send the audio to the API.

### While the request is in progress

- A **processing** state appears: first "Uploading…", then "Transcribing…" after a short delay. **Cancel** aborts the request. Errors (e.g. network, API key, quota) are shown in a red **Something went wrong** card with a short message; no stack traces.

### Results: Analysis card

After a successful run, the **Analysis** card shows:

- **Duration** – Length of the audio in **mm:ss** (e.g. 2:35). For WAV files this comes from the file; for other formats it comes from the transcription provider. If unknown, the UI shows "Unavailable".
- **Words** – Number of words in the transcript (server-computed, filler words like "um" / "[inaudible]" excluded).
- **Characters** – Character count of the full transcript.
- **Segments** – Number of timed segments (especially meaningful when using the diarization model).
- **Language** – Detected language code (e.g. en, he, ru) from the provider or a script-based heuristic. "Unavailable" if neither is possible.
- **Confidence** – A **percentage** from an evidence-based heuristic (transcription length, duration, word count). Not from the Whisper API; see **Analysis pipeline & metrics** below for how it's computed.
- **Model** – Name of the model used (e.g. whisper-1, gpt-4o-transcribe-diarize).

If the clip is longer than the recommended limit (configurable, default 15 minutes), a warning appears above the stats; the result is still shown.

### Results: Transcription card

- The full **transcription** is shown. With **With speakers** you get labels like "Speaker 1: …" and "Speaker 2: …" when the model detects multiple speakers.
- **Copy** puts the full transcript on the clipboard (with a short "Copied" message).
- **Download .txt** saves the plain transcript. **Download .json** saves the full API response (text, segments, all analysis fields). Filenames are based on the original file name or "recording".

### Recent

- The last **3** analyses are listed below the result. Each line shows file name (or "recording.webm"), duration, and word count. **Click a line** to reload that result (analysis + transcription) without calling the API again. Data is stored in the browser's **localStorage** and survives refresh; **Clear recent** removes all entries.

### End-to-end request path

1. User clicks **Upload and Analyze** (file or recording).
2. The Blazor UI calls **AudioApiClient.AnalyzeDetailsAsync** with the audio stream, file name, content type, and diarize flag. No component builds URLs or uses `HttpClient` directly.
3. The API receives `POST /api/audio/analyze/details` with `multipart/form-data` and **audioFile**. The controller validates the file and delegates to **IAudioAnalysisService.AnalyzeAsync**.
4. The analysis service: (a) tries to read **duration** from the stream (WAV supported; others use provider duration), (b) sends the stream to the **transcription provider** (OpenAI), (c) computes **word count**, **language** (provider or heuristic), and **confidence** (heuristic), (d) builds the response DTO.
5. The client receives the JSON, shows the Analysis and Transcription cards and updates Recent. Errors are parsed and shown as user-facing messages.

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
