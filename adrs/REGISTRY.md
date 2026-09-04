# ADR Registry

Архитектурные решения Harness CLI. Один ADR = один файл. После `Accepted`
неизменяемо само `Decision`; пересмотр решения — новым ADR со ссылкой `Superseded by`.
Фактологическую ошибку в `Context` или `Consequences` можно исправить, не переписывая
решение: commit history сохраняет такую правку. Шаблон — [.template.md](.template.md).
После добавления ADR обнови этот реестр. Действующее правило в одну строку живёт в
[`AGENTS.md`](../AGENTS.md) и ссылается сюда за обоснованием.

## Контракт результата
<!-- Что харнес сообщает читателю и как это читает CI -->

- [ADR-0017](0017-required-by-default.md) — Исторический required-default; контракт 2.0
  требует полный явный `policy` для каждого shipped check, без fallback ридера.
- [ADR-0035](0035-policy-switch-is-uniform.md) — `required`/`advisory`/`off` действуют для
  каждой shipped-проверки без исключений, включая топологический инвариант и ratchet-бюджет;
  запрещено адресное подавление файла или находки, а не выключение проверки целиком.
- [ADR-0027](0027-required-findings-are-blocking.md) — `required` превращает каждую
  находку проверки в blocking-нарушение; `advisory` даёт явное время на исправление.
- [ADR-0016](0016-versioned-frame-and-explicit-initialization.md) — Версионированные снимки
  вопросов отменены ADR-0023/0032; ошибка одного answer остаётся локальной, а `init` создаёт
  намеренно незавершённую рамку и не выдумывает ответы владельца.
- [ADR-0002](0002-when-a-check-becomes-blocking.md) — Проверка становится blocking, только
  если выполнены сразу пять условий: детерминизм, actionable-находка, скорость, низкий риск
  false positive, негативная фикстура. Иначе advisory, без влияния на код возврата.
- [ADR-0003](0003-six-outcomes-three-exit-codes.md) — Шесть исходов проверки (`Passed`,
  `Failed`, `Skipped`, `NotApplicable`, `ReadinessGap`, `Incomplete`) против трёх кодов
  возврата: `Incomplete` → `2`, `Failed` → `1`, иначе `0`. `NotApplicable` и `ReadinessGap`
  код не меняют, но заголовок отчёта их различает (`PASS WITH GAPS`, `NOTHING VERIFIED`).
- [ADR-0014](0014-frame-answers-are-self-reported.md) — Репозиторий self-reported отвечает
  на каждый вопрос в tracked `.harness.json`; `paths` — навигация, не доказательство.
  Харнес валидирует полноту и форму, но не инспектирует ответы и не ищет опровержения в Git.
  Заменяет [ADR-0012](0012-declaration-with-an-address-of-proof.md).
- [ADR-0026](0026-untracked-evidence-is-named-in-the-report.md) — Инвентарь остаётся
  git-индексом, но каждая проверка обязана назвать файлы, которые читает по имени, и спрашивать
  инвентарь только через контекст. Отчёт печатает `not in the index` для объявленных имён,
  лежащих в рабочем дереве без `git add`; вердикт не меняется, Git спрашивается один раз и
  только когда проверка оставила вопрос открытым.
- [ADR-0013](0013-a-named-exception-is-written-down-and-printed.md) — Рамка обязательна:
  нет или невалиден `.harness.json` — `Incomplete`. Механика named suppressions отменена
  ADR-0032.

## Что и как запускается
<!-- Что харнесу разрешено делать с репозиторием -->

- [ADR-0011](0011-the-harness-does-not-run-the-repository-toolchain.md) — Харнес не
  запускает toolchain репозитория: единственный внешний процесс — `git`. Тесты, сборку и
  линтеры гоняет CI; харнес держит рамку и считает то, что не воспроизводит чужой пайплайн.
  Заменяет часть [ADR-0004](0004-execution-plan-from-git-evidence.md).
- [ADR-0008](0008-the-harness-only-observes.md) — Харнес не правит tracked-контент, не
  ставит toolchain, не меняет lockfile. ADR-0016 вводит единственное opt-in исключение:
  `init` создаёт отсутствующий `.harness.json`, но не добавляет его в Git.
- [ADR-0020](0020-commit-message-contract-and-clone-setup.md) — Conventional header и
  структурированный локализованный body проверяются одним валидатором в hook и CI;
  `setup` активирует clone-local hook/template, а `commits.setup` доказывает активацию.
- [ADR-0046](0046-unified-verification-entry-point.md) — Обязательный self-reported
  `answers.verify.paths` называет tracked repository-owned скрипт всех применимых проверок;
  harness не инспектирует и не запускает его, а `init` сразу ставит `frame.verify: required`.
