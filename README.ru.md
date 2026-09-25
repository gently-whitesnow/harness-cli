[English](README.md) · **Русский**

# Harness CLI

Standalone CLI, который держит одну рамку качества над разными репозиториями. Это
детерминированная граница между AI-агентами и репозиторием: правила записаны в tracked
`.harness.json`, каждый прогон читает только файлы из индекса Git, и один и тот же вход даёт
один и тот же вердикт. Тесты, сборку и линтеры проекта харнес не запускает — это делает CI
самого проекта через `verify`-скрипт, который называет рамка.

Сайт: [harness.whitesnow.tech](https://harness.whitesnow.tech/ru/) · Решения: [`adrs/`](adrs/REGISTRY.md) · Лицензия: MIT

## Зачем

Агенту предстоит ещё тысяча изменений в репозитории, и после каждого он должен оставаться
понятным и пригодным для правок. Хорошего agent loop для этого мало: правила должны пережить
смену агента, контекста и проекта. Харнес превращает правила, которые обычно заново объясняют
вручную, в проверки после каждого изменения:

- циклы зависимостей модулей и достижимость файлов (DSM) с явными потолками;
- межфайловые повторы, плотность комментариев и длина функций;
- один короткий `AGENTS.md`, `CLAUDE.md` — симлинк на него, решения — в `adrs/`;
- форма commit-сообщений: `commit-msg` hook и проверка диапазона в CI;
- hardened baseline .NET и архитектура `sliced-dotnet/1` (слои × слайсы);
- self-reported ответы: где тесты, линтер, сборка и `verify`-скрипт или почему их нет.

Для тех, кто доверяет агентам менять код и хочет одинаковых правил во всех репозиториях.
Языки: C#/.NET (самый полный набор), Go, TypeScript/JavaScript, YAML и Ansible — 44 проверки;
`harness help` перечисляет их, `harness explain <check-id>` объясняет одну.

## Установка

```sh
curl -fsSL https://raw.githubusercontent.com/gently-whitesnow/harness-cli/master/install.sh | sh
```

Скрипт выбирает сборку для macOS arm64 или Linux x64/arm64 (glibc или musl), сверяет sha256
и кладёт её в `~/.local/bin/harness` без sudo. Это NativeAOT-бинарь: .NET runtime не нужен,
только Git. Та же команда обновляет его, а внутри репозитория с рамкой выполняет `harness setup`.

`HARNESS_VERSION=3.9.1` ставит конкретный релиз, `HARNESS_INSTALL_DIR` меняет каталог,
`HARNESS_NO_SETUP=1` отключает подготовку клона. `sh -s -- --scope clone` ставит бинарь в
`<git-common-dir>/harness/bin/` клона — там `commit-msg` hook ищет его раньше, чем в `PATH`.

## Быстрый старт

```sh
cd /path/to/repository
harness init --kind application   # или --kind library; спрашивается только при C# в индексе
git add .harness.json            # и .editorconfig, который init пишет для .NET
harness check                     # заполняйте ответы, пока прогон не станет полным
harness check --only <check-id> --verbose
harness explain <check-id>
```

`init` определяет языки по индексу Git (`--languages go,yaml` заменяет детекцию) и пишет
только их секции. Каждая записанная проверка стартует как `required`. Ответы о тестах,
линтере, формате, сборке, typecheck и `verify` стартуют пустыми и держат прогон `Incomplete`
(код `2`), пока вы на них не ответите. `init` заодно активирует commit hook; после клонирования
его готовит `harness setup`. `harness guide` печатает цикл работы агента для `AGENTS.md` потребителя.

## Пример вывода

C#-репозиторий, где `Orders` и `Billing` используют друг друга, а в индексе лежит лишний
`NOTES.md` (сокращено, длинные строки перенесены):

```text
$ harness check --only dependencies.csharp,docs.policy --verbose
FAIL  /work/shop
  harness 3.9.1 · repository pins 3.9.1
   CHECK ID             FINDINGS
❌ docs.policy                 1
    violation  NOTES.md: unexpected tracked Markdown; remove it, fold navigation into
               AGENTS.md, or record a concise decision in adrs/
❌ dependencies.csharp         1
    violation  src/Shop/Billing/Invoice.cs:7: module dependency cycle
               Shop.Billing -> Shop.Orders -> Shop.Billing: Shop.Billing.Invoice names
               Shop.Orders.Order at src/Shop/Billing/Invoice.cs:7; Shop.Orders.Order names
               Shop.Billing.Invoice at src/Shop/Orders/Order.cs:7.
```

Коды возврата: `0` — все выбранные применимые blocking-проверки прошли (advisory-находки
возможны); `1` — blocking-проверка доказала нарушение; `2` — проверить достоверно не удалось,
в том числе при отсутствующем или невалидном `.harness.json`.

## Рамка

Рамка явная, как EditorConfig. Эталон — [`.harness.json`](.harness.json) этого репозитория,
в нём есть все формы ответа:

```jsonc
"tests.unit":        { "paths": ["tests/Unit"] }  // есть; адрес — тестовый проект
"lint":              { "present": true,  "reason": "аналайзеры в Directory.Build.props" }
"tests.integration": { "present": false, "reason": "запланированы в ISSUE-142" }
"typecheck":         { "applicable": false, "reason": "нет web-стека" }
```

Проверка, не названная в `policy`, — вне рамки: не запускается, в отчёте её учитывает одна
строка. Названная несёт `required` (находка блокирует), `advisory` (видна, но не блокирует)
или `off` и полную секцию `settings` — скрытых дефолтов нет, исключений для отдельного файла
или находки тоже. `harness.coverage` находит язык с tracked-исходниками без записи в
`applicability` и печатает фрагмент для вставки; отказ — `{ "applicable": false, "reason": "..." }`.
Ответы self-reported: харнес валидирует их форму, `paths` служат навигацией, остальное
запускает `verify`-скрипт.

## Монорепозитории

Корневой `.harness.json` регистрирует непересекающиеся каталоги: `"projects": ["apps/api", "apps/web"]`.
У каждого свой tracked `.harness.json` с `answers`, `applicability`, `settings` и `policy`;
наследования нет, `version` и commit-настройки задаёт только корень. `harness check` из
любого каталога проверяет весь workspace; `harness check --project apps/api` валидирует все
рамки и сообщает о частичном прогоне. Дублирование и графы измеряются внутри каждого проекта,
не между проектами. [ADR-0060](adrs/0060-explicit-workspace-projects.md)

## Версия контракта

`version` фиксирует один контракт для всего workspace (`"3.9.1"`); `"latest"` следует за
установленным бинарём. Бинарь исполняет только свой контракт, другой pin даёт код `2`.
`harness upgrade` поднимает pin и печатает маршрут миграции с фрагментами для обнаруженных
осей; ответы владельца он не угадывает. [ADR-0023](adrs/0023-release-version-as-the-verification-contract.md)

## CI

GitLab:

```yaml
harness:
  image: ghcr.io/gently-whitesnow/harness:3.9.1
  script:
    - harness check
    - harness commits check "$CI_MERGE_REQUEST_DIFF_BASE_SHA..$CI_COMMIT_SHA"
```

GitHub Actions или любой контур без доступа к ghcr.io:

```yaml
- name: Repository harness
  run: |
    curl -fsSL https://raw.githubusercontent.com/gently-whitesnow/harness-cli/master/install.sh | sh
    ~/.local/bin/harness check
```

Включите `harness check` в `verify`-скрипт рядом с тестами, сборкой и линтерами. Hook
допускает временный autosquash, диапазон для `harness commits check` — нет.

## Репозиторий

`src/Harness` устроен по собственному стандарту `sliced-dotnet/1`, `tests/Harness.Tests`
гоняет скомпилированный бинарь, `./verify.sh` запускает харнес, формат, тесты и NativeAOT-публикацию.
`site/` зеркалит реестр проверок и версию под тестом ([ADR-0047](adrs/0047-landing-mirrors-the-contract.md)).
