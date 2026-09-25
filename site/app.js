/* Harness CLI landing · contract 3.9.1 · English at /, Russian at /ru/ · no dependencies. */
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
    { id: 'harness.config', group: 'common', axis: null, summary: {
      en: 'Tracked .harness.json: one root version, explicit workspace projects and local frames without inheritance; a check the policy does not name is outside the frame. Excluded generated code and repository-wide rule switches are declared only here.',
      ru: 'Tracked .harness.json: одна корневая версия, явные проекты workspace и локальные рамки без наследования; проверка вне policy — вне рамки. Исключение generated-кода и глобальное выключение правил объявляются только здесь.',
    }, adr: ['0014-frame-answers-are-self-reported.md', '0016-versioned-frame-and-explicit-initialization.md', '0054-explicit-only-frame.md', '0060-explicit-workspace-projects.md', '0067-reviewed-weakening-in-the-frame.md'] },
    { id: 'harness.coverage', group: 'common', axis: null, summary: {
      en: 'A language or stack with tracked sources must be declared applicable or declined with a reason; the finding prints a ready config fragment. A generated marker outside the declared generated paths, a stale generated path and applicable: true without sources block; excluded paths are shown in scope.',
      ru: 'Язык или стек с tracked-исходниками должен быть объявлен в applicability или отклонён с причиной; находка печатает готовый фрагмент конфига. Generated-маркер вне объявленного generated, устаревший путь generated и applicable: true без исходников блокируют; исключённые пути видны в scope.',
    }, adr: ['0054-explicit-only-frame.md', '0067-reviewed-weakening-in-the-frame.md'] },
    { id: 'architecture.sliced-dotnet.zone-shape', group: 'arch', axis: null, summary: {
      en: 'Canonical zones, required layers and source placement.',
      ru: 'Канонические зоны, обязательные слои и расположение исходников.',
    }, adr: ['0053-explicit-architecture-checks.md', '0033-canonical-standard-over-declarations.md'] },
    { id: 'architecture.sliced-dotnet.slice-shape', group: 'arch', axis: null, summary: {
      en: 'Slice and group structure, required input mirrors, no orphans.',
      ru: 'Структура слайсов и групп, обязательные входные зеркала, отсутствие сирот.',
    }, adr: ['0053-explicit-architecture-checks.md', '0051-slices-in-the-layer-root.md'] },
    { id: 'architecture.sliced-dotnet.segment-names', group: 'arch', axis: null, summary: {
      en: 'An explicit vocabulary of forbidden slice names and direct segment names.',
      ru: 'Явный словарь запрещённых имён слайсов и непосредственных сегментов.',
    }, adr: ['0053-explicit-architecture-checks.md', '0037-segments-by-purpose.md'] },
    { id: 'architecture.sliced-dotnet.layer-assemblies', group: 'arch', axis: null, summary: {
      en: 'The layer is the assembly: one project, its own sources and only allowed ProjectReference edges.',
      ru: 'Слой = сборка: один проект, собственные исходники и допустимые ProjectReference.',
    }, adr: ['0053-explicit-architecture-checks.md', '0041-layer-is-the-assembly.md'] },
    { id: 'architecture.sliced-dotnet.dependency-direction', group: 'arch', axis: null, summary: {
      en: 'Proven imports follow the layer directions and zone boundaries.',
      ru: 'Доказанные импорты соблюдают направления слоёв и границы зон.',
    }, adr: ['0053-explicit-architecture-checks.md', '0036-input-layers-read-domain.md', '0050-domain-is-the-bottom-layer.md'] },
    { id: 'architecture.sliced-dotnet.slice-isolation', group: 'arch', axis: null, summary: {
      en: 'Slices of one layer communicate through an explicit X cross-API.',
      ru: 'Слайсы одного слоя взаимодействуют через явный X-контракт.',
    }, adr: ['0053-explicit-architecture-checks.md'] },
    { id: 'architecture.sliced-dotnet.public-api', group: 'arch', axis: null, summary: {
      en: 'An Application slice is entered through its Contracts; a layer has no shared API.',
      ru: 'Вход в Application-слайс через Contracts; у слоя общего API нет.',
    }, adr: ['0053-explicit-architecture-checks.md', '0051-slices-in-the-layer-root.md'] },
    { id: 'architecture.sliced-dotnet.cross-api', group: 'arch', axis: null, summary: {
      en: 'An X cross-API is imported only by its named consumer or by Host.',
      ru: 'X-контракт импортирует только адресованный потребитель или Host.',
    }, adr: ['0053-explicit-architecture-checks.md'] },
    { id: 'complexity.csharp', group: 'csharp', axis: 'csharp', summary: {
      en: 'Limits file coupling: average reachable files at most 8 and largest cyclic group 0 by default.',
      ru: 'Ограничивает связанность файлов: средняя достижимость файлов ≤ 8, размер циклической группы — 0 по умолчанию.',
    }, adr: ['0032-topology-over-thresholds.md', '0042-dsm-over-the-product-in-files.md', '0048-dsm-product-boundary-without-a-zone.md', '0052-dsm-ceiling-is-a-declared-setting.md'] },
    { id: 'adrs.shape', group: 'common', axis: null, summary: {
      en: 'An ADR as a decision record: four digits and contiguous unique numbers, status, date and sections; settings.adrs.shape sets the limits of 1000 words, 10 code lines and 12 table rows.',
      ru: 'ADR как запись решения: четыре цифры и непрерывные уникальные номера, статус, дата и разделы; пределы 1000 слов, 10 строк кода и 12 строк таблицы задаёт settings.adrs.shape.',
    }, adr: ['0066-adr-shape-as-decision-record.md'] },
    { id: 'docs.policy', group: 'common', axis: null, summary: {
      en: 'Short AGENTS.md and README.md, up to 150 lines; a README.<language>.md translation sits beside README.md with the same limit. CLAUDE.md is a direct relative symlink to the sibling AGENTS.md; decisions live in adrs/, skills in skills/<name>/SKILL.md and the agent toolchain mirrors. DESIGN.md and forge files (community health, issue/PR templates in the root, .github/, docs/) are allowed without a limit. Any other Markdown is a violation.',
      ru: 'Короткие AGENTS.md и README.md — до 150 строк; перевод README.<язык>.md — рядом с README.md, с тем же лимитом. CLAUDE.md — прямой относительный симлинк на соседний AGENTS.md; решения живут в adrs/, навыки — в skills/<name>/SKILL.md и зеркалах агентского тулчейна. DESIGN.md и файлы форджа (community health, шаблоны issue/PR в корне, .github/, docs/) разрешены без лимита. Прочий Markdown запрещён.',
    }, adr: ['0010-documentation-policy.md', '0025-nested-agent-documents.md', '0061-forge-and-design-documents.md', '0068-readme-translations.md'] },
    { id: 'commits.setup', group: 'common', axis: null, summary: {
      en: 'Checks that the commit template and commit-msg hook are active for the whole clone, and that the harness the hook resolves at commit time exists and matches the pin. Setup: harness setup.',
      ru: 'Проверяет, что шаблон коммитов и commit-msg hook активны на весь клон, а харнес, который hook найдёт при коммите, существует и совпадает с pin. Подготовка: harness setup.',
    }, adr: ['0020-commit-message-contract-and-clone-setup.md', '0065-hook-resolves-the-harness-at-commit-time.md'] },
    { id: 'comments.csharp', group: 'csharp', axis: 'csharp', summary: {
      en: 'Limits comment density: by default a finding starts at 10 comment lines when they exceed 8% of authored lines.',
      ru: 'Ограничивает плотность комментариев: по умолчанию находка от 10 строк комментариев, если их больше 8% авторских строк.',
    }, adr: ['0028-recalibrated-csharp-defaults.md', '0043-comment-density-across-languages.md'] },
    { id: 'comments.yaml', group: 'langs', axis: 'yaml', summary: {
      en: 'YAML prose density: from 10 lines and above 8%. Blocks above keys at any depth, trailing comments and directives are not counted; init always writes required; any relaxation is agreed with the owner.',
      ru: 'Плотность прозы YAML: от 10 строк и больше 8%. Блоки над ключами любой глубины, trailing-комментарии и директивы исключены; init всегда ставит required; смягчение обсуждается с владельцем.',
    }, adr: ['0043-comment-density-across-languages.md', '0056-configuration-repository-frame.md'] },
    { id: 'comments.typescript', group: 'langs', axis: 'typescript', summary: {
      en: 'The same check for TypeScript and JavaScript: from 10 lines and above 8%.',
      ru: 'Та же проверка для TypeScript и JavaScript: от 10 строк и больше 8%.',
    }, adr: ['0043-comment-density-across-languages.md'] },
    { id: 'complexity.typescript', group: 'langs', axis: 'typescript', summary: {
      en: 'A file DSM over TS/JS with transparent barrels; initial limits: average reachable files 8, largest cyclic group 0.',
      ru: 'DSM по файлам TS/JS с прозрачными barrels; стартовые пределы: средняя достижимость 8, циклическая группа 0.',
    }, adr: ['0064-typescript-language-axis.md'] },
    { id: 'dependencies.typescript', group: 'langs', axis: 'typescript', summary: {
      en: 'Proven directory cycles through literal import, export from, require and import(), including import type; unresolved imports are shown in details.',
      ru: 'Доказанные циклы каталогов по литеральным import, export from, require и import(), включая import type; неразрешённые импорты видны в деталях.',
    }, adr: ['0064-typescript-language-axis.md'] },
    { id: 'duplication.typescript', group: 'langs', axis: 'typescript', summary: {
      en: 'Cross-file TS/JS repetition after normalization; a window of 30 lines and 90 tokens.',
      ru: 'Межфайловые дубли TS/JS после нормализации; окно 30 строк и 90 токенов.',
    }, adr: ['0064-typescript-language-axis.md', '0045-duplication-required-by-default.md'] },
    { id: 'comments.go', group: 'go', axis: 'go', summary: {
      en: 'Go comment density: from 10 lines and above 8%. Doc comments of top-level declarations and //go: directives are not counted; generated code is excluded only by a declaration in the frame, vendor and testdata by Go rules.',
      ru: 'Плотность комментариев в Go: от 10 строк и больше 8%. Doc-комментарии top-level деклараций и директивы //go: не считаются; generated исключается по объявлению в рамке, vendor и testdata — по правилам Go.',
    }, adr: ['0055-go-language-axis.md', '0043-comment-density-across-languages.md'] },
    { id: 'types-per-file.csharp', group: 'csharp', axis: 'csharp', summary: {
      en: 'At most one top-level class or record per file, so the file name points at one concept.',
      ru: 'Не больше одного верхнеуровневого class или record в файле. Имя файла указывает на одно понятие.',
    }, adr: ['0018-csharp-applicability-and-one-type-per-file.md'] },
    { id: 'dependencies.csharp', group: 'csharp', axis: 'csharp', summary: {
      en: 'Finds Proven cycles between modules and shows the source lines that close the ring.',
      ru: 'Находит доказанные циклы между модулями и показывает строки, замыкающие кольцо.',
    }, adr: ['0021-coupling-evidence-grades.md', '0029-dependency-counts-removed.md'] },
    { id: 'duplication.csharp', group: 'csharp', axis: 'csharp', summary: {
      en: 'Finds repeated blocks across files, even with different names and literals. The default window is 30 normalized lines and at least 90 tokens.',
      ru: 'Находит повторяющиеся блоки в разных файлах, даже с другими именами и литералами. Дефолтное окно — 30 нормализованных строк и минимум 90 токенов.',
    }, adr: ['0045-duplication-required-by-default.md', '0007-one-finding-one-report.md'] },
    { id: 'functions.csharp', group: 'csharp', axis: 'csharp', summary: {
      en: 'Methods, local functions, lambdas and top-level code: at most 80 own non-empty lines without comments (settings.functions.csharp.ownLines); nested units and data literals are not counted.',
      ru: 'Методы, локальные функции, лямбды и top-level код: не более 80 собственных непустых строк без комментариев (settings.functions.csharp.ownLines); вложенные единицы и литералы данных не входят.',
    }, adr: ['0063-function-own-lines.md'] },
    { id: 'duplication.go', group: 'go', axis: 'go', summary: {
      en: 'Repeated blocks across Go files after normalization: names become n, literals one token, keywords and predeclared identifiers stay. A window of 30 lines and 90 tokens.',
      ru: 'Повторяющиеся блоки в разных Go-файлах после нормализации: имена → n, литералы → токен, ключевые слова и предопределённые идентификаторы остаются. Окно 30 строк и 90 токенов.',
    }, adr: ['0055-go-language-axis.md', '0007-one-finding-one-report.md'] },
    { id: 'functions.go', group: 'go', axis: 'go', summary: {
      en: 'Go functions and func literals: at most 80 own non-empty lines without comments (settings.functions.go.ownLines); nested functions and composite literals are not counted.',
      ru: 'Go-функции и func-литералы: не более 80 собственных непустых строк без комментариев (settings.functions.go.ownLines); вложенные функции и composite literals не входят.',
    }, adr: ['0063-function-own-lines.md'] },
    { id: 'complexity.go', group: 'go', axis: 'go', summary: {
      en: 'A DSM over the package graph through tracked go.mod: average reachable packages at most 8, largest cyclic group 0 (the compiler forbids cycles). main packages are the composition root.',
      ru: 'DSM по графу пакетов через tracked go.mod: средняя достижимость пакетов ≤ 8, циклическая группа — 0 (компилятор запрещает циклы). main-пакеты — composition root.',
    }, adr: ['0055-go-language-axis.md', '0052-dsm-ceiling-is-a-declared-setting.md'] },
    { id: 'lint-suppressions.go', group: 'go', axis: 'go', summary: {
      en: 'Every //nolint blocks, even with a reason, and so does a golangci-lint exclusion by path. A linter disabled for the whole repository needs an id and reason in settings.lint-suppressions.go.repositoryWide; the linter itself is not run.',
      ru: 'Любой //nolint блокируется даже с причиной, как и исключение по пути в конфиге golangci-lint. Выключение линтера на весь репозиторий требует id и причины в settings.lint-suppressions.go.repositoryWide; сам линтер не запускается.',
    }, adr: ['0055-go-language-axis.md', '0044-editorconfig-baseline-and-warning-suppressions.md', '0067-reviewed-weakening-in-the-frame.md'] },
    { id: 'lint-suppressions.ansible', group: 'ansible', axis: 'ansible', summary: {
      en: 'Every inline # noqa and exclude_paths block; skip_list and warn_list entries need an id and reason in settings.lint-suppressions.ansible.repositoryWide.',
      ru: 'Любой inline # noqa и exclude_paths блокируются; skip_list и warn_list требуют id и причины в settings.lint-suppressions.ansible.repositoryWide.',
    }, adr: ['0057-ansible-axis.md', '0067-reviewed-weakening-in-the-frame.md'] },
    { id: 'dependencies.ansible', group: 'ansible', axis: 'ansible', summary: {
      en: 'Role cycles through meta dependencies, include_role and import_role. Literal names are Proven; Jinja names do not enter the graph.',
      ru: 'Циклы ролей по meta dependencies, include_role и import_role. Литеральные имена доказаны; Jinja-имена не входят в граф.',
    }, adr: ['0057-ansible-axis.md', '0021-coupling-evidence-grades.md'] },
    { id: 'build-properties.dotnet', group: 'dotnet', axis: 'dotnet', summary: {
      en: 'Shared build settings in Directory.Build.props: nullable, analyzers, warnings as errors and reproducibility.',
      ru: 'Единые настройки сборки: nullable, анализаторы, warnings как errors и воспроизводимость в Directory.Build.props.',
    }, adr: ['0019-dotnet-repository-policy.md'] },
    { id: 'central-packages.dotnet', group: 'dotnet', axis: 'dotnet', summary: {
      en: 'Package versions live in Directory.Packages.props, with no local overrides in projects.',
      ru: 'Версии пакетов в Directory.Packages.props, без локальных переопределений в проектах.',
    }, adr: ['0019-dotnet-repository-policy.md'] },
    { id: 'solution-format.dotnet', group: 'dotnet', axis: 'dotnet', summary: {
      en: 'The .slnx solution format; every SDK-style project is included in a solution.',
      ru: 'Формат решения .slnx; каждый SDK-style проект включён в решение.',
    }, adr: ['0019-dotnet-repository-policy.md'] },
    { id: 'editorconfig.dotnet', group: 'dotnet', axis: 'dotnet', summary: {
      en: 'One reference code style in the .editorconfig above every project. harness explain editorconfig.dotnet prints it in full.',
      ru: 'Единый эталон стиля в .editorconfig над каждым проектом. harness explain editorconfig.dotnet показывает его целиком.',
    }, adr: ['0044-editorconfig-baseline-and-warning-suppressions.md', '0067-reviewed-weakening-in-the-frame.md'] },
    { id: 'warning-suppressions.dotnet', group: 'dotnet', axis: 'dotnet', summary: {
      en: 'Forbids silencing warnings for a single file or location. A repository-wide switch needs an id and reason in settings.warning-suppressions.dotnet.repositoryWide and is printed in the ordinary report.',
      ru: 'Запрещает подавлять предупреждения для отдельного файла или места. Глобальное отключение требует id и причины в settings.warning-suppressions.dotnet.repositoryWide и печатается в обычном отчёте.',
    }, adr: ['0044-editorconfig-baseline-and-warning-suppressions.md', '0067-reviewed-weakening-in-the-frame.md'] },
    { id: 'frame.tests.unit', group: 'frame', summary: {
      en: 'Where the unit tests are, or why there are none. The address is the test project, not individual test files.',
      ru: 'Где unit-тесты или почему их нет. Адрес — тестовый проект, а не отдельные тестовые файлы.',
    }, adr: ['0049-test-suite-address-is-the-project.md'] },
    { id: 'frame.tests.integration', group: 'frame', summary: {
      en: 'Where the integration tests are, or why there are none. The address is the test project.',
      ru: 'Где интеграционные тесты или почему их нет. Адрес — тестовый проект.',
    }, adr: ['0049-test-suite-address-is-the-project.md'] },
    { id: 'frame.tests.architecture', group: 'frame', summary: {
      en: 'Where the architecture rule tests are, or why there are none.',
      ru: 'Где тесты архитектурных правил или почему их нет.',
    } },
    { id: 'frame.format', group: 'frame', summary: {
      en: 'What checks the source format.',
      ru: 'Чем проверяется формат кода.',
    } },
    { id: 'frame.lint', group: 'frame', summary: {
      en: 'Where static analysis is configured.',
      ru: 'Где настроен статический анализ.',
    } },
    { id: 'frame.build', group: 'frame', summary: {
      en: 'How to run the build.',
      ru: 'Как запустить сборку.',
    } },
    { id: 'frame.typecheck', group: 'frame', summary: {
      en: 'What checks types, or why a separate check does not apply.',
      ru: 'Чем проверяются типы или почему отдельная проверка неприменима.',
    } },
    { id: 'frame.verify', group: 'frame', summary: {
      en: 'Where the single verify script is that runs the project\'s checks, including harness check. A path is required here.',
      ru: 'Где единый verify-скрипт, который запускает проверки проекта, включая harness check. Здесь обязателен путь.',
    }, adr: ['0046-unified-verification-entry-point.md'] },
  ];

  const GROUPS = [
    { key: 'common', target: 'common-checks' },
    { key: 'frame', target: 'frame-checks' },
    { key: 'csharp', target: 'csharp-checks' },
    { key: 'dotnet', target: 'dotnet-checks' },
    { key: 'arch', target: 'architecture-checks' },
    { key: 'langs', target: 'language-checks' },
    { key: 'ansible', target: 'ansible-checks' },
    { key: 'go', target: 'go-checks' },
  ];

  const language = document.documentElement.lang === 'ru' ? 'ru' : 'en';

  for (const group of GROUPS) {
    const list = document.querySelector('#' + group.target);
    for (const check of CHECKS.filter((item) => item.group === group.key)) {
      const item = document.createElement('li');
      const id = document.createElement('code');
      id.textContent = check.id;
      const summary = document.createElement('p');
      summary.textContent = check.summary[language];
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
