using AiService.Services;

namespace AiService.Tests;

/// <summary>
/// Детектор петель в thinking: фикстура — укороченный реальный кейс
/// (чередование двух длинных предложений про простой 6 мин).
/// </summary>
public class ThinkingLoopGuardTests
{
    private const string A =
        "Однако, в данных для дневной смены указано, что простой 6 мин, причина не указана, комментарий отсутствует. ";
    private const string B =
        "Но в данных для дневной смены указано, что простой 6 мин, причина не указана, комментарий отсутствует. ";

    [Fact]
    public void IsLooping_AlternatingPair_True()
    {
        Assert.True(ThinkingLoopGuard.IsLooping(A + B + A + B));
    }

    [Fact]
    public void IsLooping_TripleRepeat_True()
    {
        Assert.True(ThinkingLoopGuard.IsLooping(A + A + A));
    }

    [Fact]
    public void IsLooping_MovedOn_False()
    {
        // Два повтора и движение дальше — не петля.
        Assert.False(ThinkingLoopGuard.IsLooping(
            A + A + "Но здесь комментарий не указан, это может быть сигналом для проверки данных смены."));
    }

    [Fact]
    public void IsLooping_DistinctReasoning_False()
    {
        Assert.False(ThinkingLoopGuard.IsLooping(
            "Проверяю КПД наладки первой детали, значение в пределах нормы. " +
            "Изготовление второй детали идёт с нормальным показателем без отклонений. " +
            "Отчёт мастера по ночной смене описывает отсутствие оператора целиком."));
    }

    [Fact]
    public void IsLooping_ShortText_False()
    {
        Assert.False(ThinkingLoopGuard.IsLooping(""));
        Assert.False(ThinkingLoopGuard.IsLooping("Коротко. Ясно."));
    }
}
