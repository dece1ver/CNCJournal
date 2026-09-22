using AiService.Models;
using AiService.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiService.Tests;

/// <summary>
/// Секция «Отчёт мастера» в промпте суточного анализа (2.8): факты смен
/// для семантической проверки релевантности (S1) + поле shift_report_issues.
/// </summary>
public class PromptBuilderShiftReportTests
{
    private static PromptBuilder Builder() => new(NullLogger<PromptBuilder>.Instance);

    private static AnalyzeRequest Request() => new()
    {
        Machine = "Станок1",
        ShiftDate = "2026-09-15",
        Parts = [],
        ShiftReports =
        [
            new ShiftReportContext
            {
                Shift = "День",
                ShiftMinutes = 660,
                ReportExists = true,
                Master = "Степанов Евгений Юрьевич",
                StoredUnspecifiedDowntimes = 117,
                FreshUnspecifiedDowntimes = 117,
                DowntimeReason = "Другое",
                MasterComment = "порван ремень привода",
                HasParts = true,
            },
        ],
        DowntimeReasons = ["Другое", "Ремонт оборудования"],
    };

    [Fact]
    public void Build_ShiftReports_RendersSectionAndFormat()
    {
        var res = Builder().Build(Request(),
            new HardRuleResult([], []), new ShiftReportRuleResult([], []));

        Assert.Contains("Отчёт мастера", res.Prompt);
        Assert.Contains("117мин", res.Prompt);
        Assert.Contains("порван ремень привода", res.Prompt);
        Assert.Contains("shift_report_issues", res.Prompt);
        Assert.Contains("без перечисления проблем", res.Prompt);
        Assert.Contains("такой причине добавить", res.Prompt);
        Assert.Contains("Внутреннее рассуждение (think) веди на русском языке", res.Prompt);
        Assert.Contains("НИКОГДА не пиши", res.Prompt);
        Assert.Contains("свыше 10% смены", res.Prompt);
        Assert.Contains("дословно включая скобки", res.Prompt);
        Assert.Contains("НЕ перечисляй их нигде, кроме signals", res.Prompt);
        Assert.NotEqual("unknown", res.Version);
    }

    [Fact]
    public void Build_NoShiftReports_NoSection_OldClient()
    {
        var req = Request();
        req.ShiftReports = [];

        var res = Builder().Build(req,
            new HardRuleResult([], []), new ShiftReportRuleResult([], []));

        Assert.DoesNotContain("Отчёт мастера за смену:", res.Prompt);
    }

    [Fact]
    public void Build_ShiftHard_DeclaredNonOverridable()
    {
        var shiftRules = new ShiftReportRuleResult(["[День] Нет суточного отчёта"], []);

        var res = Builder().Build(Request(),
            new HardRuleResult([], []), shiftRules);

        Assert.Contains("по отчёту мастера", res.Prompt);
        Assert.Contains("[День] Нет суточного отчёта", res.Prompt);
    }
}
