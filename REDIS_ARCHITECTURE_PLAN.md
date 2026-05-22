# Runtime Store, Redis and Monitoring Coverage Plan

## Цель

Redis добавляется как optional runtime-инфраструктура для ускорения и
координации работы микросервисов. Он не становится источником истины для правил,
шаблонов, CMDBuild-схемы, CMDBuild master data или Zabbix desired graph.

SQLite добавляется как durable-хранилище для membership-state,
dirty scopes и разовых срезов покрытия мониторингом. Это разные задачи: Redis
отвечает за runtime-координацию, база отвечает за состояние, которое должно
переживать restart и использоваться для отчетных срезов.

Основные ожидаемые эффекты:

- дедупликация событий переживает restart и несколько инстансов;
- долгие операции имеют общий progress/status вне памяти процесса;
- публикации графов, dependencies и SLA защищены от параллельных запусков;
- burst webhook-событий схлопывается в один reconcile там, где это безопасно;
- CMDBuild/Zabbix lookup-кэши уменьшают повторные API-запросы.

## Не цели

- Не заменять Kafka. Kafka остается журналом событий и команд.
- Не переносить правила, шаблоны и managed relations в Redis.
- Не заменять CMDBuild как master data.
- Не заменять Zabbix как фактическую целевую систему.
- Не переносить `apply-membership.json` в Redis первым этапом. Это durable
  state; для него Redis допустим только после отдельного решения по persistence,
  backup и recovery.
- Не делать непрерывный аудит покрытия в реальном времени. Нужен разовый срез
  данных по запросу администратора или по отдельному расписанию.
- Не использовать срез покрытия как источник управляющих команд. Срез должен
  показывать разрыв между ожидаемым и фактическим мониторингом, но не создавать
  объекты без явного apply/reconcile.

## Архитектурные границы

Redis должен хранить только runtime-состояние, которое можно восстановить из
CMDBuild, файлов конфигурации, Kafka-команд или Zabbix:

- locks;
- progress долгих операций;
- semantic dedup fingerprints;
- debounce markers;
- временные lookup-кэши;
- диагностические counters.

Redis-ключ сам по себе не должен быть достаточным основанием для создания,
изменения или удаления объектов в CMDBuild/Zabbix. Любая фактическая операция
должна строиться из текущих правил, шаблонов, managed state и данных внешних
систем.

Durable storage должен хранить расчетное состояние и срезы, которые нужны для
дельта-публикации, восстановления и отчетности. В текущем плане целевой backend
для этой задачи - SQLite. Вопрос отдельного multi-instance backend выведен за
рамки текущего плана.

## Области применения

### 1. Semantic Deduplication

Сервис: `cmdbconfigbuilder`.

Redis хранит fingerprints с TTL:

```text
cmdb2m:dedup:{service}:{rule_id}:{source_class}:{source_card_id}:{event_type}:{semantic_hash}
```

Поведение:

- `SET key value NX EX <windowSeconds>`;
- если ключ уже есть, команда не публикуется;
- TTL берется из `SemanticDeduplication:WindowSeconds`;
- при недоступном Redis fallback зависит от настройки: local-memory или fail.

Начальный режим: fallback в текущую in-memory дедупликацию.

### 2. Distributed Locks

Сервис: `zabbixconfig2api`, частично `cmdbconfigbuilder`.

Locks нужны для операций:

- публикация полного/частичного service graph;
- публикация suppression membership graph;
- trigger dependencies reconcile;
- SLA publication;
- cleanup managed/stale объектов, если будет реализован отдельный scoped cleanup.

Ключи:

```text
cmdb2m:lock:zabbix:apply:{layer}
cmdb2m:lock:zabbix:dependencies:suppression
cmdb2m:lock:zabbix:sla
cmdb2m:lock:cmdbuild:apply-current:{layer}
```

Требования:

- lock owner содержит operation id и service instance id;
- TTL обязателен;
- продление TTL во время долгой операции;
- release только владельцем lock;
- UI должен показывать, кто держит lock, operation id и возраст lock.

### 3. Operation Progress

Сервисы: `cmdbconfigbuilder`, `zabbixconfig2api`, UI/BFF.

Redis хранит progress долгих операций:

```text
cmdb2m:op:{operation_id}
cmdb2m:op:index:{layer}
```

Содержимое:

- operation id;
- layer;
- stage;
- counters;
- started/updated timestamps;
- last error;
- performance summary;
- cancel flag.

TTL:

- running operations: продлевается;
- completed/failed operations: например 24 часа;
- UI читает progress по operation id независимо от процесса, который начал
  операцию.

### 4. Debounce Reconcile

Сервис: `zabbixconfig2api`.

Redis используется для схлопывания множества membership updates в один
dependencies reconcile:

