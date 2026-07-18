# MessageBoxWindow — ручная проверка

## Owner / Z-order
- [ ] В PartsInfoWindow по кнопке «Записать» — диалог НЕ вытягивает MainWindow
- [ ] Из модального окна (PartsInfoWindow и т.п.) — владелец = это окно, не MainWindow
- [ ] Из MainWindow — владелец = MainWindow
- [ ] Global error handler (нет MainWindow) — owner:null, `WindowStartupLocation="CenterOwner"` не падает

## MMB (средняя кнопка)
- [ ] MMB по фону/тексту/иконке — срабатывает дефолтная кнопка
- [ ] MMB по любой кнопке — срабатывает дефолтная (не та, под которой курсор)
- [ ] MMB на кнопке Cancel/Отмена — срабатывает дефолтная, не Cancel

## Default button / Enter
- [ ] Дефолтная кнопка выделена жирным + IsDefault-рамка + фокус
- [ ] Enter — срабатывает дефолтная кнопка
- [ ] Escape — срабатывает IsCancel-кнопка
- [ ] Focus на дефолтной кнопке после открытия

## Button clicks
- [ ] Все комбинации кнопок: OK, OKCancel, YesNo, YesNoCancel
- [ ] Все defaultButton варианты: First/Second/Third, Yes/No/Ok/Cancel
- [ ] Возвращаемые MessageBoxResult: OK, Cancel, Yes, No

## Close via X
- [ ] YesNo — X возвращает No
- [ ] OKCancel/YesNoCancel — X возвращает Cancel
- [ ] OK — X возвращает Cancel

## Иконки
- [ ] Error — красный Outline-круг с X
- [ ] Warning — жёлтый Outline-треугольник с !
- [ ] Information — синий Outline-круг с i
- [ ] Question — синий Outline-круг с ?
- [ ] None — иконка скрыта
- [ ] Масштаб: 32×32, не мелкие, не растянутые

## Ручной дамп вызовов по проектам
- [ ] `eLog/ViewModels/MainWindowViewModel.cs` — ~20 вызовов
- [ ] `eLog/App.xaml.cs` — global error handler
- [ ] `eLog/Views/Dialogs/*.xaml.cs` — 5 файлов
- [ ] `eLog/Views/AppSettingsWindow.xaml.cs` — 3 вызова
- [ ] `remeLog/ViewModels/MainWindowViewModel.cs` — ~30 вызовов
- [ ] `remeLog/App.xaml.cs` — global error handler
- [ ] `remeLog/Infrastructure/Util.cs` — WinForms→WPF конверсия
- [ ] `remeLog/Views/*.xaml.cs` — 15+ файлов
- [ ] `QCTasks` — ConfirmDialog / InputConfirmDialog (MBM + defaultIsYes)
- [ ] `libeLog` — FanucService, Logs, WorkbookExtensions

## Non-UI thread safety
- [ ] Все вызовы с UI-треда (диспетчера)
- [ ] Нет вызовов из фоновых потоков без dispatcher.Invoke
