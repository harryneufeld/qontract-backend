using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Http;
using System.Threading.RateLimiting;
using FluentValidation;
using Qontract.Infrastructure.Data;
using Qontract.Application.Services;
using Qontract.Application.Interfaces;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Qontract.Core.Interfaces;
using Qontract.Infrastructure.FileStorage;
using Qontract.API.Serialization;
using Qontract.Core.Security;
using Asp.Versioning;
using Asp.Versioning.ApiExplorer;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("debugSettings.json", optional: true, reloadOnChange: true);
if (builder.Environment.EnvironmentName == "Development")
{
    var debugUrls = builder.Configuration["Urls"];
    if (!string.IsNullOrWhiteSpace(debugUrls))
    {
        builder.WebHost.UseUrls(debugUrls);
    }
}

// Get connection string from configuration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=QontractDb.sqlite";

// Database provider (issue #26): "sqlite" (default), "postgresql" or
// "sqlserver". Normalizing here makes a typo in Database:Provider fail fast
// at startup with a clear message instead of at first database access.
var databaseProvider = DatabaseProvider.Normalize(builder.Configuration["Database:Provider"]);

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Enums as strings so API consumers (Flutter app) work with readable values
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter());
        // Always serialize DateTimes as UTC with an explicit 'Z' suffix so the
        // JSON is identical between create responses (Kind=Utc) and
        // list/detail responses (entities reloaded from SQLite come back with
        // Kind=Unspecified) — issue #31.
        options.JsonSerializerOptions.Converters.Add(
            new UtcDateTimeConverter());
    });
builder.Services.AddEndpointsApiExplorer();

// API versioning (issue #28): so a future breaking change can ship as a new
// version WITHOUT breaking the Flutter app. The current API surface is
// version 1.0. The version is read from the URL segment (/api/v1/...), the
// query string (?api-version=1.0) or the X-Api-Version header; a request that
// names no version — i.e. every existing Flutter app call and every existing
// integration test — resolves to the default version (AssumeDefaultVersionWhen
// Unspecified), so introducing versioning is fully backward compatible.
// When a breaking change is needed, add a version-2.0 controller/route and
// leave the v1.0 routes running for older clients.
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    // Advertise the available API versions on each response (X-AspNet-Version
    // header) so clients can discover what is offered.
    options.ReportApiVersions = true;
    options.ApiVersionReader = ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader(),
        new QueryStringApiVersionReader("api-version"),
        new HeaderApiVersionReader("X-Api-Version"));
})
// Tag each endpoint with its API version so Swagger documents the endpoints
// per API version instead of per controller (issue #28).
.AddApiExplorer(options =>
{
    options.AssumeDefaultVersionWhenUnspecified = true;
    // Render the concrete version (e.g. /api/v1/contracts) in the docs rather
    // than the {version:apiVersion} route placeholder.
    options.SubstituteApiVersionInUrl = true;
});

builder.Services.AddSwaggerGen(options =>
{
    // Stable schema ids across versioned documents (avoids duplicate-schema
    // ids when the same type appears under more than one route).
    options.CustomSchemaIds(type => type.FullName);
});

// Register FluentValidation validators (live in Qontract.Application;
// IValidator<T> is resolved per-DTO in controllers)
builder.Services.AddValidatorsFromAssembly(typeof(Qontract.Application.Services.IContractorService).Assembly);

// Register DbContext
// (issue #36) Sensitive data logging dumps SQL parameter values — including
// password hashes on login — into the log output, so it is enabled only in
// Development, never in Production/other environments.
// (issue #26) The provider is selected by "Database:Provider" (default
// "sqlite"); the shared migration chain runs on all three providers.
var isDevelopment = builder.Environment.EnvironmentName == "Development";
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.Configure(databaseProvider, connectionString);
    if (isDevelopment)
    {
        options.EnableSensitiveDataLogging();
    }
});
builder.Services.AddScoped<IApplicationDbContext, ApplicationDbContext>();

// Register services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IContractorService, ContractorService>();
builder.Services.AddScoped<IContractService, ContractService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IAddressService, AddressService>();

// Register File Storage. The backend is selected by "Storage:Provider"
// (issue #25): "local" (default — disk under Storage:Path), "azure" (Azure
// Blob Storage) or "s3" (Amazon S3). The maximum file size lives in the
// "Storage" section for all backends (issue #34); provider-specific
// credentials are deliberately not committed (see appsettings.json).
builder.Services.Configure<FileStorageOptions>(
    builder.Configuration.GetSection(FileStorageOptions.SectionName));
