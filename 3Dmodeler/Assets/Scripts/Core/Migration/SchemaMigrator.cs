using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Builder.Core
{
    /// <summary>
    /// Upgrades old save files to the current format, one version at a time, before they are read.
    /// When the format changes: bump <see cref="ProjectDocument.CurrentSchemaVersion"/> and add a step here
    /// that turns version N into N + 1 (rename / move / fill in fields on the raw JSON).
    /// </summary>
    public static class SchemaMigrator
    {
        /// <summary>Step that upgrades a file of version (key) to version (key + 1).</summary>
        static readonly Dictionary<int, Action<JObject>> Steps = new Dictionary<int, Action<JObject>>
        {
            // Example for later:  { 1, root => root["content"]["newList"] = new JArray() },
        };

        /// <summary>Brings <paramref name="root"/> up to <paramref name="target"/>. Throws for files from a newer app.</summary>
        public static void Upgrade(JObject root, int target)
        {
            int version = root.Value<int?>("schemaVersion") ?? 1;
            if (version > target)
                throw new InvalidOperationException(
                    $"This file was saved by a newer version of the app (format {version}, this app reads up to {target}).");

            while (version < target)
            {
                if (!Steps.TryGetValue(version, out var step))
                    throw new InvalidOperationException($"No upgrade step from format {version} to {version + 1}.");
                step(root);
                version++;
                root["schemaVersion"] = version;
            }
        }
    }
}
