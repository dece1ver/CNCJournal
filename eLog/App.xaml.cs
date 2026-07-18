using eLog.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using eLog.Infrastructure.Extensions;
using libeLog.Extensions;
using libeLog.Views;

namespace eLog
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly string _uniqueEventName;
        private EventWaitHandle? _eventWaitHandle;

        public App()
        {
            _uniqueEventName = CreateUniqueEventName();

            Util.WriteLog("Создан экземпляр приложения. Уникальное имя события: " + _uniqueEventName);

            try
            {
                Util.WriteLog("Начало обработки запуска приложения");
                HandleApplicationStart();
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex, "Критическая ошибка при запуске приложения");
                MessageBoxWindow.Show($"Ошибка при запуске приложения: {ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        private void HandleApplicationStart()
        {
            Util.WriteLog("Проверка на наличие уже запущенных экземпляров");
            bool isAlreadyRunning = TryConnectToExistingInstance();
            if (isAlreadyRunning)
            {
                Util.WriteLog("Обнаружен запущенный экземпляр приложения. Активируем окно и завершаем текущий процесс.");
                _eventWaitHandle?.Set();
                Shutdown();
                return;
            }

            Util.WriteLog("Начало инициализации приложения");
            InitializeApplication();
        }

        private bool TryConnectToExistingInstance()
        {
            try
            {
                Util.WriteLog($"Попытка открыть событие с именем: {_uniqueEventName}");
                _eventWaitHandle = EventWaitHandle.OpenExisting(_uniqueEventName);
                Util.WriteLog("Событие найдено. Другой экземпляр уже запущен.");
                return true;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Util.WriteLog("Событие не найдено. Создаем новое.");
                _eventWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, _uniqueEventName);
                return false;
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex, "Ошибка при проверке запущенных экземпляров");
                throw;
            }
        }

        public static bool CheckForUpdate(string path, bool startUpdatedFile = true)
        {
            try
            {
                //Util.WriteLog("Получение текущего пути исполняемого файла");
                var currentFile = Process.GetCurrentProcess().MainModule?.FileName;
                if (currentFile == null)
                {
                    Util.WriteLog("Не удалось получить путь текущего файла. Обновление не требуется.");
                    return false;
                }

                var currentExeName = Path.GetFileName(currentFile);
                var updatePath = Path.Combine(path, currentExeName);
                //Util.WriteLog($"Проверка файла обновления по пути: {updatePath}");
                if (File.Exists(updatePath) && updatePath.IsFileNewerThan(currentFile))
                {
                    if (!startUpdatedFile) return true;
                    //Util.WriteLog("Обновленный файл найден. Запуск обновления...");
                    return TryStartUpdate(updatePath);
                }

                //Util.WriteLog("Обновление не требуется.");
                return false;
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex, "Ошибка при проверке обновлений");
                return false;
            }
        }

        private static bool TryStartUpdate(string updatePath)
        {
            try
            {
                Util.WriteLog($"Запуск обновленной версии из {updatePath}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = updatePath,
                    UseShellExecute = true
                };

                var process = Process.Start(startInfo);
                if (process == null)
                {
                    Util.WriteLog("Не удалось запустить процесс обновления");
                    return false;
                }

                if (!process.WaitForInputIdle(5000))
                {
                    Util.WriteLog("Процесс обновления не отвечает");
                    return false;
                }

                Util.WriteLog("Обновленная версия успешно запущена, завершаем текущий процесс");
                return true;
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex, "Ошибка при запуске обновления");
                return false;
            }
        }

        private void InitializeApplication()
        {
            Util.WriteLog("Инициализация настроек приложения");
            AppSettings.Instance.ReadConfig();

            Util.WriteLog("Проверка обновлений приложения");
            if (AppSettings.Instance.UpdatePath != null && CheckForUpdate(AppSettings.Instance.UpdatePath))
            {
                Util.WriteLog("Обновление найдено и успешно запущено. Завершаем текущий процесс.");
                Shutdown();
                return;
            }

            Util.WriteLog("Запуск наблюдателя активации окна");
            StartWindowActivationWatcher();

            Util.WriteLog("Настройка культуры приложения");
            ConfigureCulture();

            Util.WriteLog("Инициализация завершена");
        }

        private void StartWindowActivationWatcher()
        {
            Util.WriteLog("Запуск наблюдателя активации окна");

            Task.Factory.StartNew(() =>
            {
                Util.WriteLog("Наблюдатель активации запущен");

                while (_eventWaitHandle?.WaitOne() ?? false)
                {
                    Util.WriteLog("Получен сигнал активации");

                    try
                    {
                        if (!Dispatcher.CheckAccess())
                        {
                            throw new InvalidOperationException("Попытка использовать Dispatcher из другого потока.");
                        }
                        Dispatcher.BeginInvoke(() =>
                        {
                            Util.WriteLog("Вызов метода ActivateMainWindow через Dispatcher");
                            ActivateMainWindow();
                        });
                    }
                    catch (Exception ex)
                    {
                        Util.WriteLog(ex, "Ошибка при активации окна");
                    }
                }

                Util.WriteLog("Наблюдатель активации завершил работу");
            }, TaskCreationOptions.LongRunning);
        }

        private static void ActivateMainWindow()
        {
            Util.WriteLog("Попытка активации главного окна");

            var mainWindow = Current.MainWindow;
            if (mainWindow == null)
            {
                Util.WriteLog("Главное окно не найдено. Завершаем активацию.");
                return;
            }

            if (mainWindow.WindowState == WindowState.Minimized ||
                mainWindow.Visibility != Visibility.Visible)
            {
                Util.WriteLog("Окно свернуто или скрыто. Приводим его в нормальное состояние.");
                mainWindow.Show();
                mainWindow.WindowState = WindowState.Maximized;
            }

            mainWindow.Activate();
            mainWindow.Topmost = true;
            mainWindow.Topmost = false;
            mainWindow.Focus();

            Util.WriteLog("Главное окно активировано.");
        }
        private void ConfigureCulture()
        {
            var culture = new CultureInfo("ru-RU");
            culture.NumberFormat.NumberDecimalSeparator = ".";
            culture.NumberFormat.NaNSymbol = "-";

            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public static string CreateUniqueEventName()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0);
            string timestamp;
            try
            {
                timestamp = File.GetLastWriteTime(Process.GetCurrentProcess().MainModule?.FileName!).ToString("yyMMddHHmm");
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex, "Исключение при получении даты изменения");
                timestamp = "";
            }
            return $"eLog-{version.Major}.{version.Minor}.{version.Build}.{timestamp}";
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ConfigureCulture();
            //AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            //DispatcherUnhandledException += OnDispatcherUnhandledException;
            //TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _eventWaitHandle?.Close();
            base.OnExit(e);
        }

        //private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        //{
        //    var exception = e.ExceptionObject as Exception;
        //    string message = exception != null
        //        ? $"Необработанное исключение: {exception.Message}\n{exception.StackTrace}"
        //        : $"Необработанное исключение. Тип: {e.ExceptionObject?.GetType()}";

        //    Util.WriteLog(message);

        //    MessageBox.Show("Произошла критическая ошибка. Приложение будет закрыто.",
        //        "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);

        //    Shutdown(1);
        //}

        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            string message = $"Необработанное исключение в UI потоке: {e.Exception.Message}\n{e.Exception.StackTrace}";
            Util.WriteLog(message);

            MessageBox.Show("Произошла ошибка в пользовательском интерфейсе.",
                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);

            e.Handled = true;
            App.Current.Shutdown();
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            string message = $"Необработанное исключение в задаче: {e.Exception?.Message}\n{e.Exception?.StackTrace}";
            Util.WriteLog(message);

            e.SetObserved();
        }
    }
}