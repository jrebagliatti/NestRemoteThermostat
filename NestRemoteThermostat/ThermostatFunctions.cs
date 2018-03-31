using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage.Table;
using NestRemoteThermostat.Model;
using Newtonsoft.Json;

namespace NestRemoteThermostat
{
    public static class ThermostatFunctions
    {
        [FunctionName("TemperaturePolling")]
        public static async Task Run(
            [TimerTrigger("0 */5 * * * *")]TimerInfo myTimer, 
            [Blob("temp-monitor/nest-token", FileAccess.Read, Connection = "StorageConnectionAppSetting")] Stream inputBlob,
            [Blob("temp-monitor/nest-token", FileAccess.Write, Connection = "StorageConnectionAppSetting")] Stream outputBlob,
            [Table("ThermostatData", Connection = "StorageConnectionAppSetting")] CloudTable outputTable,
            TraceWriter log,
            ExecutionContext context)
        {
            log.Info($"C# Timer trigger function executed at: {DateTime.Now}");

            IConfigurationRoot configurationRoot = ReadConfiguration(context);
            foreach (var device in configurationRoot["Nest.Devices"].Split(','))
            {
                var currentData = await GetThermostatData(inputBlob, outputBlob, device, log, configurationRoot);
                currentData.RowKey = DateTime.UtcNow.ToString("yyyyMMddhhmmss");
                currentData.PartitionKey = device;

                await outputTable.ExecuteAsync(TableOperation.Insert(currentData));
            }
        }

        [FunctionName("GetThermostatData")]
        public static async Task<IActionResult> GetTemperature(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "thermostats/{deviceId}")]HttpRequest req,
            [Blob("temp-monitor/nest-token", FileAccess.Read, Connection = "StorageConnectionAppSetting")] Stream inputBlob,
            [Blob("temp-monitor/nest-token", FileAccess.Write, Connection = "StorageConnectionAppSetting")] Stream outputBlob,
            string deviceId,
            TraceWriter log,
            ExecutionContext context)
        {
            IConfigurationRoot configurationRoot = ReadConfiguration(context);

            var result = await GetThermostatData(inputBlob, outputBlob, deviceId, log, configurationRoot);

            return new JsonResult(result);
        }

        private static async Task<ThermostatData> GetThermostatData(Stream inputBlob, Stream outputBlob, string deviceId, TraceWriter log, IConfigurationRoot configurationRoot)
        {
            var token = await ResolveTokenAsync(configurationRoot, inputBlob, outputBlob, log);

            ThermostatData result;

            try
            {
                result = await GetThermostatDataAsync(deviceId, token.AccessToken);
            }
            catch (Exception e)
            {
                // Retry with a new token
                token = await ResolveTokenAsync(configurationRoot, inputBlob, outputBlob, log, true);

                result = await GetThermostatDataAsync(deviceId, token.AccessToken);
            }

            return result;
        }

        private static void LoadStreamFromString(Stream stream, string s)
        {
            var writer = new StreamWriter(stream);
            writer.Write(s);
            writer.Flush();
        }

        private static string GenerateStringFromStream(Stream s)
        {
            using (var reader = new StreamReader(s, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private static async Task<ThermostatData> GetThermostatDataAsync(string deviceId, string token)
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type", "application/json; charset=utf-8");

            var request = new HttpRequestMessage(HttpMethod.Get, "https://developer-api.nest.com/devices/thermostats/" + deviceId);

            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            if (response.Content != null)
            {
                var responseContent = await response.Content.ReadAsStringAsync();

                var data = JsonConvert.DeserializeObject<ThermostatData>(responseContent);

                return data;
            }
            else
            {
                return null;
            }
        }

        private static async Task<AuthenticationToken> ResolveTokenAsync(IConfigurationRoot configuration, Stream inputBlob, Stream outputBlob, TraceWriter log, bool forceTokenRenewal = false)
        {
            AuthenticationToken token;
            if (inputBlob == null || forceTokenRenewal)
            {
                log.Info("Obtaining Authentication Token");
                token = await GetTokenAsync(configuration);
                var serializedToken = JsonConvert.SerializeObject(token);
                LoadStreamFromString(outputBlob, serializedToken);
            }
            else
            {
                log.Info("Using existing Authentication token");
                token = JsonConvert.DeserializeObject<AuthenticationToken>(GenerateStringFromStream(inputBlob));
            }

            return token;
        }

        private static async Task<AuthenticationToken> GetTokenAsync(IConfigurationRoot configuration)
        {
            var client = new HttpClient();

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.home.nest.com/oauth2/access_token");

            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", configuration["Nest.ClientId"]),
                new KeyValuePair<string, string>("client_secret", configuration["Nest.ClientSecret"]),
                new KeyValuePair<string, string>("grant_type", "authorization_code"),
                new KeyValuePair<string, string>("code", configuration["Nest.VerificationCode"]),
            });

            //request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(;

            var response = await client.SendAsync(request); // await client.PostAsync("https://api.home.nest.com/oauth2/access_token", formContent);

            if (response.Content != null)
            {
                var responseContent = await response.Content.ReadAsStringAsync();

                return JsonConvert.DeserializeObject<AuthenticationToken>(responseContent);
            }
            else
            {
                return null;
            }
        }

        private static IConfigurationRoot ReadConfiguration(ExecutionContext context)
        {
            return new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();
        }
    }
}
