using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Builder.IO
{
    /// <summary>
    /// Reads 3D-print files (from MakerWorld, Printables, Thingiverse ...):
    ///   - .stl (binary or text): triangles only, one color.
    ///   - .3mf (a zip with XML inside): objects, their parts and placement, and colors - standard 3MF colors
    ///     (basematerials / colorgroup) and Bambu Studio's filament per object / part.
    /// Print files are Z-up (Z = height): converted to the app's Y-up the same way the OBJ reader does it
    /// (one mirror + corners the other way round).
    /// </summary>
    public static class PrintReader
    {
        const string CoreNs = "http://schemas.microsoft.com/3dmanufacturing/core/2015/02";
        const string ProductionNs = "http://schemas.microsoft.com/3dmanufacturing/production/2015/06";
        const string MaterialNs = "http://schemas.microsoft.com/3dmanufacturing/material/2015/02";

        /// <summary>Print space (Z up) to app space (Y up).</summary>
        static Vector3 ToApp(Vector3 p) => new Vector3(p.x, p.z, p.y);

        // ---------- STL ----------

        public static (List<Vector3> vertices, List<int> triangles) ReadStl(byte[] data)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            void Add(Vector3 a, Vector3 b, Vector3 c)
            {
                int i = vertices.Count;
                vertices.Add(ToApp(a));
                vertices.Add(ToApp(b));
                vertices.Add(ToApp(c));
                triangles.Add(i);
                triangles.Add(i + 2);   // mirrored by ToApp: corners the other way round
                triangles.Add(i + 1);
            }

            bool binary = data.Length >= 84 && 84 + 50L * BitConverter.ToUInt32(data, 80) == data.Length;
            if (binary)
            {
                uint count = BitConverter.ToUInt32(data, 80);
                Vector3 V(int at) => new Vector3(BitConverter.ToSingle(data, at), BitConverter.ToSingle(data, at + 4), BitConverter.ToSingle(data, at + 8));
                for (int t = 0; t < count; t++)
                {
                    int at = 84 + t * 50 + 12;   // skip the normal
                    Add(V(at), V(at + 12), V(at + 24));
                }
                return (vertices, triangles);
            }

            var corners = new List<Vector3>(3);
            foreach (var raw in Encoding.ASCII.GetString(data).Split('\n'))
            {
                var line = raw.Trim();
                if (!line.StartsWith("vertex ", StringComparison.OrdinalIgnoreCase)) continue;
                var p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 4) continue;
                corners.Add(new Vector3(Num(p[1]), Num(p[2]), Num(p[3])));
                if (corners.Count == 3)
                {
                    Add(corners[0], corners[1], corners[2]);
                    corners.Clear();
                }
            }
            return (vertices, triangles);
        }

        // ---------- 3MF ----------

        public static (List<Vector3> vertices, List<int> triangles, List<Color32> colors) Read3mf(byte[] data)
        {
            var files = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase);
            var filament = new List<Color32>();
            var extruderOf = new Dictionary<int, int>();   // Bambu: object / part id -> filament number (1, 2 ...)
            using (var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read))
            {
                foreach (var entry in zip.Entries)
                {
                    string name = "/" + entry.FullName.TrimStart('/');
                    if (name.EndsWith(".model", StringComparison.OrdinalIgnoreCase))
                        using (var s = entry.Open()) files[name] = XDocument.Load(s);
                    else if (name.EndsWith("project_settings.config", StringComparison.OrdinalIgnoreCase))
                        using (var r = new StreamReader(entry.Open())) ReadFilaments(r.ReadToEnd(), filament);
                    else if (name.EndsWith("model_settings.config", StringComparison.OrdinalIgnoreCase))
                        using (var s = entry.Open()) ReadExtruders(XDocument.Load(s), extruderOf);
                }
            }
            string main = files.Keys.FirstOrDefault(k => k.Equals("/3D/3dmodel.model", StringComparison.OrdinalIgnoreCase))
                          ?? files.Keys.FirstOrDefault();
            if (main == null) throw new InvalidOperationException("no 3D model inside the .3mf file");

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var colors = new List<Color32>();
            bool anyColor = false;
            float unit = Unit(files[main]);

            XElement FindObject(string file, string id) =>
                files.TryGetValue(file, out var doc)
                    ? doc.Descendants(XName.Get("object", CoreNs)).FirstOrDefault(o => (string)o.Attribute("id") == id)
                    : null;

            Color32? ColorOf(string file, string pid, string index)
            {
                if (pid == null || !files.TryGetValue(file, out var doc)) return null;
                int i = int.TryParse(index, out var n) ? n : 0;
                foreach (var group in doc.Descendants())
                {
                    if ((string)group.Attribute("id") != pid) continue;
                    var items = group.Elements().ToList();
                    if (i < 0 || i >= items.Count) return null;
                    string hex = (string)items[i].Attribute("displaycolor") ?? (string)items[i].Attribute("color");
                    return Hex(hex);
                }
                return null;
            }

            void AddObject(string file, string id, Matrix4x4 m, Color32? inherited, int depth)
            {
                var obj = FindObject(file, id);
                if (obj == null || depth > 16) return;
                int.TryParse(id, out int idNumber);
                Color32? own = ColorOf(file, (string)obj.Attribute("pid"), (string)obj.Attribute("pindex"));
                if (extruderOf.TryGetValue(idNumber, out int extruder) && extruder >= 1 && extruder <= filament.Count && file == main)
                    own = filament[extruder - 1];
                var color = own ?? inherited;

                var mesh = obj.Element(XName.Get("mesh", CoreNs));
                if (mesh != null)
                {
                    int start = vertices.Count;
                    foreach (var v in mesh.Element(XName.Get("vertices", CoreNs))?.Elements() ?? Enumerable.Empty<XElement>())
                        vertices.Add(ToApp(m.MultiplyPoint3x4(new Vector3(Attr(v, "x"), Attr(v, "y"), Attr(v, "z"))) * unit));
                    bool mirrored = m.determinant < 0f;
                    foreach (var t in mesh.Element(XName.Get("triangles", CoreNs))?.Elements() ?? Enumerable.Empty<XElement>())
                    {
                        int a = start + (int)Attr(t, "v1"), b = start + (int)Attr(t, "v2"), c = start + (int)Attr(t, "v3");
                        triangles.Add(a);
                        triangles.Add(mirrored ? b : c);   // ToApp mirrors: corners the other way round (twice = as is)
                        triangles.Add(mirrored ? c : b);
                        var tc = ColorOf(file, (string)t.Attribute("pid") ?? (string)obj.Attribute("pid"),
                                         (string)t.Attribute("p1") ?? (string)obj.Attribute("pindex"));
                        var final = (string)t.Attribute("pid") != null ? tc ?? color : color;
                        if (final.HasValue) anyColor = true;
                        colors.Add(final ?? new Color32(176, 176, 176, 255));
                    }
                }
                foreach (var comp in obj.Element(XName.Get("components", CoreNs))?.Elements() ?? Enumerable.Empty<XElement>())
                {
                    string path = (string)comp.Attribute(XName.Get("path", ProductionNs)) ?? file;
                    string compId = (string)comp.Attribute("objectid");
                    var part = color;
                    if (int.TryParse(compId, out int pn) && extruderOf.TryGetValue(pn, out int pe) && pe >= 1 && pe <= filament.Count)
                        part = filament[pe - 1];
                    AddObject(path, compId, m * Transform((string)comp.Attribute("transform")), part, depth + 1);
                }
            }

            var build = files[main].Descendants(XName.Get("build", CoreNs)).FirstOrDefault();
            var items = build?.Elements(XName.Get("item", CoreNs)).ToList() ?? new List<XElement>();
            if (items.Count == 0)   // no build list: every object once
                items = files[main].Descendants(XName.Get("object", CoreNs))
                    .Select(o => new XElement("item", new XAttribute("objectid", (string)o.Attribute("id")))).ToList();
            foreach (var item in items)
            {
                string path = (string)item.Attribute(XName.Get("path", ProductionNs)) ?? main;
                AddObject(path, (string)item.Attribute("objectid"), Transform((string)item.Attribute("transform")), null, 0);
            }
            return (vertices, triangles, anyColor ? colors : null);
        }

        /// <summary>3MF transform "m00 m01 m02 m10 m11 m12 m20 m21 m22 m30 m31 m32" (row vectors: p' = p * M).</summary>
        static Matrix4x4 Transform(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Matrix4x4.identity;
            var n = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Select(Num).ToArray();
            if (n.Length < 12) return Matrix4x4.identity;
            var m = Matrix4x4.identity;
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                    m[col, row] = n[row * 3 + col];   // transposed to column vectors
            m[0, 3] = n[9];
            m[1, 3] = n[10];
            m[2, 3] = n[11];
            return m;
        }

        /// <summary>Meters per file unit (3MF default: millimeters).</summary>
        static float Unit(XDocument doc)
        {
            switch (((string)doc.Root?.Attribute("unit") ?? "millimeter").ToLowerInvariant())
            {
                case "micron": return 1e-6f;
                case "centimeter": return 0.01f;
                case "inch": return 0.0254f;
                case "foot": return 0.3048f;
                case "meter": return 1f;
                default: return 0.001f;
            }
        }

        static void ReadFilaments(string json, List<Color32> into)
        {
            try
            {
                var colors = JObject.Parse(json)["filament_colour"] as JArray;
                if (colors == null) return;
                foreach (var c in colors) into.Add(Hex((string)c) ?? new Color32(176, 176, 176, 255));
            }
            catch (Exception) { }   // not Bambu's format: no filament colors
        }

        static void ReadExtruders(XDocument doc, Dictionary<int, int> into)
        {
            foreach (var el in doc.Descendants().Where(e => e.Name.LocalName == "object" || e.Name.LocalName == "part"))
            {
                if (!int.TryParse((string)el.Attribute("id"), out int id)) continue;
                var meta = el.Elements().FirstOrDefault(m => m.Name.LocalName == "metadata" && (string)m.Attribute("key") == "extruder");
                if (meta != null && int.TryParse((string)meta.Attribute("value"), out int extruder)) into[id] = extruder;
            }
        }

        static Color32? Hex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;
            if (hex.Length == 9) hex = hex.Substring(0, 7);   // #RRGGBBAA
            return ColorUtility.TryParseHtmlString(hex, out var c) ? (Color32)c : (Color32?)null;
        }

        static float Attr(XElement e, string name) => Num((string)e.Attribute(name) ?? "0");

        static float Num(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
