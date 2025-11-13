# McpAudienceAttribute and Streaming Implementation Guide

## Table of Contents
1. [Overview](#overview)
2. [The Challenge: Resource Segmentation in MCP Servers](#the-challenge-resource-segmentation-in-mcp-servers)
3. [Solution: McpAudienceAttribute](#solution-mcpaudienceattribute)
4. [Architecture and Design](#architecture-and-design)
5. [Streaming Implementation](#streaming-implementation)
6. [Usage Examples](#usage-examples)
7. [Benefits and Use Cases](#benefits-and-use-cases)
8. [Security Considerations](#security-considerations)
9. [Best Practices](#best-practices)
10. [Technical Deep Dive](#technical-deep-dive)

---

## Overview

The `McpAudienceAttribute` is a powerful attribute-based access control mechanism designed for Model Context Protocol (MCP) servers. It enables fine-grained segmentation of MCP resources (tools, resources, and prompts) based on audience types, allowing a single MCP server to efficiently serve multiple client types with different permission levels.

Combined with its streaming implementation, this system provides real-time filtering of MCP responses based on the requesting client's audience credentials, enabling maximum utilization of server resources while maintaining strict security boundaries.

---

## The Challenge: Resource Segmentation in MCP Servers

### Problem Statement

In modern MCP deployments, a single server often needs to serve multiple types of clients with different permission levels and access requirements. Consider these scenarios:

1. **Multi-tenant Systems**: A SaaS platform hosting multiple customers who should only access their own data
2. **Role-based Access**: Different user roles (admin, developer, read-only) requiring different tool sets
3. **Environment Segmentation**: Production vs. staging environments with different capabilities
4. **Client Type Differentiation**: Public API clients vs. internal system integrations
5. **Feature Flags**: Progressive rollout of features to specific audience segments

### Traditional Approaches and Limitations

Before `McpAudienceAttribute`, common approaches included:

1. **Separate Server Instances**: Running multiple MCP servers for different audiences
   - **Drawbacks**: High operational overhead, resource duplication, maintenance complexity

2. **Manual Filtering Logic**: Implementing custom filtering in each tool/resource handler
   - **Drawbacks**: Error-prone, inconsistent security, scattered authorization logic

3. **Post-Processing Filters**: Filtering complete responses after generation
   - **Drawbacks**: Wastes computation, potential information leakage, inefficient

### The Need for a Better Solution

What's needed is a **declarative, efficient, and centralized** approach that:
- Allows defining audience restrictions at the point of declaration (on the method/tool itself)
- Filters responses in real-time during streaming to avoid wasted computation
- Provides consistent security enforcement across all resource types
- Enables maximum server resource utilization by consolidating audiences into a single deployment

---

## Solution: McpAudienceAttribute

### What is McpAudienceAttribute?

`McpAudienceAttribute` is a C# attribute that can be applied to MCP tool, resource, and prompt methods to declare which audiences are authorized to access them.

```csharp
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class McpAudienceAttribute : System.Attribute
{
    public string[] Audiences { get; }
    
    public McpAudienceAttribute(params string[] audiences)
    {
        Audiences = audiences ?? new string[0];
    }
}
```

### Core Concept

The attribute works in conjunction with:
1. **HTTP Header**: Clients send their audience type(s) via the `X-AGENT-MODE` header
2. **Streaming Middleware**: Intercepts and filters MCP responses in real-time
3. **Audience Filter Service**: Scans assemblies to build a registry of resource-to-audience mappings

---

## Architecture and Design

### Component Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                         MCP Client                               │
│                  (sends X-AGENT-MODE: PRODUCTS)                  │
└───────────────────────────┬─────────────────────────────────────┘
                            │ HTTP Request
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                    ASP.NET Core Middleware Pipeline              │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  StreamingToolFilteringMiddleware                        │   │
│  │  StreamingResourceFilteringMiddleware                    │   │
│  │  StreamingPromptFilteringMiddleware                      │   │
│  └──────────────────────┬───────────────────────────────────┘   │
│                         │                                        │
│                         ▼                                        │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │        FilteringWriteStream (per resource type)          │   │
│  │    - Parses JSON response stream                         │   │
│  │    - Consults AudienceFilterService                      │   │
│  │    - Removes unauthorized items in real-time             │   │
│  └──────────────────────┬───────────────────────────────────┘   │
└────────────────────────┬┴──────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│                  IAudienceFilterService                          │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  Dictionary<resourceType, Dictionary<name, audiences[]>>  │   │
│  │  - "tool" -> { "get_pricing": ["PRODUCTS", "ADMIN"] }    │   │
│  │  - "resource" -> { "sales-data": ["ADMIN"] }             │   │
│  │  - "prompt" -> { "summarize": ["PRODUCTS"] }             │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                         ▲
                         │ Assembly Scanning at Startup
                         │
┌─────────────────────────────────────────────────────────────────┐
│                    Your MCP Server Code                          │
│                                                                  │
│  [McpServerTool("get_pricing")]                                 │
│  [McpAudience("PRODUCTS", "ADMIN")]                             │
│  public async Task<PricingData> GetPricing() { ... }            │
│                                                                  │
│  [McpServerTool("delete_user")]                                 │
│  [McpAudience("ADMIN")]                                         │
│  public async Task DeleteUser(string userId) { ... }            │
│                                                                  │
│  [McpServerResource("sales-data", UriTemplate = "sales://...")]│
│  [McpAudience("ADMIN", "FINANCE")]                              │
│  public async Task<SalesData> GetSalesData() { ... }            │
└─────────────────────────────────────────────────────────────────┘
```

### Key Design Principles

1. **Declarative Security**: Audience restrictions are declared alongside the resource definition
2. **Separation of Concerns**: Authorization logic is separated from business logic
3. **Efficient Streaming**: Filtering happens during response streaming, not post-processing
4. **Type Safety**: Strongly-typed service interfaces and attributes
5. **Extensibility**: Easy to add new resource types or extend filtering logic

---

## Streaming Implementation

### Why Streaming Matters

Traditional approaches filter complete responses after they're generated. This has several problems:

1. **Wasted Computation**: Server generates data that will be discarded
2. **Memory Overhead**: Full response must be buffered before filtering
3. **Latency**: Client waits for full generation before receiving filtered results
4. **Information Leakage Risk**: Brief windows where unauthorized data exists in memory

### How Streaming Works

The streaming implementation uses custom `Stream` wrappers that intercept response data as it's being written:

```csharp
public class FilteringWriteStream : Stream
{
    private readonly StringBuilder _dataBuffer = new StringBuilder();
    private bool _inToolsArray = false;
    private bool _firstToolWrittenInArray = false;
    
    public override async Task WriteAsync(byte[] buffer, int offset, int count, 
                                         CancellationToken cancellationToken)
    {
        // Append incoming chunk to buffer
        string chunk = Encoding.UTF8.GetString(buffer, offset, count);
        _dataBuffer.Append(chunk);
        
        // Process buffered data, filtering as we go
        await ProcessBufferedDataAsync(cancellationToken, false);
    }
}
```

### Processing Flow

1. **Chunk Reception**: Data arrives in chunks from the MCP server response
2. **Buffer Management**: Chunks are accumulated until complete JSON objects can be parsed
3. **Pattern Detection**: System detects when entering/exiting resource arrays (`"tools":[`, `"resources":[`, etc.)
4. **Object Extraction**: Complete JSON objects are extracted using brace-counting logic
5. **Audience Check**: Each object's name/URI is checked against the audience registry
6. **Selective Writing**: Only authorized objects are written to the underlying stream
7. **Comma Management**: Proper JSON array comma handling for removed items

### State Machine for JSON Parsing

```
┌─────────────────┐
│  Outside Array  │ ─────────┐
└────────┬────────┘          │
         │                    │
         │ Detect "tools":[   │
         ▼                    │ Write non-array
┌─────────────────┐          │ content through
│  Inside Array   │          │
└────────┬────────┘          │
         │                    │
         │ Parse tool JSON ◄──┘
         │ Check audience
         ├─ Keep? Write it
         └─ Remove? Skip it
         │
         │ Detect "]"
         ▼
    Back to Outside Array
```

### Example Streaming Scenario

**Input Stream** (from MCP server):
```json
{
  "result": {
    "tools": [
      {"name": "get_pricing", "description": "Get pricing info"},
      {"name": "delete_user", "description": "Delete a user"},
      {"name": "view_products", "description": "View products"}
    ]
  }
}
```

**Client Header**: `X-AGENT-MODE: PRODUCTS`

**Audience Configuration**:
- `get_pricing` → `["PRODUCTS", "ADMIN"]`
- `delete_user` → `["ADMIN"]`
- `view_products` → `["PRODUCTS"]`

**Output Stream** (to client):
```json
{
  "result": {
    "tools": [
      {"name": "get_pricing", "description": "Get pricing info"},
      {"name": "view_products", "description": "View products"}
    ]
  }
}
```

The `delete_user` tool is removed in real-time because the `PRODUCTS` audience doesn't have access.

---

## Usage Examples

### Basic Setup

#### 1. Register Services

```csharp
// In Program.cs or Startup.cs
builder.Services.AddMcpAudienceFiltering(Assembly.GetExecutingAssembly());
```

#### 2. Apply Middleware

```csharp
app.UseMcpAudienceFiltering();
```

#### 3. Decorate Your Tools

```csharp
public class ProductTools
{
    [McpServerTool("get_products")]
    [McpAudience("PRODUCTS", "ADMIN")]
    public async Task<List<Product>> GetProducts()
    {
        // Returns products - accessible to PRODUCTS and ADMIN audiences
    }
    
    [McpServerTool("update_inventory")]
    [McpAudience("ADMIN")]
    public async Task UpdateInventory(string productId, int quantity)
    {
        // Updates inventory - accessible only to ADMIN audience
    }
}
```

### Advanced: Multiple Audiences

A single client can present multiple audience types:

**Client Request**:
```http
GET /mcp/tools
X-AGENT-MODE: PRODUCTS,READ_ONLY
```

**Server Response**: Returns tools that match ANY of `["PRODUCTS", "READ_ONLY"]`

### Real-World Example: E-commerce Platform

```csharp
public class EcommerceMcpServer
{
    // Public-facing tools - available to customer-facing apps
    [McpServerTool("search_products")]
    [McpAudience("PUBLIC", "PRODUCTS")]
    public async Task<List<Product>> SearchProducts(string query) { }
    
    // Internal tools - available to staff applications
    [McpServerTool("view_orders")]
    [McpAudience("STAFF", "ADMIN")]
    public async Task<List<Order>> ViewOrders(string customerId) { }
    
    // Administrative tools - available only to admins
    [McpServerTool("delete_customer")]
    [McpAudience("ADMIN")]
    public async Task DeleteCustomer(string customerId) { }
    
    // Financial resources - restricted to finance team
    [McpServerResource("financial-report", UriTemplate = "finance://reports/{id}")]
    [McpAudience("FINANCE", "ADMIN")]
    public async Task<FinancialReport> GetFinancialReport(string id) { }
    
    // Analytics prompts - available to multiple teams
    [McpServerPrompt("sales_summary")]
    [McpAudience("SALES", "MARKETING", "ADMIN")]
    public async Task<PromptResult> GenerateSalesSummary() { }
}
```

### Deployment Scenarios

#### Scenario 1: Single Server, Multiple Frontends

```
┌──────────────────┐
│  Customer App    │ ─── X-AGENT-MODE: PUBLIC ────┐
└──────────────────┘                               │
                                                   │
┌──────────────────┐                               ▼
│  Staff Portal    │ ─── X-AGENT-MODE: STAFF ─────┤
└──────────────────┘                               │
                                                   │   ┌──────────────────┐
┌──────────────────┐                               ├──▶│   MCP Server     │
│  Admin Console   │ ─── X-AGENT-MODE: ADMIN ─────┤   │  (Single Deploy) │
└──────────────────┘                               │   └──────────────────┘
                                                   │
┌──────────────────┐                               │
│  Analytics Tool  │ ─── X-AGENT-MODE: FINANCE ───┘
└──────────────────┘
```

**Benefits**:
- Single codebase to maintain
- Shared infrastructure and resources
- Consistent behavior across audiences
- Easy to add new audiences

---

## Benefits and Use Cases

### 1. Maximum Resource Utilization

**Problem**: Running separate MCP servers for each audience type wastes resources.

**Solution**: A single server instance serves all audiences, filtered appropriately.

**Impact**:
- Reduced infrastructure costs (1 server instead of N)
- Simplified deployment and operations
- Better resource utilization (shared memory, caching, connections)

### 2. Consistent Security Enforcement

**Problem**: Manual filtering in each tool is error-prone and inconsistent.

**Solution**: Centralized, declarative security at the attribute level.

**Impact**:
- Security defined alongside functionality (single source of truth)
- Automated enforcement via middleware
- Reduced risk of security bugs
- Easy security auditing (scan for `[McpAudience]` attributes)

### 3. Flexible Multi-Tenancy

**Problem**: SaaS applications need to isolate tenant data.

**Solution**: Use audience types to represent tenants.

```csharp
[McpServerTool("get_customer_data")]
[McpAudience("TENANT_ABC", "TENANT_XYZ")]
public async Task<CustomerData> GetCustomerData(string customerId)
{
    // Only accessible to TENANT_ABC and TENANT_XYZ
}
```

### 4. Progressive Feature Rollout

**Problem**: Want to test new features with specific audiences before general release.

**Solution**: Use audience types for feature flags.

```csharp
[McpServerTool("new_analytics_v2")]
[McpAudience("BETA_USERS", "ADMIN")]
public async Task<AnalyticsV2> GetAnalyticsV2()
{
    // New feature available only to beta users and admins
}

[McpServerTool("analytics")]
[McpAudience("PUBLIC")]  // Old version still available to everyone
public async Task<Analytics> GetAnalytics()
{
    // Original implementation
}
```

### 5. Regulatory Compliance

**Problem**: Different regulations (GDPR, HIPAA, etc.) require different data access controls.

**Solution**: Segment audiences by regulatory requirements.

```csharp
[McpServerResource("pii-data", UriTemplate = "data://pii/{userId}")]
[McpAudience("GDPR_AUTHORIZED", "ADMIN")]
public async Task<PIIData> GetPIIData(string userId)
{
    // Strict access control for personally identifiable information
}
```

### 6. Development and Testing

**Problem**: Need different tool sets for different environments.

**Solution**: Use audiences to represent environments.

```csharp
[McpServerTool("debug_dump")]
[McpAudience("DEVELOPMENT", "STAGING")]
public async Task<DebugInfo> GetDebugInfo()
{
    // Only available in non-production environments
}

[McpServerTool("reset_database")]
[McpAudience("DEVELOPMENT")]
public async Task ResetDatabase()
{
    // Dangerous operation only in development
}
```

---

## Security Considerations

### 1. Defense in Depth

**McpAudienceAttribute is the first line of defense, not the only one.**

```csharp
[McpServerTool("delete_user")]
[McpAudience("ADMIN")]
public async Task DeleteUser(string userId, ClaimsPrincipal user)
{
    // Attribute provides first-level filtering
    // But still validate within the method:
    if (!user.IsInRole("Administrator"))
        throw new UnauthorizedException();
    
    // Additional authorization checks
    if (!await CanDeleteUser(user, userId))
        throw new ForbiddenException();
    
    await _userService.DeleteUser(userId);
}
```

### 2. Header Authentication

The `X-AGENT-MODE` header must be:
- Validated and authenticated (not just trusted)
- Set by authenticated middleware, not client input
- Mapped from authenticated user claims

**Example**:
```csharp
public class AudienceMappingMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var user = context.User;
        
        // Map authenticated user to audience types
        var audiences = new List<string>();
        
        if (user.IsInRole("Admin"))
            audiences.Add("ADMIN");
        if (user.HasClaim("Department", "Finance"))
            audiences.Add("FINANCE");
        if (user.HasClaim("AccessLevel", "Staff"))
            audiences.Add("STAFF");
        
        // Set the header based on authenticated claims
        context.Request.Headers["X-AGENT-MODE"] = string.Join(",", audiences);
        
        await next(context);
    }
}
```

### 3. No Audience = Maximum Security

When a resource has the `[McpAudience]` attribute:
- If `X-AGENT-MODE` header is missing → Resource is **removed**
- If `X-AGENT-MODE` is empty → Resource is **removed**

This "secure by default" behavior prevents accidental exposure.

### 4. Unrestricted Resources

Resources **without** `[McpAudience]` are available to all audiences:

```csharp
[McpServerTool("get_public_info")]
// No McpAudience attribute = publicly accessible
public async Task<PublicInfo> GetPublicInfo()
{
    return new PublicInfo { Version = "1.0" };
}
```

Use this sparingly and intentionally for truly public resources.

### 5. Audit Logging

Implement audit logging to track audience-based access:

```csharp
public class AuditLoggingMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var agentMode = context.Request.Headers["X-AGENT-MODE"].ToString();
        var path = context.Request.Path;
        var user = context.User.Identity?.Name;
        
        _logger.LogInformation(
            "MCP Request: User={User}, Audiences={Audiences}, Path={Path}",
            user, agentMode, path
        );
        
        await next(context);
    }
}
```

---

## Best Practices

### 1. Naming Conventions

Use clear, consistent audience names:

**Good**:
```csharp
[McpAudience("ADMIN")]           // Clear role
[McpAudience("FINANCE")]         // Clear department
[McpAudience("READ_ONLY")]       // Clear permission level
[McpAudience("PRODUCTION")]      // Clear environment
```

**Avoid**:
```csharp
[McpAudience("A")]               // Unclear abbreviation
[McpAudience("Grp1")]            // Meaningless name
[McpAudience("special_users")]   // Inconsistent casing
```

### 2. Principle of Least Privilege

Grant the minimum audiences necessary:

```csharp
// Good - specific audiences
[McpServerTool("view_salary")]
[McpAudience("HR", "PAYROLL")]
public async Task<Salary> ViewSalary(string employeeId) { }

// Bad - too permissive
[McpServerTool("view_salary")]
[McpAudience("HR", "PAYROLL", "STAFF", "ADMIN", "MANAGERS")]
public async Task<Salary> ViewSalary(string employeeId) { }
```

### 3. Document Your Audiences

Create a central reference document:

```csharp
/// <summary>
/// Audience Types Used in This Application:
/// - ADMIN: System administrators with full access
/// - FINANCE: Finance department staff
/// - HR: Human resources department
/// - STAFF: Regular employees
/// - PUBLIC: Unauthenticated or public-facing clients
/// - DEVELOPMENT: Development environment only
/// - STAGING: Staging environment
/// - PRODUCTION: Production environment
/// </summary>
public static class AudienceTypes
{
    public const string Admin = "ADMIN";
    public const string Finance = "FINANCE";
    public const string HR = "HR";
    public const string Staff = "STAFF";
    public const string Public = "PUBLIC";
    public const string Development = "DEVELOPMENT";
    public const string Staging = "STAGING";
    public const string Production = "PRODUCTION";
}

// Usage:
[McpAudience(AudienceTypes.Admin, AudienceTypes.Finance)]
```

### 4. Testing Different Audiences

Create integration tests for different audiences:

```csharp
[Fact]
public async Task AdminCanAccessAllTools()
{
    var client = CreateClientWithAudience("ADMIN");
    var response = await client.GetAsync("/mcp/tools");
    var tools = await ParseTools(response);
    
    Assert.Contains(tools, t => t.Name == "delete_user");
    Assert.Contains(tools, t => t.Name == "view_products");
}

[Fact]
public async Task PublicCannotAccessAdminTools()
{
    var client = CreateClientWithAudience("PUBLIC");
    var response = await client.GetAsync("/mcp/tools");
    var tools = await ParseTools(response);
    
    Assert.DoesNotContain(tools, t => t.Name == "delete_user");
}
```

### 5. Graceful Degradation

Design your client applications to handle varying tool availability:

```csharp
// Client-side code
var availableTools = await mcpClient.ListTools();

if (availableTools.Contains("delete_user"))
{
    // Show delete button
    UI.ShowDeleteButton();
}
else
{
    // Hide delete functionality
    UI.HideDeleteButton();
}
```

### 6. Version Your Audiences

When audience requirements change, version them:

```csharp
// Old version - being phased out
[McpServerTool("legacy_report")]
[McpAudience("FINANCE_V1")]
public async Task<Report> GetLegacyReport() { }

// New version - current
[McpServerTool("report")]
[McpAudience("FINANCE_V2", "ADMIN")]
public async Task<ReportV2> GetReport() { }
```

### 7. Monitor Filtered Resources

Track which resources are being filtered out:

```csharp
// In your logging configuration
builder.Services.Configure<LoggerFilterOptions>(options =>
{
    // Log when tools are removed
    options.AddFilter("MCP.Extensions.Middleware.FilteringWriteStream", 
                      LogLevel.Information);
});
```

Review logs to ensure filtering is working as expected and identify potential issues.

---

## Technical Deep Dive

### Assembly Scanning Process

When `AddMcpAudienceFiltering(assembly)` is called:

1. **Type Discovery**: Reflects over all types in the assembly
2. **Method Scanning**: Examines all public, private, static, and instance methods
3. **Attribute Detection**: Looks for combinations of:
   - `[McpServerTool]` + `[McpAudience]` → Registers as "tool"
   - `[McpServerResource]` + `[McpAudience]` → Registers as "resource"
   - `[McpServerPrompt]` + `[McpAudience]` → Registers as "prompt"
4. **Registry Building**: Populates internal dictionaries:
   ```csharp
   Dictionary<string, Dictionary<string, string[]>> _resourceAudiences
   // Example:
   // {
   //   "tool": {
   //     "get_pricing": ["PRODUCTS", "ADMIN"],
   //     "delete_user": ["ADMIN"]
   //   },
   //   "resource": {
   //     "sales://data": ["FINANCE", "ADMIN"]
   //   }
   // }
   ```

### Middleware Pipeline Order

The order matters:

```csharp
app.UseMcpAudienceFiltering();
// Expands to:
app.UseMiddleware<StreamingToolFilteringMiddleware>();
app.UseMiddleware<StreamingResourceFilteringMiddleware>();
app.UseMiddleware<StreamingPromptFilteringMiddleware>();
```

Each middleware wraps the response stream, creating a chain:

```
Original Response Stream
    ↓ wrapped by
StreamingToolFilteringMiddleware
    ↓ wrapped by
StreamingResourceFilteringMiddleware
    ↓ wrapped by
StreamingPromptFilteringMiddleware
    ↓
Final Filtered Stream to Client
```

### Performance Characteristics

**Time Complexity**:
- Audience lookup: O(1) via dictionary
- JSON parsing: O(n) where n = response size
- Filtering decision: O(m) where m = number of audiences (typically small)

**Space Complexity**:
- Buffer size: O(k) where k = size of largest JSON object in the array
- Registry size: O(p) where p = number of registered resources

**Streaming Benefits**:
- Memory usage: Constant per request (small buffer)
- No full response buffering required
- Start sending filtered data immediately

### Edge Cases Handled

1. **Partial JSON objects**: Buffer accumulates until complete
2. **Nested objects**: Brace counting correctly handles nested structures
3. **Special characters in strings**: JSON parsing handles escaping
4. **Empty arrays**: Correctly outputs `[]`
5. **Commas**: Proper JSON array syntax maintained when items are removed
6. **Multiple audiences in header**: CSV parsing with trimming and normalization
7. **Case sensitivity**: Audience comparison is case-insensitive

### Thread Safety

- **AudienceFilterService**: Thread-safe for read operations after initialization
- **Middleware**: Each request gets its own stream instance
- **Registry**: Populated once at startup, read-only during request processing

---

## Conclusion

The `McpAudienceAttribute` combined with its streaming implementation provides a robust, efficient, and developer-friendly solution for segmenting MCP server resources. By enabling audience-based access control with minimal code and maximum performance, it allows organizations to:

- **Maximize resource utilization** by consolidating multiple audiences into a single server deployment
- **Ensure security** through declarative, consistent enforcement
- **Improve maintainability** with clear, co-located authorization rules
- **Reduce costs** by eliminating the need for separate server instances
- **Enhance flexibility** for multi-tenancy, feature flags, and role-based access

Whether you're building a multi-tenant SaaS platform, implementing role-based access control, or managing progressive feature rollouts, `McpAudienceAttribute` provides the foundation for secure, efficient audience segmentation in your MCP server applications.

---

## Additional Resources

- [ModelContextProtocol Documentation](https://github.com/modelcontextprotocol)
- [MCP.Extensions Repository](https://github.com/echapmanFromBunnings/mcp.extensions)
- [ASP.NET Core Middleware Documentation](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/middleware/)
- [Attribute-Based Access Control](https://en.wikipedia.org/wiki/Attribute-based_access_control)

---

*Document Version: 1.0*
*Last Updated: 2025-11-13*
*Targets: MCP.Extensions v1.0+ with ModelContextProtocol SDK v0.4.0-preview.3*
