# 06 — CLI оценивает доказательства quality capabilities

**What to build:** Полный `harness check` формирует evidence-backed readiness view для test, integration и architecture capabilities, не превращая неизвестность или нераспознанную repository-specific реализацию в ложное отрицание.

**Blocked by:** 02 — CLI проверяет .NET-репозиторий; 03 — CLI проверяет web-репозиторий.

**Status:** ready-for-agent

- [ ] CLI использует уже обнаруженные .NET и web surfaces, test projects, dependencies и scripts как evidence, не строя второй расходящийся inventory.
- [ ] Для каждой поддерживаемой capability различаются `detected`, `executed`, `not detected`, `unknown` и `not applicable`.
- [ ] `not detected` сообщает, какое evidence искалось, и не утверждает, что repository capability отсутствует.
- [ ] Найденный test project без доказательства architecture semantics не объявляется полноценной architecture protection.
- [ ] Успешный запуск соответствующей известной команды повышает evidence до `executed`, но не сертифицирует completeness непроверяемых правил.
- [ ] Missing и uncertain capabilities отображаются как readiness gaps и не возвращают exit code `1` в v0.
- [ ] Capability output остаётся компактным, не создаёт scalar AI-ready score и раскрывает детали через `explain`.
- [ ] Fixtures покрывают recognized architecture/integration evidence, executable evidence, no evidence, ambiguous evidence и unsupported stack.
- [ ] Fixture с новым project или module доказывает, что неполный hard-coded inventory не превращается в ложный green, когда gap можно наблюдать.
- [ ] Все capability checks имеют stable identifiers и duration и подчиняются `--only`/`--skip`.
