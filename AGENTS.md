# Harness CLI — навигация для агента

Standalone CLI, который держит одну и ту же harness-рамку над разными репозиториями и объясняет через ошибки, как должно быть. Он не запускает toolchain репозитория: тесты, сборку и линтеры гоняет CI через исполняемый `verify.entrypoint`-скрипт, проверяемый по git-индексу ([ADR-0046](adrs/0046-unified-verification-entry-point.md), [ADR-0070](adrs/0070-contract-4-goodhart-filter.md)).

## Рамка

Корневой tracked `.harness.json` задаёт `version` и commit-интеграцию; `projects` регистрирует непересекающиеся каталоги с рамками без наследования. Корень покрывает остальные файлы; графы и дубли только локальные. `check --project <path>` проверяет корень и проект после валидации всех конфигов ([ADR-0060](adrs/0060-explicit-workspace-projects.md)).

Рамка явная: проверка вне `policy` не запускается (одна строка `outside the frame`); названная несёт `required` (блокирует), `advisory` (видима) или `off` и полную секцию `settings.<id>`, если её семейство читает настройки. Скрытых дефолтов нет; секция не названной проверки и policy без `applicability` оси — `Incomplete`. Запрещено адресное подавление файла или находки, а не выключение проверки целиком. `harness.coverage` находит ось с tracked-исходниками без `applicability` и печатает фрагмент `init`; отказ — `{ "applicable": false, "reason": "..." }` ([ADR-0054](adrs/0054-explicit-only-frame.md), [ADR-0027](adrs/0027-required-findings-are-blocking.md), [ADR-0035](adrs/0035-policy-switch-is-uniform.md)).

Проверка называется `<семейство>.<язык>`, `Group` — семейство, `Applicability` — язык. Язык-нейтральное ядро живёт в `Structure/`, чтение исходника — за `ILanguageAnalyzer` в `Languages/<Язык>/` ([ADR-0018](adrs/0018-csharp-applicability-and-one-type-per-file.md), [ADR-0022](adrs/0022-language-axis.md)).

## Оси и проверки

- **C#**: граф строится средствами BCL; `dependencies.csharp` доказывает циклы модулей по `Proven`-рёбрам ([ADR-0021](adrs/0021-coupling-evidence-grades.md), [ADR-0029](adrs/0029-dependency-counts-removed.md)). `complexity.csharp` считает среднюю достижимость файлов и размер SCC по продукту (внутри зон `sliced-dotnet` или вне tracked тестовых проектов); дефолт `8.0 / 0` ([ADR-0042](adrs/0042-dsm-over-the-product-in-files.md), [ADR-0048](adrs/0048-dsm-product-boundary-without-a-zone.md), [ADR-0052](adrs/0052-dsm-ceiling-is-a-declared-setting.md)). `duplication.csharp` — `30/90`. `functions.csharp` — собственные лексические токены `ownTokens <= 700` ([ADR-0063](adrs/0063-function-own-lines.md), [ADR-0070](adrs/0070-contract-4-goodhart-filter.md)). `types-per-file.csharp` — один top-level класс или record на файл.
- **Go**: `duplication.go` (`30/90`), `complexity.go` (DSM по пакетам через `go.mod`, `8.0 / 0`), `functions.go` (`ownTokens <= 700`), `lint-suppressions.go` (блокирует любой адресный `//nolint`; глобальные требуют id/reason в `repositoryWide`) ([ADR-0055](adrs/0055-go-language-axis.md)).
- **TypeScript**: `dependencies.typescript` (циклы каталогов по литеральным импортам, включая type-only), `duplication.typescript` (`30/90`), `complexity.typescript` (файловый DSM с прозрачными barrels, `8.0 / 0`); node/tsc не нужен ([ADR-0064](adrs/0064-typescript-language-axis.md)).
- **Ansible**: детектируется по маркерам; `dependencies.ansible` (циклы ролей) и `lint-suppressions.ansible` (адресный `noqa` блокирует) ([ADR-0059](adrs/0059-focused-ansible-release.md)).
- **.NET (`dotnet`)**: hardened `Directory.Build.props`, central package versions в `Directory.Packages.props`, `.slnx`, эталонный `.editorconfig`, запрет адресных подавлений warnings (`warning-suppressions.dotnet`; глобальные требуют `repositoryWide`) ([ADR-0019](adrs/0019-dotnet-repository-policy.md), [ADR-0044](adrs/0044-editorconfig-baseline-and-warning-suppressions.md), [ADR-0067](adrs/0067-reviewed-weakening-in-the-frame.md)).
- **Архитектура (`sliced-dotnet/1`)**: слои `Host`, `Api`, `Consumers`, `Application`, `Domain`, `Infrastructure` × слайсы `<Слой>/<Слайс>` в корне слоя. Публичный API слайса — `Contracts/`, кросс-импорт — `X/<Потребитель>`, слой = одна сборка `.csproj`. Восемь самостоятельных ID группы `architecture.sliced-dotnet`: `zone-shape`, `slice-shape`, `segment-names`, `layer-assemblies`, `dependency-direction`, `slice-isolation`, `public-api`, `cross-api`. Библиотека отвечает `"architecture": { "applicable": false, "reason": "..." }` ([ADR-0033](adrs/0033-canonical-standard-over-declarations.md), [ADR-0050](adrs/0050-domain-is-the-bottom-layer.md)–[ADR-0053](adrs/0053-explicit-architecture-checks.md)).
- **Verify (`verify.entrypoint`)**: проверяет по git-индексу, что `settings."verify.entrypoint".path` tracked, имеет mode `100755`, непустой и вызывает `harness check` на некомментируемой строке ([ADR-0070](adrs/0070-contract-4-goodhart-filter.md)).
- **Коммиты**: `settings.commits` задаёт `language` (`ru`/`en`) и `requireSetup`. `harness setup` ставит общий шаблон и `commit-msg` hook в `<git-common-dir>`, разрешающий бинарь в момент коммита. В CI используется `harness commits check <base>..<head>` ([ADR-0020](adrs/0020-commit-message-contract-and-clone-setup.md), [ADR-0065](adrs/0065-hook-resolves-the-harness-at-commit-time.md)).

