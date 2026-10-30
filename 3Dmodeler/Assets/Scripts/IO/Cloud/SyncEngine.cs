using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Builder.IO.Cloud
{
    /// <summary>One kind of thing that is synced (projects, blueprints, imported models), as files on this device.</summary>
    public interface ISyncKind
    {
        string Name { get; }                                  // also the cloud collection name
        Dictionary<string, (long modified, string name)> ListLocal();
        string Read(string id);                               // the item as one text ("payload")
        void Write(string id, string payload, long modified); // put a downloaded item in place
        void DeleteLocal(string id);                          // deleted on another device: to this device's trash
        string CopyAsConflict(string id);                     // keep this device's version under a new id; returns it
    }

    /// <summary>What one sync did.</summary>
    public class SyncReport
    {
        public int up, down, deleted, conflicts, skipped;
        public readonly List<string> downloaded = new List<string>();   // "kind/id" of downloaded items
        public override string ToString() =>
            up + down + deleted + conflicts == 0 ? "everything is up to date"
            : $"{up} up, {down} down" + (deleted > 0 ? $", {deleted} deleted" : "") + (conflicts > 0 ? $", {conflicts} kept as conflict copy" : "");
    }

    /// <summary>
    /// The sync rules (no Unity calls, so it can run in tests). For every item on this device and/or in the cloud:
    ///   - only here -> upload; only in the cloud -> download (or, if it WAS synced before, it was deleted here -> delete
    ///     it in the cloud too, as a "deleted" marker so other devices delete it as well);
    ///   - changed on one side since the last sync -> that side wins;
    ///   - changed on BOTH sides -> the cloud version is downloaded and this device's version is kept as a
    ///     "(conflict copy)" (and uploaded) - nothing is ever lost.
    /// Cloud layout: users/&lt;uid&gt;/&lt;kind&gt;/&lt;id&gt; = { modified, device, name, chunks, rev, deleted } and the item's text in
    /// users/&lt;uid&gt;/&lt;kind&gt;/&lt;id&gt;/chunks/&lt;n&gt; (Firestore documents are limited to 1 MB, so big items are split).
    /// "Last synced" times are remembered per account in cloud/state-&lt;uid&gt;.json.
    /// </summary>
    public class SyncEngine
    {
        public const int ChunkSize = 700_000;   // characters per chunk (well under Firestore's 1 MB per document)

        readonly CloudClient client;
        readonly CloudUser user;
        readonly string deviceId;
        readonly string stateFile;
        readonly string dataFolder;
        Dictionary<string, Dictionary<string, long>> state;

        /// <summary>Items that must not be replaced right now (e.g. the open project with unsaved changes).</summary>
        public Func<string, string, bool> IsBusy = (kind, id) => false;

        public SyncEngine(CloudClient client, CloudUser user, string deviceId, string dataFolder)
        {
            this.client = client;
            this.user = user;
            this.deviceId = deviceId;
            this.dataFolder = dataFolder;
            stateFile = Path.Combine(dataFolder, "cloud", "state-" + user.uid + ".json");
        }

        string Collection(ISyncKind kind) => $"users/{user.uid}/{kind.Name}";

        public async Task<SyncReport> Sync(IEnumerable<ISyncKind> kinds)
        {
            LoadState();
            var report = new SyncReport();
            foreach (var kind in kinds) await SyncKind(kind, report);
            return report;
        }

        async Task SyncKind(ISyncKind kind, SyncReport report)
        {
            if (!state.TryGetValue(kind.Name, out var synced)) state[kind.Name] = synced = new Dictionary<string, long>();
            var remote = (await client.List(user, Collection(kind))).ToDictionary(d => d.id);
            var local = kind.ListLocal();

            foreach (var id in local.Keys.Union(remote.Keys).ToList())
            {
                if (ProjectStore.Root != dataFolder) throw new CloudException("stopped (the data folder changed)");
                bool hasLocal = local.TryGetValue(id, out var l);
                bool hasRemote = remote.TryGetValue(id, out var r);
                bool wasSynced = synced.TryGetValue(id, out var s);
                long rModified = hasRemote ? r.Long("modified") : 0;
                bool rDeleted = hasRemote && r.Bool("deleted");

                if (IsBusy(kind.Name, id) && hasRemote && (!hasLocal || rModified != l.modified))
                {
                    report.skipped++;   // e.g. the open project has unsaved changes: next time
                    continue;
                }

                if (hasLocal && (!hasRemote || (!rDeleted && !wasSynced && l.modified > rModified)))
                {
                    await Upload(kind, id, l.modified, l.name, r);
                    synced[id] = l.modified;
                    report.up++;
                }
                else if (!hasLocal && hasRemote)
                {
                    if (rDeleted) synced.Remove(id);
                    else if (wasSynced)
                    {
                        await MarkDeleted(kind, id, r);   // deleted on this device
                        synced.Remove(id);
                        report.deleted++;
                    }
                    else
                    {
                        await Download(kind, id, r, report);
                        synced[id] = rModified;
                    }
                }
                else if (hasLocal && rDeleted)
                {
                    if (wasSynced && l.modified <= s)
                    {
                        kind.DeleteLocal(id);   // deleted on another device
                        synced.Remove(id);
                        report.deleted++;
                    }
                    else
                    {
                        await Upload(kind, id, l.modified, l.name, r);   // changed here after: bring it back
                        synced[id] = l.modified;
                        report.up++;
                    }
                }
                else if (hasLocal && hasRemote)
                {
                    if (l.modified == rModified) { synced[id] = l.modified; continue; }
                    bool localChanged = !wasSynced || l.modified != s;
                    bool remoteChanged = !wasSynced || rModified != s;
                    if (localChanged && remoteChanged && wasSynced)
                    {
                        // Both changed: keep this device's version as a copy, take the cloud's.
                        string copy = kind.CopyAsConflict(id);
                        await Download(kind, id, r, report);
                        synced[id] = rModified;
                        var copyInfo = kind.ListLocal()[copy];
                        await Upload(kind, copy, copyInfo.modified, copyInfo.name, null);
                        synced[copy] = copyInfo.modified;
                        report.conflicts++;
                    }
                    else if (l.modified > rModified && localChanged)
                    {
                        await Upload(kind, id, l.modified, l.name, r);
                        synced[id] = l.modified;
                        report.up++;
                    }
                    else
                    {
                        await Download(kind, id, r, report);
                        synced[id] = rModified;
                    }
                }
                SaveState();
            }
            SaveState();
        }

        async Task Upload(ISyncKind kind, string id, long modified, string name, CloudDoc old)
        {
            string payload = kind.Read(id);
            string rev = Guid.NewGuid().ToString("N");
            int chunks = Math.Max(1, (payload.Length + ChunkSize - 1) / ChunkSize);
            string path = $"{Collection(kind)}/{id}";
            // Chunks first, then the header: someone reading never sees a header pointing at missing chunks.
            for (int i = 0; i < chunks; i++)
            {
                string part = payload.Substring(i * ChunkSize, Math.Min(ChunkSize, payload.Length - i * ChunkSize));
                await client.Set(user, $"{path}/chunks/{i}", new Dictionary<string, object> { ["data"] = part, ["rev"] = rev });
            }
            await client.Set(user, path, new Dictionary<string, object>
            {
                ["modified"] = modified, ["device"] = deviceId, ["name"] = name ?? "", ["chunks"] = chunks, ["rev"] = rev, ["deleted"] = false,
            });
            long oldChunks = old?.Long("chunks") ?? 0;
            for (long i = chunks; i < oldChunks; i++) await client.Delete(user, $"{path}/chunks/{i}");
        }

        async Task Download(ISyncKind kind, string id, CloudDoc header, SyncReport report)
        {
            string path = $"{Collection(kind)}/{id}";
            string rev = header.Str("rev");
            var text = new System.Text.StringBuilder();
            for (long i = 0; i < header.Long("chunks"); i++)
            {
                var chunk = await client.Get(user, $"{path}/chunks/{i}");
                if (chunk == null || chunk.Str("rev") != rev) throw new CloudException($"{kind.Name} {header.Str("name")} is being changed right now - try again");
                text.Append(chunk.Str("data"));
            }
            kind.Write(id, text.ToString(), header.Long("modified"));
            report.down++;
            report.downloaded.Add(kind.Name + "/" + id);
        }

        async Task MarkDeleted(ISyncKind kind, string id, CloudDoc old)
        {
            string path = $"{Collection(kind)}/{id}";
            await client.Set(user, path, new Dictionary<string, object>
            {
                ["modified"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ["device"] = deviceId,
                ["name"] = old?.Str("name") ?? "", ["chunks"] = 0, ["rev"] = "", ["deleted"] = true,
            });
            for (long i = 0; i < (old?.Long("chunks") ?? 0); i++) await client.Delete(user, $"{path}/chunks/{i}");
        }

        void LoadState()
        {
            try
            {
                state = File.Exists(stateFile)
                    ? JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, long>>>(File.ReadAllText(stateFile))
                    : null;
            }
            catch (Exception) { state = null; }
            state ??= new Dictionary<string, Dictionary<string, long>>();
        }

        void SaveState()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stateFile));
            ProjectStore.WriteSafely(stateFile, JsonConvert.SerializeObject(state));
        }
    }

    // ======================= the three kinds =======================

    /// <summary>Projects: the project files themselves (they carry their own "modified" time).</summary>
    public class ProjectsKind : ISyncKind
    {
        public string Name => "projects";

        public Dictionary<string, (long, string)> ListLocal() =>
            ProjectStore.List().ToDictionary(i => i.id, i => (i.modifiedUtc, i.name));

        public string Read(string id) => File.ReadAllText(ProjectStore.PathOf(id));

        public void Write(string id, string payload, long modified) => ProjectStore.WriteSafely(ProjectStore.PathOf(id), payload);

        public void DeleteLocal(string id) => ProjectStore.Delete(id);

        public string CopyAsConflict(string id)
        {
            var json = JObject.Parse(Read(id));
            string copy = Core.Ids.New();
            json["id"] = copy;
            json["name"] = (string)json["name"] + " (conflict copy)";
            json["modifiedUtc"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            ProjectStore.WriteSafely(ProjectStore.PathOf(copy), json.ToString(Formatting.None));
            return copy;
        }
    }

    /// <summary>Blueprints: the blueprint file + its picture, together in one payload.</summary>
    public class BlueprintsKind : ISyncKind
    {
        public string Name => "blueprints";

        static string JsonPath(string id) => Path.Combine(BlueprintStore.Folder, id + ".json");

        public Dictionary<string, (long, string)> ListLocal()
        {
            var list = new Dictionary<string, (long, string)>();
            if (!Directory.Exists(BlueprintStore.Folder)) return list;
            foreach (var file in Directory.GetFiles(BlueprintStore.Folder, "*.json"))
            {
                try
                {
                    var j = JObject.Parse(File.ReadAllText(file));
                    string id = (string)j["id"];
                    if (id != null) list[id] = ((long?)j["modifiedUtc"] ?? 0L, (string)j["name"]);
                }
                catch (Exception) { }
            }
            return list;
        }

        public string Read(string id)
        {
            string png = BlueprintStore.PicturePath(id);
            return new JObject
            {
                ["json"] = File.ReadAllText(JsonPath(id)),
                ["png"] = File.Exists(png) ? Convert.ToBase64String(File.ReadAllBytes(png)) : null,
            }.ToString(Formatting.None);
        }

        public void Write(string id, string payload, long modified)
        {
            var j = JObject.Parse(payload);
            Directory.CreateDirectory(BlueprintStore.Folder);
            ProjectStore.WriteSafely(JsonPath(id), (string)j["json"]);
            var png = (string)j["png"];
            if (!string.IsNullOrEmpty(png)) File.WriteAllBytes(BlueprintStore.PicturePath(id), Convert.FromBase64String(png));
        }

        public void DeleteLocal(string id) => BlueprintStore.Delete(id);

        public string CopyAsConflict(string id)
        {
            var json = JObject.Parse(File.ReadAllText(JsonPath(id)));
            string copy = Core.Ids.New();
            json["id"] = copy;
            json["name"] = (string)json["name"] + " (conflict copy)";
            json["modifiedUtc"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            ProjectStore.WriteSafely(JsonPath(copy), json.ToString(Formatting.None));
            if (File.Exists(BlueprintStore.PicturePath(id))) File.Copy(BlueprintStore.PicturePath(id), BlueprintStore.PicturePath(copy), true);
            return copy;
        }
    }

    /// <summary>
    /// Imported models: listed ones (models/) and deleted ones (models/trash/, still used by placed pieces).
    /// They have no "modified" inside, so the file time is used (and set to the cloud's time after a download).
    /// </summary>
    public class ModelsKind : ISyncKind
    {
        public string Name => "models";

        static string Listed(string id) => Path.Combine(ModelLibrary.Folder, id + ".json");
        static string Trashed(string id) => Path.Combine(ModelLibrary.Folder, "trash", id + ".json");

        static long Millis(string file) => new DateTimeOffset(File.GetLastWriteTimeUtc(file)).ToUnixTimeMilliseconds() / 1000 * 1000;

        public Dictionary<string, (long, string)> ListLocal()
        {
            var list = new Dictionary<string, (long, string)>();
            foreach (var folder in new[] { Path.Combine(ModelLibrary.Folder, "trash"), ModelLibrary.Folder })
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var file in Directory.GetFiles(folder, "*.json"))
                    list[Path.GetFileNameWithoutExtension(file)] = (Millis(file), Path.GetFileNameWithoutExtension(file));
            }
            return list;
        }

        public string Read(string id)
        {
            bool listed = File.Exists(Listed(id));
            return new JObject { ["listed"] = listed, ["json"] = File.ReadAllText(listed ? Listed(id) : Trashed(id)) }.ToString(Formatting.None);
        }

        public void Write(string id, string payload, long modified)
        {
            var j = JObject.Parse(payload);
            bool listed = (bool?)j["listed"] ?? true;
            string target = listed ? Listed(id) : Trashed(id);
            string other = listed ? Trashed(id) : Listed(id);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            ProjectStore.WriteSafely(target, (string)j["json"]);
            if (File.Exists(other)) File.Delete(other);
            File.SetLastWriteTimeUtc(target, DateTimeOffset.FromUnixTimeMilliseconds(modified).UtcDateTime);
        }

        public void DeleteLocal(string id)
        {
            // Models are never really deleted (placed pieces may use them): just take them out of the inventory.
            if (!File.Exists(Listed(id))) return;
            Directory.CreateDirectory(Path.GetDirectoryName(Trashed(id)));
            if (File.Exists(Trashed(id))) File.Delete(Trashed(id));
            File.Move(Listed(id), Trashed(id));
        }

        public string CopyAsConflict(string id) => id;   // models don't really change: the newer one simply wins
    }
}
