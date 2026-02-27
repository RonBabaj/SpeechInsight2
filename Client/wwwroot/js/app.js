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
  // Microphone recording: getUserMedia + MediaRecorder, returns base64-encoded audio (webm) for Blazor.
  recording: {
    _stream: null,
    _recorder: null,
    _chunks: [],
    _resolveStop: null,
    start: function () {
      const self = this;
      return navigator.mediaDevices.getUserMedia({ audio: true }).then(function (stream) {
        self._stream = stream;
        self._chunks = [];
        self._recorder = new MediaRecorder(stream);
        self._recorder.ondataavailable = function (e) { if (e.data.size > 0) self._chunks.push(e.data); };
        self._recorder.onstop = function () {
          stream.getTracks().forEach(function (t) { t.stop(); });
          const blob = new Blob(self._chunks, { type: "audio/webm" });
          const reader = new FileReader();
          reader.onloadend = function () {
            const dataUrl = reader.result;
            const base64 = (dataUrl && typeof dataUrl === "string" && dataUrl.indexOf(",") >= 0) ? dataUrl.split(",")[1] : "";
            if (self._resolveStop) self._resolveStop(base64);
          };
          reader.readAsDataURL(blob);
        };
        self._recorder.start();
        return true;
      });
    },
    stop: function () {
      const self = this;
      return new Promise(function (resolve) {
        self._resolveStop = resolve;
        if (self._recorder && self._recorder.state !== "inactive") self._recorder.stop();
        else resolve("");
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

