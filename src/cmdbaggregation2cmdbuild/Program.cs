using Cmdb2MonitoringServiceSuppression.Shared.CmdbuildSchema;
using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Cmdb2MonitoringServiceSuppression.Shared.Integrations;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddSingleton<CmdbuildSchemaFactory>();
builder.Services.AddOptions<CmdbuildOptions>()
    .Bind(builder.Configuration.GetSection(CmdbuildOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.BaseUrl), "CMDBuild base URL is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Username), "CMDBuild username is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Password), "CMDBuild password is required.")
    .Validate(options => options.RequestTimeoutMs > 0, "CMDBuild request timeout must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddHttpClient<CmdbuildClient>();

var app = builder.Build();
app.MapServiceHealth();

app.MapGet("/schema/preview", (
    string? prefix,
    string? language,
    string? serviceModelRoot,
    string? suppressionModelRoot,
    CmdbuildSchemaFactory factory) =>
{
    var options = new CmdbuildSchemaOptions
    {
        Prefix = prefix ?? "",
        Language = ParseLanguage(language),
        ServiceModelRoot = serviceModelRoot ?? "",
        SuppressionModelRoot = suppressionModelRoot ?? ""
    };

    return Results.Ok(factory.Build(options));
});

app.MapPost("/schema/preview", (
    CmdbuildSchemaOptions options,
    CmdbuildSchemaFactory factory) =>
{
    return Results.Ok(factory.Build(options));
});

app.MapPost("/schema/apply", async (
    CmdbuildSchemaApplyRequest request,
    CmdbuildSchemaFactory factory,
    CmdbuildClient client,
    CancellationToken cancellationToken) =>
{
    try
    {
        var schema = factory.Build(request.Options);
        var result = await client.ApplySchemaAsync(schema, request.Selection, cancellationToken);
        return result.Success
            ? Results.Ok(result)
            : Results.Problem(
                title: "One or more CMDBuild schema objects failed to apply.",
                detail: string.Join("; ", result.Items.Where(item => !item.Success).Select(item => $"{item.Kind} {item.Code}: {item.Message}")),
                extensions: new Dictionary<string, object?> { ["result"] = result },
                statusCode: StatusCodes.Status502BadGateway);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapGet("/cmdbuild/check", async (
    CmdbuildClient client,
    CancellationToken cancellationToken) =>
{
    var result = await client.CheckConnectionAsync(cancellationToken);
    return result.Success ? Results.Ok(result) : Results.Problem(result.Error, statusCode: StatusCodes.Status502BadGateway);
});

app.MapGet("/cmdbuild/classes", async (
    string? rootPath,
    string? prefix,
    string? layer,
    bool? managedOnly,
    bool? includePrototypes,
    CmdbuildClient client,
    CancellationToken cancellationToken) =>
{
    try
    {
        var managedFilter = managedOnly == true
            ? new CmdbuildManagedClassFilter
            {
                Prefix = prefix ?? "",
                Layer = layer ?? ""
            }
            : null;
        var catalog = await client.ListClassesAsync(rootPath, managedFilter, includePrototypes == true, cancellationToken);

        if (string.IsNullOrWhiteSpace(rootPath) && managedFilter is null)
        {
            return Results.Ok(new { classes = catalog.Classes });
        }

        return Results.Ok(catalog);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapGet("/cmdbuild/classes/schema", async (
    CmdbuildClient client,
    CancellationToken cancellationToken) =>
{
    try
    {
        var classes = await client.ListClassSchemasAsync(cancellationToken);
        return Results.Ok(new { classes });
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapGet("/cmdbuild/classes/instances", async (
    string? prefix,
    string? serviceModelRoot,
    string? suppressionModelRoot,
    CmdbuildClient client,
    CancellationToken cancellationToken) =>
{
    try
    {
        var catalog = await client.ListManagedClassInstancesAsync(
            prefix,
            serviceModelRoot,
            suppressionModelRoot,
            cancellationToken);
        return Results.Ok(catalog);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapGet("/cmdbuild/domains", async (
    string? prefix,
    CmdbuildClient client,
    CancellationToken cancellationToken) =>
{
    try
    {
        var domains = await client.ListDomainsAsync(prefix, cancellationToken);
        return Results.Ok(new { domains });
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();

static SchemaLanguage ParseLanguage(string? language)
{
    return string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)
        ? SchemaLanguage.En
        : SchemaLanguage.Ru;
}
