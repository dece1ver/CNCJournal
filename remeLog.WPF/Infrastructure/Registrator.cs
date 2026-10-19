using Microsoft.Extensions.DependencyInjection;
using remeLog.ViewModels;

namespace remeLog.Infrastructure
{
    internal static class Registrator
    {
        public static IServiceCollection RegisterViewModels(this IServiceCollection services)
        {
            services.AddSingleton<MainWindowViewModel>();

            return services;
        }
    }
}