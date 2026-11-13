using System.Text;
using System.Text.Json;
using FOMS.Application;
using FOMS.Api.Services;
using FOMS.Infrastructure;
using FOMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Auto-load .env file if running locally
var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());
for (var dir = currentDir; dir != null; dir = dir.Parent)
{
    var envPath = Path.Combine(dir.FullName, ".env");
    if (File.Exists(envPath))
    {
        foreach (var line in File.ReadAllLines(envPath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
            var parts = trimmed.Split('=', 2);
            if (parts.Length == 2)
            {
                var key = parts[0].Trim();
                var val = parts[1].Trim().Trim('"').Trim('\'');
                Environment.SetEnvironmentVariable(key, val);
                if (key.Contains("__"))
                {
                    var configKey = key.Replace("__", ":");
                    builder.Configuration[configKey] = val;
                }
                else
                {
                    builder.Configuration[key] = val;
                }
            }
        }
        break;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// TASK 4: JWT SECRET VALIDATION
// Fail fast at startup if the JWT secret is missing or too short.
// A weak secret allows attackers to forge tokens for any role.
// ─────────────────────────────────────────────────────────────────────────────
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secret = builder.Configuration["JWT_SECRET"];
if (string.IsNullOrWhiteSpace(secret))
{
    secret = builder.Configuration["JwtSettings:Secret"];
}
if (string.IsNullOrWhiteSpace(secret))
{
    secret = Environment.GetEnvironmentVariable("JWT_SECRET");
}

static bool IsPlaceholderSecret(string? val)
{
    if (string.IsNullOrWhiteSpace(val)) return true;
    var trimmed = val.Trim();
    string[] forbidden = new[]
    {
        "replace_with",
        "change_me",
        "change-me",
        "your_",
        "example",
        "placeholder",
        "FOMS_Capstone_JWT_SecureKey",
        "dev-secret-key-change-in-production",
        "1234567890"
    };
    return forbidden.Any(f => trimmed.Contains(f, StringComparison.OrdinalIgnoreCase));
}

if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32 || IsPlaceholderSecret(secret))
{
    throw new InvalidOperationException(
        "Fatal Startup Error: JWT Secret is missing, too short (minimum 32 characters required for HMAC-SHA256), or contains a placeholder value. " +
        "Configure a cryptographically secure 'JWT_SECRET' in deployment configuration.");
}
builder.Configuration["JwtSettings:Secret"] = secret;

FOMS.Infrastructure.Configuration.DatabaseConfiguration.ValidateDatabaseSecrets(builder.Configuration);

var starsInboundKey = builder.Configuration["StarsIntegration:InboundApiKey"];
if (!string.IsNullOrWhiteSpace(starsInboundKey) && IsPlaceholderSecret(starsInboundKey))
{
    throw new InvalidOperationException("Fatal Startup Error: StarsIntegration:InboundApiKey contains a placeholder value.");
}

var dmsInboundKey = builder.Configuration["DmsIntegration:InboundApiKey"];
if (!string.IsNullOrWhiteSpace(dmsInboundKey) && IsPlaceholderSecret(dmsInboundKey))
{
    throw new InvalidOperationException("Fatal Startup Error: DmsIntegration:InboundApiKey contains a placeholder value.");
}

// ─────────────────────────────────────────────────────────────────────────────
// APPLICATION SERVICES
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddSingleton<RetentionArchiveService>();
builder.Services.AddHostedService<RetentionArchiveWorker>();

var fomsDbConnection = FOMS.Infrastructure.Configuration.DatabaseConfiguration.GetConnectionString(builder.Configuration);

builder.Services.AddDbContext<FomsDbContext>(options =>
    options.UseSqlServer(fomsDbConnection));

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddControllers(options =>
{
    options.Filters.Add<FOMS.Api.Filters.ApiValidationFilter>();
})
.AddJsonOptions(options =>
{
    options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
});
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "FOMS API", Version = "v1" });
});

// ─────────────────────────────────────────────────────────────────────────────
// CORS SECURITY
// Explicit allowed origins from configuration without wildcards or blanket suffixes.
// ─────────────────────────────────────────────────────────────────────────────
var configuredOrigins = builder.Configuration
    .GetSection("AllowedCorsOrigins")
    .Get<string[]>()
    ?? builder.Configuration["CORS_ALLOWED_ORIGINS"]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? Array.Empty<string>();

