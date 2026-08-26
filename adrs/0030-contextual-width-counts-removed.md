# ADR-0030: Контекстные метрики ширины удалены из актуального контракта

## Status

Accepted. Развивает [ADR-0006](0006-heuristics-are-advisory.md) и следует
[ADR-0023](0023-release-version-as-the-verification-contract.md).

## Context

`public declared members` лексически считала объявления с модификатором `public` напрямую
внутри типа. Одно число при этом сравнивало поля DTO и options, операции facade, члены
domain model и поведение service. Большая поверхность может быть признаком смешанных
обязанностей у service, но естественной формой data contract.

`constructor parameter count` также не различала values, options и collaborators. Более
того, positional record намеренно исключался как форма данных, а эквивалентный data class
с primary или declared constructor считался. Результат зависел от синтаксиса, хотя
исправление требует знать роль типа. Parameter object мог формально уменьшить count, но
скрыть те же зависимости без улучшения дизайна.

У этих метрик нет универсального remediation. Деление связного DTO или facade ради общего
порога добавляет косвенность, а повышение repository-wide порога до ширины самого широкого
легитимного контракта убирает сигнал там, где он мог быть полезен. Path- и type-specific
исключения сделали бы единый harness зависимым от layout и поощряли бы обход рамки.

## Decision

Начиная с harness `1.6.0`:

- `maintainability.csharp` не создаёт measurements `constructor parameter count` и
  `public declared members`;
- `settings.maintainability.csharp.constructorParameters` и `publicMembers` удалены из
  актуальной схемы и из `harness init`;
- явные legacy-ключи отклоняются с миграционными сообщениями, а не игнорируются;
- pins `1.0.0`–`1.5.x` продолжают принимать settings, вычислять обе метрики и получать тот
  же verdict при запуске бинарём `1.6.0` или новее.

Generated-source policy и остальные maintainability-метрики этим решением не меняются.

## Consequences

### Positive

- Один профиль применим к authored C# без исключений для DTO, models или facades.
- Удалены counts, для которых нельзя назвать одинаковое исправление во всех типах.
- Новый бинарь продолжает воспроизводить старые repository pins.

### Negative / Risks

- Harness больше не подсвечивает service только из-за ширины конструктора и facade только
  из-за числа public операций.
- Ширину зависимостей или API при необходимости должен держать семантический анализ
  конкретного репозитория или review, понимающий роль типа.
