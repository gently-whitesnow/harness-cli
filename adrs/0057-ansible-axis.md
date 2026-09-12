# ADR-0057: Ось `ansible` — что харнес измеряет сам, а что оставляет линтеру

## Status

Proposed. Развивает [ADR-0022](0022-language-axis.md) (ось = экземпляр, ридер, строка в
реестре) и [ADR-0054](0054-explicit-only-frame.md) (`FrameAxis`, детекция по индексу);
опирается на [ADR-0056](0056-configuration-repository-frame.md) для `comments.yaml` и
generated docs. Код не реализован; принятие — minor-релиз контракта 3.x.

## Context

Пилот ADR-0056 — Ansible-репозиторий одного VPS: playbook в корне, роли в `roles/<name>/`,
`group_vars`, compose-файлы как `.j2`-шаблоны ролей с образами из `group_vars`, секреты в
внешнем хранилище через lookup, CI из `ansible-playbook --syntax-check` и `ansible-lint`.
После ADR-0056 рамка проходит, но об Ansible харнес не говорит ничего: ось `yaml` видит
только плотность комментариев.

Исследование того, что проверяют в Ansible/IaC-репозиториях, дало три группы. Линтер:
`ansible-lint` с профилями и правилами `fqcn`, `name[...]`, `no-changed-when`,
`risky-file-permissions`, `no-log-password`, `package-latest`, `var-naming`, `yaml[...]`
через yamllint; подавления — адресное `# noqa: rule` в строке задачи и repo-wide
`skip_list`/`warn_list` в `.ansible-lint`. Запуск: `--syntax-check`, `--check`, molecule с
идемпотентностью вторым прогоном. Инварианты, которые ни один инструмент не считает одинаково:
образы по immutable digest (compose-lint/checkov проверяют compose, но не Jinja-шаблоны),
секреты в `!vault`/lookup вместо литерала (gitleaks ищет содержимое, не форму хранения),
граф зависимостей ролей (`ansible-playbook-grapher` рисует, не судит), раскладка роли.

Принципы: харнес не запускает toolchain (ADR-0011) и не дублирует линтер — считает то, чего
линтер не даёт; blocking только при пяти условиях ADR-0002; ребро графа несёт `Proven` или
`Inferred`, blocking — только из `Proven` (ADR-0021); policy — `required/advisory/off`
без адресного подавления (ADR-0035). Обсуждались альтернативы: детекция оси по суффиксу
(`.yml` уже занят осью `yaml`); отдельная ось `compose`; чтение отчёта `ansible-lint`;
`duplication.ansible` и DSM для ролей.

## Decision

1. **Ось `ansible` детектируется по маркерам, не по суффиксу.** `FrameAxis.Sources`
   расширяется с масок суффиксов до маркер-файлов (прецедент — `FrameAxis.DotNet` через
   project-файлы): tracked `ansible.cfg`, либо `roles/*/tasks/main.yml`, либо YAML в корне,
   чей top-level — список с ключом `hosts:`. Ось — надстройка над `yaml`: `applicability.ansible`
   включает только проверки ниже, `comments.yaml` остаётся в оси `yaml`. Ридер `AnsibleSources`
   в `Infrastructure/Languages/Ansible/` читает YAML лексически по отступам, как
   `lint-suppressions.go` читает golangci-конфиг; якоря, merge keys и flow-формы — отсутствие,
   не находка.
2. **`dependencies.ansible` — цикл ролей.** Узел — `roles/<name>`; ребро — `dependencies` в
   `meta/main.yml`, `include_role`/`import_role` с литеральным `name:` в `tasks/*.yml`
   (`Proven`); `name: "{{ var }}"` — `Inferred`, в blocking не входит. То же ядро `Structure/`
   (`StronglyConnectedComponents`, `ShortestCycle`), что для C# и Go. Находка — кратчайшее
   кольцо ролей с файлами рёбер. Remediation: вынести общую часть в третью роль или заменить
   include на `dependencies` одной стороны. Негативная фикстура: две роли, включающие друг
   друга. `init` пишет `required`.
3. **`lint-suppressions.ansible` — аналог `lint-suppressions.go`.** `# noqa: rule` в строке
   задачи без причины на той же строке (`# noqa: rule -- почему`) — blocking: ansible-lint
   причину не требует, харнес требует. `skip_list` и `warn_list` в tracked `.ansible-lint`
   печатаются как repo-wide отключение в verbose details, не находка. Линтер не запускается,
   отчёт не читается. Негативная фикстура: задача с голым `# noqa: fqcn`. `init` — `required`.
