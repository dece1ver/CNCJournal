using System;

namespace remeLog.Core.Services
{
    /// <summary>
    /// Служебные метки в комментарии ревью дня, которые добавляет accept-путь вердикта ИИ.
    /// </summary>
    public static class AiReviewComment
    {
        /// <summary>Вердикт ИИ принят аналитиком.</summary>
        public const string AcceptedMarker = "[ИИ] Вердикт ИИ принят.";

        /// <summary>Решение вопреки вердикту ИИ.</summary>
        public const string AgainstMarker = "[ИИ] Вопреки вердикту ИИ.";

        /// <summary>
        /// Убирает метки принятия вердикта и возвращает комментарий аналитика.
        /// Метка дописывается в начало при каждом сохранении, поэтому без чистки
        /// она накапливалась: кейс 02.10.2026, Mazak QTS350 26.09 — маркер стоял
        /// трижды и в данных читался как три отдельных решения.
        /// </summary>
        /// <param name="comment">Комментарий из формы (может содержать прежние метки).</param>
        /// <returns>Комментарий без меток, с нормализованными пробелами.</returns>
        public static string StripMarkers(string? comment)
        {
            if (string.IsNullOrWhiteSpace(comment)) return string.Empty;

            var text = comment;
            foreach (var marker in new[] { AcceptedMarker, AgainstMarker })
            {
                while (text.Contains(marker, StringComparison.Ordinal))
                    text = text.Replace(marker, "", StringComparison.Ordinal);
            }

            return string.Join(" ", text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>
        /// Ставит метку принятия вердикта ровно один раз: прежние метки убираются,
        /// новая добавляется в начало. Текст комментария аналитика сохраняется.
        /// </summary>
        /// <param name="comment">Комментарий аналитика (возможно, с прежними метками).</param>
        /// <param name="againstVerdict">true — решение вопреки вердикту ИИ.</param>
        public static string ApplyMarker(string? comment, bool againstVerdict)
        {
            var body = StripMarkers(comment);
            var marker = againstVerdict ? AgainstMarker : AcceptedMarker;
            return string.IsNullOrWhiteSpace(body) ? marker : marker + " " + body;
        }
    }
}