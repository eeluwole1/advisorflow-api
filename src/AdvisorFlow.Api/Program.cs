using AdvisorFlow.Api.ApiDocs;
using AdvisorFlow.Api.ErrorHandling;
using AdvisorFlow.Api.Features;
using AdvisorFlow.Application.Activities;
using AdvisorFlow.Application.Dashboard;
using AdvisorFlow.Application.Households;
using AdvisorFlow.Application.Opportunities;
using AdvisorFlow.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// --- Application + infrastructure ---------------------------------------------
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<HouseholdService>();
builder.Services.AddScoped<OpportunityService>();
builder.Services.AddScoped<ActivityService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.Configure<ApiFeatures>(builder.Configuration.GetSection(ApiFeatures.SectionName));
builder.Services.AddScoped<WritesEnabledFilter>();

// --- Web API plumbing ---------------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<CrmExceptionHandler>();
builder.Services.AddHealthChecks();

// The frontend is a separate application (advisorflow-web) on its own origin,
// so the allowed origins are configured per environment.
const string FrontendCors = "Frontend";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy(FrontendCors, policy => policy
    .WithOrigins(allowedOrigins)
    .WithMethods("GET", "POST", "PUT") // No DELETE: records are deactivated or closed, not deleted (ADR-008).
    .AllowAnyHeader()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    // Swagger UI at /swagger, backed by the versioned contract (docs/api/openapi.v1.yaml).
    app.MapApiDocs();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors(FrontendCors);

app.MapHealthChecks("/health");
app.MapControllers();

app.Logger.LogInformation("AdvisorFlow API started with CRM provider: {Provider}; writes allowed: {AllowWrites}",
    app.Configuration["Crm:Provider"] ?? "Mock",
    app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiFeatures>>().Value.AllowWrites);

app.Run();

/// <summary>Exposed so integration tests can host the API with WebApplicationFactory.</summary>
public partial class Program;
