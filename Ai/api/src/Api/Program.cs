using System.Text;
using Api.Hubs;
using Api.Infrastructure;
using Api.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;

// ---- Sprint 8: bootstrap logger --------------------------------------------
// Captures anything that goes wrong before the full Serilog pipeline (which
// needs configuration/DI) is up — otherwise a startup crash before that point
// would be silent. Replaced by the fully configured logger a few lines down.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting up");

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfig) => loggerConfig
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentName()
    .Enrich.WithProperty("Application", "AiSupportPlatform.Api")
    // Structured JSON to stdout — this is what "Serilog logs all requests and
    // errors in structured JSON format" (Sprint 8 checklist) means in practice:
    // every field (RequestPath, StatusCode, Elapsed, exception, CompanyId when
    // enriched via LogContext elsewhere) comes through as real JSON properties
    // a log aggregator (Render's log stream, Datadog, etc.) can filter/query on,
    // not just interpolated into a message string.
    .WriteTo.Console(new CompactJsonFormatter())
    // EF Core's per-query "Executed DbCommand" logs are extremely noisy at
    // Information and add no value in production — demoted here rather than
    // silently relying on appsettings (defence in depth: still overridable
    // there too, see appsettings.json's Serilog:MinimumLevel:Override).
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning));

// ---- Render PostgreSQL env vars → Npgsql connection string ------------------
// Render injects individual DB_* variables. We assemble them into the
// key-value format Npgsql expects and inject it into configuration so
// DependencyInjection.cs picks it up via GetConnectionString("Default").
var dbHost     = Environment.GetEnvironmentVariable("DB_HOST");
var dbPort     = Environment.GetEnvironmentVariable("DB_PORT") ?? "5432";
var dbName     = Environment.GetEnvironmentVariable("DB_NAME");
var dbUser     = Environment.GetEnvironmentVariable("DB_USER");
var dbPassword = Environment.GetEnvironmentVariable("DB_PASSWORD");

if (dbHost is not null)
{
    builder.Configuration["ConnectionStrings:Default"] =
        $"Host={dbHost};Port={dbPort};Database={dbName};" +
        $"Username={dbUser};Password={dbPassword};" +
        $"SSL Mode=Require;Trust Server Certificate=true";
}

// ---- Services ----------------------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "AI Support Platform API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// ---- CORS -------------------------------------------------------------------
// ---- CORS -------------------------------------------------------------------
const string DashboardCorsPolicy = "DashboardCors";
const string WidgetCorsPolicy    = "WidgetCors";

