using System.Reflection;

namespace MCP.Extensions.Services;

/// <summary>
/// A service interface for managing tool audiences.
/// </summary>
public interface IToolAudienceService
{
    /// <summary>
    /// Get all the registered tool audiences.
    /// </summary>
    /// <returns>Dictionary of tool, with the audiences available</returns>
    IReadOnlyDictionary<string, string[]> GetToolAudiences();
    
    /// <summary>
    /// Get the audiences for a specific tool by its name.
    /// </summary>
    /// <param name="toolName">the tool name</param>
    /// <returns></returns>
    string[] GetAudiencesForTool(string toolName);
    
    /// <summary>
    /// Register an additional assembly to scan for tools and audiences.
    /// </summary>
    /// <param name="assembly">The assembly to scan</param>
    void RegisterAssembly(Assembly assembly);
}