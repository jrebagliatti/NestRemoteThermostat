using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestRemoteThermostat
{
    public static class CloudTableExtensions
    {
        public static async Task<IEnumerable<T>> ExecuteQueryAsync<T>(this CloudTable cloudTable, TableQuery<T> query) where T : ITableEntity, new()
        {
            return await GetAsync<T>(cloudTable, query);
        }

        private static async Task<IEnumerable<T>> GetAsync<T>(CloudTable table, TableQuery<T> query) where T : ITableEntity, new()
        {
            var re = new List<T>();
            TableContinuationToken continuationToken = null;
            TableQuerySegment<T> result;
            do
            {
                result = await table.ExecuteQuerySegmentedAsync(query, continuationToken);
                re.AddRange(result.Results);
                continuationToken = result.ContinuationToken;
            } while (continuationToken != null);
            return re;
        }
    }
}
