using libeLog.Base;
using System;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace remeLog.Infrastructure
{
    public class AiHealthMonitor : ViewModel
    {
        private static readonly Lazy<AiHealthMonitor> _lazy = new(() => new AiHealthMonitor());
        public static AiHealthMonitor Instance => _lazy.Value;

        private readonly AiServiceClient _client = new();
        private readonly DispatcherTimer _timer;
        private bool _isChecking;

        private int _serverFailCount;
        private int _ollamaFailCount;

        private string? _HealthError;
        public string? HealthError
        {
            get => _HealthError;
            set => Set(ref _HealthError, value);
        }

        private string? _HealthTooltip;
        public string? HealthTooltip
        {
            get => _HealthTooltip;
            set => Set(ref _HealthTooltip, value);
        }

        private bool _IsServerAvailable;
        public bool IsServerAvailable
        {
            get => _IsServerAvailable;
            set => Set(ref _IsServerAvailable, value);
        }

        private bool _IsOllamaAvailable;
        public bool IsOllamaAvailable
        {
            get => _IsOllamaAvailable;
            set => Set(ref _IsOllamaAvailable, value);
        }

        public bool IsAiAvailable => IsServerAvailable && IsOllamaAvailable;

        private AiHealthMonitor()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _timer.Tick += async (_, _) => await CheckHealthAsync();
        }

        public void Start()
        {
            _ = CheckHealthAsync();
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
        }

        private async Task CheckHealthAsync()
        {
            if (_isChecking) return;
            _isChecking = true;
            try
            {
                var result = await _client.CheckHealthAsync();

                if (result.Server)
                {
                    _serverFailCount = 0;
                    IsServerAvailable = true;
                }
                else
                {
                    _serverFailCount++;
                    if (_serverFailCount >= 2)
                        IsServerAvailable = false;
                }

                if (result.Ollama)
                {
                    _ollamaFailCount = 0;
                    IsOllamaAvailable = true;
                }
                else
                {
                    _ollamaFailCount++;
                    if (_ollamaFailCount >= 2)
                        IsOllamaAvailable = false;
                }

                if (!IsServerAvailable || !IsOllamaAvailable)
                {
                    HealthError = result.Error;
                    HealthTooltip = result.Error;
                }
                else
                {
                    HealthError = null;
                    HealthTooltip = $"[{DateTime.Now:HH:mm:ss}] Соединение в порядке";
                }
            }
            finally
            {
                _isChecking = false;
            }
        }
    }
}
