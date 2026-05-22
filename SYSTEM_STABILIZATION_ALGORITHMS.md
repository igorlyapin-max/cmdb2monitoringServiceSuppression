# Алгоритмы стабилизации системы

Документ описывает высокоуровневую модель обработки изменений в
CMDBuild/Zabbix контуре. Система не работает как "одно событие равно полный
пересчет всего графа". Базовая модель другая:

```text
одно событие = локальная дельта + отметка зоны возможного рассогласования
```

После первичного полного запуска система переходит в операционный цикл
eventual consistency: потоковые изменения постепенно выравнивают CMDBuild,
membership-state и Zabbix desired graph. Это самостабилизирующаяся модель: она
не гарантирует мгновенную полноту после одного webhook, но каждое изменение и
каждый reconcile уменьшают расхождение между фактическим состоянием и desired
state.

## 1. Обработка измененных карточек

Этот контур отвечает за конкретные source-карточки, которые должны попасть на
мониторинг и в сервисную/подавляющую модель.

Типовой поток:

```text
CMDBuild source card changed
  -> managed webhook
  -> raw Kafka event
  -> cmdbconfigbuilder rule evaluation
  -> canonical aggregation commands
  -> CMDBuild/Zabbix apply
  -> membership-state update
  -> dirty scope
```

Основные шаги:

1. В CMDBuild создается, изменяется или удаляется карточка source-класса:
   `NTbook`, `ARM`, `routerG`, `VPNHUB`, `routerCore` и другие классы схемы.
2. Managed webhook отправляет событие в `cmdbwebhooks2kafka`.
3. Событие публикуется в Kafka raw topic.
4. `cmdbconfigbuilder` читает событие, загружает текущие правила и догружает
   вычисляемые поля:
   - обычные атрибуты карточки;
   - `zabbix_main_hostid`;
   - `cmdbPath` значения, например `Location.Floor.Building.City`;
   - lookup/reference/domain leaf-значения.
5. Conversion rules строят canonical `AggregationCommand` для нужных контуров:
   - CMDBuild aggregation objects and relations;
   - Zabbix service graph membership;
   - Zabbix suppression membership and dependencies.
6. Команды публикуются в соответствующие Kafka topics.
7. Applier-микросервисы применяют изменения:
   - `cmdbaggregation2cmdbuild` создает/обновляет managed CMDBuild объекты и
     связи;
   - `zabbixconfig2api` обновляет Zabbix service graph, source bindings и
     membership-state.
8. После успешной публикации/применения система помечает dirty scope, чтобы
   downstream reconcile понимал, какой участок графа надо перепроверить.

Готовность source-карточки к Zabbix определяется атрибутом
`zabbix_main_hostid`. Если карточка уже есть в CMDBuild, но host еще не создан
или не связан в Zabbix, карточка может оставаться в pending membership. Когда
`zabbix_main_hostid` появляется, следующий webhook или scoped/full reconcile
дообрабатывает карточку.

DELETE обрабатывается идемпотентно: удаляются только ранее записанные managed
membership/source bindings. Если объект не был применен системой, DELETE
считается no-op.

## 2. Целостность и полнота графа

Этот контур отвечает за вопрос: "весь граф в целом консистентен после
множества локальных изменений?"

Граф зависит не только от самой source-карточки. На него влияют:

- правила конвертации;
- population templates;
- связи шаблон-шаблон, шаблон-правило, правило-правило;
- `cmdbPath` через промежуточные CMDBuild объекты;
- lookup/reference/domain значения;
- наличие `zabbix_main_hostid`;
- SLA/service relations;
- stale membership после удаления или перемещения объектов.

Поэтому потоковая обработка не пересобирает весь граф на каждый webhook. Она
фиксирует следы изменения:

- dirty scope по target managed key;
- dirty scope по правилу или шаблону;
- dirty scope по связи;
- dirty scope по промежуточному path-объекту, например `Building`, `Floor`,
  `Room`;
- pending membership, если source-card еще не готова к Zabbix;
- stale membership, если старое состояние больше не соответствует desired
  graph.

Reconcile использует эти следы:

```text
pending dirty scopes
  -> scoped desired graph
  -> compare with membership-state
  -> apply only needed changes
  -> mark dirty scope processed/failed
  -> expose stale/errors to operator
```

Если явный scope не передан, backend scheduled reconcile может брать pending
server-side dirty scopes как scope по умолчанию. Это позволяет выравнивать
накопленные изменения без участия UI.

## 3. Промежуточные CMDBuild объекты

Отдельный случай - изменение карточки, которая сама не является source-card, но
участвует в `cmdbPath`.

Пример:

```text
ARM.Location.Floor.Building.City
```

Если изменился `Building.City`, карточка `ARM` не менялась, обычная команда по
`ARM` может не появиться. Но dimension membership для всех связанных `ARM`
может измениться.

