using System.IO;
using Builder.Input;
using Builder.UI;
using UnityEngine;

namespace Builder.IO
{
    /// <summary>Ctrl+E (or the Export button in the Groups panel): saves the model as a .glb file and says where.</summary>
    public class ExportController : MonoBehaviour
    {
        [SerializeField] BuilderSession session;
        [SerializeField] BuilderInput input;
        [SerializeField] Material pieceMaterial;   // the URP Lit material the export copies per color

        public bool Busy { get; private set; }

        /// <summary>Full path of the last file written (null until the first export).</summary>
        public string LastFile { get; private set; }

        async void Start()
        {
            // Developer check: "AY3DBuilder.exe -exportTest <file.glb>" exports a small sample model and quits.
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-exportTest");
            if (i < 0 || i + 1 >= args.Length) return;
            var doc = Core.ProjectDocument.CreateNew("Export Test", 0);
            doc.content.groups.Add(new Core.Group { id = "arm", name = "LeftArm", pivot = new Core.Vec3(1f, 2f, 0f) });
            doc.content.pieces.Add(new Core.Piece { id = "a", shape = "cylinder", p = new Core.ShapeParams { segments = 12 }, position = new Core.Vec3(1.5f, 1.5f, 0f), color = "#C0392B", groupId = "arm" });
            doc.content.pieces.Add(new Core.Piece { id = "b", shape = "wedge", p = new Core.ShapeParams(), position = new Core.Vec3(-1f, 0.5f, 0f), color = "#2980B9" });
            bool ok = await GlbExporter.ExportAsync(doc, pieceMaterial, args[i + 1]);
            Debug.Log("[Builder] exportTest " + (ok ? "OK" : "FAILED") + ": " + args[i + 1]);
            Application.Quit(ok ? 0 : 1);
        }

        void Update()
        {
            if (input.Pressed(input.Export)) Export();
        }

        /// <summary>
        /// Exports a .obj + .mtl and a .zip of both (for Mixamo and other tools that don't read .glb).
        /// Same folder and name as the .glb export.
        /// </summary>
        public void ExportObj()
        {
            if (session.Document.content.pieces.Count == 0)
            {
                Toast.Show("Nothing to export yet - place some pieces first");
                return;
            }
            try
            {
                string name = Path.GetFileNameWithoutExtension(GlbExporter.DefaultFileName(session.Document));
                LastFile = ObjExporter.Export(session.Document, GlbExporter.DefaultFolder, name);
                Toast.Show("Exported .obj (+ .zip for Mixamo): " + LastFile);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                Toast.Show("Export failed: " + e.Message);
            }
        }

        public async void Export()
        {
            if (Busy) return;
            if (session.Document.content.pieces.Count == 0)
            {
                Toast.Show("Nothing to export yet - place some pieces first");
                return;
            }

            Busy = true;
            Toast.Show("Exporting...");
            string path = Path.Combine(GlbExporter.DefaultFolder, GlbExporter.DefaultFileName(session.Document));
            bool ok = false;
            try
            {
                ok = await GlbExporter.ExportAsync(session.Document, pieceMaterial, path);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
            Busy = false;

            if (ok)
            {
                LastFile = path;
                Toast.Show("Exported: " + path);
            }
            else Toast.Show("Export failed (see the log)");
        }
    }
}
