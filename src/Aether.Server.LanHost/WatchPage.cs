using System.Net;
using System.Text.Encodings.Web;

namespace Aether.Server.LanHost;

internal static class WatchPage
{
    // Self-contained on purpose: the phone must not need anything beyond this server.
    // canPlayType only knows the container, so a codec the phone can't decode (HEVC in mp4) is caught
    // afterwards: an error event, or metadata that loads with no picture (videoWidth 0).
    public static string Build(string token, string mime) => Build(token, mime, "Aether");

    public static string Build(string token, string mime, string title)
    {
        string safeTitle = string.IsNullOrWhiteSpace(title) ? "Aether" : title;
        string htmlTitle = WebUtility.HtmlEncode(safeTitle);
        string jsTitle = JavaScriptEncoder.Default.Encode(safeTitle);
        string jsToken = JavaScriptEncoder.Default.Encode(token);
        string jsMime = JavaScriptEncoder.Default.Encode(mime);

        return $$$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
            <meta name="theme-color" content="#09090b">
            <title>{{{htmlTitle}}}</title>
            <link rel="icon" href="/favicon.svg" type="image/svg+xml">
            <style>
                * { box-sizing: border-box; -webkit-tap-highlight-color: transparent; }
                html, body {
                    margin: 0; width: 100%; height: 100%; overflow: hidden;
                    background: #000; color: #f4f4f5;
                    font-family: system-ui, -apple-system, BlinkMacSystemFont, sans-serif;
                }
                .player {
                    position: fixed; inset: 0; background: #000; overflow: hidden;
                    user-select: none; -webkit-user-select: none;
                }
                video {
                    position: absolute; inset: 0; width: 100%; height: 100%;
                    object-fit: contain; background: #000;
                }
                #gesture-layer {
                    position: absolute; inset: 0; z-index: 1; touch-action: none;
                }
                .top-bar, .bottom-bar {
                    position: absolute; left: 0; right: 0; z-index: 3;
                    transition: opacity 0.22s ease, transform 0.22s ease;
                }
                .top-bar {
                    top: 0; display: flex; align-items: center; justify-content: space-between; gap: 12px;
                    padding: calc(12px + env(safe-area-inset-top)) calc(14px + env(safe-area-inset-right)) 32px calc(14px + env(safe-area-inset-left));
                    background: linear-gradient(180deg, rgba(0, 0, 0, 0.82) 0%, rgba(0, 0, 0, 0.35) 60%, rgba(0, 0, 0, 0) 100%);
                }
                .bottom-bar {
                    bottom: 0; display: flex; flex-direction: column; gap: 2px;
                    padding: 32px calc(14px + env(safe-area-inset-right)) calc(10px + env(safe-area-inset-bottom)) calc(14px + env(safe-area-inset-left));
                    background: linear-gradient(0deg, rgba(0, 0, 0, 0.88) 0%, rgba(0, 0, 0, 0.48) 58%, rgba(0, 0, 0, 0) 100%);
                }
                .player:not(.chrome-visible) .top-bar {
                    opacity: 0; transform: translateY(-10px); pointer-events: none;
                }
                .player:not(.chrome-visible) .bottom-bar {
                    opacity: 0; transform: translateY(10px); pointer-events: none;
                }
                .title {
                    flex: 1 1 auto; min-width: 0;
                    font-size: 15px; font-weight: 600; letter-spacing: -0.01em; color: #fafafa;
                    white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
                    text-shadow: 0 1px 4px rgba(0, 0, 0, 0.7);
                }
                .conn-badge {
                    flex: 0 0 auto; display: inline-flex; align-items: center; gap: 6px;
                    padding: 5px 10px; border-radius: 999px;
                    background: rgba(24, 24, 27, 0.74); border: 1px solid rgba(255, 255, 255, 0.14);
                    backdrop-filter: blur(8px); -webkit-backdrop-filter: blur(8px);
                    font-size: 12px; font-weight: 500; font-variant-numeric: tabular-nums; color: #e4e4e7;
                    white-space: nowrap;
                }
                .conn-dot {
                    width: 8px; height: 8px; border-radius: 50%; background: #38bdf8;
                    box-shadow: 0 0 8px currentColor; color: #38bdf8;
                }
                .conn-badge[data-state="excellent"] .conn-dot { background: #22c55e; color: #22c55e; }
                .conn-badge[data-state="good"] .conn-dot { background: #38bdf8; color: #38bdf8; }
                .conn-badge[data-state="fair"] .conn-dot { background: #f59e0b; color: #f59e0b; }
                .conn-badge[data-state="poor"] .conn-dot { background: #ef4444; color: #ef4444; }
                .conn-badge[data-state="offline"] {
                    background: rgba(127, 29, 29, 0.65); border-color: rgba(248, 113, 113, 0.45);
                }
                .conn-badge[data-state="offline"] .conn-dot { background: #f87171; color: #f87171; }
                .center-play {
                    position: absolute; top: 50%; left: 50%; transform: translate(-50%, -50%);
                    z-index: 2; width: 68px; height: 68px; border: 1px solid rgba(255, 255, 255, 0.22);
                    border-radius: 50%; display: flex; align-items: center; justify-content: center;
                    background: rgba(15, 15, 20, 0.66); color: #fff; cursor: pointer;
                    backdrop-filter: blur(10px); -webkit-backdrop-filter: blur(10px);
                    transition: opacity 0.18s ease, transform 0.15s ease;
                }
                .center-play:active { transform: translate(-50%, -50%) scale(0.92); }
                .player.is-playing .center-play,
                .player:not(.chrome-visible) .center-play { opacity: 0; pointer-events: none; }
                .scrubber {
                    position: relative; width: 100%; height: 44px; min-height: 44px;
                    display: flex; align-items: center; cursor: pointer; touch-action: none;
                }
                .scrub-track {
                    position: relative; width: 100%; height: 4px; border-radius: 999px;
                    background: rgba(255, 255, 255, 0.22); transition: height 0.12s ease;
                }
                .scrubber.dragging .scrub-track { height: 6px; }
                .scrub-fill {
                    position: absolute; top: 0; bottom: 0; left: 0; width: 0%;
                    border-radius: 999px; pointer-events: none;
                }
                .scrub-fill.buf { background: rgba(255, 255, 255, 0.34); }
                .scrub-fill.play { background: #38bdf8; }
                .scrub-thumb {
                    position: absolute; top: 50%; left: 0%; width: 14px; height: 14px;
                    transform: translate(-50%, -50%); border-radius: 50%;
                    background: #fff; box-shadow: 0 1px 6px rgba(0, 0, 0, 0.65);
                    pointer-events: none; transition: transform 0.12s ease;
                }
                .scrubber.dragging .scrub-thumb { transform: translate(-50%, -50%) scale(1.28); }
                .scrub-tip {
                    position: absolute; bottom: 42px; left: 0%; transform: translateX(-50%);
                    padding: 4px 8px; border-radius: 6px;
                    background: rgba(15, 15, 20, 0.92); border: 1px solid rgba(255, 255, 255, 0.16);
                    font-size: 12px; font-weight: 600; font-variant-numeric: tabular-nums;
                    color: #fafafa; white-space: nowrap; opacity: 0; pointer-events: none;
                    transition: opacity 0.12s ease;
                }
                .scrubber.dragging .scrub-tip { opacity: 1; }
                .time-row {
                    display: flex; justify-content: space-between; align-items: center;
                    font-size: 12px; font-variant-numeric: tabular-nums; color: #d4d4d8;
                    margin-top: -6px; margin-bottom: 2px;
                }
                .transport {
                    display: flex; align-items: center; justify-content: space-between; gap: 6px;
                }
                .transport-group {
                    display: flex; align-items: center; gap: 4px;
                }
                .btn {
                    min-width: 44px; min-height: 44px; padding: 0 8px; border: 0; border-radius: 10px;
                    display: inline-flex; align-items: center; justify-content: center;
                    background: transparent; color: #f4f4f5; cursor: pointer;
                    font-family: inherit; font-size: 13px; font-weight: 600;
                    transition: background 0.15s ease, transform 0.12s ease;
                }
                .btn:active { background: rgba(255, 255, 255, 0.16); transform: scale(0.94); }
                .btn-text { font-variant-numeric: tabular-nums; min-width: 48px; }
                .hud {
                    position: absolute; top: 50%; left: 50%; transform: translate(-50%, -50%);
                    z-index: 4; padding: 10px 16px; border-radius: 12px;
                    background: rgba(15, 15, 20, 0.84); border: 1px solid rgba(255, 255, 255, 0.16);
                    backdrop-filter: blur(10px); -webkit-backdrop-filter: blur(10px);
                    font-size: 16px; font-weight: 600; font-variant-numeric: tabular-nums;
                    color: #fafafa; text-align: center; white-space: nowrap;
                    opacity: 0; pointer-events: none; transition: opacity 0.16s ease;
                }
                .hud.visible { opacity: 1; }
                .seek-badge {
                    position: absolute; top: 50%; transform: translateY(-50%);
                    z-index: 4; padding: 12px 16px; border-radius: 999px;
                    background: rgba(15, 15, 20, 0.78); border: 1px solid rgba(255, 255, 255, 0.16);
                    font-size: 15px; font-weight: 700; font-variant-numeric: tabular-nums; color: #fff;
                    opacity: 0; pointer-events: none; transition: opacity 0.18s ease, transform 0.18s ease;
                }
                .seek-badge.left { left: 12%; }
                .seek-badge.right { right: 12%; }
                .seek-badge.visible { opacity: 1; transform: translateY(-50%) scale(1.04); }
                .hidden { display: none !important; }
                #notice {
                    display: none; position: fixed; inset: 0; z-index: 10; box-sizing: border-box;
                    padding: 24px; flex-direction: column; justify-content: center;
                    background: #111; font-size: 17px; line-height: 1.5;
                }
                #notice h1 { font-size: 20px; margin: 0 0 12px; }
                #notice p { margin: 0 0 12px; color: #ccc; }
            </style>
        </head>
        <body>
            <div id="player" class="player chrome-visible">
                <video id="v" autoplay playsinline preload="auto"></video>
                <div id="gesture-layer" aria-hidden="true"></div>
                <div id="seek-badge-left" class="seek-badge left" aria-hidden="true"></div>
                <div id="seek-badge-right" class="seek-badge right" aria-hidden="true"></div>
                <div id="hud" class="hud" aria-live="polite" aria-hidden="true"></div>
                <button id="center-play" class="center-play" type="button" aria-label="Play">
                    <svg width="30" height="30" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                        <path d="M8 5v14l11-7z"/>
                    </svg>
                </button>
                <header id="top-bar" class="top-bar">
                    <div id="video-title" class="title">{{{htmlTitle}}}</div>
                    <div id="conn-badge" class="conn-badge" data-state="good" aria-live="polite">
                        <span id="conn-dot" class="conn-dot"></span>
                        <span id="conn-text">Good</span>
                    </div>
                </header>
                <footer id="bottom-bar" class="bottom-bar">
                    <div id="scrubber" class="scrubber" role="slider" tabindex="0" aria-label="Seek" aria-valuemin="0" aria-valuemax="0" aria-valuenow="0">
                        <div class="scrub-track">
                            <div id="buf-fill" class="scrub-fill buf"></div>
                            <div id="play-fill" class="scrub-fill play"></div>
                            <div id="scrub-thumb" class="scrub-thumb"></div>
                        </div>
                        <div id="scrub-tip" class="scrub-tip">0:00</div>
                    </div>
                    <div class="time-row">
                        <span id="t-cur">0:00</span>
                        <span id="t-dur">--:--</span>
                    </div>
                    <div class="transport">
                        <div class="transport-group">
                            <button id="btn-play" class="btn" type="button" aria-label="Play">
                                <svg id="ico-play" width="22" height="22" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                                    <path d="M8 5v14l11-7z"/>
                                </svg>
                                <svg id="ico-pause" class="hidden" width="22" height="22" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                                    <path d="M6 5h4v14H6zm8 0h4v14h-4z"/>
                                </svg>
                            </button>
                            <button id="btn-back" class="btn" type="button" aria-label="Seek back 10 seconds">
                                <svg width="22" height="22" viewBox="0 0 24 24" aria-hidden="true">
                                    <path fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" d="M12 5a7 7 0 1 1-6.3 4M12 2v6L8.5 5"/>
                                    <text x="12" y="15.5" text-anchor="middle" font-size="7.5" font-weight="700" font-family="system-ui,sans-serif" fill="currentColor">10</text>
                                </svg>
                            </button>
                            <button id="btn-fwd" class="btn" type="button" aria-label="Seek forward 10 seconds">
                                <svg width="22" height="22" viewBox="0 0 24 24" aria-hidden="true">
                                    <path fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" d="M12 5a7 7 0 1 0 6.3 4M12 2v6l3.5-3"/>
                                    <text x="12" y="15.5" text-anchor="middle" font-size="7.5" font-weight="700" font-family="system-ui,sans-serif" fill="currentColor">10</text>
                                </svg>
                            </button>
                        </div>
                        <div class="transport-group">
                            <button id="btn-mute" class="btn" type="button" aria-label="Mute">
                                <svg id="ico-vol-on" width="22" height="22" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                                    <path d="M3 9v6h4l5 5V4L7 9H3zm13.5 3a4.5 4.5 0 0 0-2.5-4.03v8.05A4.49 4.49 0 0 0 16.5 12zM14 3.23v2.06a7 7 0 0 1 0 13.42v2.06a9 9 0 0 0 0-17.54z"/>
                                </svg>
                                <svg id="ico-vol-off" class="hidden" width="22" height="22" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                                    <path d="M16.5 12a4.5 4.5 0 0 0-2.5-4.03v2.21l2.45 2.45c.03-.2.05-.41.05-.63zm2.5 0c0 .94-.2 1.82-.54 2.64l1.51 1.51A8.8 8.8 0 0 0 21 12a9 9 0 0 0-7-8.77v2.06c2.89.86 5 3.54 5 6.71zM4.27 3L3 4.27 7.73 9H3v6h4l5 5v-6.73l4.25 4.25c-.67.52-1.42.93-2.25 1.18v2.06a8.99 8.99 0 0 0 3.69-1.81L19.73 21 21 19.73l-9-9L4.27 3zM12 4L9.91 6.09 12 8.18V4z"/>
                                </svg>
                            </button>
                            <button id="btn-speed" class="btn btn-text" type="button" aria-label="Playback speed">1x</button>
                            <button id="btn-fs" class="btn" type="button" aria-label="Fullscreen">
                                <svg id="ico-fs-enter" width="22" height="22" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                                    <path d="M7 14H5v5h5v-2H7v-3zm-2-4h2V7h3V5H5v5zm12 7h-3v2h5v-5h-2v3zM14 5v2h3v3h2V5h-5z"/>
                                </svg>
                                <svg id="ico-fs-exit" class="hidden" width="22" height="22" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                                    <path d="M5 16h3v3h2v-5H5v2zm3-8H5v2h5V5H8v3zm6 11h2v-3h3v-2h-5v5zm2-11V5h-2v5h5V8h-3z"/>
                                </svg>
                            </button>
                        </div>
                    </div>
                </footer>
            </div>
            <div id="notice" role="alert">
                <h1>This phone can't play this video</h1>
                <p id="why"></p>
                <p>Aether doesn't convert videos, so the file has to be one your phone already understands. Try another browser, or watch it on the PC instead.</p>
            </div>
            <script>
                (function () {
                    var player = document.getElementById('player');
                    var v = document.getElementById('v');
                    var gestureLayer = document.getElementById('gesture-layer');
                    var centerPlay = document.getElementById('center-play');
                    var scrubber = document.getElementById('scrubber');
                    var bufFill = document.getElementById('buf-fill');
                    var playFill = document.getElementById('play-fill');
                    var scrubThumb = document.getElementById('scrub-thumb');
                    var scrubTip = document.getElementById('scrub-tip');
                    var tCur = document.getElementById('t-cur');
                    var tDur = document.getElementById('t-dur');
                    var btnPlay = document.getElementById('btn-play');
                    var icoPlay = document.getElementById('ico-play');
                    var icoPause = document.getElementById('ico-pause');
                    var btnBack = document.getElementById('btn-back');
                    var btnFwd = document.getElementById('btn-fwd');
                    var btnMute = document.getElementById('btn-mute');
                    var icoVolOn = document.getElementById('ico-vol-on');
                    var icoVolOff = document.getElementById('ico-vol-off');
                    var btnSpeed = document.getElementById('btn-speed');
                    var btnFs = document.getElementById('btn-fs');
                    var icoFsEnter = document.getElementById('ico-fs-enter');
                    var icoFsExit = document.getElementById('ico-fs-exit');
                    var hud = document.getElementById('hud');
                    var seekBadgeLeft = document.getElementById('seek-badge-left');
                    var seekBadgeRight = document.getElementById('seek-badge-right');
                    var connBadge = document.getElementById('conn-badge');
                    var connText = document.getElementById('conn-text');
                    var notice = document.getElementById('notice');
                    var why = document.getElementById('why');

                    var token = '{{{jsToken}}}';
                    var mime = '{{{jsMime}}}';
                    var pageTitle = '{{{jsTitle}}}';
                    var speeds = [0.5, 1, 1.25, 1.5, 2];
                    var speedIndex = 1;

                    var chromeVisible = true;
                    var hideTimer = null;
                    var hudTimer = null;
                    var seekBadgeTimers = { left: null, right: null };
                    var tapAccum = { side: null, sec: 0, lastAt: 0 };
                    var lastTap = { time: 0, side: null };
                    var singleTapTimer = null;

                    var scrubbing = false;
                    var scrubTarget = 0;

                    var activePointerId = null;
                    var gestureMode = 'none';
                    var startX = 0;
                    var startY = 0;
                    var gestureRect = null;
                    var dragBaseTime = 0;
                    var dragTargetTime = 0;
                    var dragBaseVolume = 1;

                    var pingTimer = null;
                    var pingInFlight = false;
                    var lastRtt = null;
                    var pingFailures = 0;
                    var isBuffering = false;

                    function clamp(val, min, max) {
                        return Math.min(max, Math.max(min, val));
                    }

                    function pad2(n) {
                        return n < 10 ? '0' + n : '' + n;
                    }

                    function fmtTime(sec) {
                        if (typeof sec !== 'number' || !isFinite(sec) || sec < 0) return '0:00';
                        var total = Math.floor(sec);
                        var h = Math.floor(total / 3600);
                        var m = Math.floor((total % 3600) / 60);
                        var s = total % 60;
                        return h > 0 ? (h + ':' + pad2(m) + ':' + pad2(s)) : (m + ':' + pad2(s));
                    }

                    function fmtDelta(sec) {
                        var sign = sec >= 0 ? '+' : '-';
                        return sign + fmtTime(Math.abs(sec));
                    }

                    function hasDuration() {
                        return typeof v.duration === 'number' && isFinite(v.duration) && v.duration > 0;
                    }

                    function stopPing() {
                        if (pingTimer !== null) {
                            clearInterval(pingTimer);
                            pingTimer = null;
                        }
                    }

                    function fail(reason) {
                        if (notice.style.display === 'flex') return;
                        stopPing();
                        why.textContent = reason;
                        try { v.pause(); } catch (err) { }
                        player.style.display = 'none';
                        notice.style.display = 'flex';
                    }

                    if (!v.canPlayType || v.canPlayType(mime) === '') {
                        fail('This browser can\'t open this kind of file (' + mime.replace('video/', '').replace('x-', '') + '). Phones usually can\'t play MKV, and some can\'t play HEVC (H.265) video.');
                        return;
                    }

                    function clearHideTimer() {
                        if (hideTimer !== null) {
                            clearTimeout(hideTimer);
                            hideTimer = null;
                        }
                    }

                    function scheduleHide() {
                        clearHideTimer();
                        if (v.paused || v.ended || scrubbing || gestureMode === 'seek' || gestureMode === 'volume') return;
                        hideTimer = setTimeout(function () {
                            if (v.paused || v.ended || scrubbing || gestureMode === 'seek' || gestureMode === 'volume') return;
                            chromeVisible = false;
                            player.classList.remove('chrome-visible');
                        }, 3000);
                    }

                    function showChrome() {
                        chromeVisible = true;
                        player.classList.add('chrome-visible');
                        scheduleHide();
                    }

                    function toggleChrome() {
                        if (chromeVisible && !v.paused && !v.ended) {
                            clearHideTimer();
                            chromeVisible = false;
                            player.classList.remove('chrome-visible');
                        } else {
                            showChrome();
                        }
                    }

                    function showHud(text, autoHideMs) {
                        if (hudTimer !== null) {
                            clearTimeout(hudTimer);
                            hudTimer = null;
                        }
                        hud.textContent = text;
                        hud.classList.add('visible');
                        if (autoHideMs > 0) {
                            hudTimer = setTimeout(function () {
                                hud.classList.remove('visible');
                            }, autoHideMs);
                        }
                    }

                    function hideHudSoon(delayMs) {
                        if (hudTimer !== null) clearTimeout(hudTimer);
                        hudTimer = setTimeout(function () {
                            hud.classList.remove('visible');
                        }, delayMs);
                    }

                    function showSeekBadge(side, deltaSec) {
                        var now = Date.now();
                        if (tapAccum.side === side && (now - tapAccum.lastAt) < 700) {
                            tapAccum.sec += deltaSec;
                        } else {
                            tapAccum.side = side;
                            tapAccum.sec = deltaSec;
                        }
                        tapAccum.lastAt = now;

                        var el = side === 'left' ? seekBadgeLeft : seekBadgeRight;
                        el.textContent = (tapAccum.sec > 0 ? '+' : '') + tapAccum.sec + 's';
                        el.classList.add('visible');
                        if (seekBadgeTimers[side] !== null) clearTimeout(seekBadgeTimers[side]);
                        seekBadgeTimers[side] = setTimeout(function () {
                            el.classList.remove('visible');
                            if (tapAccum.side === side) tapAccum.sec = 0;
                        }, 650);
                    }

                    function safePlay() {
                        try {
                            var p = v.play();
                            if (p && typeof p.catch === 'function') {
                                p.catch(function () {
                                    updatePlayUi();
                                    showChrome();
                                });
                            }
                        } catch (err) {
                            updatePlayUi();
                            showChrome();
                        }
                    }

                    function togglePlay() {
                        if (v.paused || v.ended) safePlay();
                        else v.pause();
                        showChrome();
                    }

                    function seekTo(sec) {
                        if (!hasDuration()) return;
                        var target = clamp(sec, 0, v.duration);
                        try { v.currentTime = target; } catch (err) { }
                        updateScrubDisplay(target);
                        updateMediaSessionState();
                    }

                    function seekBy(deltaSec) {
                        var dur = hasDuration() ? v.duration : Infinity;
                        var target = clamp((v.currentTime || 0) + deltaSec, 0, dur);
                        try { v.currentTime = target; } catch (err) { }
                        updateScrubDisplay(target);
                        updateMediaSessionState();
                    }

                    function safeGetVolume() {
                        try { return typeof v.volume === 'number' ? v.volume : 1; }
                        catch (err) { return 1; }
                    }

                    function stepVolume(delta) {
                        var cur = safeGetVolume();
                        var next = clamp(cur + delta, 0, 1);
                        try {
                            v.volume = next;
                            if (next > 0 && v.muted) v.muted = false;
                        } catch (err) { }
                        updateMuteUi();
                        showHud('Volume ' + Math.round(next * 100) + '%', 900);
                        showChrome();
                    }

                    function toggleMute() {
                        try { v.muted = !v.muted; } catch (err) { }
                        updateMuteUi();
                        showHud(v.muted ? 'Muted' : 'Volume ' + Math.round(safeGetVolume() * 100) + '%', 900);
                        showChrome();
                    }

                    function cycleSpeed() {
                        speedIndex = (speedIndex + 1) % speeds.length;
                        var rate = speeds[speedIndex];
                        v.playbackRate = rate;
                        btnSpeed.textContent = rate + 'x';
                        showHud(rate + 'x', 800);
                        updateMediaSessionState();
                        showChrome();
                    }

                    function updateFullscreenUi() {
                        var fs = !!(document.fullscreenElement || document.webkitFullscreenElement);
                        icoFsEnter.classList.toggle('hidden', fs);
                        icoFsExit.classList.toggle('hidden', !fs);
                    }

                    function toggleFullscreen() {
                        showChrome();
                        try {
                            if (document.fullscreenElement || document.webkitFullscreenElement) {
                                if (document.exitFullscreen) document.exitFullscreen().catch(function () { });
                                else if (document.webkitExitFullscreen) document.webkitExitFullscreen();
                                return;
                            }
                            if (player.requestFullscreen) {
                                player.requestFullscreen().catch(function () {
                                    if (typeof v.webkitEnterFullscreen === 'function') v.webkitEnterFullscreen();
                                });
                            } else if (player.webkitRequestFullscreen) {
                                player.webkitRequestFullscreen();
                            } else if (typeof v.webkitEnterFullscreen === 'function') {
                                v.webkitEnterFullscreen();
                            }
                        } catch (err) {
                            try {
                                if (typeof v.webkitEnterFullscreen === 'function') v.webkitEnterFullscreen();
                            } catch (e2) { }
                        }
                    }

                    function updatePlayUi() {
                        var playing = !v.paused && !v.ended;
                        player.classList.toggle('is-playing', playing);
                        icoPlay.classList.toggle('hidden', playing);
                        icoPause.classList.toggle('hidden', !playing);
                        btnPlay.setAttribute('aria-label', playing ? 'Pause' : 'Play');
                        if (!playing) showChrome();
                        else scheduleHide();
                    }

                    function updateMuteUi() {
                        var muted = v.muted || safeGetVolume() === 0;
                        icoVolOn.classList.toggle('hidden', muted);
                        icoVolOff.classList.toggle('hidden', !muted);
                        btnMute.setAttribute('aria-label', muted ? 'Unmute' : 'Mute');
                    }

                    function bufferedAhead() {
                        try {
                            var b = v.buffered;
                            var cur = v.currentTime || 0;
                            for (var i = 0; i < b.length; i++) {
                                if (b.start(i) <= cur + 0.15 && b.end(i) >= cur) {
                                    return Math.max(0, b.end(i) - cur);
                                }
                            }
                        } catch (err) { }
                        return 0;
                    }

                    function updateBufferBar() {
                        if (!hasDuration()) {
                            bufFill.style.width = '0%';
                            return;
                        }
                        try {
                            var b = v.buffered;
                            var cur = v.currentTime || 0;
                            var end = 0;
                            for (var i = 0; i < b.length; i++) {
                                if (b.start(i) <= cur + 0.25 && b.end(i) > end) end = b.end(i);
                            }
                            if (end === 0 && b.length > 0) end = b.end(b.length - 1);
                            bufFill.style.width = clamp((end / v.duration) * 100, 0, 100) + '%';
                        } catch (err) { }
                    }

                    function updateScrubDisplay(timeSec) {
                        var dur = hasDuration() ? v.duration : 0;
                        var pct = dur > 0 ? clamp((timeSec / dur) * 100, 0, 100) : 0;
                        playFill.style.width = pct + '%';
                        scrubThumb.style.left = pct + '%';
                        scrubTip.style.left = clamp(pct, 6, 94) + '%';
                        scrubTip.textContent = fmtTime(timeSec);
                        tCur.textContent = fmtTime(timeSec);
                        tDur.textContent = dur > 0 ? fmtTime(dur) : '--:--';
                        scrubber.setAttribute('aria-valuemax', dur > 0 ? String(Math.round(dur)) : '0');
                        scrubber.setAttribute('aria-valuenow', String(Math.round(timeSec)));
                    }

                    function updateConnectionQuality() {
                        var ahead = bufferedAhead();
                        var state = 'good';
                        var label = 'Good';

                        if (navigator.onLine === false || pingFailures >= 2) {
                            state = 'offline';
                            label = 'Offline';
                        } else if (pingFailures === 1 || (isBuffering && ahead < 1.5) || (lastRtt !== null && lastRtt >= 250)) {
                            state = 'poor';
                            label = 'Poor';
                        } else if (lastRtt === null) {
                            state = isBuffering ? 'poor' : 'good';
                            label = isBuffering ? 'Poor' : 'Good';
                        } else if (lastRtt <= 35 && !isBuffering && (ahead >= 2 || v.readyState >= 3 || v.paused)) {
                            state = 'excellent';
                            label = 'Excellent';
                        } else if (lastRtt <= 100) {
                            state = 'good';
                            label = 'Good';
                        } else {
                            state = 'fair';
                            label = 'Fair';
                        }

                        var text = state === 'offline' || lastRtt === null ? label : (label + ' \u00B7 ' + lastRtt + ' ms');
                        connBadge.dataset.state = state;
                        connText.textContent = text;
                        connBadge.setAttribute('aria-label', 'Connection: ' + text);
                    }

                    function measurePing() {
                        if (navigator.onLine === false) {
                            updateConnectionQuality();
                            return;
                        }
                        if (pingInFlight) return;
                        pingInFlight = true;
                        var started = performance.now();
                        fetch('/ping?t=' + encodeURIComponent(token), { cache: 'no-store' })
                            .then(function (res) {
                                if (!res.ok) throw new Error('ping');
                                lastRtt = Math.max(1, Math.round(performance.now() - started));
                                pingFailures = 0;
                                updateConnectionQuality();
                            })
                            .catch(function () {
                                pingFailures += 1;
                                updateConnectionQuality();
                            })
                            .then(function () {
                                pingInFlight = false;
                            });
                    }

                    function setupMediaSession() {
                        if (!('mediaSession' in navigator)) return;
                        var ms = navigator.mediaSession;
                        try {
                            if (typeof MediaMetadata === 'function') {
                                ms.metadata = new MediaMetadata({
                                    title: pageTitle,
                                    artist: 'Aether',
                                    artwork: [{ src: '/favicon.svg', sizes: 'any', type: 'image/svg+xml' }]
                                });
                            }
                        } catch (err) { }

                        var actions = {
                            play: function () { safePlay(); },
                            pause: function () { v.pause(); },
                            seekbackward: function (d) { seekBy(-(d && d.seekOffset ? d.seekOffset : 10)); },
                            seekforward: function (d) { seekBy(d && d.seekOffset ? d.seekOffset : 10); },
                            seekto: function (d) {
                                if (!d || typeof d.seekTime !== 'number') return;
                                if (d.fastSeek && typeof v.fastSeek === 'function') {
                                    try { v.fastSeek(d.seekTime); return; } catch (err) { }
                                }
                                seekTo(d.seekTime);
                            },
                            stop: function () {
                                v.pause();
                                seekTo(0);
                            }
                        };
                        Object.keys(actions).forEach(function (name) {
                            try { ms.setActionHandler(name, actions[name]); } catch (err) { }
                        });
                    }

                    function updateMediaSessionState() {
                        if (!('mediaSession' in navigator)) return;
                        var ms = navigator.mediaSession;
                        try {
                            ms.playbackState = v.paused ? 'paused' : 'playing';
                        } catch (err) { }
                        if (typeof ms.setPositionState === 'function' && hasDuration()) {
                            try {
                                ms.setPositionState({
                                    duration: v.duration,
                                    playbackRate: v.playbackRate || 1,
                                    position: clamp(v.currentTime || 0, 0, v.duration)
                                });
                            } catch (err) { }
                        }
                    }

                    function scrubFraction(clientX) {
                        var rect = scrubber.getBoundingClientRect();
                        return rect.width > 0 ? clamp((clientX - rect.left) / rect.width, 0, 1) : 0;
                    }

                    scrubber.addEventListener('pointerdown', function (e) {
                        if (e.pointerType === 'mouse' && e.button !== 0) return;
                        e.stopPropagation();
                        scrubbing = true;
                        scrubber.classList.add('dragging');
                        showChrome();
                        try { scrubber.setPointerCapture(e.pointerId); } catch (err) { }
                        var frac = scrubFraction(e.clientX);
                        scrubTarget = hasDuration() ? frac * v.duration : 0;
                        updateScrubDisplay(scrubTarget);
                    });

                    scrubber.addEventListener('pointermove', function (e) {
                        if (!scrubbing) return;
                        var frac = scrubFraction(e.clientX);
                        scrubTarget = hasDuration() ? frac * v.duration : 0;
                        updateScrubDisplay(scrubTarget);
                    });

                    function finishScrub(commit) {
                        if (!scrubbing) return;
                        scrubbing = false;
                        scrubber.classList.remove('dragging');
                        if (commit && hasDuration()) seekTo(scrubTarget);
                        scheduleHide();
                    }

                    scrubber.addEventListener('pointerup', function () { finishScrub(true); });
                    scrubber.addEventListener('pointercancel', function () { finishScrub(false); });

                    function resetGesture() {
                        activePointerId = null;
                        gestureMode = 'none';
                        gestureRect = null;
                    }

                    gestureLayer.addEventListener('pointerdown', function (e) {
                        if (e.pointerType === 'mouse' && e.button !== 0) return;
                        if (activePointerId !== null) {
                            resetGesture();
                            return;
                        }
                        activePointerId = e.pointerId;
                        gestureMode = 'pending';
                        startX = e.clientX;
                        startY = e.clientY;
                        gestureRect = gestureLayer.getBoundingClientRect();
                        try { gestureLayer.setPointerCapture(e.pointerId); } catch (err) { }
                    });

                    gestureLayer.addEventListener('pointermove', function (e) {
                        if (e.pointerId !== activePointerId || gestureMode === 'none' || gestureMode === 'ignored') return;
                        var dx = e.clientX - startX;
                        var dy = e.clientY - startY;
                        var rect = gestureRect || gestureLayer.getBoundingClientRect();

                        if (gestureMode === 'pending') {
                            if (Math.hypot(dx, dy) < 12) return;
                            if (singleTapTimer !== null) {
                                clearTimeout(singleTapTimer);
                                singleTapTimer = null;
                            }
                            if (Math.abs(dx) > Math.abs(dy) * 1.25) {
                                if (!hasDuration()) {
                                    gestureMode = 'ignored';
                                    return;
                                }
                                gestureMode = 'seek';
                                dragBaseTime = v.currentTime || 0;
                                dragTargetTime = dragBaseTime;
                                showChrome();
                            } else if (Math.abs(dy) > Math.abs(dx) * 1.25) {
                                if (startX >= rect.left + rect.width * 0.5) {
                                    gestureMode = 'volume';
                                    dragBaseVolume = safeGetVolume();
                                } else {
                                    gestureMode = 'ignored';
                                    return;
                                }
                            } else {
                                return;
                            }
                        }

                        if (gestureMode === 'seek') {
                            var spanSec = Math.min(120, Math.max(15, v.duration * 0.6));
                            var deltaSec = (dx / Math.max(rect.width, 1)) * spanSec;
                            dragTargetTime = clamp(dragBaseTime + deltaSec, 0, v.duration);
                            updateScrubDisplay(dragTargetTime);
                            showHud(fmtTime(dragTargetTime) + ' (' + fmtDelta(dragTargetTime - dragBaseTime) + ')', 0);
                        } else if (gestureMode === 'volume') {
                            var spanPx = Math.min(260, Math.max(140, rect.height * 0.55));
                            var newVol = clamp(dragBaseVolume - dy / spanPx, 0, 1);
                            try {
                                v.volume = newVol;
                                if (newVol > 0 && v.muted) v.muted = false;
                            } catch (err) { }
                            updateMuteUi();
                            showHud('Volume ' + Math.round(newVol * 100) + '%', 0);
                        }
                    });

                    gestureLayer.addEventListener('pointerup', function (e) {
                        if (e.pointerId !== activePointerId) return;
                        var mode = gestureMode;
                        var rect = gestureRect || gestureLayer.getBoundingClientRect();
                        resetGesture();

                        if (mode === 'seek') {
                            seekTo(dragTargetTime);
                            hideHudSoon(700);
                            scheduleHide();
                            return;
                        }
                        if (mode === 'volume') {
                            hideHudSoon(700);
                            scheduleHide();
                            return;
                        }
                        if (mode !== 'pending') return;

                        var relX = (e.clientX - rect.left) / Math.max(rect.width, 1);
                        var side = relX < 0.333 ? 'left' : (relX > 0.667 ? 'right' : 'center');
                        var now = Date.now();

                        if (side !== 'center' && tapAccum.side === side && (now - tapAccum.lastAt) < 650 && tapAccum.sec !== 0) {
                            if (singleTapTimer !== null) {
                                clearTimeout(singleTapTimer);
                                singleTapTimer = null;
                            }
                            var delta = side === 'left' ? -10 : 10;
                            seekBy(delta);
                            showSeekBadge(side, delta);
                            lastTap = { time: now, side: side };
                            return;
                        }

                        if (side !== 'center' && lastTap.side === side && (now - lastTap.time) < 280) {
                            if (singleTapTimer !== null) {
                                clearTimeout(singleTapTimer);
                                singleTapTimer = null;
                            }
                            var d2 = side === 'left' ? -10 : 10;
                            seekBy(d2);
                            showSeekBadge(side, d2);
                            lastTap = { time: 0, side: null };
                            return;
                        }

                        lastTap = { time: now, side: side };
                        if (side === 'center') {
                            toggleChrome();
                        } else {
                            if (singleTapTimer !== null) clearTimeout(singleTapTimer);
                            singleTapTimer = setTimeout(function () {
                                singleTapTimer = null;
                                toggleChrome();
                            }, 260);
                        }
                    });

                    gestureLayer.addEventListener('pointercancel', function (e) {
                        if (e.pointerId !== activePointerId) return;
                        var mode = gestureMode;
                        resetGesture();
                        if (mode === 'seek' || mode === 'volume') {
                            if (mode === 'seek' && !scrubbing) updateScrubDisplay(v.currentTime || 0);
                            hideHudSoon(400);
                            scheduleHide();
                        }
                    });

                    centerPlay.addEventListener('click', function (e) { e.stopPropagation(); togglePlay(); });
                    btnPlay.addEventListener('click', function (e) { e.stopPropagation(); togglePlay(); });
                    btnBack.addEventListener('click', function (e) { e.stopPropagation(); seekBy(-10); showSeekBadge('left', -10); showChrome(); });
                    btnFwd.addEventListener('click', function (e) { e.stopPropagation(); seekBy(10); showSeekBadge('right', 10); showChrome(); });
                    btnMute.addEventListener('click', function (e) { e.stopPropagation(); toggleMute(); });
                    btnSpeed.addEventListener('click', function (e) { e.stopPropagation(); cycleSpeed(); });
                    btnFs.addEventListener('click', function (e) { e.stopPropagation(); toggleFullscreen(); });

                    document.addEventListener('keydown', function (e) {
                        if (e.metaKey || e.ctrlKey || e.altKey) return;
                        var tag = e.target && e.target.tagName;
                        if (tag === 'INPUT' || tag === 'TEXTAREA') return;
                        switch (e.key) {
                            case ' ':
                            case 'k':
                            case 'K':
                                e.preventDefault();
                                togglePlay();
                                break;
                            case 'ArrowLeft':
                                e.preventDefault();
                                seekBy(-10);
                                showSeekBadge('left', -10);
                                showChrome();
                                break;
                            case 'ArrowRight':
                                e.preventDefault();
                                seekBy(10);
                                showSeekBadge('right', 10);
                                showChrome();
                                break;
                            case 'ArrowUp':
                                e.preventDefault();
                                stepVolume(0.1);
                                break;
                            case 'ArrowDown':
                                e.preventDefault();
                                stepVolume(-0.1);
                                break;
                            case 'm':
                            case 'M':
                                e.preventDefault();
                                toggleMute();
                                break;
                            case 'f':
                            case 'F':
                                e.preventDefault();
                                toggleFullscreen();
                                break;
                        }
                    });

                    document.addEventListener('fullscreenchange', updateFullscreenUi);
                    document.addEventListener('webkitfullscreenchange', updateFullscreenUi);

                    v.addEventListener('error', function () {
                        var e = v.error;
                        if (e && e.code === 2) fail('The connection to the PC dropped. Check that your phone is on the same Wi-Fi and that Aether is still streaming.');
                        else fail('The video uses a format this browser can\'t decode, often HEVC (H.265) video or an unusual sound track.');
                    });

                    v.addEventListener('loadedmetadata', function () {
                        if (v.videoWidth === 0) {
                            fail('The picture in this file uses a format this browser can\'t decode, often HEVC (H.265).');
                            return;
                        }
                        if (!scrubbing && gestureMode !== 'seek') updateScrubDisplay(v.currentTime || 0);
                        updateBufferBar();
                        updateMediaSessionState();
                    });

                    v.addEventListener('durationchange', function () {
                        if (!scrubbing && gestureMode !== 'seek') updateScrubDisplay(v.currentTime || 0);
                        updateMediaSessionState();
                    });

                    v.addEventListener('timeupdate', function () {
                        if (!scrubbing && gestureMode !== 'seek') updateScrubDisplay(v.currentTime || 0);
                        if (!v.paused && v.readyState >= 3 && isBuffering) {
                            isBuffering = false;
                            updateConnectionQuality();
                        }
                        updateBufferBar();
                    });

                    v.addEventListener('progress', function () {
                        updateBufferBar();
                        updateConnectionQuality();
                    });

                    v.addEventListener('play', function () { updatePlayUi(); updateMediaSessionState(); });
                    v.addEventListener('playing', function () { isBuffering = false; updatePlayUi(); updateConnectionQuality(); updateMediaSessionState(); });
                    v.addEventListener('pause', function () { updatePlayUi(); updateMediaSessionState(); });
                    v.addEventListener('ended', function () { updatePlayUi(); updateMediaSessionState(); });
                    v.addEventListener('seeked', updateMediaSessionState);
                    v.addEventListener('ratechange', updateMediaSessionState);
                    v.addEventListener('volumechange', updateMuteUi);
                    v.addEventListener('waiting', function () { isBuffering = true; updateConnectionQuality(); });
                    v.addEventListener('stalled', function () { isBuffering = true; updateConnectionQuality(); });
                    v.addEventListener('canplay', function () { isBuffering = false; updateConnectionQuality(); });

                    window.addEventListener('online', function () { pingFailures = 0; measurePing(); updateConnectionQuality(); });
                    window.addEventListener('offline', function () { updateConnectionQuality(); });
                    document.addEventListener('visibilitychange', function () {
                        if (document.visibilityState === 'visible') measurePing();
                    });

                    setupMediaSession();
                    updatePlayUi();
                    updateMuteUi();
                    updateFullscreenUi();
                    updateScrubDisplay(0);
                    updateConnectionQuality();
                    measurePing();
                    pingTimer = setInterval(function () {
                        if (document.visibilityState === 'visible') measurePing();
                    }, 4000);

                    v.src = '/stream/video?t=' + encodeURIComponent(token);
                })();
            </script>
        </body>
        </html>
        """;
    }
}
