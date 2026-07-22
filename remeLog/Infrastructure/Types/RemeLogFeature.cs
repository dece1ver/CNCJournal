using System;
using System.Linq;

namespace remeLog.Infrastructure.Types
{
    [Flags]
    public enum RemeLogFeature
    {
        None = 0,
        Ai = 1 << 0,
        AdvancedEdit = 1 << 1,
        Instances = 1 << 2,
        /// <summary>
        /// Позволяет открыть суточный отчёт при наличии ошибок валидации (пустые
        /// причины/комментарии мастера) через диалог-подтверждение вместо жёсткого
        /// блока. Для разбора бэклога старых записей, где реальную причину знает
        /// только мастер в момент смены, а не аналитик задним числом.
        /// </summary>
        ValidationOverride = 1 << 3,
        /// <summary>
        /// Фоновая ИИ-проверка релевантности комментариев мастера в PartsInfoWindow:
        /// после редактирования строки с аномалией одна запись уходит на AiService
        /// (verify-part, без thinking), результат — совещательная иконка в гриде и
        /// сводка перед суточным отчётом. Ничего не блокирует, в БД не пишется.
        /// </summary>
        AiMasterCheck = 1 << 4,
    }

    /// <summary>
    /// Единственный источник правды о списке фич — построен рефлексией над
    /// RemeLogFeature, а не ручным перечислением. Добавление новой фичи требует
    /// правки только самого enum, все места (About, список экземпляров,
    /// "--features=all") подхватывают её автоматически.
    /// </summary>
    public static class RemeLogFeatureExtensions
    {
        /// <summary> Все определённые фичи, объединённые в одну маску (кроме None). </summary>
        public static readonly RemeLogFeature All = Enum.GetValues<RemeLogFeature>()
            .Where(f => f != RemeLogFeature.None)
            .Aggregate(RemeLogFeature.None, (acc, f) => acc | f);

        /// <summary> Имена отдельных фич, установленных в маске. </summary>
        public static string[] Names(this RemeLogFeature features) =>
            Enum.GetValues<RemeLogFeature>()
                .Where(f => f != RemeLogFeature.None && features.HasFlag(f))
                .Select(f => f.ToString())
                .ToArray();
    }
}
