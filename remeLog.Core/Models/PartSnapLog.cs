using System;

namespace remeLog.Models
{
    /// <summary>
    /// История слепки времён деталей («присасывание» кнопкой в PartsInfoWindow):
    /// кто, когда и какой зазор сомкнул. Guid деталей — обычные значения без FK,
    /// чтобы история переживала удаление самих строк.
    /// </summary>
    public class PartSnapLog
    {
        public int Id { get; set; }
        public string Machine { get; set; } = string.Empty;
        public DateTime ShiftDate { get; set; }
        public string Shift { get; set; } = string.Empty;
        public Guid PrevPartGuid { get; set; }
        public Guid NextPartGuid { get; set; }
        public string NextPartName { get; set; } = string.Empty;
        public DateTime OldStartSetupTime { get; set; }
        public DateTime NewStartSetupTime { get; set; }
        public int GapMinutes { get; set; }
        public string SnappedBy { get; set; } = string.Empty;
        public DateTime SnappedAt { get; set; }
    }
}
