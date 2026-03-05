using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using libeLog.Extensions;
using libeLog.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Navigation;

namespace libeLog.Infrastructure
{
    public class GoogleSheet
    {
        string _credentialFile;
        string _sheetId;
        public GoogleSheet(string credentialFile, string sheedId)
        {
            _credentialFile = credentialFile;
            _sheetId = sheedId;
            Credential = GetCredentialsFromFile(_credentialFile);
            SheetsService = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = Credential,
            });
        }
        static readonly string[] Scopes = { SheetsService.Scope.Spreadsheets };
        private GoogleCredential Credential { get; set; }
        public SheetsService SheetsService { get; set; }
        private static GoogleCredential GetCredentialsFromFile(string filePath)
        {
            GoogleCredential credential;
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                credential = GoogleCredential.FromStream(stream).CreateScoped(Scopes);
            }
            return credential;
        }

        public async Task<Spreadsheet> GetSpreadsheetAsync()
        {
            Credential = GetCredentialsFromFile(_credentialFile);
            SheetsService = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = Credential,
            });
            var request = SheetsService.Spreadsheets.Get(_sheetId);
            return await request.ExecuteAsync();
        }

        public async Task<Dictionary<string, string>> GetAssignedPartsAsync(IProgress<string> progress)
        {
            progress.Report("Подключение к таблице");
            var data = new Dictionary<string, string>();
            Credential = GetCredentialsFromFile(_credentialFile);
            SheetsService = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = Credential,
            });
            var requestTable = SheetsService.Spreadsheets.Get(_sheetId);
            var table = await requestTable.ExecuteAsync();
            if (table is Spreadsheet gs)
            {
                progress.Report("Определение данных");
                if (gs.Sheets.Count < 1) throw new ArgumentException("В таблице нет листов");
                if (gs.Sheets[0].BandedRanges.Count < 0) throw new ArgumentException("На первом листе отсутствует требуемый диапазон данных");
                var gridRange = gs.Sheets[0].BandedRanges[0].Range;
                var range = CreateGoogleSheetsRange(((int)gridRange.StartRowIndex!, (int)gridRange.StartColumnIndex!, (int)gridRange.EndRowIndex!, (int)gridRange.EndColumnIndex!));
                progress.Report("Получение данных");
                var requestData = SheetsService.Spreadsheets.Values.Get(_sheetId, range);
                var responseData = await requestData.ExecuteAsync();
                foreach (var value in responseData.Values)
                {
                    if (value.Count < 2) continue;
                    data.TryAdd(value[0].ToString()?.ToLowerInvariant()!, value[1].ToString()!);
                }
            }
            return data;
        }

        public async Task<string> FindRowByValue(string searchValue, string machine, IEnumerable<string> machines, IProgress<(int, string)> progress, int column = 2)
        {
            progress.Report((0, "Подключение к списку заданий"));
            string range = "Загрузка станков!A1:K200";
            await Task.Delay(1000);
            Credential = GetCredentialsFromFile(_credentialFile);
            SheetsService = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = Credential,
            });
            var request = SheetsService.Spreadsheets.Values.Get(_sheetId, range);
            ValueRange response = await request.ExecuteAsync();
            progress.Report((1, "Поиск станка"));
            await Task.Delay(1000);
            var values = response.Values;
            bool machineFound = false;
            if (values != null && values.Count > 0)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    if (values[i] == null || values[i].Count == 0) continue;

                    if (values[i][0] is string currentMachine 
                        && currentMachine.Contains('|') 
                        && machine.Contains(currentMachine.Split('|')[0].Trim()))
                    {
                        machineFound = true;
                        progress.Report((2, "Станок найден"));
                        await Task.Delay(1000);
                    }
                    else if (machineFound && values[i][0] is string otherMachine 
                        && (otherMachine.ToLower() == "маркировка" || otherMachine.Contains('|') 
                        && machines.Any(name => name.Contains(otherMachine.Split('|')[0].Trim()))))
                    {
                        break;
                    }
                    else if (machineFound && SafeGet(values[i], column).ToLower() == searchValue.ToLower())
                    {
                        progress.Report((3, $"Деталь найдена"));
                        await Task.Delay(1000);
                        return $"Загрузка станков!H{i + 1}";
                    }
                }
            }
            return "";
        }

        public async Task UpdateCellValue(string range, string value, IProgress<(int, string)> progress)
        {
            progress.Report((0, "Запись статуса"));
            await Task.Delay(1000);
            Credential = GetCredentialsFromFile(_credentialFile);
            SheetsService = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = Credential,
            });
            var valueRange = new ValueRange
            {
                Values = new List<IList<object>> { new List<object> { value } }
            };

            var updateRequest = SheetsService.Spreadsheets.Values.Update(valueRange, _sheetId, range);
            updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;

            await updateRequest.ExecuteAsync();
            progress.Report((1, "Записано"));
            await Task.Delay(1000);
        }

        public async Task<HashSet<string>> GetAssignedPartsData(string machine, IProgress<string> progress, CancellationToken cancellationToken)
        {
            Credential = GetCredentialsFromFile(_credentialFile);
            SheetsService = new SheetsService(new BaseClientService.Initializer
            {
                HttpClientInitializer = Credential,
            });

            var request = SheetsService.Spreadsheets.Values.Get(_sheetId, "Список общий!A:B");

            progress.Report("Подключение к списку...");
            var response = await request.ExecuteAsync(cancellationToken);

            progress.Report("Формирование списка деталей...");
            var values = response.Values;
            if (values is not { Count: > 0 }) return new();

            var parts = new List<string>();

            foreach (var row in values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (row?.Count > 1 && row[1] is string m && m == machine && row[0] is string p)
                {
                    parts.Add(p.ToUpperInvariant());
                }
            }

            progress.Report("Сортировка списка.");
            return parts.OrderBy(d => d).ToHashSet();
        }

        public async Task<IReadOnlyList<ProductionTaskData>> GetProductionTasksData(string machine, IEnumerable<string> machines,  IProgress<string> progress, CancellationToken cancellationToken)
        {
            List<ProductionTaskData> data = new List<ProductionTaskData>();
            Credential = GetCredentialsFromFile(_credentialFile);
            SheetsService = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = Credential,
            });
            SpreadsheetsResource.ValuesResource.GetRequest request = SheetsService.Spreadsheets.Values.Get(_sheetId, "Загрузка станков!A1:L200");
            progress.Report("Подключение к списку...");
            ValueRange response = await request.ExecuteAsync(cancellationToken);
            progress.Report("Формирование списка работы...");
            var values = response.Values;
            bool machineFound = false;
            int skipCnt = 0;
            if (values != null && values.Count > 0)
            {
                foreach (var row in values)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (row == null || row.Count == 0) continue;
                    if (skipCnt > 0)
                    {
                        skipCnt--;
                        continue;
                    }
                    if (row[0] is string currentMachine 
                        && currentMachine.Contains('|') 
                        && machine.Contains(currentMachine.Split('|')[0].Trim()))
                    {
                        machineFound = true;
                        skipCnt = 2;
                    }
                    else if (machineFound && row[0] is string otherMachine 
                        && (otherMachine.ToLower() == "маркировка" || otherMachine.Contains('|') 
                        && machines.Any(name => name.Contains(otherMachine.Split('|')[0].Trim()))))
                    {
                        break;
                    }
                    else if (machineFound && row.Count > 1)
                    {
                        var partName = SafeGet(row, 1);
                        var order = SafeGet(row, 2);
                        var count = SafeGet(row, 3);
                        var date = SafeGet(row, 4);
                        var plantComment = SafeGet(row, 5);
                        var priority = SafeGet(row, 6);
                        var engineerComment = SafeGet(row, 7);
                        var setup_technician = SafeGet(row, 8);
                        var laborInput = SafeGet(row, 9);
                        var pdComment = SafeGet(row, 10);
                        var ncProgramHref = SafeGet(row, 11);

                        if (!string.IsNullOrEmpty(partName))
                        {
                            data.Add(new ProductionTaskData(
                                partName,
                                order.IfEmpty("Без М/Л", o => o.ToUpper()),
                                count.IfEmpty("?"),
                                date.IfEmpty("-"),
                                plantComment.IfEmpty("-"),
                                priority,
                                engineerComment.IfEmpty("-"),
                                setup_technician.IfEmpty("-"),
                                pdComment.IfEmpty("-"),
                                ncProgramHref.IfEmpty("-")));
                        }
                    }
                }
                progress.Report("Список работы сформирован.");

            }
            progress.Report("Сортировка списка.");
            var sortedData = data
                .Select((item, index) => new { Item = item, Index = index })
                .OrderBy(x => ParsePriority(x.Item.Priority), Comparer<int>.Default)
                .ThenBy(x => x.Index)
                .Select(x => x.Item)
                .ToList()
                .AsReadOnly();
            return sortedData;
        }

        private static int ParsePriority(string priority)
        {
            if (int.TryParse(priority, out int result))
            {
                return result;
            }
            return 9999;
        }

        /// <summary>
        /// Получает значение ячейки
        /// </summary>
        /// <param name="list">Строка</param>
        /// <param name="index">Столбец</param>
        /// <returns>Значение ячейки, либо пустая строка</returns>
        public static string SafeGet(IList<object> list, int index)
        {
            return (list.Count > index && list[index] != null) ? list[index].ToString()! : "";
        }

        public static string CreateGoogleSheetsRange((int startRow, int startColumn, int endRow, int endColumn) coordinates)
        {
            var (startRow, startColumn, endRow, endColumn) = coordinates;
            if (startRow == 0) startRow = 1;
            string startCol = Utils.ConvertColumnIndexToLetters(startColumn);
            string endCol = Utils.ConvertColumnIndexToLetters(endColumn);

            int startRowNumber = startRow;
            int endRowNumber = endRow;

            string startCell = $"{startCol}{startRowNumber}";
            string endCell = $"{endCol}{endRowNumber}";

            return (startRow == endRow && startColumn == endColumn) ?
                startCell :
                $"{startCell}:{endCell}";
        }

        public static string ExceptionMessage(Google.GoogleApiException exception)
        {
            switch (exception.HResult)
            {
                case -2146233088:
                    return "ID таблицы не указан";
                case -2146233079:
                    return "Некорректный ID таблицы";
                default:
                    return exception.Message;

            }
        }
    }
}
