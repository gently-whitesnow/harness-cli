# ADR-0057: Ось `ansible` — что харнес измеряет сам, а что оставляет линтеру

## Status

Accepted. Развивает [ADR-0022](0022-language-axis.md) (ось = экземпляр, ридер, строка в
реестре) и [ADR-0054](0054-explicit-only-frame.md) (`FrameAxis`, детекция по индексу);
опирается на [ADR-0056](0056-configuration-repository-frame.md) для `comments.yaml` и
подсказок init. Контракт 3.2.

## Context

Пилот ADR-0056 — Ansible-репозиторий одного VPS: playbook в корне, роли в `roles/<name>/`,
`group_vars`, compose-файлы как `.j2`-шаблоны ролей с образами из `group_vars`, секреты во
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
`duplication.ansible` и DSM для ролей. Рецензент проекта внёс две поправки: (P1) правило
«любой `*_key` — секрет» блокировало бы корректный конфиг — публичный ключ VPN-пира
(`public_key`) и имя поля хранилища (`token_key: token`); (P2) проверка образов должна
требовать полный digest, а не подстроку `@sha256:` — `nginx@sha256:garbage` не образ.

## Decision

1. **Ось `ansible` детектируется по маркерам, не по суффиксу.** `FrameAxis` расширяется с
   масок суффиксов до маркеров (прецедент — `FrameAxis.DotNet` через project-файлы):
   tracked `ansible.cfg` в корне, `roles/*/tasks/main.yml`, `playbooks/*.yml` или `*.yml` в
   корне, чей top-level — список с ключом `hosts:` (читается лексически, только корень).
   Ось — надстройка над `yaml`: `applicability.ansible` включает только проверки ниже,
   `comments.yaml` остаётся в оси `yaml`; ID — `<семейство>.ansible`. Область чтения —
   tracked `*.yml`/`*.yaml` (и `*.j2` для образов) вне generated/vendored, вне каталогов,
   начинающихся с точки (`.venv*`, `.ansible/`, `.github/`), и вне `molecule/` — молекулу
   не судим. Ридер `AnsibleSources` в `Infrastructure/Languages/Ansible/` читает YAML
   лексически по отступам, как `lint-suppressions.go` читает golangci-конфиг; якоря, merge
   keys и flow-формы — отсутствие, не находка. Без маркера все пять проверок `NotApplicable`.
2. **`images.ansible` — образ только по полному digest.** Каждая строка `image: <значение>`
   (ключ `image` в mapping, значение — скаляр) в tracked `.yml/.yaml/.j2` оканчивается
   `@sha256:` и ровно 64 hex-символами после снятия кавычек и trailing-комментария.
   `nginx@sha256:garbage`, короткий digest, пустое значение, `:latest` и тег без digest —
   blocking (P2). Значение с Jinja `{{ … }}` — `Inferred`: печатается в details, не
   блокирует; переменная не прослеживается. Это supply-chain-инвариант compose-lint/checkov,
   который они не считают по шаблонам. Отдельная ось `compose` отклонена: в
   Ansible-репозитории compose — шаблон роли. `init` — `required`.
3. **`secrets.ansible` — форма хранения, не содержимое, в узкой области.** Область — только
   `group_vars/**`, `host_vars/**`, `roles/*/defaults/**`, `roles/*/vars/**`, `vars/**` и
   `inventory*` yml. Словарь имён (ключ mapping, без учёта регистра, по полному имени или
   сегменту после `_`/`-`): `password`, `passwd`, `secret`, `secret_id`, `token`,
   `api_key`, `apikey`, `access_key`, `private_key`, `privatekey`, `client_secret`.
   Явные исключения (P1): `public_key`, `pubkey`, `*_key_file`, `*_key_path`, `*_key_name`,
   `*_keys`, `token_file`, `token_ttl`, `*_ttl`; `*_key` без словарного префикса — не
   секрет. Значение — литерал-строка длиной ≥ 8 без `{{`, без `!vault`, не пустое, не
   `null`/`~` — находка; ссылка на имя поля (`token_key: token`, короче 8 символов, либо
   snake_case-идентификатор вида `master_password` — имя ключа во внешнем хранилище) —
   не находка. Энтропия и паттерны провайдеров — работа gitleaks через `answers.*`.
   `init` — `advisory`: правило калибровано на одном репозитории, а FP на тестовых значениях
   в `defaults/` снимается `!vault` или lookup; пересмотр — по фикстурам с реальных
   репозиториев. Задачи без `no_log` не проверяются (см. «Не делается»).
