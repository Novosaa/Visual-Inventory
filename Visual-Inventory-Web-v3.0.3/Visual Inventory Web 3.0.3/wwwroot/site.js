// Visual Inventory (web) 3.0.3 — small extras that make the page feel like the desktop app.
// Everything works without this file too; it only makes clicking and searching quicker.
// It only reads pages from this same site. It sends nothing anywhere and saves nothing,
// except remembering (in this browser tab only) whether details are hidden and how wide they are.
(function () {
    "use strict";

    function remember(key, value) { try { sessionStorage.setItem(key, value); } catch (e) { /* not available */ } }
    function recall(key) { try { return sessionStorage.getItem(key); } catch (e) { return null; } }

    var body = document.body;
    var root = document.documentElement;
    var view = document.getElementById("section-view");
    if (!view) return; // Overview or no Library: nothing extra needed.

    // ------------------------------------------------------------ Hide / Show details
    var toggle = document.getElementById("details-toggle");
    function applyDetails(hidden) {
        body.classList.toggle("details-hidden", hidden);
        if (toggle) toggle.textContent = hidden ? "Show details" : "Hide details";
    }
    applyDetails(recall("vi-details-hidden") === "1");
    if (toggle) toggle.addEventListener("click", function () {
        var hidden = !body.classList.contains("details-hidden");
        applyDetails(hidden);
        remember("vi-details-hidden", hidden ? "1" : "0");
    });

    // ------------------------------------------------------------ drag to resize the details panel
    var splitter = document.getElementById("splitter");
    function setWidth(px) {
        var w = Math.max(260, Math.min(700, Math.round(px)));
        root.style.setProperty("--details-width", w + "px");
        return w;
    }
    var savedWidth = parseInt(recall("vi-details-width") || "", 10);
    if (savedWidth) setWidth(savedWidth);
    if (splitter) splitter.addEventListener("pointerdown", function (e) {
        e.preventDefault();
        splitter.setPointerCapture(e.pointerId);
        function move(ev) { setWidth(view.getBoundingClientRect().right - ev.clientX); }
        function up(ev) {
            splitter.removeEventListener("pointermove", move);
            splitter.removeEventListener("pointerup", up);
            remember("vi-details-width", String(setWidth(view.getBoundingClientRect().right - ev.clientX)));
        }
        splitter.addEventListener("pointermove", move);
        splitter.addEventListener("pointerup", up);
    });

    // ------------------------------------------------------------ click a tile: show it without reloading
    var tiles = document.getElementById("tiles");
    var details = document.getElementById("details");
    var loading = 0;
    function select(tile) {
        var all = tiles.querySelectorAll(".tile.selected");
        for (var i = 0; i < all.length; i++) all[i].classList.remove("selected");
        tile.classList.add("selected");
        var href = tile.getAttribute("href");
        var mine = ++loading;
        fetch(href, { credentials: "omit", headers: { "Accept": "text/html" } })
            .then(function (r) { if (!r.ok) throw new Error(); return r.text(); })
            .then(function (html) {
                if (mine !== loading) return;
                var doc = new DOMParser().parseFromString(html, "text/html");
                var fresh = doc.getElementById("details");
                if (!fresh) throw new Error();
                details.replaceChildren.apply(details, Array.prototype.slice.call(fresh.childNodes).map(function (n) { return document.importNode(n, true); }));
                details.scrollTop = 0;
                try { history.replaceState(null, "", href); } catch (e) { }
            })
            .catch(function () { if (mine === loading) window.location.href = href; });
    }
    tiles.addEventListener("click", function (e) {
        var tile = e.target.closest(".tile");
        if (!tile || e.ctrlKey || e.metaKey || e.shiftKey || e.button !== 0) return;
        e.preventDefault();
        select(tile);
    });

    // ------------------------------------------------------------ search as you type (like the desktop app)
    var search = document.getElementById("search");
    var count = document.getElementById("visible-count");
    function filter() {
        var q = search.value.trim().toLowerCase();
        var shown = 0, firstShown = null, selectedHidden = false;
        var list = tiles.querySelectorAll(".tile");
        for (var i = 0; i < list.length; i++) {
            var match = q === "" || list[i].getAttribute("data-search").indexOf(q) !== -1;
            list[i].classList.toggle("hidden", !match);
            if (match) { shown++; if (!firstShown) firstShown = list[i]; }
            else if (list[i].classList.contains("selected")) selectedHidden = true;
        }
        if (count) count.textContent = shown === 1 ? "1 shown" : shown + " shown";
        if (selectedHidden && firstShown) select(firstShown);
    }
    if (search) {
        search.addEventListener("input", filter);
        search.addEventListener("keydown", function (e) {
            if (e.key === "Escape" && search.value !== "") { search.value = ""; filter(); e.preventDefault(); }
        });
    }

    // Ctrl+F jumps to the search box in a category, like the desktop app.
    document.addEventListener("keydown", function (e) {
        if (search && (e.ctrlKey || e.metaKey) && (e.key === "f" || e.key === "F")) {
            e.preventDefault();
            search.focus();
            search.select();
        }
    });
})();
