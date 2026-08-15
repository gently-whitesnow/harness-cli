# 08 — Пользователь устанавливает первый v0-релиз

**What to build:** Версионированный v0 выпускается как проверенные self-contained NativeAOT artifacts для поддерживаемых macOS/Linux architectures, устанавливается без отдельного .NET runtime и запускает тот же полный `harness check`, который прошёл production acceptance.

**Blocked by:** 07 — Полный check проходит production acceptance.

**Status:** ready-for-agent

- [ ] Release build создаёт target-specific NativeAOT artifacts для поддерживаемых macOS и Linux x64/arm64 runners без AOT warnings.
- [ ] Каждый распространяемый artifact smoke-тестируется на matching operating system и architecture до публикации.
- [ ] Smoke test доказывает, что CLI запускается без отдельно установленного .NET runtime; repository-specific SDKs требуются только для применимых external gates.
- [ ] Release artifacts публикуются под неизменяемой version tag вместе с SHA-256 checksums.
- [ ] Installation instructions позволяют macOS и Linux пользователю получить правильный artifact и проверить checksum без сборки исходников.
- [ ] Установленный CLI выполняет `check`, `--only`, `--skip` и `explain` с теми же exit semantics, что acceptance build.
- [ ] Документация установки остаётся в пределах согласованной корневой Markdown policy.
- [ ] Release process не публикует mutable `latest` как единственный воспроизводимый reference и не требует consumer CI template.
- [ ] Ошибка сборки или smoke test любого заявленного artifact блокирует публикацию этого artifact.
