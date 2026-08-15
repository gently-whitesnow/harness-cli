# 05 — CLI показывает нормализованные дубликаты

**What to build:** Встроенная advisory-проверка заменяет копируемый duplication Python-скрипт, находит повторяющиеся cross-file C# structures, группирует evidence без лавины соседних окон и не представляет лексическое сходство как доказанный semantic defect.

**Blocked by:** 01 — NativeAOT CLI проверяет документационную политику.

**Status:** ready-for-agent

- [ ] `harness check` автоматически применяет duplication check к обнаруженным C# sources и не применяет его к неподдерживаемому stack.
- [ ] Проверка нормализует identifiers, literals, comments и whitespace детерминированно и документирует точную единицу сравнения.
- [ ] Cross-file repetitions обнаруживаются с устойчивыми locations и достаточным source context для оценки агентом.
- [ ] Перекрывающиеся соседние windows одного clone объединяются или иным образом представляются без искусственного умножения независимых findings.
- [ ] Generated code и явно повторяемые build artifacts исключаются без repository-local копии analyzer script.
- [ ] Finding остаётся advisory, подчёркивает lexical nature evidence и сам по себе не возвращает exit code `1`.
- [ ] `explain` описывает normalization, ограничения, возможные false positives и критерии, по которым агент может отказаться от рефакторинга.
- [ ] Compatibility fixtures сохраняют полезные случаи существующего Python-скрипта, а targeted negative fixtures покрывают strings, chars, interpolation, raw strings и semantically unrelated templates.
- [ ] Проверка сообщает duration, выдаёт bounded output на большом наборе clones и не изменяет analyzed repository.
