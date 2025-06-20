# mcp.extensions

A collection of reusable .NET middleware, attributes, and services to extend and enhance ASP.NET Core applications.

## Features

- **Middleware**
  - `HeaderLoggingMiddleware`: Logs HTTP request headers for diagnostics and auditing.
  - `RequestBodyLoggingMiddleware`: Logs incoming HTTP request bodies for debugging and traceability.
  - `ResponseBodyLoggingMiddleware`: Logs outgoing HTTP response bodies for monitoring and troubleshooting.
  - `ResponseToolFilteringMiddleware`: Filters HTTP responses based on custom tool logic.
  - `StreamToolFilteringMiddleware`: Filters streams in HTTP requests/responses using custom tool logic.

- **Attributes**
  - `McpAudienceAttribute`: Attribute for specifying audience-based access control on controllers or actions.

- **Services**
  - `IToolAudienceService`: Interface for implementing audience-based logic for tools or endpoints.

## Getting Started

1. **Installation**

   Add a reference to the `MCP.Extensions` project or package in your ASP.NET Core solution.

2. **Usage**

   - **Register Middleware**  
     In your `Startup.cs` or program setup, add the desired middleware to the pipeline:
     ```csharp
     app.UseMiddleware<HeaderLoggingMiddleware>();
     app.UseMiddleware<RequestBodyLoggingMiddleware>();
     app.UseMiddleware<ResponseBodyLoggingMiddleware>();
     app.UseMiddleware<ResponseToolFilteringMiddleware>();
     app.UseMiddleware<StreamToolFilteringMiddleware>();
     ```

   - **Use Attributes**  
     Decorate your controllers or actions with `[McpAudience]` to enforce audience restrictions.

   - **Implement Services**  
     Implement `IToolAudienceService` to provide custom audience logic for your application.

## Contributing

Contributions are welcome! Please open issues or submit pull requests for new features, bug fixes, or improvements.

## License

[MIT](LICENSE)
