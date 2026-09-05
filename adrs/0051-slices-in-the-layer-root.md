# ADR-0051: Слайсы в корне слоя

## Status

Accepted. Уточняет [ADR-0033](0033-canonical-standard-over-declarations.md),
[ADR-0034](0034-language-axis-in-sliced-dotnet.md) (раскладку харнеса),
[ADR-0037](0037-segments-by-purpose.md) и [ADR-0050](0050-domain-is-the-bottom-layer.md);
контракт `2.13.0`, имя стандарта остаётся `sliced-dotnet/1`.

## Context

FSD кладёт слайсы прямо в корень слоя: `src/<слой>/<слайс>/<сегмент>`, промежуточного
каталога нет, а слово `features` в FSD — имя слоя, не контейнер. ADR-0033 взял у FSD
форму дерева, но в прежней форме стандарта слайсы лежали в `Application/Features/<Слайс>` и в
`Api/Features`, `Consumers/Features`, `Infrastructure/Features`, тогда как в `Domain` — уже
в корне слоя. `Features/` отделял слайсы от каталогов уровня слоя, которых FSD не допускает
(правило Steiger `no-segments-on-sliced-layers`), и харнес применял это правило только
внутри `Features/`. Корень слоя стал лазейкой, и оба пилотных приложения ей пользовались:

- в первом половина файлов слоя `Api` лежала в девяти каталогах корня слоя вне слайсов —
  аутентификация, конфигурация, ошибки, документация API, — а в корне `Application` —
  `Contracts/{Errors,Filtering,Ports}` и шина событий;
- во втором `Application/Contracts/<Слайс>` держал десятки файлов — весь публичный API
  слайса вне слайса, — а `Host` вместо composition root нёс два десятка файлов логики.

`architecture.sliced-dotnet` на обоих прошёл с нулём находок. Два наблюдения меньшего
веса: единственная группа слайсов, названная как сам продукт и содержащая все слайсы, не
сообщает ни одному слайсу ничего (ни харнес, ни Steiger такое не ловят); сегмент, названный
как свой слайс (`Graph/Graph`), не говорит о назначении.

ADR-0033 обещал другое: пересмотр словаря — новым ADR и версией `sliced-dotnet/2` в имени
стандарта, чтобы переход никогда не был тихим. Обещание не исполняется здесь и не
исполнялось дважды до этого; почему — в пункте 7 Decision. Коротко: механизм явного
перехода в контракте уже есть, это pin `version`, а второе имя обслуживало бы легаси,
которого нет. Ни один репозиторий не стоит на прежней форме: пилотные приложения и сам
харнес переходят в этом же релизе, инструментом за его пределами ещё никто не пользуется.
Владелец решил не нести легаси-форму ради симметрии имён.

Обсуждались альтернативы формы:

- **Оставить `Features/`** и распространить `no-segments-on-sliced-layers` на корень слоя.
  Лазейку закрывает, но стандарт продолжает противоречить FSD и самому себе: `Domain` без
  контейнера, остальные слои — с ним.
- **Переименовать в `Slices/`.** Убирает конфликт со словарём FSD, но оставляет лишний
  уровень и тот же корень слоя, который надо охранять отдельным правилом.
- **Убрать каталог** и перенести правило на корень слоя — выбрано: одна форма для всех
  слоёв, как в FSD.

## Decision

1. **Слайсы в корне слоя.** Слайсовые слои — `Api`, `Consumers`, `Application`,
   `Infrastructure`, `Domain`. Слайс лежит в `<Слой>/<Слайс>`, группа — в
   `<Слой>/<Группа>/<Слайс>`: один уровень, группа не содержит файлов, как раньше, но без
   каталога `Features/`. Источник истины по-прежнему `Application/`. Каталог с файлом
   непосредственно внутри либо с дочерним `Contracts` — слайс; иначе — группа. `Host`
   остаётся sliceless: сегменты прямо в корне.
2. **Зарезервированных каталогов два:** `Domain/Shared` и `Infrastructure/Persistence`. Они
   не слайсы и не зеркала.
3. **Корень слайсового слоя содержит только слайсы и группы** — перенос
   `no-segments-on-sliced-layers` на корень слоя. В зеркальном слое (`Api`, `Consumers`,
   `Infrastructure`, `Domain`) каталог корня, не являющийся слайсом `Application` и не
   зарезервированный, даёт blocking-находку: с essence-именем по словарю ADR-0037
   (`Services`, `Validators`, `Common`…) — `no-segments-on-sliced-layers: layer 'Api' holds
   segment 'Services' at its root; a sliced layer holds only slices — move it into Host or
   into a slice`; с любым другим именем — существующую `orphan-slice-mirror` с дополненным
   remediation: `a directory in the root of a sliced layer is a slice mirror — move
   cross-cutting code into Host or into a slice`. Файл непосредственно в корне слайсового
   слоя (`Api/Endpoint.cs`) — существующая blocking-находка `outside-slice`, прежде
   действовавшая только внутри `Features/`.
