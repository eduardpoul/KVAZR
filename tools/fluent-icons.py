#!/usr/bin/env python3
"""
Builds the device icon set the application ships, out of the design files.

There are two sources and they are not equal.

The pack — "Fluent Icons Color" and "Fluent Icons Real" — is the real Fluent System
Icons library: official colour versions where Microsoft publishes them, the rest
re-tinted to the same palette (grey gradient on the body, a saturated accent on the part
that does the work). Every device the application actually meets is drawn there, and
that is what a device now gets.

The old file — "Fluent Device Icons" — is the hand-drawn set that came first, in a tiny
layer DSL. It is kept for the handful of devices the pack has no drawing for at all: a
watch, a webcam, a printer. Replacing those with the nearest pack icon would be a worse
picture than the one that exists, so they stay until the pack grows.

An icon with no colour version falls back to its "real" (monochrome) drawing, painted
with the pack's own body gradient rather than left flat — the shape is the real one, and
the paint is what makes it belong in a row of coloured icons rather than look like a
loading failure. Only the mouse needs this: Fluent has no mouse at all, and the design
drew one to the library's own rules.

Usage:  python tools/fluent-icons.py
"""

import io
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DESIGN = ROOT / "Design" / "kvazr"
COLOUR_FILE = DESIGN / "Fluent Icons Color.dc.html"
REAL_FILE = DESIGN / "Fluent Icons Real.dc.html"
LEGACY_FILE = DESIGN / "Fluent Device Icons.dc.html"

OUT = ROOT / "src" / "Kvazr.App" / "Web" / "Infrastructure" / "FluentIcons.g.cs"
AXAML_OUT = ROOT / "src" / "Kvazr.App" / "Themes" / "FluentIcons.axaml"


# =====================================================================================
# the pack
# =====================================================================================

colour_html = io.open(COLOUR_FILE, encoding="utf-8").read()
real_html = io.open(REAL_FILE, encoding="utf-8").read()

# The gradient table both files paint with, declared once as JSON in the colour file.
GRADS = {
    g["id"]: [(stop[0], stop[1]) for stop in g["stops"]]
    for g in json.loads(re.search(r"const GRADS = (\[.*?\]);", colour_html, re.S).group(1))
}

BODY_GRADIENT = "kv-grey"


def cards(html: str) -> dict[str, str]:
    """
    Every named card in a design file, as name -> the svg drawn on it.

    Found by walking the names and taking the drawing that precedes each, rather than the
    other way round: both files end with unnamed sections — a size grid, a state-colour
    strip — and a drawing-first walk would pair a name with the wrong picture.
    """
    found: dict[str, str] = {}

    for label in re.finditer(r'text-align: center;">([^<]+)</div>', html):
        drawings = re.findall(r'<svg [^>]*viewBox="0 0 24 24"[^>]*>.*?</svg>', html[: label.start()], re.S)
        if drawings:
            found[label.group(1).strip()] = drawings[-1]

    return found


COLOUR = cards(colour_html)
REAL = cards(real_html)

if not COLOUR or not REAL:
    sys.exit("the design files parsed to nothing — the card markup changed")


def kebab(name: str) -> str:
    return name.replace(" ", "-")


def tidy(markup: str, monochrome: bool) -> str:
    """
    The design's drawing as shippable SVG.

    Three things are wrong with it as exported. It is sized for a gallery card, so the
    width and height go and the icon fills whatever box it is put in. Its attributes are
    React's camelCase, which is not SVG — a browser parsing inline HTML lowercases them
    to fillrule, which is not an attribute at all, so the fill rule silently reverts to
    non-zero and every icon with a hole in it fills solid. And the gradients it paints
    with live in one shared <defs> at the top of the gallery page, which does not travel
    with a single icon.
    """
    body = markup[markup.index(">") + 1 : markup.rindex("</svg>")]

    body = body.replace("fillRule=", "fill-rule=").replace("clipRule=", "clip-rule=")

    # The real drawings are painted by the surrounding text colour, which is a flat
    # silhouette next to gradient icons. Given the body gradient instead they read as the
    # same family — the shape is still the library's own.
    if monochrome:
        body = body.replace('fill="currentColor"', f'fill="url(#{BODY_GRADIENT})"')

    used = sorted(set(re.findall(r"url\(#(kv-[a-z0-9]+)\)", body)))
    defs = "".join(
        f'<linearGradient id="{g}" x1="0" y1="0" x2="0.35" y2="1">'
        + "".join(f'<stop offset="{at}" stop-color="{colour}"/>' for at, colour in GRADS[g])
        + "</linearGradient>"
        for g in used)

    return ('<svg width="100%" height="100%" viewBox="0 0 24 24" fill="none" '
            f'xmlns="http://www.w3.org/2000/svg"><defs>{defs}</defs>{body.strip()}</svg>')


