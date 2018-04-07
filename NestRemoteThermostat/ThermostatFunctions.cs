using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Table;
using NestRemoteThermostat.Model;
using Newtonsoft.Json;

namespace NestRemoteThermostat
{
    public static class ThermostatFunctions
    {
        private const string DateTimeFormat = "yyyyMMddhhmmss";
        private const string ContainerName = "temp-monitor";
        private const string TokenFileName = "nest-token";
        [FunctionName("TemperaturePolling")]
        public static async Task TemperaturePollingAsync(
            [TimerTrigger("0 */5 * * * *")]TimerInfo myTimer,
            [Blob("temp-monitor/nest-token", FileAccess.Read, Connection = "StorageConnectionAppSetting")] Stream inputBlob,
            [Table("ThermostatData", Connection = "StorageConnectionAppSetting")] CloudTable outputTable,
            TraceWriter log,
            ExecutionContext context)
        {
            log.Info($"Temperature Polling Timer trigger function executed at: {DateTime.Now}");

            IConfigurationRoot configurationRoot = ReadConfiguration(context);
            foreach (var device in configurationRoot["Nest.Devices"].Split(','))
            {
                var currentData = await GetThermostatData(inputBlob, device, log, configurationRoot);
                currentData.RowKey = DateTime.UtcNow.ToString(DateTimeFormat);
                currentData.PartitionKey = device;

                await outputTable.ExecuteAsync(TableOperation.Insert(currentData));
            }
        }

        [FunctionName("GetThermostatData")]
        public static async Task<IActionResult> GetTemperature(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "thermostats/{deviceId}")] HttpRequest req,
            [Blob(ContainerName + "/" + TokenFileName, FileAccess.Read, Connection = "StorageConnectionAppSetting")] Stream inputBlob,
            string deviceId,
            TraceWriter log,
            ExecutionContext context)
        {
            IConfigurationRoot configurationRoot = ReadConfiguration(context);

            //// TODO: Implement an In-Memory cache to avoid throttling the API
                var result = await GetThermostatData(inputBlob, deviceId, log, configurationRoot);

            return new JsonResult(result);
        }


        [FunctionName("GetThermostats")]
        public static async Task<IActionResult> GetThermostats(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "thermostats")] HttpRequest req,
            [Blob(ContainerName + "/" + TokenFileName, FileAccess.Read, Connection = "StorageConnectionAppSetting")] Stream inputBlob,
            TraceWriter log,
            ExecutionContext context)
        {
            IConfigurationRoot configurationRoot = ReadConfiguration(context);

            var token = await ResolveTokenAsync(configurationRoot, inputBlob, log);

            var client = new HttpClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type", "application/json; charset=utf-8");

            var request = new HttpRequestMessage(HttpMethod.Get, "https://developer-api.nest.com/devices/thermostats/");

            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);

            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            if (response.Content != null)
            {
                var responseContent = await response.Content.ReadAsStringAsync();

                var data = JsonConvert.DeserializeObject(responseContent);

                return new JsonResult(data);
            }
            else
            {
                return null;
            }
        }

        private static async Task<ThermostatData> GetThermostatData(Stream inputBlob, string deviceId, TraceWriter log, IConfigurationRoot configurationRoot)
        {
            var token = await ResolveTokenAsync(configurationRoot, inputBlob, log);

            ThermostatData result;

            try
            {
                result = await GetThermostatDataAsync(deviceId, token.AccessToken);
            }
            catch (Exception e)
            {
                // Retry with a new token
                token = await ResolveTokenAsync(configurationRoot, inputBlob, log, true);

                result = await GetThermostatDataAsync(deviceId, token.AccessToken);
            }

            return result;
        }

        private static void LoadStreamFromString(Stream stream, string s)
        {
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(s);
                writer.Flush();
            }
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

        private static async Task<AuthenticationToken> ResolveTokenAsync(IConfigurationRoot configuration, Stream inputBlob, TraceWriter log, bool forceTokenRenewal = false)
        {
            AuthenticationToken token;
            if (inputBlob == null || forceTokenRenewal)
            {
                log.Info("Obtaining Authentication Token");
                token = await GetTokenAsync(configuration);
                var serializedToken = JsonConvert.SerializeObject(token);
                await UpdateBlobAsybc(configuration["StorageConnectionAppSetting"], ContainerName, TokenFileName, serializedToken);
            }
            else
            {
                log.Info("Using existing Authentication token");
                var tokenString = GenerateStringFromStream(inputBlob);
                token = JsonConvert.DeserializeObject<AuthenticationToken>(tokenString);
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

        private static async Task UpdateBlobAsybc(string connectionString, string containerName, string fileName, string content)
        {
            if (CloudStorageAccount.TryParse(connectionString, out CloudStorageAccount storageAccount))
            {
                var cloudClient = storageAccount.CreateCloudBlobClient();
                var container = cloudClient.GetContainerReference(ContainerName);

                var fileReference = container.GetBlockBlobReference(fileName);
                await fileReference.UploadTextAsync(content);
            }
        }
    }
}
