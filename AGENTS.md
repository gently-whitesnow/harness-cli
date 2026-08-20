# Repository Quality Harness — навигация для агента

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
"tests.e2e":   { "present": false, "reason": "разложим в IDP-142" }  // отсутствует осознанно
"typecheck":   { "applicable": false, "reason": "нет web-стека" }    // вопрос не про нас
```

Каждая применимая проверка априори `required`. `policy` нужен только для явного смягчения
до `advisory` или отключения через `off`; `suppress` — именованные исключения с обязательным
`reason`. Поэтому ответ `present: false` по умолчанию становится нарушением. Харнес
валидирует полноту ответов, но не инспектирует их и не ищет опровержения.
[ADR-0017](adrs/0017-required-by-default.md)

C#-проверки разделяют applicability `csharp`. Если весь этот анализ не относится к
репозиторию, одна запись `"applicability": { "csharp": { "applicable": false, "reason":
"..." } }` делает их `NotApplicable`; точечно выключать каждую через policy не нужно.
[ADR-0018](adrs/0018-csharp-applicability-and-one-type-per-file.md)

Связанность и связность считаются по tracked-исходникам средствами BCL. Каждое ребро графа
несёт `Proven` (позиция, где язык не допускает ничего, кроме типа, и единственный кандидат по
имени) или `Inferred`. Blocking строится только из `Proven`, и такая находка одна — цикл
модулей; счётные метрики остаются advisory. Ссылка между модулем и модулем внутри него —
содержание, а не цикл. [ADR-0021](adrs/0021-coupling-evidence-grades.md)

Проверка называется `<семейство>.<язык>`, `Group` — семейство, `Applicability` — язык.
Язык-нейтральное ядро живёт в `Structure/`, чтение исходника — за `ILanguageAnalyzer` в
`Languages/<Язык>/`. Второй язык — экземпляр `Language`, анализатор и строка в реестре, а не
копия проверки. [ADR-0022](adrs/0022-language-axis.md)

.NET-проекты разделяют applicability `dotnet`. Харнес статически требует общий hardened
`Directory.Build.props`, central package versions в ближайшем `Directory.Packages.props` и
`.slnx` вместо `.sln`; он читает tracked XML, но не выполняет MSBuild evaluation.
[ADR-0019](adrs/0019-dotnet-repository-policy.md)

`version` числом фиксирует снимок вопросов; `"latest"` включает rolling-контракт. `harness
init` создаёт все answer-ключи как нерешённые placeholders: исследуй репозиторий и замени
каждый честным ответом. Если intent или применимость нельзя установить, спроси владельца;
не выдумывай положительный ответ и не закрывай инициализацию массовыми suppress. Осознанное
отсутствие — `present: false` с причиной, а не suppress. [ADR-0016](adrs/0016-versioned-frame-and-explicit-initialization.md)

`settings.commits` выбирает язык `ru`/`en` и может требовать clone-local setup. `harness
setup` включает шаблон и `commit-msg` hook; `commits.setup` делает пропущенную подготовку
видимой в обычном check. Для CI передавай явный диапазон в `harness commits check
<base>..<head>`: hook допускает временный autosquash, публикуемый диапазон — нет.
[ADR-0020](adrs/0020-commit-message-contract-and-clone-setup.md)

## Раскладка

- `src/Harness` — сам CLI. NativeAOT, установленный .NET runtime в момент использования не нужен.
  - `Cli/` — разбор командной строки, usage, компактный консольный отчёт.
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
    - `Checks/Dependencies/` — blocking-цикл модулей и advisory-счётчики связанности.
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

```sh
./harness init /path/to/repository                 # создать незавершённую рамку
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

`AGENTS.md` — единственный источник агентской навигации, обычный tracked-файл не более 150
физических строк. `CLAUDE.md` — прямой относительный симлинк на него. `README.md` — краткий
обзор. Долговременные решения живут в `adrs/`. Прочий tracked Markdown — нарушение;
проверку можно смягчить через `policy` или точечно подавить через `suppress`.
[ADR-0010](adrs/0010-documentation-policy.md)
