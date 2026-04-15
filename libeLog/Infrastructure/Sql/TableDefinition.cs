using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libeLog.Infrastructure.Sql
{
    public class TableDefinition
    {
        public string Name { get; set; } = null!;
        public List<ColumnDefinition> Columns { get; set; } = new();
        public List<ForeignKeyDefinition> ForeignKeys { get; set; } = new();
        public List<string> CompositePrimaryKey { get; set; } = new();
        public List<List<string>> CompositeUniques { get; set; } = new();
        public List<IndexDefinition> Indexes { get; set; } = new();
    }
}
