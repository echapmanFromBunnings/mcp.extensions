
using MCP.Extensions.Middleware;
using MCP.Extensions.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace MCP.Extensions;

/// <summary>
/// Extension methods for configuring MCP audience-based filtering in an application.
/// Provides a unified way to register services and apply all filtering middlewares.
/// </summary>
public static class McpFilteringExtensions
{
    /// <summary>
    /// Adds all MCP audience filtering services to the dependency injection container.
    /// This registers the unified audience filter service that handles tools, resources, and prompts.
    /// </summary>
    /// <param name="services">The service collection to configure</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddMcpAudienceFiltering(this IServiceCollection services)
    {
        services.AddAudienceFilterService();
        return services;
    }

    /// <summary>
    /// Adds all MCP audience filtering services and automatically scans the specified assembly
    /// for tools, resources, and prompts with audience attributes.
    /// </summary>
    /// <param name="services">The service collection to configure</param>
    /// <param name="assembly">The assembly to scan for MCP attributes</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddMcpAudienceFiltering(this IServiceCollection services, Assembly assembly)
    {
        services.AddAudienceFilterService();
        
        // Register the assembly for scanning during service creation
        services.Configure<McpFilteringOptions>(options =>
        {
            options.AssembliesToScan.Add(assembly);
        });
        
        return services;
    }

    /// <summary>
    /// Adds all MCP audience filtering services and automatically scans the calling assembly
    /// for tools, resources, and prompts with audience attributes.
    /// </summary>
    /// <param name="services">The service collection to configure</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddMcpAudienceFilteringWithAutoScan(this IServiceCollection services)
    {
        var callingAssembly = Assembly.GetCallingAssembly();
        return services.AddMcpAudienceFiltering(callingAssembly);
    }

    /// <summary>
    /// Applies all MCP audience filtering middlewares to the application pipeline.
    /// This includes filtering for tools, resources, and prompts based on X-AGENT-MODE header.
    /// Middlewares are applied in order: Tools -> Resources -> Prompts.
    /// </summary>
    /// <param name="app">The application builder to configure</param>
    /// <returns>The application builder for method chaining</returns>
    public static IApplicationBuilder UseMcpAudienceFiltering(this IApplicationBuilder app)
    {
        // Apply all filtering middlewares in a logical order
        app.UseToolFiltering();
        app.UseResourceFiltering();
        app.UsePromptFiltering();
        
        return app;
    }

    /// <summary>
    /// Applies all MCP audience filtering middlewares and ensures the audience filter service
    /// is configured with the specified assemblies to scan.
    /// </summary>
    /// <param name="app">The application builder to configure</param>
    /// <param name="assembliesToScan">Additional assemblies to scan for MCP attributes</param>
    /// <returns>The application builder for method chaining</returns>
    public static IApplicationBuilder UseMcpAudienceFiltering(this IApplicationBuilder app, params Assembly[] assembliesToScan)
    {
        // Register additional assemblies with the audience filter service
        var audienceService = app.ApplicationServices.GetService<IAudienceFilterService>();
        if (audienceService != null)
        {
            foreach (var assembly in assembliesToScan)
            {
                audienceService.RegisterAssembly(assembly);
            }
        }

        return app.UseMcpAudienceFiltering();
    }

    /// <summary>
    /// Comprehensive setup method that configures both services and middlewares for MCP audience filtering.
    /// This is the most convenient method for basic setups.
    /// </summary>
    /// <param name="app">The application builder to configure</param>
    /// <returns>The application builder for method chaining</returns>
    public static IApplicationBuilder UseCompleteMcpFiltering(this IApplicationBuilder app)
    {
        // Ensure services are registered (this will be a no-op if already registered)
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddMcpAudienceFilteringWithAutoScan();
        
        // Apply all middlewares
        return app.UseMcpAudienceFiltering();
    }
}

/// <summary>
/// Configuration options for MCP audience filtering.
/// </summary>
public class McpFilteringOptions
{
    /// <summary>
    /// List of assemblies to scan for MCP attributes during service initialization.
    /// </summary>
    public List<Assembly> AssembliesToScan { get; set; } = new();
}

/// <summary>
/// Legacy compatibility extensions for existing IToolAudienceService usage.
/// These methods provide backward compatibility while transitioning to the unified service.
/// </summary>
public static class LegacyMcpFilteringExtensions
{
    /// <summary>
    /// Adds the legacy tool audience service. 
    /// This is maintained for backward compatibility but consider migrating to AddMcpAudienceFiltering.
    /// </summary>
    /// <param name="services">The service collection to configure</param>
    /// <returns>The service collection for method chaining</returns>
    [Obsolete("Use AddMcpAudienceFiltering instead. This method is maintained for backward compatibility.")]
    public static IServiceCollection AddToolAudienceService(this IServiceCollection services)
    {
        services.AddSingleton<IToolAudienceService, ToolAudienceService>();
        return services;
    }

    /// <summary>
    /// Applies only the tool filtering middleware.
    /// This is maintained for backward compatibility but consider using UseMcpAudienceFiltering.
    /// </summary>
    /// <param name="app">The application builder to configure</param>
    /// <returns>The application builder for method chaining</returns>
    [Obsolete("Use UseMcpAudienceFiltering instead. This method is maintained for backward compatibility.")]
    public static IApplicationBuilder UseToolFiltering(this IApplicationBuilder app)
    {
        return app.UseMiddleware<StreamingToolFilteringMiddleware>();
    }
}
