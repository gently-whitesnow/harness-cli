# 04 — CLI показывает maintainability hotspots

**What to build:** Встроенная advisory-проверка заменяет копируемый maintainability Python-скрипт, измеряет C# hotspots, точно называет приближённые метрики и даёт агенту достаточно evidence для самостоятельного решения о рефакторинге.

**Blocked by:** 01 — NativeAOT CLI проверяет документационную политику.

**Status:** ready-for-agent

- [ ] `harness check` автоматически применяет maintainability check к обнаруженным C# sources и не применяет его к неподдерживаемому stack.
- [ ] Проверка измеряет logical file/type/method size, lexical control-flow complexity, constructor arity, public surface и import fan-out с явно документированными формулами.
- [ ] Названия и объяснения не выдают import fan-out за semantic coupling, constructor arity за dependency count или lexical complexity за compiler control-flow metric.
- [ ] Generated code и conventional build-output locations исключаются детерминированно и объяснимо.
- [ ] Finding содержит stable check identifier, metric, measured value, comparison point, subject и source location без неконтролируемого вывода всего repository inventory.
- [ ] Finding остаётся advisory и сам по себе не возвращает exit code `1`.
- [ ] `explain` описывает формулу, ограничения анализа, возможный ущерб и необходимость инженерной оценки вместо обязательного механического рефакторинга.
- [ ] Совместимые fixture cases фиксируют полезное поведение существующего Python-скрипта до его удаления из consuming repositories.
- [ ] Негативные fixture cases покрывают comments/strings, records, expression-bodied members, nested types, multiline signatures и другие обнаруженные lexical edge cases.
- [ ] Проверка сообщает duration и не изменяет analyzed repository.
