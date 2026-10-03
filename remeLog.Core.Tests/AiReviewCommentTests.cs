using remeLog.Core.Services;
using Xunit;

namespace remeLog.Core.Tests;

/// <summary>
/// Метка принятия вердикта ИИ в комментарии ревью.
/// </summary>
public class AiReviewCommentTests
{
    private const string Accepted = "[ИИ] Вердикт ИИ принят.";
    private const string Against = "[ИИ] Вопреки вердикту ИИ.";

    [Fact]
    public void ApplyMarker_PlainComment_PrependsOnce()
    {
        Assert.Equal(Accepted + " Требуется создание альтернативного тех процесса",
            AiReviewComment.ApplyMarker("Требуется создание альтернативного тех процесса", false));
    }

    [Fact]
    public void ApplyMarker_Reapplied_DoesNotAccumulate()
    {
        // Кейс 02.10.2026, Mazak QTS350 26.09: маркер накопился трижды и читался
        // как три отдельных решения. Повторные сохранения так себя вести не должны.
        var once = AiReviewComment.ApplyMarker(null, false);
        var twice = AiReviewComment.ApplyMarker(once, false);
        var thrice = AiReviewComment.ApplyMarker(twice, false);

        Assert.Equal(Accepted, once);
        Assert.Equal(once, twice);
        Assert.Equal(once, thrice);
    }

    [Fact]
    public void ApplyMarker_Reapplied_KeepsAnalystText()
    {
        var first = AiReviewComment.ApplyMarker("Проверить норматив наладки", false);
        var second = AiReviewComment.ApplyMarker(first, false);

        Assert.Equal(Accepted + " Проверить норматив наладки", second);
    }

    [Fact]
    public void ApplyMarker_SwitchesToAgainst_KeepsSingleMarker()
    {
        // Аналитик сначала принял вердикт, потом нажал «Не согласен» — старая метка
        // не должна остаться рядом с новой.
        var accepted = AiReviewComment.ApplyMarker("Всё объяснено", false);
        var against = AiReviewComment.ApplyMarker(accepted, true);

        Assert.Equal(Against + " Всё объяснено", against);
        Assert.DoesNotContain(Accepted, against);
    }

    [Fact]
    public void ApplyMarker_TwoSavedMarkers_Cleaned()
    {
        // Дубль из боевых данных: маркер стоял дважды подряд.
        var dirty = Accepted + " " + Accepted;
        Assert.Equal(Accepted, AiReviewComment.ApplyMarker(dirty, false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ApplyMarker_EmptyComment_ReturnsMarker(string? comment)
    {
        Assert.Equal(Accepted, AiReviewComment.ApplyMarker(comment, false));
    }

    [Fact]
    public void StripMarkers_EmptyAndNull_ReturnEmpty()
    {
        Assert.Equal("", AiReviewComment.StripMarkers(null));
        Assert.Equal("", AiReviewComment.StripMarkers("   "));
    }

    [Fact]
    public void StripMarkers_RemovesBothMarkers_KeepsText()
    {
        Assert.Equal("Нужна сверка норматива",
            AiReviewComment.StripMarkers(Accepted + " " + Against + " Нужна сверка норматива"));
    }

    [Fact]
    public void StripMarkers_KeepsAnalystTextWithBrackets()
    {
        // Метка — только целиком; похожий текст внутри комментария не трогаем.
        var text = "Вердикт ИИ принят аналитиком вручную";
        Assert.Equal(text, AiReviewComment.StripMarkers(text));
    }
}