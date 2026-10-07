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
        Assert.Equal("Мартынов Дмитрий Юрьевич",
            ShiftOperators.Join(["Мартынов Дмитрий Юрьевич"]));
    }

    [Fact]
    public void Join_TwoOperators_OrderOfFirstAppearance()
    {
        // Кейс Rontek HTC420 №1 05.10.2026: Мартынов начал смену, Шабаров вышел
        // на одну деталь, снова Мартынов — в отчёте должны быть оба.
        Assert.Equal("Мартынов Дмитрий Юрьевич, Шабаров Дмитрий Игоревич",
            ShiftOperators.Join([
                "Мартынов Дмитрий Юрьевич",
                "Шабаров Дмитрий Игоревич",
                "Мартынов Дмитрий Юрьевич",
            ]));
    }

    [Fact]
    public void Join_EmptyAndWhitespace_Skipped()
    {
        Assert.Equal("Мартынов Дмитрий Юрьевич",
            ShiftOperators.Join([null, "", "  ", "Мартынов Дмитрий Юрьевич", " "]));
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
        Assert.Equal("Мартынов Дмитрий Юрьевич",
            ShiftOperators.Join(["  Мартынов Дмитрий Юрьевич  ", "мартынов дмитрий юрьевич"]));
    }

    [Fact]
    public void ShortName_FullName_Initials()
    {
        Assert.Equal("Мартынов Д.Ю.", ShiftOperators.ShortName("Мартынов Дмитрий Юрьевич"));
        Assert.Equal("Мартынов Д.Ю.", ShiftOperators.ShortName("  Мартынов Дмитрий Юрьевич  "));
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
        Assert.Equal("Мартынов Дмитрий Юрьевич",
            ShiftOperators.Format(["Мартынов Дмитрий Юрьевич"]));
    }

    [Fact]
    public void Format_MultipleOperators_SurnameWithInitials()
    {
        // Двое в смену — полные ФИО в одну строку не влезают, показываем «Фамилия И.О.».
        Assert.Equal("Мартынов Д.Ю., Шабаров Д.И.",
            ShiftOperators.Format(["Мартынов Дмитрий Юрьевич", "Шабаров Дмитрий Игоревич"]));
    }

    [Fact]
    public void Format_NoOperators_EmptyString()
    {
        Assert.Equal("", ShiftOperators.Format([null, ""]));
    }
}
