namespace Cmdb2MonitoringServiceSuppression.Shared.CmdbuildSchema;

internal static class Text
{
    public static string ClassName(string code, SchemaLanguage language)
    {
        if (language == SchemaLanguage.En)
        {
            return code;
        }

        return code switch
        {
            "ServiceManagedObject" => "Базовый класс сервисного слоя",
            "ServiceResource" => "Ресурс сервисной модели",
            "ServiceNetworkAccessZone" => "Сервисная зона сетевого доступа",
            "ServiceComputeCluster" => "Сервисный вычислительный кластер",
            "ServiceUserEndpointFleet" => "Сервисный пул рабочих мест",
            "ServiceWorkplaceGroup" => "Сервисная группа рабочих мест",
            "ServicePlatformService" => "Платформенный сервис",
            "ServiceSlaCalendar" => "Календарь SLA сервиса",
            "ServiceSlaPolicy" => "Политика SLA сервиса",
            "ServiceSlaDowntime" => "Регулярное окно исключения SLA",
            "ServiceDatabaseService" => "Сервис базы данных",
            "ServiceStoragePool" => "Сервисный пул хранения",
            "SuppressionManagedObject" => "Базовый класс подавления каскадов",
            "SuppressionResource" => "Ресурс подавления каскадов",
            "SuppressionNetworkAccessZone" => "Зона сети для подавления каскадов",
            "SuppressionComputeCluster" => "Вычислительный кластер для подавления каскадов",
            "SuppressionStoragePool" => "Пул хранения для подавления каскадов",
            "SuppressionProxyGroup" => "Группа proxy для подавления каскадов",
            _ => code
        };
    }

    public static string ClassPurpose(string kind, BuilderLayer layer, SchemaLanguage language)
    {
        if (language == SchemaLanguage.En)
        {
            return kind switch
            {
                "service_managed_object" => "Superclass for service-layer objects managed by the monitoring builder.",
                "suppression_managed_object" => "Superclass for suppression-layer objects managed by the monitoring builder.",
                "service_resource" => "Normalized resource used as a service model participant.",
                "suppression_resource" => "Normalized resource used as a cascade suppression participant.",
                "network_zone" => layer == BuilderLayer.Service
                    ? "Network access aggregation used by the service layer."
                    : "Network access aggregation used by the suppression layer.",
                "compute_cluster" => layer == BuilderLayer.Service
                    ? "Compute aggregation used by the service layer."
                    : "Compute aggregation used by the suppression layer.",
                "endpoint_fleet" => "Fleet of similar user endpoints used for service aggregation.",
                "workplace_group" => "Business group of workplaces used to model service impact.",
                "platform_service" => "Logical platform service used in the Zabbix service tree.",
                "sla_calendar" => "Reusable SLA calendar used by service SLA policies.",
                "sla_policy" => "Reusable SLA policy used by service-layer objects for Zabbix SLA reporting.",
                "sla_downtime" => "Reusable regular SLA downtime window published as managed excluded downtime in Zabbix SLA.",
                "database_service" => "Database abstraction used as an aggregated service dependency.",
                "storage_pool" => layer == BuilderLayer.Service
                    ? "Storage aggregation used by the service layer."
                    : "Storage aggregation used by the suppression layer.",
                "proxy_group" => "Zabbix proxy aggregation used by the suppression layer.",
                _ => "Managed monitoring configuration object."
            };
        }

        return kind switch
        {
            "service_managed_object" => "Суперкласс для объектов сервисного слоя, управляемых builder мониторинга.",
            "suppression_managed_object" => "Суперкласс для объектов слоя подавления каскадов, управляемых builder мониторинга.",
            "service_resource" => "Нормализованный ресурс, участвующий в сервисной модели.",
            "suppression_resource" => "Нормализованный ресурс, участвующий в подавлении каскадов.",
            "network_zone" => layer == BuilderLayer.Service
                ? "Агрегация сетевого доступа для сервисного слоя."
                : "Агрегация сетевого доступа для слоя подавления каскадов.",
            "compute_cluster" => layer == BuilderLayer.Service
                ? "Агрегация вычислительных ресурсов для сервисного слоя."
                : "Агрегация вычислительных ресурсов для слоя подавления каскадов.",
            "endpoint_fleet" => "Пул однотипных рабочих мест для сервисной агрегации.",
            "workplace_group" => "Бизнес-группа рабочих мест для моделирования влияния на сервис.",
            "platform_service" => "Логический платформенный сервис для дерева сервисов Zabbix.",
            "sla_calendar" => "Переиспользуемый календарь SLA для политик SLA сервисного слоя.",
            "sla_policy" => "Переиспользуемая политика SLA для объектов сервисного слоя и отчетности Zabbix SLA.",
            "sla_downtime" => "Переиспользуемое регулярное окно исключения SLA, публикуемое как managed excluded downtime в Zabbix SLA.",
            "database_service" => "Абстракция базы данных как агрегированной сервисной зависимости.",
            "storage_pool" => layer == BuilderLayer.Service
                ? "Агрегация хранения для сервисного слоя."
                : "Агрегация хранения для слоя подавления каскадов.",
            "proxy_group" => "Агрегация Zabbix proxy для слоя подавления каскадов.",
            _ => "Управляемый объект конфигурации мониторинга."
        };
    }

    public static string CustomClassPurpose(BuilderLayer layer, SchemaLanguage language)
    {
        if (language == SchemaLanguage.En)
        {
            return layer == BuilderLayer.Service
                ? "Customer-specific service-layer entity used to build Zabbix service structures."
                : "Customer-specific suppression-layer entity used to build Zabbix trigger dependencies.";
        }

        return layer == BuilderLayer.Service
            ? "Пользовательская сущность сервисного слоя для построения структур сервисов Zabbix."
            : "Пользовательская сущность слоя подавления каскадов для построения trigger dependencies Zabbix.";
    }

