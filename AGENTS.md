# AGENTS.md — CNCJournal

Конвенции проекта. Цель — править тему/стили **в общих местах**, а не точечно по окнам.

## Проекты

- `libeLog` — общий слой (палитры Areopag, иконки, `StyleDictionary.xaml`); используется и `eLog`, и `remeLog.WPF`.
- `remeLog.WPF` — приложение; свои неявные стили в `Infrastructure/ImplicitStyles.xaml`.
- `eLog` — отдельное приложение. **Изменения в `remeLog.WPF` его не касаются.** Правки в `libeLog` касаются обоих — проверяйте, что eLog не ломается.

## Тема (light/dark)

- Палитры: `libeLog/Areopag/Areopag.Palette.xaml` (светлая) и `Areopag.Palette.Dark.xaml` (тёмная).
  Наборы ключей должны быть **строго 1:1** — новый ключ добавлять в обе.
- Переключение — `libeLog/Infrastructure/ThemeManager.cs` (замена словаря палитры), без перезапуска.
- Chrome окон (заголовки) красит DWM по `DWMWA_USE_IMMERSIVE_DARK_MODE` — `libeLog/Infrastructure/WindowChromeTheme.cs`.
  Каждое новое окно обязано иметь на корневом теге `chrome:WindowChromeTheme.Enabled="True"`
  (`xmlns:chrome="clr-namespace:libeLog.Infrastructure;assembly=libeLog"`; внутри самого libeLog — без `;assembly=libeLog`).
  Class-handler на `Loaded` для этого НЕ использовать — без instance-подписчиков `Loaded` доставляется ненадёжно.
- Ссылки на цвета — **только `DynamicResource`** (иначе тема меняется только после перезапуска). `BasedOn` не может быть `DynamicResource`.
- В окнах **не должно быть хардкод-hex**. Цвет = ключ палитры `Areopag.*`.

## Где живут стили

- Общие для обоих приложений: `libeLog/Areopag/Areopag.Common.xaml` (кнопки, базовые шаблоны), `libeLog/StyleDictionary.xaml` (ComboBox/DatePicker/таблицы).
- Только remeLog: `remeLog.WPF/Infrastructure/ImplicitStyles.xaml` — неявные стили (без `x:Key`) и общие `x:Key`-стили.
- **Не задавайте цвета/рамки локально в окне.** Если понадобилось — сначала правьте общий стиль, локально оставляйте только размеры/раскладку.

## Обязательные общие стили (не дублировать в окнах)

- **Кнопки**: `Areopag.Button` / `PrimaryButton` / `DangerButton` / `OkButton`.
  Контент-текст кнопки наследует её `Foreground` автоматически (`Areopag.ButtonContentText` через `ContentPresenter.Resources`). Не задавайте `Foreground` у `TextBlock` внутри кнопки без необходимости.
- **ComboBox**: безрамочный (неявный стиль, `BorderThickness=0`). Рамку локально не включать.
- **TextBox**: базовый стиль `Areopag.TextBox`. Любой inline-стиль (`<TextBox.Style>`) и оконный неявный стиль **обязаны** быть `BasedOn="{StaticResource Areopag.TextBox}"` — иначе стиль заменяется целиком, фон остаётся системно-белым, а текст светлый (тёмная тема нечитаема).
- **DataGrid**: чередующийся фон строк — глобально, ключ `Areopag.RowAlt` (в `ImplicitStyles.xaml`). Окна **не задают** `AlternatingRowBackground`.
- **MenuItem**: всегда `BasedOn="{StaticResource Areopag.MenuItemStyle}"`, иначе системный шаблон рисует колонку под иконки (серая полоса). `ContextMenu` использует плоский шаблон без этой колонки.
- **DatePicker**: `Style="{StaticResource DatePickerOutlineStyle}"` + `CalendarStyle="{StaticResource Areopag.CalendarStyle}"`, `BorderThickness=0`, моно-шрифт, текст центрируется (`VerticalContentAlignment=Center`).
- **Подсказка в пустом поле**: `Areopag.FieldHint` + `DataTrigger` по пустому `Text` поля.
- **Фильтр по станкам**: общий контрол `remeLog.WPF/Views/Controls/MachineFilterSelector.xaml` (кнопка-«комбобокс» + попап). Не повторять разметку в окнах.
- **Иконки**, которые должны быть видны в тёмной теме: тематические копии (`*.Themed`) с цветами `Areopag.IconGrey/IconBlue/IconRed`. Оригиналы в `libeLog/Icons.xaml` не трогать (их использует eLog).

## Шрифты

- Ключи: `Areopag.FontUI` (Golos Text), `Areopag.FontMono` (JetBrains Mono), `Areopag.FontSegoe` (Segoe UI).
- remeLog перекрывает `Areopag.FontUI` на Segoe UI (в `ImplicitStyles.xaml`); eLog остаётся на Golos.

## Запрещённые/устаревшие приёмы

- `FrameworkElementFactory` для шаблонов — использовать XAML `DataTemplate`.
  Если шаблон ячейки зависит от колонки — брать параметр из `{Binding RelativeSource={RelativeSource AncestorType=DataGridCell}, Path=Column.Header}`.
- ComboBox-хак `IsEditable + IsReadOnly + Text` для плейсхолдеров/списков чекбоксов.
- Задание `AlternatingRowBackground` / цвета рамки / `Foreground` контента кнопки локально в окне.

## Сборка и проверка

- Полная сборка: `dotnet build CNCJournal.sln -t:Rebuild -c Debug`.
- Если запущено приложение (VS держит `libeLog.dll` в bin) — сборка падает на копировании. Собирайте в отдельный каталог:
  `dotnet build CNCJournal.sln -t:Rebuild -c Debug -p:OutDir=%TEMP%\cncbuild\`.
- Визуальные проверки темы/стилей делайте зондом `tools/ThemeProbe` (рендер в PNG в `%TEMP%\opencode`), а не «на глаз» в приложении. По завершении зонд удаляйте.

## Code style

- XAML: комментарии по-русски, по делу.
- Не добавлять комментарии в код без необходимости; не менять размеры/шрифты без явной просьбы.
