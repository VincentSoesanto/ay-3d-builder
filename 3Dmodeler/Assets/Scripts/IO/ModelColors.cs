using System.Collections.Generic;
using UnityEngine;

namespace Builder.IO
{
    /// <summary>
    /// Imported models keep their colors: every triangle gets a color (from the file's materials, textures or vertex
    /// colors), then close colors are merged until there are at most <see cref="MaxColors"/> - the same rule as the
    /// built-in parts library (tools/library/build_library.py). Each color becomes its own piece when placed.
    /// </summary>
    public static class ModelColors
    {
        public const int MaxColors = 8;
        const float MergeDistance = 42f;   // colors closer than this (0-255 per channel) always become one

        /// <summary>The colors ("#RRGGBB") and, per triangle, which of them it uses.</summary>
        public static (string[] colors, byte[] index) Merge(IReadOnlyList<Color32> triangleColors)
        {
            // Start with the exact colors (rounded a little, so a texture's thousands of shades become fewer).
            var groups = new List<(Vector3 rgb, List<int> tris)>();
            var byKey = new Dictionary<int, int>();
            int step = 8;
            while (true)
            {
                groups.Clear();
                byKey.Clear();
                for (int i = 0; i < triangleColors.Count; i++)
                {
                    var c = triangleColors[i];
                    int key = (c.r / step) << 16 | (c.g / step) << 8 | (c.b / step);
                    if (!byKey.TryGetValue(key, out int g))
                    {
                        byKey[key] = g = groups.Count;
                        groups.Add((Vector3.zero, new List<int>()));
                    }
                    var group = groups[g];
                    group.rgb += new Vector3(c.r, c.g, c.b);   // summed now, averaged below
                    group.tris.Add(i);
                    groups[g] = group;
                }
                if (groups.Count <= 250 || step >= 64) break;
                step *= 2;
            }
            for (int g = 0; g < groups.Count; g++) groups[g] = (groups[g].rgb / groups[g].tris.Count, groups[g].tris);

            // Merge the two closest colors (weighted by how many triangles they color) until they are far enough apart.
            while (groups.Count > 1)
            {
                float best = float.MaxValue;
                int bi = 0, bj = 1;
                for (int i = 0; i < groups.Count; i++)
                    for (int j = i + 1; j < groups.Count; j++)
                    {
                        float d = Vector3.Distance(groups[i].rgb, groups[j].rgb);
                        if (d < best) { best = d; bi = i; bj = j; }
                    }
                if (best > MergeDistance && groups.Count <= MaxColors) break;
                float wi = groups[bi].tris.Count, wj = groups[bj].tris.Count;
                var merged = (groups[bi].rgb * (wi / (wi + wj)) + groups[bj].rgb * (wj / (wi + wj)), groups[bi].tris);
                merged.tris.AddRange(groups[bj].tris);
                groups[bi] = merged;
                groups.RemoveAt(bj);
            }

            // Biggest color first.
            groups.Sort((a, b) => b.tris.Count.CompareTo(a.tris.Count));
            var colors = new string[groups.Count];
            var index = new byte[triangleColors.Count];
            for (int g = 0; g < groups.Count; g++)
            {
                var c = groups[g].rgb;
                colors[g] = $"#{Mathf.RoundToInt(c.x):X2}{Mathf.RoundToInt(c.y):X2}{Mathf.RoundToInt(c.z):X2}";
                foreach (int t in groups[g].tris) index[t] = (byte)g;
            }
            return (colors, index);
        }

        /// <summary>A material's main color (glTFast, URP and older shaders name it differently).</summary>
        public static Color MaterialColor(Material m)
        {
            if (m == null) return Color.white;
            foreach (var name in new[] { "baseColorFactor", "_BaseColor", "_Color" })
                if (m.HasProperty(name)) return m.GetColor(name);
            return Color.white;
        }

        /// <summary>A material's color picture (texture), or null.</summary>
        public static Texture MaterialTexture(Material m)
        {
            if (m == null) return null;
            foreach (var name in new[] { "baseColorTexture", "_BaseMap", "_MainTex" })
                if (m.HasProperty(name) && m.GetTexture(name) is Texture t && t != null) return t;
            return null;
        }

        /// <summary>
        /// The pixels of a texture, made small (at most 256 x 256) and readable. Null when it can't be read
        /// (no graphics card in use, e.g. automatic tests).
        /// </summary>
        public static Color32[] Pixels(Texture texture, out int width, out int height)
        {
            width = height = 0;
            if (texture is Texture2D t2 && t2.isReadable && t2.width <= 1024 && t2.height <= 1024)
            {
                width = t2.width;
                height = t2.height;
                return t2.GetPixels32();
            }
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return null;
            width = Mathf.Clamp(texture.width, 1, 256);
            height = Mathf.Clamp(texture.height, 1, 256);
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            Graphics.Blit(texture, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            var pixels = copy.GetPixels32();
            Object.Destroy(copy);
            return pixels;
        }

        /// <summary>The texture's color at a texture spot (it repeats outside 0..1).</summary>
        public static Color Sample(Color32[] pixels, int width, int height, Vector2 uv)
        {
            float u = uv.x - Mathf.Floor(uv.x), v = uv.y - Mathf.Floor(uv.y);
            int x = Mathf.Clamp((int)(u * width), 0, width - 1), y = Mathf.Clamp((int)(v * height), 0, height - 1);
            return pixels[y * width + x];
        }
    }
}
