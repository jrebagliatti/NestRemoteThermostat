using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Text;

namespace NestRemoteThermostat.Model
{
    public enum TemperatureNotificationType
    {
        Hot,
        Cold
    }

    public class TemperatureNotification : TableEntity
    {
        public string DeviceId { get; set; }

        public TemperatureNotificationType NotificationType
        {
            get
            {
                return (TemperatureNotificationType) this.NotificationTypeInternal;
            }
            set
            {
                this.NotificationTypeInternal = (int)value;
            }
        }

        public double ComfortTemperatureMin { get; set; }

        public double ComfortTemperatureMax { get; set; }

        public double CurrentTemperature { get; set; }

        public long EvaluationPeriodMinutes { get; set; }

        public int NotificationTypeInternal { get; set; }
    }
}