PACK: dict[str, str] = {}

for name in sorted(set(COLOUR) | set(REAL)):
    if name in COLOUR:
        PACK[kebab(name)] = tidy(COLOUR[name], monochrome=False)
    else:
        PACK[kebab(name)] = tidy(REAL[name], monochrome=True)


# =====================================================================================
# which device wears which icon
# =====================================================================================

# The application's keys are finer than the pack, because the hand-drawn set could afford
# to be: somebody drew a sixty-per-cent keyboard and a tenkeyless one separately. The
# library has one keyboard, and pretending otherwise would be precision we do not have.
# So several keys land on the same drawing, and that is the honest outcome rather than a
# shortcut.
ALIAS = {
    # worn on the head
    "headphones-wired": "headphones-sound-wave",
    "earbuds": "surface-earbuds",
    "earbuds-case": "surface-earbuds",
    "earphones-wired": "headphones-sound-wave",
    "neckband": "surface-earbuds",

    # heard across the room
    "speaker": "speaker-2",
    "smart-speaker": "speaker-bluetooth",
    "soundbar": "speaker-2",
    "mic-studio": "mic",

    # typed on
    "keyboard-full": "keyboard",
    "keyboard-tkl": "keyboard",
    "keyboard-compact": "keyboard",
    "keyboard-60": "keyboard",
    "keyboard-split": "keyboard",
    "numpad": "keyboard",

    # pushed around
    "mouse-gaming": "mouse",
    "mouse-ergo": "mouse",
    "trackball": "mouse",
    "trackpad": "mouse",

    # played with. Every pad the pack draws is the Xbox layout, which is also the layout
    # every one of these speaks to Windows through, so the picture is not a lie even for
    # a Switch pad. The generic one gets the games badge instead, because a pad we cannot
    # name is better shown as "something you play with" than as somebody's controller.
    "gamepad-xbox": "xbox-controller",
    "gamepad-ps": "xbox-controller",
    "gamepad-switch": "xbox-controller",
    "gamepad-generic": "games",
    "racing-wheel": "games",
    "flight-stick": "games",
    "console": "xbox-console",

    # plugged in
    "dongle": "usb-stick",
    "receiver": "usb-plug",

    # and the one that stands for a device nobody has identified: two plugs mated, which
    # says the only thing actually known about it.
    "unknown": "plug-connected",
}

# Keys the pack has no drawing for at all. These keep the hand-drawn originals — the
# nearest pack icon would be a worse picture than the one already there.
LEGACY_KEYS = [
    "stylus", "graphics-tablet", "webcam",
    "phone", "tablet", "laptop", "monitor", "watch", "band", "vr", "printer",
]


# =====================================================================================
# the hand-drawn originals, for the keys the pack cannot dress
# =====================================================================================

legacy_html = io.open(LEGACY_FILE, encoding="utf-8").read()


def js_block(name: str) -> str:
    at = legacy_html.find(f"const {name}")
    if at < 0:
        sys.exit(f"missing: const {name}")
    return legacy_html[at : legacy_html.find("\n};", at) + 3]


palette: dict[str, tuple[str, str]] = {}
for m in re.finditer(r"(\w+): \['(#[0-9A-Fa-f]{6})', '(#[0-9A-Fa-f]{6})'\]", js_block("P")):
    palette[m.group(1)] = (m.group(2), m.group(3))

PAD = re.search(r"const PAD = '([^']+)'", legacy_html).group(1)
PADGLOSS = re.search(r"const PADGLOSS = '([^']+)'", legacy_html).group(1)


