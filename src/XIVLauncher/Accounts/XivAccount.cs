﻿using AdysTech.CredentialManager;
using Newtonsoft.Json;
using Serilog;
using System;
using System.ComponentModel;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace XIVLauncher.Accounts
{
    public class XivAccount
    {
        [JsonIgnore]
        public string Id => $"{UserName}-{UseOtp}-{UseSteamServiceAccount}";

        public override string ToString() => Id;

        public string UserName { get; private set; }

        [JsonIgnore]
        public string Password
        {
            get
            {
                var credentials = CredentialManager.GetCredentials($"FINAL FANTASY XIV-{UserName.ToLower()}");

                return credentials != null ? credentials.Password : string.Empty;
            }
            set
            {
                // TODO: Remove logging here after making sure fix was good
                // This will throw when the account doesn't actually exist
                try
                {
                    var a = CredentialManager.RemoveCredentials($"FINAL FANTASY XIV-{UserName.ToLower()}");

                    Log.Information($"Set Password RemoveCredentials: {a}");
                }
                catch (Win32Exception)
                {
                    // ignored
                }

                var b = CredentialManager.SaveCredentials($"FINAL FANTASY XIV-{UserName.ToLower()}", new NetworkCredential
                {
                    UserName = UserName,
                    Password = value
                });

                Log.Information($"Set Password SaveCredentials: {b}");
            }
        }
        
        [JsonIgnore]
        public string OtpUri
        {
            get
            {
                var credentials = CredentialManager.GetCredentials($"FINAL FANTASY XIV-{UserName.ToLower()}-OTP");

                return credentials != null ? credentials.Password : string.Empty;
            }
            set
            {
                // TODO: Remove logging here after making sure fix was good
                // This will throw when the account doesn't actually exist
                try
                {
                    var a = CredentialManager.RemoveCredentials($"FINAL FANTASY XIV-{UserName.ToLower()}-OTP");

                    Log.Information($"Set Password RemoveCredentials: {a}");
                }
                catch (Win32Exception)
                {
                    // ignored
                }

                var b = CredentialManager.SaveCredentials($"FINAL FANTASY XIV-{UserName.ToLower()}-OTP", new NetworkCredential
                {
                    UserName = UserName,
                    Password = value
                });

                Log.Information($"Set Password SaveCredentials: {b}");
            }
        }

        public bool SavePassword { get; set; }
        public bool UseSteamServiceAccount { get; set; }
        public bool UseOtp { get; set; }

        public string ChosenCharacterName;
        public string ChosenCharacterWorld;

        public string ThumbnailUrl;

        public XivAccount(string userName)
        {
            UserName = userName.ToLower();
        }

        public string FindCharacterThumb()
        {
            if (string.IsNullOrEmpty(ChosenCharacterName) || string.IsNullOrEmpty(ChosenCharacterWorld))
                return null;

            try
            {
                dynamic searchResponse = GetCharacterSearch(ChosenCharacterName, ChosenCharacterWorld)
                .GetAwaiter().GetResult();

                if (searchResponse.Results.Count > 1) //If we get more than one match from XIVAPI
                {
                    foreach (var AccountInfo in searchResponse.Results)
                    {
                        //We have to check with it all lower in case they type their character name LiKe ThIsLoL. The server XIVAPI returns also contains the DC name, so let's just do a contains on the server to make it easy.
                        if (AccountInfo.Name.Value.ToLower() == ChosenCharacterName.ToLower() && AccountInfo.Server.Value.ToLower().Contains(ChosenCharacterWorld.ToLower()))
                        {
                            return AccountInfo.Avatar.Value;
                        }
                    }
                }

                return searchResponse.Results.Count > 0 ? (string)searchResponse.Results[0].Avatar : null;
            }
            catch (Exception ex)
            {
                Log.Information(ex, "Couldn't download character search.");

                return null;
            }
        }

        private const string URL = "http://xivapi.com/";

        public static async Task<JObject> GetCharacterSearch(string name, string world)
        {
            return await Get("character/search" + $"?name={name}&server={world}");
        }

        public static async Task<dynamic> Get(string endpoint)
        {
            
            using (var client = new WebClient())
            {
                var result = client.DownloadString(URL + endpoint);

                var parsedObject = JObject.Parse(result);

                return parsedObject;
            }
        }
    }
}