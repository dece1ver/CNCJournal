using remeLog.Core.Services;

namespace remeLog.Core.Tests;

/// <summary>
/// Операторы смены одной строкой для cnc_shifts.Operators и шапки суточного
/// отчёта: уникальные непустые в порядке первого появления, разделитель «, ».
/// </summary>
public class ShiftOperatorsTests
{
    [Fact]
    public void Join_SingleOperator_NoSeparator()
    {
        Assert.Equal("Петров Дмитрий Юрьевич",
            ShiftOperators.Join(["Петров Дмитрий Юрьевич"]));
    }

    [Fact]
    public void Join_TwoOperators_OrderOfFirstAppearance()
    {
        // Кейс Rontek HTC420 №1 05.10.2026: Петров начал смену, Сидоров вышел
        // на одну деталь, снова Петров — в отчёте должны быть оба.
        Assert.Equal("Петров Дмитрий Юрьевич, Сидоров Дмитрий Игоревич",
            ShiftOperators.Join([
                "Петров Дмитрий Юрьевич",
                "Сидоров Дмитрий Игоревич",
                "Петров Дмитрий Юрьевич",
            ]));
    }

    [Fact]
    public void Join_EmptyAndWhitespace_Skipped()
    {
        Assert.Equal("Петров Дмитрий Юрьевич",
            ShiftOperators.Join([null, "", "  ", "Петров Дмитрий Юрьевич", " "]));
    }

    [Fact]
    public void Join_NoOperators_EmptyString()
    {
        Assert.Equal("", ShiftOperators.Join([null, ""]));
        Assert.Equal("", ShiftOperators.Join([]));
    }

    [Fact]
    public void Join_TrimsAndIgnoresCase()
    {
        Assert.Equal("Петров Дмитрий Юрьевич",
            ShiftOperators.Join(["  Петров Дмитрий Юрьевич  ", "петров дмитрий юрьевич"]));
    }

    [Fact]
    public void ShortName_FullName_Initials()
    {
        Assert.Equal("Петров Д.Ю.", ShiftOperators.ShortName("Петров Дмитрий Юрьевич"));
        Assert.Equal("Петров Д.Ю.", ShiftOperators.ShortName("  Петров Дмитрий Юрьевич  "));
    }

    [Fact]
    public void ShortName_SingleTokenOrEmpty_Unchanged()
    {
        Assert.Equal("Ученик", ShiftOperators.ShortName("Ученик"));
        Assert.Equal("", ShiftOperators.ShortName("  "));
        Assert.Equal("", ShiftOperators.ShortName(null));
    }

    [Fact]
    public void Format_SingleOperator_FullName()
    {
        Assert.Equal("Петров Дмитрий Юрьевич",
            ShiftOperators.Format(["Петров Дмитрий Юрьевич"]));
    }

    [Fact]
    public void Format_MultipleOperators_SurnameWithInitials()
    {
        // Двое в смену — полные ФИО в одну строку не влезают, показываем «Фамилия И.О.».
        Assert.Equal("Петров Д.Ю., Сидоров Д.И.",
            ShiftOperators.Format(["Петров Дмитрий Юрьевич", "Сидоров Дмитрий Игоревич"]));
    }

    [Fact]
    public void Format_NoOperators_EmptyString()
    {
        Assert.Equal("", ShiftOperators.Format([null, ""]));
    }
}
