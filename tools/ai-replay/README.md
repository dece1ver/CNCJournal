# AiReplay — offline-прогон ИИ-анализа на исторических данных

Проверка кандидата-версии промпта или модели **до выкатки в прод**: инструмент
берёт сохранённые запросы (request-логи AiService), повторно отправляет их в
сервис и сверяет результат с решениями аналитиков из `ai_day_reviews`. Прод-данные
при этом не изменяются.

Точность считается идентично `точность по промту.sql`:
`совпадение` / `AI пропустил` (Missed) / `AI лишний флаг` (FalseAlarm) по паре
`Decision` × `requiresReview` при `IsFullyReviewed = 1`.

## Откуда берутся данные

- **Запросы** — AiService с версии 2026-07 пишет каждый анализ в
  `request_logs/yyyy-MM/*.json` рядом с exe (настройка `RequestLog` в
  appsettings.json). В файле: полный запрос, итоговый ответ (базовая линия) и
  версия промпта.
- **Решения аналитиков** — напрямую из SQL (`--connection`), либо CSV-экспорт из
  SSMS (`--labels-csv`, колонки `Machine,ShiftDate,Decision[,Comment,AiFeedback]`,
  разделитель `,` или `;`, `ShiftDate` в формате `yyyy-MM-dd`).

## Примеры

```powershell
# Базовый прогон: как текущая версия промпта отработала бы весь датасет
dotnet run -- --log-dir \\aihost\AiService\request_logs `
  --connection "Server=SQLSRV01;Database=stanki;Integrated Security=true;TrustServerCertificate=true"

# A/B: тот же датасет через промпт-кандидат prompts/system_prompt.candidate.txt
dotnet run -- --log-dir .\request_logs --connection "..." --profile candidate

# Другая модель
dotnet run -- --log-dir .\request_logs --connection "..." --model qwen3:32b

# Ограниченный прогон по одному станку, метки из CSV
dotnet run -- --log-dir .\request_logs --labels-csv labels.csv --machine Goodway --limit 20
```

Все опции: `dotnet run` без аргументов выводит справку.

## Результат

- CSV (`--out`, по умолчанию `replay_results_*.csv`, `;`-разделитель, UTF-8 BOM
  для Excel): по каждому сутко-станку — решение аналитика, базовый результат из
  лога, повторный результат, вердикты, `Changed` (изменился ли ответ относительно
  базовой линии), `NeedsAdjudication`.
- Сводка в консоли: Baseline vs Replay (N / Correct / Missed / FalseAlarm / Accuracy).
- Список расхождений с аналитиком — **не считайте их автоматически ошибкой ИИ**:
  аналитик тоже может ошибаться, спорные строки нужно разобрать вручную
  (колонки `AnalystComment` / `AnalystAiFeedback` в помощь).

## Замечания

- На один (станок, дата) в логах может быть несколько записей — берётся самая свежая.
- Прогон последовательный: Ollama всё равно обрабатывает запросы по одному.
- `--profile` работает поверх любого запроса — профиль из самого лога
  (от `cnc_machines.AiPromptProfile`) при этом переопределяется.
