# ADR-0037: Сегменты слайса именуются по назначению

## Status

Accepted. Уточняет конвенции `sliced-dotnet/1` из
[ADR-0033](0033-canonical-standard-over-declarations.md).

## Context

Первая миграция внешнего приложения показала ложноположительный архитектурный итог: один
широкий слайс мог сохранить технические каталоги `Services`, `Validators`, `Constants`,
`Repositories` и остаться зелёным. Реализованная конвенция `generic-slice-directory` знала
только пять имён, рекурсивно считала сегментами каталоги на любой глубине и печатала их как
observations, которые `required` policy не могла сделать blocking.

Feature-Sliced Design определяет сегмент как непосредственное техническое деление внутри
слайса и рекомендует называть его по назначению содержимого, а не по виду кода. Steiger на
commit `e21b75e71dc9a7805f9045d3a29c01566167e1c2` реализует это правилом
`fsd/segments-by-purpose`; соседнее `fsd/no-segments-on-sliced-layers` не позволяет
техническому сегменту притвориться слайсом на sliced-слое. Название бизнес-слайса и его
semantic cohesion остаются знанием продукта: файловая система не доказывает, что один
каталог содержит несколько бизнес-возможностей.

## Decision

1. `architecture.sliced-dotnet` переносит generic-словарь Steiger
   `segments-by-purpose`: `component`, `helper`, `util`, `constant`/`const`, `type`,
   `store`, `modal`, `service`, `function`, `class`, `enum`, `interface`, `decorator`,
   `schema`, `handler`, `fixture`, `middleware`, `validator`/`validation`, `resolver`,
   `mutation`, `asset` и их формы множественного числа.
2. Backend-расширение добавляет `Common`, `Manager`, `Managers`, `Repository` и
   `Repositories`. Эти названия описывают вид или паттерн кода и на первом внешнем
   потребителе скрыли несколько назначений в одном bucket.
3. Сегментом считается только непосредственный каталог внутри слайса. Вложенный каталог —
   внутренняя организация сегмента и повторно этим правилом не классифицируется.
4. `Host` и `Shared` — sliceless-слои sliced-dotnet; их непосредственные каталоги также
   являются сегментами и проверяются тем же словарём.
5. Если `Application/Features/<Группа>/<Лист>` без файла в группе распознан как grouped
   slice, essence-based имя листа не принимается за бизнес-слайс. Находка
   `no-segments-on-sliced-layers` требует бизнес-имя либо переноса сегмента внутрь явно
   названного слайса.
6. Находка становится обычным policy-controlled `Finding`: `required` блокирует её,
   `advisory` оставляет видимой без провала, `off` не запускает проверку. Адресных
   исключений нет. Если C#-граф прочитать невозможно, outcome остаётся `Incomplete` вместе
   с найденными сегментами: `required` применяет находку и проваливает гейт, `advisory` не
   маскирует неполное измерение.
7. Имя стандарта остаётся `sliced-dotnet/1`: ADR-0033 уже объявил generic-каталоги
   конвенцией до накопления негативных фикстур; это решение фиксирует такие фикстуры и
   завершает предусмотренное усиление.

## Consequences

### Positive

- Технические мусорные сегменты больше не проходят `required`-гейт только потому, что
  лежат внутри формально корректного слайса.
- Правило детерминировано по tracked-дереву, выполняется без C#-графа и предлагает
  локальное исправление: назвать сегмент по назначению его содержимого.
- Семантика следует двум правилам Steiger: проверяется непосредственная граница сегмента,
  а техническое имя не может пройти как leaf-слайс из-за эвристики группировки.

### Negative / Risks

- Запрет `Registry/Repositories` закрывает наблюдавшийся bucket, но хорошее имя сегмента не
  доказывает cohesion слайса. `Registry/Persistence` всё ещё может скрыть несколько
  бизнес-возможностей; выделение настоящих слайсов остаётся архитектурным решением и
  предметом review.
- Словарь основан на соглашениях и может потребовать уточнения на новых backend-формах.
  Смягчение делается policy всей проверки, а не исключением отдельного пути.

## References

- Feature-Sliced Design, slices and segments —
  https://feature-sliced.design/docs/reference/slices-segments
- Steiger, `segments-by-purpose` at `e21b75e7` —
  https://github.com/feature-sliced/steiger/blob/e21b75e71dc9a7805f9045d3a29c01566167e1c2/packages/steiger-plugin-fsd/src/segments-by-purpose/index.ts
- Steiger, `no-segments-on-sliced-layers` at `e21b75e7` —
  https://github.com/feature-sliced/steiger/blob/e21b75e71dc9a7805f9045d3a29c01566167e1c2/packages/steiger-plugin-fsd/src/no-segments-on-sliced-layers/index.ts
