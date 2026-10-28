# Иконки устройств

Иконки, которые приложение показывает устройству без фотографии: плитка на «Все
устройства», герой страницы устройства, рельса и флайаут. Один файл — один рисунок, имя
файла — ключ, который возвращает `DeviceIcon.KeyFor`.

Стиль — цветные иконки Fluent 2 (Fluent UI System Icons, вариант `color`), сетка 24×24.

- `headset.svg` и `mic.svg` — рисунки Microsoft из `@fluentui/svg-icons` 1.1.344
  (`headset_24_color`, `mic_24_color`), без изменений, только разбиты по строкам.
- Остальные нарисованы здесь по тем же правилам, потому что цветных клавиатуры, мыши,
  геймпада, вкладышей и прочего в библиотеке нет. Силуэт геймпада — контур
  `xbox_controller_24_filled` без отверстия.

Рядом лежат два пака на выбор, приложение их пока не читает (генератор берёт только
файлы из корня папки). Все три пака на одном листе — `../Device Icon Packs.html`.

- `flat/` — плоский, как навигация Параметров Windows: один цвет на устройство с лёгким
  перепадом сверху вниз, рабочие части белые, без слоёв. Контуры гарнитуры, микрофона и
  вилки — `headset_24_filled`, `mic_24_filled`, `plug_connected_24_filled`.
- `mono/` — монохромный, как значки в трее: контур толщиной 1.5 цветом текста
  (`currentColor`). Гарнитура, микрофон, клавиатура, геймпад, колонка, донгл, часы и
  вилка — `*_24_regular` из той же библиотеки, остальное нарисовано по их толщине и
  скруглениям. Чтобы включить его в приложение, генератору надо научиться `currentColor`:
  в панели это цвет текста, в Avalonia — кисть переднего плана темы.

## Правила, по которым нарисованы свои

- Устройство собрано из деталей, у каждой свой градиент. Не один силуэт с заливкой.
- Корпус — серый, светлый сверху (`#d1d8de → #959ea7`), торец и подставки темнее
  (`#8a9298 → #5b6166`), утопленные части тёмные (`#63686e → #383b3d`).
- Акцент — синий Fluent (`#0fafff → #0067bf`) на той части, которой пользуются: пробел,
  корпус мыши, кнопки геймпада, динамики. Экран — `#6ce0ff → #4894fe`, как у `laptop_24_color`.
- Градиенты только линейные и в координатах сетки (`gradientUnits="userSpaceOnUse"`).

Последнее — не вкус, а ограничение: генератор переносит рисунок в геометрию Avalonia
один в один и понимает только `path`, `rect`, `circle`, `ellipse` с заливкой цветом или
таким градиентом, плюс `opacity`/`fill-opacity`. Остальное он отвергает, чтобы рельса не
рисовала иначе, чем панель.

## Как пересобрать

```
dotnet run --project src/Kvazr.Cli -- --fluent-icons
```

Пишет `src/Kvazr.App/Web/Infrastructure/FluentIcons.g.cs` (SVG для панели) и
`src/Kvazr.App/Themes/FluentIcons.axaml` (DrawingImage для оболочки). Руками их не правят.

## Лицензия рисунков Microsoft

Рисунки и контуры, названные выше, — из https://github.com/microsoft/fluentui-system-icons:

> MIT License
>
> Copyright (c) 2020 Microsoft Corporation
>
> Permission is hereby granted, free of charge, to any person obtaining a copy
> of this software and associated documentation files (the "Software"), to deal
> in the Software without restriction, including without limitation the rights
> to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all
> copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
> SOFTWARE.
