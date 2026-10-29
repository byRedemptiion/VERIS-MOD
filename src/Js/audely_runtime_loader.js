const { app, BrowserWindow, webContents } = require("electron");
const fs = require("fs");
const path = require("path");

const ROOT = __dirname;
const CONFIG = path.join(ROOT, "runtime-config.json");
const LOG = path.join(ROOT, "runtime-loader.log");
const MAX_BYTES = 5 * 1024 * 1024;
const LOADER_VERSION = "1.3.7";

function log(message) {
  try {
    fs.appendFileSync(LOG, `${new Date().toISOString()} ${message}\n`, "utf8");
  } catch {}
}

try {
  if (fs.existsSync(LOG) && fs.statSync(LOG).size > 384 * 1024) {
    fs.writeFileSync(LOG, "", "utf8");
  }
} catch {}

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
      log("config absent");
      return payload;
    }

    const cfg = JSON.parse(fs.readFileSync(CONFIG, "utf8"));

    for (const target of Array.isArray(cfg.targets) ? cfg.targets : []) {
      const id = String(target?.id || "");

      if (!id) {
        log("skip target: id absent");
        continue;
      }

      if (!target.file) {
        log(`skip target=${id}: file absent dans config`);
        continue;
      }

      if (!Array.isArray(target.needles) || target.needles.length === 0) {
        log(`skip target=${id}: needles absents`);
        continue;
      }

      const file = path.resolve(ROOT, target.file);

      if (!file.startsWith(ROOT)) {
        log(`skip target=${id}: chemin hors Audely`);
        continue;
      }

      if (!fs.existsSync(file)) {
        log(`skip target=${id}: fichier audio absent path=${file}`);
        continue;
      }

      const st = fs.statSync(file);
      if (!st.isFile() || st.size <= 0 || st.size > MAX_BYTES) {
        log(`skip target=${id}: taille audio invalide bytes=${st.size}`);
        continue;
      }

      const volumeRaw = Number(target.volume);
      const volume = Number.isFinite(volumeRaw)
        ? Math.max(0, Math.min(100, volumeRaw))
        : 100;

      const bytes = fs.readFileSync(file);

      payload.sounds[id] = {
        target: id,
        mime: mimeFor(file),
        b64: bytes.toString("base64"),
        sha256: String(target.sha256 || ""),
        volume: volume / 100
      };

      for (const needle of target.needles) {
        if (typeof needle === "string" && needle.length > 0) {
          payload.needles[needle.toLowerCase()] = id;
        }
      }
    }

    log(
      `payload loaderVersion=${LOADER_VERSION} ` +
      `targets=${Object.keys(payload.sounds).join(",") || "<none>"} ` +
      `needles=${Object.keys(payload.needles).length}`
    );
  } catch (error) {
    log(`config error: ${error?.message || error}`);
  }

  return payload;
}

