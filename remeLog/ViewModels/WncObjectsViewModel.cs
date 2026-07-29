using libeLog;
using libeLog.Base;
using libeLog.Views;
using remeLog.Infrastructure;
using remeLog.Models;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace remeLog.ViewModels
{
    /// <summary>
    /// Окно результатов поиска в Windchill — само умеет искать заново с изменёнными критериями
    /// (см. <see cref="SearchCommand"/>), не нужно закрывать окно и повторять поиск из грида
    /// PartsInfoWindow. Три независимых поля ("Ключевое слово" / "Наименование" / "Обозначение")
    /// и переключатель типа объектов повторяют структуру
    /// <see cref="WindchillClient.SearchAsync"/> — см. документацию там за подробностями,
    /// как именно комбинируются критерии.
    /// </summary>
    public class WncObjectsViewModel : ViewModel
    {
        private CancellationTokenSource? _cts;

        public WncObjectsViewModel(ObservableCollection<WncObject> wncObjects, string? initialKeyword = null)
        {
            WncObjects = wncObjects;
            _Keyword = initialKeyword ?? "";
            OpenLinkCommand = new LambdaCommand(OnOpenLinkCommandExecuted, CanOpenLinkCommandExecute);
            SearchCommand = new LambdaCommand(OnSearchCommandExecuted, CanSearchCommandExecute);
            DownloadPdfCommand = new LambdaCommand(OnDownloadPdfCommandExecuted, CanDownloadPdfCommandExecute);
        }

        public ObservableCollection<WncObject> WncObjects { get; }

        private string _Keyword;
        /// <summary> Как штатный поиск по ключевому слову в Windchill — слово ищется хоть в обозначении, хоть в наименовании. </summary>
        public string Keyword
        {
            get => _Keyword;
            set => Set(ref _Keyword, value);
        }

        private string _Name = "";
        public string Name
        {
            get => _Name;
            set => Set(ref _Name, value);
        }

        private string _Number = "";
        public string Number
        {
            get => _Number;
            set => Set(ref _Number, value);
        }

        private bool _CadDocumentsOnly = true;
        /// <summary>
        /// true (по умолчанию) — искать только CADDocuments (3D-модели/детали/сборки/чертежи).
        /// false — искать ещё и среди обычных документов Windchill (на практике — служебные
        /// бумаги вроде извещений о несоответствии, не чертежи, но кому-то может понадобиться
        /// найти и их).
        /// </summary>
        public bool CadDocumentsOnly
        {
            get => _CadDocumentsOnly;
            set => Set(ref _CadDocumentsOnly, value);
        }

        private bool _IsSearching;
        public bool IsSearching
        {
            get => _IsSearching;
            set => Set(ref _IsSearching, value);
        }

        #region SearchCommand
        public ICommand SearchCommand { get; }

        private async void OnSearchCommandExecuted(object p)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var cancellationToken = _cts.Token;

            try
            {
                IsSearching = true;
                var (objects, truncated) = await Util.SearchInWindchill(Keyword, Name, Number, CadDocumentsOnly, cancellationToken);

                WncObjects.Clear();
                foreach (var obj in objects) WncObjects.Add(obj);

                if (objects.Count == 0)
                {
                    MessageBoxWindow.Show("Ничего не найдено :с", ":c", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (truncated)
                {
                    MessageBoxWindow.Show(
                        $"Показаны первые {objects.Count} совпадений — возможно, есть ещё. Уточните запрос, чтобы увидеть нужное.",
                        "Слишком широкий запрос", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (OperationCanceledException)
            {
                // Отменено новым поиском — молча.
            }
            catch (Exception ex)
            {
                MessageBoxWindow.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsSearching = false;
            }
        }

        private bool CanSearchCommandExecute(object p) =>
            !string.IsNullOrWhiteSpace(Keyword) || !string.IsNullOrWhiteSpace(Name) || !string.IsNullOrWhiteSpace(Number);
        #endregion

        #region OpenLinkCommand
        public ICommand OpenLinkCommand { get; }
        private void OnOpenLinkCommandExecuted(object p)
        {
            if (p is WncObject wncObject)
            {
                Process.Start(new ProcessStartInfo(wncObject.Link) { UseShellExecute = true });
            }
        }
        private static bool CanOpenLinkCommandExecute(object p) => true;
        #endregion

        #region DownloadPdfCommand
        public ICommand DownloadPdfCommand { get; }

        /// <summary>
        /// Скачивает PDF-представление объекта во временную папку и открывает программой по
        /// умолчанию (см. <see cref="Util.DownloadWndcPdf"/>). Не у каждого объекта есть
        /// PDF-представление — тогда просто сообщаем об этом, без ошибки.
        /// </summary>
        private async void OnDownloadPdfCommandExecuted(object p)
        {
            if (p is not WncObject wncObject) return;

            try
            {
                var path = await Util.DownloadWndcPdf(wncObject, CancellationToken.None);
                if (path == null)
                {
                    MessageBoxWindow.Show("У этого объекта нет PDF-представления в Windchill", ":c",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBoxWindow.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static bool CanDownloadPdfCommandExecute(object p) => p is WncObject;
        #endregion
    }
}
