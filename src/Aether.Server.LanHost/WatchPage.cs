namespace Aether.Server.LanHost;

internal static class WatchPage
{
    // Self-contained on purpose: the phone must not need anything beyond this server.
    // canPlayType only knows the container, so a codec the phone can't decode (HEVC in mp4) is caught
    // afterwards: an error event, or metadata that loads with no picture (videoWidth 0).
    public static string Build(string token, string mime) => $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Aether LAN Player</title>
            <link rel="icon" href="/favicon.svg" type="image/svg+xml">
            <style>
                html, body { margin: 0; height: 100%; background: #000; color: #fff; font-family: system-ui, sans-serif; }
                video { width: 100%; height: 100%; object-fit: contain; }
                #notice { display: none; position: fixed; inset: 0; box-sizing: border-box; padding: 24px;
                          flex-direction: column; justify-content: center; background: #111; font-size: 17px; line-height: 1.5; }
                #notice h1 { font-size: 20px; margin: 0 0 12px; }
                #notice p { margin: 0 0 12px; color: #ccc; }
            </style>
        </head>
        <body>
            <video id="v" controls autoplay playsinline></video>
            <div id="notice" role="alert">
                <h1>This phone can't play this video</h1>
                <p id="why"></p>
                <p>Aether doesn't convert videos, so the file has to be one your phone already understands. Try another browser, or watch it on the PC instead.</p>
            </div>
            <script>
                (function () {
                    var v = document.getElementById('v');
                    var notice = document.getElementById('notice');
                    var why = document.getElementById('why');
                    var mime = '{{mime}}';
                    function fail(reason) {
                        if (notice.style.display === 'flex') return;
                        why.textContent = reason;
                        v.pause();
                        v.style.display = 'none';
                        notice.style.display = 'flex';
                    }
                    if (!v.canPlayType || v.canPlayType(mime) === '') {
                        fail('This browser can\'t open this kind of file (' + mime.replace('video/', '').replace('x-', '') + '). Phones usually can\'t play MKV, and some can\'t play HEVC (H.265) video.');
                        return;
                    }
                    v.addEventListener('error', function () {
                        var e = v.error;
                        if (e && e.code === 2) fail('The connection to the PC dropped. Check that your phone is on the same Wi-Fi and that Aether is still streaming.');
                        else fail('The video uses a format this browser can\'t decode, often HEVC (H.265) video or an unusual sound track.');
                    });
                    v.addEventListener('loadedmetadata', function () {
                        if (v.videoWidth === 0) fail('The picture in this file uses a format this browser can\'t decode, often HEVC (H.265).');
                    });
                    v.src = '/stream/video?t={{token}}';
                })();
            </script>
        </body>
        </html>
        """;
}