function buildRendererCode(payload) {
  return `(() => {
    const incoming = ${JSON.stringify(payload)};

    globalThis.__AUDELY_SAFE_SOUNDS = incoming.sounds || {};
    globalThis.__AUDELY_SAFE_NEEDLES = incoming.needles || {};
    globalThis.__AUDELY_BLOB_CACHE = globalThis.__AUDELY_BLOB_CACHE || {};
    globalThis.__AUDELY_TRACE_SEEN = globalThis.__AUDELY_TRACE_SEEN || new Set();

    const trace = message => {
      try {
        const key = String(message);
        if (globalThis.__AUDELY_TRACE_SEEN.has(key)) return;
        if (globalThis.__AUDELY_TRACE_SEEN.size > 220) return;
        globalThis.__AUDELY_TRACE_SEEN.add(key);
        console.log("[AudelySafe] " + key);
      } catch {}
    };

    const revokeChangedBlobUrls = () => {
      try {
        for (const [id, cached] of Object.entries(globalThis.__AUDELY_BLOB_CACHE || {})) {
          const next = globalThis.__AUDELY_SAFE_SOUNDS[id];
          if (!next || !cached || cached.sha256 !== String(next.sha256 || "")) {
            try { URL.revokeObjectURL(cached.url); } catch {}
            delete globalThis.__AUDELY_BLOB_CACHE[id];
          }
        }
      } catch {}
    };

    revokeChangedBlobUrls();

    // ---------------------------------------------------------------
    // Discord internal message layer (V1.3.7)
    // ---------------------------------------------------------------
    // When Discord is unfocused (for example while a game is active),
    // message notifications can go through DesktopNotificationUtils instead
    // of the normal HTMLMediaElement path. We patch that layer directly,
    // and also patch SoundUtils.createSound as a second route.
    const audelyMakeBlobUrl = sound => {
      const id = String(sound && sound.target || "");
      const sha = String(sound && sound.sha256 || "");
      const cached = globalThis.__AUDELY_BLOB_CACHE[id];

      if (cached && cached.sha256 === sha && cached.url) return cached.url;

      const binary = atob(String(sound && sound.b64 || ""));
      const bytes = new Uint8Array(binary.length);
      for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);

      const blob = new Blob(
        [bytes],
        { type: String(sound && sound.mime || "audio/mpeg") }
      );
      const url = URL.createObjectURL(blob);
      globalThis.__AUDELY_BLOB_CACHE[id] = { sha256: sha, url: url };
      return url;
    };

    const audelyMessageTypeToTarget = value => {
      const type = String(value || "").toLowerCase();
      const clean = type
        .replace(/^.*[\/]/, "")
        .replace(/\.(?:mp3|wav|ogg|m4a)$/i, "");

      // Current Discord semantics:
      // message1 = normal message notifications
      // message3 = current-channel message notification
      // message2 = Friend Started Playing (intentionally NOT mapped here)
      if (
        clean === "message1" ||
        clean === "message3" ||
        /(?:^|_)message1$/.test(clean) ||
        /(?:^|_)message3$/.test(clean)
      ) {
        return "message1";
      }

      const logicalTargets = [
        "user_join",
        "user_leave",
        "mute",
        "unmute",
        "deafen",
        "undeafen",
        "reconnect",
        "disconnect",
        "call_ringing",
        "call_calling"
      ];

      for (const targetId of logicalTargets) {
        if (
          clean === targetId ||
          clean.endsWith("_" + targetId)
        ) {
          return targetId;
        }
      }

      return null;
    };

    const audelyPlayDirect = (targetId, reason, loop, listenerDuration) => {
      const sound = globalThis.__AUDELY_SAFE_SOUNDS[targetId];
      if (!sound) return null;

      // DesktopNotificationUtils + SoundUtils may both see the same message.
      // Avoid playing our custom sound twice for the same notification.
      const now = Date.now();
      const last = Number(globalThis.__AUDELY_MESSAGE_LAST_PLAY || 0);
      if (targetId === "message1" && now - last < 180) {
        trace("MESSAGE_DEDUPE reason=" + String(reason || ""));
        return Promise.resolve(true);
      }
      if (targetId === "message1") globalThis.__AUDELY_MESSAGE_LAST_PLAY = now;

      try {
        const url = audelyMakeBlobUrl(sound);
        const audio = new Audio();
        audio.src = url;
        audio.volume = Math.max(0, Math.min(1, Number(sound.volume)));
        audio.loop = Boolean(loop);

        globalThis.__AUDELY_DIRECT_AUDIOS =
          globalThis.__AUDELY_DIRECT_AUDIOS || new Set();
        globalThis.__AUDELY_DIRECT_AUDIOS.add(audio);

        const cleanup = () => {
          try { globalThis.__AUDELY_DIRECT_AUDIOS.delete(audio); } catch {}
        };

        audio.addEventListener("ended", cleanup, { once: true });
        audio.addEventListener("error", cleanup, { once: true });

        trace(
          "DIRECT_CUSTOM target=" + targetId +
          " reason=" + String(reason || "") +
          " volume=" + Math.round(Number(sound.volume) * 100)
        );

        const playResult = audio.play();

        if (listenerDuration !== undefined && listenerDuration !== null) {
          return new Promise((resolve, reject) => {
            let done = false;
            const finish = () => {
              if (done) return;
              done = true;
              resolve(true);
            };

            audio.addEventListener("ended", finish, { once: true });
            audio.addEventListener("error", () => {
              if (done) return;
              done = true;
              reject(new Error("Audely custom message audio failed"));
            }, { once: true });

            const ms = Number(listenerDuration);
            if (Number.isFinite(ms) && ms >= 0) {
              setTimeout(finish, ms);
            }

            if (playResult && typeof playResult.catch === "function") {
              playResult.catch(error => {
                if (done) return;
                done = true;
                reject(error);
              });
            }
          });
        }

        if (playResult && typeof playResult.catch === "function") {
          playResult.catch(error => {
            trace("DIRECT_CUSTOM_ERROR target=" + targetId + " error=" + String(error));
          });
        }

        return playResult || true;
      } catch (error) {
        trace("DIRECT_CUSTOM_ERROR target=" + targetId + " error=" + String(error));
        return null;
      }
    };

    const audelyGetWebpackRequire = () => {
      try {
        if (globalThis.__AUDELY_WEBPACK_REQUIRE) {
          return globalThis.__AUDELY_WEBPACK_REQUIRE;
        }

        const chunks = globalThis.webpackChunkdiscord_app;
        if (!chunks || typeof chunks.push !== "function") return null;

        let webpackRequire = null;
        const chunkId = "audely_" + Date.now() + "_" + Math.random();

        chunks.push([
          [chunkId],
          {},
          requireFn => {
            webpackRequire = requireFn;
          }
        ]);

        try {
          if (Array.isArray(chunks)) chunks.pop();
        } catch {}

        if (webpackRequire) {
          globalThis.__AUDELY_WEBPACK_REQUIRE = webpackRequire;

          trace(
            "WEBPACK_REQUIRE_READY cache=" +
            Boolean(webpackRequire.c) +
            " factories=" +
            Boolean(webpackRequire.m)
          );
        }

        return webpackRequire;
      } catch (error) {
        trace("WEBPACK_REQUIRE_ERROR " + String(error));
        return null;
      }
    };

    const audelyExportObjects = (exportsValue, maxDepth = 2) => {
      const values = [];
      const visited = new Set();

      const add = (value, depth) => {
        if (
          !value ||
          (typeof value !== "object" && typeof value !== "function") ||
          visited.has(value)
        ) {
          return;
        }

        visited.add(value);
        values.push(value);

        if (depth >= maxDepth) return;

        let names = [];
        try {
          names = Object.getOwnPropertyNames(value);
        } catch {}

        for (const name of names) {
          if (
            name === "caller" ||
            name === "callee" ||
            name === "arguments" ||
            name === "prototype"
          ) {
            continue;
          }

          try {
            const child = value[name];
            if (
              child &&
              (typeof child === "object" || typeof child === "function")
            ) {
              add(child, depth + 1);
            }
          } catch {}
        }
      };

      add(exportsValue, 0);
      return values;
    };

    const audelyFunctionSource = value => {
      try {
        if (typeof value !== "function") return "";
        return Function.prototype.toString.call(value);
      } catch {
        return "";
      }
    };

    const audelyFindFactoryIds = (webpackRequire, signatures, mode = "all") => {
      const result = [];
      const factories = webpackRequire && webpackRequire.m;
      if (!factories) return result;

      let entries = [];
      try { entries = Object.entries(factories); } catch {}

      for (const [moduleId, factory] of entries) {
        const source = audelyFunctionSource(factory);
        if (!source) continue;

        const matches =
          mode === "any"
            ? signatures.some(signature => source.includes(signature))
            : signatures.every(signature => source.includes(signature));

        if (matches) result.push(moduleId);
      }

      return result;
    };

    const audelyLoadModuleExports = (webpackRequire, moduleId) => {
      try {
        return webpackRequire(moduleId);
      } catch (error) {
        trace(
          "WEBPACK_MODULE_REQUIRE_ERROR id=" +
          String(moduleId) +
          " error=" +
          String(error)
        );
        return null;
      }
    };

    const audelyFindFunctionProperty = (root, sourceSignatures) => {
      for (const candidate of audelyExportObjects(root, 3)) {
        let names = [];
        try { names = Object.getOwnPropertyNames(candidate); } catch {}

        for (const name of names) {
          let value = null;
          try { value = candidate[name]; } catch { continue; }
          if (typeof value !== "function") continue;

          const source = audelyFunctionSource(value);
          if (
            source &&
            sourceSignatures.every(signature => source.includes(signature))
          ) {
            return { owner: candidate, key: name, fn: value };
          }
        }
      }

      return null;
    };

    const audelyHasProps = (value, props) => {
      if (
        !value ||
        (typeof value !== "object" && typeof value !== "function")
      ) {
        return false;
      }

      for (const prop of props) {
        try {
          if (!(prop in value)) return false;
        } catch {
          return false;
        }
      }

      return true;
    };

    const audelyFindByProps = (webpackRequire, props, depth = 3) => {
      if (!webpackRequire || !webpackRequire.c) return null;

      let modules = [];
      try { modules = Object.values(webpackRequire.c); } catch {}

      for (const moduleValue of modules) {
        if (!moduleValue) continue;

        let exportsValue = null;
        try { exportsValue = moduleValue.exports; } catch {}
        if (!exportsValue) continue;

        for (const candidate of audelyExportObjects(exportsValue, depth)) {
          if (audelyHasProps(candidate, props)) return candidate;
        }
      }

      return null;
    };

    const audelyRequireCandidatesByStrings = () => {
      // V1.3.7: intentionally disabled.
      // Calling webpackRequire(id) on a factory before Discord has naturally
      // loaded its dependencies caused startup errors such as:
      // "Cannot read properties of undefined (reading 'call')".
      return [];
    };

    const audelyFindByPropsWithFactories = (
      webpackRequire,
      props,
      factoryStrings
    ) => {
      const cached = audelyFindByProps(
        webpackRequire,
        props,
        4
      );

      if (cached) {
        return {
          value: cached,
          source: "cache"
        };
      }

      return null;
    };

    const audelyMarkPendingMessage = event => {
      try {
        if (
          !event ||
          event.type !== "MESSAGE_CREATE" ||
          !event.message ||
          !globalThis.__AUDELY_SAFE_SOUNDS ||
          !globalThis.__AUDELY_SAFE_SOUNDS.message1
        ) {
          return;
        }

        // The normal focused path is already handled by Discord's sound
        // engine / Audely's audio hooks. The dispatcher fallback is only
        // armed while Discord is unfocused, which is the game-mode case.
        if (document.hasFocus()) {
          globalThis.__AUDELY_PENDING_MESSAGE = null;
          return;
        }

        const message = event.message;
        const token = String(
          message.id ||
          (String(message.channel_id || "") + ":" + Date.now())
        );

        globalThis.__AUDELY_PENDING_MESSAGE = {
          token: token,
          at: Date.now(),
          handled: false
        };

        trace(
          "MESSAGE_CREATE_PENDING id=" +
          token +
          " channel=" +
          String(message.channel_id || "") +
          " focused=false"
        );

        setTimeout(() => {
          try {
            const pending =
              globalThis.__AUDELY_PENDING_MESSAGE;

            if (
              pending &&
              pending.token === token &&
              Date.now() - Number(pending.at || 0) >= 3500
            ) {
              globalThis.__AUDELY_PENDING_MESSAGE = null;
            }
          } catch {}
        }, 3600);
      } catch (error) {
        trace("MESSAGE_CREATE_PENDING_ERROR " + String(error));
      }
    };

    const audelyConsumePendingMessage = reason => {
      try {
        const pending = globalThis.__AUDELY_PENDING_MESSAGE;
        if (!pending || pending.handled) return false;

        const age = Date.now() - Number(pending.at || 0);
        if (age < 0 || age > 4000) return false;

        pending.handled = true;

        trace(
          "MESSAGE_CREATE_CONSUMED id=" +
          String(pending.token || "") +
          " reason=" +
          String(reason || "")
        );

        audelyPlayDirect(
          "message1",
          String(reason || "MESSAGE_CREATE"),
          false,
          null
        );

        return true;
      } catch (error) {
        trace("MESSAGE_CREATE_CONSUME_ERROR " + String(error));
        return false;
      }
    };

    const audelyPatchDispatcher = dispatcher => {
      try {
        if (
          !dispatcher ||
          typeof dispatcher.dispatch !== "function" ||
          !("isDispatching" in dispatcher)
        ) {
          return false;
        }

        const original = dispatcher.dispatch;

        if (original.__AUDELY_DISPATCH_WRAPPED) {
          globalThis.__AUDELY_DISPATCH_PATCHED = true;
          return true;
        }

        const wrapped = function(...args) {
          try {
            audelyMarkPendingMessage(args[0]);
          } catch {}

          return original.apply(this, args);
        };

        wrapped.__AUDELY_DISPATCH_WRAPPED = true;
        wrapped.__AUDELY_NATIVE = original;

        try {
          dispatcher.dispatch = wrapped;
        } catch {
          try {
            Object.defineProperty(
              dispatcher,
              "dispatch",
              {
                configurable: true,
                writable: true,
                value: wrapped
              }
            );
          } catch {
            return false;
          }
        }

        globalThis.__AUDELY_DISPATCH_PATCHED = true;
        trace("DISCORD_DISPATCH_PATCHED");
        return true;
      } catch (error) {
        trace("DISPATCH_PATCH_ERROR " + String(error));
        return false;
      }
    };

    const audelyPatchNotificationConstructor = () => {
      try {
        const NativeNotification =
          globalThis.__AUDELY_NATIVE_NOTIFICATION ||
          globalThis.Notification;

        if (typeof NativeNotification !== "function") {
          return false;
        }

        if (
          globalThis.Notification &&
          globalThis.Notification.__AUDELY_NOTIFICATION_WRAPPED
        ) {
          globalThis.__AUDELY_NOTIFICATION_PATCHED = true;
          return true;
        }

        globalThis.__AUDELY_NATIVE_NOTIFICATION = NativeNotification;

        const AudelyNotification = function(title, options) {
          let nextOptions = options;

          try {
            const pending =
              globalThis.__AUDELY_PENDING_MESSAGE;
            const age = pending
              ? Date.now() - Number(pending.at || 0)
              : Number.POSITIVE_INFINITY;

            if (
              pending &&
              age >= 0 &&
              age <= 4000 &&
              globalThis.__AUDELY_SAFE_SOUNDS &&
              globalThis.__AUDELY_SAFE_SOUNDS.message1
            ) {
              nextOptions = Object.assign(
                {},
                options && typeof options === "object"
                  ? options
                  : {},
                { silent: true }
              );

              trace("WEB_NOTIFICATION_SILENCED");

              if (!pending.handled) {
                audelyConsumePendingMessage(
                  "WebNotification"
                );
              }
            }
          } catch (error) {
            trace(
              "WEB_NOTIFICATION_PATCH_ERROR " +
              String(error)
            );
          }

          return new NativeNotification(
            title,
            nextOptions
          );
        };

        try {
          Object.setPrototypeOf(
            AudelyNotification,
            NativeNotification
          );
        } catch {}

        try {
          AudelyNotification.prototype =
            NativeNotification.prototype;
        } catch {}

        AudelyNotification.__AUDELY_NOTIFICATION_WRAPPED = true;
        AudelyNotification.__AUDELY_NATIVE =
          NativeNotification;

        try {
          globalThis.Notification = AudelyNotification;
        } catch {}

        if (globalThis.Notification !== AudelyNotification) {
          try {
            Object.defineProperty(
              globalThis,
              "Notification",
              {
                configurable: true,
                writable: true,
                value: AudelyNotification
              }
            );
          } catch {}
        }

        if (globalThis.Notification !== AudelyNotification) {
          return false;
        }

        globalThis.__AUDELY_NOTIFICATION_PATCHED = true;
        trace("WEB_NOTIFICATION_PATCHED");
        return true;
      } catch (error) {
        trace(
          "WEB_NOTIFICATION_INSTALL_ERROR " +
          String(error)
        );
        return false;
      }
    };

    const audelyFindWebAudioSoundClass = webpackRequire => {
      if (!webpackRequire || !webpackRequire.c) return null;

      let modules = [];
      try {
        modules = Object.entries(webpackRequire.c);
      } catch {}

      for (const [moduleId, moduleValue] of modules) {
        let exportsValue = null;
        try { exportsValue = moduleValue && moduleValue.exports; } catch {}
        if (!exportsValue) continue;

        for (
          const candidate of audelyExportObjects(
            exportsValue,
            4
          )
        ) {
          if (typeof candidate !== "function") continue;

          let protoValue = null;
          try { protoValue = candidate.prototype; } catch {}
          if (!protoValue) continue;

          if (
            typeof protoValue.play === "function" &&
            typeof protoValue.ensureAudio === "function"
          ) {
            return {
              cls: candidate,
              moduleId: moduleId
            };
          }
        }
      }

      return null;
    };

    const audelyPatchWebAudioSound = webpackRequire => {
      try {
        if (globalThis.__AUDELY_WEBAUDIO_PATCHED) {
          return true;
        }

        const found =
          audelyFindWebAudioSoundClass(webpackRequire);

        if (!found || !found.cls) return false;

        const protoValue = found.cls.prototype;
        const patched =
          audelyPatchSoundPrototype(protoValue) ||
          Boolean(
            protoValue &&
            protoValue.__AUDELY_MESSAGE_SOUND_PATCHED
          );

        if (patched) {
          globalThis.__AUDELY_WEBAUDIO_PATCHED = true;
          trace(
            "DISCORD_WEBAUDIO_PATCHED source=cache module=" +
            String(found.moduleId)
          );
          return true;
        }
      } catch (error) {
        trace("WEBAUDIO_PATCH_ERROR " + String(error));
      }

      return false;
    };

    const audelyPatchSoundPrototype = protoValue => {
      if (!protoValue || protoValue.__AUDELY_MESSAGE_SOUND_PATCHED) return false;

      const methods = ["play", "loop", "playWithListener"];
      if (!methods.some(name => typeof protoValue[name] === "function")) {
        return false;
      }

      let patchedAny = false;

      for (const methodName of methods) {
        const original = protoValue[methodName];
        if (typeof original !== "function") continue;
        if (original.__AUDELY_WRAPPED) continue;

        const wrapped = function(...args) {
          const type = String(this && this.name || "");
          const targetId = audelyMessageTypeToTarget(type);

          if (
            targetId &&
            globalThis.__AUDELY_SAFE_SOUNDS &&
            globalThis.__AUDELY_SAFE_SOUNDS[targetId]
          ) {
            trace(
              "SOUNDUTILS_HIT type=" + type +
              " method=" + methodName +
              " target=" + targetId
            );

            if (methodName === "playWithListener") {
              return audelyPlayDirect(
                targetId,
                "SoundUtils:" + type,
                false,
                args.length ? args[0] : undefined
              );
            }

            audelyPlayDirect(
              targetId,
              "SoundUtils:" + type,
              methodName === "loop",
              null
            );
            return;
          }

          return original.apply(this, args);
        };

        wrapped.__AUDELY_WRAPPED = true;

        try {
          protoValue[methodName] = wrapped;
          patchedAny = true;
        } catch {}
      }

      if (patchedAny) {
        try { protoValue.__AUDELY_MESSAGE_SOUND_PATCHED = true; } catch {}
      }

      return patchedAny;
    };

    const audelyPatchCreateSoundOwner = (owner, key) => {
      try {
        if (!owner || !key) return false;

        const originalCreateSound = owner[key];
        if (typeof originalCreateSound !== "function") return false;

        if (originalCreateSound.__AUDELY_CREATE_SOUND_WRAPPED) {
          globalThis.__AUDELY_SOUNDUTILS_PATCHED = true;
          return true;
        }

        // Patch a created Sound instance immediately, then wrap createSound so
        // all future instances use the same patched prototype.
        let sample = null;
        try { sample = originalCreateSound.call(owner, "message1"); } catch {}
        if (!sample) {
          try { sample = originalCreateSound.call(owner, "call_calling"); } catch {}
        }

        const sampleProto =
          sample &&
          sample.constructor &&
          sample.constructor.prototype;

        if (sampleProto) {
          audelyPatchSoundPrototype(sampleProto);
        }

        const wrapped = function(...args) {
          const instance = originalCreateSound.apply(this, args);

          try {
            const protoValue =
              instance &&
              instance.constructor &&
              instance.constructor.prototype;

            if (protoValue) audelyPatchSoundPrototype(protoValue);
          } catch {}

          return instance;
        };

        wrapped.__AUDELY_CREATE_SOUND_WRAPPED = true;
        wrapped.__AUDELY_NATIVE = originalCreateSound;

        try {
          owner[key] = wrapped;
        } catch {
          return false;
        }

        globalThis.__AUDELY_SOUNDUTILS_PATCHED = true;
        trace("DISCORD_SOUNDUTILS_PATCHED key=" + String(key));
        return true;
      } catch (error) {
        trace("CREATE_SOUND_PATCH_ERROR " + String(error));
        return false;
      }
    };

    const audelyPatchPlaySoundOwner = (owner, key) => {
      try {
        if (!owner || !key) return false;

        const originalPlaySound = owner[key];
        if (typeof originalPlaySound !== "function") return false;

        if (originalPlaySound.__AUDELY_PLAY_SOUND_WRAPPED) return true;

        const wrapped = function(...args) {
          const type = args.length ? String(args[0] || "") : "";
          const targetId = audelyMessageTypeToTarget(type);

          if (
            targetId &&
            globalThis.__AUDELY_SAFE_SOUNDS &&
            globalThis.__AUDELY_SAFE_SOUNDS[targetId]
          ) {
            trace(
              "PLAYSOUND_HIT type=" + type +
              " target=" + targetId
            );

            audelyPlayDirect(
              targetId,
              "playSound:" + type,
              false,
              null
            );
            return;
          }

          return originalPlaySound.apply(this, args);
        };

        wrapped.__AUDELY_PLAY_SOUND_WRAPPED = true;
        wrapped.__AUDELY_NATIVE = originalPlaySound;

        try {
          owner[key] = wrapped;
          trace("DISCORD_PLAYSOUND_PATCHED key=" + String(key));
          return true;
        } catch {
          return false;
        }
      } catch (error) {
        trace("PLAY_SOUND_PATCH_ERROR " + String(error));
        return false;
      }
    };

    const audelyPatchSoundUtilsObject = objectValue => {
      try {
        if (!objectValue) return false;

        // Do not guess obfuscated createSound keys. V1.1.5 could mark an
        // unrelated "return new" function as SoundUtils (false positive).
        // We only patch a directly verified createSound export here.
        if (typeof objectValue.createSound !== "function") {
          return false;
        }

        if (
          audelyPatchCreateSoundOwner(
            objectValue,
            "createSound"
          )
        ) {
          if (typeof objectValue.playSound === "function") {
            audelyPatchPlaySoundOwner(
              objectValue,
              "playSound"
            );
          }
          return true;
        }
      } catch (error) {
        trace("SOUNDUTILS_PATCH_ERROR " + String(error));
      }

      return false;
    };

    const audelyPatchDesktopNotifications = objectValue => {
      try {
        if (!objectValue || typeof objectValue.showNotification !== "function") {
          return false;
        }

        const original = objectValue.showNotification;
        if (original.__AUDELY_DESKTOP_WRAPPED) {
          globalThis.__AUDELY_DESKTOP_NOTIFICATION_PATCHED = true;
          return true;
        }

        const wrapped = function(...args) {
          let targetId = null;
          let originalSound = null;

          try {
            for (const arg of args) {
              if (!arg || typeof arg !== "object") continue;
              if (typeof arg.sound !== "string") continue;

              const mapped = audelyMessageTypeToTarget(arg.sound);
              if (!mapped) continue;

              targetId = mapped;
              originalSound = arg.sound;

              if (
                globalThis.__AUDELY_SAFE_SOUNDS &&
                globalThis.__AUDELY_SAFE_SOUNDS[targetId]
              ) {
                // Disable Discord's own desktop notification sound, but keep
                // the visual notification itself.
                try { arg.sound = null; } catch {}
              }
              break;
            }

            if (
              targetId &&
              globalThis.__AUDELY_SAFE_SOUNDS &&
              globalThis.__AUDELY_SAFE_SOUNDS[targetId]
            ) {
              trace(
                "DESKTOP_NOTIFICATION_HIT sound=" +
                String(originalSound || "") +
                " target=" + targetId
              );

              if (
                !audelyConsumePendingMessage(
                  "DesktopNotification:" +
                  String(originalSound || "")
                )
              ) {
                audelyPlayDirect(
                  targetId,
                  "DesktopNotification:" +
                  String(originalSound || ""),
                  false,
                  null
                );
              }
            }
          } catch (error) {
            trace("DESKTOP_NOTIFICATION_ERROR " + String(error));
          }

          return original.apply(this, args);
        };

        wrapped.__AUDELY_DESKTOP_WRAPPED = true;

        try {
          objectValue.showNotification = wrapped;
        } catch {
          return false;
        }

        globalThis.__AUDELY_DESKTOP_NOTIFICATION_PATCHED = true;
        trace("DISCORD_DESKTOP_NOTIFICATION_PATCHED");
        return true;
      } catch (error) {
        trace("DESKTOP_PATCH_ERROR " + String(error));
        return false;
      }
    };

    const audelyPatchWebAudioExports = (
      exportsValue,
      moduleId
    ) => {
      try {
        if (globalThis.__AUDELY_WEBAUDIO_PATCHED) return true;

        for (
          const candidate of audelyExportObjects(
            exportsValue,
            4
          )
        ) {
          if (typeof candidate !== "function") continue;

          let protoValue = null;
          try { protoValue = candidate.prototype; } catch {}
          if (!protoValue) continue;

          if (
            typeof protoValue.play === "function" &&
            typeof protoValue.ensureAudio === "function"
          ) {
            const patched =
              audelyPatchSoundPrototype(protoValue) ||
              Boolean(
                protoValue.__AUDELY_MESSAGE_SOUND_PATCHED
              );

            if (patched) {
              globalThis.__AUDELY_WEBAUDIO_PATCHED = true;
              trace(
                "DISCORD_WEBAUDIO_PATCHED source=factory module=" +
                String(moduleId || "?")
              );
              return true;
            }
          }
        }
      } catch (error) {
        trace(
          "WEBAUDIO_EXPORT_PATCH_ERROR " +
          String(error)
        );
      }

      return false;
    };

    const audelyPatchDesktopExports = (
      exportsValue,
      moduleId
    ) => {
      try {
        if (
          globalThis.__AUDELY_DESKTOP_NOTIFICATION_PATCHED
        ) {
          return true;
        }

        for (
          const candidate of audelyExportObjects(
            exportsValue,
            4
          )
        ) {
          if (
            audelyHasProps(
              candidate,
              ["showNotification", "requestPermission"]
            )
          ) {
            if (audelyPatchDesktopNotifications(candidate)) {
              trace(
                "DISCORD_DESKTOP_PATCHED source=factory module=" +
                String(moduleId || "?")
              );
              return true;
            }
          }
        }
      } catch (error) {
        trace(
          "DESKTOP_EXPORT_PATCH_ERROR " +
          String(error)
        );
      }

      return false;
    };

    const audelyPatchSoundUtilsExports = (
      exportsValue,
      moduleId
    ) => {
      try {
        if (globalThis.__AUDELY_SOUNDUTILS_PATCHED) {
          return true;
        }

        for (
          const candidate of audelyExportObjects(
            exportsValue,
            4
          )
        ) {
          if (audelyPatchSoundUtilsObject(candidate)) {
            trace(
              "DISCORD_SOUNDUTILS_PATCHED source=factory module=" +
              String(moduleId || "?")
            );
            return true;
          }
        }
      } catch (error) {
        trace(
          "SOUNDUTILS_EXPORT_PATCH_ERROR " +
          String(error)
        );
      }

      return false;
    };

    const audelyFactoryKinds = factory => {
      const kinds = [];
      const source = audelyFunctionSource(factory);
      if (!source) return kinds;

      if (source.includes("could not play audio:")) {
        kinds.push("webaudio");
      }

      if (
        source.includes("showNotification") &&
        source.includes("requestPermission")
      ) {
        kinds.push("desktop");
      }

      if (
        source.includes(".disableSounds)") &&
        source.includes(".getSoundpack()") &&
        source.includes("Unable to find sound for pack name")
      ) {
        kinds.push("soundutils");
      }

      return kinds;
    };

    const audelyApplyPostFactoryPatch = (
      kinds,
      moduleValue,
      moduleId
    ) => {
      let exportsValue = null;
      try {
        exportsValue =
          moduleValue &&
          moduleValue.exports;
      } catch {}

      if (!exportsValue) return;

      if (kinds.includes("webaudio")) {
        audelyPatchWebAudioExports(
          exportsValue,
          moduleId
        );
      }

      if (kinds.includes("desktop")) {
        audelyPatchDesktopExports(
          exportsValue,
          moduleId
        );
      }

      if (kinds.includes("soundutils")) {
        audelyPatchSoundUtilsExports(
          exportsValue,
          moduleId
        );
      }
    };

    const audelyWrapFactory = (
      moduleId,
      factory,
      forcedKinds
    ) => {
      if (typeof factory !== "function") return factory;

      if (factory.__AUDELY_SAFE_FACTORY_WRAPPED) {
        return factory;
      }

      const kinds =
        Array.isArray(forcedKinds) && forcedKinds.length
          ? forcedKinds
          : audelyFactoryKinds(factory);

      if (!kinds.length) return factory;

      const original = factory;

      const wrapped = function(...args) {
        const result = original.apply(this, args);

        try {
          audelyApplyPostFactoryPatch(
            kinds,
            args[0],
            moduleId
          );
        } catch (error) {
          trace(
            "FACTORY_POSTPATCH_ERROR id=" +
            String(moduleId) +
            " error=" +
            String(error)
          );
        }

        return result;
      };

      try {
        for (
          const key of Object.getOwnPropertyNames(original)
        ) {
          if (
            key === "length" ||
            key === "name" ||
            key === "prototype"
          ) {
            continue;
          }

          try {
            const descriptor =
              Object.getOwnPropertyDescriptor(
                original,
                key
              );

            if (descriptor) {
              Object.defineProperty(
                wrapped,
                key,
                descriptor
              );
            }
          } catch {}
        }
      } catch {}

      wrapped.__AUDELY_SAFE_FACTORY_WRAPPED = true;
      wrapped.__AUDELY_SAFE_FACTORY_KINDS =
        kinds.join(",");

      trace(
        "FACTORY_WRAPPED id=" +
        String(moduleId) +
        " kinds=" +
        kinds.join(",")
      );

      return wrapped;
    };

    const audelyPatchLoadedModuleCache = webpackRequire => {
      if (!webpackRequire || !webpackRequire.c) return;

      let entries = [];
      try {
        entries = Object.entries(webpackRequire.c);
      } catch {}

      for (const [moduleId, moduleValue] of entries) {
        let exportsValue = null;
        try {
          exportsValue =
            moduleValue &&
            moduleValue.exports;
        } catch {}

        if (!exportsValue) continue;

        if (!globalThis.__AUDELY_WEBAUDIO_PATCHED) {
          audelyPatchWebAudioExports(
            exportsValue,
            moduleId
          );
        }

        if (
          !globalThis.__AUDELY_DESKTOP_NOTIFICATION_PATCHED
        ) {
          audelyPatchDesktopExports(
            exportsValue,
            moduleId
          );
        }

        if (!globalThis.__AUDELY_SOUNDUTILS_PATCHED) {
          audelyPatchSoundUtilsExports(
            exportsValue,
            moduleId
          );
        }

        if (
          globalThis.__AUDELY_WEBAUDIO_PATCHED &&
          globalThis.__AUDELY_DESKTOP_NOTIFICATION_PATCHED &&
          globalThis.__AUDELY_SOUNDUTILS_PATCHED
        ) {
          break;
        }
      }
    };

    const audelyInstallFactoryObservers = webpackRequire => {
      if (!webpackRequire) return;

      // Always inspect already-loaded modules. This does not execute anything.
      audelyPatchLoadedModuleCache(webpackRequire);

      // Existing but not-yet-executed factories are wrapped ONCE. Discord
      // remains responsible for deciding when they are executed.
      if (
        webpackRequire.m &&
        !globalThis.__AUDELY_FACTORY_TABLE_WRAPPED
      ) {
        let entries = [];
        try {
          entries = Object.entries(webpackRequire.m);
        } catch {}

        let matched = 0;

        for (const [moduleId, factory] of entries) {
          const wrapped =
            audelyWrapFactory(
              moduleId,
              factory
            );

          if (wrapped !== factory) {
            try {
              webpackRequire.m[moduleId] = wrapped;
              matched++;
            } catch {}
          }
        }

        globalThis.__AUDELY_FACTORY_TABLE_WRAPPED = true;

        trace(
          "FACTORY_TABLE_READY matched=" +
          String(matched) +
          " forcedRequires=0"
        );
      }
    };

    const audelyInstallDiscordSoundLayer = () => {
      const webpackRequire =
        audelyGetWebpackRequire();

      if (!webpackRequire) {
        trace(
          "DISCORD_LAYER_WAITING webpack-require-unavailable"
        );

        return {
          soundUtils: Boolean(
            globalThis.__AUDELY_SOUNDUTILS_PATCHED
          ),
          desktop: Boolean(
            globalThis.__AUDELY_DESKTOP_NOTIFICATION_PATCHED
          ),
          dispatcher: false,
          webAudio: Boolean(
            globalThis.__AUDELY_WEBAUDIO_PATCHED
          ),
          notification: false
        };
      }

      audelyInstallFactoryObservers(
        webpackRequire
      );

      const status = {
        soundUtils: Boolean(
          globalThis.__AUDELY_SOUNDUTILS_PATCHED
        ),
        desktop: Boolean(
          globalThis.__AUDELY_DESKTOP_NOTIFICATION_PATCHED
        ),
        dispatcher: false,
        webAudio: Boolean(
          globalThis.__AUDELY_WEBAUDIO_PATCHED
        ),
        notification: false
      };

      trace(
        "DISCORD_LAYER_STATUS lazyFactorySafe=true" +
        " soundUtils=" + status.soundUtils +
        " desktop=" + status.desktop +
        " webAudio=" + status.webAudio +
        " factoryRequires=0" +
        " factories=" + Boolean(webpackRequire.m) +
        " cache=" + Boolean(webpackRequire.c)
      );

      return status;
    };

    globalThis.__AUDELY_INSTALL_DISCORD_LAYER = audelyInstallDiscordSoundLayer;

    // Patch only factories from newly arriving chunks, before Discord registers
    // them. We do not execute those modules and we do not run a full rescan.
    try {
      const chunks =
        globalThis.webpackChunkdiscord_app;

      if (
        chunks &&
        typeof chunks.push === "function" &&
        !chunks.__AUDELY_PUSH_WRAPPED
      ) {
        const nativeChunkPush =
          chunks.push.bind(chunks);

        const wrappedChunkPush = function(...args) {
          try {
            const chunk = args[0];
            const modules =
              Array.isArray(chunk) &&
              chunk.length > 1 &&
              chunk[1] &&
              typeof chunk[1] === "object"
                ? chunk[1]
                : null;

            if (modules) {
              for (
                const [moduleId, factory]
                of Object.entries(modules)
              ) {
                const wrapped =
                  audelyWrapFactory(
                    moduleId,
                    factory
                  );

                if (wrapped !== factory) {
                  try {
                    modules[moduleId] = wrapped;
                  } catch {}
                }
              }
            }
          } catch (error) {
            trace(
              "CHUNK_FACTORY_WRAP_ERROR " +
              String(error)
            );
          }

          return nativeChunkPush(...args);
        };

        try {
          wrappedChunkPush.__AUDELY_NATIVE =
            nativeChunkPush;
        } catch {}

        chunks.push = wrappedChunkPush;
        chunks.__AUDELY_PUSH_WRAPPED = true;

        trace(
          "WEBPACK_CHUNK_OBSERVER_READY forcedRequires=0"
        );
      }
    } catch {}

    const initialDiscordLayer = audelyInstallDiscordSoundLayer();

    if (globalThis.__AUDELY_SAFE_PATCHED) {
      trace(
        "MAP_UPDATED targets=" +
        Object.keys(globalThis.__AUDELY_SAFE_SOUNDS).join(",") +
        " needles=" +
        Object.keys(globalThis.__AUDELY_SAFE_NEEDLES).length
      );
      return (
        "Audely map updated; soundUtils=" +
        Boolean(initialDiscordLayer && initialDiscordLayer.soundUtils) +
        "; desktopNotifications=" +
        Boolean(initialDiscordLayer && initialDiscordLayer.desktop) +
        "; dispatcher=" +
        Boolean(initialDiscordLayer && initialDiscordLayer.dispatcher) +
        "; webAudio=" +
        Boolean(initialDiscordLayer && initialDiscordLayer.webAudio) +
        "; notification=" +
        Boolean(initialDiscordLayer && initialDiscordLayer.notification)
      );
    }

    const proto = globalThis.HTMLMediaElement && globalThis.HTMLMediaElement.prototype;
    if (!proto) {
      trace("ERROR HTMLMediaElement unavailable");
      return "Audely: HTMLMediaElement unavailable";
    }

    const descriptor = Object.getOwnPropertyDescriptor(proto, "src");
    const nativeSetAttribute = proto.setAttribute;
    const nativePlay = proto.play;

    if (!descriptor || typeof descriptor.set !== "function" || typeof descriptor.get !== "function") {
      trace("ERROR src descriptor unavailable");
      return "Audely: src descriptor unavailable";
    }

    const makeBlobUrl = sound => {
      const id = String(sound.target || "");
      const sha = String(sound.sha256 || "");
      const cached = globalThis.__AUDELY_BLOB_CACHE[id];

      if (cached && cached.sha256 === sha && cached.url) {
        return cached.url;
      }

      const binary = atob(String(sound.b64 || ""));
      const bytes = new Uint8Array(binary.length);
      for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);

      const blob = new Blob([bytes], { type: String(sound.mime || "audio/mpeg") });
      const url = URL.createObjectURL(blob);

      globalThis.__AUDELY_BLOB_CACHE[id] = { sha256: sha, url };
      return url;
    };

    const findMatch = value => {
      if (typeof value !== "string") return null;
      const lower = value.toLowerCase();

      for (const [needle, targetId] of Object.entries(globalThis.__AUDELY_SAFE_NEEDLES || {})) {
        if (!lower.includes(needle)) continue;

        const sound = globalThis.__AUDELY_SAFE_SOUNDS[targetId];
        if (sound) return { needle, sound };
      }
      return null;
    };

    const looksLikeSound = value => {
      if (typeof value !== "string") return false;
      const lower = value.toLowerCase();
      return lower.includes(".mp3") || lower.includes(".wav") ||
             lower.includes(".ogg") || lower.includes("/assets/");
    };

    const clearActive = element => {
      try { delete element.__AUDELY_SAFE_ACTIVE; } catch {}
    };

    const armFallback = (element, originalSrc, customSrc, sound, needle) => {
      const previousVolume = Number.isFinite(element.volume) ? element.volume : 1;
      const token = Math.random().toString(36).slice(2);

      element.__AUDELY_SAFE_ACTIVE = {
        token,
        originalSrc,
        customSrc,
        previousVolume,
        target: String(sound.target || ""),
        needle,
        playRequested: false,
        fallingBack: false,
        customReady: false,
        customPlaying: false,
        customEnded: false
      };

      const getActive = () => {
        const active = element.__AUDELY_SAFE_ACTIVE;
        return active && active.token === token ? active : null;
      };

      const applyVolume = () => {
        try {
          const active = getActive();
          if (active && !active.fallingBack) {
            element.volume = Math.max(0, Math.min(1, Number(sound.volume)));
          }
        } catch {}
      };

      const ready = () => {
        const active = getActive();
        if (!active || active.fallingBack) return;

        if (!active.customReady) {
          active.customReady = true;
          trace(
            "CUSTOM_READY target=" + active.target +
            " needle=" + needle +
            " volume=" + Math.round(Number(sound.volume) * 100)
          );
        }
        applyVolume();
      };

      const fallback = () => {
        const active = getActive();
        if (!active || active.fallingBack) return;

        if (active.customPlaying || active.customEnded) {
          trace(
            "LATE_ERROR_IGNORED target=" + active.target +
            " needle=" + needle
          );
          try { element.removeEventListener("error", fallback); } catch {}
          return;
        }

        active.fallingBack = true;
        const shouldReplay = active.playRequested === true;

        trace(
          "FALLBACK target=" + active.target +
          " needle=" + needle +
          " replay=" + shouldReplay
        );

        try { element.volume = active.previousVolume; } catch {}

        try {
          descriptor.set.call(element, active.originalSrc);
        } catch {
          try {
            if (typeof nativeSetAttribute === "function") {
              nativeSetAttribute.call(element, "src", active.originalSrc);
            }
          } catch {}
        }

        if (shouldReplay && typeof nativePlay === "function") {
          try {
            const p = nativePlay.call(element);
            if (p && typeof p.catch === "function") p.catch(() => {});
          } catch {}
        }

        setTimeout(() => clearActive(element), 0);
      };

      const playing = () => {
        const active = getActive();
        if (!active || active.fallingBack) return;

        active.customReady = true;
        active.customPlaying = true;
        applyVolume();

        try { element.removeEventListener("error", fallback); } catch {}

        trace(
          "CUSTOM_PLAYING target=" + active.target +
          " needle=" + needle
        );
      };

      const ended = () => {
        const active = getActive();
        if (!active) return;

        active.customEnded = true;
        try { element.removeEventListener("error", fallback); } catch {}

        trace(
          "CUSTOM_ENDED target=" + active.target +
          " needle=" + needle
        );

        try { element.volume = active.previousVolume; } catch {}
        clearActive(element);
      };

      element.addEventListener("error", fallback);
      element.addEventListener("loadeddata", ready, { once: true });
      element.addEventListener("canplay", ready, { once: true });
      element.addEventListener("playing", playing, { once: true });
      element.addEventListener("ended", ended, { once: true });

      applyVolume();
    };

    const replaceForElement = (element, value) => {
      try {
        const match = findMatch(value);

        if (!match) {
          if (looksLikeSound(value)) {
            const rawMiss = String(value);
            const cleanMiss = rawMiss.split("?")[0].split("#")[0];
            const baseMiss = cleanMiss.split("/").pop() || cleanMiss;
            trace("MISS src=" + baseMiss.slice(0, 220));
          }
          return value;
        }

        let blobUrl;
        try {
          blobUrl = makeBlobUrl(match.sound);
        } catch (error) {
          trace(
            "BLOB_ERROR target=" + String(match.sound.target || "") +
            " error=" + String(error)
          );
          return value;
        }

        armFallback(element, value, blobUrl, match.sound, match.needle);

        trace(
          "HIT target=" + String(match.sound.target || "") +
          " needle=" + match.needle +
          " transport=blob" +
          " volume=" + Math.round(Number(match.sound.volume) * 100)
        );

        return blobUrl;
      } catch (error) {
        trace("replace error=" + String(error));
        return value;
      }
    };

    Object.defineProperty(proto, "src", {
      configurable: descriptor.configurable,
      enumerable: descriptor.enumerable,
      get: descriptor.get,
      set(value) {
        return descriptor.set.call(this, replaceForElement(this, value));
      }
    });

    if (typeof nativeSetAttribute === "function") {
      proto.setAttribute = function(name, value) {
        if (String(name).toLowerCase() === "src") {
          value = replaceForElement(this, value);
        }
        return nativeSetAttribute.call(this, name, value);
      };
    }

    const getExistingMediaSrc = element => {
      try {
        const direct = descriptor.get.call(element);
        if (typeof direct === "string" && direct.length > 0) return direct;
      } catch {}

      try {
        const current = element.currentSrc;
        if (typeof current === "string" && current.length > 0) return current;
      } catch {}

      try {
        const attr = element.getAttribute && element.getAttribute("src");
        if (typeof attr === "string" && attr.length > 0) return attr;
      } catch {}

      try {
        const source = element.querySelector && element.querySelector("source[src]");
        if (source) {
          const src = source.src || source.getAttribute("src");
          if (typeof src === "string" && src.length > 0) return src;
        }
      } catch {}

      return "";
    };

    // V1.3.7:
    // Discord peut creer/precharger un Audio(src) AVANT que le setter src
    // d'Audely soit installe. Dans ce cas aucune affectation src ne repasse
    // par notre hook : seul play() arrive plus tard.
    //
    // On verifie donc aussi la source juste avant la lecture. Cela couvre les
    // elements audio deja precharges sans remplacer globalThis.Audio.
    if (typeof nativePlay === "function") {
      proto.play = function(...args) {
        try {
          let active = this.__AUDELY_SAFE_ACTIVE;

          if (!active || active.fallingBack) {
            const existingSrc = getExistingMediaSrc(this);

            if (existingSrc) {
              const match = findMatch(existingSrc);

              if (match) {
                const replacement = replaceForElement(this, existingSrc);

                if (replacement !== existingSrc) {
                  try {
                    descriptor.set.call(this, replacement);
                  } catch {
                    try {
                      if (typeof nativeSetAttribute === "function") {
                        nativeSetAttribute.call(this, "src", replacement);
                      }
                    } catch {}
                  }

                  trace(
                    "PLAYTIME_HIT target=" + String(match.sound.target || "") +
                    " needle=" + match.needle +
                    " source=preloaded"
                  );

                  active = this.__AUDELY_SAFE_ACTIVE;
                }
              }
              else if (looksLikeSound(existingSrc)) {
                const clean = String(existingSrc).split("?")[0].split("#")[0];
                const base = clean.split("/").pop() || clean;
                trace("PLAYTIME_MISS src=" + base.slice(0, 220));
              }
            }
          }

          active = this.__AUDELY_SAFE_ACTIVE;
          if (active && !active.fallingBack) {
            if (!active.playRequested) {
              active.playRequested = true;
              trace(
                "PLAY_REQUESTED target=" + active.target +
                " needle=" + active.needle
              );
            }
          }
        } catch (error) {
          trace("PLAYTIME_ERROR " + String(error));
        }

        return nativePlay.apply(this, args);
      };
    }

    globalThis.__AUDELY_SAFE_PATCHED = true;

    const finalDiscordLayer = audelyInstallDiscordSoundLayer();

    trace(
      "HOOK_ACTIVE safe=true transport=blob globalAudio=false targets=" +
      Object.keys(globalThis.__AUDELY_SAFE_SOUNDS).join(",") +
      " needles=" +
      Object.keys(globalThis.__AUDELY_SAFE_NEEDLES).length +
      " soundUtils=" +
      Boolean(finalDiscordLayer && finalDiscordLayer.soundUtils) +
      " desktopNotifications=" +
      Boolean(finalDiscordLayer && finalDiscordLayer.desktop) +
      " dispatcher=" +
      Boolean(finalDiscordLayer && finalDiscordLayer.dispatcher) +
      " webAudio=" +
      Boolean(finalDiscordLayer && finalDiscordLayer.webAudio) +
      " notification=" +
      Boolean(finalDiscordLayer && finalDiscordLayer.notification)
    );

    return (
      "Audely safe blob audio hook active; soundUtils=" +
      Boolean(finalDiscordLayer && finalDiscordLayer.soundUtils) +
      "; desktopNotifications=" +
      Boolean(finalDiscordLayer && finalDiscordLayer.desktop) +
      "; dispatcher=" +
      Boolean(finalDiscordLayer && finalDiscordLayer.dispatcher) +
      "; webAudio=" +
      Boolean(finalDiscordLayer && finalDiscordLayer.webAudio) +
      "; notification=" +
      Boolean(finalDiscordLayer && finalDiscordLayer.notification)
    );
  })();`;
}

