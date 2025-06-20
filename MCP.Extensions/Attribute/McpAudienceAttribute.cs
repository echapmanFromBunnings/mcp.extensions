namespace MCP.Extensions.Attribute;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class McpAudienceAttribute : System.Attribute
{
    public string[] Audiences { get; }

    public McpAudienceAttribute(params string[] audiences)
    {
        Audiences = audiences ?? new string[0];
    }
}