# 02 — CLI проверяет .NET-репозиторий

**What to build:** `harness check` обнаруживает поддерживаемый .NET surface и через установленный SDK выполняет немутирующие formatting, build и test gates с воспроизводимыми командами, timings и однозначными exit semantics.

**Blocked by:** 01 — NativeAOT CLI проверяет документационную политику.

**Status:** ready-for-agent

- [ ] CLI обнаруживает решения, проекты и standard tool evidence в поддерживаемом .NET fixture без обязательного manifest.
- [ ] Репозиторий без .NET surface получает `not applicable`, а не failure или выдуманный execution plan.
- [ ] Formatting запускается только в verification-режиме; build и tests используют установленный SDK и не применяют исправления.
- [ ] Каждая запущенная команда, check identifier, status и duration видны в компактном результате.
- [ ] Полностью зелёный .NET fixture возвращает exit code `0`.
- [ ] Доказанное formatting, compilation или test нарушение возвращает exit code `1` и локализованное evidence.
- [ ] Отсутствующий SDK, неоднозначный execution plan или ошибка запуска возвращает exit code `2` и не обвиняет repository content.
- [ ] CLI не устанавливает SDK, не меняет project files и не изменяет tracked source/configuration; conventional ignored build outputs допустимы.
- [ ] `--only` и `--skip` независимо выбирают .NET gates и сохраняют skipped gates в summary.
- [ ] `explain` описывает назначение, evidence и безопасную следующую реакцию для каждого .NET check.
- [ ] Позитивные, violating и incomplete fixtures проверяются через compiled CLI process с реальным SDK там, где он доступен.
