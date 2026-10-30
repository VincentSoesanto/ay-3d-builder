using System.Collections;
using System.Linq;
using Builder.Core;
using Builder.Tools;
using UnityEngine;

namespace Builder.UI
{
    /// <summary>
    /// Developer check (not for normal use): "AY3DBuilder.exe -uiShot &lt;file.png&gt; &lt;bones|colors|inventory|touch&gt;" builds a
    /// small test mech, opens that screen, saves a 1920 x 1080 picture of everything (world + menus) and quits.
    /// Used for automated UI screenshot checks.
    /// </summary>
    public class DevShots : MonoBehaviour
    {
        IEnumerator Start()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-uiShot");
            if (i < 0 || i + 1 >= args.Length) yield break;
            string file = args[i + 1];
            string what = i + 2 < args.Length ? args[i + 2] : "bones";
            // Never touch the real projects: everything (incl. the save when quitting) goes to a temporary folder.
            Builder.IO.ProjectStore.RootOverride = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AY3DBuilderShots");
            yield return null;

            var session = FindFirstObjectByType<BuilderSession>();
            session.Use(BuilderSession.NewDocument("UI check"));
            var pieces = new[]
            {
                Box("body", 0f, 4.2f, 1.6f, 1.6f, 1.2f, "#7A8B99"), Box("head", 0f, 5.4f, 0.7f, 0.6f, 0.7f, "#C0392B"),
                Box("armL", 1.2f, 4.2f, 0.4f, 1.6f, 0.4f, "#556B2F"), Box("armR", -1.2f, 4.2f, 0.4f, 1.6f, 0.4f, "#556B2F"),
                Box("legL", 0.45f, 1.7f, 0.5f, 3.4f, 0.5f, "#34495E"), Box("legR", -0.45f, 1.7f, 0.5f, 3.4f, 0.5f, "#34495E"),
            };
            session.Execute(new AddPiecesCommand(pieces));
            var tools = FindFirstObjectByType<ToolManager>();

            if (what == "colors")
            {
                tools.SetTool(ToolKind.Paint);
                yield return null;
                FindFirstObjectByType<ColorWindow>().Open();
            }
            else if (what == "inventory")
            {
                var inventory = FindFirstObjectByType<InventoryScreen>();
                inventory.Open();
                inventory.SetTab(2);
            }
            else if (what == "menu")
            {
                FindFirstObjectByType<MenuScreen>().Open();
            }
            else if (what.StartsWith("frame"))   // "frame" = the default preset, "frame2" = preset number 2 (from 0)
            {
                var presets = MechFrame.Presets.ToList();
                string preset = int.TryParse(what.Substring(5), out int n) && n >= 0 && n < presets.Count ? presets[n] : MechFrame.Default;
                // A Biped skeleton with its frame parts, without the boxes.
                session.ReplaceSkeleton(RigTools.Fitted("Biped", RigTools.ModelBounds(session.Document.content).Value), "Biped", autoBind: false);
                session.Remove(pieces.Select(p => p.id).ToList(), "boxes");
                session.ReplaceFrame(MechFrame.Build(session.Document.content, preset), "Frame parts");
                tools.SetTool(ToolKind.Select);   // no build ghost in the picture
                FindFirstObjectByType<Builder.CameraControl.CameraRig>().LookFrom(new Vector3(2.4f, 4.2f, 10f), new Vector3(0f, 3.1f, 6f));   // from the front
            }
            else if (what == "touch")
            {
                tools.SetTool(ToolKind.Build);   // start the exe with -touch too, so the touch controls show
            }
            else
            {
                session.ReplaceSkeleton(RigTools.Fitted("Biped", RigTools.ModelBounds(session.Document.content).Value), "Biped", autoBind: true);
                var panel = FindFirstObjectByType<BonesPanel>();
                panel.Open();
                yield return null;
                panel.Choose(session.Document.content.bones.First(b => b.name == "UpperArm.L").id);
                if (what == "bonelist") panel.ShowFrameList(true);   // the Frame preset list open
                FindFirstObjectByType<Builder.CameraControl.CameraRig>().LookFrom(new Vector3(-3f, 5.5f, 1f), new Vector3(0.5f, 4f, 6f));
            }
            for (int f = 0; f < 30; f++) yield return null;   // pictures, layout

            var cam = Camera.main;
            var canvas = FindObjectsByType<Canvas>(FindObjectsSortMode.None).First(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay);
            var rt = new RenderTexture(1920, 1080, 24);
            cam.targetTexture = rt;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = cam.nearClipPlane + 0.1f;
            yield return null;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
            Application.Quit();
        }

        static Piece Box(string id, float x, float y, float w, float h, float l, string color) => new Piece
        {
            id = id, shape = "box", color = color, position = new Vec3(x, y, 6f),
            p = new ShapeParams { width = w, height = h, length = l },
        };
    }
}
