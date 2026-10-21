using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
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

if (builder.Environment.EnvironmentName == "Development")
{
    builder.Configuration.AddJsonFile("debugSettings.json", optional: true, reloadOnChange: true);
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

// One-shot SQLite → PostgreSQL copy; runs instead of the web host.
if (args.FirstOrDefault() == Qontract.API.SqliteToPostgresCommand.Name)
{
    Environment.ExitCode = await Qontract.API.SqliteToPostgresCommand.RunAsync(args[1..], databaseProvider, connectionString);
    return;
}

// Add services to the container.
builder.Services.AddControllers(options => options.Filters.Add<Qontract.API.Localization.LocalizedResultFilter>())
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
// "sqlite"); PostgreSQL uses its own migration chain (Qontract.Infrastructure.PostgreSql).
var isDevelopment = builder.Environment.EnvironmentName == "Development";
// Trust only the explicitly configured immediate reverse proxy.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    var proxies = builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
    if (proxies.Length == 0) return;
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();
    foreach (var proxy in proxies)
    {
        if (!IPAddress.TryParse(proxy, out var address))
            throw new InvalidOperationException("ReverseProxy:KnownProxies must contain IP addresses.");
        options.KnownProxies.Add(address);
    }
});
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
var protection = builder.Services.AddDataProtection().SetApplicationName("qontract");
// Containers must retain this directory alongside the database. The keys are
// secrets: protect the directory with host permissions and encrypted backups.
var keyPath = builder.Configuration["DataProtection:KeyPath"];
if (builder.Environment.EnvironmentName != "Development" || !string.IsNullOrWhiteSpace(keyPath))
    protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath ?? Path.Combine(builder.Environment.ContentRootPath, "data", "keys")));
builder.Services.AddSingleton<ITwoFactorSecretProtector, Qontract.API.Services.TwoFactorSecretProtector>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<PasswordResetService>();
builder.Services.AddScoped<IResetEmailSender, Qontract.Infrastructure.Email.SmtpResetEmailSender>();
builder.Services.AddSingleton<Qontract.API.Services.PasswordResetQueue>();
builder.Services.AddHostedService<Qontract.API.Services.PasswordResetWorker>();
builder.Services.AddScoped<EmailConfirmationService>();
builder.Services.AddScoped<IConfirmationEmailSender, Qontract.Infrastructure.Email.SmtpResetEmailSender>();
builder.Services.AddScoped<TermsService>();
TermsService.ValidateConfiguration(builder.Configuration);
// Confirmation links use the same trusted app URL and SMTP relay as password reset.
if (PasswordResetService.Enabled(builder.Configuration) || EmailConfirmationService.Required(builder.Configuration))
{
    PasswordResetService.PublicBase(builder.Configuration);
    Qontract.Infrastructure.Email.SmtpResetEmailSender.ValidateConfiguration(builder.Configuration);
}
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<MembershipService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IContractorService, ContractorService>();
builder.Services.AddScoped<IContractService, ContractService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IDocumentIdentity, Qontract.API.Services.DocumentIdentity>();
builder.Services.AddScoped<InvoiceService>();
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
builder.Services.AddScoped<Qontract.Infrastructure.ContractPurgeService>();
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

