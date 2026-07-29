
using AiService.Services;

namespace AiService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Logging.AddSimpleConsole(o =>
            {
                o.TimestampFormat = "HH:mm:ss.fff ";
                o.SingleLine = true;
            });
            builder.Logging.AddProvider(new FileLoggerProvider(Path.Combine(AppContext.BaseDirectory, "logs")));
            builder.Services.AddControllers();
            builder.Services.AddSingleton<OllamaService>();
            builder.Services.AddSingleton<PromptBuilder>();
            builder.Services.AddSingleton<RequestLog>();

            builder.Host.UseWindowsService();

            var app = builder.Build();

            var port = app.Configuration.GetValue("Port", 5050);
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogDebug("AiService started on http://0.0.0.0:{Port}", port);

            app.Use(async (context, next) =>
            {
                var ip = context.Connection.RemoteIpAddress;
                logger.LogDebug("{RemoteIP} {Method} {Path}", ip, context.Request.Method, context.Request.Path);
                await next();
            });

            app.MapControllers();

            app.Run($"http://0.0.0.0:{port}");
        }
    }
}