```text
cmdb2m:debounce:dependencies:suppression
cmdb2m:queue:dependencies:suppression
```

Поведение:

- при membership update ставится debounce marker;
- scheduler запускает reconcile после quiet period;
- несколько событий внутри окна дают один reconcile;
- если reconcile уже выполняется, ставится pending rerun flag.

### 5. Lookup Cache

Сервисы: `cmdbconfigbuilder`, `zabbixconfig2api`, UI/BFF по необходимости.

Кэшируем только производные lookup-данные, которые можно безопасно перечитать:

- CMDBuild class/domain metadata;
- CMDBuild managed class catalog;
- Zabbix service id by managed key;
- Zabbix host id/name lookup;
- Zabbix trigger lookup batches;
- Zabbix SLA lookup.

Пример ключей:

```text
cmdb2m:cache:cmdbuild:class-schema:{class_code}
cmdb2m:cache:cmdbuild:domains:{prefix}
cmdb2m:cache:zabbix:service-by-key:{layer}:{managed_key}
cmdb2m:cache:zabbix:host:{hostid}
cmdb2m:cache:zabbix:triggers:{selector_hash}:{batch_hash}
```

Требования:

- TTL обязателен;
- invalidate при `configuration/reload`, schema sync, full graph apply;
- никогда не считать cache miss ошибкой;
- ошибки Redis не должны менять результат бизнес-операции, если включен fallback.

### 6. Durable Membership State

Сервисы: `cmdbconfigbuilder`, `cmdbaggregation2cmdbuild`, `zabbixconfig2api`.

Membership-state хранится не в Redis, а в durable backend:

- `file` для совместимости с текущим `apply-membership.json`;
- `sqlite` как текущий durable backend.

Минимальная модель:

```text
source_class
source_card_id
source_code
layer
rule_id
template_id
dimension_key
dimension_value
target_class
target_card_id
target_code
zabbix_service_id
zabbix_trigger_id
membership_hash
first_seen_at
last_seen_at
last_operation_id
```

Назначение:

- знать, где source-карточка находилась до изменения;
- удалять stale membership при переезде карточки между dimensions;
- пересчитывать только затронутые группы;
- восстанавливать delta-публикацию после restart;
- давать основу для аудита покрытия.

### 7. Monitoring Coverage Snapshot

Аудит покрытия в этой архитектуре - это не непрерывный контроль в реальном
времени, а разовый snapshot по выбранному срезу данных. Его запускает
администратор вручную, либо отдельное расписание, если оно будет включено
явно. Результат snapshot сохраняется в SQLite и используется для
отчетности, сравнения с предыдущими срезами и поиска разрывов.

Срез может иметь небольшую операционную дельту: CMDBuild и Zabbix читаются не
атомарно в один момент времени, поэтому между началом и завершением расчета
объекты могут измениться. Это допустимо, если UI явно показывает время начала,
время завершения, статус полноты и предупреждение о возможной дельте.

Основные вопросы аудита:

- сколько объектов должно быть поставлено на мониторинг;
- сколько объектов уже имеют заполненный `zabbix_main_hostid`;
- сколько объектов фактически существуют в Zabbix;
- сколько объектов участвуют в service/suppression membership;
- сколько объектов не покрыты правилами;
- сколько объектов покрыты правилами, но не имеют Zabbix host;
- какой процент покрытия по классу, модели, сервису, шаблону и контуру.

Базовые счетчики:

```text
expected_total
eligible_total
excluded_total
rules_matched_total
missing_rule_total
zabbix_main_hostid_filled_total
zabbix_main_hostid_missing_total
zabbix_host_found_total
zabbix_host_missing_total
membership_total
service_membership_total
suppression_membership_total
coverage_percent
zabbix_presence_percent
membership_percent
```

Разница между счетчиками:

- `expected_total` - объекты, которые по политике должны мониториться;
- `eligible_total` - объекты, прошедшие фильтры включения;
- `rules_matched_total` - объекты, на которые нашлись правила;
- `zabbix_main_hostid_filled_total` - объекты CMDBuild с заполненным
  `zabbix_main_hostid`;
- `zabbix_host_found_total` - объекты, для которых host реально найден в Zabbix;
- `membership_total` - объекты, попавшие в расчетные группы service/suppression.

Минимальные измерения отчета:

```text
snapshot_id
snapshot_started_at
snapshot_completed_at
cmdbuild_read_started_at
cmdbuild_read_completed_at
zabbix_read_started_at
zabbix_read_completed_at
snapshot_status
operational_delta_warning
layer
source_class
source_superclass
rule_id
template_id
service_object
dimension_key
dimension_value
city/building/custom_dimension
```

Для диагностики нужны drill-down списки:

- должен мониториться, но `zabbix_main_hostid` пустой;
- `zabbix_main_hostid` заполнен, но host не найден в Zabbix;
- host найден, но объект не попал в service membership;
- host найден, но объект не попал в suppression membership;
- объект попал в несколько конфликтующих правил;
- объект исключен фильтром, но выглядит как кандидат на мониторинг.

Срез аудита должен храниться в SQLite как snapshot, чтобы можно было
сравнивать покрытие по дням и после изменений правил.

Snapshot не обязан блокировать потоковую обработку микросервисов. Он читает
текущее состояние систем и фиксирует результат "как получилось на момент
расчета". Если во время расчета часть данных изменилась, это не ошибка
публикации графа, а ограничение точности отчетного среза.

## Конфигурация

Добавить общую секцию во все микросервисы, которым нужен Redis:

```json
"Redis": {
  "Enabled": false,
  "ConnectionString": "127.0.0.1:6379",
  "KeyPrefix": "cmdb2m",
  "InstanceId": "",
  "OperationTtlSeconds": 86400,
  "LockTtlSeconds": 300,
  "LockExtendSeconds": 120,
  "CacheDefaultTtlSeconds": 300,
  "FailureMode": "fallback"
}
```

`FailureMode`:

- `fallback`: использовать local-memory/no-cache поведение;
- `fail`: падать с ошибкой, если Redis недоступен для операций, где Redis
  считается обязательным.

Для production default должен быть `Enabled=false`, пока Redis не настроен
операционно. Для тестового стенда можно включать явно.

Секреты подключения не должны храниться строкой в UI. Для production
используется тот же подход, что и для остальных секретов микросервисов: файл
конфигурации, переменные окружения или PAM/secret provider.

Добавить durable storage и аудит покрытия:

```json
"DurableStore": {
  "Provider": "sqlite",
  "ConnectionString": "Data Source=state/cmdb2m.db",
  "MigrationsEnabled": true
},
"MonitoringCoverageAudit": {
  "Enabled": true,
  "SnapshotRetentionDays": 180,
  "TriggerMode": "manual",
  "DefaultExpectedPolicy": "rules_matched",
  "HostIdAttribute": "zabbix_main_hostid",
  "AllowOperationalDelta": true,
  "MaxOperationalDeltaMinutes": 30,
  "AutoSnapshotAfterFullGraphApply": false,
  "AutoSnapshotAfterScopedReconcile": false,
  "ScheduledSnapshotCron": ""
}
```

`TriggerMode`:

- `manual`: snapshot строится только по кнопке администратора;
- `scheduled`: snapshot строится по расписанию;
- `manual_and_scheduled`: доступны оба режима.

`DefaultExpectedPolicy`:

- `rules_matched`: должен мониториться объект, который попал под правила;
- `class_policy`: должен мониториться объект класса, отмеченного политикой
  мониторинга;
- `explicit_attribute`: должен мониториться объект, у которого включен отдельный
  CMDBuild-атрибут;
- `manual_scope`: аудит строится только по выбранным классам/шаблонам.

В UI показывается только provider, redacted endpoint, состояние подключения и
текущая политика расчета. Строка подключения SQLite задается в конфигурации
микросервиса и не редактируется как секрет в UI.

По умолчанию автоматические snapshot после публикации графов выключены. Их можно
включить позже, но базовая эксплуатационная модель: администратор публикует или
выравнивает граф, затем отдельно запускает срез покрытия.

## Этапы реализации

### Текущий инкремент

Сделано первым шагом:

- добавлены конфигурационные секции `Redis`, `DurableStore` и
  `MonitoringCoverageAudit` в `zabbixconfig2api`;
- `zabbixconfig2api` валидирует эти секции при старте и отдает
  `/runtime-storage/status` с redacted endpoint-ами;
- Monitoring UI показывает блок `Администрирование -> Хранилища и аудит`;
- UI может применять allowlist настроек через существующую панель
  `zabbixconfig2api` и штатный Bearer-protected reload.
- `/runtime-storage/status` уже возвращает счетчики активного membership-state
  backend: target membership, source membership, pending sources, missing host
  bindings, applied graph objects и managed trigger dependencies.
- сохранение membership-state вынесено за `IZabbixApplyStateStorage`;
  доступны `FileZabbixApplyStateStorage` и `SqliteZabbixApplyStateStorage`.
  File backend сохраняет поведение `apply-membership.json`.
- SQLite backend хранит совместимый полный JSON-документ state и нормализованные
  таблицы target memberships, source memberships, pending sources, managed
  trigger dependencies и applied graph objects.
- добавлен dry-run/apply API миграции state. Dry-run считает объем переноса;
  apply для SQLite записывает durable store и проверяет счетчики после чтения.