def W(o):
    return f"rgba(255,255,255,{o})"


def K(o):
    return f"rgba(0,0,0,{o})"


def keys(y, x0, n, w, h, gap, o=0.85):
    return [["r", x0 + i * (w + gap), y, w, h, 1, W(o)] for i in range(n)]


def kbBase(x, w):
    return ["r", x, 15, w, 21, 5, "kbB"]


def kbPlate(x, w):
    return ["p", f"M{x + 5} 15h{w - 10}a5 5 0 0 1 5 5v11H{x}V20a5 5 0 0 1 5-5Z", "kbT"]


icons_src = js_block("ICONS")
icons_src = icons_src[icons_src.find("{") : icons_src.rfind("}") + 1]

# JS object literal -> Python: quote the bare keys, spread -> unpack.
icons_src = re.sub(r"^(\s*)'?([a-z0-9-]+)'?: \{", r'\1"\2": {', icons_src, flags=re.M)
icons_src = icons_src.replace("{g:", '{"g":').replace(" label:", ' "label":').replace(" l: [", ' "l": [')
icons_src = icons_src.replace("...keys(", "*keys(").replace("...", "*")
icons_src = re.sub(r"//[^\n]*", "", icons_src)
icons_src = icons_src.replace(".concat(", " + (")

LEGACY_ICONS = eval(icons_src, {  # noqa: S307 - the input is our own design file
    "W": W, "K": K, "PAD": PAD, "PADGLOSS": PADGLOSS,
    "keys": keys, "kbBase": kbBase, "kbPlate": kbPlate,
})

missing = [k for k in LEGACY_KEYS if k not in LEGACY_ICONS]
if missing:
    sys.exit(f"the old design file no longer draws: {', '.join(missing)}")


def paint(value: str, prefix: str, used: set[str]) -> str:
    if isinstance(value, str) and value in palette:
        used.add(value)
        return f"url(#{prefix}-{value})"
    return "#FFFFFF" if value == "white" else value


def legacy_svg(key: str, layers: list) -> str:
    prefix = "fi-" + key
    used: set[str] = set()
    body: list[str] = []

    for el in layers:
        kind = el[0]
        if kind == "r":
            _, x, y, w, h, rx, fill = el
            body.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{rx}" fill="{paint(fill, prefix, used)}"/>')
        elif kind == "c":
            _, cx, cy, r, fill = el
            body.append(f'<circle cx="{cx}" cy="{cy}" r="{r}" fill="{paint(fill, prefix, used)}"/>')
        elif kind == "e":
            _, cx, cy, rx, ry, fill = el
            body.append(f'<ellipse cx="{cx}" cy="{cy}" rx="{rx}" ry="{ry}" fill="{paint(fill, prefix, used)}"/>')
        elif kind == "p" and len(el) > 3 and isinstance(el[3], str) and el[3].startswith("stroke:"):
            _, d, _, meta = el
            _, grad, width = meta.split(":")
            body.append(
                f'<path d="{d}" fill="none" stroke="{paint(grad, prefix, used)}" '
                f'stroke-width="{width}" stroke-linecap="round" stroke-linejoin="round"/>')
        else:
            _, d, fill = el[:3]
            body.append(f'<path d="{d}" fill="{paint(fill, prefix, used)}"/>')

    defs = "".join(
        f'<linearGradient id="{prefix}-{g}" x1="0" y1="0" x2="0.35" y2="1">'
        f'<stop offset="0" stop-color="{palette[g][0]}"/><stop offset="1" stop-color="{palette[g][1]}"/></linearGradient>'
        for g in sorted(used))

    return ('<svg width="100%" height="100%" viewBox="0 0 48 48" fill="none" '
            f'xmlns="http://www.w3.org/2000/svg"><defs>{defs}</defs>{"".join(body)}</svg>')


# =====================================================================================
# the finished set
# =====================================================================================

ICONS: dict[str, str] = {}

# The pack under its own names first, so the rest of the interface can reach a battery,
# a bluetooth mark or the settings cog without another table.
ICONS.update(PACK)

for key, target in ALIAS.items():
    if target not in PACK:
        sys.exit(f"alias {key} points at {target}, which the pack does not draw")
    ICONS[key] = PACK[target]

