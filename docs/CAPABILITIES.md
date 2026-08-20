# Таксономия возможностей устройств

Модель, из которой рендерится интерфейс. Выведена из реальных протоколов, а не придумана: каждая строка ниже подтверждена исходниками работающих проектов.

Сопутствующий документ — [FEATURES.md](FEATURES.md), полный разбор одного устройства (Audeze Maxwell). Этот документ обобщает.

---

## 1. Источники

| Протокол | Что покрывает | Откуда взяты данные |
|---|---|---|
| **Стандартный HID / BLE** | заряд, наличие, тип связи | HID Usage Tables: Battery System (страница `0x85`), Generic Device Controls `0x06` usage `0x20` Battery Strength; BLE GATT Battery Service `0x180F` / характеристика `0x2A19` |
| **Logitech HID++ 2.0** | мыши, клавиатуры, гарнитуры, рули | Solaar, `lib/logitech_receiver/hidpp20_constants.py` — полный список фич |
| **VIA / QMK** | клавиатуры с открытой прошивкой | `quantum/via.h`, enum `via_command_id` и `via_keyboard_value_id` |
| **HeadsetControl** | ~40 моделей гарнитур | enum `HSC_CAP_*` |
| **libratbag** | мыши разных вендоров | `libratbag-enums.h` — модель профилей, разрешений, кнопок, подсветки |
| **Audeze RACE** | Maxwell | наш собственный реверс, см. `MaxwellFeatures.cs` |

---

## 2. Ключевое подтверждение: абстракция существует

Самое важное, что нашлось. В списке фич HID++ есть целый блок гарнитур Logitech (`Centurion` — линейки Astro и G). Он ложится на наш разобранный Maxwell почти один в один:

| Возможность | Audeze Maxwell (RACE) | Logitech (HID++) |
|---|---|---|
| Заряд | cmd `0x0CD6` | `CENTURION_BATTERY_SOC 0x0104`, `UNIFIED_BATTERY 0x1004` |
| Баланс игра/чат | `0x2C82` sub `0x0B` | `HEADSET_MIX 0x0609` |
| Сайдтон вкл/выкл + уровень | sub `0x07`, sub `0x2C` | `HEADSET_AUDIO_SIDETONE 0x0604`, `SIDETONE 0x8300` |
| Шумоподавление микрофона | sub `0x24` | `HEADSET_AI_NOISE_REDUCTION 0x060E` |
| Мьют микрофона (чтение) | sub `0x22` | `HEADSET_MIC_MUTE 0x0601` |
| Уровень микрофона | sub `0x35` | `HEADSET_MIC_GAIN 0x0611` |
| Громкость | sub `0x28`/`0x29`/`0x2A` | `HEADSET_VOLUME 0x0200` |
| Пресет эквалайзера | sub `0x00` | `HEADSET_EQ 0x0201`, `HEADSET_EQ_STYLES 0x0213` |
| Полосы эквалайзера | не разобрано | `HEADSET_ADVANCED_PARA_EQ 0x020D` |
| Таймер автовыключения | `0x2C82` sub `0x01` | `CENTURION_AUTO_SLEEP 0x0108` |
| Голосовые подсказки | sub `0x20` | `HEADSET_TONES 0x060B` |
| Сопряжение | sub `0xAA` — опасно | `LIGHTSPEED_PAIRING 0x0309` |

Два независимых вендора, ничего друг о друге не знающих, пришли к одному набору. То же самое подтверждает `HeadsetControl` своим enum `HSC_CAP_*`, где есть ровно эти же понятия: сайдтон, заряд, chatmix, голосовые подсказки, таймер бездействия, пресет эквалайзера, громкость микрофона.

**Вывод:** возможности — это не наша выдумка поверх одного устройства. Это устоявшийся отраслевой словарь. Значит, интерфейс можно строить на нём безопасно.

---

## 3. Уровень 1 — примитивы контролов

То, что физически умеет рисовать UI. Их мало, и это конечный список.

