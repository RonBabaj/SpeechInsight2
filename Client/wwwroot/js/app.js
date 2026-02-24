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
  }
};

