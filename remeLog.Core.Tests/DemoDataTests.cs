using remeLog.Core;
using remeLog.Core.Services;
using remeLog.Core.Services.Demo;
using remeLog.Infrastructure.Types;
using remeLog.Models;
using System;
using System.Linq;

namespace remeLog.Core.Tests;

/// <summary>
/// Демо-генератор для режима --demo: детерминизм, связность времён,
/// покрытие типовых кейсов и фильтрация стора.
/// </summary>
public class DemoDataTests
{
    private static readonly DateTime Today = new(2026, 9, 16);

    private static void InitStore()
    {
        DemoStore.Reset();
        DomainSettings.DemoMode = true;
        DomainSettings.DemoSeed = 42;
        DemoStore.EnsureInitialized(Today, 42);
    }

    [Fact]
    public void Build_IsDeterministic()
    {
        var a = DemoDataFactory.Build(Today, 42);
        var b = DemoDataFactory.Build(Today, 42);

        Assert.Equal(a.Parts.Count, b.Parts.Count);
        Assert.Equal(
            a.Parts.Select(p => p.PartName + p.Order + p.Machine),
            b.Parts.Select(p => p.PartName + p.Order + p.Machine));
    }

    [Fact]
    public void Build_CoversCatalog()
    {
        var snapshot = DemoDataFactory.Build(Today, 42);

        Assert.Equal(6, snapshot.Machines.Count);
        Assert.Equal(8, snapshot.Operators.Count);
        Assert.True(snapshot.Operators.Count(o => o.IsActive) >= 7);
        Assert.NotEmpty(snapshot.Parts);
        Assert.NotEmpty(snapshot.Shifts);
        Assert.NotEmpty(snapshot.SetupReasons);
        Assert.NotEmpty(snapshot.MachiningReasons);
        Assert.NotEmpty(snapshot.DowntimeReasons);
        Assert.Equal(2, snapshot.Holidays.Count);
        Assert.NotEmpty(snapshot.MachineActivity);
    }

    [Fact]
    public void Parts_TimeChainsAreCoherent()
    {
        var snapshot = DemoDataFactory.Build(Today, 42);

        foreach (var part in snapshot.Parts)
        {
            Assert.True(part.StartSetupTime <= part.StartMachiningTime,
                $"Наладка позже изготовления: {part.PartName} {part.Order}");
            Assert.True(part.StartMachiningTime <= part.EndMachiningTime,
                $"Изготовление раньше наладки: {part.PartName} {part.Order}");
            Assert.True(part.SetupTimeFact > 0);
            Assert.True(part.ProductionTimeFact > 0);
        }
    }

    [Fact]
    public void Parts_CoverScenarios()
    {
        InitStore();
        var parts = DemoStore.GetParts(Today.AddDays(-14), Today);

        Assert.Contains(parts, p => p.DefectiveCount > 0); // брак
        Assert.Contains(parts, p => p.SetupTimeFactIncludePartialAndDowntimes > DomainSettings.LongSetupLimit); // длинная наладка
        Assert.Contains(parts, p => !string.IsNullOrEmpty(p.SetupReasonOverride)); // переопределение аналитиком
        Assert.Contains(parts, p => p.IsSerial); // серийные
        Assert.Contains(parts, p => !p.IsSerial && p.FinishedCount > 0); // обычные
    }

    [Fact]
    public void Store_QueryParts_FiltersByMachineAndSerial()
    {
        InitStore();
        var machines = DemoStore.GetMachines();
        var serialNames = DemoStore.GetSerialPartNames().ToList();
        // Первый по алфавиту — сверлильный без деталей («нет данных»); берём рабочий.
        var all = DemoStore.GetParts(Today.AddDays(-14), Today);
        var first = machines.First(m => all.Any(p => p.Machine == m));

        PartsFilterCriteria Criteria(PartsFilterType serial, params string[] selected) => new(
            Today.AddDays(-14), Today, new Shift(ShiftType.All),
            "", "", "", "", "",
            null, null, null, serial, serialNames, selected,
            Array.Empty<FilterChip>());

        var byMachine = DemoStore.QueryParts(Criteria(PartsFilterType.All, first));
        Assert.NotEmpty(byMachine);
        Assert.All(byMachine, p => Assert.Equal(first, p.Machine));

        var serial = DemoStore.QueryParts(Criteria(PartsFilterType.Serial, machines.ToArray()));
        Assert.NotEmpty(serial);
        Assert.All(serial, p => Assert.True(p.IsSerial));

        var nonSerial = DemoStore.QueryParts(Criteria(PartsFilterType.NonSerial, machines.ToArray()));
        Assert.NotEmpty(nonSerial);
        Assert.All(nonSerial, p => Assert.False(p.IsSerial));

        Assert.Equal(
            DemoStore.GetParts(Today.AddDays(-14), Today).Count,
            DemoStore.QueryParts(Criteria(PartsFilterType.All, machines.ToArray())).Count);
    }

    [Fact]
    public void Store_UpsertPart_PersistsInMemory()
    {
        InitStore();
        var part = DemoStore.GetParts(Today.AddDays(-14), Today).First();
        string updatedComment = "Демо-правка " + Guid.NewGuid();
        part.OperatorComment = updatedComment;

        var result = DemoStore.UpsertPart(part);

        Assert.True(result.IsOk);
        var reloaded = DemoStore.GetPartsByGuids(new[] { part.Guid }).Single();
        Assert.Equal(updatedComment, reloaded.OperatorComment);
    }
}