Текущая модель:

```text
Building changed
  -> webhook
  -> no direct source command
  -> coarse dirty scope for affected rules/target keys
  -> later scoped/scheduled reconcile checks affected graph area
```

Coarse dirty scope не вычисляет точный список source-карточек в момент
webhook. Это сделано намеренно: потоковая обработка остается быстрой и не
зависит от тяжелого обратного обхода CMDBuild graph.

Дальнейшее развитие:

- хранить durable path membership index;
- записывать для source-card resolved path value и промежуточные refs;
- при webhook по промежуточной карточке находить точные affected source-cards;
- если индекс отсутствует или устарел, использовать текущий coarse fallback.

## 4. Первичный полный запуск

Полный запуск нужен для построения базового состояния:

1. Применить CMDBuild схему.
2. Создать или обновить правила из шаблонов и связей.
3. Прогнать текущие source-карточки.
4. Создать managed CMDBuild aggregation objects and relations.
5. Опубликовать service graph в Zabbix.
6. Опубликовать suppression membership/dependencies в Zabbix.
7. Сформировать начальный membership-state.
8. Выполнить SLA publication, если SLA уже настроены.
9. Проверить stale managed objects и coverage snapshot.

После этого операционный режим работает дельтами и scheduled/scoped reconcile.

## 5. Операционный цикл

Операционный цикл состоит из повторяющихся локальных изменений:

```text
card changed
  -> local command
  -> membership update
  -> dirty scope
  -> scoped reconcile
  -> dirty scope processed or failed
```

Типовые сценарии:

- новая карточка появилась без `zabbix_main_hostid`: она остается pending;
- позже появился `zabbix_main_hostid`: карточка входит в Zabbix membership;
- карточка переехала в другой город: старый membership становится stale, новый
  membership создается;
- изменился `Building.City`: affected rules получают dirty scope;
- изменилось правило или шаблон: generated rules и связанные scopes требуют
  scoped/full reconcile;
- Zabbix временно недоступен: dirty scope остается pending или failed и виден
  оператору;
- stale cleanup удаляет лишнее и закрывает соответствующие dirty scopes.

## 6. Гарантии и ограничения

Система гарантирует:

- идемпотентность managed commands;
- сохранение membership-state между restart;
- видимость pending/failed dirty scopes;
- возможность full reconcile после крупных изменений;
- постепенное выравнивание графа после потока локальных изменений.

Система не гарантирует:

- мгновенную полноту после одного webhook;
- точное определение всех affected source-cards для промежуточных path-объектов
  без path index;
- автоматическое удаление всех stale объектов без явного cleanup;
- восстановление после потери durable membership-state без full reconcile.

Административный полный прогон остается обязательным инструментом для:

- начальной загрузки;
- массового изменения шаблонов;
- изменения CMDBuild схемы;
- смены path/reference/domain логики;
- восстановления после потери state;
- восстановления после длительной недоступности CMDBuild/Zabbix/Kafka.

## 7. Хорошие практики для стабильности

Эти правила уменьшают размер графа, ускоряют scoped reconcile и снижают риск
stale membership после обычных операционных изменений.

### Предпочитать справочники и ограниченные множества

Если dimension можно выразить через общий lookup, лучше использовать lookup.
Справочник дает предсказуемое число значений, стабильные ключи и понятную
агрегацию.

Хорошие кандидаты:

- город;
- площадка;
- филиал;
- критичность;
- тип оборудования;
- контур;
- бизнес-направление.

Не стоит строить population dimension напрямую по высококардинальным полям:

- hostname;
- serial number;
- inventory number;
- IP address;
- description.

Такие поля допустимы только если они регулярным выражением сводятся к малому и
предсказуемому набору значений.

Для lookup/reference/domain leaf-значений система разделяет стабильную и
отображаемую часть:

```text
stable value  = code/key/id
display value = description/name/label
```

`stable value` используется для логики:

- `dimension.key`;
- `dimension.value` как значение сравнения;
- idempotency key;
- relation matching;
- stale/dirty сравнение;
- membership identity.

`display value` используется только для отображения:

- `dimension.name`;
- target name/description;
- подписи в UI;
- диагностические сообщения.

Изменение display/name lookup не считается структурным изменением модели. Если
lookup `Code=City31` переименовали с `Город 31` на `Москва, филиал Север`, то
managed key остается `City31`. Уже созданные объекты могут показывать старое
имя до следующего scoped/full reconcile или до пересборки затронутого объекта.
Это нормальное поведение eventual consistency.

Плохая практика:

```text
использовать lookup description/name как dimension.key или idempotency key
```

Если бизнесу нужен новый признак, лучше добавить отдельное поле и выполнить
массовую правку source-карточек, чем менять смысл существующего lookup display
name. Для синхронного обновления подписей после косметического переименования
справочника оператор запускает scoped/full reconcile.