switch (StorageProvider.Normalize(builder.Configuration[$"{FileStorageOptions.SectionName}:Provider"]))
{
    case StorageProvider.Azure:
        builder.Services.Configure<AzureBlobStorageOptions>(
            builder.Configuration.GetSection(AzureBlobStorageOptions.SectionName));
        builder.Services.AddSingleton<IFileStorage, AzureBlobFileStorage>();
        break;
    case StorageProvider.S3:
        builder.Services.Configure<S3StorageOptions>(
            builder.Configuration.GetSection(S3StorageOptions.SectionName));
        builder.Services.AddSingleton<IFileStorage, S3FileStorage>();
        break;
    default: // local
        builder.Services.AddSingleton<IFileStorage, FileStorage>();
        break;
}

// Document deletion queue (issue #14): retention settings + the background
// worker that permanently removes expired soft-deleted documents.
builder.Services.Configure<Qontract.Application.Services.DocumentRetentionOptions>(
    builder.Configuration.GetSection(Qontract.Application.Services.DocumentRetentionOptions.SectionName));
builder.Services.AddScoped<Qontract.Application.Services.DocumentPurgeService>();
builder.Services.AddHostedService<Qontract.API.Services.DocumentRetentionWorker>();

// Configure JWT
// Resolve + validate the JWT settings up front so a missing Jwt:Key fails
// fast with a clear message (or falls back to a dev-only key in Development)
// instead of blowing up with an unhandled ArgumentNullException on the
// first authenticated request (issue #10).
//
// (issue #36) Jwt:Key is NOT committed in appsettings.json — it must come
// from an environment variable (Jwt__Key) or user secrets in every
// non-Development environment. Development falls back to a shared
// development-only key so the API runs out of the box.
var jwtKey = builder.Configuration["Jwt:Key"];
var usedJwtFallbackKey = false;
if (string.IsNullOrWhiteSpace(jwtKey))
{
    if (isDevelopment)
    {
        // Development-only fallback so the API runs out of the box.
        // This key is NOT secret-safe and must never be used in production.
        jwtKey = JwtKeys.DevelopmentFallback;
        usedJwtFallbackKey = true;
    }
    else
    {
        throw new InvalidOperationException(
            "Jwt:Key is not configured. Set the 'Jwt:Key' configuration value " +
            "(environment variable Jwt__Key or user secrets — it is intentionally " +
            "not committed in appsettings.json) to a secret of at least 32 characters.");
    }
}

if (Encoding.ASCII.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        $"Jwt:Key is too short ({Encoding.ASCII.GetByteCount(jwtKey)} bytes). " +
        "The HS256 signing key must be at least 32 characters long.");
}

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "Qontract";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "QontractClients";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(jwtKey)),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

// Add CORS
//
// In Development the Flutter web app may be served from any localhost
// port (flutter run picks a random high port, e.g. 5354 — issue #12),
// so allow any origin. AllowCredentials is intentionally omitted there:
// it cannot be combined with AllowAnyOrigin, and the app authenticates
// with a JWT bearer header, not cookies.
//
// In Production only the explicitly configured origins (Cors:Origins in
// appsettings.json) are allowed, and credentials are permitted.
var allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
    ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    if (isDevelopment)
    {
        options.AddDefaultPolicy(policy =>
            policy
                .AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader());
        options.AddPolicy("AllowLocalhost",
            policy => policy
                .AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader());
    }
    else
    {
        options.AddDefaultPolicy(policy =>
            policy
                .WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());
        options.AddPolicy("AllowLocalhost",
            policy => policy
                .WithOrigins(allowedOrigins)
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials());
    }
});

// Rate limiting (issue #27): ASP.NET Core's built-in rate limiter
// (Microsoft.AspNetCore.RateLimiting, part of the shared framework — no
// extra NuGet package). Two layers, both partitioned PER CLIENT IP so a
// single abusive client never throttles everyone else:
//
//   * a generous GLOBAL ceiling on every endpoint (unbounded-abuse net), and
//   * a STRICTER "auth" policy applied (via [EnableRateLimiting("auth")])
//     to the credential endpoints (/auth/login, /auth/change-password) to
//     blunt brute-force / credential-stuffing attacks.
//
// Limits are config-driven (RateLimiting:* in appsettings.json) with safe
// defaults, so operators can tune per deployment without a rebuild.
// Rejected requests get 429 (Too Many Requests) + a Retry-After hint.
var rlGlobal = builder.Configuration.GetSection("RateLimiting:Global");
var rlAuth = builder.Configuration.GetSection("RateLimiting:Auth");
var globalPermit = int.TryParse(rlGlobal["PermitLimit"], out var g) ? g : 300;
var globalWindow = TimeSpan.FromSeconds(int.TryParse(rlGlobal["WindowSeconds"], out var gw) ? gw : 60);
var authPermit = int.TryParse(rlAuth["PermitLimit"], out var a) ? a : 5;
var authWindow = TimeSpan.FromSeconds(int.TryParse(rlAuth["WindowSeconds"], out var aw) ? aw : 60);

