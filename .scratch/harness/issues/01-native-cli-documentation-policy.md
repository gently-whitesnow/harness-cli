# 01 — NativeAOT CLI проверяет документационную политику

**What to build:** Первый полностью работающий `harness check`: устанавливаемый без отдельного .NET runtime нативный CLI проверяет документационную политику Git-репозитория, объясняет результаты и измеряет время проверки, не изменяя tracked-содержимое.

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

- [ ] CLI запускает `check` для текущего каталога или явно переданного репозитория и проверяется как отдельный скомпилированный процесс.
- [ ] NativeAOT-публикация для текущей платформы проходит без AOT warnings, а опубликованный бинарник успешно выполняет smoke test.
- [ ] Проверка рассматривает только tracked Markdown и не создаёт шум из generated, vendored и build-output содержимого.
- [ ] `ROOT.md` признаётся единственным источником корневых агентских инструкций и ограничивается 150 физическими строками.
- [ ] `AGENTS.md` и `CLAUDE.md` проверяются как обязательные прямые относительные Git-symlink на `ROOT.md`; copies, chains, broken links, absolute links и другие targets обнаруживаются.
- [ ] Корневой `README.md` разрешён и ограничивается 150 физическими строками; Markdown под корневым `adrs` разрешён как долговременные решения.
- [ ] Остальной tracked Markdown выдаёт advisory finding и не делает результат blocking failure.
- [ ] У проверки есть стабильный identifier, concise default output, ненулевая duration и отдельное понятное `explain`.
- [ ] `--only` и `--skip` выбирают проверку явно, а skipped result остаётся видимым в summary.
- [ ] Неизвестный check identifier и невозможность достоверно прочитать Git evidence завершаются как incomplete/tool error, а не как pass или repository violation.
- [ ] Позитивные и негативные fixture-репозитории покрывают line limits, разрешённые документы и все варианты symlink behavior через внешний CLI seam.
- [ ] После выполнения tracked-содержимое и Git state fixture-репозитория не изменены.
