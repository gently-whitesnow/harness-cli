# site/ — публичный лендинг harness

Статика без сборки: `index.html`, `styles.css`, `app.js`, `favicon.svg`, `fonts/`. Деплой —
`.github/workflows/deploy-site.yml` (rsync каталога на VPS). Локально: `python3 -m http.server`
из этого каталога. Правила и обоснование — [ADR-0047](../adrs/0047-landing-mirrors-the-contract.md).

## Что зеркалит контракт

Сайт описывает тот же контракт, что исполняет бинарь, поэтому изменение контракта не
завершено, пока не обновлён сайт. Источник правды всегда код; сайт — его отражение.

| Изменилось в коде | Обновить на сайте |
| --- | --- |
| `Host/CheckRegistry.cs` — новая или удалённая проверка | `CHECKS` в `app.js` (id, группа, ось, summary, ADR) и, если нужно, `GROUPS` |
| `Config/ConfigInitializer.cs` — дефолт policy в `init` | `defaultState` в `app.js`, бейдж `default …` берётся из группы |
| `Config/HarnessSettings.cs` — дефолты settings | `defaultState.settings` в `app.js` и раздел «Settings» конструктора |
| `Config/HarnessSettingsReader.cs` — новая секция settings | `buildConfig`, шаг 6 конструктора и счётчик `settings n/n` |
| `Domain/Harness/Languages/Language.cs` — новая языковая ось | `AXES`, профили `PROFILES`, группа «Другие языки» |
| `Version.props` — новый релиз | Все упоминания версии в `index.html` и `app.js`; маршрут в `FrameUpgrade.cs` |
| `*Explanation.cs` — формула, пределы, remediation | Соответствующая карточка проверки и раздел «Глубже» |
| `SlicedDotNetShapeCheck.cs`, ADR-0033–0041, 0050 — словарь слоёв, инварианты, конвенции | Раздел «Архитектура»: дерево-пример, таблица слоёв, правила импортов, свёрнутые блоки инвариантов и сравнения с FSD; `renderArch` в `app.js` |
| Новый ADR про проверку | Ссылка в карточке (`adr` в `CHECKS`) и в тексте раздела «Глубже» |

Тест `tests/Harness.Tests/SiteContractTests.cs` сверяет идентификаторы `CHECKS` с выводом
`harness help` и версию на сайте с `Version.props`: расхождение падает в `dotnet test`.

## Дизайн

Токены — из `DESIGN.md` проекта Throne, перенесены в `styles.css` дословно (OKLCH, light и
throne-dark). Шрифты Mona Sans и Monaspace Neon лежат в `fonts/`; у Mona Sans нет кириллицы,
русский текст идёт системным fallback. Не добавляй сборку, зависимости и внешние CDN: сайт
выкладывается каталогом как есть.

## Язык и тон

Русский, спокойный и плотный: формулы, пределы и цена решений, а не обещания. Тексты
проверок пересказывают `harness explain <check-id>` и ADR, не выдумывая новых правил.
