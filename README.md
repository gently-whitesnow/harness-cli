# Harness CLI

Standalone CLI с единой рамкой качества для разных репозиториев. Он читает self-reported
ответы из tracked `.harness.json` и измеряет то, что можно одинаково проверить в любом репозитории.
Тесты, сборку и линтеры проекта харнес не запускает — за них отвечает CI самого проекта.

## Установка

```sh
curl -fsSL https://raw.githubusercontent.com/gently-whitesnow/harness-cli/master/install.sh | sh
```

Скрипт определяет платформу, сверяет sha256 и кладёт бинарь в `~/.local/bin/harness` без
sudo. Никакого .NET runtime для запуска не нужно. Той же командой харнес обновляется: пинить
при установке нечего, потому что поведение задаёт проверяемый репозиторий, а не бинарь.
Внутри репозитория с `.harness.json` скрипт заодно выполняет `harness setup`, который
активирует commit-шаблон и `commit-msg` hook этого клона.

Для disposable-контейнера или installer-задачи бинарь можно оставить внутри клона:

```sh
curl -fsSL https://raw.githubusercontent.com/gently-whitesnow/harness-cli/master/install.sh \
  | sh -s -- --scope clone
```

`--scope clone` атомарно устанавливает его в `$(git rev-parse --git-common-dir)/harness/bin/harness`,
под lock-файлом защищает две параллельные установки и обязательно выполняет `harness setup`.
Именно там `commit-msg` hook ищет харнес первым делом, а вторым — `harness` в `PATH`: путь
бинаря в hook не запекается, поэтому файл одинаков для всего клона и его linked worktree,
переживает удаление worktree и не зависит от того, какой бинарь выполнял setup. Не найдя ни одного,
hook отказывает в коммите и печатает оба просмотренных места. User-каталоги и tracked-файлы не меняются.

`HARNESS_VERSION=3.8.1` ставит конкретный релиз, `HARNESS_INSTALL_DIR` меняет каталог
обычной user-установки, а `HARNESS_NO_SETUP=1` отключает подготовку клона.

## Запуск

```sh
harness init /path/to/repository   # создать рамку для языков из git-индекса
harness init --kind application    # ответ application/library без интерактивного stdin
harness init --languages go,yaml   # объявить оси явно вместо детекции (CI)
harness upgrade                    # поднять pin и получить маршрут от него
harness check                      # проверить весь workspace
harness check --project apps/api    # корень и выбранный проект (частичный прогон)
harness setup                      # подготовить этот клон
harness version                    # релиз бинаря и текущий контракт
```

`init` записывает applicability, settings и policy только обнаруженных стеков. При C# выбор
`--kind application|library` задаёт sliced-dotnet или неприменимость архитектуры. Все проверки —
`required`; смягчение обсуждается с владельцем. Pin — текущий релиз; `--latest` включает
rolling-контракт. Существующие файлы не перезаписываются, новые не добавляются в Git.
Для .NET создаётся отсутствующий `.editorconfig` с baseline. `init` также активирует hook;
после клонирования его готовит `harness setup`. Требуемый, но отсутствующий setup ломает `check`.
В CI сообщения проверяются диапазоном: `harness commits check <base>..<head>`.

`answers.verify.paths` называет tracked скрипт всех проверок, включая `harness check`; харнес
его не запускает и не инспектирует. Здесь это `./verify.sh`; commit range задаёт отдельно CI.

`harness help` перечисляет команды. `--verbose` раскрывает причины, `--all` — измеренные
субъекты, `--only <check-id>` выбирает проверку. Evidence берётся только из tracked-инвентаря:
не добавленный через `git add` файл отчёт обозначает `not in the index`.

## Монорепозитории

Корневой `.harness.json` может регистрировать проекты полем `projects`:

```json
"projects": ["apps/api", "apps/web", "agent-profile"]
```

Каждый проект содержит tracked `.harness.json` с `answers`, `applicability`, `policy`,
`settings` и при необходимости `architecture`. Пути — относительные каталоги без glob, `..`,
пересечений и вложенных проектов. Незарегистрированный tracked `.harness.json` — ошибка,
`harness upgrade` перечисляет такие файлы; повторяющийся ключ в конфиге отклоняется.