Остается сделать отдельно: production-режим one-shot coverage snapshot и
завершение workflow dirty scopes после reconcile/apply.

### Этап 1. Общая Redis-инфраструктура

Задачи:

- добавить `RedisOptions`;
- добавить health check `/redis/check` или включить Redis в общий health;
- добавить общий `RedisRuntimeStore` в shared;
- добавить no-op реализацию при `Redis:Enabled=false`;
- добавить конфигурацию в `appsettings.json` микросервисов;
- добавить документацию администратора.

Проверки:

- сервисы стартуют без Redis;
- сервисы стартуют с Redis;
- reload конфигурации перечитывает Redis options там, где это возможно;
- недоступный Redis в `fallback` не ломает текущий поток.

### Этап 2. Distributed Locks

Задачи:

- ввести lock API в shared;
- обернуть Zabbix graph apply;
- обернуть trigger dependencies apply;
- обернуть SLA apply;
- показывать lock conflict в UI понятным сообщением;
- добавить operation id в lock owner.

Проверки:

- два одновременных publish не выполняются параллельно;
- lock освобождается после success/error/cancel;
- зависший lock истекает по TTL;
- повторный запуск после истечения lock возможен.

### Этап 3. Operation Progress в Redis

Задачи:

- вынести `ApplyCurrentRulesProgressStore` в Redis/no-op backend;
- добавить progress store для zabbixconfig2api операций, если требуется;
- UI читает progress после reload страницы;
- cancel flag хранится в Redis.

Проверки:

- progress доступен после обновления страницы;
- progress доступен при нескольких UI/BFF инстансах;
- completed progress исчезает по TTL;
- cancel работает через Redis flag.

### Этап 4. Semantic Dedup в Redis

Задачи:

- расширить `SemanticCommandDeduplicator` Redis backend;
- ключи включают semantic hash, rule/source/event;
- TTL соответствует `SemanticDeduplication:WindowSeconds`;
- fallback на текущую память при отключенном Redis.

Проверки:

- повторное событие в TTL не публикует команду;
- restart сервиса не сбрасывает Redis dedup;
- несколько инстансов не публикуют дубль;
- после TTL событие снова проходит.

### Этап 5. Debounce Dependencies

Задачи:

- перенести debounce flags для trigger dependencies в Redis;
- защитить reconcile lock-ом;
- реализовать pending rerun flag;
- добавить runtime counters в status.

Проверки:

- burst membership updates дает один reconcile;
- если reconcile идет, второй запуск ставит rerun, а не стартует параллельно;
- после завершения выполняется один дополнительный reconcile при наличии pending.

### Этап 6. Lookup Cache

Задачи:

- кэшировать безопасные CMDBuild/Zabbix lookups;
- добавить TTL по типам данных;
- добавить invalidate hooks;
- добавить counters hit/miss/error в status/performance.

Проверки:

- dry-run/apply уменьшают число Zabbix/CMDBuild API calls;
- stale cache не сохраняется после invalidate/full graph apply;
- Redis недоступен: операция продолжает работать без cache.

### Этап 7. Durable State: SQLite

Задачи:

- добавить `DurableStoreOptions`;
- добавить миграции SQLite;
- вынести `apply-membership.json` за интерфейс `MembershipStateStore`;
- реализовать file/sqlite backend;
- добавить мигратор `apply-membership.json -> DurableStore`;
- добавить health/status durable backend.

Проверки:

- restart не теряет membership-state;
- card move удаляет старое membership и создает новое;
- DELETE карточки чистит membership по previous state;
- потеря Redis не влияет на durable membership;
- потеря durable store блокирует delta-публикацию и требует full reconcile.

### Этап 8. Разовые срезы покрытия мониторингом

Задачи:

- добавить модель one-shot snapshot покрытия;
- считать expected/eligible/rules matched/hostid filled/host found/membership;
- добавить drill-down списки проблемных объектов;
- добавить расчет процентов покрытия;
- добавить API для UI;
- добавить экспорт CSV/JSON;
- добавить retention snapshots;
- добавить ручной запуск snapshot из UI;
- добавить опциональный scheduled запуск;
- сохранять started/completed timestamps по CMDBuild и Zabbix;
- показывать предупреждение о допустимой операционной дельте;
- не запускать snapshot автоматически после full graph apply по умолчанию.

Проверки:

- объект без `zabbix_main_hostid` попадает в список "должен мониториться, но не
  поставлен";
- объект с `zabbix_main_hostid`, но без host в Zabbix попадает в отдельный
  список;
- объект с host, но без service membership виден как разрыв сервисной модели;
- проценты считаются отдельно по source class, template, layer и всему контуру;
- snapshots разных дат не перетирают друг друга;
- snapshot сохраняет временные метки чтения CMDBuild/Zabbix;
- изменение данных во время snapshot не считается ошибкой, если включен
  `AllowOperationalDelta`.

