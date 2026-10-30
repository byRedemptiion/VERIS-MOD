"use strict";

// Keep Discord's original preload first. If ViiCord fails afterwards, the
// overlay still has DiscordNative / IPC exactly as Discord expects.
try {
  require("./overlayPreload");
} catch (error) {
  try { console.error("[ViiCordOverlay] original preload failed", error); } catch {}
  throw error;
}

try {
  const { contextBridge, webFrame } = require("electron");
  const fs = require("fs");
  const path = require("path");

  const VERSION = "1.3.7";
  // Legacy Audely compatibility : les installations d'avant ViiCord utilisent encore le dossier « Audely »
  // (devenu un lien vers « ViiCord » après la migration). On préfère « ViiCord » s'il contient la configuration.
  const APPDATA_DIR = process.env.APPDATA || "";
  const ROOT_CANDIDATES = [path.join(APPDATA_DIR, "ViiCord"), path.join(APPDATA_DIR, "Audely")];
  const ROOT = ROOT_CANDIDATES.find(dir => fs.existsSync(path.join(dir, "runtime-config.json"))) || ROOT_CANDIDATES[0];
  const CONFIG = path.join(ROOT, "runtime-config.json");
  const LOG = path.join(ROOT, "overlay-runtime.log");
  const MAX_BYTES = 5 * 1024 * 1024;

  function appendLog(message) {
    try {
      const line = `${new Date().toISOString()} ${String(message).slice(0, 1200)}\n`;
      if (fs.existsSync(LOG) && fs.statSync(LOG).size > 384 * 1024) {
        fs.writeFileSync(LOG, "", "utf8");
      }
      fs.appendFileSync(LOG, line, "utf8");
    } catch {}
  }

  function mimeFor(file) {
    const ext = path.extname(file).toLowerCase();
    if (ext === ".wav") return "audio/wav";
    if (ext === ".ogg") return "audio/ogg";
    if (ext === ".m4a") return "audio/mp4";
    if (ext === ".aac") return "audio/aac";
    if (ext === ".webm") return "audio/webm";
    if (ext === ".flac") return "audio/flac";
    return "audio/mpeg";
  }

  function readPayload() {
    const payload = { sounds: {}, needles: {} };

    try {
      if (!fs.existsSync(CONFIG)) {
        appendLog("CONFIG_ABSENT");
        return payload;
      }

      const cfg = JSON.parse(fs.readFileSync(CONFIG, "utf8"));

      for (const target of Array.isArray(cfg.targets) ? cfg.targets : []) {
        const id = String(target && target.id || "");
        const relative = String(target && target.file || "");
        if (!id || !relative) continue;

        const file = path.resolve(ROOT, relative);
        if (!file.toLowerCase().startsWith(ROOT.toLowerCase())) continue;
        if (!fs.existsSync(file)) continue;

        const st = fs.statSync(file);
        if (!st.isFile() || st.size <= 0 || st.size > MAX_BYTES) continue;

        const rawVolume = Number(target.volume);
        const volume = Number.isFinite(rawVolume)
          ? Math.max(0, Math.min(100, rawVolume)) / 100
          : 1;

        const bytes = fs.readFileSync(file);
        payload.sounds[id] = {
          target: id,
          mime: mimeFor(file),
          b64: bytes.toString("base64"),
          sha256: String(target.sha256 || ""),
          volume
        };

        for (const needle of Array.isArray(target.needles) ? target.needles : []) {
          if (typeof needle === "string" && needle.length > 0) {
            payload.needles[needle.toLowerCase()] = id;
          }
        }
      }
    } catch (error) {
      appendLog(`CONFIG_ERROR ${error && error.message || error}`);
    }

    return payload;
  }

  function buildMainWorldCode(payload) {
    return `(() => {
      const incoming = ${JSON.stringify(payload)};
      const bridge = globalThis.__AudelyOverlayBridge;
      const trace = message => {
        try { bridge && bridge.log && bridge.log(String(message)); } catch {}
        try { console.log("[AudelyOverlay] " + String(message)); } catch {}
      };

      globalThis.__AUDELY_OVERLAY_SOUNDS = incoming.sounds || {};
      globalThis.__AUDELY_OVERLAY_NEEDLES = incoming.needles || {};
      globalThis.__AUDELY_OVERLAY_BLOBS = globalThis.__AUDELY_OVERLAY_BLOBS || {};
      globalThis.__AUDELY_OVERLAY_LAST = globalThis.__AUDELY_OVERLAY_LAST || {};

      const decodeBytes = sound => {
        const binary = atob(String(sound && sound.b64 || ""));
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
        return bytes;
      };

      const blobUrlFor = targetId => {
        const sound = globalThis.__AUDELY_OVERLAY_SOUNDS[targetId];
        if (!sound) return null;

        const cached = globalThis.__AUDELY_OVERLAY_BLOBS[targetId];
        if (cached && cached.sha256 === String(sound.sha256 || "") && cached.url) {
          return cached.url;
        }

        if (cached && cached.url) {
          try { URL.revokeObjectURL(cached.url); } catch {}
        }

        const blob = new Blob([decodeBytes(sound)], { type: String(sound.mime || "audio/mpeg") });
        const url = URL.createObjectURL(blob);
        globalThis.__AUDELY_OVERLAY_BLOBS[targetId] = {
          sha256: String(sound.sha256 || ""),
          url
        };
        return url;
      };

      const matchTarget = value => {
        let src = "";
        try { src = String(value && value.url || value || "").toLowerCase(); } catch {}
        if (!src || src.startsWith("blob:") || src.startsWith("data:")) return null;

        for (const [needle, targetId] of Object.entries(globalThis.__AUDELY_OVERLAY_NEEDLES || {})) {
          if (needle && src.includes(String(needle).toLowerCase())) return targetId;
        }
        return null;
      };

      const playCustom = (targetId, reason) => {
        const sound = globalThis.__AUDELY_OVERLAY_SOUNDS[targetId];
        if (!sound) return false;

        const now = Date.now();
        const last = Number(globalThis.__AUDELY_OVERLAY_LAST[targetId] || 0);
        if (now - last < 150) return true;
        globalThis.__AUDELY_OVERLAY_LAST[targetId] = now;

        try {
          const url = blobUrlFor(targetId);
          if (!url) return false;

          const audio = new Audio();
          audio.src = url;
          audio.volume = Math.max(0, Math.min(1, Number(sound.volume)));
          const result = audio.play();
          if (result && typeof result.catch === "function") {
            result.catch(error => trace("CUSTOM_PLAY_ERROR target=" + targetId + " reason=" + reason + " error=" + String(error)));
          }
          trace("CUSTOM_PLAY target=" + targetId + " reason=" + reason + " volume=" + Math.round(Number(sound.volume) * 100));
          return true;
        } catch (error) {
          trace("CUSTOM_PLAY_THROW target=" + targetId + " error=" + String(error));
          return false;
        }
      };

      const makeSilentWav = () => {
        const sampleRate = 8000;
        const samples = 800;
        const bytes = new Uint8Array(44 + samples * 2);
        const view = new DataView(bytes.buffer);
        const ascii = (offset, value) => {
          for (let i = 0; i < value.length; i++) view.setUint8(offset + i, value.charCodeAt(i));
        };
        ascii(0, "RIFF");
        view.setUint32(4, 36 + samples * 2, true);
        ascii(8, "WAVE");
        ascii(12, "fmt ");
        view.setUint32(16, 16, true);
        view.setUint16(20, 1, true);
        view.setUint16(22, 1, true);
        view.setUint32(24, sampleRate, true);
        view.setUint32(28, sampleRate * 2, true);
        view.setUint16(32, 2, true);
        view.setUint16(34, 16, true);
        ascii(36, "data");
        view.setUint32(40, samples * 2, true);
        return bytes;
      };

      if (!globalThis.__AUDELY_OVERLAY_FETCH_PATCHED && typeof globalThis.fetch === "function") {
        const nativeFetch = globalThis.fetch.bind(globalThis);
        globalThis.fetch = function(input, init) {
          const targetId = matchTarget(input);
          if (targetId && globalThis.__AUDELY_OVERLAY_SOUNDS[targetId]) {
            playCustom(targetId, "fetch");
            trace("FETCH_HIT target=" + targetId + " src=" + String(input && input.url || input || "").slice(-160));
            const silent = makeSilentWav();
            return Promise.resolve(new Response(silent, {
              status: 200,
              headers: {
                "Content-Type": "audio/wav",
                "Content-Length": String(silent.byteLength),
                "Cache-Control": "no-store"
              }
            }));
          }
          return nativeFetch(input, init);
        };
        globalThis.__AUDELY_OVERLAY_FETCH_PATCHED = true;
        trace("FETCH_PATCHED");
      }

      if (!globalThis.__AUDELY_OVERLAY_MEDIA_PATCHED && globalThis.HTMLMediaElement) {
        const proto = globalThis.HTMLMediaElement.prototype;
        const srcDesc = Object.getOwnPropertyDescriptor(proto, "src");

        if (srcDesc && typeof srcDesc.set === "function" && typeof srcDesc.get === "function") {
          Object.defineProperty(proto, "src", {
            configurable: srcDesc.configurable,
            enumerable: srcDesc.enumerable,
            get: srcDesc.get,
            set(value) {
              const targetId = matchTarget(value);
              if (targetId && globalThis.__AUDELY_OVERLAY_SOUNDS[targetId]) {
                const url = blobUrlFor(targetId);
                const sound = globalThis.__AUDELY_OVERLAY_SOUNDS[targetId];
                try { this.volume = Math.max(0, Math.min(1, Number(sound.volume))); } catch {}
                trace("MEDIA_SRC_HIT target=" + targetId);
                return srcDesc.set.call(this, url);
              }
              return srcDesc.set.call(this, value);
            }
          });
        }

        const nativeSetAttribute = proto.setAttribute;
        if (typeof nativeSetAttribute === "function") {
          proto.setAttribute = function(name, value) {
            if (String(name).toLowerCase() === "src") {
              const targetId = matchTarget(value);
              if (targetId && globalThis.__AUDELY_OVERLAY_SOUNDS[targetId]) {
                const sound = globalThis.__AUDELY_OVERLAY_SOUNDS[targetId];
                try { this.volume = Math.max(0, Math.min(1, Number(sound.volume))); } catch {}
                trace("MEDIA_ATTR_HIT target=" + targetId);
                return nativeSetAttribute.call(this, name, blobUrlFor(targetId));
              }
            }
            return nativeSetAttribute.call(this, name, value);
          };
        }

        const nativePlay = proto.play;
        if (typeof nativePlay === "function") {
          proto.play = function(...args) {
            try {
              const src = (srcDesc && srcDesc.get ? srcDesc.get.call(this) : "") || this.currentSrc || this.getAttribute("src") || "";
              const targetId = matchTarget(src);
              if (targetId && globalThis.__AUDELY_OVERLAY_SOUNDS[targetId]) {
                const sound = globalThis.__AUDELY_OVERLAY_SOUNDS[targetId];
                try { this.volume = Math.max(0, Math.min(1, Number(sound.volume))); } catch {}
                if (srcDesc && srcDesc.set) srcDesc.set.call(this, blobUrlFor(targetId));
                trace("MEDIA_PLAY_HIT target=" + targetId);
              }
            } catch {}
            return nativePlay.apply(this, args);
          };
        }

        globalThis.__AUDELY_OVERLAY_MEDIA_PATCHED = true;
        trace("MEDIA_PATCHED");
      }

      globalThis.__AUDELY_OVERLAY_HOOK_V124 = true;
      trace("HOOK_ACTIVE version=1.3.7 targets=" + Object.keys(globalThis.__AUDELY_OVERLAY_SOUNDS).join(","));
      return "Audely overlay hook active";
    })();`;
  }

  try {
    contextBridge.exposeInMainWorld("__AudelyOverlayBridge", {
      version: VERSION,
      log: message => appendLog(String(message))
    });
  } catch (error) {
    appendLog(`CONTEXT_BRIDGE_ERROR ${error && error.message || error}`);
  }

  const inject = label => {
    try {
      const payload = readPayload();
      appendLog(`INJECT_REQUEST label=${label} targets=${Object.keys(payload.sounds).join(",") || "<none>"} needles=${Object.keys(payload.needles).length}`);
      const code = buildMainWorldCode(payload);
      const result = webFrame.executeJavaScript(code, false);
      if (result && typeof result.then === "function") {
        result.then(value => appendLog(`INJECT_OK label=${label} result=${String(value)}`))
          .catch(error => appendLog(`INJECT_ERROR label=${label} error=${error && error.message || error}`));
      }
    } catch (error) {
      appendLog(`INJECT_THROW label=${label} error=${error && error.message || error}`);
    }
  };

  appendLog(`PRELOAD_START version=${VERSION} pid=${process.pid}`);

  // Preload scripts run before the page's own scripts. Try immediately so
  // fetch/media can be wrapped before Discord's overlay bundle initializes.
  inject("preload");

  try {
    window.addEventListener("DOMContentLoaded", () => inject("dom-ready"), { once: true });
  } catch {}
} catch (error) {
  try {
    const fs = require("fs");
    const path = require("path");
    const log = path.join(process.env.APPDATA || "", "ViiCord", "overlay-runtime.log");
    fs.appendFileSync(log, `${new Date().toISOString()} PRELOAD_FATAL ${error && error.stack || error}\n`, "utf8");
  } catch {}
}