## Контракт и доказательства

`version` фиксирует релиз (`"4.0.0"`) или `"latest"`. Бинарь исполняет только свой контракт; другой pin даёт `Incomplete`, а мигрирует только `harness upgrade` ([ADR-0023](adrs/0023-release-version-as-the-verification-contract.md), [ADR-0032](adrs/0032-topology-over-thresholds.md)). `harness init` детектирует оси по git-индексу (`--languages` заменяет детекцию) и пишет все проверки как `required` ([ADR-0058](adrs/0058-required-initial-frame.md)).

Доказательство — только tracked-файл: созданный без `git add` файл невидим. Проверка обязана объявить читаемые по имени файлы в `Evidence` и читать через `context.Tracked`/`context.Nearest` ([ADR-0026](adrs/0026-untracked-evidence-is-named-in-the-report.md)).

## Раскладка

- `src/Harness` — CLI (NativeAOT, слой = проект `Harness.<Слой>.csproj`, публикуется `Host`):
  - `Host/` — composition root и статический реестр.
  - `Api/Harness/Cli/` — разбор командной строки.
  - `Application/Harness/` — проверки, конфигурация, движок, commit-команды, отчёт и `Contracts/` ([ADR-0034](adrs/0034-language-axis-in-sliced-dotnet.md)).
  - `Domain/Harness/` — `Structure/`, `Languages/`, `Evidence/`, `Versioning/`.
  - `Infrastructure/Harness/` — `Git/` и `Languages/<Язык>/`.
- `tests/Harness.Tests` — приёмочные тесты на скомпилированном бинаре ([ADR-0001](adrs/0001-compiled-cli-as-only-test-seam.md)).
- `site/` — публичный лендинг, зеркалирующий реестр проверок и версию (`site/AGENTS.md`, [ADR-0047](adrs/0047-landing-mirrors-the-contract.md)).
- `adrs/` — решения (`adrs/REGISTRY.md`, `adrs/.template.md`).

## Команды

Репозиторий публичный: tracked-тексты не называют внутренние репозитории, сервисы, домены, хосты и локальные пути.

```sh
./harness init /path/to/repository                 # создать рамку по индексу
./harness upgrade                                  # мигрировать pin и схему контракта
./harness setup                                    # активировать hook и шаблон в клоне
./verify.sh                                        # harness, format, тесты и NativeAOT
dotnet build                                       # быстрая обратная связь
dotnet format Harness.slnx --verify-no-changes --severity warn
```

## Коды возврата

- `0` — все выбранные применимые blocking-проверки прошли (advisory возможны).
- `1` — blocking-проверка доказала нарушение.
- `2` — проверить достоверно не удалось (невалидный `.harness.json`, другой pin, нечитаемые данные).

## Документация

`AGENTS.md` — каноническая агентская навигация, обычный tracked-файл не более **1200 слов**. `CLAUDE.md` — прямой относительный симлинк на соседний `AGENTS.md`. `README.md` и `README.<тег BCP 47>.md` (только рядом с `README.md`) не ограничены по длине. `SKILL.md` разрешён в `skills/<name>/` и зеркалах `.agents/skills/`, `.claude/skills/`; `DESIGN.md` и файлы форджа разрешены без лимита. Решения — в корневом `adrs/`; `adrs.shape` проверяет нумерацию без дыр, дату, статус, разделы H2 и `fencedBlockLineLimit: 20`. Прочий tracked Markdown блокируется ([ADR-0010](adrs/0010-documentation-policy.md), [ADR-0025](adrs/0025-nested-agent-documents.md), [ADR-0061](adrs/0061-forge-and-design-documents.md), [ADR-0066](adrs/0066-adr-shape-as-decision-record.md), [ADR-0068](adrs/0068-readme-translations.md), [ADR-0070](adrs/0070-contract-4-goodhart-filter.md)).
