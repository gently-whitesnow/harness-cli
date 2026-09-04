/* Harness CLI landing — interactive parts. No build step, no dependencies. */
(function () {
  'use strict';

  const $ = (selector, root) => (root || document).querySelector(selector);
  const $$ = (selector, root) => Array.from((root || document).querySelectorAll(selector));
  const ADR = (n) => `https://github.com/gently-whitesnow/harness-cli/blob/master/adrs/${n}`;
  const SVG_NS = 'http://www.w3.org/2000/svg';
  const svgEl = (name, attrs, parent) => {
    const el = document.createElementNS(SVG_NS, name);
    for (const key in attrs) el.setAttribute(key, attrs[key]);
    if (parent) parent.appendChild(el);
    return el;
  };
  const el = (name, attrs, children) => {
    const node = document.createElement(name);
    for (const key in attrs || {}) {
      if (key === 'class') node.className = attrs[key];
      else if (key === 'text') node.textContent = attrs[key];
      else if (key === 'html') node.innerHTML = attrs[key];
      else if (key.startsWith('on')) node.addEventListener(key.slice(2), attrs[key]);
      else node.setAttribute(key, attrs[key]);
    }
    for (const child of children || []) if (child) node.appendChild(typeof child === 'string' ? document.createTextNode(child) : child);
    return node;
  };
  const escapeHtml = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

  /* ── Theme & clipboard ─────────────────────────────────────────────── */
  $('#theme-toggle').addEventListener('click', () => {
    const next = document.documentElement.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
    document.documentElement.setAttribute('data-theme', next);
    try { localStorage.setItem('harness-site-theme', next); } catch (e) { /* private mode */ }
  });

  function copyText(text, button, label) {
    const done = () => {
      button.classList.add('is-copied');
      if (label) { button.dataset.original = button.dataset.original || button.textContent; button.textContent = label; }
      window.setTimeout(() => {
        button.classList.remove('is-copied');
        if (label) button.textContent = button.dataset.original;
      }, 1800);
    };
    if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(text).then(done, () => {});
  }
  $$('[data-copy-target]').forEach((button) => {
    button.addEventListener('click', () => copyText($('#' + button.dataset.copyTarget).textContent, button));
  });

  /* ── Check catalogue (execution order of CheckRegistry, contract 2.12.0) ── */
  const CHECKS = [
    { id: 'harness.config', group: 'common', axis: null, summary: 'Tracked .harness.json, который читает весь прогон: version, architecture, answers, applicability, settings, policy. Без него харнес ничего не доказал — Incomplete, код 2.', adr: ['0014-frame-answers-are-self-reported.md', '0016-versioned-frame-and-explicit-initialization.md'] },
    { id: 'architecture.sliced-dotnet', group: 'arch', axis: null, section: 'architecture', summary: 'Зоны, канонические слои и слайсы стандарта sliced-dotnet/1: DAG слоёв, изоляция слайсов, публичный API через Contracts/, зеркала, слой = сборка. Standalone-библиотека отвечает applicable: false.', adr: ['0033-canonical-standard-over-declarations.md', '0041-layer-is-the-assembly.md'] },
    { id: 'complexity.csharp', group: 'arch', axis: 'csharp', budget: true, summary: 'DSM по продукту: внутри архитектурных зон, а без зоны — вне tracked тестовых проектов. Mean reach и core size против tracked .harness.budget.json; превышение потолка блокирует, harness budget update только ужимает.', adr: ['0032-topology-over-thresholds.md', '0042-dsm-over-the-product-in-files.md', '0048-dsm-product-boundary-without-a-zone.md'] },
    { id: 'docs.policy', group: 'common', axis: null, summary: 'Один корневой AGENTS.md ≤ 150 строк, CLAUDE.md — прямой относительный симлинк на него, README.md ≤ 150 строк, adrs/**.md и SKILL.md разрешены, прочий tracked Markdown — нарушение.', adr: ['0010-documentation-policy.md', '0025-nested-agent-documents.md'] },
    { id: 'commits.setup', group: 'common', axis: null, settings: 'commits', summary: 'Clone-local commit-шаблон и commit-msg hook активированы (harness setup). Применима только при settings.commits.requireSetup: true; conventional header + структурированное тело на выбранном языке.', adr: ['0020-commit-message-contract-and-clone-setup.md'] },
    { id: 'comments.csharp', group: 'csharp', axis: 'csharp', settings: 'comments.csharp', summary: 'Плотность комментариев в C#: файл нарушает правило, если достиг minimumCommentLines и комментарии превышают percentageLimit авторских строк. Лексический счёт, не оценка пользы прозы.', adr: ['0028-recalibrated-csharp-defaults.md', '0043-comment-density-across-languages.md'] },
    { id: 'comments.yaml', group: 'langs', axis: 'yaml', settings: 'comments.yaml', summary: 'Та же плотность комментариев для tracked .yml/.yaml: # в начале строки или после пробела считается, внутри кавычек и блочных скаляров — содержимое.', adr: ['0043-comment-density-across-languages.md'] },
    { id: 'comments.typescript', group: 'langs', axis: 'typescript', settings: 'comments.typescript', summary: 'Та же плотность для .ts/.tsx/.js и родственных; .d.ts, .min.js и файлы с маркером @generated исключены. JSDoc считается комментарием.', adr: ['0043-comment-density-across-languages.md'] },
    { id: 'types-per-file.csharp', group: 'csharp', axis: 'csharp', summary: 'Не больше одного верхнеуровневого class или record в authored-файле, чтобы имя файла, история и навигация указывали на одно понятие. Интерфейсы, структуры, enum и вложенные типы не считаются.', adr: ['0018-csharp-applicability-and-one-type-per-file.md'] },
    { id: 'dependencies.csharp', group: 'csharp', axis: 'csharp', summary: 'Доказанные циклы модулей по Proven-рёбрам: набор namespace, достижимых друг из друга. Отчёт называет кратчайшее кольцо и строки, которые его замыкают. Fan-in/fan-out не считаются.', adr: ['0021-coupling-evidence-grades.md', '0029-dependency-counts-removed.md'] },
    { id: 'duplication.csharp', group: 'csharp', axis: 'csharp', settings: 'duplication.csharp', summary: 'Межфайловые повторы после нормализации: окно windowLines строк с minimumTokens токенами, регион растёт и репортится один раз. Повтор внутри одного файла не виден. С 2.9.0 init пишет required.', adr: ['0045-duplication-required-by-default.md', '0007-one-finding-one-report.md'] },
    { id: 'build-properties.dotnet', group: 'dotnet', axis: 'dotnet', summary: 'Hardened Directory.Build.props над каждым SDK-style проектом: nullable, анализаторы, warnings как errors, воспроизводимость; проект не ослабляет baseline и не повторяет TargetFramework.', adr: ['0019-dotnet-repository-policy.md'] },
    { id: 'central-packages.dotnet', group: 'dotnet', axis: 'dotnet', summary: 'Central Package Management: версии только в ближайшем Directory.Packages.props, без локальных Version/VersionOverride и конфликтующих центральных версий.', adr: ['0019-dotnet-repository-policy.md'] },
    { id: 'solution-format.dotnet', group: 'dotnet', axis: 'dotnet', summary: '.slnx вместо legacy .sln; каждый tracked SDK-style проект присутствует хотя бы в одном .slnx, сам файл — валидный XML.', adr: ['0019-dotnet-repository-policy.md'] },
    { id: 'editorconfig.dotnet', group: 'dotnet', axis: 'dotnet', summary: 'Эталонный code-style baseline в tracked-цепочке .editorconfig над каждым проектом: LF, финальный перевод строки, 4 пробела, IDE0055/0065/0161/0011/0040/0007 как warning, Allman. explain печатает эталон, init записывает его.', adr: ['0044-editorconfig-baseline-and-warning-suppressions.md'] },
    { id: 'warning-suppressions.dotnet', group: 'dotnet', axis: 'dotnet', summary: 'Адресное подавление диагностик блокируется: #pragma warning disable, SuppressMessage, NoWarn в .csproj, severity none в path-секции. Выключение правила для всего репозитория разрешено и печатается observation на каждом прогоне.', adr: ['0044-editorconfig-baseline-and-warning-suppressions.md'] },
    { id: 'frame.tests.unit', group: 'frame', key: 'tests.unit', summary: 'Ответ репозитория про unit-тесты: где они, или почему их нет. Адрес — тестовый проект (каталог или project file), не файлы внутри него.', hint: 'tests/Unit', testProjects: true, adr: ['0049-test-suite-address-is-the-project.md'] },
    { id: 'frame.tests.integration', group: 'frame', key: 'tests.integration', summary: 'Ответ про интеграционные тесты; границу между видами тестов определяет сам репозиторий. Адрес — тестовый проект, не его файлы.', hint: 'tests/Integration', testProjects: true, adr: ['0049-test-suite-address-is-the-project.md'] },
    { id: 'frame.tests.architecture', group: 'frame', key: 'tests.architecture', summary: 'Ответ про исполняемые утверждения о собственной структуре: допустимые направления слоёв и прочие продуктовые правила.', hint: 'tests/Architecture' },
    { id: 'frame.format', group: 'frame', key: 'format', summary: 'Ответ про механически проверяемый формат исходников; форматтер харнес не запускает.', hint: '.editorconfig' },
    { id: 'frame.lint', group: 'frame', key: 'lint', summary: 'Ответ про статический анализ сверх форматирования; адрес опционален — правила могут жить в csproj.', hint: '.globalconfig' },
    { id: 'frame.build', group: 'frame', key: 'build', summary: 'Ответ про точку входа сборки, которой должен воспользоваться читатель.', hint: 'Repository.slnx' },
    { id: 'frame.typecheck', group: 'frame', key: 'typecheck', summary: 'Ответ про проверку типов, идущую впереди кода; compiler-only репозиторий отвечает applicable: false с причиной.', hint: 'tsconfig.json' },
    { id: 'frame.verify', group: 'frame', key: 'verify', requiresPaths: true, summary: 'Единый repository-owned скрипт всех применимых проверок, включая harness check. Применим к любому репозиторию, полный ответ — только paths; харнес его не исполняет.', hint: 'verify.sh', adr: ['0046-unified-verification-entry-point.md'] },
  ];
  const GROUPS = [
    { key: 'common', title: 'Общие', note: 'Для любого репозитория, независимо от языка. Оси применимости на них не влияют.' },
    { key: 'frame', title: 'Рамка: self-reported вопросы', note: 'Одна и та же анкета для всех. Формы ответа: { "paths": [...] } (не более 5 адресов; для тестов — проект, не файлы) · { "present": true|false, "reason": "..." } · { "applicable": false, "reason": "..." }. present: false — readiness gap, а не провал; harness init оставляет ответы пустыми и ставит off, кроме verify.' },
    { key: 'arch', title: 'Архитектура и DSM', note: 'Первый и второй ярусы контракта 2.0. Секция architecture выбирает стандарт sliced-dotnet/1 либо applicable: false; DSM требует tracked .harness.budget.json.' },
    { key: 'csharp', title: 'C# · ось csharp', note: 'Все читают tracked .cs лексическим ридером; generated-локации, *.g.cs и файлы с маркером <auto-generated> исключены. Одна запись applicability.csharp: { "applicable": false, "reason": "…" } делает их NotApplicable.' },
    { key: 'dotnet', title: '.NET · ось dotnet', note: 'Читают tracked XML проектов и props-файлы без MSBuild evaluation. Репозиторий без SDK-style проектов — NotApplicable, никогда не pass.' },
    { key: 'langs', title: 'Другие языки · оси yaml и typescript', note: 'Языковая ось ADR-0022: второй язык — экземпляр Language, ридер и строка в реестре, а не копия проверки.' },
  ];
  const AXES = [
    { key: 'csharp', name: 'C#', reason: 'в репозитории нет C#-исходников' },
    { key: 'dotnet', name: '.NET', reason: 'нет SDK-style проектов .NET' },
    { key: 'yaml', name: 'YAML', reason: 'tracked YAML отсутствует' },
    { key: 'typescript', name: 'TypeScript', reason: 'нет web-стека и TypeScript/JavaScript' },
  ];

  function renderChecks() {
    const root = $('#check-groups');
    for (const group of GROUPS) {
      const checks = CHECKS.filter((c) => c.group === group.key);
      const list = el('ul', { class: 'check-list' });
      for (const check of checks) {
        const meta = [];
        if (check.axis) meta.push(el('span', { text: `ось ${check.axis}` }));
        if (check.section) meta.push(el('span', { text: 'секция architecture' }));
        if (check.key) meta.push(el('span', { text: `answers.${check.key}` }));
        if (check.settings) meta.push(el('span', { text: `settings.${check.settings}` }));
        if (check.budget) meta.push(el('span', { text: '.harness.budget.json' }));
        for (const a of check.adr || []) meta.push(el('a', { href: ADR(a), target: '_blank', rel: 'noreferrer noopener', text: 'ADR-' + a.slice(0, 4) }));
        list.appendChild(el('li', { class: 'check' }, [
          el('code', { class: 'check__id', text: check.id }),
          el('span', { class: 'badge badge--neutral badge--mono', text: check.group === 'frame' ? 'default off' : 'default required' }),
          el('p', { class: 'check__summary', text: check.summary }),
          el('span', { class: 'check__meta' }, meta),
        ]));
      }
      const verifyFix = group.key === 'frame' ? ' Только frame.verify стартует required.' : '';
      root.appendChild(el('div', { class: 'check-group' }, [
        el('div', { class: 'check-group__head' }, [el('h3', { text: group.title }), el('span', { class: 'badge badge--neutral', text: `${checks.length} ${plural(checks.length, 'проверка', 'проверки', 'проверок')}` })]),
        el('p', { class: 'check-group__note', text: group.note + verifyFix }),
        list,
      ]));
    }
  }
  function plural(n, one, few, many) {
    const m10 = n % 10, m100 = n % 100;
    if (m10 === 1 && m100 !== 11) return one;
    if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
    return many;
  }

  /* ── Graph algorithms (mirror Domain/Harness/Structure) ─────────────── */
  function tarjan(n, adjacency) {
    const index = new Array(n).fill(0), low = new Array(n).fill(0), onStack = new Array(n).fill(false);
    const stack = [], components = [];
    let next = 1;
    function explore(v) {
      index[v] = low[v] = next++;
      stack.push(v); onStack[v] = true;
      for (const w of adjacency[v]) {
        if (index[w] === 0) { explore(w); low[v] = Math.min(low[v], low[w]); }
        else if (onStack[w]) low[v] = Math.min(low[v], index[w]);
      }
      if (low[v] === index[v]) {
        const component = [];
        let w;
        do { w = stack.pop(); onStack[w] = false; component.push(w); } while (w !== v);
        components.push(component);
      }
    }
    for (let v = 0; v < n; v++) if (index[v] === 0) explore(v);
    return components;
  }
  function shortestCycle(adjacency, nodes) {
    const members = new Set(nodes);
    let shortest = null;
    for (const start of nodes) {
      const parents = new Map([[start, start]]);
      const queue = [start];
      while (queue.length) {
        const node = queue.shift();
        for (const next of adjacency[node]) if (members.has(next) && !parents.has(next)) { parents.set(next, node); queue.push(next); }
      }
      for (const node of nodes) {
        if (!parents.has(node) || !adjacency[node].includes(start)) continue;
        const path = [];
        let cursor = node;
        while (true) { path.push(cursor); if (parents.get(cursor) === cursor) break; cursor = parents.get(cursor); }
        path.reverse();
        if (!shortest || path.length < shortest.length) shortest = path;
      }
    }
    return shortest || nodes.slice();
  }
  function reachability(n, adjacency) {
    const reach = [];
    for (let i = 0; i < n; i++) {
      const seen = new Set([i]);
      const stack = [i];
      while (stack.length) { const v = stack.pop(); for (const w of adjacency[v]) if (!seen.has(w)) { seen.add(w); stack.push(w); } }
      reach.push(seen);
    }
    return reach;
  }

  /* ── Diagram helpers ────────────────────────────────────────────────── */
  function edgePath(a, b, w, h, bend) {
    // From rect centre a to rect centre b, trimmed to the rectangle borders, bent sideways.
    const dx = b.x - a.x, dy = b.y - a.y, len = Math.hypot(dx, dy) || 1;
    const ux = dx / len, uy = dy / len, nx = -uy, ny = ux;
    const trim = (p, sx, sy) => {
      const tx = Math.abs(sx) < 1e-6 ? Infinity : (w / 2) / Math.abs(sx);
      const ty = Math.abs(sy) < 1e-6 ? Infinity : (h / 2) / Math.abs(sy);
      const t = Math.min(tx, ty) + 3;
      return { x: p.x + sx * t, y: p.y + sy * t };
    };
    const mx = (a.x + b.x) / 2 + nx * bend, my = (a.y + b.y) / 2 + ny * bend;
    const s = trim(a, (mx - a.x) / Math.hypot(mx - a.x, my - a.y), (my - a.y) / Math.hypot(mx - a.x, my - a.y));
    const e = trim(b, (mx - b.x) / Math.hypot(mx - b.x, my - b.y), (my - b.y) / Math.hypot(mx - b.x, my - b.y));
    return `M${s.x.toFixed(1)} ${s.y.toFixed(1)} Q${mx.toFixed(1)} ${my.toFixed(1)} ${e.x.toFixed(1)} ${e.y.toFixed(1)}`;
  }

  /* ── Cycles demo ────────────────────────────────────────────────────── */
  const CYCLE_PRESETS = {
    shop: {
      prefix: 'Shop.',
      nodes: [
        { name: 'Api', x: 80, y: 50 }, { name: 'Orders', x: 260, y: 60 }, { name: 'Billing', x: 430, y: 110 },
        { name: 'Catalog', x: 110, y: 170 }, { name: 'Notifications', x: 330, y: 220 }, { name: 'Domain', x: 120, y: 265 },
      ],
      edges: [['Api', 'Orders'], ['Api', 'Billing'], ['Orders', 'Catalog'], ['Orders', 'Billing'], ['Billing', 'Orders'], ['Billing', 'Notifications'], ['Notifications', 'Orders'], ['Catalog', 'Domain'], ['Orders', 'Domain'], ['Notifications', 'Domain']],
    },
    adr: {
      prefix: 'Harness.',
      nodes: [
        { name: 'Report', x: 80, y: 60 }, { name: 'Checks', x: 260, y: 80 }, { name: 'Config', x: 430, y: 150 },
        { name: 'Git', x: 270, y: 240 }, { name: 'Structure', x: 90, y: 200 },
      ],
      edges: [['Report', 'Checks'], ['Checks', 'Config'], ['Config', 'Checks'], ['Config', 'Git'], ['Git', 'Config'], ['Checks', 'Structure']],
    },
  };
  const cycles = { svg: $('#cycles-svg'), preset: null, edges: [] };
  function loadCycles(key) {
    cycles.preset = CYCLE_PRESETS[key];
    cycles.edges = cycles.preset.edges.map(([from, to]) => ({ from, to, flipped: false }));
    renderCycles();
  }
  function renderCycles() {
    const { svg, preset } = cycles;
    const W = 96, H = 30;
    svg.innerHTML = '';
    const idx = new Map(preset.nodes.map((n, i) => [n.name, i]));
    const adjacency = preset.nodes.map(() => []);
    const representative = new Map();
    for (const edge of cycles.edges) {
      const from = idx.get(edge.from), to = idx.get(edge.to);
      if (!adjacency[from].includes(to)) adjacency[from].push(to);
      representative.set(from + '>' + to, edge);
    }
    for (const list of adjacency) list.sort((a, b) => preset.nodes[a].name.localeCompare(preset.nodes[b].name));
    const components = tarjan(preset.nodes.length, adjacency).filter((c) => c.length > 1);
    components.sort((a, b) => Math.min(...a) - Math.min(...b));
    const rings = components.map((component) => shortestCycle(adjacency, component.slice().sort((a, b) => preset.nodes[a].name.localeCompare(preset.nodes[b].name))));
    const ringEdges = new Set();
    for (const ring of rings) for (let i = 0; i < ring.length; i++) ringEdges.add(ring[i] + '>' + ring[(i + 1) % ring.length]);
    const inCore = new Set(components.flat());

    const edgesLayer = svgEl('g', {}, svg);
    for (const edge of cycles.edges) {
      const a = preset.nodes[idx.get(edge.from)], b = preset.nodes[idx.get(edge.to)];
      const reverse = cycles.edges.some((o) => o.from === edge.to && o.to === edge.from);
      const d = edgePath(a, b, W, H, reverse ? 16 : 6);
      const key = idx.get(edge.from) + '>' + idx.get(edge.to);
      const path = svgEl('path', { d, class: 'edge' + (ringEdges.has(key) ? ' in-ring' : '') + (edge.flipped ? ' is-flipped' : '') }, edgesLayer);
      const hit = svgEl('path', { d, class: 'edge-hit' }, edgesLayer);
      svgEl('title', {}, hit).textContent = `${preset.prefix}${edge.from} → ${preset.prefix}${edge.to} · клик развернёт`;
      hit.addEventListener('click', () => {
        const from = edge.to; edge.to = edge.from; edge.from = from; edge.flipped = !edge.flipped;
        renderCycles();
      });
      path.addEventListener('click', () => hit.dispatchEvent(new Event('click')));
    }
    for (const node of preset.nodes) {
      const i = idx.get(node.name);
      const inRing = rings.some((r) => r.includes(i));
      const g = svgEl('g', { class: 'node' + (inCore.has(i) ? ' in-core' : '') + (inRing ? ' in-ring' : '') }, svg);
      svgEl('rect', { x: node.x - W / 2, y: node.y - H / 2, width: W, height: H, rx: 6 }, g);
      svgEl('text', { x: node.x, y: node.y + 1 }, g).textContent = node.name;
    }

    $('#cycles-count').textContent = String(components.length);
    const verdict = $('#cycles-verdict');
    verdict.textContent = components.length ? 'failed · exit 1' : 'passed · exit 0';
    verdict.className = 'stat__value ' + (components.length ? 'is-error' : 'is-ok');
    const finding = $('#cycles-finding');
    if (!components.length) {
      $('#cycles-ring').textContent = '—';
      finding.className = 'finding is-ok';
      finding.textContent = `✅ dependencies.csharp  ${cycles.edges.length} proven module edges, 0 cycles. 100% of the names that match a declared type resolved to exactly one of them.`;
      return;
    }
    const ring = rings[0].map((i) => preset.nodes[i].name);
    $('#cycles-ring').textContent = ring.length + ' модул' + plural(ring.length, 'ь', 'я', 'ей');
    const closed = [...ring, ring[0]].map((n) => preset.prefix + n).join(' -> ');
    const evidence = rings[0].map((i, step) => {
      const j = rings[0][(step + 1) % rings[0].length];
      const from = preset.nodes[i].name, to = preset.nodes[j].name;
      return `${preset.prefix}${from}.${from}Service names ${preset.prefix}${to}.${to}Model at src/${from}/${from}Service.cs:${12 + step * 7}`;
    });
    const wider = components[0].length > rings[0].length
      ? ` It is the shortest ring inside a group of ${components[0].length} modules that all reach each other, so more will surface once it is broken.`
      : '';
    const more = components.length > 1 ? `\n… and ${components.length - 1} more module dependency ${plural(components.length - 1, 'cycle', 'cycles', 'cycles')}` : '';
    finding.className = 'finding';
    finding.textContent = `❌ dependencies.csharp  src/${ring[0]}/${ring[0]}Service.cs:12\n   module dependency cycle ${closed}: ${evidence.join('; ')}. These modules cannot be read, moved or reused in one direction until one of these references is turned around or the concept both need is moved out of both.${wider}${more}`;
  }
  $('#cycles-reset').addEventListener('click', () => loadCycles('shop'));
  $('#cycles-preset-adr').addEventListener('click', () => loadCycles('adr'));

  /* ── DSM demo ───────────────────────────────────────────────────────── */
  const DSM_BASE = {
    nodes: [
      { name: 'Host/Program.cs', x: 260, y: 30, side: 'top' },
      { name: 'Api/OrdersEndpoint.cs', x: 150, y: 95, side: 'left' },
      { name: 'Infrastructure/SqlOrderStore.cs', x: 370, y: 95, side: 'right' },
      { name: 'Application/PlaceOrder.cs', x: 150, y: 170, side: 'left' },
      { name: 'Application/Contracts/IOrderStore.cs', x: 370, y: 170, side: 'right' },
      { name: 'Domain/Order.cs', x: 260, y: 225, side: 'right' },
      { name: 'Domain/Money.cs', x: 150, y: 270, side: 'left' },
      { name: 'Domain/Shared/Clock.cs', x: 370, y: 270, side: 'right' },
    ],
    edges: [[0, 1], [0, 2], [0, 3], [1, 3], [3, 4], [3, 5], [3, 7], [4, 5], [5, 6], [2, 4], [2, 5]],
  };
  const dsm = { svg: $('#dsm-svg'), nodes: [], edges: [], leaves: 0, cycle: false, selected: null, budget: null };
  function resetDsm() {
    dsm.nodes = DSM_BASE.nodes.map((n) => ({ ...n }));
    dsm.edges = DSM_BASE.edges.map((e) => e.slice());
    dsm.leaves = 0; dsm.cycle = false; dsm.selected = null;
    dsm.budget = measureDsm();
    renderDsm();
  }
  function measureDsm() {
    const n = dsm.nodes.length;
    const adjacency = dsm.nodes.map(() => []);
    for (const [a, b] of dsm.edges) if (!adjacency[a].includes(b)) adjacency[a].push(b);
    const reach = reachability(n, adjacency);
    const pairs = reach.reduce((sum, r) => sum + r.size, 0);
    const components = tarjan(n, adjacency);
    const core = components.filter((c) => c.length > 1).sort((a, b) => b.length - a.length)[0] || [];
    return { n, pairs, reach, meanReach: pairs / n, cost: (100 * pairs) / (n * n), core: core.length, coreNodes: new Set(core), adjacency };
  }
  function renderDsm() {
    const { svg } = dsm;
    const m = measureDsm();
    svg.innerHTML = '';
    const R = 9;
    const selectedReach = dsm.selected === null ? null : m.reach[dsm.selected];
    const edgesLayer = svgEl('g', {}, svg);
    for (const [a, b] of dsm.edges) {
      const A = dsm.nodes[a], B = dsm.nodes[b];
      const dx = B.x - A.x, dy = B.y - A.y, len = Math.hypot(dx, dy);
      const sx = A.x + (dx / len) * (R + 2), sy = A.y + (dy / len) * (R + 2);
      const ex = B.x - (dx / len) * (R + 3), ey = B.y - (dy / len) * (R + 3);
      let cls = 'edge';
      if (selectedReach) cls += selectedReach.has(a) && selectedReach.has(b) ? ' is-highlight' : ' is-dim';
      if (m.coreNodes.has(a) && m.coreNodes.has(b)) cls = 'edge in-ring';
      svgEl('path', { d: `M${sx} ${sy} L${ex} ${ey}`, class: cls }, edgesLayer);
    }
    dsm.nodes.forEach((node, i) => {
      const cls = ['node'];
      if (m.coreNodes.has(i)) cls.push('in-core');
      if (dsm.selected === i) cls.push('is-selected');
      else if (selectedReach && selectedReach.has(i)) cls.push('is-reached');
      const g = svgEl('g', { class: cls.join(' '), style: 'cursor:pointer' }, svg);
      svgEl('circle', { cx: node.x, cy: node.y, r: R }, g);
      const slash = node.name.lastIndexOf('/');
      const file = node.name.slice(slash + 1), dir = node.name.slice(0, slash + 1);
      const side = node.side || 'top';
      const anchor = side === 'left' ? 'end' : side === 'right' ? 'start' : 'middle';
      const lx = side === 'left' ? node.x - R - 6 : side === 'right' ? node.x + R + 6 : node.x;
      const ly = side === 'top' ? node.y - R - 12 : node.y - 2;
      const label = svgEl('text', { x: lx, y: ly, class: 'label', 'text-anchor': anchor, style: 'font-family: var(--font-mono); fill: var(--color-text)' }, g);
      label.textContent = file;
      const sub = svgEl('text', { x: lx, y: side === 'top' ? ly - 12 : ly + 12, class: 'label', 'text-anchor': anchor, style: 'font-family: var(--font-mono); font-size: 9.5px' }, g);
      sub.textContent = dir;
      svgEl('title', {}, g).textContent = `${node.name} · достигает ${m.reach[i].size} файлов, включая себя`;
      g.addEventListener('click', () => { dsm.selected = dsm.selected === i ? null : i; renderDsm(); });
    });
    const fmt = (v) => v.toFixed(2);
    $('#dsm-n').textContent = String(m.n);
    $('#dsm-pairs').textContent = String(m.pairs);
    $('#dsm-reach').textContent = fmt(m.meanReach);
    $('#dsm-cost').textContent = fmt(m.cost) + ' %';
    $('#dsm-core').textContent = String(m.core);
    $('#dsm-budget-reach').textContent = fmt(dsm.budget.meanReach);
    $('#dsm-budget-core').textContent = String(dsm.budget.core);
    const reachEl = $('#dsm-reach'), coreEl = $('#dsm-core');
    reachEl.className = 'stat__value ' + (m.meanReach > dsm.budget.meanReach + 1e-9 ? 'is-error' : '');
    coreEl.className = 'stat__value ' + (m.core > dsm.budget.core ? 'is-error' : '');
    const finding = $('#dsm-finding');
    const regressed = [];
    if (m.meanReach > dsm.budget.meanReach + 1e-9) regressed.push(`mean reach +${fmt(m.meanReach - dsm.budget.meanReach)} files`);
    if (m.core > dsm.budget.core) regressed.push(`core size +${m.core - dsm.budget.core} files`);
    const selectedLine = dsm.selected === null ? '' : `\n   R(${dsm.nodes[dsm.selected].name}) = ${m.reach[dsm.selected].size} files: ${[...m.reach[dsm.selected]].map((i) => dsm.nodes[i].name.split('/').pop()).join(', ')}`;
    if (regressed.length) {
      finding.className = 'finding';
      const coreLines = m.core > dsm.budget.core ? '\n' + [...m.coreNodes].map((i) => `   ${dsm.nodes[i].name}  This file belongs to the largest SCC (${m.core} files).`).join('\n') : '';
      finding.textContent = `❌ complexity.csharp  .harness.budget.json\n   DSM budget regressed (${regressed.join(', ')}); reduce the graph or review the tracked budget manually.${coreLines}${selectedLine}`;
    } else if (dsm.budget.meanReach - m.meanReach >= 0.1 || m.core < dsm.budget.core) {
      const progress = [];
      if (dsm.budget.meanReach - m.meanReach >= 0.1) progress.push(`mean reach -${fmt(dsm.budget.meanReach - m.meanReach)} files`);
      if (m.core < dsm.budget.core) progress.push(`core size -${dsm.budget.core - m.core} files`);
      finding.className = 'finding is-warn';
      finding.textContent = `⚠️ complexity.csharp  DSM complexity improved (${progress.join(', ')}); run \`harness budget update\` to record the progress.${selectedLine}`;
    } else {
      finding.className = 'finding is-ok';
      finding.textContent = `✅ complexity.csharp  mean reach: ${fmt(m.meanReach)} files (${m.pairs} reachable file pairs / ${m.n} files; propagation cost ${fmt(m.cost)}%) · core size: ${m.core} files · scope: ${m.n} files inside architecture zone [src]${selectedLine}`;
    }
  }
  $('#dsm-add-leaf').addEventListener('click', () => {
    if (dsm.leaves >= 6) return;
    dsm.leaves++;
    dsm.nodes.push({ name: `Domain/Leaf${dsm.leaves}.cs`, x: 30 + dsm.leaves * 78, y: 318, side: 'top' });
    renderDsm();
  });
  $('#dsm-close-cycle').addEventListener('click', () => {
    if (dsm.cycle) { dsm.edges = dsm.edges.filter(([a, b]) => !(a === 5 && b === 3)); dsm.cycle = false; $('#dsm-close-cycle').textContent = 'Замкнуть цикл'; }
    else { dsm.edges.push([5, 3]); dsm.cycle = true; $('#dsm-close-cycle').textContent = 'Разомкнуть цикл'; }
    renderDsm();
  });
  $('#dsm-reset').addEventListener('click', () => { $('#dsm-close-cycle').textContent = 'Замкнуть цикл'; resetDsm(); });

  /* ── Duplication demo ───────────────────────────────────────────────── */
  const CS_KEYWORDS = new Set(('abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while and async await file global init nameof not or record required var when where with yield').split(' '));
  const DUP_A = [
    'async Task<Order?> Find(Guid id)',
    '{',
    '  // lookup by identifier',
    '  var f = Filter.Eq(x => x.Id, id);',
    '  var c = await orders.Find(f);',
    '  var found = await c.First();',
    '  if (found is null)',
    '  {',
    '    log.Warn("order {Id}", id);',
    '    return null;',
    '  }',
    '  return found;',
    '}',
  ];
  const DUP_B = [
    'async Task<Invoice?> Get(Guid no)',
    '{',
    '  var q = Filter.Eq(i => i.No, no);',
    '',
    '  var cur = await docs.Find(q);',
    '  var inv = await cur.First();',
    '  if (inv is null)',
    '  {',
    '    logger.Warn("invoice {No}", no);',
    '    return null;',
    '  }',
    '  return inv with { Loaded = true };',
    '}',
  ];
  function normalizeLine(line) {
    let text = line.replace(/\/\/.*$/, '');
    const tokens = [];
    let i = 0;
    while (i < text.length) {
      const ch = text[i];
      if (/\s/.test(ch)) { i++; continue; }
      if (ch === '"') { let j = i + 1; while (j < text.length && text[j] !== '"') j++; tokens.push('"'); i = j + 1; continue; }
      if (ch === "'") { let j = i + 1; while (j < text.length && text[j] !== "'") j++; tokens.push("'"); i = j + 1; continue; }
      if (/[A-Za-z_@]/.test(ch)) { let j = i; while (j < text.length && /[A-Za-z0-9_@]/.test(text[j])) j++; const word = text.slice(i, j); tokens.push(CS_KEYWORDS.has(word) ? word : 'n'); i = j; continue; }
      if (/[0-9]/.test(ch)) { let j = i; while (j < text.length && /[A-Za-z0-9_]/.test(text[j])) j++; tokens.push('#'); i = j; continue; }
      tokens.push(ch); i++;
    }
    return tokens;
  }
  function normalizeFile(lines) {
    const out = [];
    lines.forEach((line, index) => { const tokens = normalizeLine(line); if (tokens.length) out.push({ line: index, text: tokens.join(' '), count: tokens.length }); });
    return out;
  }
  function findRepetition(a, b, window, minTokens) {
    const tokensIn = (file, start) => file.slice(start, start + window).reduce((s, l) => s + l.count, 0);
    const matches = (i, j) => { for (let k = 0; k < window; k++) if (a[i + k].text !== b[j + k].text) return false; return true; };
    for (let i = 0; i + window <= a.length; i++) {
      if (tokensIn(a, i) < minTokens) continue;
      for (let j = 0; j + window <= b.length; j++) {
        if (tokensIn(b, j) < minTokens || !matches(i, j)) continue;
        let start = 0;
        while (i + start - 1 >= 0 && j + start - 1 >= 0 && a[i + start - 1].text === b[j + start - 1].text) start--;
        let length = window - start;
        while (i + start + length < a.length && j + start + length < b.length && a[i + start + length].text === b[j + start + length].text) length++;
        return { a: a.slice(i + start, i + start + length), b: b.slice(j + start, j + start + length), length };
      }
    }
    return null;
  }
  function renderDup() {
    const window = Number($('#dup-window').value), minTokens = Number($('#dup-tokens').value);
    $('#dup-window-out').textContent = String(window);
    $('#dup-tokens-out').textContent = String(minTokens);
    const na = normalizeFile(DUP_A), nb = normalizeFile(DUP_B);
    const rep = findRepetition(na, nb, window, minTokens);
    const inA = new Set(rep ? rep.a.map((l) => l.line) : []), inB = new Set(rep ? rep.b.map((l) => l.line) : []);
    const renderRaw = (lines, marked) => lines.map((line, i) => {
      const text = escapeHtml(line) || ' ';
      return marked.has(i) ? `<mark class="m-dup">${text}</mark>` : text;
    }).join('\n');
    const renderNorm = (file, marked) => file.map((l) => {
      const row = `${escapeHtml(l.text)}  <span class="c-dim">·${l.count}</span>`;
      return marked.has(l.line) ? `<mark class="m-dup">${row}</mark>` : row;
    }).join('\n');
    $('#dup-raw-a').innerHTML = renderRaw(DUP_A, inA);
    $('#dup-raw-b').innerHTML = renderRaw(DUP_B, inB);
    $('#dup-norm-a').innerHTML = renderNorm(na, inA);
    $('#dup-norm-b').innerHTML = renderNorm(nb, inB);
    const finding = $('#dup-finding');
    if (rep) {
      const span = (lines) => `${lines[0].line + 1}-${lines[lines.length - 1].line + 1}`;
      finding.className = 'finding';
      finding.textContent = `❌ duplication.csharp  src/Orders/OrdersRepository.cs:${span(rep.a)}\n   a lexically repeated block of ${rep.length} normalized lines occurs 2 times: src/Invoices/InvoicesRepository.cs:${span(rep.b)}, src/Orders/OrdersRepository.cs:${span(rep.a)}`;
    } else {
      finding.className = 'finding is-ok';
      finding.textContent = `✅ duplication.csharp  no window of ${window} normalized lines with at least ${minTokens} tokens repeats across files`;
    }
  }
  $('#dup-window').addEventListener('input', renderDup);
  $('#dup-tokens').addEventListener('input', renderDup);

  /* ── Architecture diagram ───────────────────────────────────────────── */
  function renderArch() {
    const svg = $('#arch-svg');
    svg.innerHTML = '';
    const layer = (name, x, y, w, h, cls) => {
      const g = svgEl('g', { class: 'layer ' + (cls || '') }, svg);
      svgEl('rect', { x, y, width: w, height: h }, g);
      svgEl('text', { x: x + 10, y: y + 17 }, g).textContent = name;
      return g;
    };
    const slice = (name, x, y, w, cls) => {
      const g = svgEl('g', { class: 'slice ' + (cls || '') }, svg);
      svgEl('rect', { x, y, width: w, height: 26 }, g);
      svgEl('text', { x: x + 8, y: y + 17 }, g).textContent = name;
    };
    const arrow = (x1, y1, x2, y2, cls, label, lx, ly) => {
      svgEl('path', { d: `M${x1} ${y1} L${x2} ${y2}`, class: 'edge ' + (cls || ''), style: 'cursor: default' }, svg);
      if (label) { const t = svgEl('text', { x: lx, y: ly, class: 'label ' + (cls === 'forbidden' ? 'is-error' : '') }, svg); t.textContent = label; }
    };
    layer('Host', 20, 8, 480, 30, 'is-host');
    layer('Api', 20, 58, 140, 40, 'is-input');
    layer('Consumers', 180, 58, 140, 40, 'is-input');
    layer('Infrastructure', 380, 58, 120, 232);
    layer('Application', 20, 118, 300, 118);
    slice('Features/Orders', 32, 150, 132);
    slice('Features/Billing', 176, 150, 132);
    slice('Contracts/X/Orders', 176, 184, 132, 'is-x');
    svgEl('text', { x: 32, y: 226, class: 'label is-error' }, svg).textContent = 'Orders → Billing напрямую запрещено; только через X/Orders';
    layer('Domain', 20, 256, 300, 56, 'is-domain');
    slice('Orders', 32, 282, 80); slice('Billing', 120, 282, 80); slice('Shared', 208, 282, 80);
    // allowed
    arrow(90, 98, 90, 118);
    arrow(250, 98, 250, 118);
    arrow(170, 236, 170, 256);
    arrow(380, 150, 320, 192, '', 'только Contracts/', 326, 112);
    arrow(380, 232, 320, 290);
    arrow(60, 38, 60, 58, '', 'composition root видит всё', 100, 52);
    // forbidden
    arrow(300, 256, 300, 236, 'forbidden', 'Domain → Application запрещено', 180, 250);
    arrow(164, 163, 176, 163, 'forbidden');
  }

  /* ── Builder ────────────────────────────────────────────────────────── */
  const PROFILES = [
    { key: 'app', title: '.NET-приложение', desc: 'Сервис, CLI или воркер по стандарту sliced-dotnet/1. Все четыре оси применимы; typecheck не про нас.', kind: 'application', axes: { csharp: true, dotnet: true, yaml: true, typescript: false }, typecheck: 'нет web-стека; типы проверяет компилятор C# на сборке' },
    { key: 'lib', title: '.NET-библиотека', desc: 'Standalone-библиотека: ось слайсов вырождается, architecture отвечает applicable: false, DSM измеряется целиком.', kind: 'library', axes: { csharp: true, dotnet: true, yaml: true, typescript: false }, typecheck: 'нет web-стека; типы проверяет компилятор C# на сборке' },
    { key: 'front', title: 'Frontend / TypeScript', desc: 'Web-репозиторий без .NET: остаются общие проверки, рамка, comments.typescript и comments.yaml.', kind: 'library', axes: { csharp: false, dotnet: false, yaml: true, typescript: true }, archReason: 'не .NET-приложение: стандарт sliced-dotnet не применим' },
    { key: 'infra', title: 'Инфраструктура / YAML', desc: 'Ansible, Helm, CI-шаблоны: общие проверки, рамка и comments.yaml.', kind: 'library', axes: { csharp: false, dotnet: false, yaml: true, typescript: false }, archReason: 'репозиторий инфраструктуры без .NET-кода', typecheck: 'нет типизируемого кода' },
    { key: 'mono', title: 'Монорепозиторий', desc: '.NET-зона плюс web-приложение рядом: все оси применимы, архитектура по стандарту.', kind: 'application', axes: { csharp: true, dotnet: true, yaml: true, typescript: true } },
  ];
  const FRAME_FORMS = [
    { value: 'none', label: 'не отвечено' },
    { value: 'paths', label: 'paths' },
    { value: 'present', label: 'present: true' },
    { value: 'absent', label: 'present: false' },
    { value: 'na', label: 'applicable: false' },
  ];
  const state = {};
  function defaultState(profileKey) {
    const profile = PROFILES.find((p) => p.key === profileKey) || PROFILES[0];
    const s = {
      profile: profile.key,
      latest: false,
      kind: profile.kind,
      archReason: profile.archReason || 'standalone library',
      axes: {},
      answers: {},
      policy: {},
      settings: {
        comments: { csharp: { min: 10, pct: 8 }, yaml: { min: 10, pct: 8 }, typescript: { min: 10, pct: 8 } },
        duplication: { window: 30, tokens: 90 },
        commits: { language: 'ru', requireSetup: true },
      },
    };
    for (const axis of AXES) s.axes[axis.key] = { applicable: profile.axes[axis.key], reason: profile.axes[axis.key] ? '' : axis.reason };
    for (const check of CHECKS) if (check.key) s.answers[check.key] = { form: 'none', text: '' };
    if (profile.typecheck) s.answers.typecheck = { form: 'na', text: profile.typecheck };
    for (const check of CHECKS) s.policy[check.id] = check.id === 'frame.verify' ? 'required' : check.group === 'frame' ? 'off' : 'required';
    return s;
  }
  function setState(next) { Object.keys(state).forEach((k) => delete state[k]); Object.assign(state, next); }

  function buildConfig() {
    const answers = {};
    for (const check of CHECKS.filter((c) => c.key)) {
      const a = state.answers[check.key];
      if (a.form === 'none') answers[check.key] = {};
      else if (a.form === 'paths') answers[check.key] = { paths: a.text.split(',').map((s) => s.trim()).filter(Boolean) };
      else if (a.form === 'present') answers[check.key] = { present: true, reason: a.text };
      else if (a.form === 'absent') answers[check.key] = { present: false, reason: a.text };
      else answers[check.key] = { applicable: false, reason: a.text };
    }
    const applicability = {};
    for (const axis of AXES) applicability[axis.key] = state.axes[axis.key].applicable ? { applicable: true } : { applicable: false, reason: state.axes[axis.key].reason };
    const settings = {};
    for (const lang of ['csharp', 'yaml', 'typescript']) settings['comments.' + lang] = { minimumCommentLines: state.settings.comments[lang].min, percentageLimit: state.settings.comments[lang].pct };
    settings['duplication.csharp'] = { windowLines: state.settings.duplication.window, minimumTokens: state.settings.duplication.tokens };
    settings.commits = { language: state.settings.commits.language, requireSetup: state.settings.commits.requireSetup };
    const policy = {};
    for (const check of CHECKS) policy[check.id] = state.policy[check.id];
    return {
      version: state.latest ? 'latest' : '2.12.0',
      architecture: state.kind === 'application' ? { standard: 'sliced-dotnet/1' } : { applicable: false, reason: state.archReason },
      answers, applicability, settings, policy,
    };
  }
  function highlightJson(json) {
    return escapeHtml(json).replace(/("(?:[^"\\]|\\.)*")(\s*:)?|\b(true|false|null)\b|(-?\d+(?:\.\d+)?)/g, (m, str, colon, bool, num) => {
      if (str) return colon ? `<span class="j-key">${str}</span>${colon}` : `<span class="j-str">${str}</span>`;
      if (bool) return `<span class="j-bool">${bool}</span>`;
      return `<span class="j-num">${num}</span>`;
    });
  }
  function compactJson(config) {
    // Keep short objects on one line, the way the reference .harness.json is written.
    const lines = JSON.stringify(config, null, 2).split('\n');
    const out = [];
    for (let i = 0; i < lines.length; i++) {
      const line = lines[i];
      const open = line.match(/^(\s*)("[^"]+"): \{$/);
      if (open) {
        let j = i + 1, inner = [];
        while (j < lines.length && !/^\s*\},?$/.test(lines[j])) { inner.push(lines[j].trim()); j++; }
        const closing = lines[j] || '';
        const singleLine = `${open[1]}${open[2]}: { ${inner.join(' ')} }${closing.endsWith(',') ? ',' : ''}`;
        if (inner.every((l) => !l.endsWith('{') && !l.endsWith('[')) && singleLine.length <= 62 && inner.length <= 3) { out.push(inner.length ? singleLine : `${open[1]}${open[2]}: {}${closing.endsWith(',') ? ',' : ''}`); i = j; continue; }
      }
      const arr = line.match(/^(\s*)("[^"]+"): \[$/);
      if (arr) {
        let j = i + 1, inner = [];
        while (j < lines.length && !/^\s*\],?$/.test(lines[j])) { inner.push(lines[j].trim().replace(/,$/, '')); j++; }
        const closing = lines[j] || '';
        out.push(`${arr[1]}${arr[2]}: [${inner.join(', ')}]${closing.endsWith(',') ? ',' : ''}`); i = j; continue;
      }
      out.push(line);
    }
    return out.join('\n');
  }
  function evaluate(config) {
    const lines = [];
    const add = (level, text) => lines.push({ level, text });
    add('ok', `version "${config.version}" — контракт ${config.version === 'latest' ? 'следует за установленным бинарём' : '2.12.0, тот же, что исполняет бинарь'}.`);
    if (config.architecture.standard) add('ok', 'architecture: sliced-dotnet/1 — architecture.sliced-dotnet проверит зоны, слои и слайсы; complexity.csharp измерит только файлы внутри зон.');
    else if (!config.architecture.reason.trim()) add('error', 'architecture.reason должен объяснить, почему стандарт не применим — иначе Incomplete.');
    else add('ok', `architecture: applicable false — "${config.architecture.reason}". architecture.sliced-dotnet → NotApplicable; DSM измеряет репозиторий целиком.`);
    const naAxes = AXES.filter((a) => !config.applicability[a.key].applicable);
    for (const axis of naAxes) if (!config.applicability[axis.key].reason.trim()) add('error', `applicability.${axis.key}.reason must say why these checks do not apply.`);
    if (naAxes.length) {
      const affected = CHECKS.filter((c) => c.axis && naAxes.some((a) => a.key === c.axis)).map((c) => c.id);
      add('ok', `Оси ${naAxes.map((a) => a.key).join(', ')} не применимы: ${affected.length} ${plural(affected.length, 'проверка получает', 'проверки получают', 'проверок получают')} NotApplicable, но остаются в policy — ${affected.join(', ')}.`);
    }
    let answered = 0;
    for (const check of CHECKS.filter((c) => c.key)) {
      const answer = config.answers[check.key];
      const policy = config.policy[check.id];
      const missing = !Object.keys(answer).length;
      if (missing) { if (policy !== 'off') add('error', `answers.${check.key} не отвечен — frame.${check.key} даст Incomplete (exit 2). Исследуйте репозиторий и ответьте честно.`); continue; }
      answered++;
      if (answer.paths && !answer.paths.length) add('error', `answers.${check.key}.paths пуст — назовите хотя бы один адрес.`);
      if (answer.paths && answer.paths.length > 5) add('error', `answers.${check.key}.paths: ${answer.paths.length} адресов — навигация называет не более 5 точек входа; назовите проекты или каталоги, а не их содержимое.`);
      if (check.testProjects && answer.paths && answer.paths.some((p) => /\.(cs|ts|tsx|mts|cts|js|jsx|mjs|cjs)$/i.test(p))) add('error', `answers.${check.key}.paths называет тестовые файлы — адрес теста это проект, который их запускает; перечислите каждый проект один раз.`);
      if ('reason' in answer && !answer.reason.trim()) add('error', `answers.${check.key}.reason обязателен для этой формы ответа.`);
      if (check.requiresPaths && !answer.paths) add('error', `answers.verify принимает только paths: у каждого репозитория может быть единый verify-скрипт.`);
      if (answer.present === false && policy === 'required') add('warn', `answers.${check.key}: present false при required — readiness gap станет нарушением; advisory принимает пробел, не скрывая его.`);
    }
    if (config.policy['complexity.csharp'] !== 'off' && config.applicability.csharp.applicable) add('warn', 'complexity.csharp требует tracked .harness.budget.json: без него — Incomplete даже при advisory. Создайте его командой harness budget update и закоммитьте.');
    if (config.settings.commits.requireSetup) add('ok', 'commits.requireSetup: true — commits.setup сделает неподготовленный клон видимым; harness setup активирует hook и шаблон.');
    else add('ok', 'commits.requireSetup: false — commits.setup отвечает NotApplicable.');
    const advisory = CHECKS.filter((c) => config.policy[c.id] === 'advisory').length, off = CHECKS.filter((c) => config.policy[c.id] === 'off').length;
    add(off ? 'warn' : 'ok', `policy: ${CHECKS.length - advisory - off} required · ${advisory} advisory · ${off} off. Выключенная проверка видна в tracked-файле и в отчёте на каждом прогоне.`);
    return { lines, answered };
  }

  function renderBuilder() {
    const root = $('#builder-steps');
    root.innerHTML = '';
    const step = (num, title, desc, body) => el('section', { class: 'step' }, [
      el('div', { class: 'step__head' }, [el('span', { class: 'step__num', text: String(num) }), el('h3', { class: 'step__title', text: title })]),
      desc ? el('p', { class: 'step__desc', html: desc }) : null,
      body,
    ]);
    const seg = (options, value, onChange, disabled) => {
      const wrap = el('div', { class: 'seg', role: 'group' });
      for (const option of options) {
        wrap.appendChild(el('button', { type: 'button', class: option.value === value ? 'is-active' : '', 'data-value': option.value, text: option.label, disabled: disabled ? 'disabled' : null, onclick: () => onChange(option.value) }));
      }
      $$('button[disabled="null"]', wrap).forEach((b) => b.removeAttribute('disabled'));
      return wrap;
    };
    const update = () => { renderBuilder(); renderOutput(); };

    // 1. Profile
    const profiles = el('div', { class: 'profile-grid' }, PROFILES.map((p) => el('button', { type: 'button', class: 'profile' + (state.profile === p.key ? ' is-active' : ''), onclick: () => { setState(defaultState(p.key)); update(); } }, [
      el('span', { class: 'profile__title', text: p.title }), el('span', { class: 'profile__desc', text: p.desc }),
    ])));
    root.appendChild(step(1, 'Профиль репозитория', 'Профиль выставляет стартовые оси, архитектуру и ответ про typecheck. Дальше всё редактируется. Выбор профиля сбрасывает остальные шаги.', profiles));

    // 2. Architecture + version
    const archBody = el('div', { class: 'field-list' }, [
      el('div', { class: 'field' }, [
        el('div', { class: 'field__label' }, [el('code', { text: 'architecture' }), el('small', { text: 'Приложение → стандарт sliced-dotnet/1. Библиотека → applicable: false с причиной; деклараций layers/modules не существует.' })]),
        seg([{ value: 'application', label: 'application' }, { value: 'library', label: 'library' }], state.kind, (v) => { state.kind = v; update(); }),
        state.kind === 'library' ? el('div', { class: 'field__reason' }, [el('input', { class: 'text-input', type: 'text', value: state.archReason, placeholder: 'reason: почему стандарт не применим', oninput: (e) => { state.archReason = e.target.value; renderOutput(); } })]) : null,
      ]),
      el('div', { class: 'field' }, [
        el('div', { class: 'field__label' }, [el('code', { text: 'version' }), el('small', { text: '"2.12.0" пинит контракт; "latest" включает rolling-контракт вслед за установленным бинарём.' })]),
        seg([{ value: 'pin', label: '2.12.0' }, { value: 'latest', label: 'latest' }], state.latest ? 'latest' : 'pin', (v) => { state.latest = v === 'latest'; update(); }),
      ]),
    ]);
    root.appendChild(step(2, 'Архитектура и контракт', null, archBody));

    // 3. Applicability
    const axesBody = el('div', { class: 'field-list' }, AXES.map((axis) => {
      const a = state.axes[axis.key];
      const affected = CHECKS.filter((c) => c.axis === axis.key).map((c) => c.id);
      return el('div', { class: 'field' + (a.applicable ? '' : ' is-off') }, [
        el('div', { class: 'field__label' }, [el('code', { text: `applicability.${axis.key}` }), el('small', { text: `${axis.name}: ${affected.join(', ')}` })]),
        seg([{ value: 'true', label: 'applicable' }, { value: 'false', label: 'not applicable' }], String(a.applicable), (v) => { a.applicable = v === 'true'; if (!a.applicable && !a.reason) a.reason = axis.reason; update(); }),
        a.applicable ? null : el('div', { class: 'field__reason' }, [el('input', { class: 'text-input', type: 'text', value: a.reason, placeholder: 'reason: почему эти проверки не про этот репозиторий', oninput: (e) => { a.reason = e.target.value; renderOutput(); } })]),
      ]);
    }));
    root.appendChild(step(3, 'Языковые оси применимости', 'Ось выключает все проверки языка одной записью с причиной. Проверки при этом остаются в <code>policy</code>: ридер требует полный список независимо от применимости.', axesBody));

    // 4. Frame answers
    const answersBody = el('div', { class: 'field-list' }, CHECKS.filter((c) => c.key).map((check) => {
      const a = state.answers[check.key];
      const forms = check.requiresPaths ? FRAME_FORMS.filter((f) => f.value === 'none' || f.value === 'paths') : FRAME_FORMS;
      const placeholder = a.form === 'paths' ? `пути через запятую, например ${check.hint}` : a.form === 'none' ? '' : 'reason: где это есть, почему отсутствует или почему вопрос не применим';
      return el('div', { class: 'field field--stack' + (a.form === 'none' ? ' is-off' : '') }, [
        el('div', { class: 'field__label' }, [el('code', { text: `answers.${check.key}` }), el('small', { text: check.summary })]),
        el('div', { class: 'inline-fields' }, [
          el('select', { class: 'select-input', style: 'width:auto', onchange: (e) => { a.form = e.target.value; if (a.form === 'paths' && !a.text) a.text = ''; update(); } }, forms.map((f) => el('option', { value: f.value, text: f.label, selected: f.value === a.form ? 'selected' : null }))),
          a.form === 'none' ? null : el('input', { class: 'text-input', type: 'text', style: 'flex:1 1 220px', value: a.text, placeholder, oninput: (e) => { a.text = e.target.value; renderOutput(); } }),
        ]),
      ]);
    }));
    $$('option[selected="null"]', answersBody).forEach((o) => o.removeAttribute('selected'));
    root.appendChild(step(4, 'Ответы рамки', 'Self-reported: <code>paths</code> — навигация, не доказательство. Не выдумывайте положительный ответ; <code>present: false</code> с причиной — честный readiness gap, который при <code>advisory</code> не ломает прогон.', answersBody));

    // 5. Policy
    const policyBody = el('div', {});
    for (const group of GROUPS) {
      const checks = CHECKS.filter((c) => c.group === group.key);
      const setAll = (value) => { for (const c of checks) state.policy[c.id] = value; update(); };
      const list = el('div', { class: 'field-list' }, checks.map((check) => {
        const na = (check.axis && !state.axes[check.axis].applicable) || (check.section && state.kind !== 'application');
        return el('div', { class: 'field' + (na ? ' is-off' : '') }, [
          el('div', { class: 'field__label' }, [el('code', { text: check.id }), el('small', { text: na ? 'ось не применима → NotApplicable при любой policy; запись всё равно обязательна' : check.budget && state.policy[check.id] !== 'off' ? 'нужен tracked .harness.budget.json' : '' })]),
          seg([{ value: 'required', label: 'required' }, { value: 'advisory', label: 'advisory' }, { value: 'off', label: 'off' }], state.policy[check.id], (v) => { state.policy[check.id] = v; update(); }),
        ]);
      }));
      policyBody.appendChild(el('div', { class: 'policy-group' }, [
        el('div', { class: 'policy-group__head' }, [
          el('span', { class: 'policy-group__title' }, [group.title, el('span', { class: 'badge badge--neutral', text: String(checks.length) })]),
          el('div', { class: 'policy-group__actions' }, ['required', 'advisory', 'off'].map((v) => el('button', { type: 'button', text: 'все ' + v, onclick: () => setAll(v) }))),
        ]),
        list,
      ]));
    }
    root.appendChild(step(5, 'Policy каждой проверки', '<code>required</code> блокирует, <code>advisory</code> показывает находки без провала, <code>off</code> пропускает. Переключатель един, включая инвариант архитектуры и DSM-бюджет; адресных исключений нет.', policyBody));

    // 6. Settings
    const numField = (label, value, onInput, max) => el('label', { class: 'inline-fields' }, [label, el('input', { class: 'num-input', type: 'number', min: '0', max: max || '', value: String(value), oninput: (e) => { onInput(Number(e.target.value) || 0); renderOutput(); } })]);
    const settingsBody = el('div', { class: 'field-list' }, [
      ...['csharp', 'yaml', 'typescript'].map((lang) => el('div', { class: 'field' + (state.axes[lang].applicable ? '' : ' is-off') }, [
        el('div', { class: 'field__label' }, [el('code', { text: `settings.comments.${lang}` }), el('small', { text: 'файл нарушает правило, если достиг minimumCommentLines и комментарии превышают percentageLimit авторских строк' })]),
        el('div', { class: 'inline-fields' }, [numField('минимум строк', state.settings.comments[lang].min, (v) => { state.settings.comments[lang].min = v; }), numField('% предел', state.settings.comments[lang].pct, (v) => { state.settings.comments[lang].pct = Math.min(100, v); }, '100')]),
      ])),
      el('div', { class: 'field' + (state.axes.csharp.applicable ? '' : ' is-off') }, [
        el('div', { class: 'field__label' }, [el('code', { text: 'settings.duplication.csharp' }), el('small', { text: 'калиброванный профиль 30/90: окно нормализованных строк и минимум токенов в окне' })]),
        el('div', { class: 'inline-fields' }, [numField('windowLines', state.settings.duplication.window, (v) => { state.settings.duplication.window = Math.max(1, v); }), numField('minimumTokens', state.settings.duplication.tokens, (v) => { state.settings.duplication.tokens = v; })]),
      ]),
      el('div', { class: 'field' }, [
        el('div', { class: 'field__label' }, [el('code', { text: 'settings.commits' }), el('small', { text: 'язык человеческих частей сообщения и требование clone-local setup' })]),
        el('div', { class: 'inline-fields' }, [
          seg([{ value: 'ru', label: 'ru' }, { value: 'en', label: 'en' }], state.settings.commits.language, (v) => { state.settings.commits.language = v; update(); }),
          seg([{ value: 'true', label: 'requireSetup' }, { value: 'false', label: 'без setup' }], String(state.settings.commits.requireSetup), (v) => { state.settings.commits.requireSetup = v === 'true'; update(); }),
        ]),
      ]),
    ]);
    root.appendChild(step(6, 'Settings', 'Каждая секция обязательна, даже если её ось выключена: параметр, который никто не применяет, хуже отсутствующего — ридер отвергает лишние и требует недостающие.', settingsBody));
  }
  function renderOutput() {
    const config = buildConfig();
    const json = compactJson(config);
    $('#builder-json').innerHTML = highlightJson(json);
    $('#builder-json').dataset.raw = json;
    const { lines, answered } = evaluate(config);
    const status = $('#builder-status');
    status.innerHTML = '';
    for (const line of lines) status.appendChild(el('div', { class: 'status-line is-' + line.level }, [el('span', { class: 'status-line__dot' }), el('span', { text: line.text })]));
    const tally = $('#builder-tally');
    tally.innerHTML = '';
    const answersTotal = CHECKS.filter((c) => c.key).length;
    tally.appendChild(el('span', { class: 'badge badge--success badge--mono', text: `policy ${CHECKS.length}/${CHECKS.length}` }));
    tally.appendChild(el('span', { class: 'badge badge--success badge--mono', text: `applicability ${AXES.length}/${AXES.length}` }));
    tally.appendChild(el('span', { class: 'badge badge--success badge--mono', text: 'settings 5/5' }));
    tally.appendChild(el('span', { class: 'badge badge--mono ' + (answered === answersTotal ? 'badge--success' : 'badge--warning'), text: `answers ${answered}/${answersTotal}` }));
    const hasError = lines.some((l) => l.level === 'error');
    tally.appendChild(el('span', { class: 'badge badge--mono ' + (hasError ? 'badge--error' : 'badge--success'), text: hasError ? 'harness check → exit 2' : 'ридер примет файл' }));
    $('#builder-cli').textContent = `harness init --kind ${state.kind}${state.latest ? ' --latest' : ''} --language ${state.settings.commits.language}\n# затем ответить на answers.* и, если нужно, смягчить policy\nharness budget update   # создаёт .harness.budget.json для complexity.csharp\ngit add .harness.json .harness.budget.json .editorconfig\nharness check --verbose`;
  }
  $('#builder-copy').addEventListener('click', (e) => copyText($('#builder-json').dataset.raw || '', e.currentTarget, 'Скопировано'));
  $('#builder-reset').addEventListener('click', () => { setState(defaultState(state.profile)); renderBuilder(); renderOutput(); });

  /* ── Boot ───────────────────────────────────────────────────────────── */
  renderChecks();
  loadCycles('shop');
  resetDsm();
  renderDup();
  renderArch();
  setState(defaultState('app'));
  renderBuilder();
  renderOutput();
})();
