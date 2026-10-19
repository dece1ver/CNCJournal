namespace remeLog.Models
{
    public struct DataSource
    {
        public DataSource(Types type)
        {
            Type = type;
        }

        public enum Types
        {
            Database, Excel
        }

        public Types Type { get; set; }

        public readonly string Name => Type switch
        {
            Types.Database => "База данных",
            Types.Excel => "Книга Excel",
            _ => "",
        };
    }
}
