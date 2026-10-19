using ClosedXML.Excel;

namespace libeLog.Infrastructure.Excel
{
    public static class WorksheetExtensions
    {
        public static void ApplyStandardBorders(this IXLWorksheet ws, XLBorderStyleValues inside = XLBorderStyleValues.Thin, XLBorderStyleValues outside = XLBorderStyleValues.Medium)
        {
            var range = ws.RangeUsed();
            if (range is null) return;
            range.Style.Border.InsideBorder = inside;
            range.Style.Border.OutsideBorder = outside;
        }

        public static void ApplyAutoFilter(this IXLWorksheet ws)
        {
            ws.RangeUsed()?.SetAutoFilter(true);
        }

        public static void AdjustColumns(this IXLWorksheet ws)
        {
            ws.Columns().AdjustToContents();
        }
    }
}
