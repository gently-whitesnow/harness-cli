# Repository Quality Harness

Standalone CLI, который держит одну и ту же рамку качества над разными репозиториями —
одинаково для человека, AI-агента и CI.

Харнес **не запускает** тесты, сборку и линтеры репозитория: это уже делает его CI, и в
каждом репозитории по-своему. Воспроизводить уникальное — не задача инструмента. Харнес
отвечает на два вопроса: что репозиторий сообщает о своей quality-инфраструктуре и что
показывают измерения, которые в каждом репозитории расходятся.

## Запуск

Из корня этого репозитория (нужен .NET 10 SDK):

```sh
dotnet run --project src/Harness -- check /path/to/repository
```

Путь можно не указывать, если проверяется текущий репозиторий. Он должен содержать tracked
`.harness.json`. Список команд и проверок покажет `harness help`, подробности конкретной
проверки — `harness explain <check-id>`.

## Установка

Соберите самостоятельный NativeAOT-бинарник и положите его в каталог из `PATH`:

```sh
dotnet publish src/Harness/Harness.csproj -c Release -r osx-arm64 -o /tmp/harness-publish
mkdir -p ~/.local/bin
install -m 755 /tmp/harness-publish/harness ~/.local/bin/harness
harness check /path/to/repository
```

Для другой платформы замените `osx-arm64` на подходящий RID, например `linux-x64`,
`linux-arm64` или `osx-x64`. Собранному бинарнику установленный .NET runtime не нужен.

## Как это работает

Репозиторий заводит tracked `.harness.json` и отвечает в нём на все вопросы рамки. Это
self-reported описание: адрес помогает читателю найти вещь, но харнес его не инспектирует.

```json
{
  "version": 2,
  "answers": {
    "tests.unit": { "paths": ["tests/Unit"] },
    "tests.integration": { "paths": ["tests/Integration"] },
    "tests.architecture": { "present": false, "reason": "planned, IDP-142" },
    "format": { "paths": [".editorconfig"] },
    "lint": { "present": true, "reason": "analyzers enabled in Directory.Build.props" },
    "build": { "paths": ["Repository.sln"] },
    "typecheck": { "applicable": false, "reason": "no web stack" }
  },
  "policy": {
    "frame.tests.unit": "required"
  },
  "suppress": [
    { "check": "duplication.csharp", "location": "src/Legacy", "reason": "rewrite in Q3, IDP-88" }
  ]
}
```

- **`answers`** — полный self-reported ответ репозитория. `paths` — навигация; харнес не
  проверяет существование или содержимое адреса и не ищет противоречия в Git.
- **`policy`** — строгость: `required` превращает ответ `present: false` в нарушение,
  `advisory` печатает нарушения не заваливая прогон, `off` выключает проверку.
- **`suppress`** — именованные исключения. `reason` обязателен, а подавленная находка всё
  равно печатается строкой `suppressed`.

`harness explain <check-id>` печатает форму ответа, его исход и способ поднять планку.

## В CI

Отдельный шаг рядом с тестами:

```yaml
- name: Repository harness
  run: harness check
```

Коды возврата: `0` — всё выбранное прошло, `1` — доказано нарушение, `2` — проверить
достоверно не удалось (сюда же относится отсутствующий или невалидный `.harness.json`).

## Что харнес считает сам

То, что не воспроизводит чужой пайплайн и что в каждом репозитории расходится:

- maintainability hotspots — размер файлов, типов и методов, ветвление, ширина API;
- нормализованные межфайловые повторы C#;
- документационная политика: один корневой навигационный документ и симлинки на него.

Метрики связанности и связности — следующий шаг.

## Граница ответственности

Репозиторий хранит знания, специфичные для продукта: код, бизнес-тесты, исполняемые
контракты, архитектурные тесты конкретной системы, ADR. Его CI отвечает за то, что всё это
собирается и проходит. Харнес хранит переиспользуемую рамку и не пытается угадать
правильную архитектуру проекта.
