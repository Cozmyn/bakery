using System.Text;
using Bakery.Api.Data;
using Bakery.Api.Models;
using Bakery.Api.Services;
using Bakery.Api.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));

builder.Services.AddDbContext<AppDbContext>(opt =>
{
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Db"));
});

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var cs = builder.Configuration.GetValue<string>("Redis:ConnectionString") ?? "localhost:6379";
    return ConnectionMultiplexer.Connect(cs);
});

builder.Services.AddSingleton<IVisImageCache, VisImageCache>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<SimulatorManager>();
builder.Services.AddScoped<OeeService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<AnalyticsBucketService>();

// Background jobs (industrial logic, still single-process in ETAPA 3)
builder.Services.AddHostedService<LineMonitoringService>();
builder.Services.AddHostedService<TrackingJobService>();
builder.Services.AddHostedService<RunClosingService>();
builder.Services.AddHostedService<AlertingService>();

builder.Services.AddControllers();

builder.Services.AddCors(opt =>
{
    opt.AddPolicy("dev", p => p
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()
        .SetIsOriginAllowed(_ => true));
});

// Basic rate limiting (safe default)
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("fixed", o =>
    {
        o.PermitLimit = 200;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        o.QueueLimit = 50;
    });
});

// JWT auth
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = key,
            NameClaimType = "email",
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(opt =>
{
    opt.AddPolicy("AdminOnly", p => p.RequireRole(UserRole.Admin.ToString()));
    opt.AddPolicy("OperatorOrAdmin", p => p.RequireRole(UserRole.Admin.ToString(), UserRole.Operator.ToString()));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Bakery API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme."
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            new string[] { }
        }
    });
});

var app = builder.Build();

app.UseCors("dev");
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<AuditMiddleware>();

app.MapControllers().RequireRateLimiting("fixed");

// Seed on startup (dev)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbMigrationService.ApplyAsync(db);
    await SeedService.EnsureSeedAsync(db);
}

app.Run();
