using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace NestRemoteThermometer.Model
{
    public class AuthenticationToken
    {
        [JsonProperty(PropertyName ="access_token")]
        public string AccessToken { get; set; }

        [JsonProperty(PropertyName = "expires_in")]
        public long ExpiresIn { get; set; }
    }
}