4. **`no-layer-public-api`** (порт правила Steiger): каталог `Contracts` непосредственно под
   корнем слайсового слоя — `Application/Contracts`, `Api/Contracts`, `Domain/Contracts`,
   `Infrastructure/Contracts`, `Consumers/Contracts` — blocking-находка `no-layer-public-api:
   layer 'Application' publishes Contracts/ at its root; a slice publishes its own Contracts/
   — move them into Application/<Slice>/Contracts/`. Такой каталог не считается слайсом,
   группой или зеркалом и второй находки не порождает. Blocking по ADR-0002:
   детерминировано, actionable, негативная фикстура из пилота.
5. **`repetitive-naming`** — advisory-observation (строка `advisory …`, не `Finding`, код
   возврата не меняет ни при какой policy, как `insignificant-slice`): если в зоне ровно одна
   группа и все слайсы лежат в ней — `advisory <zone>/Application/<Group>:
   repetitive-naming: every slice of dimension 'Application' sits in the single group
   '<Group>'; the group name repeats on every slice and carries no information — flatten the
   group or split slices into two or more groups`.
6. **`ambiguous-slice-names`** — advisory-observation: непосредственный сегмент слайса в
   любом слайсовом измерении, чьё имя совпадает с leaf-именем слайса без учёта регистра —
   `advisory <zone>/<Layer>/<slice>/<Segment>: ambiguous-slice-names: segment '<Segment>' of
   slice '<slice>', dimension '<Layer>', repeats the slice name; name the segment after its
   purpose`.
7. **Контракт `2.13.0`; имя стандарта остаётся `sliced-dotnet/1`, версия в имени как
   механизм отменяется.** Форму называет pin `version`
   ([ADR-0023](0023-release-version-as-the-verification-contract.md),
   [ADR-0032](0032-topology-over-thresholds.md)):
   бинарь исполняет только свой контракт, любой другой pin даёт `Incomplete`, а меняет pin
   только `harness upgrade`, печатающий весь маршрут миграции. Версия в имени стандарта
   дублировала этот сигнал, ничего к нему не добавляя. Обещание ADR-0033 уже дважды не
   исполнялось осознанно: [ADR-0041](0041-layer-is-the-assembly.md) добавил инвариант
   «слой = сборка», [ADR-0050](0050-domain-is-the-bottom-layer.md) убрал слой `Shared` —
   оба пересмотрели стандарт и сохранили имя `/1`, сославшись на то, что переход несёт
   контракт. Это решение признаёт практику. `harness upgrade` поднимает только pin и
   печатает блок «Release 2.13 changes» с описанием переноса; строку `standard` он не
   трогает. Ридер принимает `sliced-dotnet/1`, любое другое имя — `Incomplete` с текстом
   `'architecture.standard' must be 'sliced-dotnet/1'; '<имя>' is not supported`; других
   имён стандарта не существует. `harness init` пишет `sliced-dotnet/1`.
8. **Харнес сам мигрирует:** `src/Harness/<Слой>/Features/Harness` →
   `src/Harness/<Слой>/Harness` для `Api`, `Application` и `Infrastructure` через `git mv`;
   пространства имён `Features` не содержали и не меняются.

Всё остальное — DAG слоёв, изоляция слайсов, X-контракты, слой = сборка,
segments-by-purpose, flat-directory-grouping, cross-api density, directories-by-purpose —
не меняется, кроме префиксов путей: везде `<Layer>/` вместо `<Layer>/Features/`.

## Consequences

### Positive

- Одна форма для всех слоёв, как в FSD: `<Слой>/<Слайс>/<Сегмент>` без исключения для
  `Domain`, стандарт больше не противоречит сам себе.
- Лазейка корня слоя закрыта: сквозная обвязка не может лежать «рядом со слайсами» и пройти
  проверку с нулём находок, публичный API слайса не может жить вне слайса.
- Два новых advisory называют шум в именах — группу-обёртку и сегмент-эхо, — не меняя кода
  возврата.

### Negative / Risks

- Каждый репозиторий на стандарте переносит `<Слой>/Features/<Слайс>` в `<Слой>/<Слайс>`
  при `harness upgrade`; до переноса `architecture.sliced-dotnet` блокирует.
- Сквозная web-обвязка — аутентификация, обработка ошибок, документация API — обязана уйти в
  `Host` или в слайс. Это принятая цена: корень слоя перестаёт быть местом для кода, у
  которого нет владельца-слайса.
- Пилот с `Application/Contracts/<Слайс>` переносит десятки файлов внутрь слайсов; до
  переноса `no-layer-public-api` блокирует.
- Различать формы по имени стандарта больше нечем: имя одно и оно молчит о том, какая форма
  за ним стоит, — это знает только pin контракта. Пока все потребители переходят одним
  релизом, цена нулевая; если у стандарта появятся внешние потребители, застрявшие на разных
  формах, различение придётся вернуть отдельным ADR — и это будет дороже, чем зарезервировать
  имя заранее.

## Ссылки

- [Feature-Sliced Design](https://feature-sliced.design) — слайсы в корне слоя, `features`
  как имя слоя.
- Steiger: `no-segments-on-sliced-layers`, `no-layer-public-api`, `repetitive-naming`,
  `ambiguous-slice-names`.