### Делать path коротким

Чем короче путь от source-карточки до dimension, тем дешевле пересчет и тем
меньше dirty scopes появляется при изменении промежуточных объектов.

Предпочтительно:

```text
карточка -> Building -> City
```

Допустимо, но дороже:

```text
карточка -> Room -> Floor -> Building -> City
```

Плохо для массовой модели:

```text
карточка -> Room -> Floor -> Building -> District -> Region -> City
```

Если объект по бизнес-смыслу находится в городе или филиале, лучше иметь
короткую reference-связь на город/филиал, чем каждый раз вычислять это через
длинную цепочку помещений.

### Снижать кардинальность связей

Не надо строить модель как плотную сетку, где тысячи объектов потенциально
связаны с тысячами других объектов.

Ориентир:

```text
плохо:      5000 -> 5000
лучше:     5000 -> 500
еще лучше: 5000 -> 50
лучше всего: 5000 -> lookup/справочник с десятками значений
```

Если нужно связать много рабочих мест с большим числом сетевых объектов, лучше
ввести промежуточные агрегаты:

```text
Рабочие места / City31
Маршрутизаторы / City31
ВПН / Region01
```

И связывать агрегаты, а не каждую source-карточку с каждой другой
source-карточкой.

### Использовать стабильные ключи

`dimension.key` должен быть машинно стабильным и редко меняться:

```text
city-31
building-004
branch-msk-02
```

Отображаемое имя может быть человекочитаемым:

```text
City31
Москва, филиал 2
```

Если ключ зависит от названия, которое пользователи часто редактируют,
косметическое переименование будет выглядеть как удаление старого объекта и
создание нового. Это увеличивает stale membership и объем reconcile.

### Использовать regexp только для сжатия множества

Regexp полезен, когда из hostname/code извлекается ограниченный бизнес-признак:

```text
ctest2-NTbook-094 -> NTbook
w31-router-047 -> city-31
```

Плохой regexp порождает почти уникальное значение на каждый объект. Хороший
regexp сводит множество к десяткам или сотням значений, а не к тысячам.

Перед применением regexp-шаблона надо оценить:

- сколько distinct values получится;
- сколько generated rules будет создано;
- сколько managed objects появится;
- сколько stale objects может остаться после изменения regexp.

### Строить модель устойчивыми слоями

Хорошая сервисная или suppression модель обычно строится сверху вниз:

```text
сервис
  -> регион/город/филиал
  -> функциональный слой
  -> агрегат оборудования
  -> source-hosts
```

Не стоит сразу связывать бизнес-сервис с тысячами leaf-объектов, если в модели
есть естественные промежуточные уровни. Промежуточные агрегаты делают граф
понятнее для оператора и дешевле для reconcile.

### Не смешивать разные бизнес-смыслы в одном агрегате

Если один агрегат одновременно означает город, тип оборудования и критичность,
его сложнее сопровождать и объяснять. Лучше явно разделять измерения или
фиксировать композицию ключа:

```text
city-31/router/access
city-31/workplace/critical
```

Название агрегата должно помогать оператору, но связи и reconcile должны
опираться на стабильные ключи, relation types и managed metadata.

### Проверять массовые изменения через dry-run

Любое изменение population field, regexp, key template или relation mapping
может массово переместить объекты. Перед публикацией надо смотреть:

- сколько правил будет создано или обновлено;
- сколько managed objects появится;
- сколько объектов станет stale;
- какие dirty scopes будут затронуты;
- не превышены ли лимиты cardinality/formula/dependencies.

### Сохранять путь к полному восстановлению

Даже при хорошей delta-модели должен оставаться понятный full reconcile:

```text
пересчитать правила
  -> пересобрать membership
  -> опубликовать граф
  -> проверить stale
  -> удалить stale при подтверждении оператора
```

Это страховка после больших изменений схемы, массового изменения шаблонов,
потери durable state, длительной недоступности внешних систем и ручных правок в
Zabbix/CMDBuild.

Короткое правило:

```text
чем меньше кардинальность,
чем короче path,
чем стабильнее ключи,
чем явнее бизнес-смысл связей,
тем быстрее система стабилизируется
и тем реже нужен ручной full reconcile.
```

## 8. Короткая формулировка

Система строит desired state из правил, шаблонов, CMDBuild данных и Zabbix
state. Потоковые webhooks дают локальные дельты и dirty scopes. Backend
reconcile применяет эти dirty scopes, сравнивает desired graph с сохраненным
membership-state и постепенно приводит CMDBuild/Zabbix к консистентному
состоянию. Полный прогон остается операторским механизмом для начальной
загрузки, крупных изменений модели и аварийного восстановления.
