/* Pigeon website. No network requests, no storage.
   Theme (follows the system), the hook animation's Pause/Play, Copy buttons, the phone menu,
   and the Install page tabs. Without this file every page still works: the hook shows its still frame,
   the tab panels are stacked and the menu is a plain <details>. */
(function () {
  "use strict";
  var root = document.documentElement;

  function media(q) {
    try { return window.matchMedia(q); } catch (e) { return null; }
  }
  function onChange(mq, fn) {
    if (!mq) return;
    if (mq.addEventListener) mq.addEventListener("change", fn);
    else if (mq.addListener) mq.addListener(fn);
  }

  /* Theme: Charcoal by default, Daylight when the system asks for light. The inline script in <head>
     sets it before the first paint; this keeps it in step if the system setting changes. */
  var light = media("(prefers-color-scheme: light)");
  onChange(light, function () {
    root.setAttribute("data-polarity", light.matches ? "light" : "dark");
  });

  /* Hook animation */
  var scene = document.querySelector(".hk");
  var toggle = scene && scene.querySelector(".hk-toggle");
  if (scene && toggle) {
    var calm = media("(prefers-reduced-motion: reduce)");
    var userPaused = false;
    var offscreen = false;

    var apply = function () {
      var paused = userPaused || offscreen;
      scene.classList.toggle("is-paused", paused);
      toggle.textContent = userPaused ? "Play" : "Pause";
      toggle.setAttribute("aria-label", userPaused ? "Play the animation" : "Pause the animation");
    };
    var setMode = function () {
      var still = !!(calm && calm.matches);
      scene.classList.toggle("hk--still", still);
      scene.classList.toggle("hk--play", !still);
      toggle.hidden = still;
      apply();
    };
    toggle.addEventListener("click", function () {
      userPaused = !userPaused;
      apply();
    });
    if ("IntersectionObserver" in window) {
      new IntersectionObserver(function (entries) {
        offscreen = !entries[entries.length - 1].isIntersecting;
        apply();
      }).observe(scene);
    }
    onChange(calm, setMode);
    setMode();
  }

  /* Copy buttons: the text of the command block they sit in */
  var live = document.createElement("p");
  live.className = "pg-vh";
  live.setAttribute("aria-live", "polite");
  document.body.appendChild(live);

  function selectText(el) {
    var range = document.createRange();
    range.selectNodeContents(el);
    var sel = window.getSelection();
    sel.removeAllRanges();
    sel.addRange(range);
  }
  function legacyCopy(el) {
    selectText(el);
    var ok = false;
    try { ok = document.execCommand("copy"); } catch (e) { ok = false; }
    if (ok) window.getSelection().removeAllRanges();
    return ok;
  }
  function done(btn, ok) {
    btn.textContent = ok ? "Copied" : "Selected";
    btn.classList.add("is-done");
    live.textContent = ok ? "Copied to the clipboard." : "Text selected. Press Ctrl+C or Command+C to copy it.";
    clearTimeout(btn._t);
    btn._t = setTimeout(function () {
      btn.textContent = "Copy";
      btn.classList.remove("is-done");
    }, 2000);
  }
  document.addEventListener("click", function (e) {
    var btn = e.target.closest && e.target.closest(".pg-copy");
    if (!btn) return;
    var pre = btn.parentElement.querySelector("pre");
    if (!pre) return;
    var text = pre.textContent;
    if (navigator.clipboard && window.isSecureContext) {
      navigator.clipboard.writeText(text).then(
        function () { done(btn, true); },
        function () { done(btn, legacyCopy(pre)); }
      );
    } else {
      done(btn, legacyCopy(pre));
    }
  });

  /* Phone menu: close it after choosing a link, on Escape, and on a click outside */
  var menu = document.querySelector(".pg-menu");
  if (menu) {
    menu.addEventListener("click", function (e) {
      if (e.target.closest("a")) menu.open = false;
    });
    document.addEventListener("keydown", function (e) {
      if (e.key === "Escape" && menu.open) {
        menu.open = false;
        menu.querySelector("summary").focus();
      }
    });
    document.addEventListener("click", function (e) {
      if (menu.open && !menu.contains(e.target)) menu.open = false;
    });
  }

  /* Tabs (Install page). Served as stacked panels; this turns each group into tabs,
     and opens the panel named in the address (#windows, #claude ...). */
  var groups = [];
  Array.prototype.forEach.call(document.querySelectorAll(".c-tabs"), function (box) {
    var tabs = Array.prototype.slice.call(box.querySelectorAll('[role="tab"]'));
    if (!tabs.length) return;
    var panelOf = function (tab) { return document.getElementById(tab.getAttribute("aria-controls")); };
    var select = function (tab, focus) {
      tabs.forEach(function (t) {
        var on = t === tab;
        t.setAttribute("aria-selected", on ? "true" : "false");
        t.tabIndex = on ? 0 : -1;
        var p = panelOf(t);
        if (p) p.hidden = !on;
      });
      if (focus) tab.focus();
    };
    tabs.forEach(function (tab, i) {
      tab.addEventListener("click", function () {
        select(tab, false);
        if (history.replaceState) history.replaceState(null, "", "#" + tab.getAttribute("aria-controls"));
      });
      tab.addEventListener("keydown", function (e) {
        var n = null;
        if (e.key === "ArrowRight") n = tabs[(i + 1) % tabs.length];
        else if (e.key === "ArrowLeft") n = tabs[(i - 1 + tabs.length) % tabs.length];
        else if (e.key === "Home") n = tabs[0];
        else if (e.key === "End") n = tabs[tabs.length - 1];
        if (n) { e.preventDefault(); select(n, true); }
      });
    });
    select(tabs.filter(function (t) { return t.getAttribute("aria-selected") === "true"; })[0] || tabs[0], false);
    groups.push({ box: box, tabs: tabs, select: select });
  });

  function openFromHash(scroll) {
    var id = decodeURIComponent(location.hash.slice(1));
    if (!id) return;
    groups.forEach(function (g) {
      g.tabs.forEach(function (t) {
        if (t.getAttribute("aria-controls") === id) {
          g.select(t, false);
          if (scroll) g.box.scrollIntoView({ block: "start" });
        }
      });
    });
  }
  if (groups.length) {
    openFromHash(false);
    window.addEventListener("load", function () { openFromHash(true); });
    window.addEventListener("hashchange", function () { openFromHash(true); });
  }

  /* Arriving at an anchor (#security ...): the web fonts can change the page's height after the browser
     has scrolled, so settle on the target again once they are in, unless the reader has moved already. */
  var moved = false;
  ["wheel", "touchstart", "keydown", "mousedown"].forEach(function (ev) {
    window.addEventListener(ev, function () { moved = true; }, { once: true, passive: true });
  });
  window.addEventListener("load", function () {
    var id = decodeURIComponent(location.hash.slice(1));
    var target = id && document.getElementById(id);
    if (!target || target.closest(".c-tabs")) return;
    var settle = function () {
      if (moved) return;
      var top = target.getBoundingClientRect().top;
      if (top < 0 || top > 120) target.scrollIntoView({ block: "start", behavior: "auto" });
    };
    if (document.fonts && document.fonts.ready) document.fonts.ready.then(settle); else settle();
  });
})();
