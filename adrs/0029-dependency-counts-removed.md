# ADR-0029: Dependency counts удалены из актуального контракта

## Status

Accepted. Развивает [ADR-0021](0021-coupling-evidence-grades.md).

## Context

Пилот required-контракта показал, что dependency counts дают два разных вида шума.
`Inferred`-рёбра принимали свойство `string Env` и enum-член `Manual` за ссылки на
одноимённые типы. После ограничения графа доказанными ссылками верх outgoing count всё
равно закономерно занимали integration tests и composition roots, а высокий incoming count
часто отмечал правильно выбранную стабильную абстракцию.

У этих чисел нет универсального remediation. Широкий production-тип уже проверяют размер,
constructor width и cohesion; повтор тестового setup — duplication; направления слоёв —
semantic architecture tests репозитория. Glob-пороги добавили бы знание layout, но не
превратили бы raw count в архитектурное правило.

Документация называла counts неблокирующими, required-policy повышала их до violations, а
новый уровень optional observations создал бы сигнал, который CI не требует прочитать.
Метрика без обязанности исправить или письменно принять находку не принадлежит harness.

## Decision

Начиная с harness 1.5 `dependencies.csharp` сообщает только module dependency cycles:

- цикл строится исключительно из `Proven`-рёбер;
- цикл остаётся blocking при required-policy;
- намеренно принятый цикл или лексический false positive принимает адресный `suppress` с
  обязательной причиной;
- fan-in, fan-out и external import counts не вычисляются как находки;
- `settings.dependencies.csharp` удалён из актуальной схемы и `harness init`;
- `--all` раскрывает полные списки других ограниченных required-метрик, а также все циклы.

Pins до 1.5 сохраняют counts, settings и policy-поведение исходного контракта. Обновление
бинаря без tracked upgrade не меняет их результат. При upgrade явное legacy-setting нужно
удалить; валидатор называет эту миграцию вместо молчаливого игнорирования числа.

## Consequences

### Positive

- `dependencies.csharp` содержит один универсальный дефект с конкретным исправлением.
- Tests и composition roots не требуют glob-исключений и не задают глобальный baseline.
- Высокий fan-in больше не наказывает удачную общую абстракцию.
- Suppress относится только к циклу, а не к контекстному count.
- В контракте нет optional-сигнала, который никто не обязан читать.

### Negative / Risks

- Harness больше не показывает типы с большим числом лексически видимых зависимостей.
- Architecture tests конкретного репозитория должны держать допустимые направления слоёв.
- Лексический резолвер без symbol table может ошибочно связать уникальный внутренний тип с
  одноимённым внешним; для такого доказанного на вид цикла остаётся named suppression.
