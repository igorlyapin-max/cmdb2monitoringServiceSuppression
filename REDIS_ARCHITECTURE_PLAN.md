# Redis Architecture Plan

## Цель

Redis добавляется как optional runtime-инфраструктура для ускорения и
координации работы микросервисов. Он не становится источником истины для правил,
шаблонов, CMDBuild-схемы, CMDBuild master data или Zabbix desired graph.

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

## Этапы реализации

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

### Этап 7. Решение по durable state

Отдельное архитектурное решение после стабилизации предыдущих этапов.

Варианты:

- оставить `apply-membership.json` как durable state;
- перейти на SQLite/PostgreSQL;
- использовать Redis с AOF/RDB только при наличии эксплуатационных гарантий.

Критерии выбора:

- backup/restore;
- atomic updates;
- observability;
- размер state;
- поведение при crash во время apply;
- требования к multi-instance.

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

## Набор автотестов

Минимальный набор:

- `RedisOptions` parsing and defaults;
- no-op store работает при `Enabled=false`;
- lock acquire/release/owner mismatch/TTL;
- progress write/read/expire/cancel;
- dedup NX+TTL behavior;
- fallback behavior при Redis exception;
- UI regression на Redis settings/status labels.

Интеграционные тесты:

- docker Redis в CI/local optional profile;
- two-service-instance dedup scenario;
- parallel Zabbix apply lock scenario;
- progress survives UI/BFF reload.

## Критерии приемки

- При `Redis:Enabled=false` поведение системы остается эквивалентным текущему.
- При недоступном Redis в `fallback` сервисы продолжают работу без cache/progress
  гарантий и пишут понятное предупреждение.
- Две параллельные публикации одного Zabbix-графа не выполняются одновременно.
- UI после перезагрузки страницы видит progress операции, если Redis включен.
- Restart микросервиса не сбрасывает Redis-backed dedup в пределах TTL.
- Очистка Redis не удаляет и не создает объекты CMDBuild/Zabbix.
- Все Redis-ключи имеют prefix окружения, чтобы тестовый и production контуры не
  пересекались.
- Документация администратора описывает включение, отключение, health,
  аварийную очистку locks и последствия потери Redis.

## Риски

- Redis может стать скрытой обязательной зависимостью. Нужен explicit
  `Enabled=false` default и понятный `FailureMode`.
- Lock bugs могут блокировать публикации. Нужны TTL, owner token и forced
  operator cleanup.
- Cache invalidation может дать устаревшую диагностику. Нужны короткие TTL и
  explicit invalidate.
- Перенос durable state в Redis без AOF/backup может привести к потере
  membership/dependency истории.

## Практический порядок внедрения

Рекомендуемый порядок:

1. Общая Redis-инфраструктура и health.
2. Locks для Zabbix apply/dependencies/SLA.
3. Progress long-running operations.
4. Semantic dedup в Redis.
5. Debounce dependencies.
6. Lookup cache.
7. Отдельное решение по durable state.

Первый полезный production-safe результат достигается уже после этапов 1-2:
параллельные тяжелые публикации перестают конфликтовать, при этом Redis еще не
хранит критичную модельную информацию.
