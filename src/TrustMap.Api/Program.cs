using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using TrustMap.Api.Common;
using TrustMap.Api.Health;
using TrustMap.Api.Quotas;
using TrustMap.Api.RateLimiting;
using TrustMap.Api.Services;
using TrustMap.Api.Services.PushNotifications;
using TrustMap.Infrastructure;
using TrustMap.Infrastructure.Authentication;
using TrustMap.Infrastructure.Persistence;
using TrustMap.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);
var configuredPhotoStorageOptions =
    builder.Configuration.GetSection(PhotoStorageOptions.SectionName).Get<PhotoStorageOptions>()
    ?? new PhotoStorageOptions();
var maxUploadBytes = configuredPhotoStorageOptions.MaxUploadBytes > 0
    ? configuredPhotoStorageOptions.MaxUploadBytes
    : PhotoStorageOptions.DefaultMaxUploadBytes;
const long MultipartEnvelopeBytes = 1_048_576;
var multipartBodyLimit = maxUploadBytes > long.MaxValue - MultipartEnvelopeBytes
    ? long.MaxValue
    : maxUploadBytes + MultipartEnvelopeBytes;

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = multipartBodyLimit;
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Public API enum values are strings; numeric enum JSON is rejected during request binding.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
    });
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value!.Errors
                    .Select(error => SanitizeValidationError(error.ErrorMessage))
                    .ToArray());

        var problemDetails = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
            Instance = context.HttpContext.Request.Path
        };
        problemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

        return new BadRequestObjectResult(problemDetails);
    };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "TrustMap API",
        Version = "v1"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = JwtBearerDefaults.AuthenticationScheme,
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste a valid TrustMap access token."
    });
});

builder.Services.PostConfigure<PhotoStorageOptions>(options =>
{
    options.RootPath = Path.GetFullPath(options.RootPath, builder.Environment.ContentRootPath);
});

builder.Services.AddTrustMapInfrastructure(
    builder.Configuration,
    builder.Environment.ContentRootPath,
    builder.Environment.EnvironmentName);
builder.Services.AddScoped<CategoryAssignmentService>();
builder.Services.AddScoped<CategoryMembershipService>();
builder.Services.AddScoped<HandleGenerator>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<SocialGraphService>();
builder.Services.AddScoped<PrivacyAccessService>();
builder.Services.AddScoped<PlaceCatalogService>();
builder.Services.AddScoped<ProfileQueryService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<IQuotaService, EfQuotaService>();
builder.Services.AddOptions<PushNotificationOptions>()
    .Bind(builder.Configuration.GetSection(PushNotificationOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<PushNotificationOptions>>(
    _ => new PushNotificationOptionsValidator(builder.Environment.EnvironmentName));
builder.Services.AddHttpClient(ApnsPushNotificationSender.HttpClientName, (serviceProvider, httpClient) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PushNotificationOptions>>().Value;
    httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.Apns.RequestTimeoutSeconds));
});
builder.Services.AddSingleton<NoOpPushNotificationSender>();
builder.Services.AddSingleton<ApnsPushNotificationSender>();
builder.Services.AddSingleton<IPushNotificationSender>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PushNotificationOptions>>().Value;
    return options.UseApnsProvider
        ? serviceProvider.GetRequiredService<ApnsPushNotificationSender>()
        : serviceProvider.GetRequiredService<NoOpPushNotificationSender>();
});
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
builder.Services.AddOptions<QuotaOptions>()
    .Bind(builder.Configuration.GetSection(QuotaOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<QuotaOptions>, QuotaOptionsValidator>();
builder.Services.AddTrustMapRateLimiting(builder.Configuration);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<Microsoft.Extensions.Options.IOptions<JwtOptions>>((options, jwtOptionsAccessor) =>
    {
        var jwtOptions = jwtOptionsAccessor.Value;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "display_name"
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Force critical options validation before startup side effects such as database migrations.
_ = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<JwtOptions>>().Value;
_ = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<PushNotificationOptions>>().Value;

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
}

app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready");

var databaseOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>().Value;
if (databaseOptions.ApplyMigrationsOnStartup)
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.Run();

public partial class Program
{
    private static string SanitizeValidationError(string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return "The value is invalid.";
        }

        return errorMessage.Contains("could not be converted", StringComparison.OrdinalIgnoreCase)
            ? "The value is invalid or has the wrong JSON type."
            : errorMessage;
    }
}
