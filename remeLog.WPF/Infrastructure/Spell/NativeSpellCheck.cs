using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace remeLog.Infrastructure.Spell
{
    /// <summary>Ошибка проверки: смещение и длина слова в тексте.</summary>
    public readonly record struct NativeSpellingError(int Start, int Length);

    /// <summary>
    /// Проверка орфографии через системный Windows Spell Checking API:
    /// без внешних пакетов и словарных файлов, русский поддерживается ОС.
    /// При недоступности API тихо отключается — подчёркиваний просто нет.
    /// Экземпляр checker создаётся лениво и используется только из UI-потока.
    /// </summary>
    internal static class NativeSpellCheck
    {
        private static readonly object Sync = new();
        private static ISpellChecker? _checker;
        private static bool _initialized;
        private static bool _supported;

        public static bool IsSupported
        {
            get
            {
                EnsureInitialized();
                return _supported;
            }
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            lock (Sync)
            {
                if (_initialized) return;
                _initialized = true;
                try
                {
                    var factory = (ISpellCheckerFactory)new SpellCheckerFactoryCoClass();
                    factory.IsSupported("ru-RU", out bool supported);
                    if (!supported) return;
                    factory.CreateSpellChecker("ru-RU", out var checker);
                    if (checker is null) return;
                    _checker = checker;
                    _supported = true;
                }
                catch
                {
                    _supported = false;
                }
            }
        }

        public static IReadOnlyList<NativeSpellingError> Check(string text, Func<string, bool>? isAllowed = null)
        {
            var result = new List<NativeSpellingError>();
            if (string.IsNullOrEmpty(text)) return result;
            EnsureInitialized();
            var checker = _checker;
            if (!_supported || checker is null) return result;
            try
            {
                if (checker.Check(text, out var errors) != 0 || errors is null) return result;
                try
                {
                    while (errors.Next(out var error) == 0 && error is not null)
                    {
                        try
                        {
                            error.GetStartIndex(out uint start);
                            error.GetLength(out uint length);
                            if (start + length <= (uint)text.Length)
                            {
                                var word = text.Substring((int)start, (int)length);
                                if (isAllowed is null || !isAllowed(word))
                                    result.Add(new NativeSpellingError((int)start, (int)length));
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(error);
                        }
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(errors);
                }
            }
            catch
            {
                // проверка не должна ронять редактирование
            }
            return result;
        }

        public static IReadOnlyList<string> Suggest(string word, int max = 5)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(word)) return result;
            EnsureInitialized();
            var checker = _checker;
            if (!_supported || checker is null) return result;
            try
            {
                if (checker.Suggest(word, out var suggestions) != 0 || suggestions is null) return result;
                for (int i = 0; i < max; i++)
                {
                    var next = new string[1];
                    if (suggestions.Next(1, next, IntPtr.Zero) != 0 || next[0] is null) break;
                    result.Add(next[0]);
                }
                Marshal.ReleaseComObject(suggestions);
            }
            catch
            {
            }
            return result;
        }

        // COM-объявления: IID и порядок методов — из Windows SDK (um/spellcheck.h).
        [ComImport, Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC")]
        private class SpellCheckerFactoryCoClass
        {
        }

        [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISpellCheckerFactory
        {
            [PreserveSig] int GetSupportedLanguages(out IntPtr value);
            [PreserveSig] int IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag, [MarshalAs(UnmanagedType.Bool)] out bool value);
            [PreserveSig] int CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out ISpellChecker value);
        }

        [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISpellChecker
        {
            [PreserveSig] int GetLanguageTag(out IntPtr value);
            [PreserveSig] int Check([MarshalAs(UnmanagedType.LPWStr)] string text, out IEnumSpellingError value);
            [PreserveSig] int Suggest([MarshalAs(UnmanagedType.LPWStr)] string word,
                [MarshalAs(UnmanagedType.Interface)] out System.Runtime.InteropServices.ComTypes.IEnumString value);
        }

        [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEnumSpellingError
        {
            [PreserveSig] int Next(out ISpellingError value);
        }

        [ComImport, Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISpellingError
        {
            [PreserveSig] int GetStartIndex(out uint value);
            [PreserveSig] int GetLength(out uint value);
            [PreserveSig] int GetCorrectiveAction(out uint value);
        }
    }
}
