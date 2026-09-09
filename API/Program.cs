using System.Reflection;
using System.Collections.Concurrent;
using API.Contracts;
using API.Infrastructure;
using API.Models;
using API.Services;
using API.Persistence.SysDatabase;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel.Connectors.Qdrant;

var builder = WebApplication.CreateBuilder(args);
var qdrantHost = builder.Configuration["Qdrant:Host"]?.Trim();
if (string.IsNullOrWhiteSpace(qdrantHost))
{
    qdrantHost = "localhost";
}

var qdrantGrpcPort = builder.Configuration.GetValue<int?>("Qdrant:GrpcPort") ?? 6334;

const string FrontendCorsPolicy = "FrontendCorsPolicy";

builder.Services.AddScoped<GeminiQueryPlanningService>();
builder.Services.AddScoped<IInsuranceChatCompletionService, InsuranceChatCompletionRouterService>();
builder.Services.AddScoped<GroqQueryPlanningService>();
builder.Services.AddScoped<GptQueryPlanningService>();
builder.Services.AddScoped<GithubQueryPlanningService>();
builder.Services.AddScoped<NimQueryPlanningService>();
builder.Services.AddScoped<MyLlamaQueryPlanningService>();
builder.Services.AddSingleton<ConcurrentDictionary<string, List<string>>>();
builder.Services.Configure<PiperLivePlayerSettings>(builder.Configuration.GetSection(PiperLivePlayerSettings.SectionName));
builder.Services.AddHttpClient();
builder.Services.AddQdrantVectorStore(
    host: qdrantHost,
    port: qdrantGrpcPort,
    https: false,
    apiKey: null,
    options: new QdrantVectorStoreOptions(),
    lifetime: ServiceLifetime.Singleton);

// 註冊系統資料庫上下文
builder.Services.AddDbContext<SysDatabaseContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("SysDatabase")));

builder.Services.AddScoped<IQueryPlanningService>(serviceProvider =>
{
    var llmProvider = LlmProviderResolver.ResolveProvider(serviceProvider.GetRequiredService<IConfiguration>());

    if (llmProvider.Equals("Gpt", StringComparison.OrdinalIgnoreCase))
    {
        return serviceProvider.GetRequiredService<GptQueryPlanningService>();
    }

    if (llmProvider.Equals("Groq", StringComparison.OrdinalIgnoreCase))
    {
        return serviceProvider.GetRequiredService<GroqQueryPlanningService>();
    }

    if (llmProvider.Equals("Github", StringComparison.OrdinalIgnoreCase))
    {
        return serviceProvider.GetRequiredService<GithubQueryPlanningService>();
    }

    if (llmProvider.Equals("Nim", StringComparison.OrdinalIgnoreCase))
    {
        return serviceProvider.GetRequiredService<NimQueryPlanningService>();
    }

    if (llmProvider.Equals("MyLlama", StringComparison.OrdinalIgnoreCase))
    {
        return serviceProvider.GetRequiredService<MyLlamaQueryPlanningService>();
    }

    return serviceProvider.GetRequiredService<GeminiQueryPlanningService>();
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "TextToSql.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.None;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.IsEssential = true;
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsJsonAsync(new ResponseDataSchema<object?>
                {
                    Code = StatusCodes.Status401Unauthorized,
                    Msg = "未登入或登入已失效，請先登入。",
                    Data = null,
                    Details = null,
                    TraceId = context.HttpContext.TraceIdentifier
                });
            },
            OnRedirectToAccessDenied = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsJsonAsync(new ResponseDataSchema<object?>
                {
                    Code = StatusCodes.Status403Forbidden,
                    Msg = "目前使用者沒有查詢權限。",
                    Data = null,
                    Details = null,
                    TraceId = context.HttpContext.TraceIdentifier
                });
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IClaimsTransformation, RoleClaimsTransformation>();
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        policy
            .WithOrigins("http://localhost:5219", "https://localhost:7034")
            .AllowCredentials()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddControllers(options =>
{
    options.Filters.Add<ApiExceptionFilter>();
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddAttributedServices(Assembly.GetExecutingAssembly());
builder.Services.AddSingleton<IGeminiContextCacheService, GeminiContextCacheService>();
var contextCacheEnabled = builder.Configuration.GetValue<bool>("LlmSettings:ContextCacheEnabled");
if (contextCacheEnabled)
{
    builder.Services.AddHostedService(serviceProvider =>
        (GeminiContextCacheService)serviceProvider.GetRequiredService<IGeminiContextCacheService>());
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors(FrontendCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

var insuranceBrainService = app.Services.GetRequiredService<IInsuranceBrainService>();
insuranceBrainService.EnsureInitialized();

app.Run();
