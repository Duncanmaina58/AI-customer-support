using System.Text;
using Api.Hubs;
using Api.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
var builder = WebApplication.CreateBuilder(args);

// ---- Services ----------------------------------------------------------------

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "AI Support Platform API", Version = "v1" });

    // Lets you click "Authorize" in Swagger UI and paste a Bearer token to test
    // authenticated endpoints without a separate HTTP client.
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
// Two policies:
//  "DashboardCors" — allows only the dashboard origin (authenticated agents).
//  "WidgetCors"    — allows any origin for the embeddable chat widget (SignalR hub
//                    and public API endpoints). Per-company origin validation is a
//                    Sprint 6 hardening task (validate against registered widget URLs).

const string DashboardCorsPolicy = "DashboardCors";
const string WidgetCorsPolicy = "WidgetCors";

builder.Services.AddCors(options =>
{
    options.AddPolicy(DashboardCorsPolicy, policy =>
    {
        policy
            .WithOrigins(
                builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                    ?? ["http://localhost:5173"])
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials(); // required for SignalR WebSocket upgrade
    });

    // Widget is embedded on arbitrary third-party websites so we must allow any origin.
    // Tenant isolation is achieved via the public API key lookup, not by origin checking.
    options.AddPolicy(WidgetCorsPolicy, policy =>
    {
        policy
            .SetIsOriginAllowed(_ => true)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials(); // required for SignalR
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
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization();

// ---- Build ------------------------------------------------------------------

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Apply CORS before auth so the browser's preflight OPTIONS request is handled
// before it can be rejected by an auth check.
app.UseCors(DashboardCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// ChatHub uses WidgetCors so anonymous widget visitors on external sites can connect.
// All other SignalR usage (e.g. future agent notifications) stays on DashboardCors.
app.MapHub<ChatHub>("/hubs/chat").RequireCors(WidgetCorsPolicy);
// Auto-migrate on startup (safe for Render deploys)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}
app.Run();