    public static string SuggestedDomainReason(string reason, SchemaLanguage language)
    {
        if (language == SchemaLanguage.En)
        {
            return reason switch
            {
                "service_member_of" => "Suggested so service resources can be grouped into the custom service entity.",
                "service_aggregates_to" => "Suggested so the custom entity can be aggregated into the platform service layer.",
                "service_depends_on" => "Suggested so platform services can depend on the custom service entity.",
                "suppression_resource_dependency" => "Suggested so suppression resources can depend on the custom suppression entity.",
                "suppression_network_dependency" => "Suggested to keep the custom suppression entity tied to network access.",
                "suppression_suppresses_resource" => "Suggested so the custom suppression entity can suppress generated suppression resources.",
                _ => "Suggested by schema setup."
            };
        }

        return reason switch
        {
            "service_member_of" => "Предлагается, чтобы сервисные ресурсы можно было включать в пользовательскую сервисную сущность.",
            "service_aggregates_to" => "Предлагается, чтобы пользовательская сущность агрегировалась в уровень платформенного сервиса.",
            "service_depends_on" => "Предлагается, чтобы платформенные сервисы могли зависеть от пользовательской сервисной сущности.",
            "suppression_resource_dependency" => "Предлагается, чтобы ресурсы suppression могли зависеть от пользовательской сущности подавления.",
            "suppression_network_dependency" => "Предлагается, чтобы пользовательская suppression-сущность была связана с сетевым доступом.",
            "suppression_suppresses_resource" => "Предлагается, чтобы пользовательская suppression-сущность могла подавлять сгенерированные ресурсы suppression.",
            _ => "Предложено мастером подготовки схемы."
        };
    }

    public static string ClassHelp(string purpose, SchemaLanguage language)
    {
        return ClassHelp("", purpose, language);
    }

    public static string ClassHelp(string kind, string purpose, SchemaLanguage language)
    {
        var managedNotice = language == SchemaLanguage.En
            ? "This class is managed automatically by the monitoring configuration preparation system. To exclude an object from automated population or change population rules, contact the monitoring owner."
            : "Класс управляется автоматизировано системой подготовки конфигурации мониторинга. Если необходимо исключить объект из автонаполнения или изменить правила наполнения, свяжитесь с ответственным за мониторинг.";

        var detail = kind == "endpoint_fleet"
            ? EndpointFleetHelp(language)
            : kind == "sla_calendar"
                ? SlaCalendarHelp(language)
                : kind == "sla_policy"
                    ? SlaPolicyHelp(language)
                    : kind == "sla_downtime"
                        ? SlaDowntimeHelp(language)
            : "";

        return string.IsNullOrWhiteSpace(detail)
            ? $"{purpose} {managedNotice}"
            : $"{purpose}\n\n{detail}\n\n{managedNotice}";
    }

    private static string SlaPolicyHelp(SchemaLanguage language)
    {
        if (language == SchemaLanguage.En)
        {
            return "Why this class exists: ServiceSlaPolicy stores the authoritative SLA target in CMDBuild instead of hardcoding SLA settings in rules or deriving them directly from customer source cards. Role in the model: service-layer objects link to one SLA policy through the 'has_sla_policy' domain; the policy links to a reusable ServiceSlaCalendar through 'has_sla_calendar' and to regular excluded windows through 'has_regular_downtime'. Zabbix publication can then group services by SLA target, reporting period, calendar, and timezone. What should be included: reusable SLA profiles such as '24x7 monthly 99.9', 'business-hours monthly 99.5', or 'critical yearly 99.95'. Fill sla_target as a percent from 0 to 100, choose reporting_period, link the calendar object when a reusable calendar is required, optionally set the legacy/external calendar code and a stable zabbix_sla_name. What should not be included: concrete endpoint or infrastructure objects; those remain in the service topology classes and only reference this policy. If a service has no contractual SLA, do not link it to a policy.";
        }

        return "Зачем нужен класс: ServiceSlaPolicy хранит авторитетную цель SLA в CMDBuild, а не вшивает SLA в правила и не берет его напрямую из сырых source-карточек заказчика. Роль в модели: объекты сервисного слоя связываются с одной политикой SLA через domain 'has_sla_policy'; политика связывается с переиспользуемым ServiceSlaCalendar через 'has_sla_calendar' и с регулярными окнами исключения через 'has_regular_downtime'. При публикации в Zabbix можно группировать сервисы по цели SLA, отчетному периоду, календарю и таймзоне. Что должно попадать внутрь: переиспользуемые SLA-профили, например '24x7 monthly 99.9', 'рабочее время monthly 99.5' или 'critical yearly 99.95'. Заполняйте sla_target как процент от 0 до 100, выбирайте reporting_period, связывайте календарь отдельным объектом, если нужен переиспользуемый календарь, при необходимости указывайте legacy/внешний код calendar и стабильное zabbix_sla_name. Что не должно попадать внутрь: конкретные endpoint- или инфраструктурные объекты; они остаются в топологических сервисных классах и только ссылаются на эту политику. Если у сервиса нет договорного SLA, не связывайте его с политикой.";
    }

    private static string SlaCalendarHelp(SchemaLanguage language)
    {
        if (language == SchemaLanguage.En)
        {
            return "Why this class exists: ServiceSlaCalendar stores reusable SLA working calendars as CMDBuild objects instead of keeping the calendar only as free text on each SLA policy. Role in the model: SLA policies link to a calendar through the 'has_sla_calendar' domain; several policies can reuse the same calendar while keeping different SLA targets or reporting periods. If a policy is not linked to ServiceSlaCalendar, the SLA publisher treats the calendar as externally/manual managed and does not create or change a managed CMDBuild calendar for that policy. What should be included: stable calendars such as '24x7', 'business days 09:00-18:00 Europe/Moscow', 'customer office calendar', or another bounded schedule that is reused by multiple SLA policies. Fill calendar_code as a stable key and the seven weekday fields monday_hours ... sunday_hours in HH:mm-HH:mm format; leave a weekday empty when SLA time is disabled for that day. Several intervals may be separated with semicolon, for example 09:00-13:00;14:00-18:00. Fill timezone when it differs from the default, and zabbix_calendar_name or external_calendar_id when publication must bind to an existing external calendar. What should not be included: one-time maintenance windows or incident exclusions; those belong to ServiceSlaDowntime or remain manual in Zabbix.";
        }

        return "Зачем нужен класс: ServiceSlaCalendar хранит переиспользуемые рабочие календари SLA как отдельные объекты CMDBuild, а не только свободным текстом в каждой политике SLA. Роль в модели: политики SLA связываются с календарем через domain 'has_sla_calendar'; несколько политик могут использовать один календарь, но иметь разные цели SLA или отчетные периоды. Если политика не связана с ServiceSlaCalendar, публикатор SLA считает календарь внешним/ручным и не создает и не меняет managed-календарь CMDBuild для этой политики. Что должно попадать внутрь: стабильные календари, например '24x7', 'рабочие дни 09:00-18:00 Europe/Moscow', 'календарь офиса заказчика' или другое ограниченное расписание, которое переиспользуется несколькими SLA-политиками. Заполняйте calendar_code как стабильный ключ и семь полей дней недели monday_hours ... sunday_hours в формате HH:mm-HH:mm; оставляйте день пустым, если в этот день SLA-время выключено. Несколько интервалов можно разделять точкой с запятой, например 09:00-13:00;14:00-18:00. Заполняйте timezone если она отличается от умолчания, а zabbix_calendar_name или external_calendar_id если публикация должна привязаться к уже существующему внешнему календарю. Что не должно попадать внутрь: разовые окна обслуживания или аварийные исключения; для них используется ServiceSlaDowntime или ручные downtime в Zabbix.";
    }