## UI изменения

Добавить в `Администрирование -> Микросервисы` read-only/managed блок Redis:

- Redis enabled;
- endpoint без секрета;
- failure mode;
- key prefix;
- lock TTL;
- operation TTL;
- cache TTL;
- health;
- текущие активные locks;
- последние Redis errors.

Для long-running операций показывать:

- operation id;
- owner instance id;
- lock status;
- progress source: memory или Redis;
- предупреждение, если Redis отключен и progress потеряется при restart.

Добавить в `Администрирование -> Хранилища и аудит` блоки:

- Durable storage provider: file/sqlite;
- connection status без секрета;
- migration status;
- membership-state counters;
- monitoring coverage snapshots enabled;
- host id attribute: `zabbix_main_hostid`;
- expected policy;
- snapshot retention;
- trigger mode: manual/scheduled;
- allow operational delta;
- max operational delta;
- кнопка "Рассчитать срез покрытия мониторингом";
- список последних snapshots.

Отдельный отчет `Покрытие мониторингом`:

- статус snapshot: completed/partial/failed;
- время начала и завершения;
- предупреждение об операционной дельте;
- общий процент покрытия;
- таблица по классам;
- таблица по шаблонам/правилам;
- таблица по сервисам;
- таблица по suppression-группам;
- drill-down проблемных объектов;
- экспорт.

## Набор автотестов

Минимальный набор:

- `RedisOptions` parsing and defaults;
- no-op store работает при `Enabled=false`;
- lock acquire/release/owner mismatch/TTL;
- progress write/read/expire/cancel;
- dedup NX+TTL behavior;
- fallback behavior при Redis exception;
- UI regression на Redis settings/status labels.
- `DurableStoreOptions` parsing and defaults;
- migration `apply-membership.json -> sqlite`;
- monitoring coverage snapshot counters;
- coverage drill-down lists;
- snapshot retention cleanup.
- snapshot timing and operational delta flags.

Интеграционные тесты:

- docker Redis в CI/local optional profile;
- two-service-instance dedup scenario;
- parallel Zabbix apply lock scenario;
- progress survives UI/BFF reload.
- manual coverage snapshot on live CMDBuild/Zabbix data;
- object with empty `zabbix_main_hostid` appears in uncovered report;
- object with filled `zabbix_main_hostid` but missing Zabbix host appears in
  broken binding report.

## Критерии приемки

- При `Redis:Enabled=false` поведение системы остается эквивалентным текущему.
- При недоступном Redis в `fallback` сервисы продолжают работу без cache/progress
  гарантий и пишут понятное предупреждение.
- Две параллельные публикации одного Zabbix-графа не выполняются одновременно.
- UI после перезагрузки страницы видит progress операции, если Redis включен.
- Restart микросервиса не сбрасывает Redis-backed dedup в пределах TTL.
- Очистка Redis не удаляет и не создает объекты CMDBuild/Zabbix.
- Очистка Redis не удаляет membership-state и не портит аудит покрытия.
- Durable store содержит актуальное previous/current membership после delta.
- UI показывает процент покрытия мониторингом по классу, шаблону и всему
  контуру для выбранного snapshot, а не как непрерывную online-метрику.
- Объект с пустым `zabbix_main_hostid`, который должен мониториться, виден в
  drill-down отчете.
- Объект с заполненным `zabbix_main_hostid`, но отсутствующим host в Zabbix,
  виден отдельной диагностикой.
- Snapshot явно показывает временные метки чтения CMDBuild/Zabbix и предупреждает
  о возможной операционной дельте.
- Автоматический snapshot после публикации графа выключен по умолчанию.
- Все Redis-ключи имеют prefix окружения, чтобы тестовый и production контуры не
  пересекались.
- Документация администратора описывает включение, отключение, health,
  аварийную очистку locks, последствия потери Redis и восстановление durable
  state.

## Риски

- Redis может стать скрытой обязательной зависимостью. Нужен explicit
  `Enabled=false` default и понятный `FailureMode`.
- Lock bugs могут блокировать публикации. Нужны TTL, owner token и forced
  operator cleanup.
- Cache invalidation может дать устаревшую диагностику. Нужны короткие TTL и
  explicit invalidate.
- Перенос durable state в Redis без AOF/backup может привести к потере
  membership/dependency истории, поэтому Redis не используется как основной
  backend аудита покрытия.
- Неверно выбранная `DefaultExpectedPolicy` может завысить или занизить процент
  покрытия. Политика должна быть явно видна в UI и в каждом snapshot.
