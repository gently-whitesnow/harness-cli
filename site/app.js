/* Harness CLI landing · contract 3.2.0 · no dependencies. */
(function () {
  'use strict';

  const themeButton = document.querySelector('#theme-toggle');
  themeButton.hidden = false;
  themeButton.addEventListener('click', () => {
    const next = document.documentElement.dataset.theme === 'dark' ? 'light' : 'dark';
    document.documentElement.dataset.theme = next;
    try { localStorage.setItem('harness-site-theme', next); } catch (_) {}
  });

  const CHECKS = [
    { id: 'harness.config', group: 'common', axis: null, summary: 'Tracked .harness.json: версия, ответы и правила явно заданы для этого репозитория; проверка вне policy — вне рамки.', adr: ['0014-frame-answers-are-self-reported.md', '0016-versioned-frame-and-explicit-initialization.md', '0054-explicit-only-frame.md'] },
    { id: 'harness.coverage', group: 'common', axis: null, summary: 'Язык или стек с tracked-исходниками должен быть объявлен в applicability или отклонён с причиной; находка печатает готовый фрагмент конфига. Суффиксы вне осей видны в verbose details без новой находки.', adr: ['0054-explicit-only-frame.md'] },
    { id: 'architecture.sliced-dotnet.zone-shape', group: 'arch', axis: null, summary: 'Канонические зоны, обязательные слои и расположение исходников.', adr: ['0053-explicit-architecture-checks.md'] },
    { id: 'architecture.sliced-dotnet.slice-shape', group: 'arch', axis: null, summary: 'Структура слайсов и групп, обязательные входные зеркала, отсутствие сирот.', adr: ['0053-explicit-architecture-checks.md'] },
    { id: 'architecture.sliced-dotnet.segment-names', group: 'arch', axis: null, summary: 'Явный словарь запрещённых имён слайсов и непосредственных сегментов.', adr: ['0053-explicit-architecture-checks.md'] },
    { id: 'architecture.sliced-dotnet.layer-assemblies', group: 'arch', axis: null, summary: 'Слой = сборка: один проект, собственные исходники и допустимые ProjectReference.', adr: ['0053-explicit-architecture-checks.md'] },
    { id: 'architecture.sliced-dotnet.dependency-direction', group: 'arch', axis: null, summary: 'Доказанные импорты соблюдают направления слоёв и границы зон.', adr: ['0053-explicit-architecture-checks.md'] },
    { id: 'architecture.sliced-dotnet.slice-isolation', group: 'arch', axis: null, summary: 'Слайсы одного слоя взаимодействуют через явный X-контракт.', adr: ['0053-explicit-architecture-checks.md'] },
    { id: 'architecture.sliced-dotnet.public-api', group: 'arch', axis: null, summary: 'Вход в Application-слайс через Contracts; у слоя общего API нет.', adr: ['0053-explicit-architecture-checks.md'] },
    { id: 'architecture.sliced-dotnet.cross-api', group: 'arch', axis: null, summary: 'X-контракт импортирует только адресованный потребитель или Host.', adr: ['0053-explicit-architecture-checks.md'] },
    { id: 'complexity.csharp', group: 'csharp', axis: 'csharp', summary: 'Ограничивает связанность файлов: средняя достижимость файлов ≤ 8, размер циклической группы — 0 по умолчанию.', adr: ['0032-topology-over-thresholds.md', '0042-dsm-over-the-product-in-files.md', '0048-dsm-product-boundary-without-a-zone.md', '0052-dsm-ceiling-is-a-declared-setting.md'] },
    { id: 'docs.policy', group: 'common', axis: null, summary: 'Короткие AGENTS.md и README.md — до 150 строк. CLAUDE.md ссылается на соседний AGENTS.md; решения живут в adrs/, навыки — в SKILL.md. Прочий Markdown запрещён.', adr: ['0010-documentation-policy.md', '0025-nested-agent-documents.md'] },
    { id: 'commits.setup', group: 'common', axis: null, summary: 'Проверяет установку шаблона коммитов и commit-msg hook на весь клон. Подготовка: harness setup.', adr: ['0020-commit-message-contract-and-clone-setup.md', '0052-hook-resolves-the-harness-at-commit-time.md'] },
    { id: 'comments.csharp', group: 'csharp', axis: 'csharp', summary: 'Ограничивает плотность комментариев: по умолчанию находка от 10 строк комментариев, если их больше 8% авторских строк.', adr: ['0028-recalibrated-csharp-defaults.md', '0043-comment-density-across-languages.md'] },
    { id: 'comments.yaml', group: 'langs', axis: 'yaml', summary: 'Плотность прозы YAML: от 10 строк и больше 8%. Блоки над ключами любой глубины, trailing-комментарии и директивы исключены; init всегда ставит required; смягчение обсуждается с владельцем.', adr: ['0043-comment-density-across-languages.md', '0056-configuration-repository-frame.md'] },
    { id: 'comments.typescript', group: 'langs', axis: 'typescript', summary: 'Та же проверка для TypeScript и JavaScript: от 10 строк и больше 8%.', adr: ['0043-comment-density-across-languages.md'] },
    { id: 'comments.go', group: 'go', axis: 'go', summary: 'Плотность комментариев в Go: от 10 строк и больше 8%. Doc-комментарии top-level деклараций и директивы //go: не считаются; generated, vendor и testdata исключены.', adr: ['0055-go-language-axis.md', '0043-comment-density-across-languages.md'] },
    { id: 'types-per-file.csharp', group: 'csharp', axis: 'csharp', summary: 'Не больше одного верхнеуровневого class или record в файле. Имя файла указывает на одно понятие.', adr: ['0018-csharp-applicability-and-one-type-per-file.md'] },
    { id: 'dependencies.csharp', group: 'csharp', axis: 'csharp', summary: 'Находит доказанные циклы между модулями и показывает строки, замыкающие кольцо.', adr: ['0021-coupling-evidence-grades.md', '0029-dependency-counts-removed.md'] },
    { id: 'duplication.csharp', group: 'csharp', axis: 'csharp', summary: 'Находит повторяющиеся блоки в разных файлах, даже с другими именами и литералами. Дефолтное окно — 30 нормализованных строк и минимум 90 токенов.', adr: ['0045-duplication-required-by-default.md', '0007-one-finding-one-report.md'] },
    { id: 'duplication.go', group: 'go', axis: 'go', summary: 'Повторяющиеся блоки в разных Go-файлах после нормализации: имена → n, литералы → токен, ключевые слова и предопределённые идентификаторы остаются. Окно 30 строк и 90 токенов.', adr: ['0055-go-language-axis.md', '0007-one-finding-one-report.md'] },
    { id: 'complexity.go', group: 'go', axis: 'go', summary: 'DSM по графу пакетов через tracked go.mod: средняя достижимость пакетов ≤ 8, циклическая группа — 0 (компилятор запрещает циклы). main-пакеты — composition root.', adr: ['0055-go-language-axis.md', '0052-dsm-ceiling-is-a-declared-setting.md'] },
    { id: 'lint-suppressions.go', group: 'go', axis: 'go', summary: 'Голый //nolint и //nolint:linter без «// причины» блокируются. Repo-wide отключения в .golangci.yml/.toml/.json печатаются в отчёте; сам линтер не запускается.', adr: ['0055-go-language-axis.md', '0044-editorconfig-baseline-and-warning-suppressions.md'] },
    { id: 'images.ansible', group: 'ansible', axis: 'ansible', summary: 'Образы в YAML и Jinja требуют полного @sha256: и 64 hex-символов. Jinja-значения показаны как Inferred, переменные не прослеживаются.', adr: ['0057-ansible-axis.md'] },
    { id: 'secrets.ansible', group: 'ansible', axis: 'ansible', summary: 'Литералы от 8 символов под узким словарём имён секретов в vars; публичные ключи и ссылки на поля исключены. По умолчанию required.', adr: ['0057-ansible-axis.md'] },
    { id: 'lint-suppressions.ansible', group: 'ansible', axis: 'ansible', summary: 'Голый inline # noqa и подавление без причины блокируются; skip_list и warn_list показываются как решения всего репозитория.', adr: ['0057-ansible-axis.md'] },
    { id: 'dependencies.ansible', group: 'ansible', axis: 'ansible', summary: 'Циклы ролей по meta dependencies, include_role и import_role. Литеральные имена доказаны; Jinja-имена не входят в граф.', adr: ['0057-ansible-axis.md'] },
    { id: 'role-shape.ansible', group: 'ansible', axis: 'ansible', summary: 'Каталоги роли по словарю Ansible; при наличии tasks нужен main.yml или main.yaml. По умолчанию required.', adr: ['0057-ansible-axis.md'] },
    { id: 'build-properties.dotnet', group: 'dotnet', axis: 'dotnet', summary: 'Единые настройки сборки: nullable, анализаторы, warnings как errors и воспроизводимость в Directory.Build.props.', adr: ['0019-dotnet-repository-policy.md'] },
    { id: 'central-packages.dotnet', group: 'dotnet', axis: 'dotnet', summary: 'Версии пакетов в Directory.Packages.props, без локальных переопределений в проектах.', adr: ['0019-dotnet-repository-policy.md'] },
    { id: 'solution-format.dotnet', group: 'dotnet', axis: 'dotnet', summary: 'Формат решения .slnx; каждый SDK-style проект включён в решение.', adr: ['0019-dotnet-repository-policy.md'] },
    { id: 'editorconfig.dotnet', group: 'dotnet', axis: 'dotnet', summary: 'Единый эталон стиля в .editorconfig над каждым проектом. harness explain editorconfig.dotnet показывает его целиком.', adr: ['0044-editorconfig-baseline-and-warning-suppressions.md'] },
    { id: 'warning-suppressions.dotnet', group: 'dotnet', axis: 'dotnet', summary: 'Запрещает подавлять предупреждения для отдельного файла или места. Отключение правила во всём репозитории остаётся видимым в отчёте.', adr: ['0044-editorconfig-baseline-and-warning-suppressions.md'] },
    { id: 'frame.tests.unit', group: 'frame', summary: 'Где unit-тесты или почему их нет. Адрес — тестовый проект, а не отдельные тестовые файлы.', adr: ['0049-test-suite-address-is-the-project.md'] },
    { id: 'frame.tests.integration', group: 'frame', summary: 'Где интеграционные тесты или почему их нет. Адрес — тестовый проект.', adr: ['0049-test-suite-address-is-the-project.md'] },
    { id: 'frame.tests.architecture', group: 'frame', summary: 'Где тесты архитектурных правил или почему их нет.' },
    { id: 'frame.format', group: 'frame', summary: 'Чем проверяется формат кода.' },
    { id: 'frame.lint', group: 'frame', summary: 'Где настроен статический анализ.' },
    { id: 'frame.build', group: 'frame', summary: 'Как запустить сборку.' },
    { id: 'frame.typecheck', group: 'frame', summary: 'Чем проверяются типы или почему отдельная проверка неприменима.' },
    { id: 'frame.verify', group: 'frame', summary: 'Где единый verify-скрипт, который запускает проверки проекта, включая harness check. Здесь обязателен путь.', adr: ['0046-unified-verification-entry-point.md'] },
  ];

  const GROUPS = [
    { key: 'common', target: 'common-checks', title: 'Правила репозитория' },
    { key: 'frame', target: 'frame-checks', title: 'Ответы о проверках проекта' },
    { key: 'csharp', target: 'csharp-checks', title: 'C#' },
    { key: 'dotnet', target: 'dotnet-checks', title: '.NET' },
    { key: 'arch', target: 'architecture-checks', title: 'Архитектура .NET-приложения' },
    { key: 'langs', target: 'language-checks', title: 'YAML, TypeScript и JavaScript' },
    { key: 'ansible', target: 'ansible-checks', title: 'Ansible' },
    { key: 'go', target: 'go-checks', title: 'Go' },
  ];

  for (const group of GROUPS) {
    const list = document.querySelector('#' + group.target);
    for (const check of CHECKS.filter((item) => item.group === group.key)) {
      const item = document.createElement('li');
      const id = document.createElement('code');
      id.textContent = check.id;
      const summary = document.createElement('p');
      summary.textContent = check.summary;
      item.append(id, summary);
      if (check.adr) {
        const links = document.createElement('div');
        links.className = 'sources';
        for (const adr of check.adr) {
          const link = document.createElement('a');
          link.href = 'https://github.com/gently-whitesnow/harness-cli/blob/master/adrs/' + adr;
          link.textContent = 'ADR-' + adr.slice(0, 4);
          links.append(link);
        }
        item.append(links);
      }
      list.append(item);
    }
  }
})();
