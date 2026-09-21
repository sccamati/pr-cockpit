using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PRCockpit.Application.Ports;
using PRCockpit.Infrastructure.Analysis;
using PRCockpit.Infrastructure.AzureDevOps;
using PRCockpit.Infrastructure.Persistence;

namespace PRCockpit.Infrastructure;

/// <summary>
/// The one place that names concrete adapters. Everything above this layer sees only the
/// ports, which is what keeps the dependency direction pointing inwards.
/// </summary>
public static class InfrastructureServices
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<IAzureDevOpsClient, AzureDevOpsClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddDbContext<PrCockpitContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("PrCockpit")));

        // Scoped, because they depend on the scoped DbContext.
        services.AddScoped<IChecklistStore, ChecklistStore>();
        services.AddScoped<ISummaryStore, SummaryStore>();
        services.AddScoped<IReviewProgressStore, ReviewProgressStore>();
        services.AddScoped<IFileExplanationStore, FileExplanationStore>();
        services.AddScoped<IFileQuestionStore, FileQuestionStore>();

        // Stateless: one process spawner and one Roslyn wrapper.
        services.AddSingleton<IAiSummaryAnalyzer, CliSummaryAnalyzer>();
        services.AddSingleton<ICSharpHoverAnalyzer, CSharpHoverAnalyzer>();

        return services;
    }
}
