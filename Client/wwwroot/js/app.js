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
  // Microphone recording: capture PCM via Web Audio API and encode 16 kHz mono 16-bit WAV.
  // Avoids MediaRecorder WebM/MP4, which gpt-4o-transcribe-diarize often rejects.
  recording: {
    _stream: null,
    _audioCtx: null,
    _source: null,
    _processor: null,
    _mute: null,
    _pcmChunks: [],
    _inputSampleRate: 48000,
    _recording: false,

    _floatTo16BitPcm: function (input) {
      const out = new Int16Array(input.length);
      for (let i = 0; i < input.length; i++) {
        const s = Math.max(-1, Math.min(1, input[i]));
        out[i] = s < 0 ? s * 0x8000 : s * 0x7fff;
      }
      return out;
    },

    // Linear resample Float32 mono PCM to target rate.
    _resample: function (input, fromRate, toRate) {
      if (fromRate === toRate) return input;
      const ratio = fromRate / toRate;
      const newLen = Math.max(1, Math.round(input.length / ratio));
      const out = new Float32Array(newLen);
      for (let i = 0; i < newLen; i++) {
        const srcIndex = i * ratio;
        const i0 = Math.floor(srcIndex);
        const i1 = Math.min(i0 + 1, input.length - 1);
        const frac = srcIndex - i0;
        out[i] = input[i0] * (1 - frac) + input[i1] * frac;
      }
      return out;
    },

    _encodeWavBytes: function (floatSamples, sampleRate) {
      const pcm = this._floatTo16BitPcm(floatSamples);
      const numChannels = 1;
      const bytesPerSample = 2;
      const blockAlign = numChannels * bytesPerSample;
      const dataSize = pcm.length * bytesPerSample;
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
      for (let i = 0; i < pcm.length; i++, offset += 2) {
        view.setInt16(offset, pcm[i], true);
      }

      // Return raw bytes to Blazor (maps to byte[]) — avoids base64 corruption.
      return new Uint8Array(buffer);
    },

    _cleanup: function () {
      try {
        if (this._processor) {
          this._processor.onaudioprocess = null;
          this._processor.disconnect();
        }
      } catch { /* ignore */ }
      try { if (this._source) this._source.disconnect(); } catch { /* ignore */ }
      try { if (this._mute) this._mute.disconnect(); } catch { /* ignore */ }
      if (this._stream) {
        this._stream.getTracks().forEach(function (t) { t.stop(); });
      }
      const ctx = this._audioCtx;
      this._stream = null;
      this._source = null;
      this._processor = null;
      this._mute = null;
      this._audioCtx = null;
      this._recording = false;
      if (ctx && typeof ctx.close === "function") {
        try { ctx.close(); } catch { /* ignore */ }
      }
    },

    start: function () {
      const self = this;
      if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
        return Promise.reject(new Error("Microphone API is not available in this browser."));
      }
      const AudioCtx = window.AudioContext || window.webkitAudioContext;
      if (!AudioCtx) {
        return Promise.reject(new Error("Web Audio API is not available in this browser."));
      }

      self._cleanup();
      self._pcmChunks = [];

      return navigator.mediaDevices.getUserMedia({
        audio: {
          channelCount: 1,
          echoCancellation: true,
          noiseSuppression: true
        }
      }).then(function (stream) {
        self._stream = stream;
        self._audioCtx = new AudioCtx();
        // Browsers often ignore a requested rate; record actual rate and resample on stop.
        self._inputSampleRate = self._audioCtx.sampleRate || 48000;

        return self._audioCtx.resume().then(function () {
          self._source = self._audioCtx.createMediaStreamSource(stream);
          // ScriptProcessor is deprecated but widely available; AudioWorklet needs a separate module URL.
          const bufferSize = 4096;
          self._processor = self._audioCtx.createScriptProcessor(bufferSize, 1, 1);
          self._processor.onaudioprocess = function (e) {
            if (!self._recording) return;
            const input = e.inputBuffer.getChannelData(0);
            self._pcmChunks.push(new Float32Array(input));
          };

          // Keep the processor graph alive without audible feedback.
          self._mute = self._audioCtx.createGain();
          self._mute.gain.value = 0;
          self._source.connect(self._processor);
          self._processor.connect(self._mute);
          self._mute.connect(self._audioCtx.destination);

          self._recording = true;
          return true;
        });
      }).catch(function (err) {
        self._cleanup();
        throw err;
      });
    },

    stop: function () {
      const self = this;
      return new Promise(function (resolve) {
        try {
          self._recording = false;
          const chunks = self._pcmChunks || [];
          const inputRate = self._inputSampleRate || 48000;
          self._cleanup();

          if (!chunks.length) {
            resolve(null);
            return;
          }

          let total = 0;
          for (let i = 0; i < chunks.length; i++) total += chunks[i].length;
          const merged = new Float32Array(total);
          let offset = 0;
          for (let i = 0; i < chunks.length; i++) {
            merged.set(chunks[i], offset);
            offset += chunks[i].length;
          }
          self._pcmChunks = [];

          // 24 kHz mono PCM WAV — preferred by gpt-4o-transcribe-diarize (whisper-1 is more forgiving).
          const targetRate = 24000;
          const resampled = self._resample(merged, inputRate, targetRate);
          if (!resampled.length) {
            resolve(null);
            return;
          }

          resolve(self._encodeWavBytes(resampled, targetRate));
        } catch (err) {
          console.error("SpeechInsight: failed to finalize recording", err);
          self._cleanup();
          resolve(null);
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
