# ADR-0017: Применимые проверки required по умолчанию

## Status

Superseded for contract 2.0 by the explicit-policy decision in
[ADR-0032](0032-topology-over-thresholds.md).

## Context

Конфигурация различала документированные `required`, `advisory`, `off` и скрытый четвёртый
режим `Default`. Последний принимал `present: false`, поэтому новый репозиторий оказывался
нестрогим, пока не перечислял нормальные проверки в `policy`. Рабочий конфиг был вынужден
содержать `docs.policy: required` и `frame.tests.integration: required`, хотя никакого
локального решения в этих строках не было.

## Decision

Начиная со schema version 3 каждая выбранная применимая проверка имеет policy `required`,
если более точный check- или group-override отсутствует. `present: false` у frame-вопроса
поэтому является blocking-нарушением. `policy` остаётся механизмом явного отклонения от
нормы: `advisory` сохраняет находку или readiness gap без провала, `off` не запускает
проверку. Значение `required` читается для единообразия формы, но в обычном конфиге
избыточно.

`NotApplicable` остаётся отдельным исходом и не превращается в нарушение: строго требовать
можно только то, что относится к репозиторию.

## Consequences

- Новый конфиг строг без списка boilerplate-строк `required`.
- Осознанные послабления видны непосредственно в `policy`.
- Конфиги версии 2 требуют явного перехода на version 3.

## Contract 2.0

Скрытого default больше нет: `.harness.json` обязан перечислить каждый shipped check с
`required`, `advisory` или `off`. Инварианты `architecture.sliced-dotnet` и
`complexity.csharp` могут быть только `required`. Добавление новой проверки делает старый
конфиг incomplete, пока владелец явно не выберет policy.
