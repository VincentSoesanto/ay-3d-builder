using System.Collections.Generic;
using System.Linq;
using Builder.Core;
using Builder.IO;
using Builder.Shapes;
using Builder.View;
using UnityEngine;

namespace Builder.UI
{
    /// <summary>
    /// Makes a small picture of each shape for the hotbar and inventory, by photographing the shape
    /// with a hidden camera far below the world. Each picture is made once, when first needed.
    /// Blueprints use the picture saved with them (made by <see cref="RenderPieces"/> when they were saved).
    /// </summary>
    public class ShapeIcons : MonoBehaviour
    {
        [SerializeField] Material iconMaterial;
        [SerializeField] int size = 128;
        [SerializeField] BlueprintLibrary library;

        static readonly Vector3 StudioSpot = new Vector3(0f, -5000f, 0f);   // far away from everything else

        readonly Dictionary<string, Texture> icons = new Dictionary<string, Texture>();
        Camera studioCamera;

        void Start()
        {
            // Developer check: "AY3DBuilder.exe -dumpIcons <folder>" saves every icon as a PNG, then quits.
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-dumpIcons");
            if (i >= 0 && i + 1 < args.Length)
            {
                SaveAll(args[i + 1]);
                Application.Quit();
            }
        }

        /// <summary>Saves every shape's icon as a PNG file into <paramref name="folder"/>.</summary>
        public void SaveAll(string folder)
        {
            System.IO.Directory.CreateDirectory(folder);
            foreach (var shape in ShapeRegistry.All.Concat(ShapeRegistry.Points).Concat(ShapeRegistry.Reference))
            {
                if (!(Get(shape.Id) is RenderTexture rt)) continue;
                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = previous;
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, shape.Id + ".png"), tex.EncodeToPNG());
                if (Application.isPlaying) Destroy(tex); else DestroyImmediate(tex);
            }
            // The built-in library parts too (file name: tab_category_name).
            foreach (var part in PartLibrary.Items)
            {
                var tex = RenderPieces(part.blueprint.content.pieces);
                if (tex == null) continue;
                string name = $"{part.tab}_{part.category}_{part.name}".Replace(' ', '-').Replace('&', 'n');
                foreach (char bad in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(bad, '-');
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, name + ".png"), tex.EncodeToPNG());
                if (Application.isPlaying) Destroy(tex); else DestroyImmediate(tex);
            }
        }

        /// <summary>The name to show for a hotbar item: the shape's name, or the blueprint's name.</summary>
        public string NameOf(string item)
        {
            if (BlueprintLibrary.IsBlueprintItem(item)) return library.FromItem(item)?.name ?? "(deleted blueprint)";
            if (PartLibrary.IsLibraryItem(item)) return PartLibrary.FromItem(item)?.name ?? "(missing part)";
            return ShapeRegistry.Get(item)?.DisplayName ?? "";
        }

        public Texture Get(string shapeId)
        {
            if (shapeId == null) return null;
            if (BlueprintLibrary.IsBlueprintItem(shapeId)) return library.Picture(BlueprintLibrary.IdOf(shapeId));
            if (icons.TryGetValue(shapeId, out var icon)) return icon;
            // No graphics card in use (e.g. automatic tests in "no graphics" mode): no pictures, just names.
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return null;
            if (PartLibrary.IsLibraryItem(shapeId))
            {
                var part = PartLibrary.FromItem(shapeId);
                if (part == null) return null;
                icon = RenderPieces(part.blueprint.content.pieces);
                icons[shapeId] = icon;
                return icon;
            }
            if (Builder.IO.ModelLibrary.ColoredBlueprint(shapeId) is Blueprint colored)   // an imported model in its own colors
            {
                icon = RenderPieces(colored.content.pieces);
                icons[shapeId] = icon;
                return icon;
            }
            var shape = ShapeRegistry.Get(shapeId);
            if (shape == null) return null;
            icon = Render(shape);
            icons[shapeId] = icon;
            return icon;
        }

        Texture Render(IShape shape)
        {
            var piece = new Piece { shape = shape.Id, p = shape.CreateDefaults(), color = ShapeRegistry.StartColor(shape.Id, "#C7D1E0") };
            var texture = RenderModels(new[] { piece }, size);
            texture.name = "Icon " + shape.Id;
            return texture;
        }

        /// <summary>
        /// A picture of these pieces in their own colors (for a blueprint), as a readable texture that can be saved
        /// as PNG. Returns null when there is no graphics card in use.
        /// </summary>
        public Texture2D RenderPieces(IEnumerable<Piece> pieces, int pixels = 256)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return null;
            var rt = RenderModels(pieces, pixels);
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(pixels, pixels, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, pixels, pixels), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            rt.Release();
            return tex;
        }

        /// <summary>Photographs the pieces (placed around the studio spot) into a new render texture.</summary>
        RenderTexture RenderModels(IEnumerable<Piece> pieces, int pixels)
        {
            if (studioCamera == null) studioCamera = MakeCamera();

            var made = new List<Object>();
            Bounds? box = null;
            // Turned around, so the picture shows the FRONT (+Z, Unity's forward) on the side the sun lights.
            var turn = Quaternion.Euler(0f, 180f, 0f);
            foreach (var p in pieces)
            {
                var model = new GameObject("Icon Model");
                var mesh = PaintedMesh.Get(p, out var colors);
                model.transform.SetPositionAndRotation(StudioSpot + turn * p.position.ToUnity(), turn * p.rotation.ToUnity());
                model.AddComponent<MeshFilter>().sharedMesh = mesh;
                var materials = new Material[colors.Length];
                for (int i = 0; i < colors.Length; i++)
                {
                    materials[i] = ColorFinish.MaterialFor(iconMaterial, colors[i]);
                    made.Add(materials[i]);
                }
                model.AddComponent<MeshRenderer>().sharedMaterials = materials;
                made.Add(model);
                var b = model.GetComponent<MeshRenderer>().bounds;
                if (box == null) box = b; else { var t = box.Value; t.Encapsulate(b); box = t; }
            }

            // Look from the front-left, a bit from above, and back off until everything fits.
            var all = box ?? new Bounds(StudioSpot, Vector3.one);
            var rotation = Quaternion.Euler(25f, 35f, 0f);
            float radius = Mathf.Max(all.extents.magnitude, 0.01f);
            float distance = radius / Mathf.Sin(studioCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.02f;
            studioCamera.farClipPlane = distance + radius * 2f + 1f;
            studioCamera.transform.SetPositionAndRotation(all.center - rotation * Vector3.forward * distance, rotation);

            var texture = new RenderTexture(pixels, pixels, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            studioCamera.targetTexture = texture;
            studioCamera.Render();
            studioCamera.targetTexture = null;

            // Hide them right away: Destroy only happens at the end of the frame, and the next picture may be taken
            // in this same frame (otherwise pictures would show models on top of each other).
            foreach (var o in made)
            {
                if (o is GameObject g) g.SetActive(false);
                // (Outside play mode, e.g. the editor's "save icons" tool, objects must be removed with DestroyImmediate.)
                if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
            }
            return texture;
        }

        Camera MakeCamera()
        {
            var go = new GameObject("Icon Camera");
            go.transform.SetParent(transform, false);
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;                          // only renders when we ask
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;                      // sees only the studio, not the world (set per picture)
            return cam;
        }
    }
}
