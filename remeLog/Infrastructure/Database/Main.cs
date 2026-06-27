using Dapper;
using libeLog.Extensions;
using libeLog.Infrastructure;
using libeLog.Models;
using Microsoft.Data.SqlClient;
using remeLog.Infrastructure.Extensions;
using remeLog.Infrastructure.Types;
using remeLog.Models;
using remeLog.Models.Reports;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using static libeLog.Infrastructure.Db.DbHelper;
using Part = remeLog.Models.Part;

namespace remeLog.Infrastructure
{
    public static partial class Database
    {
        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }
}
