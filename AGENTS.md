# Harness CLI — навигация для агента

Standalone CLI, который держит одну и ту же harness-рамку над разными репозиториями и
объясняет через ошибки, как должно быть. Он не запускает toolchain репозитория: тесты,
сборку и линтеры гоняет CI. Харнес сообщает self-reported ответы репозитория и выполняет
те измерения, которые в каждом репозитории расходятся.

## Рамка

Репозиторий отвечает на все вопросы рамки в собственном tracked `.harness.json`. Ответы
self-reported: адрес служит навигацией, а не доказательством. Формы ответа:

```jsonc
"tests.unit":  { "paths": ["tests/Unit"] }                        // есть; вот где искать
"lint":        { "present": true,  "reason": "аналайзеры в csproj" } // есть без одного адреса
"tests.e2e":   { "present": false, "reason": "разложим в HARNESS-142" } // отсутствует осознанно
"typecheck":   { "applicable": false, "reason": "нет web-стека" }    // вопрос не про нас
```

Каждая применимая проверка априори `required`: любая её находка blocking, даже если сама
эвристика помечает исходную уверенность как advisory. `policy` смягчает проверку до
`advisory` или отключает через `off`; `suppress` принимает именованное исключение с
обязательным `reason`. Поэтому `present: false` по умолчанию — нарушение. Харнес валидирует
полноту ответов, но не инспектирует их и не ищет опровержения.
[ADR-0017](adrs/0017-required-by-default.md), [ADR-0027](adrs/0027-required-findings-are-blocking.md)

C#-проверки разделяют applicability `csharp`. Если весь этот анализ не относится к
репозиторию, одна запись `"applicability": { "csharp": { "applicable": false, "reason":
"..." } }` делает их `NotApplicable`; точечно выключать каждую через policy не нужно.
[ADR-0018](adrs/0018-csharp-applicability-and-one-type-per-file.md)

Граф C# строится по tracked-исходникам средствами BCL. Ребро несёт `Proven` (позиция
допускает только тип, кандидат по имени один) или `Inferred`. Начиная с 1.5
`dependencies.csharp` строит из `Proven` только blocking-циклы модулей; raw fan-in/out и
external imports удалены как контекстные counts без универсального remediation. Вложенный
модуль — содержание, а не цикл. [ADR-0021](adrs/0021-coupling-evidence-grades.md),
[ADR-0029](adrs/0029-dependency-counts-removed.md)

Начиная с 1.6 `maintainability.csharp` не считает constructor parameters и public members:
единые пороги смешивали роли кода без универсального remediation.
[ADR-0030](adrs/0030-contextual-width-counts-removed.md)

Проверка называется `<семейство>.<язык>`, `Group` — семейство, `Applicability` — язык.
Язык-нейтральное ядро живёт в `Structure/`, чтение исходника — за `ILanguageAnalyzer` в
`Languages/<Язык>/`. Второй язык — экземпляр `Language`, анализатор и строка в реестре, а не
копия проверки. [ADR-0022](adrs/0022-language-axis.md)

.NET-проекты разделяют applicability `dotnet`. Харнес статически требует общий hardened
`Directory.Build.props`, central package versions в ближайшем `Directory.Packages.props` и
`.slnx` вместо `.sln`; он читает tracked XML, но не выполняет MSBuild evaluation.
[ADR-0019](adrs/0019-dotnet-repository-policy.md)

`version` — строка релиза харнеса (`"1.0.0"`) и единственная версия в конфиге: она фиксирует
вопросы, проверки и дефолтные пороги. Бинарь не старше пина исполняет закреплённый релиз,
поэтому обновление бинаря само по себе не добавляет находок; новую проверку включает только
`harness upgrade`, правящий tracked-файл. Проверка новее пина показывается как skipped, пин
новее бинаря делает прогон incomplete. Каждая новая проверка несёт `Since` со своим релизом.
[ADR-0023](adrs/0023-release-version-as-the-verification-contract.md)

`"latest"` включает rolling-контракт. `harness init` создаёт все answer-ключи как нерешённые
placeholders: исследуй репозиторий и замени каждый честным ответом. Если intent или
применимость нельзя установить, спроси владельца; не выдумывай положительный ответ и не
закрывай инициализацию массовыми suppress. Осознанное отсутствие — `present: false` с
причиной, а не suppress. [ADR-0016](adrs/0016-versioned-frame-and-explicit-initialization.md)

`settings.commits` выбирает язык `ru`/`en` и может требовать clone-local setup. `harness
setup` включает шаблон и `commit-msg` hook в общем каталоге клона, поэтому одна подготовка
покрывает и все его worktree; `commits.setup` делает пропущенную подготовку
видимой в обычном check. Для CI передавай явный диапазон в `harness commits check
<base>..<head>`: hook допускает временный autosquash, публикуемый диапазон — нет.
[ADR-0020](adrs/0020-commit-message-contract-and-clone-setup.md)

