# ADR Registry

Архитектурные решения repository quality harness. Один ADR = один файл. После `Accepted`
неизменяемо само `Decision`; пересмотр решения — новым ADR со ссылкой `Superseded by`.
Фактологическую ошибку в `Context` или `Consequences` можно исправить, не переписывая
решение: commit history сохраняет такую правку. Шаблон — [.template.md](.template.md).
После добавления ADR обнови этот реестр. Действующее правило в одну строку живёт в
[`AGENTS.md`](../AGENTS.md) и ссылается сюда за обоснованием.

## Контракт результата
<!-- Что харнес сообщает читателю и как это читает CI -->

- [ADR-0017](0017-required-by-default.md) — Каждая применимая проверка априори `required`;
  `policy` хранит только явные послабления `advisory`/`off`, а не перечисляет норму.
- [ADR-0016](0016-versioned-frame-and-explicit-initialization.md) — Числовая `version`
  фиксирует снимок вопросов, `latest` следует за текущим; ошибка одного answer локальна,
  а `init` создаёт намеренно незавершённую рамку и не выдумывает ответы владельца.
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
- [ADR-0013](0013-a-named-exception-is-written-down-and-printed.md) — Рамка обязательна:
  нет или невалиден `.harness.json` — `Incomplete`. Исключение называет `check`, `location`
  и непустой `reason`, печатается строкой `suppressed`, а протухшее — advisory-находка.

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

## Анализ и отчёт
<!-- Как измеряются и как подаются эвристические находки -->

- [ADR-0018](0018-csharp-applicability-and-one-type-per-file.md) — Все C#-проверки имеют
  общий applicability `csharp`; `types-per-file.csharp` блокирует второй верхнеуровневый
  `class` или `record` в одном authored-файле.
- [ADR-0019](0019-dotnet-repository-policy.md) — Три blocking-проверки applicability
  `dotnet` требуют hardened `Directory.Build.props`, Central Package Management через
  ближайший `Directory.Packages.props` и `.slnx`, покрывающий authored-проекты.
- [ADR-0015](0015-comment-density-is-a-blocking-source-policy.md) — `comments.csharp`
  падает при не менее чем 10 строках комментариев и доле выше 25% authored physical lines;
  это локальная source policy, а не универсальная оценка качества каждого комментария.
- [ADR-0006](0006-heuristics-are-advisory.md) — Лексические метрики advisory и называются
  формулой, а не свойством, которое из формулы не следует. `explain` содержит формулу, её
  пределы и вид false positive; отчёт ограничен худшими субъектами плюс счётчиком остальных.
- [ADR-0007](0007-one-finding-one-report.md) — Находка укрупняется до всего региона,
  который покрывает, и репортится один раз: счёт находок отражает дефект, а не размер окна
  анализа.

## Сборка и документация
<!-- Инварианты, которые ограничивают код и тексты репозитория -->

- [ADR-0001](0001-compiled-cli-as-only-test-seam.md) — Приёмочные тесты гоняют
  скомпилированный исполняемый файл и читают stdout и код возврата; internal-типы не
  становятся public ради тестируемости.
- [ADR-0009](0009-nativeaot-as-a-build-invariant.md) — NativeAOT-публикация как инвариант:
  без рефлексии и генерации кода в рантайме, без динамических managed-плагинов, проверки —
  статический реестр в коде. `dotnet test` включает публикацию.
- [ADR-0010](0010-documentation-policy.md) — `AGENTS.md` — единственный источник агентской
  навигации, обычный файл не более 150 строк; `CLAUDE.md` — прямой относительный симлинк на
  него; долговременные решения — в `adrs/`. `AGENTS.md` несёт правило, ADR — обоснование.
