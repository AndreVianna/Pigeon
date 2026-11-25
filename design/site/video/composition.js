/* Timeline of the demo video. One clock, in seconds, drives everything:
   - the site's hook animations (CSS keyframes named hk-*, a 12 s loop) are paused and seeked;
   - the scene changes below are Web Animations, also paused and seeked.
   window.__seek(t) shows the frame at time t. render.mjs calls it once per frame.
   Without ?render in the address the page plays itself in real time, in a loop, for preview. */
(function () {
  "use strict";
  var DURATION = 23;       // seconds
  var HOOK_HOLD = 11.4;    // the hook plays 0 to 11.4 s (through "Nobody typed."), then holds while scene 1 fades
  var EASE_OUT = "cubic-bezier(.2,0,0,1)";
  var EASE_IN = "cubic-bezier(.4,0,1,1)";

  function $(s) { return document.querySelector(s); }
  function at(el, from, to, start, dur, easing) {
    return el.animate([from, to], { delay: start * 1000, duration: dur * 1000, fill: "both", easing: easing || EASE_OUT });
  }
  var hidden = { opacity: 0 }, shown = { opacity: 1 };
  var low = { opacity: 0, transform: "translateY(10px)" }, up = { opacity: 1, transform: "translateY(0)" };

  // Scene 1, 0 to 12 s: the hook. It leaves between 11.4 and 12.0 s.
  at($("#s1"), shown, hidden, 11.4, 0.6, EASE_IN);

  // Scene 2, 12 to 19.4 s: headline, subline, then the three facts one after another.
  at($("#s2-h"), low, up, 12.0, 0.7);
  at($("#s2-line"), low, up, 12.5, 0.6);
  var facts = document.querySelectorAll(".vd-facts li");
  at(facts[0], low, up, 13.2, 0.5);
  at(facts[1], low, up, 13.9, 0.5);
  at(facts[2], low, up, 14.6, 0.5);
  at($("#s2"), shown, hidden, 18.9, 0.5, EASE_IN);

  // Scene 3, 19.5 to 23 s: the end card. It holds to the last frame.
  at($("#s3-logo"), low, up, 19.5, 0.7);
  at($("#s3-url"), low, up, 20.0, 0.6);

  var anims = null;
  function collect() {
    anims = document.getAnimations().map(function (a) {
      a.pause();
      return { a: a, hook: typeof a.animationName === "string" && a.animationName.indexOf("hk-") === 0 };
    });
  }
  function set(t) {
    if (!anims) collect();
    var ms = t * 1000, hookMs = Math.min(t, HOOK_HOLD) * 1000;
    for (var i = 0; i < anims.length; i++) anims[i].a.currentTime = anims[i].hook ? hookMs : ms;
  }
  function nextPaint() {
    return new Promise(function (r) { requestAnimationFrame(function () { requestAnimationFrame(r); }); });
  }

  var FONTS = ['400 16px "Inter"', '500 16px "Inter"', '600 16px "Inter"', '500 16px "JetBrains Mono"'];
  window.__duration = DURATION;
  window.__ready = Promise.all(FONTS.map(function (f) { return document.fonts.load(f, "Pigeon ·"); }))
    .then(function () { return document.fonts.ready; })
    .then(function () {
      var img = $("#s3-logo");
      return img.complete ? null : new Promise(function (r) { img.onload = img.onerror = r; });
    })
    .then(function () {
      var missing = FONTS.filter(function (f) { return !document.fonts.check(f, "Pigeon ·"); });
      set(0);
      var loaded = {};
      document.fonts.forEach(function (f) {
        if (f.status === "loaded") { var k = f.family.replace(/"/g, ""); loaded[k] = (loaded[k] || 0) + 1; }
      });
      if (!loaded["Inter"]) missing.push("Inter (no face loaded)");
      if (!loaded["JetBrains Mono"]) missing.push("JetBrains Mono (no face loaded)");
      return { fontsMissing: missing, fontsLoaded: loaded, logo: $("#s3-logo").naturalWidth > 0, animations: anims.length,
               hookAnimations: anims.filter(function (x) { return x.hook; }).length };
    });
  window.__seek = function (t) { set(t); return nextPaint().then(function () { return t; }); };

  if (!/[?&]render\b/.test(location.search)) {
    window.__ready.then(function () {
      var t0 = performance.now();
      (function tick() { set(((performance.now() - t0) / 1000) % DURATION); requestAnimationFrame(tick); })();
    });
  }
})();