| Примитив | Данные | Пример |
|---|---|---|
| `Readout` | значение только на чтение | заряд, версия прошивки, состояние мьюта, тип подключения |
| `Toggle` | bool | сайдтон, подсветка, инверсия Fn |
| `Range` | число + min/max/step/unit, опционально центр | громкость, DPI, уровень микрофона, баланс чашек |
| `RangeGroup` | несколько связанных Range | левый/правый канал, DPI по X и Y |
| `Choice` | именованный список | пресет EQ, шумоподавление (3 режима), частота опроса |
| `Action` | одноразовая команда + уровень подтверждения | сброс, сопряжение, проиграть уведомление, вход в загрузчик |
| `Curve` | набор полос или фильтров | графический EQ, параметрический EQ, кривая отклика оси |
| `KeyMap` | слои × позиции → keycode | VIA-клавиатуры |
| `ButtonMap` | физическая кнопка → действие | мыши: кнопка / клавиша / спец-действие / макрос |
| `Lighting` | зоны × (режим, цвет, яркость, скорость) | RGB, подсветка клавиш |
| `ProfileSet` | именованные профили на устройстве | onboard-профили Logitech, слои VIA |
| `HostSlots` | список хостов + переключение | мультихост клавиатур и мышей |

**Двенадцать контролов покрывают все четыре протокола.** Это и есть ответ на вопрос «сколько UI надо написать»: не по экрану на устройство, а двенадцать контролов один раз.

---

## 4. Уровень 2 — каталог возможностей

Семантические возможности, сгруппированные по доменам. Колонка «Примитив» задаёт рендер, колонка «Источник» — кто это умеет.

### 4.1 Идентичность и питание

| ID | Примитив | Источник |
|---|---|---|
| `power.battery` | Readout (0–100 %) | все четыре |
| `power.charging` | Readout (bool) | HID++, HeadsetControl, RACE (косвенно) |
| `power.voltage` | Readout (мВ) | HID++ `BATTERY_VOLTAGE 0x1001`, HeadsetControl |
| `power.auto_off` | Choice (пресеты минут) | RACE, `CENTURION_AUTO_SLEEP`, `HSC_CAP_INACTIVE_TIME` (0–90 мин) |
| `power.battery_saver` | Toggle | `HEADSET_BATTERY_SAVER 0x0618` |
| `device.firmware` | Readout (строка) | HID++ `0x0003`, VIA `id_firmware_version`, RACE |
| `device.name` | Readout | HID++ `0x0005` / `0x0007` |
| `device.identify` | Action | VIA `id_device_indication`, `HSC_CAP_NOTIFICATION_SOUND` |

### 4.2 Связь

| ID | Примитив | Источник |
|---|---|---|
| `link.transport` | Readout (кабель / донгл / BT) | все |
| `link.signal` | Readout | HID++ `WIRELESS_SIGNAL_STRENGTH 0x0080` |
| `link.hosts` | HostSlots | HID++ `CHANGE_HOST 0x1814`, `HOSTS_INFO 0x1815` |
| `link.pair` | Action (опасно) | RACE sub `0xAA`, `LIGHTSPEED_PAIRING 0x0309` |
| `link.bt_on_power` | Toggle | `HSC_CAP_BT_WHEN_POWERED_ON` |

### 4.3 Звук

| ID | Примитив | Источник |
|---|---|---|
| `audio.volume` | Range | RACE `0x28`, `HEADSET_VOLUME 0x0200` |
| `audio.channel_gain` | RangeGroup (L/R) | RACE `0x29` / `0x2A` |
| `audio.balance` | Range с центром | RACE `0x25` |
| `audio.eq_preset` | Choice | RACE `0x00`, `HEADSET_EQ_STYLES`, `HSC_CAP_EQUALIZER_PRESET` |
| `audio.eq_graphic` | Curve (полосы) | `HSC_CAP_EQUALIZER`, HID++ `EQUALIZER 0x8310` |
| `audio.eq_parametric` | Curve (фильтры с Q) | `HSC_CAP_PARAMETRIC_EQUALIZER`, `HEADSET_ADVANCED_PARA_EQ 0x020D` |
| `audio.volume_limiter` | Toggle | `HSC_CAP_VOLUME_LIMITER`, `HEADSET_NOISE_EXPOSURE 0x060D` |
| `audio.voice_prompts` | Toggle или Range | RACE `0x20`, `HEADSET_TONES`, `HSC_CAP_VOICE_PROMPTS` |

