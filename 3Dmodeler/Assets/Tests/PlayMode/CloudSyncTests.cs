using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Builder.Core;
using Builder.IO;
using Builder.IO.Cloud;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Builder.Tests
{
    /// <summary>
    /// A pretend Firebase on this PC (Auth + Firestore REST, just what the app uses), so cloud sync can be tested without
    /// a real account. Like the real Firestore rules, a user may only touch users/&lt;their uid&gt;/...
    /// </summary>
    public class FakeFirebase : IDisposable
    {
        readonly HttpListener listener = new HttpListener();
        readonly Dictionary<string, (string uid, string password)> accounts = new Dictionary<string, (string, string)>();
        readonly Dictionary<string, string> tokens = new Dictionary<string, string>();   // id token -> uid
        public readonly Dictionary<string, JObject> docs = new Dictionary<string, JObject>();   // path -> fields
        public readonly int port;
        int counter;

        public FakeFirebase()
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            listener.Prefixes.Add($"http://localhost:{port}/");
            listener.Start();
            Task.Run(Loop);
        }

        public CloudConfig Config => new CloudConfig
        {
            apiKey = "test-key", projectId = "demo",
            authUrl = $"http://localhost:{port}/auth", tokenUrl = $"http://localhost:{port}/token", firestoreUrl = $"http://localhost:{port}/fs",
        };

        public void Dispose() => listener.Close();

        async Task Loop()
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch (Exception) { return; }
                try { Handle(ctx); }
                catch (Exception e) { Reply(ctx, 500, new JObject { ["error"] = new JObject { ["message"] = e.Message } }); }
            }
        }

        static void Reply(HttpListenerContext ctx, int status, JObject body)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(body.ToString());
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        static JObject Error(string message, int code = 400) => new JObject { ["error"] = new JObject { ["code"] = code, ["message"] = message } };

        string NewToken(string uid)
        {
            lock (tokens)
            {
                string t = "tok" + (++counter) + uid;
                tokens[t] = uid;
                return t;
            }
        }

        void Handle(HttpListenerContext ctx)
        {
            string path = Uri.UnescapeDataString(ctx.Request.Url.AbsolutePath);
            string body = new StreamReader(ctx.Request.InputStream).ReadToEnd();
            string method = ctx.Request.HttpMethod;

            if (path.StartsWith("/auth/"))
            {
                var j = JObject.Parse(body);
                string email = (string)j["email"], password = (string)j["password"];
                lock (accounts)
                {
                    if (path.EndsWith("accounts:signUp"))
                    {
                        if (accounts.ContainsKey(email)) { Reply(ctx, 400, Error("EMAIL_EXISTS")); return; }
                        if ((password ?? "").Length < 6) { Reply(ctx, 400, Error("WEAK_PASSWORD : Password should be at least 6 characters")); return; }
                        accounts[email] = ("uid" + accounts.Count, password);
                    }
                    else if (path.EndsWith("accounts:sendOobCode")) { Reply(ctx, 200, new JObject { ["email"] = email }); return; }
                    if (!accounts.TryGetValue(email, out var acc) || acc.password != password) { Reply(ctx, 400, Error("INVALID_LOGIN_CREDENTIALS")); return; }
                    Reply(ctx, 200, new JObject
                    {
                        ["localId"] = acc.uid, ["email"] = email, ["idToken"] = NewToken(acc.uid), ["refreshToken"] = "ref-" + acc.uid, ["expiresIn"] = "3600",
                    });
                    return;
                }
            }
            if (path.StartsWith("/token/"))
            {
                string refresh = body.Split('&').Select(p => p.Split('=')).First(p => p[0] == "refresh_token")[1];
                string uid = refresh.Substring(4);
                Reply(ctx, 200, new JObject { ["id_token"] = NewToken(uid), ["refresh_token"] = refresh, ["user_id"] = uid, ["expires_in"] = "3600" });
                return;
            }

            // Firestore: /fs/projects/demo/databases/(default)/documents/<path>
            const string prefix = "/fs/projects/demo/databases/(default)/documents/";
            if (!path.StartsWith(prefix)) { Reply(ctx, 404, Error("NOT_FOUND", 404)); return; }
            string doc = path.Substring(prefix.Length);
            string auth = ctx.Request.Headers["Authorization"] ?? "";
            string who;
            lock (tokens) tokens.TryGetValue(auth.Replace("Bearer ", ""), out who);
            if (who == null || !doc.StartsWith("users/" + who + "/")) { Reply(ctx, 403, Error("Missing or insufficient permissions.", 403)); return; }

            lock (docs)
            {
                int segments = doc.Split('/').Length;
                if (method == "GET" && segments % 2 == 1)   // a collection: list its documents
                {
                    var list = new JArray();
                    foreach (var pair in docs.Where(d => d.Key.StartsWith(doc + "/") && d.Key.Split('/').Length == segments + 1))
                        list.Add(new JObject { ["name"] = prefix.Substring(4) + pair.Key, ["fields"] = pair.Value });
                    Reply(ctx, 200, new JObject { ["documents"] = list });
                }
                else if (method == "GET")
                {
                    if (docs.TryGetValue(doc, out var f)) Reply(ctx, 200, new JObject { ["name"] = prefix.Substring(4) + doc, ["fields"] = f });
                    else Reply(ctx, 404, Error("NOT_FOUND", 404));
                }
                else if (method == "PATCH")
                {
                    docs[doc] = (JObject)JObject.Parse(body)["fields"];
                    Reply(ctx, 200, new JObject { ["name"] = prefix.Substring(4) + doc, ["fields"] = docs[doc] });
                }
                else if (method == "DELETE")
                {
                    docs.Remove(doc);
                    Reply(ctx, 200, new JObject());
                }
            }
        }
    }

    public class CloudSyncTests
    {
        string root;
        FakeFirebase fake;

        [SetUp]
        public void Setup()
        {
            root = Path.Combine(Path.GetTempPath(), "AY3DCloudTests_" + Guid.NewGuid().ToString("N"));
            fake = new FakeFirebase();
        }

        [TearDown]
        public void Teardown()
        {
            ProjectStore.RootOverride = null;
            fake.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        /// <summary>Switches "which device we are": every device has its own data folder.</summary>
        string Device(string name)
        {
            ProjectStore.RootOverride = Path.Combine(root, name);
            Directory.CreateDirectory(ProjectStore.RootOverride);
            return ProjectStore.RootOverride;
        }

        static IEnumerator Wait<T>(Task<T> task)
        {
            while (!task.IsCompleted) yield return null;
            if (task.Exception != null) throw task.Exception.InnerException ?? task.Exception;
        }

        static ISyncKind[] Kinds => new ISyncKind[] { new ModelsKind(), new BlueprintsKind(), new ProjectsKind() };

        SyncReport last;

        IEnumerator Sync(string device, CloudUser user)
        {
            var engine = new SyncEngine(new CloudClient(fake.Config), user, device, Device(device));
            var task = engine.Sync(Kinds);
            yield return Wait(task);
            last = task.Result;
        }

        [UnityTest]
        public IEnumerator TwoDevices_UploadDownloadChangeConflictDelete()
        {
            var client = new CloudClient(fake.Config);

            // Device A: a project, a blueprint (with picture) and a model; make an account, sync up.
            Device("A");
            var doc = ProjectDocument.CreateNew("Mech", 0);
            doc.content.pieces.Add(new Piece { id = "p1", shape = "box", p = new ShapeParams() });
            ProjectStore.Save(doc);
            var bp = new Blueprint { id = "bp1", name = "Arm", version = 1 };
            BlueprintStore.Save(bp, new byte[] { 1, 2, 3 });
            Directory.CreateDirectory(ModelLibrary.Folder);
            File.WriteAllText(Path.Combine(ModelLibrary.Folder, "m1.json"), "{\"id\":\"m1\",\"name\":\"Gun\"}");

            var signUp = client.SignUp("user@example.com", "secret1");
            yield return Wait(signUp);
            var userA = signUp.Result;
            yield return Sync("A", userA);
            Assert.AreEqual(3, last.up, "project, blueprint and model went up");

            // Device B: sign in with the same account, sync down.
            var signIn = client.SignIn("user@example.com", "secret1");
            yield return Wait(signIn);
            var userB = signIn.Result;
            Assert.AreEqual(userA.uid, userB.uid);
            yield return Sync("B", userB);
            Assert.AreEqual(3, last.down);
            Assert.AreEqual("Mech", ProjectStore.Load(doc.id).name);
            Assert.IsTrue(File.Exists(BlueprintStore.PicturePath("bp1")), "the blueprint's picture came too");
            Assert.IsTrue(File.Exists(Path.Combine(ModelLibrary.Folder, "m1.json")));
            yield return Sync("B", userB);
            Assert.AreEqual("everything is up to date", last.ToString());

            // Changed on B -> comes to A.
            var onB = ProjectStore.Load(doc.id);
            onB.name = "Mech B";
            ProjectStore.Save(onB);
            yield return Sync("B", userB);
            Assert.AreEqual(1, last.up);
            yield return Sync("A", userA);
            Assert.AreEqual(1, last.down);
            Assert.AreEqual("Mech B", ProjectStore.Load(doc.id).name);

            // Changed on BOTH -> the cloud one wins, the other is kept as a conflict copy (nothing lost).
            var onA = ProjectStore.Load(doc.id);
            onA.name = "Mech A2";
            ProjectStore.Save(onA);
            yield return Sync("A", userA);
            Device("B");
            onB = ProjectStore.Load(doc.id);
            onB.name = "Mech B2";
            System.Threading.Thread.Sleep(5);
            ProjectStore.Save(onB);
            yield return Sync("B", userB);
            Assert.AreEqual(1, last.conflicts);
            Assert.AreEqual("Mech A2", ProjectStore.Load(doc.id).name, "B now has A's version");
            var copy = ProjectStore.List().Single(i => i.name == "Mech B2 (conflict copy)");
            yield return Sync("A", userA);
            Assert.IsTrue(ProjectStore.Exists(copy.id), "and A gets B's version as the conflict copy");

            // Deleted on A -> deleted on B too (to its trash).
            ProjectStore.Delete(copy.id);
            yield return Sync("A", userA);
            Assert.AreEqual(1, last.deleted);
            yield return Sync("B", userB);
            Assert.AreEqual(1, last.deleted);
            Assert.IsFalse(ProjectStore.Exists(copy.id));
            Assert.IsTrue(Directory.GetFiles(ProjectStore.TrashFolder).Any(f => Path.GetFileName(f).StartsWith(copy.id)), "it is in B's trash");
        }

        [UnityTest]
        public IEnumerator InTheApp_SignUp_SyncsByItself_AfterASave()
        {
            Device("App");
            var config = fake.Config;
            config.Save();   // cloud.json in the app's data folder
            UnityEngine.SceneManagement.SceneManager.LoadScene("Builder");
            yield return null;
            yield return null;

            var cloud = UnityEngine.Object.FindFirstObjectByType<CloudSync>();
            Assert.IsTrue(cloud.Config.IsSet);
            Assert.IsFalse(cloud.SignedIn);
            var signIn = cloud.SignIn("app@example.com", "secret1", newAccount: true);
            yield return Wait(signIn);
            Assert.IsNull(signIn.Result, signIn.Result);
            float until = Time.realtimeSinceStartup + 20f;
            while ((cloud.Busy || !cloud.Status.StartsWith("Synced")) && Time.realtimeSinceStartup < until) yield return null;
            StringAssert.StartsWith("Synced", cloud.Status);

            // Place something and save: it goes up by itself a few seconds later.
            var session = UnityEngine.Object.FindFirstObjectByType<BuilderSession>();
            session.Execute(new AddPiecesCommand(new[] { new Piece { id = "x", shape = "box", p = new ShapeParams() } }));
            UnityEngine.Object.FindFirstObjectByType<ProjectController>().Save(quiet: true);
            cloud.SyncSoon(0.1f);
            until = Time.realtimeSinceStartup + 20f;
            string header = $"users/{cloud.User.uid}/projects/{session.Document.id}";
            while (!fake.docs.ContainsKey(header) && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsTrue(fake.docs.ContainsKey(header), "the project is in the cloud");
            Assert.IsTrue(File.Exists(Path.Combine(ProjectStore.Root, "cloud", "user.json")), "stays signed in next time");
            while (cloud.Busy) yield return null;
            UnityEngine.Object.Destroy(cloud);   // nothing may keep syncing after the test (its folder is temporary)
            yield return null;
        }

        [UnityTest]
        public IEnumerator BigProject_GoesInChunks_AndWrongPasswordIsExplained()
        {
            var client = new CloudClient(fake.Config);
            Device("A");
            var doc = ProjectDocument.CreateNew("Big", 0);
            for (int i = 0; i < 9000; i++)
                doc.content.pieces.Add(new Piece { id = "p" + i, shape = "box", p = new ShapeParams(), position = new Vec3(i, 0, 0) });
            ProjectStore.Save(doc);
            string original = File.ReadAllText(ProjectStore.PathOf(doc.id));
            Assert.That(original.Length, Is.GreaterThan(SyncEngine.ChunkSize), "big enough to need more than one chunk");

            var signUp = client.SignUp("big@example.com", "secret1");
            yield return Wait(signUp);
            yield return Sync("A", signUp.Result);
            Assert.That(fake.docs.Keys.Count(k => k.Contains(doc.id + "/chunks/")), Is.GreaterThan(1));

            yield return Sync("B", signUp.Result);
            Assert.AreEqual(original, File.ReadAllText(ProjectStore.PathOf(doc.id)), "arrives exactly the same");

            var wrong = client.SignIn("big@example.com", "nope");
            while (!wrong.IsCompleted) yield return null;
            Assert.IsInstanceOf<CloudException>(wrong.Exception.InnerException);
            Assert.AreEqual("wrong email or password", wrong.Exception.InnerException.Message);
        }
    }
}