builder.Services.AddCors(options =>
{
    options.AddPolicy(DashboardCorsPolicy, policy =>
    {
        var allowedOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>()
            ?? ["https://ai-customind.vercel.app"];

        // Sprint 8 security audit: the dashboard authenticates via a JWT in the
        // Authorization header (see web/src/lib/api.ts — token lives in
        // localStorage, never a cookie), so credentialed CORS isn't needed here
        // either. Kept to a fixed origin allowlist (unlike WidgetCors) since
        // only this platform's own dashboard should ever call these endpoints.
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });

    options.AddPolicy(WidgetCorsPolicy, policy =>
    {
        // Sprint 8 security audit: this must allow literally any origin, since
        // any customer's website can embed the chat widget — but it must NOT
        // allow credentials. The widget authenticates purely via a public key
        // baked into the embed snippet (see ChatHub.ResolveCompanyAsync), never
        // via cookies, so AllowCredentials() here would only add attack surface
        // (any site could make credentialed cross-origin calls) for zero benefit.
        policy
            .SetIsOriginAllowed(_ => true)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSignalR();

var jwtSigningKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException("Jwt:SigningKey is not configured.");

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuer              = builder.Configuration["Jwt:Issuer"],
            ValidateAudience         = true,
            ValidAudience            = builder.Configuration["Jwt:Audience"],
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(
                                           Encoding.UTF8.GetBytes(jwtSigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization();

// ---- Sprint 8: rate limiting --------------------------------------------------
// Every public endpoint gets a sane default limit (partitioned per client IP, or
// per authenticated agent when a JWT is present, so one heavy dashboard user
// can't starve others behind the same office NAT). Auth endpoints get a much
// tighter, separate policy to blunt credential-stuffing/brute-force attempts —
// see AuthController's [EnableRateLimiting("auth")].
const string AuthRateLimitPolicy = "auth";

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = (context, ct) =>
    {
        Log.Warning(
            "Rate limit exceeded | path={Path} ip={Ip}",
            context.HttpContext.Request.Path,
            context.HttpContext.Connection.RemoteIpAddress);
        return ValueTask.CompletedTask;
    };

    options.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        // Webhook and health-check traffic is provider-driven (Meta/Telegram
        // retries, uptime pings), not a human clicking around — exempt it from
        // the general-purpose limiter rather than risk a legitimate provider
        // retry storm tripping it. These are separately hardened: webhooks via
        // VerifyMetaSignatureAttribute / the Telegram secret_token check.
        var path = httpContext.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/webhook/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/api/health", StringComparison.OrdinalIgnoreCase))
            return System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter(path);

        var partitionKey = httpContext.User?.FindFirst(HttpCurrentTenantProvider.AgentIdClaimType)?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";

        return System.Threading.RateLimiting.RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => new System.Threading.RateLimiting.SlidingWindowRateLimiterOptions
        {
            PermitLimit = 300,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 4,
            QueueLimit = 0,
        });
    });

    // Tight limit for login/register — a handful of genuine attempts per
    // minute is normal; dozens is a credential-stuffing bot.
    options.AddSlidingWindowLimiter(AuthRateLimitPolicy, o =>
    {
        o.PermitLimit = 10;
        o.Window = TimeSpan.FromMinutes(1);
        o.SegmentsPerWindow = 2;
        o.QueueLimit = 0;
    });
});

// Sprint 8 security audit: Render (and similar PaaS) terminates TLS and proxies
// every request through an edge load balancer, so without this,
// HttpContext.Connection.RemoteIpAddress is the *proxy's* IP for every single
// request — collapsing the rate limiter's per-IP partitioning into one shared
// bucket for all traffic, and making the RemoteIp field in every structured
// log line useless for the audit trail. KnownNetworks/KnownProxies are cleared
// because on PaaS deployments like this one there's no fixed, publishable
// proxy IP/CIDR to pin — this is the standard pattern for such platforms.
// Safe here specifically because the app is not reachable except through that
// proxy (no direct network path exists for a client to fake the header
// against the origin directly).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// ---- Build ------------------------------------------------------------------
var app = builder.Build();

// Must run before anything that reads the client IP (rate limiter, request
// logging, HTTPS redirection) or the scheme (HTTPS redirection again).
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    // Sprint 8 security audit: never let an unhandled exception's stack trace,
    // connection string, or internal type names reach a client response in
    // production — that's a textbook data-leakage finding. Full detail still
    // goes to the structured Serilog error log, just not to the HTTP response body.
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
            if (feature?.Error is { } ex)
                Log.Error(ex, "Unhandled exception | path={Path}", context.Request.Path);

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("""{"message":"An unexpected error occurred."}""");
        });
    });
}

// Structured request logging — every request gets one JSON log line with
// method/path/status/elapsed automatically; enriched below with tenant
// context when the caller is authenticated, so log queries can filter
// "everything company X did" without touching a single controller.
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        var companyId = httpContext.User?.FindFirst(HttpCurrentTenantProvider.CompanyIdClaimType)?.Value;
        if (companyId is not null)
            diagnosticContext.Set("CompanyId", companyId);

        diagnosticContext.Set("RemoteIp", httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    };
});

app.UseHttpsRedirection();
app.UseCors(DashboardCorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat").RequireCors(WidgetCorsPolicy);

// ---- Auto-migrate on startup ------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}