    private static string SlaDowntimeHelp(SchemaLanguage language)
    {
        if (language == SchemaLanguage.En)
        {
            return "Why this class exists: ServiceSlaDowntime stores regular contractual maintenance windows in CMDBuild while one-time operational downtimes may remain manual in Zabbix. Role in the model: SLA policies link to these windows through the 'has_regular_downtime' domain; the Zabbix SLA publisher expands the schedule over the configured publication horizon and manages only excluded downtimes with the configured prefix. What should be included: stable recurring exclusions such as weekly maintenance Sunday 02:00 for 120 minutes or monthly patch window on day 15. What should not be included: ad-hoc incident work or emergency windows created by an operator for one concrete service; those can be created manually in Zabbix and are preserved when their name does not use the managed prefix.";
        }

        return "Зачем нужен класс: ServiceSlaDowntime хранит регулярные договорные окна обслуживания в CMDBuild, а разовые операционные downtime могут оставаться ручными в Zabbix. Роль в модели: политики SLA связываются с такими окнами через domain 'has_regular_downtime'; публикатор Zabbix SLA разворачивает расписание на настроенный горизонт публикации и управляет только excluded downtime с настроенным префиксом. Что должно попадать внутрь: стабильные регулярные исключения, например еженедельное обслуживание в воскресенье 02:00 на 120 минут или ежемесячное окно патчинга 15-го числа. Что не должно попадать внутрь: разовые аварийные или операционные окна для конкретного сервиса; их можно создать вручную в Zabbix, и они сохраняются при следующей публикации, если имя не использует managed-префикс.";
    }

    private static string EndpointFleetHelp(SchemaLanguage language)
    {
        if (language == SchemaLanguage.En)
        {
            return "Why this class exists: ServiceUserEndpointFleet groups many similar user endpoints into one service-model object so Zabbix service aggregation does not have to contain hundreds or thousands of individual workplace cards at the same level. Role in the model: it is an intermediate aggregation node between normalized resources and higher-level workplace groups or platform services. The fleet calculates its state from member endpoints by aggregation_type: all, any, threshold, or n_of_m. What should be included: laptops, desktops, thin clients, VDI sessions, kiosks, terminals, or other user endpoint cards that share one service meaning and can be evaluated as one population. Typical grouping keys are location, office, floor, building, city, department, owner group, endpoint type, criticality, operating system, or a rule-selected source class. Examples: 'All NTbook laptops' with threshold 80%; 'Moscow office notebooks' grouped by city and building; 'Call-center thin clients' with n_of_m 20; 'VIP endpoints' marked is_critical=true. What should not be included: servers, network devices, databases, storage pools, or platform components; use the dedicated service classes for those objects. Each managed card must have a stable Code and name, for example Code=NTbookGroup and name='All laptops'.";
        }

        return "Зачем нужен класс: ServiceUserEndpointFleet объединяет множество однотипных пользовательских endpoint-объектов в один объект сервисной модели, чтобы в дерево сервисов Zabbix не попадали сотни или тысячи отдельных рабочих мест на одном уровне. Роль в модели: это промежуточный узел агрегации между нормализованными ресурсами и вышестоящими группами рабочих мест или платформенными сервисами. Состояние пула рассчитывается по дочерним endpoint-объектам через aggregation_type: all, any, threshold или n_of_m. Что должно попадать внутрь: ноутбуки, ПК, тонкие клиенты, VDI-сессии, киоски, терминалы и другие пользовательские endpoint-карточки, которые имеют общий сервисный смысл и могут оцениваться как одна популяция. Типовые ключи группировки: локация, офис, этаж, здание, город, подразделение, группа владельцев, тип endpoint, критичность, ОС или выбранный правилом source-класс. Примеры: 'Все ноутбуки NTbook' с threshold 80%; 'Ноутбуки московского офиса' по городу и зданию; 'Тонкие клиенты call-центра' с n_of_m 20; 'VIP рабочие места' с is_critical=true. Что не должно попадать внутрь: серверы, сетевые устройства, базы данных, storage-пулы и платформенные компоненты; для них используются отдельные сервисные классы. У каждой управляемой карточки должны быть стабильные Code и name, например Code=NTbookGroup и name='Все ноутбуки'.";
    }

    public static string ModelRootSuperclassPurpose(BuilderLayer layer, string rootPath, SchemaLanguage language)
    {
        _ = layer;
        if (language == SchemaLanguage.En)
        {
            return $"Empty superclass for the monitoring model root {rootPath}.";
        }

        return $"Пустой суперкласс корня модели мониторинга {rootPath}.";
    }

    public static string ModelRootSuperclassHelp(BuilderLayer layer, string rootPath, SchemaLanguage language)
    {
        _ = layer;
        if (language == SchemaLanguage.En)
        {
            return $"This empty prototype class materializes the monitoring model root {rootPath}. It separates the monitoring CMDBuild model branch from other classes and must exist before service and suppression managed classes are created. When both layers use the same root path, this superclass is shared and the layer separation starts at ServiceManagedObject and SuppressionManagedObject. This class is managed automatically by the monitoring configuration preparation system.";
        }

        return $"Этот пустой prototype-класс материализует корень модели мониторинга {rootPath}. Он отделяет ветку модели мониторинга от остальных классов CMDBuild и должен существовать до создания управляемых сервисных и suppression-классов. Если оба слоя используют один root path, этот суперкласс общий, а разделение слоев начинается с ServiceManagedObject и SuppressionManagedObject. Класс управляется автоматизировано системой подготовки конфигурации мониторинга.";
    }

