#!/usr/bin/env python3
"""
Reads the measurements out of the design files and writes them into an Avalonia
resource dictionary.

This exists because of a specific, repeated failure. The design is authored as HTML with
inline styles — `height: 84px; gap: 16px; border-radius: 6px` — and every screen that was
built by reading those numbers and typing them into XAML came out wrong in a way nobody
noticed until it was rendered and put next to the artboard. Not wrong by much, and never
wrong in the same place twice, which is exactly what makes hand-transcription the problem
rather than any individual mistake.

So the numbers are not typed any more. They are lifted from the design file by the anchors
below, and re-running this after the design changes updates them. What still takes judgement
is the naming — a script cannot know that `height: 84px` on that particular div is the
dashboard's row height — so each token names an anchor and a CSS property, and the value
comes from the file.

Usage:  python tools/design-tokens.py
"""

import io
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DESIGN = ROOT / "Design" / "kvazr" / "Devices Hub.dc.html"
OUT = ROOT / "src" / "Maxwell.App" / "Themes" / "Metrics.axaml"


def load() -> str:
    return io.open(DESIGN, encoding="utf-8").read()


def region(html: str, anchor: str, span: int = 12000) -> str:
    """The markup of one artboard, so a token cannot pick up a value from another screen."""
    at = html.find(anchor)
    if at < 0:
        sys.exit(f"anchor not found: {anchor}")
    return html[at : at + span]


def styles(fragment: str):
    for st in re.findall(r'style="([^"]*)"', fragment):
        decls = {}
        for decl in st.split(";"):
            if ":" not in decl:
                continue
            k, v = decl.split(":", 1)
            decls[k.strip()] = v.strip()
        yield decls


def find(fragment: str, marker: str, prop: str, nth: int = 0) -> str:
    """
    The value of `prop` on the nth element whose style block contains `marker`.

    Matching on a distinctive fragment of the element's own style rather than on a class
    or an id, because the design file has neither — it is inline styles all the way down.
    """
    hits = [d for d in styles(fragment) if marker in "; ".join(f"{k}: {v}" for k, v in d.items())]
    if len(hits) <= nth:
        sys.exit(f"no match {nth} for {marker!r}")
    if prop not in hits[nth]:
        sys.exit(f"{prop!r} not on the element matched by {marker!r}")
    return hits[nth][prop]


def px(value: str) -> str:
    """`84px` -> `84`. Avalonia wants a bare number, and a stray unit fails silently."""
    m = re.match(r"^(-?[\d.]+)px$", value.strip())
    if not m:
        sys.exit(f"not a pixel value: {value!r}")
    return m.group(1).rstrip(".0") or "0" if "." in m.group(1) else m.group(1)


def pad(value: str) -> str:
    """CSS `0 16px` -> Avalonia `16,0`. CSS is top/right/bottom/left; Avalonia is l,t,r,b."""
    parts = [p.strip() for p in value.split()]
    nums = [0 if p == "0" else float(px(p)) for p in parts]

    if len(nums) == 1:
        t = r = b = l = nums[0]
    elif len(nums) == 2:
        t = b = nums[0]
        r = l = nums[1]
    elif len(nums) == 3:
        t, (r, l), b = nums[0], (nums[1], nums[1]), nums[2]
    else:
        t, r, b, l = nums

    return ",".join(str(int(v)) if v == int(v) else str(v) for v in (l, t, r, b))


