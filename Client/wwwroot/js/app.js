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
  // Microphone recording via MediaRecorder. Returns JSON: { base64, mimeType, fileName }.
  // Extension always matches the browser's actual container (critical for OpenAI).
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

    _extensionForMime: function (mimeType) {
      const bare = (mimeType || "").split(";")[0].trim().toLowerCase();
      if (bare.indexOf("mp4") >= 0 || bare.indexOf("m4a") >= 0 || bare.indexOf("aac") >= 0) return "m4a";
      if (bare.indexOf("ogg") >= 0 || bare.indexOf("opus") >= 0 && bare.indexOf("webm") < 0) return "ogg";
      if (bare.indexOf("wav") >= 0) return "wav";
      if (bare.indexOf("mpeg") >= 0 || bare.indexOf("mp3") >= 0) return "mp3";
      return "webm";
    },

    _contentTypeForMime: function (mimeType) {
      const bare = (mimeType || "").split(";")[0].trim().toLowerCase();
      if (bare.indexOf("mp4") >= 0 || bare.indexOf("m4a") >= 0) return "audio/mp4";
      if (bare.indexOf("ogg") >= 0) return "audio/ogg";
      if (bare.indexOf("wav") >= 0) return "audio/wav";
      if (bare.indexOf("mpeg") >= 0 || bare.indexOf("mp3") >= 0) return "audio/mpeg";
      return "audio/webm";
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

    start: function () {
      const self = this;
      if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
        return Promise.reject(new Error("Microphone API is not available in this browser."));
      }
      if (!window.MediaRecorder) {
        return Promise.reject(new Error("MediaRecorder is not available in this browser."));
      }

      return navigator.mediaDevices.getUserMedia({ audio: true }).then(function (stream) {
        self._stream = stream;
        self._chunks = [];
        self._mimeType = self._pickMimeType();
        self._recorder = self._mimeType
          ? new MediaRecorder(stream, { mimeType: self._mimeType })
          : new MediaRecorder(stream);
        if (self._recorder.mimeType) self._mimeType = self._recorder.mimeType;

        self._recorder.ondataavailable = function (e) {
          if (e.data && e.data.size > 0) self._chunks.push(e.data);
        };
        self._recorder.onstop = function () {
          stream.getTracks().forEach(function (t) { t.stop(); });
          self._stream = null;

          const resolve = self._resolveStop;
          self._resolveStop = null;
          if (!resolve) return;

          const mime = self._mimeType || "audio/webm";
          const contentType = self._contentTypeForMime(mime);
          const ext = self._extensionForMime(mime);
          const blob = new Blob(self._chunks, { type: contentType });

          if (!blob.size) {
            resolve("");
            return;
          }

          self._blobToBase64(blob).then(function (base64) {
            resolve(JSON.stringify({
              base64: base64,
              mimeType: contentType,
              fileName: "recording." + ext
            }));
          }).catch(function (err) {
            console.error("SpeechInsight: failed to read recording", err);
            resolve("");
          });
        };

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