4. **`lint-suppressions.ansible` — аналог `lint-suppressions.go`.** `# noqa: <rule>` (и
   `# noqa <rule>`) без причины после ` -- ` или отдельного `#` на той же строке — blocking;
   голый `# noqa` — blocking: ansible-lint причину не требует, харнес требует. `skip_list` и
   `warn_list` в tracked `.ansible-lint` (`.yml`/`.yaml`) печатаются как repo-wide
   отключение в verbose details, не находка. Линтер не запускается, отчёт не читается.
   `init` — `required`.
5. **`dependencies.ansible` — цикл ролей тем же ядром `Structure/`.** Узел — `roles/<name>`;
   ребро — `dependencies:` в `roles/<name>/meta/main.yml` (`- role: x` или `- x`) и
   `include_role`/`import_role` с литеральным `name:` в `roles/<name>/tasks/**` и
   `handlers/**` — `Proven`; `name:` с Jinja — `Inferred`, в blocking не входит; роли вне
   `roles/` (коллекции, galaxy) — внешние, не узлы. Playbooks в граф не входят. Находка —
   кратчайшее кольцо через `StronglyConnectedComponents` и `ShortestCycle`, одна на цикл
   (ADR-0007), с файлами рёбер. `init` — `required`.
6. **`role-shape.ansible` — аналог `zone-shape`.** `roles/<name>/` содержит только каталоги
   словаря Ansible (`tasks defaults vars handlers templates files meta library module_utils
   filter_plugins lookup_plugins test_plugins action_plugins tests molecule`) и `README.md`;
   при наличии `tasks/` обязателен `tasks/main.yml` (или `main.yaml`); yml-файл
   непосредственно в `roles/<name>/` — нарушение. `init` — `advisory`: нестандартные
   раскладки коллекций дают FP.
7. **Не делается.** `fqcn`, `name[...]`, `no-changed-when`, `risky-file-permissions`,
   `package-latest`, формат yamllint — это `ansible-lint` через `answers.lint`; идемпотентность
   и molecule — запуск (ADR-0011); `no_log` для задач с секретной переменной — правило
   `no-log-password` линтера, харнес его не дублирует; `complexity.ansible` — у ролей нет
   единицы достижимости через данные; `duplication.ansible` — повторы задач между ролями
   редко совпадают лексически и не стоят копии словаря токенизатора; `types-per-file.ansible`
   — нет единицы; отдельная ось `compose`. Отказ зафиксирован, чтобы следующий пилот не
   выдумывал формулу заново.
8. **Контракт — minor (3.2).** Новая ось, пять policy-записей, без секций настроек; рамки без
   маркеров Ansible не меняются, с маркерами — получают фрагмент от `harness.coverage` и
   `upgrade` (ADR-0054 п. 4–5).

## Consequences

### Positive

- На копии пилота `init` пишет оси `yaml` + `ansible`; `dependencies.ansible` и
  `images.ansible` проходят (граф ролей ациклический, оба образа по digest; образ в
  Jinja-шаблоне — `Inferred` в details), `lint-suppressions.ansible` печатает `skip_list`
  как деталь, `role-shape.ansible` проходит.
- Харнес считает четыре инварианта, которых нет в `ansible-lint`, и ни одно его правило не
  копирует; граф ролей — тем же ядром `Structure/`, что модули C# и пакеты Go.
- Маркер-детекция — прецедент для осей без собственного суффикса; таблица `FrameAxis`
  остаётся одной для `init`, `upgrade` и `coverage`.

### Negative / Risks

- Лексический ридер YAML без грамматики: якоря, merge keys и flow-mapping читаются как
  отсутствие; block scalar складывается в значение ключа, поэтому `image:` внутри inline
  compose в `content: |` не судится; `include_role` в `block:`/`rescue:` читается по
  отступам, ошибка — `Inferred`, не blocking.
- Словари имён секретов и каталогов роли — таблицы в коде; ключ вне словаря (`db_pw`) не
  проверяется, и это принято: проверка не ищет секреты, а требует формы для очевидных имён.
  Snake_case-значение читается как ссылка на имя поля — пароль из одних строчных букв и
  подчёркиваний пройдёт.
- `images.ansible` не прослеживает переменную: образ из `set_fact` или inventory — `Inferred`
  в details, не находка.
- Дефолты policy (`required` для трёх проверок, `advisory` для двух) выбраны по одному
  пилоту; пересмотр — отдельным ADR по данным следующих.