async function inject(webContents) {
  try {
    if (!webContents || webContents.isDestroyed()) return;
    const payload = readPayload();
    const result = await webContents.executeJavaScript(buildRendererCode(payload), true);
    log(
      `inject ok; loaderVersion=${LOADER_VERSION}; ` +
      `targets=${Object.keys(payload.sounds).join(",") || "<none>"}; ` +
      `needles=${Object.keys(payload.needles).length}; result=${String(result)}`
    );
  } catch (error) {
    log(`inject failed: ${error?.message || error}`);
  }
}

function getContentsType(wc) {
  try {
    return typeof wc.getType === "function" ? String(wc.getType()) : "unknown";
  } catch {
    return "unknown";
  }
}

function getContentsUrl(wc) {
  try {
    return typeof wc.getURL === "function" ? String(wc.getURL()).slice(0, 180) : "";
  } catch {
    return "";
  }
}

// V1.3.7 GAME MODE:
// Discord peut créer d'autres WebContents quand un jeu / l'overlay est actif.
// Avant, Audely ne suivait que les BrowserWindow. On attache désormais le hook
// à TOUS les WebContents Electron existants et futurs.
function scheduleInject(wc, delay = 250) {
  try {
    if (!wc || wc.isDestroyed()) return;

    if (wc.__AUDELY_SAFE_INJECT_TIMER) {
      try {
        clearTimeout(
          wc.__AUDELY_SAFE_INJECT_TIMER
        );
      } catch {}
    }

    wc.__AUDELY_SAFE_INJECT_TIMER =
      setTimeout(() => {
        try {
          wc.__AUDELY_SAFE_INJECT_TIMER = null;
          inject(wc);
        } catch {}
      }, delay);
  } catch {}
}