    public static string ExistingModelClassPurpose(BuilderLayer layer, string modelRoot, SchemaLanguage language)
    {
        if (language == SchemaLanguage.En)
        {
            return layer == BuilderLayer.Service
                ? $"Existing service-layer managed descendant read from CMDBuild model root {modelRoot}."
                : $"Existing suppression-layer managed descendant read from CMDBuild model root {modelRoot}.";
        }

        return layer == BuilderLayer.Service
            ? $"Существующий управляемый наследник сервисного слоя, считанный из корня модели CMDBuild {modelRoot}."
            : $"Существующий управляемый наследник слоя подавления каскадов, считанный из корня модели CMDBuild {modelRoot}.";
    }

    public static string ExistingModelClassHelp(BuilderLayer layer, string modelRoot, SchemaLanguage language)
    {
        if (language == SchemaLanguage.En)
        {
            return layer == BuilderLayer.Service
                ? $"This class already exists under {modelRoot}, inherits the service managed superclass, and has builder management controls enabled. Population links can be configured for it even if the class was not created by this builder."
                : $"This class already exists under {modelRoot}, inherits the suppression managed superclass, and has builder management controls enabled. Population links can be configured for it even if the class was not created by this builder.";
        }

        return layer == BuilderLayer.Service
            ? $"Класс уже существует в {modelRoot}, наследует управляемый суперкласс сервисного слоя и имеет включенные признаки управления builder. Для него можно настроить связи наполнения, даже если класс создан не этим builder."
            : $"Класс уже существует в {modelRoot}, наследует управляемый суперкласс слоя подавления и имеет включенные признаки управления builder. Для него можно настроить связи наполнения, даже если класс создан не этим builder.";
    }

    public static string SchemaStatusLabel(string status, SchemaLanguage language)
    {
        if (language == SchemaLanguage.En)
        {
            return status switch
            {
                "ready_to_work" => "Ready to work",
                "recommended_to_create" => "Recommended to create",
                _ => status
            };
        }

        return status switch
        {
            "ready_to_work" => "Готовы к работе",
            "recommended_to_create" => "Рекомендовано к созданию",
            _ => status
        };
    }

    public static string DomainName(string relationType, SchemaLanguage language)
    {
        if (language == SchemaLanguage.En)
        {
            return relationType;
        }

        return relationType switch
        {
            "member_of" => "Входит в",
            "aggregates_to" => "Агрегируется в",
            "service_depends_on" => "Сервис зависит от",
            "has_sla_policy" => "Использует SLA",
            "has_sla_calendar" => "Использует календарь SLA",
            "has_regular_downtime" => "Имеет регулярный downtime SLA",
            "populated_from" => "Наполняется из",
            "depends_on_network" => "Зависит от сети",
            "runs_on_compute" => "Размещен на вычислительном ресурсе",
            "depends_on" => "Зависит от",
            "monitored_via" => "Мониторится через",
            _ => relationType
        };
    }

    public static string DomainHelp(string relationType, SchemaLanguage language)
    {
        var baseHelp = language == SchemaLanguage.En
            ? relationType switch
            {
                "member_of" => "Links a resource to an aggregation object.",
                "aggregates_to" => "Links a lower-level aggregate to an upper-level service aggregate.",
                "service_depends_on" => "Defines a service-layer dependency used to build the Zabbix service tree.",
                "has_sla_policy" => "Links a service-layer object to the CMDBuild SLA policy used for Zabbix SLA publication and reporting.",
                "has_sla_calendar" => "Links an SLA policy to the reusable CMDBuild SLA calendar used for Zabbix SLA publication.",
                "has_regular_downtime" => "Links an SLA policy to regular excluded downtime windows managed from CMDBuild.",
                "populated_from" => "Links a managed monitoring object to the existing customer CMDBuild object that populates it.",
                "depends_on_network" => "Defines a suppression dependency on network access.",
                "runs_on_compute" => "Defines a suppression dependency on compute placement.",
                "depends_on" => "Defines a suppression dependency between infrastructure objects.",
                "monitored_via" => "Defines a suppression dependency on the monitoring proxy path.",
                _ => "Managed relation."
            }
            : relationType switch
            {
                "member_of" => "Связывает ресурс с агрегирующим объектом.",
                "aggregates_to" => "Связывает нижестоящий агрегат с вышестоящим сервисным агрегатом.",
                "service_depends_on" => "Задает сервисную зависимость для построения дерева сервисов Zabbix.",
                "has_sla_policy" => "Связывает объект сервисного слоя с политикой SLA в CMDBuild, которая используется для публикации и отчетности Zabbix SLA.",
                "has_sla_calendar" => "Связывает политику SLA с переиспользуемым календарем SLA в CMDBuild для публикации Zabbix SLA.",
                "has_regular_downtime" => "Связывает политику SLA с регулярными окнами исключения, управляемыми из CMDBuild.",
                "populated_from" => "Связывает управляемый объект мониторинга с существующим объектом CMDBuild заказчика, из которого он наполняется.",
                "depends_on_network" => "Задает зависимость подавления каскадов от сетевого доступа.",
                "runs_on_compute" => "Задает зависимость подавления каскадов от вычислительного размещения.",
                "depends_on" => "Задает инфраструктурную зависимость для подавления каскадов.",
                "monitored_via" => "Задает зависимость от пути мониторинга через proxy.",
                _ => "Управляемая связь."
            };

        var deleteHelp = language == SchemaLanguage.En
            ? "The domain must delete the relation when a linked card is deleted so Zabbix structures can be reconciled."
            : "Domain должен удалять связь при удалении связанной карточки, чтобы структуры Zabbix были пересчитаны.";

        return $"{baseHelp} {deleteHelp}";
    }