- [ADR-0049](0049-test-suite-address-is-the-project.md) — `answers.tests.unit` и
  `answers.tests.integration` адресуют тестовый проект, а не его файлы: путь с исходным
  суффиксом даёт `Incomplete` с подсказкой каталогов, любой `paths` держит не более пяти
  адресов; пилот с перечнем из 17 тестовых файлов показал, что форму надо объяснять ошибкой.
- [ADR-0050](0050-domain-is-the-bottom-layer.md) — Слоя `Shared` в `sliced-dotnet/1` нет:
  `Domain` — основание DAG без исходящих рёбер, общий для слайсов код живёт в `Domain/Shared`,
  каталог `Shared` под зоной — вне канонических слоёв; контракт `2.12.0`.

## Анализ и отчёт
<!-- Как измеряются и как подаются эвристические находки -->

- [ADR-0021](0021-coupling-evidence-grades.md) — Связанность и связность считаются по
  tracked-исходникам средствами BCL; каждое ребро несёт `Proven` или `Inferred`, и blocking
  по уверенности самой проверки строится только из `Proven`. Единственная такая находка —
  цикл модулей; счётные метрики исходно advisory, а repository policy применяет ADR-0027.
  Ссылка между модулем и модулем внутри него — содержание, а не цикл.
- [ADR-0022](0022-language-axis.md) — `Id` = `<семейство>.<язык>`, `Group` = семейство,
  `Applicability` = язык; язык-нейтральное ядро в `Structure/`, чтение исходника — за
  `ILanguageAnalyzer`. Второй язык не создаёт вторую проверку.
- [ADR-0034](0034-language-axis-in-sliced-dotnet.md) — Уточняет физическую раскладку
  языковой оси ADR-0022 внутри одного слайса `Harness` стандарта sliced-dotnet/1.
- [ADR-0043](0043-comment-density-across-languages.md) — Плотность комментариев считается
  и для YAML и TypeScript/JavaScript через лексические ридеры за общим портом; свои
  applicability и `settings.comments.<язык>`, дефолт 10/8; контракт 2.7.
- [ADR-0018](0018-csharp-applicability-and-one-type-per-file.md) — Все C#-проверки имеют
  общий applicability `csharp`; `types-per-file.csharp` блокирует второй верхнеуровневый
  `class` или `record` в одном authored-файле.
- [ADR-0019](0019-dotnet-repository-policy.md) — Три blocking-проверки applicability
  `dotnet` требуют hardened `Directory.Build.props`, Central Package Management через
  ближайший `Directory.Packages.props` и `.slnx`, покрывающий authored-проекты.
- [ADR-0044](0044-editorconfig-baseline-and-warning-suppressions.md) — `editorconfig.dotnet`
  требует эталонный code-style baseline в tracked-цепочке `.editorconfig` над каждым
  проектом (эталон печатает `explain`, записывает `init`); `warning-suppressions.dotnet`
  блокирует адресное подавление — pragma, `SuppressMessage`, `NoWarn` в `.csproj`,
  path-scoped `severity = none` — а repository-wide выключение правила печатает observation.
- [ADR-0028](0028-recalibrated-csharp-defaults.md) — Начиная с 1.4.0 дефолты C#-эвристик:
  comments `10/8`, cohesion `6/2`, duplication `30/90`. Порог cohesion отменён ADR-0032;
  comments и duplication остаются с текущими значениями.
- [ADR-0045](0045-duplication-required-by-default.md) — Ретроспектива на трёх репозиториях
  сохраняет `duplication.csharp` `30/90`, а `harness init` делает policy `required`;
  существующая явная policy при upgrade не переписывается.
- [ADR-0029](0029-dependency-counts-removed.md) — Начиная с 1.5 `dependencies.csharp`
  оставляет только blocking-циклы по `Proven`; fan-in/out и external imports удалены как
  контекстные counts без универсального remediation.
- [ADR-0015](0015-comment-density-is-a-blocking-source-policy.md) — Историческое правило
  comments `10/25`, заменённое ADR-0028 начиная с контракта 1.4.0.
- [ADR-0006](0006-heuristics-are-advisory.md) — Лексические метрики исходно advisory и
  называются формулой, а не свойством, которое из формулы не следует. Их enforcement
  пересмотрен ADR-0027. `explain` содержит формулу, пределы и вид false positive.
- [ADR-0007](0007-one-finding-one-report.md) — Находка укрупняется до всего региона,
  который покрывает, и репортится один раз: счёт находок отражает дефект, а не размер окна
  анализа.
- [ADR-0032](0032-topology-over-thresholds.md) — Контракт 2.0 удаляет
  `maintainability.csharp`, `cohesion.csharp`, `suppress` и `overrides`; бинарь исполняет
  только текущий контракт, а пороговые скоры заменяются топологическими инвариантами.
