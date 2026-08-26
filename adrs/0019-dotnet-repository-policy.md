# ADR-0019: Статическая политика .NET-репозитория

## Status

Accepted. Рекомендация про `suppress` superseded by
[ADR-0032](0032-topology-over-thresholds.md).

## Context

Одинаковые MSBuild-свойства и версии NuGet-пакетов расходятся между проектами незаметно:
новый `.csproj` легко получает более слабые nullable, analyzers или warning defaults, а
ручные версии `PackageReference` начинают дрейфовать. Legacy `.sln` многословен и скрывает
структурное изменение в шумном diff; `.slnx` в .NET 10 стал стандартным XML-форматом.

Харнес не запускает toolchain репозитория по ADR-0011. Значит, новая проверка может
утверждать только то, что детерминированно следует из tracked XML, и не должна выдавать
собственную частичную реализацию MSBuild evaluation за результат сборки.

## Decision

Все три проверки имеют applicability `dotnet` и blocking policy по умолчанию.

`build-properties.dotnet` требует, чтобы каждый tracked SDK-style `.csproj`, `.fsproj` или
`.vbproj` был покрыт ближайшим `Directory.Build.props`. В нём обязательны `Nullable`,
`ImplicitUsings`, warnings-as-errors, встроенные analyzers, `latest-Recommended`, code style
на build, deterministic output и условный `ContinuousIntegrationBuild`. Явное ослабление
этих значений в проекте — нарушение. Одинаковый `TargetFramework`, повторённый во всех
проектах, нужно вынести; различающиеся TFM остаются локальными.

`central-packages.dotnet` применяется, когда проект содержит `PackageReference`. Ближайший
`Directory.Packages.props` включает Central Package Management, содержит единственную
версию каждого используемого пакета, а project reference не хранит `Version` или
`VersionOverride`.

`solution-format.dotnet` запрещает tracked `.sln`. Для нескольких SDK-style проектов нужен
хотя бы один tracked `.slnx`, и каждый проект должен входить хотя бы в одно такое решение.

Проверки читают только Git evidence и XML. Они поддерживают независимые scoped
`Directory.Build.props` и `Directory.Packages.props` через правило ближайшего файла, но не
вычисляют произвольные imports, conditions и MSBuild functions. Осознанное отклонение
оформляется обычным именованным `suppress` с путём и причиной.

## Consequences

- Новый проект сразу наследует общую build policy и не выбирает версии пакетов локально.
- Mixed-TFM и scoped monorepo остаются допустимыми без единого корневого значения.
- `.slnx` становится проверяемой картой authored-проектов, а не только build entry point.
- Сложная динамическая MSBuild-конфигурация требует именованного исключения: харнес не
  запускает evaluation и не заявляет доказательство, которого у него нет.
