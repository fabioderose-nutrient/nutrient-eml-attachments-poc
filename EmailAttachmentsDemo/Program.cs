using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace EmailAttachmentsDemo
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                    // Files picked with "Load and merge" travel as base64 in a custom
                    // action request: allow bodies up to 100 MB.
                    webBuilder.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 100 * 1024 * 1024);
                    webBuilder.UseUrls(Startup.AppUri);
                });
    }
}