// A key that is publicly known (the development fallback set explicitly, or
// an unedited REPLACE_WITH_... placeholder copied from deploy/*.env.example)
// passes the length check but lets anyone mint valid tokens, so refuse to
// start with one outside Development.
if (!isDevelopment && JwtKeys.IsKnownInsecure(jwtKey))
{
    throw new InvalidOperationException(
        "Jwt:Key is set to a publicly known value (the development fallback key or a " +
        "placeholder from the example env files). Generate a random secret of at least " +
        "32 characters, e.g. `openssl rand -base64 48`, and set it as Jwt__Key.");
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
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async validation =>
        {
            var identity = validation.Principal?.Identity as System.Security.Claims.ClaimsIdentity;
            if (!int.TryParse(validation.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var userId)
                || !int.TryParse(validation.Principal?.FindFirst("TenantId")?.Value, out var tenantId)
                || !Guid.TryParse(validation.Principal?.FindFirst("sid")?.Value, out var sessionId)
                || !int.TryParse(validation.Principal?.FindFirst("auth_version")?.Value, out var authVersion)
                || userId <= 0 || tenantId < 0)
            {
                validation.Fail("Invalid tenant identity.");
                return;
            }
            var db = validation.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
            var session = await db.AuthSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId
                && s.UserId == userId && s.RevokedAt == null && s.AuthVersion == authVersion
                && s.User.AuthVersion == authVersion && s.User.IsActive && !s.User.RequiresPasswordSetup,
                validation.HttpContext.RequestAborted);
            if (session is null || session.ExpiresAt <= DateTime.UtcNow)
            {
                validation.Fail("Session is no longer active.");
                return;
            }
            if (tenantId == 0 && identity is not null && validation.Principal?.FindFirst("access_scope")?.Value == "personal")
            {
                foreach (var claim in identity.FindAll(identity.RoleClaimType).ToList()) identity.RemoveClaim(claim);
                identity.AddClaim(new System.Security.Claims.Claim(identity.RoleClaimType, "User"));
                return;
            }
            var membership = await db.TenantMemberships.AsNoTracking().SingleOrDefaultAsync(m => m.UserId == userId
                && m.TenantId == tenantId && m.IsActive && m.User.IsActive && m.Tenant.IsActive,
                validation.HttpContext.RequestAborted);
            if (membership is null || identity is null)
            {
                validation.Fail("Tenant membership is no longer active.");
                return;
            }
            // Roles are membership-specific and take effect immediately,
            // even for a token issued before a role change.
            foreach (var claim in identity.FindAll(identity.RoleClaimType).ToList()) identity.RemoveClaim(claim);
            identity.AddClaim(new System.Security.Claims.Claim(identity.RoleClaimType, membership.Role.ToString()));
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    // "Session" covers the caller's own account and session (me, logout,
    // memberships, switching, password change). "Account" additionally
    // requires a member role for workspace content (issue #80): only
    // administrators and regular members sign in to use content, so a
    // membership still carrying the legacy Contractor role is denied.
    options.AddPolicy("Session", policy => policy.RequireAuthenticatedUser());
    options.AddPolicy("Account", policy => policy.RequireAuthenticatedUser()
        .RequireRole(Qontract.API.AppRoles.TenantAdmin, Qontract.API.AppRoles.User));
    options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser().RequireRole(Qontract.API.AppRoles.TenantAdmin, Qontract.API.AppRoles.User)
        .RequireAssertion(context =>
            int.TryParse(context.User.FindFirst("TenantId")?.Value, out var tenantId) && tenantId > 0).Build();
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

// (issue #80) No "Server: Kestrel" banner on responses.
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

var app = builder.Build();

// Security headers (issue #80), registered first so successful responses,
// downloads and error envelopes all carry them. OnStarting runs after the
// exception middleware has rewritten an error response.
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        // Tokens, account data, contract lists and documents must not be
        // kept by browsers or intermediaries.
        if (context.Request.Path.StartsWithSegments("/api")) headers.CacheControl = "no-store";
        return Task.CompletedTask;
    });
    await next();
});

// Negotiate only UI culture. Numeric/date parsing remains invariant and the
// same API values round-trip regardless of the display language.
var languages = new[] { new System.Globalization.CultureInfo("en"), new System.Globalization.CultureInfo("de") };
app.UseRequestLocalization(new Microsoft.AspNetCore.Builder.RequestLocalizationOptions
{
    DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(System.Globalization.CultureInfo.InvariantCulture, languages[0]),
    SupportedCultures = new[] { System.Globalization.CultureInfo.InvariantCulture },
    SupportedUICultures = languages,
    RequestCultureProviders = new[] { new Microsoft.AspNetCore.Localization.AcceptLanguageHeaderRequestCultureProvider() }
});
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers.ContentLanguage = Qontract.API.Localization.UserMessages.Language;
        context.Response.Headers.Append("Vary", "Accept-Language");
        return Task.CompletedTask;
    });
    await next();
});

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
app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (app.Environment.EnvironmentName == "Development")
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!isDevelopment)
{
    // Emitted for HTTPS requests (as seen through the trusted proxy's
    // forwarded headers). Configure HSTS at the TLS edge as well.
    app.UseHsts();
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
app.MapGet("/health/ready", async (ApplicationDbContext database, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.StatusCode(await database.Database.CanConnectAsync(cancellationToken) ? 200 : 503);
    }
    catch
    {
        return Results.StatusCode(503);
    }
}).AllowAnonymous().DisableRateLimiting();

// Apply migrations and seed data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var context = services.GetRequiredService<ApplicationDbContext>();
    
    // Apply migrations if needed
    context.Database.Migrate();
    
    if (isDevelopment)
    {
        var userManager = services.GetRequiredService<IUserService>();
        await SeedDataAsync(context, userManager);
    }
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
            EmailConfirmedAt = DateTime.UtcNow,
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
            EmailConfirmedAt = DateTime.UtcNow,
            TenantId = tenant2.Id,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };

        context.Users.Add(user2);
        await context.SaveChangesAsync();
    }
}
