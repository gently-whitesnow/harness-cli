# ADR-0009: NativeAOT — инвариант сборки

## Status

Accepted

## Context

Пользователь потребовал удобную установку без отдельного .NET runtime: основной локальный
target — macOS, будущий CI target — Linux. В дизайн-сессии сравнивались Go, Rust, C#
NativeAOT, Node SEA и Bun. Rust признан технически подходящим, Go — простым для поставки,
но пользователь предпочёл .NET; выбор C# не был выведен из уже написанного кода.

NativeAOT удовлетворяет требованию self-contained executable и сохраняет знакомый стек,
но артефакт остаётся platform/RID-specific. Кроме того, standalone harness не делает
standalone проверяемый toolchain: для `dotnet test`, pnpm или Git соответствующие программы
по-прежнему должны быть установлены в окружении репозитория.

## Decision

Харнес публикуется как NativeAOT-исполняемый файл; установленный .NET runtime в момент
использования не нужен. Совместимость с NativeAOT — инвариант сборки, а не разовая
настройка: без рефлексии в рантайме, без генерации кода в рантайме, без динамически
загружаемых managed-плагинов. Поставляемые проверки собираются в статический реестр в коде
(`Checks/CheckRegistry.cs`).

`dotnet test` включает NativeAOT-публикацию, потому что нарушение инварианта видно не на
сборке, а на публикации или уже в самом бинарнике.

## Consequences

### Positive

- Для каждого поддерживаемого RID получается один self-contained executable, запускаемый
  без установки .NET runtime.
- Приёмочный publication test удаляет runtime-related environment variables и гоняет
  опубликованный бинарник на passing и failing repository fixtures.
- Реализация и проверяемый .NET adapter остаются в знакомом для владельца стеке.

### Negative / Risks

- Расширение набора проверок — только изменением кода и пересборкой: плагинов нет.
- Полный `dotnet test` дороже обычного, так как включает публикацию.
- NativeAOT не поддерживает cross-OS publication как общий путь: macOS и Linux artifacts
  требуют собственной release matrix, а совместимость зависимостей проверяется AOT analyzer.
- Отсутствие runtime относится только к харнесу; применимый repository gate всё равно
  возвращает `Incomplete`, если его SDK или package manager не установлен.