    public static string AttrName(string code, SchemaLanguage language)
    {
        if (language == SchemaLanguage.En)
        {
            return code;
        }

        return code switch
        {
            "name" => "Наименование",
            "description" => "Описание",
            "is_active" => "Активен",
            "managed_by_builder" => "Управляется builder",
            "auto_population_enabled" => "Автонаполнение включено",
            "population_rule_id" => "ID правила наполнения",
            "population_source_key" => "Ключ источника",
            "last_populated_at" => "Последнее автонаполнение",
            "builder_version" => "Версия builder",
            "zone_id" => "ID зоны",
            "subnet_list" => "Список подсетей",
            "site" => "Площадка",
            "is_critical" => "Критичный",
            "cluster_id" => "ID кластера",
            "is_ha_enabled" => "HA включен",
            "aggregation_type" => "Тип агрегации",
            "threshold" => "Порог",
            "group_id" => "ID группы",
            "location" => "Локация",
            "service_type" => "Тип сервиса",
            "sla_target" => "Цель SLA",
            "reporting_period" => "Отчетный период SLA",
            "calendar" => "Календарь SLA",
            "calendar_code" => "Код календаря SLA",
            "calendar_type" => "Тип календаря SLA",
            "monday_hours" => "Понедельник",
            "tuesday_hours" => "Вторник",
            "wednesday_hours" => "Среда",
            "thursday_hours" => "Четверг",
            "friday_hours" => "Пятница",
            "saturday_hours" => "Суббота",
            "sunday_hours" => "Воскресенье",
            "timezone" => "Таймзона SLA",
            "zabbix_sla_name" => "Имя SLA в Zabbix",
            "zabbix_calendar_name" => "Имя календаря в Zabbix",
            "external_calendar_id" => "Внешний ID календаря",
            "downtime_type" => "Тип окна SLA",
            "schedule_type" => "Расписание окна SLA",
            "start_time" => "Время начала окна",
            "duration_minutes" => "Длительность, минут",
            "day_of_week" => "День недели",
            "day_of_month" => "День месяца",
            "valid_from" => "Действует с",
            "valid_to" => "Действует по",
            "reason" => "Причина",
            "zabbix_downtime_name" => "Имя downtime в Zabbix",
            "n" => "N",
            "storage_type" => "Тип хранения",
            "redundancy_level" => "Уровень резервирования",
            "fallback_supported" => "Поддержан fallback",
            "priority" => "Приоритет",
            "source" => "Источник",
            _ => code
        };
    }

