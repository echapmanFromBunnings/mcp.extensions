using System.Reflection;
using MCP.Extensions.Attribute;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace MCP.Extensions.Services;

public class ToolAudienceService(ILogger<ToolAudienceService> logger) : IToolAudienceService
{
    private readonly Dictionary<string, string[]> _toolAudiences = new();
    private readonly List<Assembly> _assemblies = new();

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
        logger.LogInformation("Scanning assembly {Name} for McpAudience and McpServerTool attributes...", assembly.GetName().Name);

        foreach (var type in assembly.GetTypes())
        {
            logger.LogDebug("Scanning type {TypeFullName} in assembly {AssemblyName}.", type.FullName, assembly.GetName().Name);
            foreach (
                var method in type.GetMethods(
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Instance
                )
            )
            {
                logger.LogTrace("Inspecting method {TypeFullName}.{MethodName} for tool and audience attributes.", type.FullName, method.Name);
                var mcpServerToolAttribute = method.GetCustomAttribute<McpServerToolAttribute>();
                var mcpAudienceAttribute = method.GetCustomAttribute<McpAudienceAttribute>();

                if (mcpServerToolAttribute != null && mcpAudienceAttribute != null)
                {
                    if (string.IsNullOrEmpty(mcpServerToolAttribute.Name))
                    {
                        logger.LogWarning(
                            "Method {TypeFullName}.{MethodName} has McpServerToolAttribute but Name is null or empty. Skipping.", type.FullName, method.Name
                        );
                        continue;
                    }

                    if (_toolAudiences.ContainsKey(mcpServerToolAttribute.Name))
                    {
                        logger.LogWarning(
                            "Duplicate tool name '{Name}' found for method {TypeFullName}.{MethodName}. Overwriting previous audience entry.", mcpServerToolAttribute.Name, type.FullName, method.Name
                        );
                        continue;
                    }
                    _toolAudiences[mcpServerToolAttribute.Name] = mcpAudienceAttribute.Audiences;
                    logger.LogDebug(
                        "Registered tool '{Name}' with audiences: [{Join}] for method {TypeFullName}.{MethodName}", mcpServerToolAttribute.Name, string.Join(", ", mcpAudienceAttribute.Audiences), type.FullName, method.Name
                    );
                }
            }
        }
        logger.LogInformation("Finished scanning assembly {Name}. Total audience tool count is {Count}.", assembly.GetName().Name, _toolAudiences.Count);
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
    /// Registers the ToolAudienceService as a singleton
    /// Enabling runtime discovery of tool audiences if called in the startup.
    /// </summary>
    /// <param name="services">service collection</param>
    /// <returns>service collection</returns>
    public static IServiceCollection AddToolAudienceService(this IServiceCollection services)
    {
        services.AddSingleton<IToolAudienceService, ToolAudienceService>();
        return services;
    }
}