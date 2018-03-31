using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace NestRemoteThermometer.Model
{
    public class ThermostatData
    {
        [JsonProperty("humidity")]
        public int Humidity { get; set; }

        [JsonProperty("locale")]
        public string Locale { get; set; }

        [JsonProperty("temperature_scale")]
        public string TemperatureScale { get; set; }

        [JsonProperty("is_using_emergency_heat")]
        public bool IsUsingEmergencyHeat { get; set; }

        [JsonProperty("has_fan")]
        public bool HasFan { get; set; }

        [JsonProperty("software_version")]
        public string SoftwareVersion { get; set; }

        [JsonProperty("has_leaf")]
        public bool HasLeaf { get; set; }

        [JsonProperty("where_id")]
        public string WhereId { get; set; }

        [JsonProperty("device_id")]
        public string DeviceId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("can_heat")]
        public bool CanHeat { get; set; }

        [JsonProperty("can_cool")]
        public bool CanCool { get; set; }

        [JsonProperty("target_temperature_c")]
        public double TargetTemperatureC { get; set; }

        [JsonProperty("target_temperature_f")]
        public int TargetTemperatureF { get; set; }

        [JsonProperty("target_temperature_high_c")]
        public double TargetTemperatureHighC { get; set; }

        [JsonProperty("target_temperature_high_f")]
        public int TargetTemperatureHighF { get; set; }

        [JsonProperty("target_temperature_low_c")]
        public double TargetTemperatureLowC { get; set; }

        [JsonProperty("target_temperature_low_f")]
        public int TargetTemperatureLowF { get; set; }

        [JsonProperty("ambient_temperature_c")]
        public double AmbientTemperatureC { get; set; }

        [JsonProperty("ambient_temperature_f")]
        public int AmbientTemperatureF { get; set; }

        [JsonProperty("away_temperature_high_c")]
        public double AwayTemperatureHighC { get; set; }

        [JsonProperty("away_temperature_high_f")]
        public int AwayTemperatureHighF { get; set; }

        [JsonProperty("away_temperature_low_c")]
        public double AwayTemperatureLowC { get; set; }

        [JsonProperty("away_temperature_low_f")]
        public int AwayTemperatureLowF { get; set; }

        [JsonProperty("eco_temperature_high_c")]
        public double EcoTemperatureHighC { get; set; }

        [JsonProperty("eco_temperature_high_f")]
        public int EcoTemperatureHighF { get; set; }

        [JsonProperty("eco_temperature_low_c")]
        public double EcoTemperatureLowC { get; set; }

        [JsonProperty("eco_temperature_low_f")]
        public int EcoTemperatureLowF { get; set; }

        [JsonProperty("is_locked")]
        public bool IsLocked { get; set; }

        [JsonProperty("locked_temp_min_c")]
        public double LockedTempMinC { get; set; }

        [JsonProperty("locked_temp_min_f")]
        public int LockedTempMinF { get; set; }

        [JsonProperty("locked_temp_max_c")]
        public double LockedTempMaxC { get; set; }

        [JsonProperty("locked_temp_max_f")]
        public int LockedTempMaxF { get; set; }

        [JsonProperty("sunlight_correction_active")]
        public bool SunlightCorrectionActive { get; set; }

        [JsonProperty("sunlight_correction_enabled")]
        public bool SunlightCorrectionEnabled { get; set; }

        [JsonProperty("structure_id")]
        public string StructureId { get; set; }

        [JsonProperty("fan_timer_active")]
        public bool FanTimerActive { get; set; }

        [JsonProperty("fan_timer_timeout")]
        public DateTime FanTimerTimeout { get; set; }

        [JsonProperty("fan_timer_duration")]
        public int FanTimerDuration { get; set; }

        [JsonProperty("previous_hvac_mode")]
        public string PreviousHvacMode { get; set; }

        [JsonProperty("hvac_mode")]
        public string HvacMode { get; set; }

        [JsonProperty("time_to_target")]
        public string TimeToTarget { get; set; }

        [JsonProperty("time_to_target_training")]
        public string TimeToTargetTraining { get; set; }

        [JsonProperty("where_name")]
        public string WhereName { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("name_long")]
        public string NameLong { get; set; }

        [JsonProperty("is_online")]
        public bool IsOnline { get; set; }

        [JsonProperty("hvac_state")]
        public string HvacState { get; set; }
    }
}