    public static string AttrHelp(string code, SchemaLanguage language)
    {
        if (language == SchemaLanguage.En)
        {
            return code switch
            {
                "name" => "Human-readable object name used in UI and audit output.",
                "description" => "Optional explanation of the object purpose.",
                "is_active" => "Controls whether the object participates in generated Zabbix configuration. When false, the builder keeps the CMDBuild card but excludes this object from the desired Zabbix service/suppression model: it is not created or updated as an active Zabbix structure element, and relations where this object is a source or target are ignored during generation. For service aggregation, inactive children are excluded from all/any/threshold/n-of-m calculations.",
                "managed_by_builder" => "Marks objects managed by automated population.",
                "auto_population_enabled" => "Allows excluding an object from automated population without deleting it.",
                "population_rule_id" => "Conversion rule that produced or last updated this object.",
                "population_source_key" => "Stable source value copied from the customer CMDB card. Automated population writes it when creating or updating the managed target card and uses it to find the same card idempotently without duplicates.",
                "last_populated_at" => "Timestamp of the last automated population update.",
                "builder_version" => "Builder version that produced or last updated this object.",
                "domain_is_active" => "Controls whether this relation participates in generated Zabbix configuration. When false, the builder keeps the CMDBuild relation but excludes this edge from the desired Zabbix model: service hierarchy/dependency edges are not generated and suppression dependency edges are removed or left absent on the next reconciliation.",
                "priority" => "Integer relation rank used to order suppression dependencies and resolve cycles. The value is not a percentage or time interval. Use 1 for the highest priority; larger numbers mean lower priority. Leave empty for the default/lowest priority when no special ordering is needed.",
                "source" => "Identifies the source system or rule that created the relation.",
                "aggregation_type" => "Selects how this managed service or suppression object calculates state from active child objects: all means any child failure can make the group a cause; any means one healthy child is enough; threshold uses the threshold percentage field from 0 to 100; n_of_m uses the absolute count field n. For Zabbix suppression trigger dependencies, every object is represented by one managed aggregate trigger; downstream triggers depend on that aggregate trigger, not on individual source-host triggers. For all and any, threshold and n are ignored.",
                "threshold" => "Percentage threshold from 0 to 100 used only when aggregation_type is threshold. For example, 80 means at least 80% of active child objects must be available. Pay attention to CMDBuild UI regional settings: the decimal separator may be dot or comma. The builder must accept both 80.5 and 80,5 and normalize the value to the dot format expected by Zabbix, for example 80.5.",
                "n" => "Required number of available children for n-of-m aggregation.",
                "service_type" => "Classifies the logical service for Zabbix service tree grouping and reporting: business is a user-facing or SLA service; application is an application or product component; platform is a shared technical platform such as authentication, containers, messaging, virtualization, or middleware; integration is an API, bus, exchange, or data-flow service; infrastructure is a technical dependency surfaced as a service for impact analysis. The value does not change aggregation math; aggregation_type, threshold, and n control state calculation.",
                "sla_target" => "Target availability percentage for SLA reporting over the agreed reporting period. Store values from 0 to 100 as percent, for example 99, 99.5, 99.9, 99.95, or 99.99. Enter 99.9 for 99.9%, not 0.999. Pay attention to CMDBuild UI regional settings: the decimal separator may be dot or comma. The builder must accept both 99.9 and 99,9 and normalize the value to the dot format expected by Zabbix, for example 99.9. Leave empty when the service has no formal SLA. The value is used for reporting and comparison with measured availability; it does not change service state calculation.",
                "reporting_period" => "Reporting window used to group services into Zabbix SLA definitions, for example daily, weekly, monthly, quarterly, or yearly. The value does not change current service state; it controls SLA reporting.",
                "calendar" => "Optional legacy or external SLA calendar code kept on the policy for compatibility. Prefer the 'has_sla_calendar' relation to a ServiceSlaCalendar object when the calendar is managed in CMDBuild and reused by several policies.",
                "calendar_code" => "Stable calendar key used by rules and Zabbix publication, for example 24x7, business-hours-msk, or customer-office-calendar.",
                "calendar_type" => "Optional calendar category, for example 24x7, business, customer, or external. It is descriptive metadata and does not replace weekday hour fields.",
                "calendar_day_hours" => "SLA active time for this weekday. Leave empty when the day is outside the SLA calendar. Use HH:mm-HH:mm format, for example 09:00-18:00. Several intervals may be separated with semicolon: 09:00-13:00;14:00-18:00.",
                "timezone" => "Optional timezone for SLA reporting boundaries, for example Europe/Moscow. Leave empty to use the Zabbix/default reporting timezone.",
                "zabbix_sla_name" => "Optional stable Zabbix SLA display name. When empty, the publisher may derive the name from sla_target, reporting_period, calendar, and timezone.",
                "zabbix_calendar_name" => "Optional stable Zabbix calendar or schedule display name used when publication must bind to an existing Zabbix-side calendar.",
                "external_calendar_id" => "Optional identifier of a calendar managed outside CMDBuild, for example an ITSM or customer calendar id.",
                "downtime_type" => "Type of SLA downtime window. Use regular for recurring CMDBuild-owned maintenance windows.",
                "schedule_type" => "Recurrence unit for expanding managed excluded downtimes into Zabbix: daily, weekly, or monthly.",
                "start_time" => "Local start time in HH:mm format, for example 02:00.",
                "duration_minutes" => "Downtime duration in minutes. Must be a positive integer.",
                "day_of_week" => "Day of week for weekly schedules, 1..7 where 1 is Monday. Leave empty for daily schedules.",
                "day_of_month" => "Day of month for monthly schedules, 1..31. Leave empty for daily or weekly schedules.",
                "valid_from" => "Optional date from which the regular downtime is effective.",
                "valid_to" => "Optional date after which the regular downtime must no longer be published.",
                "reason" => "Human-readable reason included in generated Zabbix downtime names or audit output.",
                "zabbix_downtime_name" => "Optional stable base name for managed Zabbix excluded downtime. The publisher adds the managed prefix to distinguish it from manual one-time downtimes.",
                "is_critical" => "Marks an object whose failure has stronger service impact. The flag does not make an inactive object active and does not create extra topology by itself. It is used by the builder to preserve impact metadata in generated Zabbix structures and to rank root-cause/suppression decisions: a critical object should raise the impact of parent services or suppression analysis more strongly than a non-critical object with the same topology.",
                "fallback_supported" => "Shows whether an alternate proxy or path can be used.",
                _ => $"Stores {code} for generated monitoring configuration."
            };
        }

        return code switch
        {
            "name" => "Человекочитаемое имя объекта для UI и аудита.",
            "description" => "Дополнительное описание назначения объекта.",
            "is_active" => "Определяет, участвует ли объект в генерируемой конфигурации Zabbix. Если false, карточка в CMDBuild сохраняется, но объект исключается из желаемой модели Zabbix service/suppression: он не создается и не обновляется как активный элемент структуры Zabbix, а связи, где он является источником или целью, игнорируются при генерации. В сервисной агрегации неактивные дочерние объекты исключаются из расчетов all/any/threshold/n-of-m.",
            "managed_by_builder" => "Отмечает объекты, управляемые автоматизированным наполнением.",
            "auto_population_enabled" => "Позволяет исключить объект из автонаполнения без удаления.",
            "population_rule_id" => "Правило конвертации, которое создало или обновило объект.",
            "population_source_key" => "Стабильное значение из карточки-источника заказчика. Автоматизированное наполнение записывает его при создании или обновлении целевой карточки и по нему повторно находит тот же объект без дублей.",
            "last_populated_at" => "Время последнего автоматизированного обновления объекта.",
            "builder_version" => "Версия builder, создавшая или обновившая объект.",
            "domain_is_active" => "Определяет, участвует ли связь в генерируемой конфигурации Zabbix. Если false, связь в CMDBuild сохраняется, но ребро исключается из желаемой модели Zabbix: сервисная иерархия/зависимость не генерируется, а suppression dependency удаляется или остается отсутствующей при ближайшей сверке.",
            "priority" => "Целочисленный ранг связи для упорядочивания suppression-зависимостей и разрыва циклов. Значение не является процентом или временем. 1 - самый высокий приоритет; чем больше число, тем ниже приоритет. Пустое значение означает обычный/самый низкий приоритет, если специальный порядок не нужен.",
            "source" => "Указывает систему или правило, создавшее связь.",
            "aggregation_type" => "Выбирает, как managed service или suppression-объект рассчитывает состояние по активным дочерним объектам: all - отказ любого дочернего объекта может сделать группу причиной; any - достаточно одного исправного дочернего объекта; threshold - используется процентный порог в поле threshold от 0 до 100; n_of_m - используется абсолютное количество в поле n. Для trigger dependencies подавления в Zabbix каждый объект получает один managed aggregate trigger; downstream triggers зависят от него, а не от отдельных triggers source-хостов. Для all и any поля threshold и n не используются.",
            "threshold" => "Процентный порог от 0 до 100, используется только когда aggregation_type = threshold. Например, 80 означает, что должно быть доступно не меньше 80% активных дочерних объектов. Обратите внимание на региональные настройки UI CMDBuild: десятичный разделитель может быть точкой или запятой. Builder должен принимать оба формата, например 80.5 и 80,5, и нормализовать значение в формат с точкой, ожидаемый Zabbix: 80.5.",
            "n" => "Требуемое количество доступных дочерних объектов для n-of-m агрегации.",
            "service_type" => "Классифицирует логический сервис для группировки и отчетности в дереве сервисов Zabbix: business - пользовательский или SLA-сервис верхнего уровня; application - приложение или продуктовый компонент; platform - общая техническая платформа, например аутентификация, контейнерная платформа, брокер сообщений, виртуализация или middleware; integration - API, шина, обмен или поток данных между системами; infrastructure - техническая зависимость, которую нужно показать как сервис для анализа влияния. Значение не меняет расчет состояния; за расчет отвечают aggregation_type, threshold и n.",
            "sla_target" => "Целевая доступность для SLA-отчетности за согласованный отчетный период. Заполняется как процент от 0 до 100: например 99, 99.5, 99.9, 99.95 или 99.99. Для 99.9% нужно вводить 99.9, а не 0.999. Обратите внимание на региональные настройки UI CMDBuild: десятичный разделитель может быть точкой или запятой. Builder должен принимать оба формата, например 99.9 и 99,9, и нормализовать значение в формат с точкой, ожидаемый Zabbix: 99.9. Оставьте пустым, если у сервиса нет формального SLA. Значение используется для отчетности и сравнения с фактической доступностью; оно не меняет расчет текущего состояния сервиса.",
            "reporting_period" => "Отчетное окно, по которому сервисы группируются в определения Zabbix SLA: daily, weekly, monthly, quarterly или yearly. Значение не меняет текущее состояние сервиса; оно управляет SLA-отчетностью.",
            "calendar" => "Необязательный legacy или внешний код календаря SLA, оставленный в политике для совместимости. Если календарь управляется в CMDBuild и переиспользуется несколькими политиками, предпочтительно связывать политику с объектом ServiceSlaCalendar через 'has_sla_calendar'.",
            "calendar_code" => "Стабильный ключ календаря для правил и публикации в Zabbix, например 24x7, business-hours-msk или customer-office-calendar.",
            "calendar_type" => "Необязательная категория календаря, например 24x7, business, customer или external. Это описательная метаинформация, она не заменяет поля часов по дням недели.",
            "calendar_day_hours" => "Активное SLA-время для этого дня недели. Оставьте пустым, если день не входит в SLA-календарь. Формат HH:mm-HH:mm, например 09:00-18:00. Несколько интервалов разделяются точкой с запятой: 09:00-13:00;14:00-18:00.",
            "timezone" => "Необязательная таймзона для границ SLA-отчетности, например Europe/Moscow. Оставьте пустым, чтобы использовать таймзону Zabbix/по умолчанию.",
            "zabbix_sla_name" => "Необязательное стабильное отображаемое имя SLA в Zabbix. Если пусто, публикатор может сформировать имя из sla_target, reporting_period, calendar и timezone.",
            "zabbix_calendar_name" => "Необязательное стабильное имя календаря или расписания в Zabbix, если публикация должна привязаться к уже существующему календарю на стороне Zabbix.",
            "external_calendar_id" => "Необязательный идентификатор календаря, которым управляют вне CMDBuild, например ID календаря ITSM или заказчика.",
            "downtime_type" => "Тип окна SLA. Для регулярных окон обслуживания, которыми владеет CMDBuild, используйте regular.",
            "schedule_type" => "Единица повторения для разворачивания managed excluded downtime в Zabbix: daily, weekly или monthly.",
            "start_time" => "Локальное время начала в формате HH:mm, например 02:00.",
            "duration_minutes" => "Длительность окна в минутах. Должно быть положительным целым числом.",
            "day_of_week" => "День недели для еженедельного расписания, 1..7, где 1 - понедельник. Для ежедневного расписания оставьте пустым.",
            "day_of_month" => "День месяца для ежемесячного расписания, 1..31. Для ежедневного или еженедельного расписания оставьте пустым.",
            "valid_from" => "Необязательная дата, с которой регулярный downtime начинает действовать.",
            "valid_to" => "Необязательная дата, после которой регулярный downtime больше не должен публиковаться.",
            "reason" => "Человекочитаемая причина, попадающая в имена downtime Zabbix или аудит.",
            "zabbix_downtime_name" => "Необязательная стабильная основа имени managed excluded downtime в Zabbix. Публикатор добавляет managed-префикс, чтобы отличать такие окна от ручных разовых downtime.",
            "is_critical" => "Отмечает объект с усиленным влиянием на сервис. Признак не делает неактивный объект активным и сам по себе не создает дополнительные связи. Builder использует его как impact-метаданные в генерируемых структурах Zabbix и при ранжировании первопричины/подавления: критичный объект должен сильнее повышать влияние на родительские сервисы или анализ suppression, чем некритичный объект с такой же топологией.",
            "fallback_supported" => "Показывает, поддерживается ли резервный proxy или путь.",
            _ => $"Хранит {code} для генерируемой конфигурации мониторинга."
        };
    }