### 4.4 Микрофон

| ID | Примитив | Источник |
|---|---|---|
| `mic.mute` | **Readout, не Toggle** на части устройств | RACE `0x22` (записи не существует), `HEADSET_MIC_MUTE 0x0601` |
| `mic.level` | Range | RACE `0x35`, `HEADSET_MIC_GAIN 0x0611`, `HSC_CAP_MICROPHONE_VOLUME` (0–128) |
| `mic.sidetone` | Toggle | RACE `0x07` |
| `mic.sidetone_level` | Range, потолок читается из устройства | RACE `0x2C` с максимумом из `0x2D`, `HSC_CAP_SIDETONE` (0–128) |
| `mic.noise_reduction` | Choice | RACE `0x24`, `HEADSET_AI_NOISE_REDUCTION`, `LOGIVOICE_AINR 0x0909` |
| `mic.mute_led` | Range (0–3) | `HSC_CAP_MICROPHONE_MUTE_LED_BRIGHTNESS` |
| `mic.rotate_to_mute` | Toggle | `HSC_CAP_ROTATE_TO_MUTE` |
| `mic.processing` | Curve / группа | `LOGIVOICE_*`: гейт, компрессор, де-эссер, лимитер, ФВЧ |

### 4.5 Игра и чат

| ID | Примитив | Источник |
|---|---|---|
| `mix.chat` | Range с засечками | RACE `0x2C82/0x0B`, `HEADSET_MIX 0x0609`, `HSC_CAP_CHATMIX_STATUS` |

### 4.6 Указатель (мыши)

| ID | Примитив | Источник |
|---|---|---|
| `pointer.dpi` | Range или Choice ступеней | HID++ `ADJUSTABLE_DPI 0x2201`, `EXTENDED_ADJUSTABLE_DPI 0x2202` |
| `pointer.dpi_xy` | RangeGroup | libratbag `RATBAG_RESOLUTION_CAP_SEPARATE_XY_RESOLUTION` |
| `pointer.report_rate` | Choice (Гц) | HID++ `REPORT_RATE 0x8060`, `0x8061` |
| `pointer.speed` | Range | HID++ `POINTER_SPEED 0x2205` |
| `pointer.angle_snapping` | Toggle | HID++ `ANGLE_SNAPPING 0x2230` |
| `pointer.surface_tuning` | Action / Range | HID++ `SURFACE_TUNING 0x2240` |
| `pointer.swap_lr` | Toggle | HID++ `LEFT_RIGHT_SWAP 0x2001` |
| `scroll.smart_shift` | Range | HID++ `SMART_SHIFT 0x2110`, `0x2111` |
| `scroll.hires` | Toggle | HID++ `HIRES_WHEEL 0x2121` |
| `scroll.thumb_wheel` | Toggle + направление | HID++ `THUMB_WHEEL 0x2150` |
| `buttons.map` | ButtonMap | HID++ `REPROG_CONTROLS_V4 0x1B04`, `PERSISTENT_REMAPPABLE_ACTION 0x1C00`; libratbag `ratbag_button_action_type` |

Типы действий кнопки берём готовыми из libratbag, там они уже нормализованы: `BUTTON`, `KEY`, `SPECIAL`, `MACRO`, `NONE`. Спец-действия — двойной клик, циклы DPI и профилей, режим храповика, показать заряд.

### 4.7 Клавиатура

