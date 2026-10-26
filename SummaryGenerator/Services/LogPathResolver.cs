namespace SummaryGenerator.Services
{
    /// <summary>
    /// Serilog's file sink resolves relative paths against the process working directory,
    /// which differs between `dotnet run`, IIS and Windows services. Anchor them to the
    /// content root so logs always land beside the app's other data folders.
    /// </summary>
    public static class LogPathResolver
    {
        public static void AnchorFileSinkPaths(IConfiguration configuration, string contentRootPath)
        {
            foreach (var sink in configuration.GetSection("Serilog:WriteTo").GetChildren())
            {
                if (!string.Equals(sink["Name"], "File", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var path = sink["Args:path"];
                if (!string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path))
                {
                    sink["Args:path"] = Path.Combine(contentRootPath, path);
                }
            }
        }
    }
}
