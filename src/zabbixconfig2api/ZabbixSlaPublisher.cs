using System.Globalization;
using Cmdb2MonitoringServiceSuppression.Shared.Integrations;
using Microsoft.Extensions.Options;

public sealed class ZabbixSlaPublisher(
    CmdbuildClient cmdbuildClient,
    ZabbixClient zabbixClient,
    IOptionsMonitor<ZabbixSlaOptions> options,
    ILogger<ZabbixSlaPublisher> logger)
{
    private const string Layer = "service";
    private const int WeekSeconds = 7 * 24 * 60 * 60;

    public async Task<ZabbixSlaPublishResult> RunAsync(
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var currentOptions = options.CurrentValue;
        var result = new ZabbixSlaPublishResult
        {
            DryRun = dryRun,
            Enabled = currentOptions.Enabled,
            Status = currentOptions.Enabled ? "running" : "disabled",
            DefaultPolicyKey = currentOptions.DefaultPolicyKey,
            DowntimePublicationHorizonMonths = currentOptions.DowntimePublicationHorizonMonths,
            ManagedExcludedDowntimePrefix = currentOptions.ManagedExcludedDowntimePrefix
        };

        if (!currentOptions.Enabled)
        {
            return result with
            {
                Status = "skipped",
                Message = "ZabbixSla:Enabled=false; публикация SLA пропущена."
            };
        }

        try
        {
            var catalog = await cmdbuildClient.ListManagedClassInstancesAsync(
                currentOptions.CmdbuildPrefix,
                currentOptions.ServiceRootPath,
                suppressionRootPath: null,
                cancellationToken);
            var relations = await cmdbuildClient.ListDomainRelationsAsync(
                currentOptions.CmdbuildPrefix,
                cancellationToken);
            var context = BuildContext(catalog.Classes, relations.Relations, currentOptions);
            var plan = BuildPlan(context, currentOptions);
            var serviceTopology = await ResolveServiceTopologyAsync(plan.Services, cancellationToken);
            var topologyErrors = ServiceTopologyErrors(serviceTopology).ToArray();
            var allErrors = plan.Errors.Concat(topologyErrors).ToArray();
            result = result with
            {
                CmdbuildServiceClasses = context.ServiceClasses,
                CmdbuildServicesScanned = context.ServiceCards.Count,
                PolicyCount = context.Policies.Count,
                CalendarCount = context.Calendars.Count,
                DowntimeCount = context.Downtimes.Count,
                ServiceCandidates = plan.Services.Count,
                TopologyServicesFound = serviceTopology.Count(service => service.ZabbixServiceFound),
                TopologyServicesMissing = serviceTopology.Count(service => !service.ZabbixServiceFound),
                TopologyServicesWithoutLinks = serviceTopology.Count(service => service.ZabbixServiceFound && !service.ZabbixServiceHasTopology),
                SlasPlanned = plan.Slas.Count,
                ServiceActions = serviceTopology
                    .GroupBy(service => service.Action, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
                SlaActions = plan.Slas
                    .GroupBy(sla => sla.Action, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
                SampleServices = serviceTopology.Take(currentOptions.SampleLimit).ToArray(),
                SampleSlas = plan.Slas.Take(currentOptions.SampleLimit).ToArray(),
                Warnings = context.Warnings.Concat(plan.Warnings).Take(currentOptions.SampleLimit).ToArray()
            };

            if (dryRun)
            {
                return result with
                {
                    Status = allErrors.Length > 0 ? "blocked" : "ok",
                    Errors = allErrors.Take(currentOptions.SampleLimit).ToArray(),
                    Message = allErrors.Length > 0
                        ? $"Dry-run SLA завершен с блокирующими замечаниями: {allErrors.Length}. Сначала опубликуйте сервисную модель в Zabbix."
                        : $"Dry-run SLA завершен: сервисов к маркировке {serviceTopology.Count}, SLA к публикации {plan.Slas.Count}."
                };
            }

            if (allErrors.Length > 0)
            {
                return result with
                {
                    Status = "error",
                    Errors = allErrors.Take(currentOptions.SampleLimit).ToArray(),
                    Message = $"SLA не опубликованы: сначала опубликуйте сервисную модель в Zabbix и исправьте ошибки плана ({allErrors.Length})."
                };
            }

            var serviceResults = new List<ZabbixSlaServiceApplySample>();
            foreach (var service in serviceTopology)
            {
                var apply = await zabbixClient.ApplyManagedServiceTagsAsync(service.Definition, cancellationToken);
                serviceResults.Add(service with
                {
                    Action = apply.Action,
                    ServiceId = apply.ServiceId,
                    Definition = service.Definition
                });
            }

            var slaResults = new List<ZabbixSlaApplySample>();
            foreach (var sla in plan.Slas)
            {
                var apply = await zabbixClient.ApplySlaAsync(sla.Definition, cancellationToken);
                slaResults.Add(sla with
                {
                    Action = apply.Action,
                    SlaId = apply.SlaId,
                    ManagedExcludedDowntimeCount = apply.ManagedExcludedDowntimes,
                    PreservedManualExcludedDowntimeCount = apply.PreservedManualExcludedDowntimes,
                    Definition = sla.Definition
                });
            }

            return result with
            {
                Status = "ok",
                ServicesApplied = serviceResults.Count,
                SlasApplied = slaResults.Count,
                TopologyServicesFound = serviceResults.Count(service => service.ZabbixServiceFound),
                TopologyServicesMissing = 0,
                TopologyServicesWithoutLinks = 0,
                ServiceActions = serviceResults
                    .GroupBy(service => service.Action, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
                SlaActions = slaResults
                    .GroupBy(sla => sla.Action, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
                SampleServices = serviceResults.Take(currentOptions.SampleLimit).ToArray(),
                SampleSlas = slaResults.Take(currentOptions.SampleLimit).ToArray(),
                Message = $"SLA опубликованы: сервисов промаркировано {serviceResults.Count}, SLA применено {slaResults.Count}."
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or FormatException)
        {
            logger.LogError(ex, "Zabbix SLA publication failed.");
            return result with
            {
                Status = "error",
                Errors = [ex.Message],
                Message = $"SLA не опубликованы: {ex.Message}"
            };
        }
    }

    private static ZabbixSlaContext BuildContext(
        IReadOnlyList<CmdbuildClassInstanceCatalogItem> classes,
        IReadOnlyList<CmdbuildDomainRelationCatalogItem> relations,
        ZabbixSlaOptions options)
    {
        var warnings = new List<string>();
        var allCards = classes
            .Where(item => string.Equals(item.Layer, "Service", StringComparison.OrdinalIgnoreCase))
            .SelectMany(item => item.Cards.Select(card => SlaCmdbCard.From(item, card)))
            .ToDictionary(card => CardKey(card.ClassCode, card.Id), StringComparer.Ordinal);
        var serviceCards = allCards.Values
            .Where(card => !IsSlaAuxClass(card.ClassCode, options.CmdbuildPrefix))
            .ToDictionary(card => card.Key, StringComparer.Ordinal);
        var policies = allCards.Values
            .Where(card => IsSlaPolicyClass(card.ClassCode, options.CmdbuildPrefix))
            .Select(card => SlaPolicy.From(card, options, warnings))
            .Where(policy => policy is not null)
            .Select(policy => policy!)
            .GroupBy(policy => policy.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var policiesByCardKey = policies.Values
            .ToDictionary(policy => policy.Card.Key, StringComparer.Ordinal);
        var calendars = allCards.Values
            .Where(card => IsSlaCalendarClass(card.ClassCode, options.CmdbuildPrefix))
            .ToDictionary(card => card.Key, card => card, StringComparer.Ordinal);
        var downtimes = allCards.Values
            .Where(card => IsSlaDowntimeClass(card.ClassCode, options.CmdbuildPrefix))
            .ToDictionary(card => card.Key, card => card, StringComparer.Ordinal);
        var servicePolicyByService = new Dictionary<string, SlaPolicy>(StringComparer.Ordinal);
        var calendarByPolicy = new Dictionary<string, SlaCmdbCard>(StringComparer.OrdinalIgnoreCase);
        var downtimesByPolicy = new Dictionary<string, List<SlaCmdbCard>>(StringComparer.OrdinalIgnoreCase);

        foreach (var relation in relations)
        {
            var sourceKey = CardKey(relation.SourceType, relation.SourceId);
            var destinationKey = CardKey(relation.DestinationType, relation.DestinationId);
            if (serviceCards.ContainsKey(sourceKey) && policiesByCardKey.TryGetValue(destinationKey, out var policy))
            {
                servicePolicyByService[sourceKey] = policy;
                continue;
            }

            if (serviceCards.ContainsKey(destinationKey) && policiesByCardKey.TryGetValue(sourceKey, out policy))
            {
                servicePolicyByService[destinationKey] = policy;
                continue;
            }

            if (policiesByCardKey.TryGetValue(sourceKey, out policy) && calendars.TryGetValue(destinationKey, out var calendar))
            {
                calendarByPolicy[policy.Key] = calendar;
                continue;
            }

            if (policiesByCardKey.TryGetValue(destinationKey, out policy) && calendars.TryGetValue(sourceKey, out calendar))
            {
                calendarByPolicy[policy.Key] = calendar;
                continue;
            }

            if (policiesByCardKey.TryGetValue(sourceKey, out policy) && downtimes.TryGetValue(destinationKey, out var downtime))
            {
                AddPolicyDowntime(downtimesByPolicy, policy.Key, downtime);
                continue;
            }

            if (policiesByCardKey.TryGetValue(destinationKey, out policy) && downtimes.TryGetValue(sourceKey, out downtime))
            {
                AddPolicyDowntime(downtimesByPolicy, policy.Key, downtime);
            }
        }

        return new ZabbixSlaContext(
            ServiceClasses: classes.Count(item => string.Equals(item.Layer, "Service", StringComparison.OrdinalIgnoreCase)),
            ServiceCards: serviceCards,
            Policies: policies,
            Calendars: calendars,
            Downtimes: downtimes,
            ServicePolicyByService: servicePolicyByService,
            CalendarByPolicy: calendarByPolicy,
            DowntimesByPolicy: downtimesByPolicy,
            Warnings: warnings);
    }

    private static ZabbixSlaPlan BuildPlan(
        ZabbixSlaContext context,
        ZabbixSlaOptions options)
    {
        var services = new List<ZabbixSlaServiceApplySample>();
        var slaByPolicy = new Dictionary<string, ZabbixSlaBuildState>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var errors = new List<string>();
        var defaultPolicy = ResolveDefaultPolicy(context, options, warnings);

        foreach (var service in context.ServiceCards.Values.OrderBy(card => card.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var explicitPolicy = context.ServicePolicyByService.GetValueOrDefault(service.Key);
            var policy = explicitPolicy ?? BuildDirectPolicy(service, options, warnings) ?? defaultPolicy;
            if (policy is null)
            {
                continue;
            }

            if (policy.Slo <= 0 || policy.Slo > 100)
            {
                errors.Add($"{service.DisplayName}: SLA policy '{policy.Key}' has invalid sla_target={policy.Slo}.");
                continue;
            }

            if (explicitPolicy is null && defaultPolicy is not null && !IsConcreteServiceObject(service))
            {
                continue;
            }

            var definition = BuildServiceDefinition(service, policy);
            services.Add(new ZabbixSlaServiceApplySample
            {
                ClassCode = service.ClassCode,
                CardId = service.Id,
                ServiceName = definition.Name,
                PolicyKey = policy.Key,
                Slo = policy.Slo,
                Action = "planned",
                Definition = definition
            });

            if (!slaByPolicy.TryGetValue(policy.Key, out var state))
            {
                var calendar = context.CalendarByPolicy.GetValueOrDefault(policy.Key);
                var policyDowntimes = context.DowntimesByPolicy.GetValueOrDefault(policy.Key) ?? [];
                var slaDefinition = BuildSlaDefinition(policy, calendar, policyDowntimes, options, warnings, errors);
                state = new ZabbixSlaBuildState(slaDefinition);
                slaByPolicy[policy.Key] = state;
            }

            state.ServiceCount++;
        }

        var slas = slaByPolicy.Values
            .Where(state => state.ServiceCount > 0)
            .Select(state => new ZabbixSlaApplySample
            {
                PolicyKey = state.Definition.PolicyKey,
                SlaName = state.Definition.Name,
                Slo = state.Definition.Slo,
                Period = state.Definition.Period,
                Timezone = state.Definition.Timezone,
                ServiceCount = state.ServiceCount,
                SchedulePeriodCount = state.Definition.Schedule.Count,
                ManagedExcludedDowntimeCount = state.Definition.ExcludedDowntimes.Count,
                Action = "planned",
                Definition = state.Definition
            })
            .OrderBy(sla => sla.SlaName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ZabbixSlaPlan(services, slas, warnings, errors);
    }

    private async Task<IReadOnlyList<ZabbixSlaServiceApplySample>> ResolveServiceTopologyAsync(
        IReadOnlyList<ZabbixSlaServiceApplySample> services,
        CancellationToken cancellationToken)
    {
        var result = new List<ZabbixSlaServiceApplySample>();
        foreach (var service in services)
        {
            var existing = await zabbixClient.FindManagedServiceByKeyAsync(
                service.Definition.Layer,
                service.Definition.ManagedKey,
                cancellationToken);
            if (existing is null)
            {
                result.Add(service with
                {
                    Action = "blocked_missing_topology",
                    ZabbixServiceFound = false,
                    ZabbixServiceHasTopology = false,
                    TopologyStatus = "missing"
                });
                continue;
            }

            var hasTopology = existing.Children.Count > 0 || existing.Parents.Count > 0;
            result.Add(service with
            {
                ServiceId = existing.ServiceId,
                Action = hasTopology ? "will_tag" : "blocked_unlinked_topology",
                ZabbixServiceFound = true,
                ZabbixServiceHasTopology = hasTopology,
                TopologyStatus = hasTopology ? "linked" : "unlinked"
            });
        }

        return result;
    }

    private static IEnumerable<string> ServiceTopologyErrors(
        IReadOnlyList<ZabbixSlaServiceApplySample> services)
    {
        foreach (var service in services)
        {
            if (!service.ZabbixServiceFound)
            {
                yield return
                    $"{service.ServiceName} ({service.ClassCode}#{service.CardId}): Zabbix Service для SLA не найден по key '{service.Definition.ManagedKey}'. Сначала выполните \"Сервисный слой -> Применить сервисную модель в Zabbix\".";
                continue;
            }

            if (!service.ZabbixServiceHasTopology)
            {
                yield return
                    $"{service.ServiceName} ({service.ClassCode}#{service.CardId}, serviceid={service.ServiceId}): найден изолированный Zabbix Service без parents/children. Сначала опубликуйте сервисную модель в Zabbix, чтобы этот service стал частью дерева, затем повторите публикацию SLA.";
            }
        }
    }

    private static ZabbixManagedServiceDefinition BuildServiceDefinition(
        SlaCmdbCard service,
        SlaPolicy policy)
    {
        var managedKey = ManagedKey(service);
        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ZabbixManagedServiceTags.Managed] = "true",
            [ZabbixManagedServiceTags.Layer] = Layer,
            [ZabbixManagedServiceTags.Class] = service.ClassCode,
            [ZabbixManagedServiceTags.Key] = managedKey,
            [ZabbixManagedServiceTags.CardId] = service.Id,
            [ZabbixManagedServiceTags.Role] = ZabbixManagedServiceRoles.RootService,
            [ZabbixManagedServiceTags.Visibility] = ZabbixManagedServiceVisibility.Root,
            [ZabbixManagedServiceTags.SlaPolicy] = policy.Key,
            [ZabbixManagedServiceTags.SlaTarget] = policy.Slo.ToString(CultureInfo.InvariantCulture)
        };

        return new ZabbixManagedServiceDefinition
        {
            Layer = Layer,
            ManagedKey = managedKey,
            ClassCode = service.ClassCode,
            CardId = service.Id,
            Name = service.DisplayName,
            Description = string.IsNullOrWhiteSpace(service.Description)
                ? $"CMDBuild service object {service.ClassCode}#{service.Id}"
                : service.Description,
            Algorithm = ZabbixServiceAlgorithms.MostCriticalOfChildren,
            Role = ZabbixManagedServiceRoles.RootService,
            Visibility = ZabbixManagedServiceVisibility.Root,
            Tags = tags
        };
    }

    private static ZabbixSlaDefinition BuildSlaDefinition(
        SlaPolicy policy,
        SlaCmdbCard? calendar,
        IReadOnlyList<SlaCmdbCard> downtimes,
        ZabbixSlaOptions options,
        List<string> warnings,
        List<string> errors)
    {
        var timezone = FirstNonEmpty(
                policy.Timezone,
                calendar?.Attr("timezone"),
                options.DefaultTimezone)
            ?? "UTC";
        var schedule = calendar is null
            ? [new ZabbixSlaSchedulePeriod(0, WeekSeconds)]
            : BuildWeeklySchedule(calendar, warnings);
        if (schedule.Count == 0)
        {
            errors.Add($"SLA policy '{policy.Key}': календарь '{calendar?.DisplayName}' не дал ни одного периода; публикация SLA невозможна.");
            schedule = [new ZabbixSlaSchedulePeriod(0, WeekSeconds)];
        }

        var excludedDowntimes = ExpandDowntimes(policy.Key, downtimes, timezone, options, warnings);
        return new ZabbixSlaDefinition
        {
            PolicyKey = policy.Key,
            Name = policy.ZabbixName,
            Slo = policy.Slo,
            Period = policy.Period,
            Timezone = timezone,
            EffectiveDate = StartOfCurrentMonthUtc(),
            Description = $"Managed by cmdb2monitoring from CMDBuild SLA policy {policy.Card.ClassCode}#{policy.Card.Id}.",
            ManagedExcludedDowntimePrefix = options.ManagedExcludedDowntimePrefix,
            ServiceTags =
            [
                new ZabbixSlaServiceTag(ZabbixManagedServiceTags.SlaPolicy, policy.Key)
            ],
            Schedule = schedule,
            ExcludedDowntimes = excludedDowntimes
        };
    }

    private static IReadOnlyList<ZabbixSlaSchedulePeriod> BuildWeeklySchedule(
        SlaCmdbCard calendar,
        List<string> warnings)
    {
        var result = new List<ZabbixSlaSchedulePeriod>();
        var dayFields = new[]
        {
            ("sunday_hours", 0),
            ("monday_hours", 1),
            ("tuesday_hours", 2),
            ("wednesday_hours", 3),
            ("thursday_hours", 4),
            ("friday_hours", 5),
            ("saturday_hours", 6)
        };

        foreach (var (field, dayIndex) in dayFields)
        {
            var value = calendar.Attr(field);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            foreach (var interval in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = interval.Split('-', StringSplitOptions.TrimEntries);
                if (parts.Length != 2
                    || !TryParseClock(parts[0], out var start)
                    || !TryParseClock(parts[1], out var end)
                    || end <= start)
                {
                    warnings.Add($"{calendar.DisplayName}: поле {field} содержит некорректный интервал '{interval}', он пропущен.");
                    continue;
                }

                var dayOffset = dayIndex * 24 * 60 * 60;
                result.Add(new ZabbixSlaSchedulePeriod(
                    dayOffset + (int)start.TotalSeconds,
                    dayOffset + (int)end.TotalSeconds));
            }
        }

        return result
            .OrderBy(period => period.PeriodFrom)
            .ThenBy(period => period.PeriodTo)
            .ToArray();
    }

    private static IReadOnlyList<ZabbixSlaExcludedDowntime> ExpandDowntimes(
        string policyKey,
        IReadOnlyList<SlaCmdbCard> downtimes,
        string timezone,
        ZabbixSlaOptions options,
        List<string> warnings)
    {
        if (downtimes.Count == 0)
        {
            return [];
        }

        var timeZoneInfo = ResolveTimeZone(timezone, warnings);
        var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZoneInfo);
        var horizonEndLocal = nowLocal.AddMonths(options.DowntimePublicationHorizonMonths);
        var result = new List<ZabbixSlaExcludedDowntime>();
        foreach (var downtime in downtimes)
        {
            if (!TryParseClock(downtime.Attr("start_time"), out var startTime))
            {
                warnings.Add($"{downtime.DisplayName}: start_time не распознан, downtime пропущен.");
                continue;
            }

            var duration = ParseInt(downtime.Attr("duration_minutes"), 0);
            if (duration <= 0)
            {
                warnings.Add($"{downtime.DisplayName}: duration_minutes должен быть больше 0, downtime пропущен.");
                continue;
            }

            var today = DateOnly.FromDateTime(nowLocal.Date);
            var horizonEnd = DateOnly.FromDateTime(horizonEndLocal.Date);
            var validFrom = ParseDate(downtime.Attr("valid_from")) ?? today;
            var validTo = ParseDate(downtime.Attr("valid_to")) ?? horizonEnd;
            var from = validFrom > today ? validFrom : today;
            var to = validTo < horizonEnd ? validTo : horizonEnd;
            if (to < from)
            {
                continue;
            }

            var scheduleType = NormalizeToken(downtime.Attr("schedule_type"));
            foreach (var date in CandidateDates(from, to, scheduleType, downtime))
            {
                var dateTime = date.ToDateTime(TimeOnly.MinValue);
                var localStart = new DateTimeOffset(
                    date.Year,
                    date.Month,
                    date.Day,
                    startTime.Hours,
                    startTime.Minutes,
                    0,
                    timeZoneInfo.GetUtcOffset(dateTime));
                var localEnd = localStart.AddMinutes(duration);
                var baseName = FirstNonEmpty(
                        downtime.Attr("zabbix_downtime_name"),
                        downtime.Attr("Code"),
                        downtime.Description,
                        downtime.DisplayName)
                    ?? downtime.Id;
                result.Add(new ZabbixSlaExcludedDowntime(
                    $"{options.ManagedExcludedDowntimePrefix}{baseName} {date:yyyy-MM-dd} [{policyKey}]",
                    localStart.ToUniversalTime().ToUnixTimeSeconds(),
                    localEnd.ToUniversalTime().ToUnixTimeSeconds()));
            }
        }

        return result
            .GroupBy(item => $"{item.Name}\u001f{item.PeriodFrom}\u001f{item.PeriodTo}", StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.PeriodFrom)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<DateOnly> CandidateDates(
        DateOnly from,
        DateOnly to,
        string scheduleType,
        SlaCmdbCard downtime)
    {
        var dayOfWeek = ParseInt(downtime.Attr("day_of_week"), 0);
        var dayOfMonth = ParseInt(downtime.Attr("day_of_month"), 0);
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (scheduleType == "weekly")
            {
                var mondayBased = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
                if (dayOfWeek is >= 1 and <= 7 && mondayBased != dayOfWeek)
                {
                    continue;
                }
            }
            else if (scheduleType == "monthly")
            {
                if (dayOfMonth is >= 1 and <= 31 && date.Day != Math.Min(dayOfMonth, DateTime.DaysInMonth(date.Year, date.Month)))
                {
                    continue;
                }
            }

            yield return date;
        }
    }

    private static SlaPolicy? ResolveDefaultPolicy(
        ZabbixSlaContext context,
        ZabbixSlaOptions options,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(options.DefaultPolicyKey))
        {
            return null;
        }

        if (context.Policies.TryGetValue(options.DefaultPolicyKey, out var policy))
        {
            return policy;
        }

        warnings.Add($"ZabbixSla:DefaultPolicyKey='{options.DefaultPolicyKey}' не найден среди карточек ServiceSlaPolicy; default SLA не применен.");
        return null;
    }

    private static SlaPolicy? BuildDirectPolicy(
        SlaCmdbCard service,
        ZabbixSlaOptions options,
        List<string> warnings)
    {
        var targetText = service.Attr("sla_target");
        if (string.IsNullOrWhiteSpace(targetText))
        {
            return null;
        }

        if (!TryParseDecimal(targetText, out var slo))
        {
            warnings.Add($"{service.DisplayName}: sla_target='{targetText}' не распознан, SLA по прямому атрибуту пропущен.");
            return null;
        }

        var periodText = FirstNonEmpty(service.Attr("reporting_period"), options.DefaultReportingPeriod) ?? "monthly";
        var period = ParsePeriod(periodText);
        var timezone = FirstNonEmpty(service.Attr("timezone"), options.DefaultTimezone) ?? "UTC";
        var key = $"direct:{NormalizePolicyPart(slo.ToString(CultureInfo.InvariantCulture))}:{PeriodName(period)}:{NormalizePolicyPart(timezone)}";
        var name = FirstNonEmpty(
                service.Attr("zabbix_sla_name"),
                $"CMDB2M SLA {slo.ToString(CultureInfo.InvariantCulture)} {PeriodName(period)} {timezone}")
            ?? key;
        return new SlaPolicy(
            Key: key,
            ZabbixName: name,
            Slo: slo,
            Period: period,
            Timezone: timezone,
            Card: service);
    }

    private static bool IsConcreteServiceObject(SlaCmdbCard service)
    {
        return service.HasAttribute("sla_target")
            || service.HasAttribute("service_type")
            || service.ClassCode.EndsWith("ServicePlatformService", StringComparison.Ordinal);
    }

    private static void AddPolicyDowntime(
        Dictionary<string, List<SlaCmdbCard>> downtimesByPolicy,
        string policyKey,
        SlaCmdbCard downtime)
    {
        if (!downtimesByPolicy.TryGetValue(policyKey, out var list))
        {
            list = [];
            downtimesByPolicy[policyKey] = list;
        }

        if (list.All(item => !string.Equals(item.Key, downtime.Key, StringComparison.Ordinal)))
        {
            list.Add(downtime);
        }
    }

    private static bool IsSlaAuxClass(string classCode, string prefix)
    {
        return IsSlaPolicyClass(classCode, prefix)
            || IsSlaCalendarClass(classCode, prefix)
            || IsSlaDowntimeClass(classCode, prefix);
    }

    private static bool IsSlaPolicyClass(string classCode, string prefix)
    {
        return string.Equals(RemovePrefix(classCode, prefix), "ServiceSlaPolicy", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSlaCalendarClass(string classCode, string prefix)
    {
        return string.Equals(RemovePrefix(classCode, prefix), "ServiceSlaCalendar", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSlaDowntimeClass(string classCode, string prefix)
    {
        return string.Equals(RemovePrefix(classCode, prefix), "ServiceSlaDowntime", StringComparison.OrdinalIgnoreCase);
    }

    private static string RemovePrefix(string value, string prefix)
    {
        return !string.IsNullOrWhiteSpace(prefix) && value.StartsWith(prefix, StringComparison.Ordinal)
            ? value[prefix.Length..]
            : value;
    }

    private static string ManagedKey(SlaCmdbCard card)
    {
        return $"cmdbuild:{card.ClassCode}:{card.Id}";
    }

    private static string CardKey(string classCode, string cardId)
    {
        return $"{classCode}:{cardId}";
    }

    private static bool TryParseClock(string? value, out TimeSpan result)
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value)
            && TimeSpan.TryParseExact(value.Trim(), "hh\\:mm", CultureInfo.InvariantCulture, out result);
    }

    private static bool TryParseDecimal(string? value, out decimal result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return decimal.TryParse(value.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }

    private static int ParseInt(string? value, int fallback)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : fallback;
    }

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date))
        {
            return date;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dto)
            ? DateOnly.FromDateTime(dto.Date)
            : null;
    }

    private static int ParsePeriod(string? value)
    {
        return NormalizeToken(value) switch
        {
            "daily" or "day" => ZabbixSlaPeriods.Daily,
            "weekly" or "week" => ZabbixSlaPeriods.Weekly,
            "quarterly" or "quarter" => ZabbixSlaPeriods.Quarterly,
            "yearly" or "annually" or "annual" or "year" => ZabbixSlaPeriods.Annually,
            _ => ZabbixSlaPeriods.Monthly
        };
    }

    private static string PeriodName(int period)
    {
        return period switch
        {
            ZabbixSlaPeriods.Daily => "daily",
            ZabbixSlaPeriods.Weekly => "weekly",
            ZabbixSlaPeriods.Quarterly => "quarterly",
            ZabbixSlaPeriods.Annually => "yearly",
            _ => "monthly"
        };
    }

    private static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var text = value.Trim();
        if ((text.StartsWith('{') && text.EndsWith('}')) || (text.StartsWith('[') && text.EndsWith(']')))
        {
            var lowered = text.ToLowerInvariant();
            foreach (var candidate in new[] { "daily", "weekly", "monthly", "quarterly", "yearly", "annually", "regular" })
            {
                if (lowered.Contains(candidate, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
        }

        return text.Trim('"').ToLowerInvariant();
    }

    private static string NormalizePolicyPart(string value)
    {
        return new string(value
            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-')
            .ToArray()).Trim('-');
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static long StartOfCurrentMonthUtc()
    {
        var now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
    }

    private static TimeZoneInfo ResolveTimeZone(string timezone, List<string> warnings)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(timezone) ? "UTC" : timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            warnings.Add($"Timezone '{timezone}' не найден в системе; для downtime используется UTC.");
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            warnings.Add($"Timezone '{timezone}' поврежден в системе; для downtime используется UTC.");
            return TimeZoneInfo.Utc;
        }
    }

    private sealed record ZabbixSlaContext(
        int ServiceClasses,
        IReadOnlyDictionary<string, SlaCmdbCard> ServiceCards,
        IReadOnlyDictionary<string, SlaPolicy> Policies,
        IReadOnlyDictionary<string, SlaCmdbCard> Calendars,
        IReadOnlyDictionary<string, SlaCmdbCard> Downtimes,
        IReadOnlyDictionary<string, SlaPolicy> ServicePolicyByService,
        IReadOnlyDictionary<string, SlaCmdbCard> CalendarByPolicy,
        IReadOnlyDictionary<string, List<SlaCmdbCard>> DowntimesByPolicy,
        IReadOnlyList<string> Warnings);

    private sealed record ZabbixSlaPlan(
        IReadOnlyList<ZabbixSlaServiceApplySample> Services,
        IReadOnlyList<ZabbixSlaApplySample> Slas,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<string> Errors);

    private sealed record ZabbixSlaBuildState(ZabbixSlaDefinition Definition)
    {
        public int ServiceCount { get; set; }
    }

    private sealed record SlaPolicy(
        string Key,
        string ZabbixName,
        decimal Slo,
        int Period,
        string Timezone,
        SlaCmdbCard Card)
    {
        public static SlaPolicy? From(SlaCmdbCard card, ZabbixSlaOptions options, List<string> warnings)
        {
            if (!TryParseDecimal(card.Attr("sla_target"), out var slo))
            {
                warnings.Add($"{card.DisplayName}: sla_target не заполнен или не распознан; SLA policy пропущена.");
                return null;
            }

            var period = ParsePeriod(FirstNonEmpty(card.Attr("reporting_period"), options.DefaultReportingPeriod));
            var timezone = FirstNonEmpty(card.Attr("timezone"), options.DefaultTimezone) ?? "UTC";
            var key = FirstNonEmpty(
                    card.Attr("Code"),
                    card.Attr("code"),
                    card.Attr("name"),
                    card.Attr("Name"),
                    card.Attr("zabbix_sla_name"),
                    card.Id)
                ?? card.Id;
            var name = FirstNonEmpty(
                    card.Attr("zabbix_sla_name"),
                    card.Attr("name"),
                    card.Attr("Name"),
                    card.Description,
                    $"CMDB2M SLA {slo.ToString(CultureInfo.InvariantCulture)} {PeriodName(period)} {timezone}")
                ?? key;
            return new SlaPolicy(
                Key: key,
                ZabbixName: name,
                Slo: slo,
                Period: period,
                Timezone: timezone,
                Card: card);
        }
    }

    private sealed record SlaCmdbCard(
        string Key,
        string Layer,
        string ClassCode,
        string ClassName,
        string Id,
        string Description,
        IReadOnlyDictionary<string, string> Attributes)
    {
        public string DisplayName =>
            FirstNonEmpty(Attr("zabbix_service_name"), Attr("monitoring_name"), Attr("name"), Attr("Name"), Attr("Code"), Attr("code"), Description)
            ?? $"{ClassCode} #{Id}";

        public bool HasAttribute(string code)
        {
            return Attributes.Keys.Any(key => string.Equals(key, code, StringComparison.OrdinalIgnoreCase));
        }

        public string Attr(string code)
        {
            if (Attributes.TryGetValue(code, out var value))
            {
                return value;
            }

            foreach (var pair in Attributes)
            {
                if (string.Equals(pair.Key, code, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value;
                }
            }

            return "";
        }

        public static SlaCmdbCard From(
            CmdbuildClassInstanceCatalogItem classItem,
            CmdbuildClassCardCatalogItem card)
        {
            var attributes = card.Attributes
                .GroupBy(attribute => attribute.Code, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Last().Value ?? "",
                    StringComparer.Ordinal);
            if (!attributes.ContainsKey("Description") && !string.IsNullOrWhiteSpace(card.Description))
            {
                attributes["Description"] = card.Description;
            }

            return new SlaCmdbCard(
                Key: CardKey(card.ClassCode, card.Id),
                Layer: card.Layer,
                ClassCode: card.ClassCode,
                ClassName: classItem.ClassName,
                Id: card.Id,
                Description: card.Description,
                Attributes: attributes);
        }
    }
}

public sealed record ZabbixSlaPublishResult
{
    public bool DryRun { get; init; }

    public bool Enabled { get; init; }

    public string Status { get; init; } = "";

    public string Message { get; init; } = "";

    public string DefaultPolicyKey { get; init; } = "";

    public int DowntimePublicationHorizonMonths { get; init; }

    public string ManagedExcludedDowntimePrefix { get; init; } = "";

    public int CmdbuildServiceClasses { get; init; }

    public int CmdbuildServicesScanned { get; init; }

    public int PolicyCount { get; init; }

    public int CalendarCount { get; init; }

    public int DowntimeCount { get; init; }

    public int ServiceCandidates { get; init; }

    public int ServicesApplied { get; init; }

    public int TopologyServicesFound { get; init; }

    public int TopologyServicesMissing { get; init; }

    public int TopologyServicesWithoutLinks { get; init; }

    public int SlasPlanned { get; init; }

    public int SlasApplied { get; init; }

    public IReadOnlyDictionary<string, int> ServiceActions { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, int> SlaActions { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    public IReadOnlyList<ZabbixSlaServiceApplySample> SampleServices { get; init; } = [];

    public IReadOnlyList<ZabbixSlaApplySample> SampleSlas { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IReadOnlyList<string> Errors { get; init; } = [];
}

public sealed record ZabbixSlaServiceApplySample
{
    public string ClassCode { get; init; } = "";

    public string CardId { get; init; } = "";

    public string ServiceName { get; init; } = "";

    public string PolicyKey { get; init; } = "";

    public decimal Slo { get; init; }

    public string ServiceId { get; init; } = "";

    public string Action { get; init; } = "";

    public bool ZabbixServiceFound { get; init; }

    public bool ZabbixServiceHasTopology { get; init; }

    public string TopologyStatus { get; init; } = "";

    public ZabbixManagedServiceDefinition Definition { get; init; } = new();
}

public sealed record ZabbixSlaApplySample
{
    public string PolicyKey { get; init; } = "";

    public string SlaName { get; init; } = "";

    public decimal Slo { get; init; }

    public int Period { get; init; }

    public string Timezone { get; init; } = "";

    public int ServiceCount { get; init; }

    public int SchedulePeriodCount { get; init; }

    public int ManagedExcludedDowntimeCount { get; init; }

    public int PreservedManualExcludedDowntimeCount { get; init; }

    public string SlaId { get; init; } = "";

    public string Action { get; init; } = "";

    public ZabbixSlaDefinition Definition { get; init; } = new();
}