4. **`images.ansible` — образ только по digest.** Любая строка `image:` в tracked
   `.yml/.yaml/.j2` вне generated и вне `docs.generated`, чьё значение — литерал без
   `@sha256:`, — blocking; значение из Jinja-выражения (`{{ ... }}`) прослеживается до литерала
   в `group_vars`/`defaults`/`vars` по имени переменной, иначе `Inferred` и в details.
   Это supply-chain-инвариант compose-lint/checkov, который они не считают по шаблонам.
   Отдельная ось `compose` отклонена: в Ansible-репозитории compose — шаблон роли; ID для
   голого compose-репозитория — вне этого ADR. Негативная фикстура: `image: nginx:latest`
   в `templates/compose.yml.j2`. `init` — `required`.
5. **`secrets.ansible` — форма хранения, не содержимое.** В `group_vars/`, `host_vars/`,
   `vars/`, `defaults/`: значение ключа, чьё имя совпадает со словарём (`password`, `secret`,
   `token`, `api_key`, `private_key`, суффикс `_key`), должно быть `!vault`-блоком,
   Jinja-выражением (lookup) или пустым; литерал — blocking. Энтропия и паттерны
   провайдеров — работа gitleaks через `answers.*`. Задача с такой переменной без
   `no_log: true` — advisory: правило `no-log-password` есть в ansible-lint, харнес его не
   дублирует, а лишь называет; FP на тестовых значениях в `defaults/` принят и снимается
   `!vault` или пустым значением. Негативная фикстура: `db_password: hunter2` в `group_vars`.
6. **`role-shape.ansible` — аналог `zone-shape`.** `roles/<name>/` содержит только каталоги
   словаря Ansible (`tasks defaults vars handlers templates files meta library module_utils
   filter_plugins lookup_plugins tests molecule`) и `README.md`; `tasks/main.yml` обязателен,
   если есть `tasks/`; playbook-файл (список с `hosts:`) под `roles/` — нарушение. `init` пишет
   `advisory`: нестандартные раскладки коллекций дают FP.
7. **Не делается.** `fqcn`, `name[...]`, `no-changed-when`, `risky-file-permissions`,
   `package-latest`, формат yamllint — это `ansible-lint` через `answers.lint`; идемпотентность
   и molecule — запуск (ADR-0011); `complexity.ansible` — у ролей нет единицы достижимости
   через данные; `duplication.ansible` — повторы задач между ролями редко совпадают лексически
   и не стоят копии словаря токенизатора; `types-per-file.ansible` — нет единицы. Отказ
   зафиксирован, чтобы следующий пилот не выдумывал формулу заново.
8. **Контракт — minor.** Новая ось, пять policy-записей, без секций настроек; рамки без
   маркеров Ansible не меняются, с маркерами — получают фрагмент от `harness.coverage` и
   `upgrade` (ADR-0054 п. 4–5).

## Consequences

### Positive

- Ожидается, что на пилоте `init` пишет оси `yaml` + `ansible`; `dependencies.ansible`,
  `images.ansible` и `secrets.ansible` проходят (граф ролей ациклический, оба образа по
  digest, секреты через lookup); `lint-suppressions.ansible` — по факту `# noqa` в задачах.
- Харнес считает три инварианта, которых нет в `ansible-lint`, и ни одно его правило не
  копирует; граф ролей — тем же ядром `Structure/`, что модули C# и пакеты Go.
- Маркер-детекция — прецедент для осей без собственного суффикса; таблица `FrameAxis`
  остаётся одной для `init`, `upgrade` и `coverage`.

### Negative / Risks

- Лексический ридер YAML без грамматики: многодокументные файлы, якоря и `!vault` внутри
  flow-mapping читаются как отсутствие; `include_role` в `block:`/`rescue:` глубже одного
  уровня требует чтения по отступам, ошибка — `Inferred`, не blocking.
- Словарь имён секретов и словарь каталогов роли — таблицы в коде; ключ вне словаря
  (`db_pw`) не проверяется, и это принято: проверка не ищет секреты, а требует формы для
  очевидных имён.
- `images.ansible` прослеживает переменную только по имени и только в трёх каталогах:
  образ из `set_fact` или inventory — `Inferred` в details, не находка.
- Дефолты policy (`required` для четырёх проверок) выбраны по одному пилоту; пересмотр —
  отдельным ADR по данным следующих.
