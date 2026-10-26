using Microsoft.Extensions.Configuration;
using SummaryGenerator.Services;

namespace SummaryGenerator.Tests.Unit
{
    public class LogPathResolverTests
    {
        private static IConfiguration Config(Dictionary<string, string?> values)
            => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        [Fact]
        public void AnchorFileSinkPaths_RelativeFilePath_IsCombinedWithContentRoot()
        {
            var config = Config(new()
            {
                ["Serilog:WriteTo:0:Name"] = "Console",
                ["Serilog:WriteTo:1:Name"] = "File",
                ["Serilog:WriteTo:1:Args:path"] = "Logs/log-.txt"
            });

            LogPathResolver.AnchorFileSinkPaths(config, @"C:\app");

            Assert.Equal(Path.Combine(@"C:\app", "Logs/log-.txt"), config["Serilog:WriteTo:1:Args:path"]);
            Assert.Null(config["Serilog:WriteTo:0:Args:path"]);
        }

        [Fact]
        public void AnchorFileSinkPaths_AbsolutePath_IsUnchanged()
        {
            var config = Config(new()
            {
                ["Serilog:WriteTo:0:Name"] = "File",
                ["Serilog:WriteTo:0:Args:path"] = @"D:\logs\log-.txt"
            });

            LogPathResolver.AnchorFileSinkPaths(config, @"C:\app");

            Assert.Equal(@"D:\logs\log-.txt", config["Serilog:WriteTo:0:Args:path"]);
        }

        [Fact]
        public void AnchorFileSinkPaths_NoSerilogSection_DoesNothing()
        {
            var config = Config(new() { ["Other:Key"] = "value" });

            LogPathResolver.AnchorFileSinkPaths(config, @"C:\app");

            Assert.Equal("value", config["Other:Key"]);
            Assert.Empty(config.GetSection("Serilog").GetChildren());
        }
    }
}
