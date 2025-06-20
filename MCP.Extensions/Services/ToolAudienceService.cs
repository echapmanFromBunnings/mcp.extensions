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

    public ToolAudienceService(ILogger<ToolAudienceService> logger)
    {
        _logger = logger;
        Initialize();
    }

    private void Initialize()
    {
        _logger.LogInformation("Scanning assembly for McpAudience and McpServerTool attributes...");
        // Assuming tools are in the currently executing assembly.
        // If tools can be in other assemblies, this might need adjustment.
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var type in assembly.GetTypes())
        {
            // Considering static methods as per current tool implementations.
            // Add BindingFlags.Instance if tools can also be instance methods.
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
                            $"Method {type.FullName}.{method.Name} has McpServerToolAttribute but Name is null or empty. Skipping."
                        );
                        continue;
                    }

                    if (_toolAudiences.ContainsKey(mcpServerToolAttribute.Name))
                    {
                        // This could happen if different methods (e.g. overloads, or in different classes if names are not unique globally)
                        // are decorated with the same McpServerToolAttribute.Name.
                        // Depending on desired behavior, this could be an error, or merge audiences, or overwrite.
                        // Current implementation overwrites.
                        _logger.LogWarning(
                            $"Duplicate tool name '{mcpServerToolAttribute.Name}' found for method {type.FullName}.{method.Name}. Overwriting previous audience entry."
                        );
                    }
                    _toolAudiences[mcpServerToolAttribute.Name] = mcpAudienceAttribute.Audiences;
                    _logger.LogDebug(
                        $"Registered tool '{mcpServerToolAttribute.Name}' with audiences: [{string.Join(", ", mcpAudienceAttribute.Audiences ?? Array.Empty<string>())}] for method {type.FullName}.{method.Name}"
                    );
                }
            }
        }
        _logger.LogInformation($"Finished scanning. Found {_toolAudiences.Count} tools with audience information.");
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
    public static IServiceCollection AddToolAudienceService(this IServiceCollection services)
    {
        services.AddSingleton<IToolAudienceService, ToolAudienceService>();
        return services;
    }
}