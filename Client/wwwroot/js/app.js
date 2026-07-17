window.speechInsight = {
  copyText: async (text) => {
    if (!navigator.clipboard) throw new Error("Clipboard API not available.");
    await navigator.clipboard.writeText(text ?? "");
    return true;
  },
  download: (filename, contentType, content) => {
    const blob = new Blob([content ?? ""], { type: contentType ?? "application/octet-stream" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = filename || "download";
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  },
  getLocalStorage: (key) => {
    try {
      return localStorage.getItem(key);
    } catch {
      return null;
    }
  },
  setLocalStorage: (key, value) => {
    try {
      localStorage.setItem(key, value ?? "");
    } catch { /* quota or disabled */ }
  },
  scrollToId: (id) => {
    const el = document.getElementById(id);
    if (el) el.scrollIntoView({ behavior: "smooth", block: "start" });
  },
  // Microphone recording: getUserMedia + MediaRecorder, converted to WAV for OpenAI compatibility.
  // gpt-4o-transcribe / diarize models often reject raw MediaRecorder WebM as "corrupted or unsupported".
  recording: {
    _stream: null,
    _recorder: null,
    _chunks: [],
    _mimeType: "",
    _resolveStop: null,

    _pickMimeType: function () {
      if (!window.MediaRecorder || typeof MediaRecorder.isTypeSupported !== "function") return "";
      const candidates = [
        "audio/webm;codecs=opus",
        "audio/webm",
        "audio/mp4",
        "audio/ogg;codecs=opus",
        "audio/ogg"
      ];
      for (let i = 0; i < candidates.length; i++) {
        if (MediaRecorder.isTypeSupported(candidates[i])) return candidates[i];
      }
      return "";
    },

    _blobToBase64: function (blob) {
      return new Promise(function (resolve, reject) {
        const reader = new FileReader();
        reader.onloadend = function () {
          const dataUrl = reader.result;
          const base64 = (dataUrl && typeof dataUrl === "string" && dataUrl.indexOf(",") >= 0)
            ? dataUrl.split(",")[1]
            : "";
          resolve(base64);
        };
        reader.onerror = function () { reject(reader.error || new Error("Failed to read recording.")); };
        reader.readAsDataURL(blob);
      });
    },

    // Encode an AudioBuffer as 16-bit PCM mono WAV (widely accepted by OpenAI transcription models).
    _audioBufferToWavBlob: function (audioBuffer) {
      const numChannels = 1;
      const sampleRate = audioBuffer.sampleRate;
      const length = audioBuffer.length;
      const channelCount = audioBuffer.numberOfChannels;
      const samples = new Float32Array(length);

      // Mix down to mono.
      for (let ch = 0; ch < channelCount; ch++) {
        const data = audioBuffer.getChannelData(ch);
        for (let i = 0; i < length; i++) samples[i] += data[i] / channelCount;
      }

      const bytesPerSample = 2;
      const blockAlign = numChannels * bytesPerSample;
      const dataSize = length * blockAlign;
      const buffer = new ArrayBuffer(44 + dataSize);
      const view = new DataView(buffer);

      const writeString = function (offset, str) {
        for (let i = 0; i < str.length; i++) view.setUint8(offset + i, str.charCodeAt(i));
      };

      writeString(0, "RIFF");
      view.setUint32(4, 36 + dataSize, true);
      writeString(8, "WAVE");
      writeString(12, "fmt ");
      view.setUint32(16, 16, true);
      view.setUint16(20, 1, true); // PCM
      view.setUint16(22, numChannels, true);
      view.setUint32(24, sampleRate, true);
      view.setUint32(28, sampleRate * blockAlign, true);
      view.setUint16(32, blockAlign, true);
      view.setUint16(34, 16, true);
      writeString(36, "data");
      view.setUint32(40, dataSize, true);

      let offset = 44;
      for (let i = 0; i < length; i++) {
        const s = Math.max(-1, Math.min(1, samples[i]));
        view.setInt16(offset, s < 0 ? s * 0x8000 : s * 0x7fff, true);
        offset += 2;
      }

      return new Blob([buffer], { type: "audio/wav" });
    },

    _convertToWavBase64: async function (blob) {
      const AudioCtx = window.AudioContext || window.webkitAudioContext;
      if (!AudioCtx) throw new Error("Web Audio API is not available in this browser.");

      const arrayBuffer = await blob.arrayBuffer();
      const audioCtx = new AudioCtx();
      try {
        // slice() copies — decodeAudioData may detach the buffer in some browsers.
        const audioBuffer = await audioCtx.decodeAudioData(arrayBuffer.slice(0));
        const wavBlob = this._audioBufferToWavBlob(audioBuffer);
        return await this._blobToBase64(wavBlob);
      } finally {
        if (typeof audioCtx.close === "function") {
          try { await audioCtx.close(); } catch { /* ignore */ }
        }
      }
    },

    start: function () {
      const self = this;
      return navigator.mediaDevices.getUserMedia({ audio: true }).then(function (stream) {
        self._stream = stream;
        self._chunks = [];
        self._mimeType = self._pickMimeType();
        self._recorder = self._mimeType
          ? new MediaRecorder(stream, { mimeType: self._mimeType })
          : new MediaRecorder(stream);
        // Keep the actual type the recorder chose (may differ from the request).
        if (self._recorder.mimeType) self._mimeType = self._recorder.mimeType;

        self._recorder.ondataavailable = function (e) {
          if (e.data && e.data.size > 0) self._chunks.push(e.data);
        };
        self._recorder.onstop = function () {
          stream.getTracks().forEach(function (t) { t.stop(); });
          self._stream = null;

          const type = (self._mimeType || "audio/webm").split(";")[0] || "audio/webm";
          const blob = new Blob(self._chunks, { type: type });
          const resolve = self._resolveStop;
          self._resolveStop = null;

          if (!resolve) return;
          if (!blob.size) {
            resolve("");
            return;
          }

          // Always convert to WAV — gpt-4o-transcribe-diarize often rejects MediaRecorder WebM/MP4.
          self._convertToWavBase64(blob)
            .then(function (base64) { resolve(base64); })
            .catch(function (err) {
              console.error("SpeechInsight: failed to convert recording to WAV", err);
              resolve("");
            });
        };

        // Timeslice ensures chunks are emitted during recording (more reliable final blob).
        self._recorder.start(250);
        return true;
      });
    },

    stop: function () {
      const self = this;
      return new Promise(function (resolve) {
        self._resolveStop = resolve;
        if (self._recorder && self._recorder.state !== "inactive") {
          try { self._recorder.requestData(); } catch { /* optional */ }
          self._recorder.stop();
        } else {
          resolve("");
        }
      });
    }
  },
  theme: {
    key: "SpeechInsight_Theme",
    get: () => {
      try {
        return document.documentElement.getAttribute("data-theme") || "light";
      } catch {
        return "light";
      }
    },
    set: (value) => {
      const theme = value === "dark" ? "dark" : "light";
      try {
        document.documentElement.setAttribute("data-theme", theme);
        localStorage.setItem("SpeechInsight_Theme", theme);
      } catch { }
      return theme;
    },
    init: () => {
      try {
        const saved = localStorage.getItem("SpeechInsight_Theme");
        const theme = saved === "dark" ? "dark" : "light";
        document.documentElement.setAttribute("data-theme", theme);
      } catch { }
    }
  }
};
window.getTheme = () => window.speechInsight.theme.get();
window.setTheme = (value) => window.speechInsight.theme.set(value);
window.speechInsight.theme.init();