| ID | Примитив | Источник |
|---|---|---|
| `keys.map` | KeyMap (слои × позиции) | VIA `id_dynamic_keymap_get/set_keycode 0x04`/`0x05`, буфер `0x12`/`0x13` |
| `keys.layers` | Readout (количество) | VIA `id_dynamic_keymap_get_layer_count 0x11` |
| `keys.encoders` | KeyMap для энкодеров | VIA `0x14` / `0x15` |
| `keys.macros` | список макросов | VIA `0x0C`–`0x10` |
| `keys.layout_options` | Choice | VIA `id_layout_options 0x02` |
| `keys.fn_inversion` | Toggle | HID++ `FN_INVERSION 0x40A0`, `0x40A2`, `0x40A3` |
| `keys.disable` | набор Toggle | HID++ `KEYBOARD_DISABLE_KEYS 0x4521` |
| `keys.platform` | Choice (Win/Mac) | HID++ `MULTIPLATFORM 0x4531` |
| `keys.reset` | Action (опасно) | VIA `id_dynamic_keymap_reset 0x06`, `id_eeprom_reset 0x0A` |

Важно: у VIA раскладка **пишется в саму клавиатуру** и работает без запущенного приложения. Это принципиально отличается от перехвата на хосте, и в интерфейсе это надо проговаривать.

### 4.8 Подсветка

| ID | Примитив | Источник |
|---|---|---|
| `light.on` | Toggle | `HSC_CAP_LIGHTS` |
| `light.brightness` | Range | HID++ `BACKLIGHT2 0x1982`, `BRIGHTNESS_CONTROL 0x8040` |
| `light.effect` | Choice | libratbag `RATBAG_LED_OFF/ON/CYCLE/BREATHING`, HID++ `RGB_EFFECTS 0x8071` |
| `light.color` | Lighting (зоны) | HID++ `COLOR_LED_EFFECTS 0x8070`, `PER_KEY_LIGHTING_V2 0x8081` |

### 4.9 Профили и обслуживание

| ID | Примитив | Источник |
|---|---|---|
| `profiles.onboard` | ProfileSet | HID++ `ONBOARD_PROFILES 0x8100`, libratbag |
| `maint.factory_reset` | Action (опасно) | RACE `0x1101` sub `0x95`, HID++ `DEVICE_RESET 0x1802` |
| `maint.firmware_update` | Action (отдельный экран) | HID++ `DFU 0x00D0` |
| `maint.bootloader` | Action (опасно) | VIA `id_bootloader_jump 0x0B` |

---

## 5. Уровень 3 — матрица покрытия

Что даёт каждый драйвер. Показывает, в каком порядке их писать.

| Домен | Стандартный HID/BLE | HID++ | VIA | RACE |
|---|:--:|:--:|:--:|:--:|
| Заряд и питание | ● | ● | — | ● |
| Связь и хосты | ◐ | ● | — | ◐ |
| Звук | — | ● | — | ● |
| Микрофон | — | ● | — | ● |
| Игра/чат | — | ● | — | ● |
| Указатель | — | ● | — | — |
| Клавиши | — | ◐ | ● | — |
| Подсветка | — | ● | ◐ | — |
| Профили | — | ● | ● | — |

● полностью · ◐ частично

Читается так: **HID++ покрывает больше всех доменов сразу**. Это подтверждает порядок фаз — генерик-слой, потом HID++, потом VIA.

---

## 6. Уровень 4 — обязательные атрибуты возможности

Без этих полей UI получится либо врущим, либо опасным. Каждое взято из конкретной боли, уже пережитой на Maxwell.

| Атрибут | Зачем | Откуда взялось |
|---|---|---|
| `status` | ✅ проверено / 🟡 команда известна / 🔵 нет реализации | у нас 9 функций с непроверенной записью |
| `access` | read / write / readwrite | мьют микрофона читается, но функции записи не существует — это индикатор, а не кнопка |
| `range_source` | `static` или `device` | потолок сайдтона читается из sub `0x2D`, а список DPI — из самого устройства. Хардкодить нельзя |
| `volatile` | значение меняется помимо приложения | у Maxwell физическое колесо ChatMix, у мышей — кнопка переключения DPI, у клавиатур — смена хоста |
| `reversible` | запись можно откатить | sub `0x31` защёлкивается после первой записи гейна и назад не возвращается |
| `risk` | `safe` / `confirm` / `hidden` | `0x1101` подписан в публичной документации как «Battery/sync», а на деле стирает гарнитуру |
| `requires` | условие доступности | уровень микрофона мёртв при отсоединённом бум-микрофоне; всё управление мертво на донгле |

