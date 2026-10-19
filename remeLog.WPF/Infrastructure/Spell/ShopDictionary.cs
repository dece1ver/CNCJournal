using System;
using System.Collections.Generic;
using System.Linq;

namespace remeLog.Infrastructure.Spell
{
    /// <summary>Цеховые слова, которые проверка не должна подсвечивать.</summary>
    internal static class ShopDictionary
    {
        private static readonly HashSet<string> Words = new(StringComparer.OrdinalIgnoreCase)
        {
            // производители станков (см. MasterCommentCellContextMenu в PartsInfoWindow.xaml)
            "Goodway", "Hyundai", "Mazak", "Quaser", "Rontek", "Victor",
            "Integrex", "Nexus", "WIA", "GS", "SKT", "XH", "QTS", "MV", "HTC", "VMC", "HB",
            // цеховые сокращения
            "УП", "ЧПУ", "СГТ", "СОЖ",
        };

        public static bool IsAllowed(string word)
        {
            if (string.IsNullOrEmpty(word)) return true;
            if (word.Length < 3) return true;
            if (word.Any(char.IsDigit)) return true;
            if (word.All(c => !char.IsLetter(c) || char.IsUpper(c))) return true;
            return Words.Contains(word);
        }
    }
}
