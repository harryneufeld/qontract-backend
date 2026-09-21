using Microsoft.EntityFrameworkCore;
using FluentValidation;
using Qontract.Infrastructure.Data;
using Qontract.Application.Services;
using Qontract.Application.Interfaces;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Qontract.Core.Interfaces;
using Qontract.Infrastructure.FileStorage;

var builder = WebApplication.CreateBuilder(args);

// Get connection string from configuration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=QontractDb.sqlite";

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Enums as strings so API consumers (Flutter app) work with readable values
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register FluentValidation validators (live in Qontract.Application;
// IValidator<T> is resolved per-DTO in controllers)
builder.Services.AddValidatorsFromAssembly(typeof(Qontract.Application.Services.IContractorService).Assembly);

// Register DbContext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlite(connectionString, b => b.MigrationsAssembly("Qontract.Infrastructure"));
    options.EnableSensitiveDataLogging();
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

// Register File Storage
builder.Services.AddSingleton<IFileStorage, FileStorage>();

// Configure JWT
// Resolve + validate the JWT settings up front so a missing Jwt:Key fails
// fast with a clear message (or falls back to a dev-only key in Development)
// instead of blowing up with an unhandled ArgumentNullException on the
// first authenticated request (issue #10).
var isDevelopment = builder.Environment.EnvironmentName == "Development";
var jwtKey = builder.Configuration["Jwt:Key"];
var usedJwtFallbackKey = false;
if (string.IsNullOrWhiteSpace(jwtKey))
{
    if (isDevelopment)
    {
        // Development-only fallback so the API runs out of the box.
        // This key is NOT secret-safe and must never be used in production.
        jwtKey = "qontract-development-fallback-key-DO-NOT-USE-IN-PROD";
        usedJwtFallbackKey = true;
    }
    else
    {
        throw new InvalidOperationException(
            "Jwt:Key is not configured. Set the 'Jwt:Key' configuration value " +
            "(appsettings.json, environment variable Jwt__Key, or user secrets) " +
            "to a secret of at least 32 characters.");
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
        UpdatedAt = DateTime.UtcNow
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
        UpdatedAt = DateTime.UtcNow
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
            UpdatedAt = DateTime.UtcNow
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
            UpdatedAt = DateTime.UtcNow
        };

        context.Users.Add(user2);
        await context.SaveChangesAsync();
    }
}