    public static string LookupName(string code, SchemaLanguage language)
    {
        if (language == SchemaLanguage.En)
        {
            return code switch
            {
                "ServiceAggregationType" => "Service aggregation type",
                "ServiceType" => "Service type",
                "ServiceSlaReportingPeriod" => "Service SLA reporting period",
                "ServiceSlaDowntimeType" => "Service SLA downtime type",
                "ServiceSlaDowntimeSchedule" => "Service SLA downtime schedule",
                _ => code
            };
        }

        return code switch
        {
            "ServiceAggregationType" => "Тип сервисной агрегации",
            "ServiceType" => "Тип сервиса",
            "ServiceSlaReportingPeriod" => "Отчетный период SLA сервиса",
            "ServiceSlaDowntimeType" => "Тип downtime SLA сервиса",
            "ServiceSlaDowntimeSchedule" => "Расписание downtime SLA сервиса",
            _ => code
        };
    }

    public static string LookupValueName(string lookupCode, string valueCode, SchemaLanguage language)
    {
        if (lookupCode == "ServiceSlaReportingPeriod")
        {
            if (language == SchemaLanguage.En)
            {
                return valueCode switch
                {
                    "daily" => "Daily",
                    "weekly" => "Weekly",
                    "monthly" => "Monthly",
                    "quarterly" => "Quarterly",
                    "yearly" => "Yearly",
                    _ => valueCode
                };
            }

            return valueCode switch
            {
                "daily" => "Ежедневно",
                "weekly" => "Еженедельно",
                "monthly" => "Ежемесячно",
                "quarterly" => "Ежеквартально",
                "yearly" => "Ежегодно",
                _ => valueCode
            };
        }

        if (lookupCode == "ServiceSlaDowntimeType")
        {
            if (language == SchemaLanguage.En)
            {
                return valueCode switch
                {
                    "regular" => "Regular",
                    _ => valueCode
                };
            }

            return valueCode switch
            {
                "regular" => "Регулярное",
                _ => valueCode
            };
        }

        if (lookupCode == "ServiceSlaDowntimeSchedule")
        {
            if (language == SchemaLanguage.En)
            {
                return valueCode switch
                {
                    "daily" => "Daily",
                    "weekly" => "Weekly",
                    "monthly" => "Monthly",
                    _ => valueCode
                };
            }

            return valueCode switch
            {
                "daily" => "Ежедневно",
                "weekly" => "Еженедельно",
                "monthly" => "Ежемесячно",
                _ => valueCode
            };
        }

        if (lookupCode == "ServiceType")
        {
            if (language == SchemaLanguage.En)
            {
                return valueCode switch
                {
                    "business" => "Business service",
                    "application" => "Application service",
                    "platform" => "Platform service",
                    "integration" => "Integration service",
                    "infrastructure" => "Infrastructure service",
                    _ => valueCode
                };
            }

            return valueCode switch
            {
                "business" => "Бизнес-сервис",
                "application" => "Прикладной сервис",
                "platform" => "Платформенный сервис",
                "integration" => "Интеграционный сервис",
                "infrastructure" => "Инфраструктурный сервис",
                _ => valueCode
            };
        }

        if (lookupCode != "ServiceAggregationType")
        {
            return valueCode;
        }

        if (language == SchemaLanguage.En)
        {
            return valueCode switch
            {
                "all" => "All children",
                "any" => "Any child",
                "threshold" => "Threshold",
                "n_of_m" => "N of M",
                _ => valueCode
            };
        }

        return valueCode switch
        {
            "all" => "Все дочерние",
            "any" => "Любой дочерний",
            "threshold" => "Порог",
            "n_of_m" => "N из M",
            _ => valueCode
        };
    }

