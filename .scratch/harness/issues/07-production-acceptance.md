# 07 — Полный check проходит production acceptance

**What to build:** Собранный v0 harness полезно и честно проверяет production-scale `services-platform` и дополнительные реальные repositories, оставаясь переносимым, компактным и измеримым без hard-coded знания канонического layout.

**Blocked by:** 02 — CLI проверяет .NET-репозиторий; 03 — CLI проверяет web-репозиторий; 04 — CLI показывает maintainability hotspots; 05 — CLI показывает нормализованные дубликаты; 06 — CLI оценивает доказательства quality capabilities.

**Status:** ready-for-agent

- [ ] Полный `harness check` успешно строит execution plan для актуального `services-platform` без repository-name checks и hard-coded production paths.
- [ ] Existing .NET и web gates запускаются или честно классифицируются как readiness/incomplete; repository-specific architecture semantics не угадываются.
- [ ] Maintainability и duplication findings дают bounded actionable summary, несмотря на production-scale количество source files и clones.
- [ ] Documentation policy корректно сообщает текущие deviations как advisory и не изменяет documentation.
- [ ] Итоговый output помещает violations, readiness gaps, incomplete/skipped checks и timings выше успешного шума.
- [ ] Каждый attempted gate предоставляет duration, достаточную для будущего решения об optimization, но v0 не вводит affected selection, cache или parallelism.
- [ ] После запуска tracked repository state не изменён; conventional ignored outputs учитываются отдельно.
- [ ] Harness также запускается как минимум на одном дополнительном реальном repository и не переносит assumptions из `services-platform` как универсальную policy.
- [ ] Любая обнаруженная ложная blocking-классификация исправлена или понижена до advisory/unknown и закреплена regression fixture.
- [ ] Любое новое universal blocking rule принимается только вместе с доказанным harm, localized remediation и negative fixture.
- [ ] Полный acceptance run завершается воспроизводимо и документирует измеренный timing profile для будущих решений, не превращая его в постоянную narrative spec.
