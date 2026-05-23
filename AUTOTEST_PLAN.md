# Autotest Plan

Этот план фиксирует, какие проверки должны оставаться автоматизированными. Обычный прогон не зависит от живого стенда и запускается отдельной командой:

```bash
./scripts/test-diagnostics.sh
```

Совместимый gate для конфигураций и правил:

```bash
./scripts/test-configs.sh
```

Он намеренно запускает тот же офлайн-набор, чтобы изменение схемы, UI, правил, шаблонов, runtime-state или Zabbix-публикации не проходило без contract-проверок.

## Профили

- `offline`: синтаксис UI, валидация конфигурации, UI-regression contracts, contract покрытия этого плана, сборка и запуск `tests/sharedcontracts`.
- `live`: офлайн-набор плюс проверка доступности CMDBuild/Zabbix и статуса Kafka auto-apply на стенде.
- `redis`: офлайн-набор плюс Redis runtime e2e для режимов `fail`, `fallback` и lookup-cache.
- `redis-kafka`: офлайн-набор плюс Kafka/Redis semantic dedup e2e.
- `all`: все проверки выше.

Запуск профилей:

```bash
./scripts/test-integration.sh live
./scripts/test-integration.sh redis
./scripts/test-integration.sh redis-kafka
./scripts/test-integration.sh all
```

## Обязательные группы проверок

1. UI и операторский workflow: единый workspace модели, контроль модели, подготовка правил, публикация Zabbix, отсутствие старых прямых меню.
2. Конфигурация и схема: генерация классов/доменов, SLA-классы, универсальные сервисные и suppression-домены, настройки микросервисов.
3. Правила и шаблоны: материализация шаблонов, версии, detached/generated rules, планы удаления, связи шаблон-шаблон, шаблон-правило и правило-правило.
4. CMDBuild streaming: semantic dedup, path/reference/domain traversal, lookup handling, stale membership после смены dimension/source key.
5. Сервисная публикация Zabbix: desired graph, root/orphan detection, scoped/full publication, SLA dry-run/apply.
6. Suppression публикация Zabbix: membership-state, aggregate triggers, trigger dependencies, transitive group depth, scoped reconcile.
7. Redis и durable state: Redis runtime coordination, lookup cache, SQLite membership-state, dirty scopes, migration dry-run/apply.
8. Kafka/e2e hooks: raw webhook topic, независимые consumer group, semantic dedup, публикация dirty scopes.
9. Live smoke: CMDBuild, Zabbix, Kafka auto-apply, ручные профили без зависимости обычного офлайн-прогона от стенда.
10. Производительность и safety limits: timeout, batch size, `MaxDependenciesPerRun`, `MaxSourceHostsPerAggregate`, `MaxAggregateFormulaLength`, предупреждения по сложности aggregate formula.

Файл `tests/autotest-plan-contracts.mjs` проверяет, что эти группы представлены в текущих скриптах и contract-тестах. Он не заменяет функциональные тесты, а защищает сам состав автотестового набора от случайного удаления.
