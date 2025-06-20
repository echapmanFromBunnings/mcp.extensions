# MCP.Extensions

Extensions and middleware for ModelContextProtocol in ASP.NET Core.

## Overview
This package provides useful middleware and extension methods for working with the [ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol) in ASP.NET Core applications. It is designed to help with request/response logging, audience filtering, and other common tasks when using ModelContextProtocol.

## Features
- Middleware for logging HTTP headers, request bodies, and response bodies
- Middleware for filtering tools in responses and streams
- Attribute-based audience targeting
- Service abstractions for tool audience management

## Installation
Install via NuGet Package Manager:

```
dotnet add package MCP.Extensions
```

Or via the NuGet UI in Visual Studio.

## Usage
Add the desired middleware to your ASP.NET Core pipeline in `Startup.cs` or `Program.cs`:

```csharp
// Debugging and logging middleware
app.UseMiddleware<HeaderLoggingMiddleware>();
app.UseMiddleware<RequestBodyLoggingMiddleware>();
app.UseMiddleware<ResponseBodyLoggingMiddleware>();
app.UseMiddleware<ResponseToolFilteringMiddleware>();
//Practical use case for filtering tools in streams
app.UseMiddleware<StreamToolFilteringMiddleware>();
```

Use the `[McpAudience]` attribute to restrict the tools listing to specific audiences:

```csharp
[McpAudience("admin")]
public IActionResult AdminOnlyAction() {
    // ...
}
```

## Requirements
- .NET 9.0 or later
- [ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol)
- [ModelContextProtocol.AspNetCore](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore)

## License
MIT

## Repository
[https://github.com/echapmanFromBunnings/mcp.extensions](https://github.com/echapmanFromBunnings/mcp.extensions)