var allowedOrigins = configuredOrigins.Length > 0
    ? configuredOrigins
    : new[] { "http://localhost:5173", "http://localhost:4173" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("FomsPolicy", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// ─────────────────────────────────────────────────────────────────────────────
// AUTHENTICATION — JWT BEARER
// TASK 5: RequireHttpsMetadata is true in production, false only in development
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    // TASK 5: Enforce HTTPS in production. Development still works over HTTP.
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.GetValue<string>("Issuer"),
        ValidAudience = jwtSettings.GetValue<string>("Audience"),
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
        ClockSkew = TimeSpan.FromSeconds(30) // tight clock skew — tokens expire precisely
    };
    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            // Do not expose exception details to the client
            context.Response.Headers.Append("X-Auth-Failed", "true");
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

var app = builder.Build();

// ─────────────────────────────────────────────────────────────────────────────
// TASK 7: GLOBAL EXCEPTION HANDLER MIDDLEWARE
// Catches all unhandled exceptions and returns a consistent JSON response.
// Stack traces and exception types are NEVER exposed to clients.
// Mapping:
//   UnauthorizedAccessException  → 401 Unauthorized
//   ArgumentException            → 400 Bad Request
//   InvalidOperationException    → 409 Conflict
//   Everything else              → 500 Internal Server Error
// ─────────────────────────────────────────────────────────────────────────────
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exceptionFeature = context.Features
            .Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();

        if (exceptionFeature == null) return;

        var exception = exceptionFeature.Error;
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();

        // Log the full exception internally — never send to client
        logger.LogError(exception, "Unhandled exception on {Method} {Path}",
            context.Request.Method, context.Request.Path);

        int statusCode;
        string message;

        switch (exception)
        {
            case FOMS.Application.Exceptions.ValidationException valEx:
                statusCode = StatusCodes.Status400BadRequest;
                message = "Validation failed";
                break;

            case UnauthorizedAccessException:
                statusCode = StatusCodes.Status401Unauthorized;
                message = exception.Message.Contains("locked")
                    ? exception.Message
                    : "Authentication failed. Please verify your credentials.";
                break;

            case ArgumentException:
                statusCode = StatusCodes.Status400BadRequest;
                message = exception.Message;
                break;

            case InvalidOperationException:
                statusCode = StatusCodes.Status409Conflict;
                message = exception.Message;
                break;

            case KeyNotFoundException:
                statusCode = StatusCodes.Status404NotFound;
                message = exception.Message;
                break;

            default:
                statusCode = StatusCodes.Status500InternalServerError;
                // Never expose internal details in production
                message = app.Environment.IsDevelopment()
                    ? $"Internal error: {exception.Message}"
                    : "An unexpected error occurred. Please try again later.";
                break;
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        object response;
        if (exception is FOMS.Application.Exceptions.ValidationException validationEx)
        {
            response = new
            {
                success = false,
                message = "Validation failed",
                errors = validationEx.Errors
            };
        }
        else
        {
            response = new
            {
                success = false,
                message,
                statusCode
            };
        }

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(response,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    });
});

// ─────────────────────────────────────────────────────────────────────────────
// MIDDLEWARE PIPELINE
// Order matters: Exception handler → HTTPS Redirect → CORS → Auth → Authorization
// ─────────────────────────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "FOMS API v1");
    c.RoutePrefix = "swagger";
});

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWith("/ws", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrEmpty(path) && path != "/")
    {
        context.Request.Path = "/api" + path;
    }
    await next();
});

app.UseRouting();
app.UseCors("FomsPolicy");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/", () => Results.Redirect("/swagger"));

// ─────────────────────────────────────────────────────────────────────────────
// DATABASE INITIALIZATION & MIGRATIONS
// Applies pending migrations on startup. Fails fast if migrations cannot be applied
// so the service never reports healthy or operates on an unmigrated schema.
// ─────────────────────────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    var context = services.GetRequiredService<ApplicationDbContext>();

    await FOMS.Infrastructure.Persistence.DatabaseMigrationRunner.ApplyMigrationsAsync(context, logger);

    logger.LogInformation("Database connected. Mock/seeded data disabled - running 100% on live database.");
}

await app.RunAsync();
