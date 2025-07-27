using System.Reflection;

namespace MCP.Extensions.Services;

/// <summary>
/// A service interface for managing audience-based filtering for different resource types.
/// </summary>
public interface IAudienceFilterService
{
    /// <summary>
    /// Get all the registered audiences for a specific resource type.
    /// </summary>
    /// <param name="resourceType">The type of resource (e.g., "tool", "resource")</param>
    /// <returns>Dictionary of resource name with the audiences available</returns>
    IReadOnlyDictionary<string, string[]> GetAudiences(string resourceType);
    
    /// <summary>
    /// Get the audiences for a specific resource by its name and type.
    /// </summary>
    /// <param name="resourceType">The type of resource (e.g., "tool", "resource")</param>
    /// <param name="resourceName">The resource name</param>
    /// <returns>Array of allowed audiences</returns>
    string[] GetAudiencesForResource(string resourceType, string resourceName);
    
    /// <summary>
    /// Register an additional assembly to scan for resources and audiences.
    /// </summary>
    /// <param name="assembly">The assembly to scan</param>
    void RegisterAssembly(Assembly assembly);
}
