# SpeechInsight

SpeechInsight is a small C# .NET demo app for **audio transcription**: you upload an audio file in the browser, and the app transcribes it (with optional speaker separation) using the OpenAI speech-to-text APIs. Built with **Blazor WebAssembly** (UI) and **ASP.NET Core Web API** (backend).

---

## Prerequisites

- **.NET 10 SDK** (or the latest .NET runtime you have installed)
- **OpenAI API key** (with access to Whisper / speech-to-text and, optionally, the diarization model)

---

## Architecture

- **Client (Blazor WASM)** – Single-page UI on port **5190**. User picks a file, chooses model, uploads; the app shows stats, transcription, and export actions. Recent results are kept in **localStorage** (per browser).
- **API (ASP.NET Core)** – REST API on port **5200**. Receives the file, validates it, calls the OpenAI transcription endpoint (raw `HttpClient`, no SDK), and returns plain text or structured details (segments, duration, etc.). Config (limits, model names) is in `appsettings.json`; the API key is read from **.env** via DotNetEnv.

---

## Project structure

```
SpeechInsight2/
├── SpeechInsight.sln
├── README.md
├── .gitignore
├── Api/
│   ├── Program.cs                 # Host, CORS, DI, DotNetEnv
│   ├── appsettings.json           # Transcription limits & model names
│   ├── .env.example               # Template for OPENAI_API_KEY
│   ├── Controllers/
│   │   ├── AudioController.cs     # /api/audio/* endpoints
│   │   └── HealthController.cs    # /api/health
│   ├── Options/
│   │   └── TranscriptionOptions.cs
│   └── Services/
│       ├── ITranscriptionService.cs
│       ├── ITranscriptionDetailsService (in OpenAITranscriptionService.cs)
│       └── OpenAITranscriptionService.cs  # OpenAI HTTP calls, parsing
└── Client/
    ├── Program.cs                 # Blazor host, HttpClient base address
    ├── App.razor, _Imports.razor
    ├── Pages/
    │   └── Index.razor            # Upload, model choice, stats, transcript, recent, export
    ├── Layout/
    │   └── MainLayout.razor
    └── wwwroot/
        ├── index.html
        ├── css/app.css
        └── js/app.js              # Copy, download, localStorage helpers
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

1. **Sample clips** – Optional “Try a sample” links open short audio files; download one and upload it here to test.
2. **Model** – Choose **“With speakers (recommended)”** (diarized) or **“Basic (Whisper — cheaper)”**.
3. **File** – Use “Choose File” to select an allowed type (e.g. .mp3, .wav, .m4a) and under the configured max size (default 25 MB). Invalid type/size shows an error before upload.
4. **Upload** – Click **Upload and Analyze**. Use **Cancel** to abort the request.
5. **Result** – Stats (duration, words, characters, segments, model) and the transcription (with speaker labels when diarized). If the clip is longer than the recommended limit, a short warning is shown.
6. **Export** – **Copy**, **Download .txt**, or **Download .json** (full details).
7. **Recent** – The last 3 analyses are listed; click a file name to view that result again. Data is stored in the browser’s **localStorage**. Use **Clear recent** to remove all stored entries and clear the list.

---

## API reference

Base URL when running locally: **http://localhost:5200**.

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/health` | Health check. Returns `{ "status": "ok", "timestamp": "..." }`. |
| GET | `/api/audio/limits` | Returns `maxFileSizeBytes`, `maxDurationSeconds`, `allowedExtensions`. |
| POST | `/api/audio/analyze` | Plain-text transcription. Body: `multipart/form-data` with field **`audioFile`**. Response: `text/plain`. |
| POST | `/api/audio/analyze/details?diarize=true` | Structured result. Same body. Response: JSON with `text`, `model`, `durationSeconds`, `segments`, `diarized`, `durationExceedsRecommended`. |

**Request (analyze/details)**  
- Content-Type: `multipart/form-data`  
- Field name: **`audioFile`** (required)  
- Query: `diarize` (optional, default `true`) – when `true`, uses the diarization model and returns segments with speaker labels when available.

**Response (analyze/details, 200)**  
- `text` – Full transcript.  
- `model` – Model used (e.g. `whisper-1`, `gpt-4o-transcribe-diarize`).  
- `durationSeconds` – Audio duration when provided by the API or derived from segments.  
- `segments` – Array of `{ speaker, start, end, text }` (when available).  
- `diarized` – Whether the diarization model was used.  
- `durationExceedsRecommended` – `true` when duration &gt; configured `MaxDurationSeconds`.

**Error responses**  
- 400 – Invalid or missing file, wrong type, or file too large. Body: `{ "message": "...", "detail": "..." }`.  
- 401/403/429/500/502 – Body includes a `message` (and optionally `detail`). The UI maps these to short, user-friendly text.

---

## Configuration

**API – `Api/appsettings.json`** (section `Transcription`):

| Option | Description | Default |
|--------|-------------|--------|
| `MaxFileSizeBytes` | Max upload size in bytes. | 25 MB |
| `MaxDurationSeconds` | Recommended max duration; responses longer than this set `durationExceedsRecommended`. | 900 |
| `AllowedExtensions` | Allowed file extensions (e.g. `.mp3`, `.wav`, `.m4a`, `.webm`, `.mp4`, `.mpeg`, `.mpga`). | (see file) |
| `DefaultModel` | Model for non-diarized requests. | `whisper-1` |
| `DiarizeModel` | Model for diarized requests. | `gpt-4o-transcribe-diarize` |

**Client**  
- Allowed types and max size (25 MB) are enforced in the UI to match the API.  
- The API base URL is set in `Client/Program.cs` (e.g. `http://localhost:5200`).

---

## Local storage (Recent)

- **Key:** `SpeechInsight_Recent`  
- **Content:** JSON array of up to 3 items. Each item has `fileName`, `durationSeconds`, `wordCount`, `transcription`, and full `details` (same shape as the analyze/details response).  
- **When:** Saved after each successful analysis; loaded on app startup.  
- **Clear:** Use the **Clear recent** button in the UI to clear the list and remove the key from localStorage.

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
- **Empty or wrong transcription** – Use an allowed format (e.g. mp3, wav, m4a) and stay under the max file size. For long files, the UI may show a “longer than recommended” warning; you can still use the result.
- **Recent not persisting** – Recent is stored in the browser’s localStorage for this origin. Private/incognito or clearing site data will remove it. Use **Clear recent** to wipe it manually.
