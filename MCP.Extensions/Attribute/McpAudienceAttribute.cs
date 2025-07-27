namespace MCP.Extensions.Attribute;

/// <summary>
/// Attribute to specify the audience for a method in MCP.
/// This is used to control access to tools, resources and prompts based on the audience(s).
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class McpAudienceAttribute : System.Attribute
{
    public string[] Audiences { get; }

    public McpAudienceAttribute(params string[] audiences)
    {
        Audiences = audiences ?? new string[0];
    }
}