// All requests in the TestServer / Kestrel pipeline carry the client IP;
// fall back to a stable sentinel when it is unavailable so partitioning
// never throws.
static string IpKey(HttpContext context) =>
    context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Throttled responses get a 429 plus a Retry-After hint so clients know
    // when to back off instead of hammering the endpoint blindly. The hint is
    // the auth window (the time a fixed window stays exhausted) — a safe,
    // predictable "wait a full window before retrying" value.
    options.OnRejected = async (ctx, _) =>
    {
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        ctx.HttpContext.Response.Headers["Retry-After"] =
            ((int)Math.Ceiling(authWindow.TotalSeconds)).ToString();
    };

    // Global per-IP ceiling: a broad, high bar that catches runaway clients
    // hitting ANY endpoint, without throttling normal app traffic.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
        context => RateLimitPartition.GetFixedWindowLimiter(
            IpKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = globalPermit,
                Window = globalWindow,
                QueueLimit = 0,
            }));

    // Stricter per-IP policy for credential endpoints (brute-force
    // protection). Applied explicitly by the controllers, not globally.
    options.AddPolicy<string>("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            IpKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermit,
                Window = authWindow,
                QueueLimit = 0,
            }));
});

var app = builder.Build();

if (usedJwtFallbackKey)
{
    app.Logger.LogWarning(
        "Jwt:Key is not configured. Falling back to a development-only signing key. " +
        "Set 'Jwt:Key' in configuration before deploying.");
}

// Exception handling middleware: converts unhandled exceptions into a clean,
// consistent JSON error envelope so no stack trace ever reaches clients
// (issue #11). Registered first so it wraps the whole pipeline.
app.UseMiddleware<Qontract.API.Middleware.ExceptionHandlingMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.EnvironmentName == "Development")
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowLocalhost");
app.UseAuthentication();
app.UseAuthorization();
// Rate limiter middleware (issue #27): placed after authentication and
// before the controllers are mapped so per-endpoint [EnableRateLimiting]
// policies resolve against the routed endpoint. Rejected requests short-
// circuit here with a 429 + Retry-After, before the controller runs.
app.UseRateLimiter();
app.MapControllers();

// Apply migrations and seed data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var context = services.GetRequiredService<ApplicationDbContext>();
    
    // Apply migrations if needed
    context.Database.Migrate();
    
    var userManager = services.GetRequiredService<IUserService>();
    await SeedDataAsync(context, userManager);
}

app.Run();

async Task SeedDataAsync(ApplicationDbContext context, IUserService userService)
{
    var tenant = new Tenant
    {
        Name = "Test Family",
        Type = TenantType.Family,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        ModifiedAt = DateTime.UtcNow
    };

    var tenantExists = await context.Tenants.AnyAsync(t => t.Name == "Test Family");
    if (!tenantExists)
    {
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
    }

    // Second tenant for multi-tenancy tests (its own admin user + contractors
    // that must never be visible to the first tenant)
    var tenant2 = new Tenant
    {
        Name = "Test Family Two",
        Type = TenantType.Family,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        ModifiedAt = DateTime.UtcNow
    };

    if (!await context.Tenants.AnyAsync(t => t.Name == "Test Family Two"))
    {
        context.Tenants.Add(tenant2);
        await context.SaveChangesAsync();
    }

    // Create admin user (tenant 1)
    if (!await context.Users.AnyAsync(u => u.Email == "admin@qontract.com"))
    {
        var user = new User
        {
            Name = "Admin User",
            Email = "admin@qontract.com",
            PasswordHash = "cW9udHJhY3Rfc2VlZF9zYWx0:IqHPhrNNkcLnI0/caOLUw3Cixr9EUYwymWWdYK8DLcA=", // salted SHA256 of "password123"
            Role = UserRole.TenantAdmin,
            IsActive = true,
            TenantId = tenant.Id,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();
    }

    // Create admin user (tenant 2)
    if (!await context.Users.AnyAsync(u => u.Email == "admin2@qontract.com"))
    {
        var user2 = new User
        {
            Name = "Admin User Two",
            Email = "admin2@qontract.com",
            PasswordHash = "dGVzdF9zYWx0XzE2Ynl0ZQ==:jt8FI9ppKsxLD6SNvDkj5HCXkgj++ybAlEXxTaUWmuM=", // salted SHA256 of "password123"
            Role = UserRole.TenantAdmin,
            IsActive = true,
            TenantId = tenant2.Id,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };

        context.Users.Add(user2);
        await context.SaveChangesAsync();
    }
}