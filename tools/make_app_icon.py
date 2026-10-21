"""Renders the Aether icon: a cut-chrome four-point star, with a soft glow behind it at large sizes.

Two themes: "dark" is the pale star for dark backgrounds (the default, and what the exe and shortcuts
use), "light" is the graphite star for light backgrounds. The glow is dark-theme only; on light
backgrounds it reads as grey haze. Sizes below 48 px drop the glow and enlarge the star so it stays
crisp in the taskbar and title bar.

Writes:
  src/Aether.App/Assets/AppIcon.ico, AppIcon.light.ico
  docs/assets/aether-icon.png, aether-icon-light.png, aether-icon.svg (follows the viewer's theme)
  src/Aether.Server.LanHost/favicon.svg (the same SVG, the phone watch page's tab icon)

Usage: python tools/make_app_icon.py
"""
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
SS = 8  # supersampling factor
ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
SMALL = 48  # below this, no glow and a larger star

# Facet shades, lightest first; FACET_ORDER maps the eight facets (clockwise from the top arm's
# left half) to a shade, so faces toward the top left catch the light.
SHADES = {
    "dark": ["#ffffff", "#c9d3ec", "#eef2ff", "#8e9bbd", "#dfe6fa", "#a7b3d4", "#b9c4e3", "#6f7c9f"],
    "light": ["#6b7389", "#2b3142", "#545c72", "#1a1f2c", "#4a5268", "#232838", "#3c4458", "#141822"],
}
FACET_ORDER = [0, 1, 2, 3, 7, 5, 6, 4]
GLOW = ("#9fb4ff", 0.5)
THICKNESS = 0.09  # arm width as a fraction of the radius


def geometry(size):
    """Star radius and glow radius on a 256 grid for an icon drawn at `size` px."""
    return (120, None) if size < SMALL else (104, 124)


def star_segments(cx, cy, r, k=THICKNESS):
    """The star as four cubic curves, tip to tip, clockwise from the top."""
    a, b = r * k, r * 0.42
    return [
        ((cx, cy - r), (cx + a, cy - b), (cx + b, cy - a), (cx + r, cy)),
        ((cx + r, cy), (cx + b, cy + a), (cx + a, cy + b), (cx, cy + r)),
        ((cx, cy + r), (cx - a, cy + b), (cx - b, cy + a), (cx - r, cy)),
        ((cx - r, cy), (cx - b, cy - a), (cx - a, cy - b), (cx, cy - r)),
    ]


def star_polygon(cx, cy, r, steps=64):
    pts = []
    for p0, p1, p2, p3 in star_segments(cx, cy, r):
        for i in range(steps):
            t = i / steps
            u = 1 - t
            pts.append(tuple(u**3 * p0[j] + 3 * u * u * t * p1[j] + 3 * u * t * t * p2[j] + t**3 * p3[j] for j in (0, 1)))
    return pts


def facets(cx, cy, r):
    R = r * 1.2
    return [
        [(cx, cy), (cx, cy - R), (cx - R, cy - R)], [(cx, cy), (cx, cy - R), (cx + R, cy - R)],
        [(cx, cy), (cx + R, cy - R), (cx + R, cy)], [(cx, cy), (cx + R, cy), (cx + R, cy + R)],
        [(cx, cy), (cx + R, cy + R), (cx, cy + R)], [(cx, cy), (cx, cy + R), (cx - R, cy + R)],
        [(cx, cy), (cx - R, cy + R), (cx - R, cy)], [(cx, cy), (cx - R, cy), (cx - R, cy - R)],
    ]


def rgb(hex_colour):
    return tuple(int(hex_colour[i:i + 2], 16) for i in (1, 3, 5))


def render(size, theme):
    W = size * SS
    s = W / 256
    r, glow_r = geometry(size)
    out = Image.new("RGBA", (W, W), (0, 0, 0, 0))

    if theme == "dark" and glow_r:
        # Linear falloff from the centre, like an SVG radial gradient.
        colour, peak = GLOW
        glow = Image.new("L", (256, 256))
        glow.putdata([max(0, int(255 * peak * (1 - ((x - 127.5) ** 2 + (y - 127.5) ** 2) ** 0.5 / glow_r)))
                      for y in range(256) for x in range(256)])
        glow = glow.resize((W, W), Image.BICUBIC)
        layer = Image.new("RGBA", (W, W), rgb(colour) + (0,))
        layer.putalpha(glow)
        out = Image.alpha_composite(out, layer)

    c = 128 * s
    fill = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    d = ImageDraw.Draw(fill)
    for poly, shade in zip(facets(c, c, r * s), FACET_ORDER):
        d.polygon(poly, fill=rgb(SHADES[theme][shade]) + (255,))
    mask = Image.new("L", (W, W), 0)
    ImageDraw.Draw(mask).polygon(star_polygon(c, c, r * s), fill=255)
    fill.putalpha(mask)
    out = Image.alpha_composite(out, fill)
    return out.resize((size, size), Image.LANCZOS)


def save_ico(path, theme):
    frames = [render(n, theme) for n in ICO_SIZES]
    frames[-1].save(path, format="ICO", sizes=[(n, n) for n in ICO_SIZES], append_images=frames[:-1])


def svg():
    """A single SVG that switches between the two themes with the viewer's colour scheme (large-size geometry)."""
    r, glow_r = geometry(256)
    seg = star_segments(128, 128, r)
    path = f"M{seg[0][0][0]:g} {seg[0][0][1]:g}" + "".join(
        f"C{p1[0]:g} {p1[1]:g} {p2[0]:g} {p2[1]:g} {p3[0]:g} {p3[1]:g}" for _, p1, p2, p3 in seg) + "Z"
    dark = "".join(f".f{i}{{fill:{SHADES['dark'][o]}}}" for i, o in enumerate(FACET_ORDER))
    light = "".join(f".f{i}{{fill:{SHADES['light'][o]}}}" for i, o in enumerate(FACET_ORDER))
    polys = "".join(
        f'<polygon class="f{i}" points="{" ".join(f"{x:g},{y:g}" for x, y in poly)}"/>'
        for i, poly in enumerate(facets(128, 128, r)))
    return (
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">'
        f"<style>{dark}@media (prefers-color-scheme:light){{{light}.g{{display:none}}}}</style>"
        f'<defs><radialGradient id="g"><stop offset="0" stop-color="{GLOW[0]}" stop-opacity="{GLOW[1]}"/>'
        f'<stop offset="1" stop-color="{GLOW[0]}" stop-opacity="0"/></radialGradient>'
        f'<clipPath id="s"><path d="{path}"/></clipPath></defs>'
        f'<circle class="g" cx="128" cy="128" r="{glow_r}" fill="url(#g)"/>'
        f'<g clip-path="url(#s)">{polys}</g></svg>\n'
    )


def main():
    assets = ROOT / "src/Aether.App/Assets"
    save_ico(assets / "AppIcon.ico", "dark")
    save_ico(assets / "AppIcon.light.ico", "light")
    docs = ROOT / "docs/assets"
    render(256, "dark").save(docs / "aether-icon.png")
    render(256, "light").save(docs / "aether-icon-light.png")
    for path in (docs / "aether-icon.svg", ROOT / "src/Aether.Server.LanHost/favicon.svg"):
        path.write_text(svg(), encoding="utf-8")


if __name__ == "__main__":
    main()
