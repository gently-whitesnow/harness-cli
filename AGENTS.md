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

Каждая shipped-проверка явно перечислена в `policy`: `required` делает находку blocking,
`advisory` оставляет её видимой, `off` пропускает проверку. Переключатель един для всех
проверок, включая топологический инвариант и ratchet-бюджет: запрещено адресное подавление
файла или находки, а не выключение проверки целиком. `settings`, applicability и policy
полны — ридер не подставляет скрытые defaults, поэтому состояние всех проверок видно в
tracked-файле. Харнес валидирует полноту ответов, но не инспектирует их и не ищет
опровержения. [ADR-0017](adrs/0017-required-by-default.md), [ADR-0027](adrs/0027-required-findings-are-blocking.md),
[ADR-0035](adrs/0035-policy-switch-is-uniform.md)

C#-проверки разделяют applicability `csharp`. Если весь этот анализ не относится к
репозиторию, одна запись `"applicability": { "csharp": { "applicable": false, "reason":
"..." } }` делает их `NotApplicable`; точечно выключать каждую через policy не нужно.
[ADR-0018](adrs/0018-csharp-applicability-and-one-type-per-file.md)

Граф C# строится по tracked-исходникам средствами BCL. Ребро несёт `Proven` (позиция
допускает только тип, кандидат по имени один) или `Inferred`. `dependencies.csharp` строит
из `Proven` только blocking-циклы модулей; raw fan-in/out и external imports удалены как
counts без универсального remediation. Вложенный модуль — содержание, а не цикл.
[ADR-0021](adrs/0021-coupling-evidence-grades.md), [ADR-0029](adrs/0029-dependency-counts-removed.md)

DSM `complexity.csharp` — mean reach (файлов на изменение) и core size по файлам внутри зон
sliced-dotnet, тесты вне зоны не входят; превышение tracked `.harness.budget.json` блокирует check,
`harness budget update` только ужимает потолок. [ADR-0032](adrs/0032-topology-over-thresholds.md), [ADR-0042](adrs/0042-dsm-over-the-product-in-files.md)

Проверка называется `<семейство>.<язык>`, `Group` — семейство, `Applicability` — язык.
Язык-нейтральное ядро живёт в `Structure/`, чтение исходника — за `ILanguageAnalyzer` в
`Languages/<Язык>/`. Второй язык — экземпляр `Language`, ридер и строка в реестре, а не
копия проверки; так `comments.yaml` и `comments.typescript` считают плотность комментариев
в YAML и TypeScript со своими applicability и `settings`. [ADR-0022](adrs/0022-language-axis.md),
[ADR-0043](adrs/0043-comment-density-across-languages.md)

.NET-проекты разделяют applicability `dotnet`. Харнес статически требует общий hardened
`Directory.Build.props`, central package versions в ближайшем `Directory.Packages.props` и
`.slnx` вместо `.sln`; он читает tracked XML, но не выполняет MSBuild evaluation.
[ADR-0019](adrs/0019-dotnet-repository-policy.md)

`version` — строка текущего контракта (`"2.7.0"`). Бинарь исполняет только этот контракт;
любой другой pin даёт `Incomplete`, а меняет pin только `harness upgrade`, печатающий весь
маршрут миграции. Legacy-проверки не воспроизводятся. [ADR-0032](adrs/0032-topology-over-thresholds.md)

`architecture` называет единственный стандарт топологии `sliced-dotnet/1`: фиксированные
Clean Architecture-слои (`Host`, `Api`, `Consumers`, `Application`, `Domain`,
`Infrastructure`, `Shared`) × сквозные слайсы `Features/<Слайс>`; публичный API слайса —
его `Contracts/`, кросс-импорт — только явный cross-API `X/<Потребитель>` (аналог `@x`
FSD 2.1), верхние слои читают любой `Domain`-слайс, прямой сегмент именуется по назначению;
слой = сборка: ровно один `.csproj` на слой с C#-кодом, без linked-компиляции чужих слоёв,
ProjectReference — по той же таблице слоёв. Плоский каталог 20+ файлов, плотность
X-контрактов и essence-имена вне сегментных позиций — неблокирующие advisory-observations.
Standalone-библиотека отвечает `"architecture": { "applicable": false, "reason": "..." }`.
[ADR-0033](adrs/0033-canonical-standard-over-declarations.md),
[ADR-0037](adrs/0037-segments-by-purpose.md)–[ADR-0041](adrs/0041-layer-is-the-assembly.md)

`"latest"` включает rolling-контракт. `harness init` спрашивает только application или
standalone-library (либо принимает `--kind application|library` без stdin), создаёт
соответствующую `architecture`, DSM-бюджет текущих tracked-исходников и полный
явный конфиг. Нерешённые answer-ключи остаются `{}` и `off`: исследуй репозиторий, замени
каждый честным ответом и включи его policy. Не выдумывай положительный ответ.

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

- `src/Harness` — сам CLI: NativeAOT, слой = отдельный проект `Harness.<Слой>.csproj`, публикуется `Host`.
  - `Host/` — composition root, статический реестр и запуск процесса.
  - `Api/Features/Harness/Cli/` — входное зеркало с разбором командной строки.
  - `Application/Features/Harness/` — единый прикладной слайс: проверки, конфигурация,
    движок, commit-команды, отчёт и публичные `Contracts/` для входа и адаптеров; это одна
    CLI-capability, а не набор независимых бизнес-слайсов ([ADR-0034](adrs/0034-language-axis-in-sliced-dotnet.md)).
  - `Domain/Harness/Structure/` — язык-нейтральная модель графа, модулей, циклов и DSM.
  - `Domain/Harness/Languages/` — языковой порт ADR-0022/0034; реализаций здесь нет.
  - `Domain/Harness/Evidence/` — модель tracked evidence и порт репозитория.
  - `Infrastructure/Features/Harness/Git/` — Git-процесс и clone-local интеграция.
  - `Infrastructure/Features/Harness/Languages/CSharp/` — C#-ридер в отдельном infra namespace.
  - `Shared/Versioning/` — версия бинаря и граница текущего контракта.
- `tests/Harness.Tests` — приёмочные тесты, которые гоняют скомпилированный исполняемый файл.
- `adrs/` — долговременные решения; правила ниже ссылаются туда за обоснованием.
  Реестр — [`adrs/REGISTRY.md`](adrs/REGISTRY.md), шаблон — `adrs/.template.md`.

## Команды

Репозиторий публичный: tracked-тексты не называют внутренние репозитории, сервисы, домены,
хосты и локальные пути. Примеры и результаты пилотных прогонов всегда обезличиваются;
перед сдачей проверь tracked diff на инфраструктурные имена.

```sh
./harness version                                  # релиз бинаря и текущий контракт
./harness init /path/to/repository                 # создать незавершённую рамку
./harness upgrade                                  # поднять pin и увидеть миграцию 2.0
./harness setup                                    # активировать hook и шаблон в этом клоне
./harness commit-message template                  # показать шаблон выбранного языка
./harness commits check <base>..<head>             # проверить диапазон для CI
dotnet test                                        # полный набор, включая NativeAOT-публикацию
dotnet build                                       # быстрая обратная связь
dotnet format Harness.slnx --verify-no-changes --severity warn # формат и code style без правок
dotnet publish src/Harness/Host/Harness.Host.csproj -c Release -r osx-arm64
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
Прочий tracked Markdown — нарушение; проверку можно смягчить через `policy`.
[ADR-0010](adrs/0010-documentation-policy.md),
[ADR-0025](adrs/0025-nested-agent-documents.md)