function attachContents(wc) {
  try {
    if (!wc || wc.isDestroyed()) return;

    if (!wc.__AUDELY_SAFE_ATTACHED) {
      wc.__AUDELY_SAFE_ATTACHED = true;

      log(
        `attach webContents id=${String(wc.id || "?")} ` +
        `type=${getContentsType(wc)} url=${getContentsUrl(wc) || "<empty>"}`
      );

      wc.on("console-message", (...args) => {
        try {
          const strings =
            args.filter(
              v => typeof v === "string"
            );

          const message =
            strings.find(
              v => v.includes("[AudelySafe]")
            );

          if (message) {
            log(
              `renderer id=${String(wc.id || "?")} ` +
              `type=${getContentsType(wc)} ${message}`
            );
          }
        } catch {}
      });

      wc.on("dom-ready", () => {
        log(
          `dom-ready id=${String(wc.id || "?")} ` +
          `type=${getContentsType(wc)} url=${getContentsUrl(wc) || "<empty>"}`
        );

        scheduleInject(wc, 180);
      });

      wc.on("did-finish-load", () => {
        log(
          `did-finish-load id=${String(wc.id || "?")} ` +
          `type=${getContentsType(wc)} url=${getContentsUrl(wc) || "<empty>"}`
        );

        scheduleInject(wc, 320);
      });

      wc.on("did-navigate", () => {
        scheduleInject(wc, 350);
      });

      wc.on("did-navigate-in-page", () => {
        scheduleInject(wc, 350);
      });
    }

    // Single startup attempt instead of 120/700/1800 ms bursts.
    scheduleInject(wc, 450);
  } catch (error) {
    log(
      `webContents attach failed: ${error?.message || error}`
    );
  }
}