- Проверка фактического наличия host в Zabbix может быть дорогой. Нужны batch
  lookup, cache и сохранение snapshot результата.
- Из-за неатомарного чтения CMDBuild/Zabbix snapshot может иметь небольшую
  операционную дельту. Это должно быть явно отражено в статусе snapshot и не
  восприниматься как ошибка потоковой обработки.

## Практический порядок внедрения

Рекомендуемый порядок:

1. Общая Redis-инфраструктура и health.
2. Locks для Zabbix apply/dependencies/SLA.
3. Progress long-running operations.
4. Semantic dedup в Redis.
5. Debounce dependencies.
6. Lookup cache.
7. Durable membership-state на SQLite.
8. Разовые срезы покрытия мониторингом.

Первый полезный production-safe результат достигается уже после этапов 1-2:
параллельные тяжелые публикации перестают конфликтовать, при этом Redis еще не
хранит критичную модельную информацию.

Первый полезный результат по покрытию достигается после этапов 7-8: система
сможет по ручному snapshot показать не только факт публикации графа, но и разрыв
между CMDBuild-объектами, `zabbix_main_hostid`, фактическими host в Zabbix и
membership в service/suppression моделях. Этот отчет не является непрерывной
online-метрикой и допускает небольшую операционную дельту между CMDBuild и
Zabbix.

## Осталось не сделано

Состояние после инкрементов с `Redis`/`DurableStore` настройками,
`/runtime-storage/status`, `IZabbixApplyStateStorage` и dry-run миграции
membership-state.

### 1. SQLite backend для membership-state

Базовый `SQLiteZabbixApplyStateStorage` реализован для
`IZabbixApplyStateStorage`.

Сделано:

- добавлена зависимость `Microsoft.Data.Sqlite`;
- добавлены таблицы для target memberships, source memberships, pending sources,
  managed trigger dependencies и applied graph objects;
- добавлена таблица версии схемы;
- запись выполняется транзакционно;
- сохранена совместимость с текущей JSON-моделью `apply-membership.json`:
  SQLite хранит полный JSON-документ и нормализованные таблицы;
- `/runtime-storage/status` показывает active backend, schema version и
  счетчики SQLite.

Осталось:

- добавить инкрементальные миграции схемы при появлении версии 2+;
- расширить автоматические тесты на чтение/запись SQLite без live Zabbix.

### 2. Миграция `apply-membership.json -> sqlite`

Dry-run миграции считает объем переносимых данных. Apply для SQLite записывает
state в SQLite и валидирует счетчики после чтения.

Нужно:

- сохранить backup исходного файла;
- записывать migration operation id;
- не переключать active backend, если migration validation не прошла;
- добавить rollback/retry инструкцию.

### 3. Dirty scopes в durable store

Первый server-side вариант реализован. UI по-прежнему держит локальный журнал
для непрерывности работы браузера, но при изменениях правил/шаблонов/связей
отправляет dirty scopes в `zabbixconfig2api`. При `DurableStore:Provider=sqlite`
они сохраняются в таблицу `zabbix_dirty_scopes`; при другом provider работает
process-memory fallback.
Реальный Zabbix command apply, graph apply и Kafka apply теперь обновляют
server-side dirty scopes по target managed key: успешный результат переводит
scope в `processed`, ошибка переводит scope в `failed` и сохраняет
`last_reconcile_result`. Dry-run и `pending_manual` dirty scopes не закрывают.
UI показывает только pending/failed scope, а processed остается в durable store
как диагностическая история.
`cmdbconfigbuilder` также помечает dirty scopes из потоковой обработки
webhook/Kafka: после успешной публикации Zabbix-команды в layer-specific
Zabbix topic он вызывает `zabbixconfig2api` `/runtime-storage/dirty-scopes` и
ставит target managed key в `pending`. Ошибка этого вызова логируется, но не
откатывает публикацию команды в Kafka.
UI при пустом поле scope автоматически берет pending server-side dirty scopes
как default для проверки и публикации, но оператор по-прежнему может очистить
поле и явно запустить полный слой. Ручной и автоматический
`dependencies/suppression/apply` закрывают связанные suppression dirty scopes
по aggregate/dependency target managed keys. Если в backend scheduled reconcile
не передан явный scope, `zabbixconfig2api` использует pending dirty scopes как
default scope без участия UI.

`cmdbconfigbuilder` дополнительно помечает dirty scopes для промежуточных
CMDBuild-классов, которые встречаются внутри `source.fields[].cmdbPath`, но не
являются source-классом правила. Например, webhook по `Building` для path
`ARM.Location.Floor.Building.City` не порождает обычную команду по карточке
`ARM`, но может изменить dimension membership; в таком случае dirty scope
ставится по статическим rule/target ключам затронутых правил.

