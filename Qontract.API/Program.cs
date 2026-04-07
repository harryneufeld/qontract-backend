using Microsoft.EntityFrameworkCore;
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

    // Create admin user
    if (!await context.Users.AnyAsync(u => u.Email == "admin@qontract.com"))
    {
        var user = new User
        {
            Name = "Admin User",
            Email = "admin@qontract.com",
            PasswordHash = "2b7f3a5d9f3e8c1a:8:password123:8", // SHA256 hash of "password123"
            Role = UserRole.TenantAdmin,
            IsActive = true,
            TenantId = tenant.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();
    }
}