function attachWindow(win) {
  try {
    if (!win || win.isDestroyed()) return;
    attachContents(win.webContents);
  } catch (error) {
    log(`window attach failed: ${error?.message || error}`);
  }
}

function injectAllContents() {
  let count = 0;

  try {
    if (webContents && typeof webContents.getAllWebContents === "function") {
      for (const wc of webContents.getAllWebContents()) {
        try {
          attachContents(wc);
          scheduleInject(wc, 120);
          count++;
        } catch {}
      }
    }
  } catch (error) {
    log(`getAllWebContents failed: ${error?.message || error}`);
  }

  // Fallback pour les versions Electron où getAllWebContents serait indisponible.
  if (count === 0) {
    try {
      for (const win of BrowserWindow.getAllWindows()) {
        try {
          attachWindow(win);
          scheduleInject(win.webContents, 120);
          count++;
        } catch {}
      }
    } catch {}
  }

  return count;
}

app.on("web-contents-created", (_event, wc) => {
  try {
    log(
      `web-contents-created id=${String(wc?.id || "?")} ` +
      `type=${getContentsType(wc)}`
    );
    attachContents(wc);
  } catch {}
});

app.on("browser-window-created", (_event, win) => attachWindow(win));

app.whenReady().then(() => {
  const count = injectAllContents();
  log(`initial all-webcontents scan count=${count}`);
});

fs.watchFile(CONFIG, { interval: 1200, persistent: false }, () => {
  const count = injectAllContents();
  log(`config updated; reinjected webContents=${count}`);
});

log(
  "safe blob runtime loader registered; " +
  "gameMode=webcontents-events; " +
  "periodicScan=false; factoryRequires=0"
);