for key in LEGACY_KEYS:
    ICONS[key] = legacy_svg(key, LEGACY_ICONS[key]["l"])

if "unknown" not in ICONS:
    sys.exit("nothing to show for an unidentified device")


lines = [
    "// <auto-generated>",
    "//   Из Design/kvazr/Fluent Icons Color.dc.html и Fluent Icons Real.dc.html,",
    "//   плюс Fluent Device Icons.dc.html для того, чего в паке нет — tools/fluent-icons.py.",
    "//   НЕ ПРАВИТЬ РУКАМИ: перерисовали иконку в дизайне — перезапустить скрипт.",
    "// </auto-generated>",
    "#nullable enable",
    "",
    "namespace Kvazr.App.Web;",
    "",
    "/// <summary>The Fluent icons, exactly as the design draws them.</summary>",
    "public static class FluentIcons",
    "{",
    "    /// <summary>The icon for a key, or the unknown plate when the key is not in the set.</summary>",
    '    public static string Svg(string key) => Icons.TryGetValue(key, out string? svg) ? svg : Icons["unknown"];',
    "",
    "    public static bool Has(string key) => Icons.ContainsKey(key);",
    "",
    "    private static readonly Dictionary<string, string> Icons = new(System.StringComparer.Ordinal)",
    "    {",
]

for key, markup in ICONS.items():
    lines.append(f'        ["{key}"] = @"{markup.replace(chr(34), chr(34) * 2)}",')

lines += ["    };", "}", ""]

OUT.parent.mkdir(exist_ok=True)
OUT.write_text("\n".join(lines), encoding="utf-8")
print(f"wrote {OUT.relative_to(ROOT)} — {len(PACK)} из пака, {len(ALIAS)} псевдонимов, {len(LEGACY_KEYS)} старых")


# =====================================================================================
# the same drawings for Avalonia
# =====================================================================================
# The shell — the rail, the flyout, the native fallback views — draws the same set, so
# both stacks stay one design. Emitted as DrawingImage resources keyed "Fi.<name>".

def argb(colour: str) -> str:
    if colour.startswith("rgba("):
        r, g, b, a = colour[5:-1].split(",")
        return f"#{round(float(a) * 255):02X}{int(r):02X}{int(g):02X}{int(b):02X}"
    return colour


def gradient_xml(stops: list[tuple], indent: str) -> str:
    inner = "\n".join(f'{indent}  <GradientStop Offset="{at}" Color="{colour}"/>' for at, colour in stops)
    return (f'{indent}<LinearGradientBrush StartPoint="0%,0%" EndPoint="35%,100%">\n'
            f"{inner}\n"
            f"{indent}</LinearGradientBrush>")


def brush_xml(value: str, indent: str) -> str:
    """A gradient pair from either palette, or a flat colour."""
    hit = re.fullmatch(r"url\(#(?:fi-[a-z0-9-]+-)?([A-Za-z0-9-]+)\)", value)
    if hit:
        name = hit.group(1)
        if name in GRADS:
            return gradient_xml(GRADS[name], indent)
        if name in palette:
            return gradient_xml([(0, palette[name][0]), (1, palette[name][1])], indent)
    return f'{indent}<SolidColorBrush Color="{argb(value)}"/>'


def legacy_geometry(el) -> str:
    kind = el[0]
    if kind == "r":
        _, x, y, w, h, rx, _ = el
        return f'<RectangleGeometry Rect="{x},{y},{w},{h}" RadiusX="{rx}" RadiusY="{rx}"/>'
    if kind == "c":
        _, cx, cy, r, _ = el
        return f'<EllipseGeometry Center="{cx},{cy}" RadiusX="{r}" RadiusY="{r}"/>'
    if kind == "e":
        _, cx, cy, rx, ry, _ = el
        return f'<EllipseGeometry Center="{cx},{cy}" RadiusX="{rx}" RadiusY="{ry}"/>'
    return f'<PathGeometry Figures="{el[1]}"/>'


def attr(tag: str, name: str) -> str | None:
    hit = re.search(rf'\s{name}="([^"]*)"', tag)
    return hit.group(1) if hit else None


