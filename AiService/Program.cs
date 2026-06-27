
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
            builder.Services.AddControllers();
            builder.Services.AddSingleton<OllamaService>();

            var app = builder.Build();
            app.MapControllers();

            app.Run("http://0.0.0.0:5050");
        }
    }
}
