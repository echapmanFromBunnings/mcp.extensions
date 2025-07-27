using System.Reflection;
using MCP.Extensions.Attribute;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace MCP.Extensions.Services;

public class AudienceFilterService(ILogger<AudienceFilterService> logger) : IAudienceFilterService
{
    private readonly Dictionary<string, Dictionary<string, string[]>> _resourceAudiences = new();
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
        logger.LogInformation("Scanning assembly {Name} for McpAudience attributes...", assembly.GetName().Name);

        foreach (var type in assembly.GetTypes())
        {
            logger.LogDebug("Scanning type {TypeFullName} in assembly {AssemblyName}.", type.FullName, assembly.GetName().Name);
            foreach (
                var method in type.GetMethods(
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Instance
                )
            )
            {
                logger.LogTrace("Inspecting method {TypeFullName}.{MethodName} for resource and audience attributes.", type.FullName, method.Name);
                
                // Check for tools
                var mcpServerToolAttribute = method.GetCustomAttribute<McpServerToolAttribute>();
                var mcpAudienceAttribute = method.GetCustomAttribute<McpAudienceAttribute>();

                if (mcpServerToolAttribute != null && mcpAudienceAttribute != null)
                {
                    if (mcpServerToolAttribute.Name != null)
                        RegisterResource("tool", mcpServerToolAttribute.Name, mcpAudienceAttribute.Audiences,
                            type.FullName, method.Name);
                }

                // Check for prompts
                var mcpServerPromptAttribute = method.GetCustomAttribute<McpServerPromptAttribute>();
                if (mcpServerPromptAttribute != null && mcpAudienceAttribute != null)
                {
                    RegisterResource("prompt", mcpServerPromptAttribute.Name, mcpAudienceAttribute.Audiences, type.FullName, method.Name);
                }

                // Check for resources
                var mcpServerResourceAttribute = method.GetCustomAttribute<McpServerResourceAttribute>();
                if (mcpServerResourceAttribute != null && mcpAudienceAttribute != null)
                {
                    // For resources, we'll use the URI as the resource identifier
                    string resourceIdentifier = mcpServerResourceAttribute.UriTemplate ?? mcpServerResourceAttribute.Name ?? "";
                    if (!string.IsNullOrEmpty(resourceIdentifier))
                    {
                        RegisterResource("resource", resourceIdentifier, mcpAudienceAttribute.Audiences, type.FullName, method.Name);
                    }
                    else
                    {
                        logger.LogWarning(
                            "Method {TypeFullName}.{MethodName} has McpServerResourceAttribute but no UriTemplate or Name. Skipping.", 
                            type.FullName, method.Name
                        );
                    }
                }
            }
        }
        
        logger.LogInformation("Finished scanning assembly {Name}. Total resource types: {Count}.", 
            assembly.GetName().Name, _resourceAudiences.Count);
    }

    private void RegisterResource(string resourceType, string resourceName, string[] audiences, string typeName, string methodName)
    {
        if (string.IsNullOrEmpty(resourceName))
        {
            logger.LogWarning(
                "Method {TypeFullName}.{MethodName} has resource attribute but Name is null or empty. Skipping.", 
                typeName, methodName
            );
            return;
        }

        if (!_resourceAudiences.ContainsKey(resourceType))
        {
            _resourceAudiences[resourceType] = new Dictionary<string, string[]>();
        }

        if (_resourceAudiences[resourceType].ContainsKey(resourceName))
        {
            logger.LogWarning(
                "Duplicate {ResourceType} name '{Name}' found for method {TypeFullName}.{MethodName}. Overwriting previous audience entry.", 
                resourceType, resourceName, typeName, methodName
            );
        }

        _resourceAudiences[resourceType][resourceName] = audiences;
        logger.LogDebug(
            "Registered {ResourceType} '{Name}' with audiences: [{Join}] for method {TypeFullName}.{MethodName}", 
            resourceType, resourceName, string.Join(", ", audiences), typeName, methodName
        );
    }

    public IReadOnlyDictionary<string, string[]> GetAudiences(string resourceType)
    {
        return _resourceAudiences.TryGetValue(resourceType, out var audiences) 
            ? audiences 
            : new Dictionary<string, string[]>();
    }

    public string[] GetAudiencesForResource(string resourceType, string resourceName)
    {
        if (_resourceAudiences.TryGetValue(resourceType, out var typeAudiences))
        {
            return typeAudiences.TryGetValue(resourceName, out var audiences) ? audiences : Array.Empty<string>();
        }
        return Array.Empty<string>();
    }
}

public static class AudienceFilterServiceRegistrationExtensions
{
    /// <summary>
    /// Registers the AudienceFilterService as a singleton
    /// Enabling runtime discovery of resource audiences if called in the startup.
    /// </summary>
    /// <param name="services">service collection</param>
    /// <returns>service collection</returns>
    public static IServiceCollection AddAudienceFilterService(this IServiceCollection services)
    {
        services.AddSingleton<IAudienceFilterService, AudienceFilterService>();
        return services;
    }
}
