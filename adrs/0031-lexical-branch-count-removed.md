# ADR-0031: Лексический branch count удалён

## Status

Accepted. Развивает [ADR-0006](0006-heuristics-are-advisory.md) и следует границам
[ADR-0009](0009-nativeaot-as-a-build-invariant.md).

## Context

`lexical branch count` назывался числом ветвлений, но вычислял `1` плюс число выбранных
токенов. Поэтому метод без ветвлений имел значение `1`, а `do … while` увеличивал его дважды:
отдельно за `do` и `while`. В то же время ternary conditionals, pattern combinators и arms
`switch` expressions не учитывались. `case` в `goto case` и contextual identifier `when`
могли, наоборот, создать ложный инкремент.

Формула была воспроизводима, но не обладала одной интерпретацией. Начальное значение
соответствовало cyclomatic complexity, набор токенов — приближённым decision points, а имя —
ветвям control flow. Измерение систематически зависело от выбранной формы одинакового C#-кода.

Точный control-flow count требует синтаксической модели языка и построения CFG. Roslyn не
берётся из-за размера и несовместимости с NativeAOT-инвариантом, а расширение собственного
лексического reader до второго неполного C# parser увеличило бы сложность harness без
доказуемой точности. Единого remediation для превышения также нет: state machine или parser
могут иметь естественно разветвлённый метод.

## Decision

Начиная с harness `1.6.0`:

- `maintainability.csharp` не создаёт measurement `lexical branch count`;
- `settings.maintainability.csharp.branches` удалён из схемы и из `harness init`;
- явный `branches` отклоняется с причиной, а не игнорируется;
- legacy-вычисление удалено, в том числе для старых pins: сохранение заведомо
  противоречивого evidence не оправдывает постоянный код совместимости.

Если control-flow complexity станет необходимым контрактом конкретного репозитория, её
должен вычислять compiler-backed analyzer в toolchain этого репозитория. Harness может
спросить о наличии такого анализа через frame, но не подменяет его лексической формулой.

## Consequences

### Positive

- Required-by-default больше не блокирует репозиторий на числе без устойчивого смысла.
- Из harness удалён отдельный scanner, который неизбежно отставал от синтаксиса C#.
- Maintainability называет только непосредственно наблюдаемый размер authored source.

### Negative / Risks

- Harness больше не подсвечивает разветвлённые методы самостоятельно.
- Новый бинарь может убрать legacy branch finding у старого pin. Это осознанное безопасное
  послабление: оно не создаёт новых blocking findings, а единственный пользователь может
  поднять tracked pin вместе с обновлением.
