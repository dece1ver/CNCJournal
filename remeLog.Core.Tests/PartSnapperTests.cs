using remeLog.Core.Services;
using remeLog.Models;
using System;
using System.Collections.Generic;

namespace remeLog.Core.Tests;

/// <summary>
/// Слепка деталей встык (кнопка-магнит): начало следующей тянется к концу
/// предыдущей в рамках смены, частичная наладка тянется вместе с началом.
/// </summary>
public class PartSnapperTests
{
    private static readonly DateTime Day = new(2026, 10, 16);

    private static DateTime T(string hhmm) =>
        Day.AddHours(int.Parse(hhmm[..2])).AddMinutes(int.Parse(hhmm[3..]));

    private static Part Row(
        string shift, string startSetup, string startMachining, string endMachining,
        double setupDowntimes = 0, double partial = 0, string name = "Деталь") =>
        new(
            guid: Guid.NewGuid(), machine: "Станок", shift: shift, shiftDate: Day,
            @operator: "Оператор", partName: name, order: "З1", setup: 1,
            finishedCount: 1, defectiveCount: 0, totalCount: 1,
            startSetupTime: T(startSetup), startMachiningTime: T(startMachining),
            setupTimeFact: 0, endMachiningTime: T(endMachining),
            setupTimePlan: 0, setupTimePlanForReport: 0,
            singleProductionTimePlan: 0, productionTimeFact: 0,
            machiningTime: TimeSpan.Zero, setupDowntimes: setupDowntimes,
            machiningDowntimes: 0, partialSetupTime: partial,
            createNcProgramTime: 0, maintenanceTime: 0, toolSearchingTime: 0,
            toolChangingTime: 0, mentoringTime: 0, contactingDepartmentsTime: 0,
            fixtureMakingTime: 0, hardwareFailureTime: 0, specialDowntimeTime: 0,
            operatorComment: "");

    [Fact]
    public void Gap_PullsNextStartBack()
    {
        // Кейс из постановки: 07:15–10:30 и 10:40–13:00 → вторая с 10:30.
        var a = Row("День", "07:15", "07:20", "10:30");
        var b = Row("День", "10:40", "10:45", "13:00");

        var r = PartSnapper.Plan([a, b], 180);

        var plan = Assert.Single(r.Plans);
        Assert.Same(b, plan.Next);
        Assert.Equal(T("10:30"), plan.NewStart);
        Assert.Equal(10, plan.GapMinutes);
        Assert.Equal(0, r.SkippedInvariant);
        Assert.Equal(0, r.SkippedPartial);
    }

    [Fact]
    public void DayAndNight_NeverSnapped()
    {
        var day = Row("День", "17:00", "17:05", "19:00");
        var night = Row("Ночь", "19:10", "19:15", "20:00");

        var r = PartSnapper.Plan([day, night], 180);

        Assert.Empty(r.Plans);
    }

    [Fact]
    public void Threshold_180Snaps181Skips()
    {
        var a = Row("День", "07:00", "07:05", "10:00");
        var far = Row("День", "13:01", "13:05", "14:00"); // зазор 181

        var over = PartSnapper.Plan([a, far], 180);
        Assert.Empty(over.Plans);

        var b = Row("День", "13:00", "13:05", "14:00"); // зазор ровно 180
        var exact = PartSnapper.Plan([a, b], 180);
        Assert.Single(exact.Plans);
    }

    [Fact]
    public void Overlap_PushesNextStartForward()
    {
        var a = Row("День", "07:00", "07:05", "10:40");
        var b = Row("День", "10:30", "10:45", "12:00");

        var r = PartSnapper.Plan([a, b], 180);

        var plan = Assert.Single(r.Plans);
        Assert.Equal(T("10:40"), plan.NewStart);
        Assert.Equal(-10, plan.GapMinutes);
    }

    [Fact]
    public void OverlapBeyondMachiningStart_Skipped()
    {
        // Конец предыдущей (12:00) позже начала изготовления следующей (11:00) —
        // инвариант наладки, стык скип.
        var a = Row("День", "07:00", "07:05", "12:00");
        var b = Row("День", "10:00", "11:00", "13:00");

        var r = PartSnapper.Plan([a, b], 180);

        Assert.Empty(r.Plans);
        Assert.Equal(1, r.SkippedInvariant);
    }

    [Fact]
    public void PartialSetup_StretchedWithStart_FactUnchanged()
    {
        // Наладка 10:40–11:00 целиком закрыта частичной (факт 0): натяг на 10 мин
        // назад растягивает и частичную, факт остаётся 0.
        var a = Row("День", "07:00", "07:05", "10:30");
        var b = Row("День", "10:40", "11:00", "12:00", partial: 20);
        Assert.Equal(0, b.SetupTimeFact);

        var r = PartSnapper.Plan([a, b], 180);

        var plan = Assert.Single(r.Plans);
        Assert.Equal(20, plan.OldPartial);
        Assert.Equal(30, plan.NewPartial);

        PartSnapper.Apply(r);
        Assert.Equal(T("10:30"), b.StartSetupTime);
        Assert.Equal(30, b.PartialSetupTime);
        Assert.Equal(0, b.SetupTimeFact);
    }

    [Fact]
    public void PartialSetup_WouldGoNegative_Skipped()
    {
        // Сдвиг вперёд на 4 мин при частичной 3 мин увёл бы её в минус — скип.
        var a = Row("День", "07:00", "07:05", "10:04");
        var b = Row("День", "10:00", "10:10", "11:00", partial: 3);

        var r = PartSnapper.Plan([a, b], 180);

        Assert.Empty(r.Plans);
        Assert.Equal(1, r.SkippedPartial);
        Assert.Equal(T("10:00"), b.StartSetupTime);
    }

    [Fact]
    public void OtherDowntimes_LeftUntouched()
    {
        // Обычные простои наладки не трогаем: факт наладки растёт на зазор.
        var a = Row("День", "07:00", "07:05", "10:30");
        var b = Row("День", "10:40", "11:00", "12:00", setupDowntimes: 15);
        var factBefore = b.SetupTimeFact;

        var r = PartSnapper.Plan([a, b], 180);

        Assert.Single(r.Plans);
        PartSnapper.Apply(r);
        Assert.Equal(15, b.SetupDowntimes);
        Assert.Equal(factBefore + 10, b.SetupTimeFact);
    }

    [Fact]
    public void ZeroGap_NoPlans()
    {
        var a = Row("День", "07:00", "07:05", "10:30");
        var b = Row("День", "10:30", "10:35", "12:00");

        var r = PartSnapper.Plan([a, b], 180);

        Assert.Empty(r.Plans);
    }

    [Fact]
    public void EmptyAndSingle_NoPlans()
    {
        Assert.Empty(PartSnapper.Plan([], 180).Plans);
        Assert.Empty(PartSnapper.Plan(
            [Row("День", "07:00", "07:05", "10:30")], 180).Plans);
    }

    [Fact]
    public void ChainOfThree_SnapsBothJoints()
    {
        var a = Row("День", "07:00", "07:05", "10:00");
        var b = Row("День", "10:10", "10:15", "12:00");
        var c = Row("День", "12:20", "12:25", "14:00");

        var r = PartSnapper.Plan([a, b, c], 180);

        Assert.Equal(2, r.Plans.Count);
        PartSnapper.Apply(r);
        Assert.Equal(T("10:00"), b.StartSetupTime);
        Assert.Equal(T("12:00"), c.StartSetupTime);
    }
}
