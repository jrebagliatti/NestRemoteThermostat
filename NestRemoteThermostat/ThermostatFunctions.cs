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
        private const int DefaultCacheTimeout = 60000;

        private static DateTime latestReadingTimestamp;
        private static ThermostatData latestReading;

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

        [FunctionName("TemperatureCop")]
        public static async Task TemperatureCopAsync(
            [TimerTrigger("0 */1 * * * *")]TimerInfo myTimer,
            [Table("ThermostatData", Connection = "StorageConnectionAppSetting")] CloudTable temperatureTable,
            [Table("ReportsTable", Connection = "StorageConnectionAppSetting")] CloudTable reportsTable,
            [Blob(ContainerName + "/" + TokenFileName, FileAccess.Read, Connection = "StorageConnectionAppSetting")] Stream inputBlob,
            TraceWriter log,
            ExecutionContext context)
        {
            log.Info($"Temperature Cop Timer trigger function executed at: {DateTime.Now}");

            IConfigurationRoot configurationRoot = ReadConfiguration(context);

            int temperatureCheckRangeMinutes = int.Parse(configurationRoot["TemperatureCheckRangeMinutes"]);
            int temperatureReportingRangeMinutes = int.Parse(configurationRoot["TemperatureReportingRangeMinutes"]);
            double targetTemp = double.Parse(configurationRoot["TargetTemperature"]);
            double comfortRange = double.Parse(configurationRoot["ComfortTemperatureRange"]);

            double comfortMaxTemp = targetTemp + comfortRange;
            double comfortMinTemp = targetTemp - comfortRange;

            var dateFrom = DateTime.UtcNow.AddMinutes(-temperatureCheckRangeMinutes).ToString(DateTimeFormat);

            foreach (var device in configurationRoot["Nest.Devices"].Split(','))
            {
                // Get the latests lectures
                var query = new TableQuery<ThermostatData>()
                    .Where(
                        TableQuery.CombineFilters(
                            TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, device),
                            TableOperators.And,
                            TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.GreaterThan, dateFrom)));

                var result = await temperatureTable.ExecuteQueryAsync<ThermostatData>(query);

                if (result != null && result.Any())
                {
                    // If all readings are below/above comfort temperature
                    var tooHot = result.All(x => x.AmbientTemperatureC > comfortMaxTemp);
                    var tooCold = result.All(x => x.AmbientTemperatureC < comfortMinTemp);

                    if (tooHot || tooCold)
                    {
                        var currentTemp = result.OrderBy(x => x.RowKey).Last().AmbientTemperatureC;
                        var message = new TemperatureNotification()
                        {
                            DeviceId = device,
                            NotificationType = tooHot ? TemperatureNotificationType.Hot : TemperatureNotificationType.Cold,
                            ComfortTemperatureMax = comfortMaxTemp,
                            ComfortTemperatureMin = comfortMinTemp,
                            CurrentTemperature = currentTemp,
                            EvaluationPeriodMinutes = temperatureReportingRangeMinutes,
                            RowKey = DateTime.UtcNow.ToString(DateTimeFormat),
                            PartitionKey = device
                        };

                        // Check if a report has recently sent in the last minutes
                        var notificationType = Enum.GetName(typeof(TemperatureNotificationType), message.NotificationType);

                        log.Info($"Temperature out of range detected for device {message.DeviceId}, Notification Type {notificationType}.");

                        var reportDateFrom = DateTime.UtcNow.AddMinutes(-temperatureReportingRangeMinutes).ToString(DateTimeFormat);
                        var latestReportsQuery = new TableQuery<TemperatureNotification>()
                            .Where(
                                TableQuery.CombineFilters(
                                    TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, device),
                                    TableOperators.And,
                                    TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.GreaterThan, dateFrom)));

                        var latestReports = await reportsTable.ExecuteQueryAsync<TemperatureNotification>(latestReportsQuery);
                        if (!latestReports.Any(x => x.NotificationType == message.NotificationType))
                        {
                            await ReportTemperatureIssue(configurationRoot, message, log);
                            await reportsTable.ExecuteAsync(TableOperation.Insert(message));
                        }
                        else
                        {
                            log.Info($"Temperature {notificationType} has been already reported in the last {temperatureReportingRangeMinutes} minutes.");
                        }
                    }
                }
            }
        }

        [FunctionName("GetThermostatData")]
        public static async Task<IActionResult> GetThermostatDataAsync(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "thermostats/{deviceId}")] HttpRequest req,
            [Blob(ContainerName + "/" + TokenFileName, FileAccess.Read, Connection = "StorageConnectionAppSetting")] Stream inputBlob,
            string deviceId,
            TraceWriter log,
            ExecutionContext context)
        {
            var result = await ExecuteGetThermostatDataAsync(inputBlob, deviceId, log, context);

            return new JsonResult(result);
        }

        [FunctionName("GetThermostatDataSlack")]
        public static async Task<IActionResult> GetThermostatDataSlackAsync(
            [HttpTrigger(Route = "thermostats/{deviceId}/slack", WebHookType = "slack")] HttpRequest req,
            [Blob(ContainerName + "/" + TokenFileName, FileAccess.Read, Connection = "StorageConnectionAppSetting")] Stream inputBlob,
            string deviceId,
            TraceWriter log,
            ExecutionContext context)
        {
            var result = await ExecuteGetThermostatDataAsync(inputBlob, deviceId, log, context);

            return new JsonResult(new { Text = $"Current Temperature is *{result.AmbientTemperatureC}°C*. Humidity {result.Humidity}%." });
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

        private static async Task ReportTemperatureIssue(IConfigurationRoot configurationRoot, TemperatureNotification notification, TraceWriter log)
        {
            var notificationType = Enum.GetName(typeof(TemperatureNotificationType), notification.NotificationType);

            log.Info($"Generating Temperature Notification for device {notification.DeviceId}, Notification Type {notificationType}.");

            var messageTemplate = configurationRoot[$"ReportTemperatureMessage.{notificationType}"];
            var message = string.Format(messageTemplate, notification.DeviceId, notification.CurrentTemperature, notification.EvaluationPeriodMinutes, notification.ComfortTemperatureMin, notification.ComfortTemperatureMax);
            var notificationUrl = configurationRoot["SlackNotificationUrl"];

            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, notificationUrl);
            request.Content = new StringContent(JsonConvert.SerializeObject(new
            {
                text = message
            }));

            await client.SendAsync(request);
        }

        private static async Task<ThermostatData> ExecuteGetThermostatDataAsync(Stream inputBlob, string deviceId, TraceWriter log, ExecutionContext context)
        {
            var requireNewReading = true;

            IConfigurationRoot configurationRoot = ReadConfiguration(context);

            // Using an In-Memory cache to avoid throttling the API
            //// TODO: Resolve possible racing conditions
            if (latestReading != null && latestReadingTimestamp != null)
            {
                var cacheTimeout = DefaultCacheTimeout;

                if (int.TryParse(configurationRoot["ThermostatDataCacheTimeoutMs"], out int cacheTimeoutOverride))
                {
                    cacheTimeout = cacheTimeoutOverride;
                }

                var newTimestamp = DateTime.UtcNow.AddMilliseconds(-cacheTimeout);
                if (newTimestamp < latestReadingTimestamp)
                {
                    requireNewReading = false;
                }
            }

            if (requireNewReading)
            {
                log.Info("Obtaining a new Reading from Nest");
                var result = await GetThermostatData(inputBlob, deviceId, log, configurationRoot);
                latestReadingTimestamp = DateTime.UtcNow;
                latestReading = result;
            }

            return latestReading;
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
