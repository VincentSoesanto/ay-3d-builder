using System.IO;
using Builder.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Builder.EditorTools
{
    /// <summary>
    /// Debug tool: renders every shape icon the same way the app does and saves them as PNG files
    /// (menu Builder > Debug > Save Shape Icons, or -executeMethod Builder.EditorTools.IconPreview.SaveIcons).
    /// </summary>
    public static class IconPreview
    {
        [MenuItem("Builder/Debug/Save Shape Icons")]
        public static void SaveIcons()
        {
            EditorSceneManager.OpenScene(BuilderSceneSetup.ScenePath);
            var icons = Object.FindFirstObjectByType<ShapeIcons>();
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Icons"));

            icons.SaveAll(folder);
            Debug.Log("[Builder] Icons saved to " + folder);
        }

        /// <summary>Debug: a picture of a few cut pieces (Logs/Icons/cuts.png), to check cuts look right.</summary>
        [MenuItem("Builder/Debug/Save Cut Preview")]
        public static void SaveCutPreview()
        {
            EditorSceneManager.OpenScene(BuilderSceneSetup.ScenePath);
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Piece.mat");
            var root = new GameObject("Cut Preview");
            root.transform.position = new Vector3(0f, -3000f, 0f);
            void Add(Builder.Core.Piece p, Vector3 at)
            {
                var go = new GameObject(p.shape);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = at;
                go.AddComponent<MeshFilter>().sharedMesh = Builder.Shapes.ShapeRegistry.GetPieceMesh(p);
                go.AddComponent<MeshRenderer>().sharedMaterial = material;
            }
            Builder.Core.Cut C(string shape, Vector3 pos, Vector3 size, Vector3 euler) => new Builder.Core.Cut
            {
                shape = shape, p = new Builder.Core.ShapeParams { width = size.x, height = size.y, length = size.z, segments = 12 },
                position = new Builder.Core.Vec3(pos.x, pos.y, pos.z), rotation = Builder.View.Conversions.ToDoc(Quaternion.Euler(euler)),
            };
            Add(new Builder.Core.Piece { shape = "box", p = new Builder.Core.ShapeParams(),
                cuts = new System.Collections.Generic.List<Builder.Core.Cut> { C("sphere", new Vector3(0.5f, 0.5f, -0.5f), Vector3.one, Vector3.zero) } }, new Vector3(-1.4f, 0f, 0f));
            Add(new Builder.Core.Piece { shape = "box", p = new Builder.Core.ShapeParams { width = 1f, height = 1f, length = 1.4f },
                cuts = new System.Collections.Generic.List<Builder.Core.Cut> { C("cylinder", Vector3.zero, new Vector3(0.5f, 2f, 0.5f), new Vector3(90f, 0f, 0f)) } }, Vector3.zero);
            Add(new Builder.Core.Piece { shape = "cylinder", p = new Builder.Core.ShapeParams { segments = 12 }, mirrored = true,
                cuts = new System.Collections.Generic.List<Builder.Core.Cut> { C("wedge", new Vector3(0.2f, 0.3f, -0.2f), new Vector3(1.2f, 0.8f, 0.8f), new Vector3(0f, 30f, 0f)) } }, new Vector3(1.4f, 0f, 0f));

            var camGo = new GameObject("Preview Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.2f, 0.22f, 0.25f);
            cam.fieldOfView = 30f;
            camGo.transform.rotation = Quaternion.Euler(25f, 20f, 0f);
            camGo.transform.position = root.transform.position - camGo.transform.forward * 8f;
            var rt = new RenderTexture(768, 384, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            RenderTexture.active = null;
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Icons"));
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "cuts.png"), tex.EncodeToPNG());
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(camGo);
            Debug.Log("[Builder] Cut preview saved");
        }
    }
}
