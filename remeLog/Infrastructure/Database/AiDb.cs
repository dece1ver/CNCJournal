using libeLog.Infrastructure;
using libeLog.Models;
using Microsoft.Data.SqlClient;
using remeLog.Infrastructure.Extensions;
using remeLog.Infrastructure.Types;
using remeLog.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace remeLog.Infrastructure
{
    public static partial class Database
    {
        public static async Task<DbResult<int>>
            SaveDayReviewAsync(DayReview review)
        {
            const string upsertSql = @"
                DECLARE @id INT;

                IF EXISTS (SELECT 1 FROM ai_day_reviews
                           WHERE Machine = @Machine AND ShiftDate = @ShiftDate)
                BEGIN
                    UPDATE ai_day_reviews
                    SET ReviewedBy      = CASE WHEN @TouchReviewMeta = 1 THEN @ReviewedBy ELSE ReviewedBy END,
                        ReviewedAt      = CASE WHEN @TouchReviewMeta = 1 THEN @ReviewedAt ELSE ReviewedAt END,
                        Decision        = @Decision,
                        IsFullyReviewed = @IsFullyReviewed,
                        Comment         = @Comment,
                        AiFeedback      = @AiFeedback
                    WHERE Machine = @Machine AND ShiftDate = @ShiftDate;

                    SELECT @id = Id FROM ai_day_reviews
                    WHERE Machine = @Machine AND ShiftDate = @ShiftDate;
                END
                ELSE
                BEGIN
                    INSERT INTO ai_day_reviews
                        (Machine, ShiftDate, ReviewedBy, ReviewedAt,
                         Decision, IsFullyReviewed, Comment, AiFeedback)
                    VALUES
                        (@Machine, @ShiftDate, @ReviewedBy, @ReviewedAt,
                         @Decision, @IsFullyReviewed, @Comment, @AiFeedback);

                    SET @id = SCOPE_IDENTITY();
                END

                SELECT @id;";

            try
            {
                await using var conn = new SqlConnection(AppSettings.Instance.ConnectionString);
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(upsertSql, conn);

                cmd.Parameters.AddWithValue("@Machine", review.Machine);
                cmd.Parameters.AddWithValue("@ShiftDate", review.ShiftDate.Date);
                cmd.Parameters.AddWithValue("@ReviewedBy", review.ReviewedBy);
                cmd.Parameters.AddWithValue("@ReviewedAt", review.ReviewedAt);
                cmd.Parameters.AddWithValue("@TouchReviewMeta", review.TouchReviewMeta);
                cmd.Parameters.AddWithValue("@Decision", review.Decision.ToDbString());
                cmd.Parameters.AddWithValue("@IsFullyReviewed", review.IsFullyReviewed);
                cmd.Parameters.AddWithValue("@Comment",
                    string.IsNullOrEmpty(review.Comment) ? DBNull.Value : review.Comment);
                cmd.Parameters.AddWithValue("@AiFeedback",
                    string.IsNullOrEmpty(review.AiFeedback) ? DBNull.Value : review.AiFeedback);

                var scalar = await cmd.ExecuteScalarAsync();
                int newId = Convert.ToInt32(scalar);
                review.Id = newId;

                return DbResult<int>.Ok(newId);
            }
            catch (SqlException sqlEx)
            {
                Util.WriteLog(sqlEx, $"SaveDayReviewAsync: #{sqlEx.Number}");
                return sqlEx.Number == 18456
                    ? DbResult<int>.Fail(DbResult.AuthError, $"Ошибка авторизации: {sqlEx.Message}")
                    : DbResult<int>.Fail(DbResult.Error, $"Ошибка БД #{sqlEx.Number}: {sqlEx.Message}");
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex, "SaveDayReviewAsync");
                return DbResult<int>.FailWithError(ex.Message);
            }
        }

        public static async Task<DbResult<bool>> SaveAiAnalysisAsync(
            int dayReviewId, AiAnalysisResult result, string modelVersion, bool thinkingEnabled)
        {
            const string sql = @"
        UPDATE ai_day_reviews
        SET AiRequiresReview = @AiRequiresReview,
            AiThinkingEnabled = @AiThinkingEnabled,
            AiConfidence     = @AiConfidence,
            AiSignals        = @AiSignals,
            AiExplanation    = @AiExplanation,
            AiModelVersion   = @AiModelVersion,
            AiPromptVersion   = @AiPromptVersion,
            AiAnalyzedAt     = @AiAnalyzedAt,
            AiFeedback       = NULL
        WHERE Id = @Id";
            try
            {
                await using var conn = new SqlConnection(AppSettings.Instance.ConnectionString);
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Id", dayReviewId);
                cmd.Parameters.AddWithValue("@AiRequiresReview", result.RequiresReview);
                cmd.Parameters.AddWithValue("@AiThinkingEnabled", thinkingEnabled);
                cmd.Parameters.AddWithValue("@AiConfidence", result.Confidence);
                cmd.Parameters.AddWithValue("@AiSignals", JsonSerializer.Serialize(result.Signals, _jsonOpts));
                cmd.Parameters.AddWithValue("@AiExplanation",
                    (object?)result.Explanation ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@AiModelVersion", modelVersion);
                cmd.Parameters.AddWithValue("@AiPromptVersion",
                    (object?)result.PromptVersion ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@AiAnalyzedAt", DateTime.Now);
                await cmd.ExecuteNonQueryAsync();
                return DbResult<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex, "SaveAiAnalysisAsync");
                return DbResult<bool>.FailWithError(ex.Message);
            }
        }

        public static async Task<DayReview?> GetDayReviewAsync(string machine, DateTime shiftDate)
        {
            const string sql = @"
                SELECT Id, Machine, ShiftDate, ReviewedBy, ReviewedAt,
                       Decision, IsFullyReviewed, Comment,
                       AiRequiresReview, AiConfidence, AiSignals, AiExplanation,
                       AiModelVersion, AiPromptVersion, AiAnalyzedAt,
                       AiThinkingEnabled, AiFeedback, AiVerdict
                FROM ai_day_reviews
                WHERE Machine = @Machine AND ShiftDate = @ShiftDate";

            try
            {
                await using var conn = new SqlConnection(AppSettings.Instance.ConnectionString);
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Machine", machine);
                cmd.Parameters.AddWithValue("@ShiftDate", shiftDate.Date);

                await using var r = await cmd.ExecuteReaderAsync();
                return await r.ReadAsync() ? ReadDayReview(r) : null;
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex, "GetDayReviewAsync");
                return null;
            }
        }

        public static async Task<Dictionary<(string Machine, DateTime Date), DayReview>>
            GetDayReviewsForPeriodAsync(IEnumerable<string> machines, DateTime fromDate, DateTime toDate)
        {
            var result = new Dictionary<(string, DateTime), DayReview>();

            const string sql = @"
                SELECT Id, Machine, ShiftDate, ReviewedBy, ReviewedAt,
                       Decision, IsFullyReviewed, Comment,
                       AiRequiresReview, AiConfidence, AiSignals, AiExplanation,
                       AiModelVersion, AiPromptVersion, AiAnalyzedAt,
                       AiThinkingEnabled, AiFeedback, AiVerdict
                FROM ai_day_reviews
                WHERE ShiftDate BETWEEN @From AND @To
                ORDER BY ShiftDate, Machine";

            try
            {
                await using var conn = new SqlConnection(AppSettings.Instance.ConnectionString);
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@From", fromDate.Date);
                cmd.Parameters.AddWithValue("@To", toDate.Date);

                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var dr = ReadDayReview(r);
                    result[(dr.Machine, dr.ShiftDate.Date)] = dr;
                }
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex, "GetDayReviewsForPeriodAsync");
            }

            return result;
        }

        public static async Task<List<DayReview>> GetAllDayReviewsAsync(CancellationToken ct = default)
        {
            var result = new List<DayReview>();

            const string sql = @"
                SELECT Id, Machine, ShiftDate, ReviewedBy, ReviewedAt,
                       Decision, IsFullyReviewed, Comment,
                       AiRequiresReview, AiConfidence, AiSignals, AiExplanation,
                       AiModelVersion, AiPromptVersion, AiAnalyzedAt,
                       AiThinkingEnabled, AiFeedback, AiVerdict
                FROM ai_day_reviews
                ORDER BY ShiftDate, Machine";

            try
            {
                await using var conn = new SqlConnection(AppSettings.Instance.ConnectionString);
                await conn.OpenAsync(ct);
                await using var cmd = new SqlCommand(sql, conn);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    result.Add(ReadDayReview(r));
                }
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex, "GetAllDayReviewsAsync");
            }

            return result;
        }

        public static async Task<DbResult<string>>
            SavePartFlagAsync(PartFlag flag)
        {
            const string sql = @"
                IF EXISTS (SELECT 1 FROM ai_part_flags
                           WHERE DayReviewId = @DayReviewId AND PartGuid = @PartGuid)
                    UPDATE ai_part_flags
                    SET IsCleared = @IsCleared, Comment = @Comment
                    WHERE DayReviewId = @DayReviewId AND PartGuid = @PartGuid;
                ELSE
                    INSERT INTO ai_part_flags (DayReviewId, PartGuid, IsCleared, Comment)
                    VALUES (@DayReviewId, @PartGuid, @IsCleared, @Comment);";

            try
            {
                await using var conn = new SqlConnection(AppSettings.Instance.ConnectionString);
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@DayReviewId", flag.DayReviewId);
                cmd.Parameters.AddWithValue("@PartGuid", flag.PartGuid);
                cmd.Parameters.AddWithValue("@IsCleared", flag.IsCleared);
                cmd.Parameters.AddWithValue("@Comment",
                    string.IsNullOrEmpty(flag.Comment) ? DBNull.Value : flag.Comment);

                await cmd.ExecuteNonQueryAsync();
                return DbResult<string>.Ok("OK");
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex, "SavePartFlagAsync");
                return DbResult<string>.FailWithError(ex.Message);
            }
        }

        public static async Task<List<PartFlag>> GetPartFlagsAsync(int dayReviewId)
        {
            const string sql = @"
                SELECT Id, DayReviewId, PartGuid, IsCleared, Comment,
                       AiRequiresReview, AiConfidence, AiSuggestedReason, AiSignals, AiExplanation
                FROM ai_part_flags
                WHERE DayReviewId = @DayReviewId";

            var result = new List<PartFlag>();
            try
            {
                await using var conn = new SqlConnection(AppSettings.Instance.ConnectionString);
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@DayReviewId", dayReviewId);

                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    result.Add(new PartFlag
                    {
                        Id = r.GetInt32(0),
                        DayReviewId = r.GetInt32(1),
                        PartGuid = r.GetGuid(2),
                        IsCleared = r.GetBoolean(3),
                        Comment = r.IsDBNull(4) ? string.Empty : r.GetString(4),
                        AiRequiresReview = r.IsDBNull(5) ? null : r.GetBoolean(5),
                        AiConfidence = r.IsDBNull(6) ? null : r.GetDouble(6),
                        AiSuggestedReason = r.IsDBNull(7) ? null : r.GetString(7),
                        AiSignals = r.IsDBNull(8) ? null : r.GetString(8),
                        AiExplanation = r.IsDBNull(9) ? null : r.GetString(9),
                    });
                }
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex, "GetPartFlagsAsync");
            }
            return result;
        }

        public static async Task<DbResult<string>>
            ClearPartFlagsAsync(int dayReviewId)
        {
            const string sql = "DELETE FROM ai_part_flags WHERE DayReviewId = @DayReviewId";

            try
            {
                await using var conn = new SqlConnection(AppSettings.Instance.ConnectionString);
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@DayReviewId", dayReviewId);
                await cmd.ExecuteNonQueryAsync();
                return DbResult<string>.Ok("OK");
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex, "ClearPartFlagsAsync");
                return DbResult<string>.FailWithError(ex.Message);
            }
        }

        private static DayReview ReadDayReview(SqlDataReader r) => new()
        {
            Id = r.GetInt32(0),
            Machine = r.GetString(1),
            ShiftDate = r.GetDateTime(2),
            ReviewedBy = r.GetString(3),
            ReviewedAt = r.GetDateTime(4),
            Decision = AnalystDecisionExtensions.FromDbString(r.GetString(5)),
            IsFullyReviewed = r.GetBoolean(6),
            Comment = r.IsDBNull(7) ? string.Empty : r.GetString(7),
            AiRequiresReview = r.IsDBNull(8) ? null : r.GetBoolean(8),
            AiConfidence = r.IsDBNull(9) ? null : r.GetDouble(9),
            AiSignals = r.IsDBNull(10) ? null : r.GetString(10),
            AiExplanation = r.IsDBNull(11) ? null : r.GetString(11),
            AiModelVersion = r.IsDBNull(12) ? null : r.GetString(12),
            AiPromptVersion = r.IsDBNull(13) ? null : r.GetString(13),
            AiAnalyzedAt = r.IsDBNull(14) ? null : r.GetDateTime(14),
            AiThinkingEnabled = r.IsDBNull(15) ? null : r.GetBoolean(15),
            AiFeedback = r.IsDBNull(16) ? null : r.GetString(16),
            AiVerdict = r.IsDBNull(17) ? null : r.GetString(17),
        };
    }
}
