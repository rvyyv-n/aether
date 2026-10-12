"""Renders the Aether app icon set into src/Aether.App/Assets.

Usage: python tools/make_app_icon.py [assets_dir]
"""
import math
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

SS = 4  # supersampling factor


def star_points(cx, cy, r, k=2.7, n=160):
    pts = []
    for i in range(n):
        t = 2 * math.pi * i / n
        c, s = math.cos(t), math.sin(t)
        pts.append((cx + r * math.copysign(abs(c) ** k, c), cy + r * math.copysign(abs(s) ** k, s)))
    return pts


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def diagonal_gradient(w, h, stops):
    """stops: list of (pos, rgb); gradient runs top-left to bottom-right."""
    small = Image.new("RGB", (256, 256))
    px = small.load()
    for y in range(256):
        for x in range(256):
            t = (x + y) / 510
            for (p0, c0), (p1, c1) in zip(stops, stops[1:]):
                if p0 <= t <= p1:
                    px[x, y] = lerp(c0, c1, (t - p0) / (p1 - p0))
                    break
    return small.resize((w, h), Image.BILINEAR)


def glyph_layer(w, h, cx, cy, s, mode):
    """Star + satellites in a w*h RGBA layer. s = main star radius in px."""
    small = s < 20 * SS
    k = 2.2 if small else 2.7
    shapes = [(cx, cy, s), (cx + 0.98 * s, cy - 0.92 * s, 0.40 * s), (cx + 1.02 * s, cy + 0.80 * s, 0.22 * s)]
    if small:
        shapes = shapes[:2]
    mask = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(mask)
    for x, y, r in shapes:
        d.polygon(star_points(x, y, r, k), fill=255)

    if mode == "dark":
        stops = [(0, (30, 36, 70)), (0.5, (52, 62, 140)), (1, (92, 70, 170))]
    else:
        stops = [(0, (255, 255, 255)), (0.45, (190, 225, 255)), (1, (185, 160, 255))]
    fill = diagonal_gradient(w, h, stops).convert("RGBA")
    fill.putalpha(mask)

    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    if mode == "glow":
        glow = Image.new("RGBA", (w, h), (120, 170, 255, 0))
        glow.putalpha(mask.filter(ImageFilter.GaussianBlur(s * 0.16)).point(lambda v: int(v * 0.9)))
        out = Image.alpha_composite(out, glow)
        tight = Image.new("RGBA", (w, h), (170, 205, 255, 0))
        tight.putalpha(mask.filter(ImageFilter.GaussianBlur(s * 0.05)).point(lambda v: int(v * 0.6)))
        out = Image.alpha_composite(out, tight)
    return Image.alpha_composite(out, fill)


def background(w, h, radius):
    bg = diagonal_gradient(w, h, [(0, (34, 40, 74)), (0.55, (18, 20, 40)), (1, (30, 18, 52))]).convert("RGBA")
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, w - 1, h - 1), radius=radius, fill=255) if radius else mask.paste(255, (0, 0, w, h))
    # soft top-left sheen for the glass feel
    sheen = Image.new("L", (w, h), 0)
    ImageDraw.Draw(sheen).ellipse((-w * 0.3, -h * 0.9, w * 0.9, h * 0.35), fill=16)
    sheen = sheen.filter(ImageFilter.GaussianBlur(min(w, h) * 0.18))
    white = Image.new("RGBA", (w, h), (255, 255, 255, 0))
    white.putalpha(sheen)
    bg = Image.alpha_composite(bg, white)
    if radius:
        edge = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        ImageDraw.Draw(edge).rounded_rectangle((0, 0, w - 1, h - 1), radius=radius, outline=(255, 255, 255, 38), width=max(1, SS))
        bg = Image.alpha_composite(bg, edge)
    bg.putalpha(mask)
    return bg


def render(w, h, kind):
    """kind: tile (rounded plate), full (full-bleed), glyph (bare, light), glyph_dark (bare, for light themes)."""
    W, H = w * SS, h * SS
    m = min(W, H)
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    if kind == "tile":
        img = background(W, H, int(m * 0.22))
        frac = 0.74
    elif kind == "full":
        img = background(W, H, 0)
        frac = 0.70 if W == H else 0.66
    else:
        frac = 0.80
    cx, cy = W * 0.5 - m * frac * 0.12, H * 0.5 + m * frac * 0.06
    s = m * frac * 0.42
    mode = {"glyph_dark": "dark", "glyph": "plain"}.get(kind, "glow")
    layer = glyph_layer(W, H, cx, cy, s, mode)
    img = Image.alpha_composite(img, layer)
    return img.resize((w, h), Image.LANCZOS)


def main():
    out = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[1] / "src/Aether.App/Assets"
    out.mkdir(parents=True, exist_ok=True)
    files = {
        "Square150x150Logo.scale-200.png": (300, 300, "full"),
        "Square44x44Logo.scale-200.png": (88, 88, "tile"),
        "Square44x44Logo.targetsize-24_altform-unplated.png": (24, 24, "glyph"),
        "Square44x44Logo.targetsize-48_altform-lightunplated.png": (48, 48, "glyph_dark"),
        "StoreLogo.png": (50, 50, "tile"),
        "Wide310x150Logo.scale-200.png": (620, 300, "full"),
        "SplashScreen.scale-200.png": (1240, 600, "full"),
        "LockScreenLogo.scale-200.png": (48, 48, "glyph"),
    }
    for name, (w, h, kind) in files.items():
        render(w, h, kind).save(out / name)
    sizes = [16, 24, 32, 48, 64, 128, 256]
    frames = [render(s, s, "tile") for s in sizes]
    frames[-1].save(out / "AppIcon.ico", format="ICO", sizes=[(s, s) for s in sizes], append_images=frames[:-1])


if __name__ == "__main__":
    main()