def main() -> None:
    html = load()

    dash = region(html, 'id="1a"')
    fly = region(html, 'id="12a"')
    empty = region(html, 'id="6c"')

    tokens: list[tuple[str, str, str]] = []

    def num(name: str, value: str, note: str = "") -> None:
        tokens.append((name, value, note))

    # ---- Dashboard, artboard 1a -------------------------------------------------------
    # The device list is one card with rows and dividers inside it, not a card per device.
    num("DashRowHeight", px(find(dash, "height: 84px", "height")),
        "Строка устройства целиком, включая разделитель")
    num("DashRowGap", px(find(dash, "height: 84px", "gap")),
        "Между плиткой и текстом")
    num("DashRowPadding", pad(find(dash, "height: 84px", "padding")), "")
    num("DashThumbSize", px(find(dash, "width: 60px", "width")), "Плитка изображения")
    num("DashThumbRadius", px(find(dash, "width: 60px", "border-radius")), "")
    num("DashNameSize", px(find(dash, "font-size: 15px; font-weight: 600", "font-size")), "")
    num("DashSubGap", px(find(dash, "gap: 6px; flex-wrap: nowrap", "gap")),
        "Между пилюлями под именем")
    num("DashPillSize", px(find(dash, "background: rgba(0,0,0,0.05); border-radius: 4px", "font-size")),
        "Пилюля класса устройства")
    num("DashPillRadius", px(find(dash, "background: rgba(0,0,0,0.05); border-radius: 4px", "border-radius")), "")
    num("DashPillPadding", pad(find(dash, "background: rgba(0,0,0,0.05); border-radius: 4px", "padding")), "")
    num("DashTransportPadding", pad(find(dash, "color: #0F6CBD; background: rgba(15,108,189,0.10)", "padding")),
        "Пилюля транспорта — на пиксель уже, потому что у неё есть рамка")
    num("DashGroupSize", px(find(dash, "font-weight: 600; color: #616161; letter-spacing: 0.02em", "font-size")),
        "Заголовок группы, ПОДКЛЮЧЕНЫ · 6")
    num("DashTitleSize", px(find(dash, "font-size: 28px; font-weight: 600", "font-size")),
        "Заголовок страницы")

    # ---- Tray panel, artboard 12a -----------------------------------------------------
    # Every device is the same row and none of them starts open.
    num("FlyoutRowHeight", px(find(fly, "height: 56px; display: flex", "height")), "")
    num("FlyoutRowGap", px(find(fly, "height: 56px; display: flex", "gap")), "")
    num("FlyoutRowPadding", pad(find(fly, "height: 56px; display: flex", "padding")), "")
    num("FlyoutRowRadius", px(find(fly, "height: 56px; display: flex", "border-radius")), "")
    num("FlyoutThumbSize", px(find(fly, "width: 36px; height: 36px", "width")), "")
    num("FlyoutThumbRadius", px(find(fly, "width: 36px; height: 36px", "border-radius")), "")
    num("FlyoutNameSize", px(find(fly, "font-size: 13px; font-weight: 600", "font-size")), "")
    num("FlyoutSubSize", px(find(fly, "font-size: 11px; color: #A0A0A0", "font-size")), "")
    num("FlyoutBatterySize", px(find(fly, "font-size: 15px; font-weight: 600", "font-size")), "")
    num("FlyoutListHeight", px(find(fly, "overflow: hidden; position: relative", "height")),
        "Ровно пять строк; дальше список прокручивается, а панель не растёт")
    num("FlyoutWidth", px(find(fly, "background: rgba(44,44,44,0.86)", "width")), "")
    num("FlyoutRadius", px(find(fly, "background: rgba(44,44,44,0.86)", "border-radius")), "")
    num("FlyoutHeaderPadding", pad(find(fly, "padding: 14px 16px 8px", "padding")), "")
    num("FlyoutHeaderSize", px(find(fly, "font-weight: 600; color: #E8E8E8", "font-size")), "")
    num("FlyoutFooterHeight", px(find(fly, "border-top: 1px solid rgba(255,255,255,0.08)", "height")), "")
    num("FlyoutFooterPadding", pad(find(fly, "border-top: 1px solid rgba(255,255,255,0.08)", "padding")), "")
    num("FlyoutFooterSize", px(find(fly, "border-top: 1px solid rgba(255,255,255,0.08)", "font-size")), "")
    num("FlyoutListPadding", pad(find(fly, "padding: 0 10px", "padding")), "")

    # ---- First launch, artboard 6c ----------------------------------------------------
    # Not an error screen. It is the only chance to say what the application is for.
    num("EmptyTitleSize", px(find(empty, "font-size: 32px; font-weight: 600", "font-size")), "")
    num("EmptyBodySize", px(find(empty, "line-height: 1.5", "font-size")), "")
    num("EmptyCardPadding", pad(find(empty, "box-shadow: 0 2px 4px rgba(0,0,0,0.04); padding: 18px", "padding")), "")
    num("EmptyCardRadius", px(find(empty, "box-shadow: 0 2px 4px rgba(0,0,0,0.04); padding: 18px", "border-radius")), "")
    num("EmptyCardGap", px(find(empty, "display: flex; gap: 14px", "gap")), "")
    num("EmptyCardTitleSize", px(find(empty, "font-size: 14px; font-weight: 600", "font-size")), "")
    num("EmptyCardBodySize", px(find(empty, "line-height: 1.45", "font-size")), "")
    # An SVG width attribute rather than a CSS property, so it is written out here
    # instead of being found. The comment is the honesty: this one number is typed.
    num("EmptyGlyphSize", "34", "Глиф класса на карточке, из <svg width> артборда 6c")

    lines = [
        "<!--",
        "  Размеры, снятые с макета машинно. НЕ ПРАВИТЬ РУКАМИ.",
        "",
        "  Сгенерировано tools/design-tokens.py из Design/kvazr/Devices Hub.dc.html.",
        "  Каждое значение взято из конкретного элемента конкретного артборда, а не",
        "  перенесено на глаз: экраны, которые собирали переписыванием чисел, каждый раз",
        "  расходились с макетом в новом месте, и заметно это становилось только когда",
        "  готовый экран клали рядом с артбордом.",
        "",
        "  Поменялся дизайн — перезапустить скрипт, а не искать числа по разметке.",
        "-->",
        '<ResourceDictionary xmlns="https://github.com/avaloniaui"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"',
        '                    xmlns:sys="using:System">',
        "",
    ]

    for name, value, note in tokens:
        if note:
            lines.append(f"  <!-- {note} -->")

        if "," in value:
            lines.append(f'  <Thickness x:Key="{name}">{value}</Thickness>')
            continue

        lines.append(f'  <sys:Double x:Key="{name}">{value}</sys:Double>')

        # A rounding is used two ways and Avalonia will not convert between them: a
        # Border wants a CornerRadius and a Skeleton wants a plain number. Binding the
        # wrong one throws at runtime rather than at build, and only when the theme
        # changes, so both are emitted and the name says which is which.
        if name.endswith("Radius"):
            lines.append(f'  <CornerRadius x:Key="{name}Corner">{value}</CornerRadius>')

    lines += ["", "</ResourceDictionary>", ""]

    OUT.write_text("\n".join(lines), encoding="utf-8")
    print(f"wrote {OUT.relative_to(ROOT)} — {len(tokens)} tokens")
    for name, value, _ in tokens:
        print(f"   {name:<24} {value}")


if __name__ == "__main__":
    main()
