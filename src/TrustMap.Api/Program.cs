using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using TrustMap.Api.Common;
using TrustMap.Api.Health;
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
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = multipartBodyLimit;
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
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

builder.Services.AddTrustMapInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddScoped<CategoryAssignmentService>();
builder.Services.AddScoped<CategoryMembershipService>();
builder.Services.AddScoped<HandleGenerator>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<SocialGraphService>();
builder.Services.AddScoped<PrivacyAccessService>();
builder.Services.AddScoped<PlaceCatalogService>();
builder.Services.AddScoped<ProfileQueryService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddSingleton<IPushNotificationSender, NoOpPushNotificationSender>();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

var configuredJwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (Encoding.UTF8.GetByteCount(configuredJwtOptions.SigningKey) < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes long.");
}

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

var photoStorageOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<PhotoStorageOptions>>().Value;
if (string.Equals(photoStorageOptions.Provider, "local", StringComparison.OrdinalIgnoreCase))
{
    var rootPath = Path.GetFullPath(photoStorageOptions.RootPath, app.Environment.ContentRootPath);
    Directory.CreateDirectory(rootPath);

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(rootPath),
        RequestPath = photoStorageOptions.RequestPath,
        OnPrepareResponse = context =>
        {
            context.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        }
    });
}

app.UseAuthentication();
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

public partial class Program;