    public static string LookupValueHelp(string lookupCode, string valueCode, SchemaLanguage language)
    {
        if (lookupCode == "ServiceSlaReportingPeriod")
        {
            if (language == SchemaLanguage.En)
            {
                return valueCode switch
                {
                    "daily" => "Use for SLA reports where the target is evaluated per day.",
                    "weekly" => "Use for SLA reports where the target is evaluated per week.",
                    "monthly" => "Use for the common monthly SLA reporting window.",
                    "quarterly" => "Use for quarterly contractual SLA reporting.",
                    "yearly" => "Use for annual SLA reporting.",
                    _ => valueCode
                };
            }

            return valueCode switch
            {
                "daily" => "Используется, когда цель SLA оценивается за день.",
                "weekly" => "Используется, когда цель SLA оценивается за неделю.",
                "monthly" => "Типовое месячное окно SLA-отчетности.",
                "quarterly" => "Используется для квартальной договорной SLA-отчетности.",
                "yearly" => "Используется для годовой SLA-отчетности.",
                _ => valueCode
            };
        }

        if (lookupCode == "ServiceSlaDowntimeType")
        {
            if (language == SchemaLanguage.En)
            {
                return valueCode switch
                {
                    "regular" => "Recurring maintenance window owned by CMDBuild and expanded by the Zabbix SLA publisher.",
                    _ => valueCode
                };
            }

            return valueCode switch
            {
                "regular" => "Повторяющееся окно обслуживания, которым владеет CMDBuild и которое разворачивает публикатор Zabbix SLA.",
                _ => valueCode
            };
        }

        if (lookupCode == "ServiceSlaDowntimeSchedule")
        {
            if (language == SchemaLanguage.En)
            {
                return valueCode switch
                {
                    "daily" => "Publish one managed excluded downtime per day inside the configured horizon.",
                    "weekly" => "Publish one managed excluded downtime for the configured day of week inside the horizon.",
                    "monthly" => "Publish one managed excluded downtime for the configured day of month inside the horizon.",
                    _ => valueCode
                };
            }

            return valueCode switch
            {
                "daily" => "Публикует одно managed excluded downtime на каждый день в пределах настроенного горизонта.",
                "weekly" => "Публикует managed excluded downtime для указанного дня недели в пределах горизонта.",
                "monthly" => "Публикует managed excluded downtime для указанного дня месяца в пределах горизонта.",
                _ => valueCode
            };
        }

        if (lookupCode == "ServiceType")
        {
            if (language == SchemaLanguage.En)
            {
                return valueCode switch
                {
                    "business" => "Use for a customer-visible service or SLA object. It usually sits near the top of the service tree and represents business impact.",
                    "application" => "Use for an application, product module, or user-facing functional component that aggregates platform and infrastructure dependencies.",
                    "platform" => "Use for a shared technical platform such as authentication, containers, messaging, virtualization, middleware, or a common runtime used by several applications.",
                    "integration" => "Use for an API, integration bus, exchange, job chain, or data-flow service where availability means systems can communicate.",
                    "infrastructure" => "Use when an infrastructure dependency must be visible as a service object for impact analysis, ownership, or SLA reporting.",
                    _ => valueCode
                };
            }

            return valueCode switch
            {
                "business" => "Используется для пользовательского сервиса или SLA-объекта. Обычно находится близко к верхнему уровню дерева сервисов и показывает бизнес-влияние.",
                "application" => "Используется для приложения, продуктового модуля или функционального компонента, который агрегирует платформенные и инфраструктурные зависимости.",
                "platform" => "Используется для общей технической платформы: аутентификации, контейнерной платформы, брокера сообщений, виртуализации, middleware или общего runtime для нескольких приложений.",
                "integration" => "Используется для API, интеграционной шины, обмена, цепочки заданий или потока данных, где доступность означает возможность обмена между системами.",
                "infrastructure" => "Используется, когда инфраструктурную зависимость нужно показать как сервисный объект для анализа влияния, ответственности или SLA-отчетности.",
                _ => valueCode
            };
        }

        if (lookupCode != "ServiceAggregationType")
        {
            return valueCode;
        }

        if (language == SchemaLanguage.En)
        {
            return valueCode switch
            {
                "all" => "Use when every child component is mandatory. The service is available only when all active child objects are available; threshold and n are ignored.",
                "any" => "Use for redundant or fallback groups where one child is enough. The service is available when at least one active child object is available; threshold and n are ignored.",
                "threshold" => "Use when the service can tolerate partial degradation. Fill threshold as a percentage from 0 to 100; n is ignored.",
                "n_of_m" => "Use when a fixed minimum number of children must be available. Fill n as the required active-child count; threshold is ignored.",
                _ => valueCode
            };
        }

        return valueCode switch
        {
            "all" => "Используется, когда каждый дочерний компонент обязателен. Сервис доступен только когда доступны все активные дочерние объекты; threshold и n не используются.",
            "any" => "Используется для резервированных групп или fallback-сценариев, где достаточно одного дочернего объекта. Сервис доступен когда доступен хотя бы один активный дочерний объект; threshold и n не используются.",
            "threshold" => "Используется, когда сервис допускает частичную деградацию. Заполните threshold как процент от 0 до 100; n не используется.",
            "n_of_m" => "Используется, когда должно быть доступно фиксированное минимальное количество дочерних объектов. Заполните n как требуемое число активных дочерних объектов; threshold не используется.",
            _ => valueCode
        };
    }

    public static string ModelRootHelp(BuilderLayer layer, SchemaLanguage language)
    {
        if (language == SchemaLanguage.En)
        {
            return layer == BuilderLayer.Service
                ? "CMDBuild navigation root where the service-layer class model is read from. Default is /Monitoring."
                : "CMDBuild navigation root where the cascade-suppression class model is read from. Default is /Monitoring.";
        }

        return layer == BuilderLayer.Service
            ? "Корень навигации CMDBuild, откуда читается модель классов сервисного слоя. По умолчанию /Мониторинг."
            : "Корень навигации CMDBuild, откуда читается модель классов слоя подавления каскадов. По умолчанию /Мониторинг.";
    }

}
