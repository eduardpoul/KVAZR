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


def box(fragment: str, marker: str) -> str:
    """
    The width the browser actually drew, not the number in the `width` declaration.

    The design file has no `box-sizing: border-box`, so an element with `width: 248px`
    and `padding: 0 8px` occupies 264. Lifting the declaration alone made the navigation
    pane sixteen pixels narrower than the artboard everywhere it was used, which is
    exactly the class of mistake this script exists to prevent — so the box model is
    done here rather than left to whoever reads the token.
    """
    width = float(px(find(fragment, marker, "width")))
    parts = [p.strip() for p in find(fragment, marker, "padding").split()]
    horizontal = parts[1] if len(parts) > 1 else parts[0]
    side = 0.0 if horizontal == "0" else float(px(horizontal))

    total = width + (side * 2)
    return str(int(total)) if total == int(total) else str(total)


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
    page = region(html, 'id="5a"')

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
    num("DashBatteryBlock", px(find(dash, "flex: none; width: 92px", "width")),
        "Колонка заряда: одинаковая на всех строках, иначе проценты не выстраиваются")
    num("DashBatteryGap", px(find(dash, "flex: none; width: 92px", "gap")), "")
    num("DashPercentWidth", px(find(dash, "font-size: 15px; font-weight: 600; width: 42px", "width")), "")
    num("DashPercentSize",
        px(find(dash, "font-size: 15px; font-weight: 600; width: 42px", "font-size")), "")
    num("DashNoteSize", px(find(dash, "font-size: 12px; color: #616161; white-space: nowrap", "font-size")),
        "Приписка после пилюль")

    # ---- Settings, artboard 7a --------------------------------------------------------
    # Shorter rows than a device page: nothing here has a slider or a photograph, and 64
    # pixels for a label and a switch is a page that scrolls for no reason.
    prefs = region(html, 'id="7a"', 14000)
    num("SettingsRowHeight", px(find(prefs, "height: 52px; display: flex", "height")), "")
    num("SettingsRowGap", px(find(prefs, "height: 52px; display: flex", "gap")), "")
    num("SettingsRowPadding", pad(find(prefs, "height: 52px; display: flex", "padding")), "")
    num("SegmentHeight", px(find(prefs, "height: 30px; border-radius: 4px", "height")),
        "Переключатель темы — один слитный контрол, а не три кнопки рядом")
    num("SegmentRadius", px(find(prefs, "height: 30px; border-radius: 4px", "border-radius")), "")
    num("SegmentSize", px(find(prefs, "height: 30px; border-radius: 4px", "font-size")), "")
    num("SegmentWideWidth", px(find(prefs, "width: 84px; background: #0F6CBD", "width")), "")
    num("SegmentWidth", px(find(prefs, "width: 72px; display: flex", "width")), "")
    # The artboard picks a width per dropdown — 168 for the language, 140 for the poll
    # interval — rather than lining them up in a column the way a device page does. Both
    # are content-box, so the padding is added back in.
    num("SettingsLanguageWidth", box(prefs, "flex: none; height: 30px; width: 168px"), "")
    num("SettingsIntervalWidth", box(prefs, "flex: none; height: 30px; width: 140px"), "")
    num("SettingsSliderWidth", px(find(prefs, "flex: none; width: 240px", "width")),
        "Ползунок на странице приложения; страница устройства шире — там колонка 260")
    num("SettingsValueWidth", px(find(prefs, "width: 44px; font-size: 14px", "width")), "")

    # ---- Notice card, artboard 11c ----------------------------------------------------
    # Says what the application does not know, rather than warning about anything: a
    # device that answers nothing is a fact about the device, not a fault of the user's.
    bare = region(html, 'id="11c"', 9000)
    num("NoticeRadius", px(find(bare, "background: rgba(234,163,0,0.08)", "border-radius")), "")
    num("NoticePadding", pad(find(bare, "background: rgba(234,163,0,0.08)", "padding")), "")
    num("NoticeGap", px(find(bare, "background: rgba(234,163,0,0.08)", "gap")), "")
    num("NoticeDotSize", px(find(bare, "width: 7px; height: 7px", "width")), "")
    num("NoticeTextSize", px(find(bare, "line-height: 1.5", "font-size")), "")

    # ---- Content pane, artboard 5a ----------------------------------------------------
    # A raised surface rather than the window's own background, joined to the pane by one
    # rounded corner. Without it the two halves of the window read as one flat field.
    num("ContentBackgroundRadius",
        px(find(dash, "border-top-left-radius: 8px", "border-top-left-radius")), "")
    num("ContentTopLeftCorner",
        px(find(dash, "border-top-left-radius: 8px", "border-top-left-radius")) + ",0,0,0",
        "Скруглён только верхний левый угол — остальные три упираются в край окна")
    # Taken from 5a rather than 1a: the dashboard is the one screen that uses 24/14 and
    # every other screen uses 22/16, and the pane is one control for all of them.
    num("ContentPadding", pad(find(page, "border-top-left-radius: 8px", "padding")), "")
    num("ContentGap", px(find(page, "border-top-left-radius: 8px", "gap")), "")

    # ---- Navigation pane, artboard 1a -------------------------------------------------
    # Three shapes share one geometry: the devices, one device's sections, and the
    # application's own pages. Every row in all three is the same 36 pixels.
    num("RailWidth", box(dash, "width: 248px; flex: none"),
        "Вместе с внутренними отступами: в макете это content-box, 248 + 8 + 8")
    num("RailPadding", pad(find(dash, "width: 248px; flex: none", "padding")), "")
    num("RailGap", px(find(dash, "width: 248px; flex: none", "gap")), "")
    num("RailRowHeight", px(find(dash, "border-radius: 4px; background: #EBEBEB", "height")), "")
    num("RailRowRadius", px(find(dash, "border-radius: 4px; background: #EBEBEB", "border-radius")), "")
    num("RailRowPadding", pad(find(dash, "border-radius: 4px; background: #EBEBEB", "padding")), "")
    num("RailRowSize", px(find(dash, "border-radius: 4px; background: #EBEBEB", "font-size")), "")
    num("RailRootGap", px(find(dash, "border-radius: 4px; background: #EBEBEB", "gap")),
        "Корневая строка и «Параметры» — иконка крупнее, зазор шире")
    num("RailDeviceGap",
        px(find(dash, "border-radius: 4px; display: flex; align-items: center; gap: 10px", "gap")),
        "Строка устройства")
    num("RailBatterySize", px(find(dash, "font-size: 12px; color: #616161", "font-size")),
        "Процент в панели — тише, чем имя")
    num("RailGroupSize", px(find(dash, "letter-spacing: 0.04em", "font-size")), "")
    num("RailGroupPadding", pad(find(dash, "letter-spacing: 0.04em", "padding")), "")
    num("RailMarkWidth", px(find(dash, "width: 3px; height: 16px", "width")),
        "Полоска текущего раздела")
    num("RailMarkHeight", px(find(dash, "width: 3px; height: 16px", "height")), "")
    num("RailMarkRadius", px(find(dash, "width: 3px; height: 16px", "border-radius")), "")
    num("RailSearchHeight", px(find(dash, "height: 32px; margin: 0 4px 6px", "height")), "")
    num("RailSearchMargin", pad(find(dash, "height: 32px; margin: 0 4px 6px", "margin")), "")
    num("RailSearchPadding", pad(find(dash, "height: 32px; margin: 0 4px 6px", "padding")), "")
    num("RailSearchGap", px(find(dash, "height: 32px; margin: 0 4px 6px", "gap")), "")
    num("RailSearchSize", px(find(dash, "height: 32px; margin: 0 4px 6px", "font-size")), "")
    # Another SVG width attribute rather than a CSS property, so this one is typed too.
    num("RailIconSize", "16", "Глиф строки, из <svg width> артборда 1a")

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

    # ---- Line boxes -------------------------------------------------------------------
    # Not in the design file at all: it never sets line-height, so these are the browser's
    # `normal` for this font at these sizes, measured off the rendered artboards — the
    # distance from the hero's bottom edge to the first card, minus the gaps around the
    # heading, on 5a and 1a. Avalonia's own default for Segoe UI Variable is three pixels
    # taller, which pushed every card down the page by that much on every screen.
    num("PageTitleLine", "37", "Строка заголовка страницы, 28 px")
    num("SectionHeadingLine", "18", "Строка заголовка раздела, 12 px")

    # ---- Device page, artboard 5a ------------------------------------------------------
    # The page is a hero and then sections; the hero is where the device is identified and
    # the sections are where it is changed.
    num("PagePadding", pad(find(page, "padding: 22px 28px", "padding")), "")
    num("HeroGap", px(find(page, "display: flex; align-items: center; gap: 20px", "gap")), "")
    num("HeroThumbSize", px(find(page, "width: 96px; height: 96px", "width")), "")
    num("HeroThumbRadius", px(find(page, "width: 96px; height: 96px", "border-radius")), "")
    num("HeroNameSize", px(find(page, "font-size: 26px; font-weight: 600", "font-size")), "")
    num("HeroBatterySize", px(find(page, "font-size: 44px; font-weight: 600", "font-size")),
        "Заряд — самое крупное на странице, потому что за ним и приходят чаще всего")
    num("SectionHeadingSize",
        px(find(page, "font-weight: 600; color: #616161; letter-spacing: 0.02em", "font-size")), "")
    num("PageRowHeight", px(find(page, "height: 64px; display: flex", "height")), "")
    num("PageRowPadding", pad(find(page, "height: 64px; display: flex", "padding")), "")
    num("PageRowGap", px(find(page, "height: 64px; display: flex", "gap")), "")
    num("PageRowTitleSize", px(find(page, "font-size: 14px", "font-size")), "")
    num("PageControlWidth", px(find(page, "flex: none; width: 260px", "width")),
        "Колонка контрола: одинаковая на всех строках, иначе значения не выстраиваются")

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

        # Named for what it is, because a partly-rounded corner cannot be assembled in
        # XAML out of a number: the content pane rounds only where it does not meet the
        # window frame, and that is four values or nothing.
        if name.endswith("Corner"):
            lines.append(f'  <CornerRadius x:Key="{name}">{value}</CornerRadius>')
            continue

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

        # A gap is often wanted as a one-sided margin, and XAML cannot build a Thickness
        # out of a resource — "0,0,{DynamicResource X},0" is a parse error, not a binding.
        if name.endswith("Gap"):
            lines.append(f'  <Thickness x:Key="{name}Right">0,0,{value},0</Thickness>')
            lines.append(f'  <Thickness x:Key="{name}Left">{value},0,0,0</Thickness>')

    lines += ["", "</ResourceDictionary>", ""]

    OUT.write_text("\n".join(lines), encoding="utf-8")
    print(f"wrote {OUT.relative_to(ROOT)} — {len(tokens)} tokens")
    for name, value, _ in tokens:
        print(f"   {name:<24} {value}")


if __name__ == "__main__":
    main()
