using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCP.Extensions.Attribute;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace MCP.Extensions.Services;

public class ToolAudienceService : IToolAudienceService
{
    private readonly Dictionary<string, string[]> _toolAudiences = new();
    private readonly ILogger<ToolAudienceService> _logger;
    private readonly List<Assembly> _assemblies = new();

    public ToolAudienceService(ILogger<ToolAudienceService> logger)
    {
        _logger = logger;
        // Register the executing assembly by default
        RegisterAssembly(Assembly.GetExecutingAssembly());
    }

    public void RegisterAssembly(Assembly assembly)
    {
        if (!_assemblies.Contains(assembly))
        {
            _assemblies.Add(assembly);
            ScanAssembly(assembly);
        }
    }

    private void ScanAssembly(Assembly assembly)
    {
        _logger.LogInformation("Scanning assembly {Name} for McpAudience and McpServerTool attributes...", assembly.GetName().Name);

        foreach (var type in assembly.GetTypes())
        {
            foreach (
                var method in type.GetMethods(
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Instance
                )
            )
            {
                var mcpServerToolAttribute = method.GetCustomAttribute<McpServerToolAttribute>();
                var mcpAudienceAttribute = method.GetCustomAttribute<McpAudienceAttribute>();

                if (mcpServerToolAttribute != null && mcpAudienceAttribute != null)
                {
                    if (string.IsNullOrEmpty(mcpServerToolAttribute.Name))
                    {
                        _logger.LogWarning(
                            "Method {TypeFullName}.{MethodName} has McpServerToolAttribute but Name is null or empty. Skipping.", type.FullName, method.Name
                        );
                        continue;
                    }

                    if (_toolAudiences.ContainsKey(mcpServerToolAttribute.Name))
                    {
                        _logger.LogWarning(
                            "Duplicate tool name '{Name}' found for method {TypeFullName}.{MethodName}. Overwriting previous audience entry.", mcpServerToolAttribute.Name, type.FullName, method.Name
                        );
                        continue;
                    }
                    _toolAudiences[mcpServerToolAttribute.Name] = mcpAudienceAttribute.Audiences;
                    _logger.LogDebug(
                        "Registered tool '{Name}' with audiences: [{Join}] for method {TypeFullName}.{MethodName}", mcpServerToolAttribute.Name, string.Join(", ", mcpAudienceAttribute.Audiences ?? Array.Empty<string>()), type.FullName, method.Name
                    );
                }
            }
        }
    }
    public IReadOnlyDictionary<string, string[]> GetToolAudiences()
    {
        return _toolAudiences;
    }

    public string[] GetAudiencesForTool(string toolName)
    {
        return _toolAudiences.TryGetValue(toolName, out var audiences) ? audiences : Array.Empty<string>();
    }
}

public static class ToolAudienceRegistrationExtensions
{
    /// <summary>
    /// Registers the ToolAudienceService as a singleton and scans the provided assemblies
    /// for tool and audience attributes, enabling runtime discovery of tool audiences.
    /// </summary>
    /// <param name="services">service collection</param>
    /// <param name="assemblies">assemblies to scan</param>
    /// <returns>service collection</returns>
    public static IServiceCollection AddToolAudienceService(this IServiceCollection services, List<Assembly> assemblies)
    {
        services.AddSingleton<IToolAudienceService, ToolAudienceService>();
        var toolAudienceService = services.BuildServiceProvider().GetRequiredService<IToolAudienceService>();
        foreach (var assembly in assemblies)
        {
            toolAudienceService.RegisterAssembly(assembly);
        }
        return services;
    }
}