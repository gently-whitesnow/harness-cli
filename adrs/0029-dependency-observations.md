# ADR-0029: Dependency counts — доказанные наблюдения, а не зональная норма

## Status

Accepted. Развивает [ADR-0021](0021-coupling-evidence-grades.md) и уточняет границу
[ADR-0027](0027-required-findings-are-blocking.md).

## Context

Пилот required-контракта показал два разных источника шума `dependencies.csharp`.
Лексический reader включал в fan-in/fan-out `Inferred`-рёбра: свойство `string Env` и
enum-член `Manual` становились ссылками на одноимённые типы. После удаления этих рёбер
верх fan-out всё равно закономерно занимали integration tests и composition roots.

Glob-исключения или отдельные пороги для `tests/**` способны скрыть второй эффект, но не
исправляют первый и вводят repository layout в универсальную проверку. Высокий fan-out
composition root, fixture или стабильного domain hub — точное число, но не дефект.

Документация обещала, что approximate counts не blocking, тогда как required-policy
повышала их до violations. Компактный отчёт показывал пять субъектов и число остальных,
но не давал получить полный список без повторной реализации анализа.

## Decision

Начиная с harness 1.5:

- `proven outgoing type references` и `proven incoming type references` считают только
  уникальные `Proven`-рёбра;
- эти два счётчика и `external import fan-out` имеют severity `Observation`;
- observation не является enforceable-находкой, не повышается required-policy и не требует
  `suppress`; comparison point только фильтрует показываемый сигнал;
- доказанный module dependency cycle остаётся blocking и может быть смягчён policy или
  принят именованным `suppress`;
- `harness check --all` включает verbose и печатает каждый измеренный субъект; обычный
  verbose сохраняет top-five и счётчик остальных.

Pins до 1.5 сохраняют resolved counts из `Proven` и `Inferred` и прежнее повышение policy.
Обновление бинаря без tracked upgrade не меняет их вердикт или компактный отчёт.

## Consequences

### Positive

- Совпадение имени члена с типом больше не раздувает текущие dependency counts.
- Tests, composition roots и hubs видны без специальных знаний о layout и не ломают CI.
- Универсальный доказанный инвариант — отсутствие module cycle — остаётся строгим.
- Агент получает полный inventory той же реализации через `--all`.

### Negative / Risks

- Proven-only counts занижены там, где зависимость видна лишь компилятору или написана как
  member access; это осознанная цена отсутствия symbol table.
- Comparison point больше нельзя использовать как fan-out budget. Если репозиторию нужен
  такой зональный контракт, его должен держать semantic architecture test.
- `--all` на крупном монорепозитории может напечатать большой отчёт, поэтому не является
  default.
