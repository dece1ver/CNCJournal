using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace remeLog.Infrastructure.Types
{
    public class SearchPattern
    {
        public string Pattern { get; }
        public bool IsExactMatch { get; }

        public SearchPattern(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                Pattern = "IS NULL";
                IsExactMatch = true;
                return;
            }

            if (input.StartsWith('='))
            {
                Pattern = $"= '{input[1..]}'";
                IsExactMatch = true;
            }
            else if (input.StartsWith('*') && input.EndsWith('*'))
            {
                Pattern = $"LIKE '{input.Replace('*', '%')}'";
                IsExactMatch = false;
            }
            else if (input.StartsWith('*'))
            {
                Pattern = $"LIKE '%{input[1..]}'";
                IsExactMatch = false;
            }
            else if (input.EndsWith('*'))
            {
                Pattern = $"LIKE '{input[..^1]}%'";
                IsExactMatch = false;
            }
            else
            {
                Pattern = $"LIKE '%{input}%'";
                IsExactMatch = false;
            }
        }

        public override string ToString() => Pattern;
    }
}