Версия контракта одна, в корне. Только корень задаёт `settings.commits` и `commits.setup`;
дочерние конфиги не содержат `version`, `projects` или настройки commit-интеграции, а
`settings` обязательна даже пустая: `"settings": {}`. Наследования нет: у каждого проекта
явная рамка. Например, проект с Markdown-данными может задать `"docs.policy": "off"` или
`"advisory"` в своей `policy`, сохранив `required` у других проектов. Исключения для
отдельных файлов и находок не поддерживаются.

Корень проверяет файлы вне проектов; каждый проект — свой каталог, ответы и локальные пути
читаются относительно него. Общие tracked `.editorconfig` и `Directory.*` выше проекта доступны
как настройки, не расширяя измерение на соседний код; секция `.editorconfig` судится в рамке,
чьи исходники она адресует. Корневые документы не заменяют обязательные документы проекта.

`harness check` из любого каталога репозитория проверяет весь workspace. `harness check
--project apps/api` — частичный прогон: сначала валидирует все конфиги workspace, затем
проверяет корневую область и выбранный проект; соседние проекты не измеряются. `--verbose`
раскрывает область измерений. В CI используйте полный прогон.

Дублирование и графы измеряются внутри каждого компонента: межпроектные повторы, циклы
и достижимость этим прогоном не доказаны и не исключены. Изменение границ меняет область
измерения и требует ревью; affected-прогонов и presets нет. [ADR-0060](adrs/0060-explicit-workspace-projects.md)

## Версия

Корневой `version` фиксирует единственный контракт всего workspace. Бинарь исполняет только
текущий контракт; другой pin даёт код `2`. `harness upgrade` меняет корневой pin и печатает
маршрут миграции с фрагментами для обнаруженных осей. Ответы владельца не угадываются;
правки принимаются одним reviewable-коммитом. [ADR-0023](adrs/0023-release-version-as-the-verification-contract.md)

## В CI

GitLab:
```yaml
harness:
  image: ghcr.io/gently-whitesnow/harness:3.8.1
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

## Как это работает

Эталон — рабочий [`.harness.json`](.harness.json) этого репозитория: в нём представлены все
формы ответа и секции конфигурации. `paths` служит навигацией и не проверяется как
доказательство. Рамка явная и только явная, как EditorConfig: проверка, не названная в
`policy`, — вне рамки и не запускается; названная несёт `required` (блокирует), `advisory`
(находки видны без провала) или `off`, и полную секцию `settings`, если её читает — defaults
в ридере нет. Ось объявляется, когда её проверка названа: `{ "applicable": true }` или
`{ "applicable": false, "reason": "..." }`. Забытый стек ловит `harness.coverage`: язык с
tracked-исходниками без записи в `applicability` — находка с готовым фрагментом конфига.

Коды возврата: `0` — всё выбранное прошло, `1` — доказано нарушение, `2` — проверить
достоверно не удалось (сюда же относится отсутствующий или невалидный `.harness.json`).

## Собственные проверки

Общий стандарт включает графы зависимостей, восемь правил sliced-dotnet, DSM-сложность,
дублирование C#, Go и TypeScript/JavaScript, комментарии, документационную политику и .NET baseline;
Ansible — запрет inline `noqa` и циклы ролей (обе проверки стартуют `required`).
Каждая проверка объясняет себя через `harness explain`; `harness guide` печатает агенту цикл работы — в `AGENTS.md` потребителя хватит одной строки.
DSM сравнивает среднюю достижимость и размер циклической группы с явными потолками
`settings."complexity.csharp"` / `settings."complexity.go"` / `settings."complexity.typescript"` (8.0 / 0).

## Сайт

Лендинг — статика в [`site/`](site/): все проверки, формулы циклов, DSM и дупликации, области workspace. Реестр и версия зеркалят бинарь и сверяются тестом; что обновлять — [`site/AGENTS.md`](site/AGENTS.md), почему — [ADR-0047](adrs/0047-landing-mirrors-the-contract.md).