- [ADR-0033](0033-canonical-standard-over-declarations.md) — Секция `architecture`
  сведена к каноническому стандарту `sliced-dotnet/1`: фиксированные Clean
  Architecture-слои × сквозные слайсы `Features/` с публичным API `Contracts/`;
  per-repo декларации `layers`/`modules`/`mirrors` не реализуются,
  кросс-импорт слайсов — только явный cross-API `X/<Потребитель>`,
  standalone-библиотека отвечает `applicable: false`.
- [ADR-0036](0036-input-layers-read-domain.md) — Исправляет реализацию инварианта
  «Domain — общий словарь»: `Api` и `Consumers`, как и `Application` с
  `Infrastructure`, могут читать любой `Domain`-слайс. Контракт `sliced-dotnet/1` не
  меняется; релиз 2.3.0 устраняет расхождение кода с ADR-0033.
- [ADR-0037](0037-segments-by-purpose.md) — Переносит generic-словарь Steiger
  `segments-by-purpose` на непосредственные сегменты sliced-dotnet; находка подчиняется
  общей policy, backend-расширение блокирует `Repositories`, а технический leaf не может
  притвориться grouped-слайсом.
- [ADR-0038](0038-flat-directory-grouping.md) — Каталог architecture-zone с 20+
  непосредственными authored `.cs`-файлами получает advisory-observation
  `flat-directory-grouping` (адаптация Steiger `shared-lib-grouping`); сигнал не меняет
  код возврата ни при какой policy.
- [ADR-0039](0039-cross-api-density.md) — Плотность X-контрактов — единственный сигнал
  скрытой слоистости на едином слое слайсов: взаимная пара — `mutual-cross-api`, четыре и
  более потребителей — `cross-api-fan-in`; оба — advisory-observations без влияния на код
  возврата.
- [ADR-0040](0040-zone-wide-vocabulary.md) — Словарь ADR-0037 сканирует все
  tracked-каталоги architecture-zone на любой глубине; прежние позиции сохраняют прежние
  находки, новые получают advisory-observation `directories-by-purpose`.
- [ADR-0041](0041-layer-is-the-assembly.md) — Слой — это сборка: канонический слой с
  C#-кодом — ровно один `.csproj`, linked-компиляция чужих слоёв — blocking, ProjectReference
  между проектами зоны идут строго по таблице слоёв; читается только tracked XML без MSBuild
  evaluation, репозиторий без SDK-style проектов не судится.

## Сборка и документация
<!-- Инварианты, которые ограничивают код и тексты репозитория -->

- [ADR-0042](0042-dsm-over-the-product-in-files.md) — `complexity.csharp` измеряет файлы
  внутри архитектурных зон sliced-dotnet и бюджетирует mean reach (файлов на изменение)
  вместо propagation cost; маркер `<auto-generated>` в области измерения называется в
  отчёте, бюджет 2.5 мигрирует `harness budget update`.
- [ADR-0048](0048-dsm-product-boundary-without-a-zone.md) — Без архитектурной зоны граница
  продукта для DSM читается из tracked `.csproj`: файлы, чей ближайший проект тестовый,
  не измеряются; путь к бизнес-логике в конфиге и флаг включения тестов отклонены как
  декларации, ломающие сравнимость ratchet-бюджета.
- [ADR-0001](0001-compiled-cli-as-only-test-seam.md) — Приёмочные тесты гоняют
  скомпилированный исполняемый файл и читают stdout и код возврата; internal-типы не
  становятся public ради тестируемости.
- [ADR-0009](0009-nativeaot-as-a-build-invariant.md) — NativeAOT-публикация как инвариант:
  без рефлексии и генерации кода в рантайме, без динамических managed-плагинов, проверки —
  статический реестр в коде. `dotnet test` включает публикацию.
- [ADR-0023](0023-release-version-as-the-verification-contract.md) — `version` называет
  релиз харнеса и фиксирует контракт. Бинарь 2.0 исполняет только текущий контракт;
  `harness upgrade` — единственный путь смены pin и печатает миграцию без догадок.
- [ADR-0010](0010-documentation-policy.md) — `AGENTS.md` — единственный источник агентской
  навигации, обычный файл не более 150 строк; `CLAUDE.md` — прямой относительный симлинк на
  него; долговременные решения — в `adrs/`. `AGENTS.md` несёт правило, ADR — обоснование.
- [ADR-0025](0025-nested-agent-documents.md) — документ судится по имени, а не по каталогу:
  `AGENTS.md`, `CLAUDE.md` и `README.md` действуют на любой глубине под теми же правилами,
  что в корне, а `SKILL.md` разрешён и не измеряется — скилл загружается под задачу.
- [ADR-0047](0047-landing-mirrors-the-contract.md) — Публичный лендинг живёт в `site/`
  этого репозитория статикой без сборки и зеркалит контракт бинаря: реестр проверок и
  версия сверяются тестом `SiteContractTests`, таблица «изменилось в коде → обновить на
  сайте» — в `site/AGENTS.md`.
