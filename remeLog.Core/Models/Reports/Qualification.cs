namespace remeLog.Models.Reports
{
    public readonly record struct LevelValue(double Value, double Coefficient);

    public readonly record struct EfficiencyGroup(LevelValue HH, LevelValue H, LevelValue N, LevelValue L, LevelValue LL, LevelValue LLL);
    public readonly record struct DownTimesGroup(LevelValue HH, LevelValue H, LevelValue N, LevelValue L, LevelValue LL, LevelValue LLL);
    public readonly record struct NonSerialEfficiencyGroup(LevelValue HH, LevelValue H, LevelValue N, LevelValue L, LevelValue LL, LevelValue LLL);

    public sealed record Qualification
    {
        public int Value { get; init; }

        public EfficiencyGroup Efficiency { get; init; }
        public DownTimesGroup DownTimes { get; init; }
        public NonSerialEfficiencyGroup NonSerialEfficiency { get; init; }

        public static Qualification FromRow(QualificationRow row) => new()
        {
            Value = row.Qualification,
            Efficiency = new(
                new LevelValue(row.EfficiencyValueHH, row.EfficiencyCoefficientHH),
                new LevelValue(row.EfficiencyValueH, row.EfficiencyCoefficientH),
                new LevelValue(row.EfficiencyValueN, row.EfficiencyCoefficientN),
                new LevelValue(row.EfficiencyValueL, row.EfficiencyCoefficientL),
                new LevelValue(row.EfficiencyValueLL, row.EfficiencyCoefficientLL),
                new LevelValue(row.EfficiencyValueLLL, row.EfficiencyCoefficientLLL)),
            DownTimes = new(
                new LevelValue(row.DownTimesValueHH, row.DownTimesCoefficientHH),
                new LevelValue(row.DownTimesValueH, row.DownTimesCoefficientH),
                new LevelValue(row.DownTimesValueN, row.DownTimesCoefficientN),
                new LevelValue(row.DownTimesValueL, row.DownTimesCoefficientL),
                new LevelValue(row.DownTimesValueLL, row.DownTimesCoefficientLL),
                new LevelValue(row.DownTimesValueLLL, row.DownTimesCoefficientLLL)),
            NonSerialEfficiency = new(
                new LevelValue(row.NonSerialEfficiencyValueHH, row.NonSerialEfficiencyCoefficientHH),
                new LevelValue(row.NonSerialEfficiencyValueH, row.NonSerialEfficiencyCoefficientH),
                new LevelValue(row.NonSerialEfficiencyValueN, row.NonSerialEfficiencyCoefficientN),
                new LevelValue(row.NonSerialEfficiencyValueL, row.NonSerialEfficiencyCoefficientL),
                new LevelValue(row.NonSerialEfficiencyValueLL, row.NonSerialEfficiencyCoefficientLL),
                new LevelValue(row.NonSerialEfficiencyValueLLL, row.NonSerialEfficiencyCoefficientLLL))
        };
    }
}