Stale membership cleanup, stale Zabbix service delete и SLA apply закрывают
dirty scopes в том же журнале после реального apply. Dry-run cleanup/SLA не
меняет статусы.

Хранится:

- layer;
- scope type;
- scope key;
- reason;
- created/updated timestamps;
- processing status;
- last reconcile result.

Осталось:

- сузить промежуточные graph/webhook dirty scopes до точного набора source
  карточек, если понадобится более агрессивная оптимизация;
- расширить тот же подход на service dependency reconcile, если сервисный
  backend получит отдельный scheduled reconcile без UI.

### 4. One-shot расчет покрытия мониторингом

Первый read-only вариант реализован: `POST /monitoring-coverage/snapshot`
считает текущий `rules_matched` membership-state, проверяет сохраненные
`zabbix_main_hostid` через Zabbix `host.get` и отдает UI общий процент
заполненности hostid, процент найденных Zabbix hosts, разрез service/suppression
membership и первые drill-down примеры.
Snapshot получает `snapshotId`, сохраняется в SQLite durable store
(`monitoring_coverage_snapshots`) и доступен через
`GET /monitoring-coverage/snapshots`. UI показывает последние сохраненные
snapshot-ы в `Администрирование -> Хранилища и аудит`.

Осталось довести до production-режима:

- читать CMDBuild по выбранному scope, чтобы считать не только уже попавшие в
  membership объекты, но и весь expected inventory;
- читать Zabbix service state батчами для проверки публикации service-графа;
- считать eligible/rules matched/hostid filled/host found/service
  membership/suppression membership отдельно;
- сохранять расширенный CMDBuild/Zabbix timing по каждому источнику данных;
- явно показывать operational delta по CMDBuild и Zabbix отдельно, когда
  появится чтение CMDBuild inventory;
- добавить drill-down:
  - должен мониториться, но `zabbix_main_hostid` пустой;
  - `zabbix_main_hostid` заполнен, но host не найден в Zabbix;
  - host есть, но нет service membership;
  - host есть, но нет suppression membership;
  - объект попал в конфликтующие правила.

### 5. UI-отчет `Покрытие мониторингом`

Первый встроенный отчет добавлен в
`Администрирование -> Хранилища и аудит -> Сформировать срез покрытия`.
В этом же блоке показывается история последних snapshot-ов из durable store.

Осталось:

- вынести в отдельный отчет, если встроенного блока станет недостаточно;
- вынести список snapshots в отдельный отчет с фильтрами;
- добавить разрезы по классам, шаблонам, правилам, сервисам,
  suppression-группам;
- добавить полный drill-down проблемных объектов без лимита первого экрана;
- добавить экспорт CSV/JSON.

### 6. Redis runtime backend-и

Секции настроек Redis уже есть. Добавлен runtime-coordination слой:
`IRuntimeCoordinationStore`, Redis backend и local-memory fallback. Долгие
операции Zabbix graph apply, trigger dependencies и SLA publication уже получают
operation lock и operation progress через этот слой. При `Redis:Enabled=false`
backend - `local-memory`; при `Redis:Enabled=true` и доступном endpoint backend -
`redis`; при недоступном Redis и `FailureMode=fallback` backend -
`local-memory-fallback`; при недоступном Redis и `FailureMode=fail` операции
возвращают `runtime_coordination_unavailable`.
Добавлен `GET /redis/check` и кнопка `Проверить Redis` в UI для проверки
эффективного backend-а без запуска тяжелой операции.
`cmdbconfigbuilder` получил Redis-backed semantic dedup:
`<Redis:KeyPrefix>:semantic-dedup:<sha256(semantic_key)>` с TTL окна
`SemanticDeduplication:WindowSeconds`; при `FailureMode=fallback` сохраняется
in-memory fallback. Для builder добавлен отдельный `GET /redis/check`, BFF route
`/api/rules/redis/check` и кнопка `Проверить Redis builder`, чтобы оператор
видел фактический backend semantic dedup отдельно от runtime coordination
`zabbixconfig2api`. Добавлен `POST /redis/semantic-dedup/check`: endpoint
создает синтетический `AggregationCommandPlan`, проверяет `IsDuplicate=false`,
делает `MarkPublished`, затем проверяет `IsDuplicate=true`. Это диагностирует
тот же Redis/fallback путь, который использует Kafka worker, без публикации
Kafka-сообщений.

Нужно:

- завершить durable snapshots аудита покрытия на SQLite backend.

