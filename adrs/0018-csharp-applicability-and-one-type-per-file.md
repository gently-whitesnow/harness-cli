# ADR-0018: Общая применимость C# и один file-level тип

## Status

Accepted.

## Context

Комментарии, maintainability и duplication используют один C# lexical reader, но раньше
не имели общей оси применимости: репозиторию без применимого C#-анализа пришлось бы
выключать проверки по одной. Новое структурное правило также должно войти в то же
семейство, а не добавлять ещё один независимый флаг.

## Decision

Schema version 3 вводит optional `applicability`. Каждая C#-проверка объявляет ключ
`csharp`; запись ниже завершает все такие проверки исходом `NotApplicable` до чтения
исходников:

```json
"applicability": {
  "csharp": { "applicable": false, "reason": "why C# analysis does not apply" }
}
```

Отсутствие записи означает применимость. `applicable: true` не хранится: это норма, а не
исключение.

Blocking-проверка `types-per-file.csharp` допускает не более одного верхнеуровневого
`class` или `record` в каждом tracked authored `.cs`-файле. Вложенные типы не задают второй
file-level concept; `interface`, `struct` и `enum` не входят в сформулированное правило.
Generated-файлы исключает общий C# source reader.

## Consequences

- Один честный ответ отключает весь неприменимый C#-анализ и объясняет причину.
- `--only csharp` выбирает семейство целиком.
- Файлы с несколькими верхнеуровневыми классами или records нужно разнести либо оформить
  именованное исключение.