def pack_drawings(markup: str, out: list[str]) -> None:
    """
    The pack's SVG, element by element, as Avalonia geometry drawings.

    The fill rule is carried across rather than defaulted. Fluent draws a shape and the
    hole inside it as two figures of one path, and under the non-zero rule the hole fills
    in — a battery becomes a brick, a keyboard a slab.
    """
    body = markup[markup.index("</defs>") + len("</defs>") :]

    for tag in re.findall(r"<(?:path|rect)\b[^>]*/?>", body):
        fill = attr(tag, "fill")
        if fill in (None, "none"):
            continue

        if tag.startswith("<rect"):
            x, y = attr(tag, "x") or "0", attr(tag, "y") or "0"
            w, h = attr(tag, "width"), attr(tag, "height")
            rx = attr(tag, "rx") or "0"
            geometry = f'<RectangleGeometry Rect="{x},{y},{w},{h}" RadiusX="{rx}" RadiusY="{rx}"/>'
        else:
            rule = ' FillRule="EvenOdd"' if attr(tag, "fill-rule") == "evenodd" else ""
            geometry = f'<PathGeometry Figures="{attr(tag, "d")}"{rule}/>'

        out.append("      <GeometryDrawing>")
        out.append("        <GeometryDrawing.Brush>")
        out.append(brush_xml(fill, "          "))
        out.append("        </GeometryDrawing.Brush>")
        out.append(f"        <GeometryDrawing.Geometry>{geometry}</GeometryDrawing.Geometry>")
        out.append("      </GeometryDrawing>")


ax = [
    "<!--",
    "  Из Design/kvazr/Fluent Icons Color.dc.html и Fluent Icons Real.dc.html,",
    "  плюс Fluent Device Icons.dc.html для того, чего в паке нет — tools/fluent-icons.py.",
    "  НЕ ПРАВИТЬ РУКАМИ: перерисовали иконку в дизайне — перезапустить скрипт.",
    "-->",
    '<ResourceDictionary xmlns="https://github.com/avaloniaui"',
    '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
    "",
]

for key, markup in ICONS.items():
    side = 48 if 'viewBox="0 0 48 48"' in markup else 24

    ax.append(f'  <DrawingImage x:Key="Fi.{key}">')
    ax.append("    <DrawingGroup>")
    # the canvas, so every icon measures its whole grid whatever it draws inside it
    ax.append('      <GeometryDrawing Brush="#00FFFFFF">')
    ax.append(f'        <GeometryDrawing.Geometry><RectangleGeometry Rect="0,0,{side},{side}"/></GeometryDrawing.Geometry>')
    ax.append("      </GeometryDrawing>")

    if key in LEGACY_KEYS:
        for el in LEGACY_ICONS[key]["l"]:
            stroked = el[0] == "p" and len(el) > 3 and isinstance(el[3], str) and el[3].startswith("stroke:")
            if stroked:
                _, grad, width = el[3].split(":")
                ax.append("      <GeometryDrawing>")
                ax.append("        <GeometryDrawing.Pen>")
                ax.append(f'          <Pen Thickness="{width}" LineCap="Round" LineJoin="Round">')
                ax.append("            <Pen.Brush>")
                ax.append(brush_xml(f"url(#{grad})", "              "))
                ax.append("            </Pen.Brush>")
                ax.append("          </Pen>")
                ax.append("        </GeometryDrawing.Pen>")
            else:
                value = el[-1] if el[0] != "p" else el[2]
                ax.append("      <GeometryDrawing>")
                ax.append("        <GeometryDrawing.Brush>")
                ax.append(brush_xml(f"url(#{value})" if value in palette else value, "          "))
                ax.append("        </GeometryDrawing.Brush>")
            ax.append(f"        <GeometryDrawing.Geometry>{legacy_geometry(el)}</GeometryDrawing.Geometry>")
            ax.append("      </GeometryDrawing>")
    else:
        pack_drawings(markup, ax)

    ax.append("    </DrawingGroup>")
    ax.append("  </DrawingImage>")
    ax.append("")

ax += ["</ResourceDictionary>", ""]
AXAML_OUT.write_text("\n".join(ax), encoding="utf-8")
print(f"wrote {AXAML_OUT.relative_to(ROOT)} — {len(ICONS)} иконок")