Последнее — `requires` — самое недооценённое. Без него интерфейс будет показывать живые контролы, которые молча ничего не делают. У нас таких состояний уже два: «только донгл» и «бум отсоединён».

---

## 7. Что из этого следует для UI

**Страница устройства генерируется, а не рисуется.** Драйвер отдаёт список возможностей, оболочка группирует их по доменам и рендерит соответствующие примитивы. Ни одной страницы «под Maxwell» или «под G Pro» не существует.

**Порядок и группировка — из домена, а не из драйвера.** Домены задают разделы: Питание, Связь, Звук, Микрофон, Указатель, Клавиши, Подсветка, Профили, Дополнительно. Пустые разделы скрываются. Так гарнитура и мышь выглядят как одно приложение, а не как два разных.

**Двенадцать контролов пишутся один раз и переиспользуются.** График параметрического EQ, написанный для Maxwell, автоматически обслуживает `HEADSET_ADVANCED_PARA_EQ` у Logitech и `HSC_CAP_PARAMETRIC_EQUALIZER` у остальных. Это главный аргумент против UI внутри плагинов: там каждый вендор рисовал бы свой график.

**Трей и флайаут работают только с `power.*` и `mix.chat`.** Это единственные возможности, которые нужны в быстром доступе, и они есть почти у всех устройств. Всё остальное — в основном окне.

**Возможности, которых нет, не рисуются вообще.** Не серым, не с подписью «недоступно» — их просто нет. Исключение только для `requires`: если контрол есть, но временно недоступен из-за состояния устройства, он показывается неактивным с объяснением причины.

---

## 8. Схема дескриптора устройства

То, что лежит в каталоге на GitHub и добавляется без релиза приложения.

```jsonc
{
  "id": "audeze/maxwell-xbox",
  "name": "Audeze Maxwell",
  "vendor": "Audeze",
  "kind": "headset",
  "match": [
    { "vid": "0x3329", "pid": "0x4B1E", "usagePage": "0xFF13", "transport": "usb" },
    { "vid": "0x3329", "pid": "0x4B18", "usagePage": "0xFF13", "transport": "dongle" }
  ],
  "driver": "audeze.race",
  "image": "audeze/maxwell.png",
  "capabilities": [
    { "id": "power.battery", "status": "verified" },

    { "id": "mix.chat", "status": "verified",
      "min": 0, "max": 20, "detents": [0, 10, 20],
      "volatile": true,
      "requires": "transport == usb" },

    { "id": "audio.volume", "status": "verified",
      "min": 0, "max": 150, "reversible": false,
      "note": "выше 150 повреждает излучатель" },

    { "id": "mic.sidetone_level", "status": "untested",
      "rangeSource": "device", "maxFrom": "0x2D" },

    { "id": "mic.mute", "status": "untested", "access": "read",
      "requires": "boomMic == attached" },

    { "id": "maint.factory_reset", "status": "untested", "risk": "hidden" }
  ]
}
```

Новая мышь Logitech — такой же файл с `"driver": "logitech.hidpp"` и другим списком возможностей. Кода не требует.

---

## 9. Что осталось выяснить

- Набор значений `power.auto_off` у Maxwell: устройство отдаёт `7`, и это почти наверняка индекс пресета, а не минуты. У HeadsetControl тот же параметр — это диапазон 0–90 минут. Нужно понять, приводимы ли они к одной шкале, или `Choice` и `Range` придётся держать раздельно.
- Единицы `mic.level` и `mic.sidetone_level`: у нас 0–255 и 0–31, у HeadsetControl 0–128. Нормализовать в проценты на уровне драйвера или показывать сырые значения — решить до появления второго драйвера.
- Формат `KeyMap` для VIA требует JSON-определения раскладки из репозитория `the-via/keyboards`. Это второй внешний каталог: либо втягивать в свой, либо ходить в чужой.
- Модель макросов у VIA и у libratbag разная. Общий тип `Macro` придётся выводить из обеих, а не из одной.