Доказательство — только tracked-файл: созданный, но не добавленный в индекс файл харнес не
видит, и вердикт от этого не меняется. Проверка обязана назвать в `Evidence` файлы, которые
читает по имени, и спрашивать инвентарь только через `context.Tracked`/`context.Nearest`:
необъявленное чтение — `Incomplete`. Отчёт печатает `not in the index` для объявленных имён,
лежащих в рабочем дереве без `git add`, если проверка оставила вопрос открытым.
[ADR-0026](adrs/0026-untracked-evidence-is-named-in-the-report.md)

## Раскладка

- `src/Harness` — сам CLI. NativeAOT, установленный .NET runtime в момент использования не нужен.
  - `Cli/` — разбор командной строки, usage, компактный консольный отчёт.
  - `Versioning/` — релиз как значение: разбор, сравнение и граница поддерживаемых пинов.
  - `Config/` — чтение и полная валидация `.harness.json`: ответы, политика, исключения.
  - `Engine/` — движок: селекция, порядок, тайминг, политика, исключения, коды возврата.
  - `Structure/` — язык-нейтральное ядро: типы, рёбра с уровнем доказательности, границы
    модулей, циклы и кратчайшее кольцо в них, компоненты связности членов.
  - `Languages/` — `Language` как ось и `ILanguageAnalyzer` как шов.
    - `Languages/CSharp/` — лексический ридер C#: какие исходники анализируемы, маскирование
      комментариев и литералов, структура объявлений, индекс имён и градация ссылок.
  - `Checks/` — поставляемые проверки и их содержимое для `explain`.
    - `Checks/Frame/` — одинаковые self-reported вопросы рамки для каждого репозитория.
    - `Checks/Metrics/` — метрика, измерение и общий отчёт «худшие субъекты плюс счётчик».
    - `Checks/Comments/` — blocking-порог плотности комментариев в authored C#.
    - `Checks/TypesPerFile/` — blocking-правило: один верхнеуровневый class или record в файле.
    - `Checks/Dependencies/` — blocking-циклы модулей по доказанным ссылкам.
    - `Checks/Cohesion/` — advisory-компоненты «член ↔ поле» внутри типа.
    - `Checks/Maintainability/` — advisory-метрики hotspot'ов, измеряемые этим ридером.
    - `Checks/Duplication/` — нормализация токенов и построенный на ней advisory-отчёт
      о межфайловых повторах.
  - `Git/` — безопасный вызов Git без shell и чтение evidence: tracked-записей, режимов
    файлов и целей символических ссылок.
  - `Commits/` — Conventional header, локализованный body, шаблон и общий валидатор hook/CI.
- `tests/Harness.Tests` — приёмочные тесты, которые гоняют скомпилированный исполняемый файл.
- `adrs/` — долговременные решения; правила ниже ссылаются туда за обоснованием.
  Реестр — [`adrs/REGISTRY.md`](adrs/REGISTRY.md), шаблон — `adrs/.template.md`.

## Команды

Репозиторий публичный: tracked-тексты не называют внутренние репозитории, сервисы, домены,
хосты и локальные пути. Примеры и результаты пилотных прогонов всегда обезличиваются;
перед сдачей проверь tracked diff на инфраструктурные имена.

```sh
./harness version                                  # релиз бинаря и диапазон пинов
./harness init /path/to/repository                 # создать незавершённую рамку
./harness upgrade --dry-run                        # что включит подъём пина, ничего не записывая
./harness setup                                    # активировать hook и шаблон в этом клоне
./harness commit-message template                  # показать шаблон выбранного языка
./harness commits check <base>..<head>             # проверить диапазон для CI
dotnet test                                        # полный набор, включая NativeAOT-публикацию
dotnet build                                       # быстрая обратная связь
dotnet format Harness.slnx --verify-no-changes --severity warn # формат и code style без правок
dotnet publish src/Harness/Harness.csproj -c Release -r osx-arm64
./src/Harness/bin/Release/net10.0/osx-arm64/publish/harness check
```

Прогон харнеса над собственным репозиторием занимает доли секунды и ничего не собирает,
поэтому отдельного быстрого режима не нужно.

## Коды возврата

- `0` — каждая выбранная применимая blocking-проверка отработала и прошла. Advisory-находки
  и readiness gaps при этом могут быть: отчёт скажет об этом вместо `PASS`.
- `1` — выбранная применимая blocking-проверка доказала нарушение.
- `2` — проверку не удалось выполнить достоверно; сюда же относится отсутствующий или
  невалидный `.harness.json`.

## Документация

`AGENTS.md` — источник агентской навигации, обычный tracked-файл не более 150 физических
строк. `CLAUDE.md` — прямой относительный симлинк на соседний `AGENTS.md`. `README.md` —
краткий обзор. Документ судится по имени, а не по каталогу: эти три имени действуют на любой
глубине по тем же правилам, что в корне, где `AGENTS.md` и `CLAUDE.md` обязательны.
`SKILL.md` разрешён везде и не измеряется. Долговременные решения живут в корневом `adrs/`.
Прочий tracked Markdown — нарушение; проверку можно смягчить через `policy` или точечно
подавить через `suppress`. [ADR-0010](adrs/0010-documentation-policy.md),
[ADR-0025](adrs/0025-nested-agent-documents.md)
