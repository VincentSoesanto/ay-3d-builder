using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Builder.IO.Cloud
{
    /// <summary>
    /// Which Firebase project to use. Made in the Firebase console (free "Spark" plan); the app asks for the two values
    /// once and keeps them in cloud.json in its data folder. (The web API key is not a secret: the Firestore rules
    /// decide who may read what.) The three URLs are only changed by tests (a fake Firebase on this PC).
    /// </summary>
    public class CloudConfig
    {
        public string apiKey;
        public string projectId;
        public string authUrl = "https://identitytoolkit.googleapis.com/v1";
        public string tokenUrl = "https://securetoken.googleapis.com/v1";
        public string firestoreUrl = "https://firestore.googleapis.com/v1";

        public bool IsSet => !string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(projectId);

        public static string FilePath => Path.Combine(ProjectStore.Root, "cloud.json");

        public static CloudConfig Load()
        {
            try
            {
                if (File.Exists(FilePath)) return JsonConvert.DeserializeObject<CloudConfig>(File.ReadAllText(FilePath)) ?? new CloudConfig();
            }
            catch (Exception) { }
            return new CloudConfig();
        }

        public void Save() => ProjectStore.WriteSafely(FilePath, JsonConvert.SerializeObject(this, Formatting.Indented));
    }

    /// <summary>A signed-in user: who (uid, email) and the tokens that prove it.</summary>
    public class CloudUser
    {
        public string uid, email, refreshToken;
        [JsonIgnore] public string idToken;
        [JsonIgnore] public DateTime idTokenExpires;
    }

    /// <summary>A readable error from Firebase (e.g. "wrong password").</summary>
    public class CloudException : Exception
    {
        public readonly int status;
        public CloudException(string message, int status = 0) : base(message) { this.status = status; }
    }

    /// <summary>One Firestore document: its id, its fields (as plain values) and when it was last written.</summary>
    public class CloudDoc
    {
        public string id;
        public Dictionary<string, object> fields = new Dictionary<string, object>();

        public string Str(string key) => fields.TryGetValue(key, out var v) ? v as string : null;
        public long Long(string key) => fields.TryGetValue(key, out var v) && v is long l ? l : 0L;
        public bool Bool(string key) => fields.TryGetValue(key, out var v) && v is bool b && b;
    }

    /// <summary>
    /// Talks to Firebase over its plain web (REST) interface - no Firebase plugin needed, so it works the same on Windows
    /// and Android: sign up / sign in with email + password (Firebase Auth), and read / write / list / delete documents
    /// (Cloud Firestore). Every call is async; errors become <see cref="CloudException"/> with a readable message.
    /// </summary>
    public class CloudClient
    {
        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };

        readonly CloudConfig config;

        public CloudClient(CloudConfig config) => this.config = config;

        string DocumentsUrl => $"{config.firestoreUrl}/projects/{config.projectId}/databases/(default)/documents";

        // ---------- sign in ----------

        public Task<CloudUser> SignUp(string email, string password) => Account("accounts:signUp", email, password);
        public Task<CloudUser> SignIn(string email, string password) => Account("accounts:signInWithPassword", email, password);

        async Task<CloudUser> Account(string what, string email, string password)
        {
            var body = new JObject { ["email"] = email, ["password"] = password, ["returnSecureToken"] = true };
            var json = await Send(HttpMethod.Post, $"{config.authUrl}/{what}?key={config.apiKey}", body.ToString(), null);
            return new CloudUser
            {
                uid = (string)json["localId"], email = (string)json["email"] ?? email,
                refreshToken = (string)json["refreshToken"], idToken = (string)json["idToken"],
                idTokenExpires = DateTime.UtcNow.AddSeconds(ParseSeconds(json["expiresIn"]) - 60),
            };
        }

        /// <summary>Sends a "reset your password" email.</summary>
        public async Task ResetPassword(string email)
        {
            var body = new JObject { ["requestType"] = "PASSWORD_RESET", ["email"] = email };
            await Send(HttpMethod.Post, $"{config.authUrl}/accounts:sendOobCode?key={config.apiKey}", body.ToString(), null);
        }

        /// <summary>Gets a fresh id token (they last an hour) with the refresh token.</summary>
        public async Task Refresh(CloudUser user)
        {
            if (user.idToken != null && DateTime.UtcNow < user.idTokenExpires) return;
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token", ["refresh_token"] = user.refreshToken,
            });
            var response = await http.PostAsync($"{config.tokenUrl}/token?key={config.apiKey}", form);
            var json = await Read(response);
            user.idToken = (string)json["id_token"];
            user.refreshToken = (string)json["refresh_token"] ?? user.refreshToken;
            user.uid = (string)json["user_id"] ?? user.uid;
            user.idTokenExpires = DateTime.UtcNow.AddSeconds(ParseSeconds(json["expires_in"]) - 60);
        }

        static double ParseSeconds(JToken t) => double.TryParse((string)t, out var s) ? s : 3600;

        // ---------- documents ----------

        /// <summary>All documents in a collection (e.g. "users/&lt;uid&gt;/projects"), page by page.</summary>
        public async Task<List<CloudDoc>> List(CloudUser user, string collection)
        {
            var result = new List<CloudDoc>();
            string pageToken = null;
            do
            {
                string url = $"{DocumentsUrl}/{collection}?pageSize=300" + (pageToken != null ? "&pageToken=" + Uri.EscapeDataString(pageToken) : "");
                var json = await Send(HttpMethod.Get, url, null, user);
                if (json["documents"] is JArray docs)
                    foreach (var d in docs) result.Add(FromJson((JObject)d));
                pageToken = (string)json["nextPageToken"];
            } while (!string.IsNullOrEmpty(pageToken));
            return result;
        }

        /// <summary>One document, or null if it does not exist.</summary>
        public async Task<CloudDoc> Get(CloudUser user, string path)
        {
            try
            {
                return FromJson(await Send(HttpMethod.Get, $"{DocumentsUrl}/{path}", null, user));
            }
            catch (CloudException e) when (e.status == 404)
            {
                return null;
            }
        }

        /// <summary>Writes a document (creates it, or replaces all its fields).</summary>
        public async Task Set(CloudUser user, string path, Dictionary<string, object> fields)
        {
            var body = new JObject { ["fields"] = ToFields(fields) };
            await Send(new HttpMethod("PATCH"), $"{DocumentsUrl}/{path}", body.ToString(Formatting.None), user);
        }

        public async Task Delete(CloudUser user, string path)
        {
            try
            {
                await Send(HttpMethod.Delete, $"{DocumentsUrl}/{path}", null, user);
            }
            catch (CloudException e) when (e.status == 404) { }
        }

        // ---------- plumbing ----------

        async Task<JObject> Send(HttpMethod method, string url, string body, CloudUser user)
        {
            if (user != null) await Refresh(user);
            var request = new HttpRequestMessage(method, url);
            if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            if (user != null) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", user.idToken);
            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request);
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException)
            {
                throw new CloudException("no connection to the cloud (offline?)");
            }
            return await Read(response);
        }

        static async Task<JObject> Read(HttpResponseMessage response)
        {
            string text = await response.Content.ReadAsStringAsync();
            JObject json = null;
            try { json = string.IsNullOrWhiteSpace(text) ? new JObject() : JObject.Parse(text); }
            catch (JsonException) { }
            if (response.IsSuccessStatusCode) return json ?? new JObject();
            string message = (string)json?["error"]?["message"] ?? response.ReasonPhrase;
            throw new CloudException(Friendly(message), (int)response.StatusCode);
        }

        /// <summary>Firebase's error codes in plain words.</summary>
        static string Friendly(string code)
        {
            if (code == null) return "unknown error";
            if (code.StartsWith("EMAIL_EXISTS")) return "there is already an account with this email - sign in instead";
            if (code.StartsWith("EMAIL_NOT_FOUND") || code.StartsWith("INVALID_LOGIN_CREDENTIALS") || code.StartsWith("INVALID_PASSWORD"))
                return "wrong email or password";
            if (code.StartsWith("WEAK_PASSWORD")) return "the password is too short (at least 6 characters)";
            if (code.StartsWith("INVALID_EMAIL")) return "that email address doesn't look right";
            if (code.StartsWith("TOO_MANY_ATTEMPTS")) return "too many tries - wait a few minutes";
            if (code.StartsWith("OPERATION_NOT_ALLOWED")) return "email sign-in is not switched on in the Firebase console";
            if (code.Contains("API key not valid") || code.StartsWith("API_KEY_INVALID")) return "the Web API key is wrong";
            if (code.Contains("PERMISSION_DENIED") || code.Contains("Missing or insufficient permissions")) return "the cloud said no (check the Firestore rules)";
            if (code.Contains("NOT_FOUND") && code.Contains("database")) return "the Firestore database is not made yet (Firebase console > Firestore)";
            return code;
        }

        static CloudDoc FromJson(JObject json)
        {
            var doc = new CloudDoc();
            string name = (string)json["name"];
            doc.id = name?.Substring(name.LastIndexOf('/') + 1);
            if (json["fields"] is JObject fields)
                foreach (var f in fields)
                {
                    var v = (JObject)f.Value;
                    if (v["stringValue"] != null) doc.fields[f.Key] = (string)v["stringValue"];
                    else if (v["integerValue"] != null) doc.fields[f.Key] = long.Parse((string)v["integerValue"]);
                    else if (v["booleanValue"] != null) doc.fields[f.Key] = (bool)v["booleanValue"];
                    else if (v["doubleValue"] != null) doc.fields[f.Key] = (double)v["doubleValue"];
                }
            return doc;
        }

        static JObject ToFields(Dictionary<string, object> fields)
        {
            var o = new JObject();
            foreach (var pair in fields)
            {
                switch (pair.Value)
                {
                    case string s: o[pair.Key] = new JObject { ["stringValue"] = s }; break;
                    case bool b: o[pair.Key] = new JObject { ["booleanValue"] = b }; break;
                    case int i: o[pair.Key] = new JObject { ["integerValue"] = i.ToString() }; break;
                    case long l: o[pair.Key] = new JObject { ["integerValue"] = l.ToString() }; break;
                    case double d: o[pair.Key] = new JObject { ["doubleValue"] = d }; break;
                }
            }
            return o;
        }
    }
}
