# 03 — CLI проверяет web-репозиторий

**What to build:** `harness check` обнаруживает TypeScript/Vite/React surface, выбирает package manager по repository evidence и запускает существующие немутирующие quality scripts, честно отделяя нарушения от отсутствующих capabilities.

**Blocked by:** 01 — NativeAOT CLI проверяет документационную политику.

**Status:** ready-for-agent

- [ ] CLI определяет поддерживаемый package manager по lockfile и не выбирает его по глобальным пользовательским предпочтениям.
- [ ] Репозиторий без web surface получает `not applicable`, а не failure или выдуманный execution plan.
- [ ] Существующие немутирующие format verification, lint, typecheck, test и build scripts распознаются и запускаются через repository package manager.
- [ ] Отсутствующий standard script отображается как readiness gap, а не автоматически синтезируется инструментом.
- [ ] CLI не запускает заведомо mutating format script как проверку, не устанавливает dependencies и не изменяет lockfile.
- [ ] Каждая запущенная команда, check identifier, status и duration видны в компактном результате.
- [ ] Полностью зелёный web fixture возвращает exit code `0`; доказанный lint, typecheck, test или build defect возвращает `1`.
- [ ] Отсутствующий executable, конфликтующее package-manager evidence или невозможность запуска возвращает exit code `2`.
- [ ] `--only`, `--skip` и `explain` работают для каждого web gate через тот же CLI interface.
- [ ] Позитивные, violating, missing-capability и incomplete fixtures проверяются через compiled CLI process с реальным package manager там, где он доступен.
- [ ] После выполнения tracked source, configuration и dependency lockfiles fixture-репозитория не изменены.
