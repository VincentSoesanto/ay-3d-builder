using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Builder.IO
{
    /// <summary>
    /// Reads the shape out of a Wavefront .obj file (a simple text 3D format most 3D apps can save): the corner
    /// points ("v x y z") and faces ("f 1 2 3 4"). Faces with more than 3 corners are split into triangles.
    /// OBJ files use the other "handedness" than Unity, so X is flipped (like Unity's own OBJ importer).
    /// Colors: each face gets the "Kd" color of its material ("usemtl") from the .mtl file. Textures and normals are
    /// ignored (flat shading).
    /// </summary>
    public static class ObjReader
    {
        public static (List<Vector3> vertices, List<int> triangles) Read(string text)
        {
            var (vertices, triangles, _) = ReadColored(text, null);
            return (vertices, triangles);
        }

        /// <summary>
        /// Same, plus a color per triangle (null when the file has no material colors). <paramref name="readFile"/>
        /// reads a file next to the .obj (the .mtl named by "mtllib"); null = colors are not read.
        /// </summary>
        public static (List<Vector3> vertices, List<int> triangles, List<Color32> colors) ReadColored(string text, Func<string, string> readFile)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var corners = new List<int>();
            var materials = new Dictionary<string, Color32>();
            var colors = new List<Color32>();
            Color32 current = new Color32(176, 176, 176, 255);
            bool anyColor = false;

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("mtllib ", StringComparison.Ordinal) && readFile != null)
                {
                    try { ReadMtl(readFile(line.Substring(7).Trim()), materials); }
                    catch (Exception) { }   // no .mtl file next to it: one color
                }
                else if (line.StartsWith("usemtl ", StringComparison.Ordinal))
                {
                    if (materials.TryGetValue(line.Substring(7).Trim(), out var c)) { current = c; anyColor = true; }
                }
                else if (line.StartsWith("v ", StringComparison.Ordinal))
                {
                    var p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    vertices.Add(new Vector3(-Num(p[1]), Num(p[2]), Num(p[3])));
                }
                else if (line.StartsWith("f ", StringComparison.Ordinal))
                {
                    corners.Clear();
                    var p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 1; i < p.Length; i++)
                    {
                        // "7", "7/2" or "7/2/5": the first number is the corner; negative = counted from the end.
                        int index = int.Parse(p[i].Split('/')[0], CultureInfo.InvariantCulture);
                        corners.Add(index < 0 ? vertices.Count + index : index - 1);
                    }
                    // Fan of triangles; the order is reversed because X was flipped.
                    for (int i = 1; i + 1 < corners.Count; i++)
                    {
                        triangles.Add(corners[0]);
                        triangles.Add(corners[i + 1]);
                        triangles.Add(corners[i]);
                        colors.Add(current);
                    }
                }
            }
            return (vertices, triangles, anyColor ? colors : null);
        }

        /// <summary>Material name -> its "Kd" (diffuse) color.</summary>
        static void ReadMtl(string text, Dictionary<string, Color32> materials)
        {
            string name = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("newmtl ", StringComparison.Ordinal)) name = line.Substring(7).Trim();
                else if (line.StartsWith("Kd ", StringComparison.Ordinal) && name != null)
                {
                    var p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length >= 4)
                        materials[name] = new Color(Num(p[1]), Num(p[2]), Num(p[3]));
                }
            }
        }

        static float Num(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