Проверка на dev-стенде выполнена вручную: временный `cmdbconfigbuilder` с
`Redis:Enabled=true`, `Redis:ConnectionString=127.0.0.1:6379` и отдельным
портом вернул `backend=redis`, `redisAvailable=true` через `/redis/check`.
Добавлен opt-in e2e `tests/redis-runtime-e2e.mjs`, который запускается через
`INTEGRATION_PROFILE=redis ./scripts/test-diagnostics.sh` или
`./scripts/test-integration.sh redis`. Legacy-флаг `LIVE_REDIS=1` сохранен для
обратной совместимости. Он поднимает временные
`cmdbconfigbuilder` и `zabbixconfig2api`, проверяет настоящий Redis backend, а
также поведение недоступного Redis при `FailureMode=fallback` и
`FailureMode=fail`. Для builder e2e дополнительно проверяет
`/redis/semantic-dedup/check`: первый synthetic plan не считается дублем,
повтор после `MarkPublished` считается дублем; при недоступном Redis в fallback
используется in-memory backend, а при `FailureMode=fail` self-check блокируется.
Добавлен первый инфраструктурный слой lookup cache в `zabbixconfig2api`:
`IRuntimeLookupCache`, `RedisRuntimeLookupCache` и local/no-cache fallback.
Статус виден в `/runtime-storage/status` и UI как `Lookup cache`. Ключи
изолированы в `<Redis:KeyPrefix>:cache:<scope>:<hash>`.
Первое подключение cache выполнено для one-shot coverage snapshot:
`POST /monitoring-coverage/snapshot` кэширует положительные Zabbix `host.get`
результаты по scope `zabbix:host`. Missing host-ы не кэшируются, чтобы новый
host в Zabbix был виден уже на следующем snapshot.
Второе подключение выполнено для dry-run trigger dependencies:
`ZabbixTriggerDependencyApplier` кэширует положительные `host.get` и
source-host `trigger.get` результаты в scope `zabbix:host` и
`zabbix:trigger-by-host:{enabled|all}`. Настоящий apply/reconcile cache не
использует и читает Zabbix напрямую, чтобы не применять зависимости на основе
устаревшего состояния trigger dependencies.
Третье подключение выполнено для read-only stale managed service report:
`/apply/state/stale-report` при `IncludeZabbixServices=true` кэширует
положительный `service.get` по layer/limit в scope `zabbix:service-by-layer`.
Cleanup/publication действия cache не используют.

Integration-профили тестов:

- `INTEGRATION_PROFILE=redis ./scripts/test-diagnostics.sh` - offline gates +
  Redis runtime e2e;
- `INTEGRATION_PROFILE=redis-kafka ./scripts/test-diagnostics.sh` - offline
  gates + Redis/Kafka semantic dedup e2e;
- `INTEGRATION_PROFILE=live ./scripts/test-diagnostics.sh` - offline gates +
  live CMDBuild/Zabbix checks;
- `INTEGRATION_PROFILE=all ./scripts/test-diagnostics.sh` или
  `./scripts/test-integration.sh all` - все стендовые профили.

Добавлен streaming Kafka e2e `tests/redis-kafka-dedup-e2e.mjs`. Тест создает
уникальные временные Kafka topics, запускает два `cmdbconfigbuilder` с разными
consumer group id, одинаковым `Redis:KeyPrefix` и одним rule file, отправляет
один `CmdbRawEvent` в общий raw topic и проверяет, что в aggregation topic
появилась ровно одна команда. Для этого Redis semantic dedup теперь делает
короткую atomic reservation через `SET ... NX ... EX` в `IsDuplicate`; после
успешной публикации `MarkPublished` продлевает запись до полного окна
`SemanticDeduplication:WindowSeconds`. Если процесс упал между reservation и
publish, pending reservation истекает за короткий TTL и не становится durable
source of truth.

### 8. Graph webhooks и server-side dirty marking

Нужно отделить object webhooks от graph webhooks.

Graph webhooks помечают dirty scopes для промежуточных объектов, которые
влияют на membership, например:

```text
NTbook -> Room -> Floor -> Building -> City
```

Если изменился `Building.City`, source-карточки не менялись, но membership мог
измениться. В текущей реализации это попадает в server-side dirty scopes как
coarse rule/target scope. Следующая оптимизация, если она понадобится, должна
использовать сохраненный membership-state/path index и ставить dirty scope уже
по точным affected target/source keys.

### 9. Автотесты и regression suite

Нужно добавить проверки:

- SQLite backend сохраняет и читает previous/current membership;
- migration dry-run и apply дают одинаковые счетчики;
- card move пересчитывает старую и новую группу;
- DELETE чистит membership по previous state;
- потеря Redis не ломает durable state;
- потеря durable state блокирует delta publish и требует full reconcile;
- snapshot покрытия корректно классифицирует пустой `zabbix_main_hostid`;
- snapshot покрытия находит hostid без host в Zabbix;
- graph webhook создает dirty scope;
- два параллельных apply блокируются Redis lock.
