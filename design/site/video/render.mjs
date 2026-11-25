// Renders composition.html to pigeon-demo.mp4, frame by frame. Nothing is recorded in real time:
// for every frame the page's animations are paused and set to that frame's time, then Chrome takes a screenshot.
//
//   node render.mjs                     full render: pigeon-demo.mp4, pigeon-demo-poster.png, stills/*.png
//   node render.mjs --only 2,7,15.2     only capture those times (seconds) as PNGs into --outdir (default: preview/)
//
// Options: --work <dir>   where the temporary Chrome profile goes (default: the system temp folder)
//          --crf <n>      x264 quality, lower is better (default 16)
// Needs: Node 22 or later (built-in WebSocket), Chrome, ffmpeg on PATH, and the network (Google Fonts).
// Set CHROME to the browser's path if it is not in the default place. No packages to install.
import { spawn, spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdtempSync, mkdirSync, rmSync, writeFileSync, statSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const CSS_W = 1200, CSS_H = 675, SCALE = 1.6;      // 1920 x 1080 device pixels
const OUT_W = 1920, OUT_H = 1080, FPS = 30;
const POSTER_AT = 10.8;                             // both windows, both messages, "Nobody typed."
const STILLS = [["01-at-02s", 2], ["02-at-07s", 7], ["03-at-11s", 11], ["04-at-15s", 15.2], ["05-end-card", 22]];
const RECHECK = [3.5, 1.2, 8.5, 12.3, 19.8];        // re-captured out of order after the render; must match exactly

const args = process.argv.slice(2);
const opt = (name, def) => { const i = args.indexOf("--" + name); return i < 0 ? def : args[i + 1]; };
const only = opt("only", null);
const crf = opt("crf", "16");
const outDir = resolve(opt("outdir", join(HERE, "preview")));
const workRoot = resolve(opt("work", tmpdir()));
const chromePath = process.env.CHROME ||
  (process.platform === "win32" ? "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe"
    : process.platform === "darwin" ? "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" : "google-chrome");

mkdirSync(workRoot, { recursive: true });
const profile = mkdtempSync(join(workRoot, "chrome-profile-"));
let chrome = null, ws = null, ffmpeg = null;

function killChrome() {
  if (!chrome || chrome.exitCode !== null) return;
  if (process.platform === "win32") spawnSync("taskkill", ["/pid", String(chrome.pid), "/T", "/F"], { stdio: "ignore" });
  else chrome.kill("SIGKILL");
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function startChrome() {
  chrome = spawn(chromePath, [
    "--headless=new", "--remote-debugging-port=0", `--user-data-dir=${profile}`,
    "--no-first-run", "--no-default-browser-check", "--disable-extensions", "--mute-audio",
    "--hide-scrollbars", "--force-color-profile=srgb", "--disable-lcd-text", "--force-device-scale-factor=1",
    `--window-size=${CSS_W},${CSS_H}`, "about:blank",
  ], { stdio: ["ignore", "ignore", "pipe"] });
  const url = await new Promise((ok, fail) => {
    let buf = "";
    const timer = setTimeout(() => fail(new Error("Chrome did not report its DevTools address:\n" + buf)), 30000);
    chrome.stderr.on("data", (d) => {
      buf += d;
      const m = buf.match(/DevTools listening on (ws:\/\/\S+)/);
      if (m) { clearTimeout(timer); ok(m[1]); }
    });
    chrome.on("exit", (c) => { clearTimeout(timer); fail(new Error("Chrome exited early (" + c + "):\n" + buf)); });
    chrome.on("error", fail);
  });
  ws = new WebSocket(url);
  await new Promise((ok, fail) => { ws.onopen = ok; ws.onerror = () => fail(new Error("cannot connect to Chrome")); });
}

// Minimal Chrome DevTools Protocol client
let nextId = 0;
const pending = new Map(), waiters = [];
function listen() {
  ws.onmessage = (ev) => {
    const msg = JSON.parse(ev.data);
    if (msg.id && pending.has(msg.id)) {
      const { ok, fail, method } = pending.get(msg.id);
      pending.delete(msg.id);
      if (msg.error) fail(new Error(method + ": " + msg.error.message)); else ok(msg.result);
    } else if (msg.method) {
      for (let i = waiters.length - 1; i >= 0; i--) if (waiters[i].method === msg.method) waiters.splice(i, 1)[0].ok(msg.params);
    }
  };
}
function send(method, params = {}, sessionId) {
  const id = ++nextId;
  return new Promise((ok, fail) => {
    pending.set(id, { ok, fail, method });
    ws.send(JSON.stringify({ id, method, params, sessionId }));
  });
}
const event = (method) => new Promise((ok) => waiters.push({ method, ok }));

async function evaluate(session, expression) {
  const r = await send("Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true }, session);
  if (r.exceptionDetails) throw new Error("page error: " + (r.exceptionDetails.exception?.description || r.exceptionDetails.text));
  return r.result.value;
}
async function capture(session, t) {
  await evaluate(session, `window.__seek(${t})`);
  const r = await send("Page.captureScreenshot", { format: "png", captureBeyondViewport: false }, session);
  return Buffer.from(r.data, "base64");
}
const pngSize = (b) => [b.readUInt32BE(16), b.readUInt32BE(20)];
const sha = (b) => createHash("sha1").update(b).digest("hex");

function run(cmd, a) {
  const r = spawnSync(cmd, a, { encoding: "utf8" });
  if (r.status !== 0) throw new Error(cmd + " failed:\n" + r.stderr);
  return r.stdout;
}

async function main() {
  await startChrome();
  listen();
  const { targetId } = await send("Target.createTarget", { url: "about:blank" });
  const { sessionId: s } = await send("Target.attachToTarget", { targetId, flatten: true });
  await send("Emulation.setDeviceMetricsOverride", { width: CSS_W, height: CSS_H, deviceScaleFactor: SCALE, mobile: false }, s);
  await send("Emulation.setEmulatedMedia", { features: [
    { name: "prefers-color-scheme", value: "dark" }, { name: "prefers-reduced-motion", value: "no-preference" }] }, s);
  await send("Page.enable", {}, s);
  const loaded = event("Page.loadEventFired");
  await send("Page.navigate", { url: pathToFileURL(join(HERE, "composition.html")).href + "?render" }, s);
  await loaded;
  const info = await evaluate(s, "window.__ready");
  console.log("page ready:", JSON.stringify(info));
  if (info.fontsMissing.length) throw new Error("web fonts did not load (network?): " + info.fontsMissing.join(", "));
  if (!info.logo) throw new Error("the logo image did not load");
  if (!info.hookAnimations) throw new Error("the hook's CSS animations were not found");
  const duration = await evaluate(s, "window.__duration");

  if (only) {
    mkdirSync(outDir, { recursive: true });
    for (const t of only.split(",").map(Number)) {
      const png = await capture(s, t);
      const f = join(outDir, `t${t.toFixed(2).padStart(5, "0")}.png`);
      writeFileSync(f, png);
      console.log(f, pngSize(png).join("x"));
    }
    return;
  }

  const total = Math.round(duration * FPS);
  const mp4 = join(HERE, "pigeon-demo.mp4");
  ffmpeg = spawn("ffmpeg", [
    "-hide_banner", "-loglevel", "error", "-y",
    "-f", "image2pipe", "-framerate", String(FPS), "-c:v", "png", "-i", "-",
    "-vf", "scale=in_range=pc:out_range=tv:out_color_matrix=bt709,format=yuv420p",
    "-c:v", "libx264", "-preset", "slow", "-crf", String(crf), "-profile:v", "high", "-pix_fmt", "yuv420p",
    "-r", String(FPS), "-g", String(FPS * 2),
    "-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709", "-color_range", "tv",
    "-movflags", "+faststart", "-an", mp4,
  ], { stdio: ["pipe", "inherit", "inherit"] });
  const encoded = new Promise((ok, fail) => {
    ffmpeg.on("exit", (c) => (c === 0 ? ok() : fail(new Error("ffmpeg exited with " + c))));
    ffmpeg.on("error", fail);
  });

  const hashes = [];
  const t0 = Date.now();
  for (let i = 0; i < total; i++) {
    const png = await capture(s, i / FPS);
    if (i === 0) {
      const [w, h] = pngSize(png);
      if (w !== OUT_W || h !== OUT_H) throw new Error(`frame is ${w}x${h}, expected ${OUT_W}x${OUT_H}`);
    }
    hashes.push(sha(png));
    if (i === Math.round(POSTER_AT * FPS)) writeFileSync(join(HERE, "pigeon-demo-poster.png"), png);
    if (!ffmpeg.stdin.write(png)) await new Promise((r) => ffmpeg.stdin.once("drain", r));
    if (i % 90 === 0) console.log(`frame ${i}/${total}`);
  }
  ffmpeg.stdin.end();
  await encoded;
  console.log(`rendered ${total} frames in ${((Date.now() - t0) / 1000).toFixed(1)} s`);

  // Seeking must not depend on what was shown before: capture a few frames again, out of order.
  for (const t of RECHECK) {
    const i = Math.round(t * FPS);
    const again = sha(await capture(s, i / FPS));
    if (again !== hashes[i]) throw new Error(`frame ${i} (${t} s) differs when captured out of order`);
  }
  console.log("out-of-order check: " + RECHECK.length + " frames identical");

  // Stills come from the finished MP4, so they show what ships.
  const stillDir = join(HERE, "stills");
  mkdirSync(stillDir, { recursive: true });
  for (const [name, t] of STILLS) {
    run("ffmpeg", ["-hide_banner", "-loglevel", "error", "-y", "-ss", String(t), "-i", mp4, "-frames:v", "1", join(stillDir, name + ".png")]);
  }
  console.log(run("ffprobe", ["-v", "error", "-show_entries",
    "stream=codec_name,codec_type,profile,width,height,pix_fmt,r_frame_rate,nb_frames,color_space:format=duration,size,bit_rate",
    "-of", "default=noprint_wrappers=1", mp4]).trim());
  console.log("size: " + (statSync(mp4).size / 1048576).toFixed(2) + " MB");
}

let failed = false;
try {
  await main();
} catch (e) {
  failed = true;
  console.error("render failed: " + e.message);
} finally {
  try { if (ffmpeg && ffmpeg.exitCode === null) ffmpeg.kill(); } catch {}
  try { if (ws) { await Promise.race([send("Browser.close"), sleep(3000)]).catch(() => {}); ws.close(); } } catch {}
  await sleep(500);
  killChrome();
  for (let i = 0; i < 10; i++) {
    try { rmSync(profile, { recursive: true, force: true }); break; } catch { await sleep(500); }
  }
}
process.exit(failed ? 1 : 0);
