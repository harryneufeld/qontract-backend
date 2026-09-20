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
builder.Services.AddControllers();
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
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(builder.Configuration["Jwt:Key"]!)),
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidateAudience = true,
        ValidAudience = builder.Configuration["Jwt:Audience"],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(builder =>
    {
        builder.WithOrigins("http://localhost:3000", "https://localhost:3000")
               .AllowAnyHeader()
               .AllowAnyMethod()
               .AllowCredentials();
    });
});

// Add CORS policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalhost",
        builder => builder
            .WithOrigins("http://localhost:3000", "https://localhost:3000")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

var app = builder.Build();

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