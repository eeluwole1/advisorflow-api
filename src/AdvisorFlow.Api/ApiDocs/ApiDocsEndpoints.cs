namespace AdvisorFlow.Api.ApiDocs;

/// <summary>
/// Development-only API documentation.
/// <list type="bullet">
/// <item><c>/openapi/v1.yaml</c> serves the versioned contract shared with advisorflow-web
/// (docs/api/openapi.v1.yaml), the same file that ContractTests checks the API against.</item>
/// <item><c>/swagger</c> renders it with Swagger UI, so "Try it out" calls the running API.</item>
/// </list>
/// Swagger UI loads from a pinned CDN version with Subresource Integrity, so no
/// extra NuGet package or bundled assets are needed. Not mapped outside Development.
/// </summary>
public static class ApiDocsEndpoints
{
    public const string ContractFileName = "openapi.v1.yaml";

    public static IEndpointRouteBuilder MapApiDocs(this IEndpointRouteBuilder app)
    {
        var contractPath = Path.Combine(AppContext.BaseDirectory, ContractFileName);

        app.MapGet("/openapi/v1.yaml", () => File.Exists(contractPath)
            ? Results.File(contractPath, "application/yaml; charset=utf-8")
            : Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "API contract file is missing from the build output."));

        app.MapGet("/swagger", () => Results.Content(SwaggerUiPage, "text/html; charset=utf-8"));

        return app;
    }

    private const string SwaggerUiVersion = "5.35.0";

    private const string SwaggerUiPage = $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <title>AdvisorFlow API · Swagger UI</title>
          <link rel="stylesheet"
                href="https://cdn.jsdelivr.net/npm/swagger-ui-dist@{{SwaggerUiVersion}}/swagger-ui.css"
                integrity="sha384-WlYgdmXYOOBWL5apwjDEZwN+U3HmXSSu5R6V5aGxEcf7z+KuRNwBhoxKVN1i3D5I"
                crossorigin="anonymous" />
        </head>
        <body>
          <div id="swagger-ui"></div>
          <script src="https://cdn.jsdelivr.net/npm/swagger-ui-dist@{{SwaggerUiVersion}}/swagger-ui-bundle.js"
                  integrity="sha384-gjRm0bEPMeTmMh5jT2cHccIwCUZSTd8YC+j0d0sXw0MdGEfScduGpSyF3ZSSLkX9"
                  crossorigin="anonymous"></script>
          <script>
            window.ui = SwaggerUIBundle({
              url: '/openapi/v1.yaml',
              dom_id: '#swagger-ui',
              deepLinking: true,
              tryItOutEnabled: true,
              displayRequestDuration: true,
            });
          </script>
        </body>
        </html>
        """;
}
