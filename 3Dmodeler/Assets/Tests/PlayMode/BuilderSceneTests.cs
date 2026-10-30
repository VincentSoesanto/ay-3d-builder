using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Builder.Core;
using Builder.Shapes;
using Builder.Tools;
using Builder.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Builder.Tests
{
    /// <summary>
    /// Runs the real Builder scene for a few frames and drives it from code.
    /// Any error or exception logged while it runs makes the test fail.
    /// </summary>
    public class BuilderSceneTests
    {
        string temp;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            // Projects go to an empty temporary folder: tests never see or change the real projects,
            // and every test starts with a new, empty project.
            temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AY3DBuilderPlayTests_" + System.Guid.NewGuid().ToString("N"));
            Builder.IO.ProjectStore.RootOverride = temp;
            SceneManager.LoadScene("Builder");
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Builder.IO.ProjectStore.RootOverride = null;
            if (System.IO.Directory.Exists(temp)) System.IO.Directory.Delete(temp, true);
            yield return null;
        }

        static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();

        static void PlaceNow(BuildTool tool) =>
            typeof(BuildTool).GetMethod("PlaceStamp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(tool, null);

        [UnityTest]
        public IEnumerator Precision_Build_MovesGhostByExactOffset()
        {
            var session = Find<BuilderSession>();
            var tool = Find<BuildTool>();
            var precision = Find<PrecisionMode>();
            for (int i = 0; i < 3; i++) yield return null;   // the camera looks at the floor: the ghost has a spot

            precision.Set(true);
            yield return null;
            PlaceNow(tool);                                  // at the start spot
            precision.Nudge(new Vector3(1f, 0f, 0.25f));
            yield return null;
            PlaceNow(tool);                                  // 1 m right, 25 cm back

            var pieces = session.Document.content.pieces;
            Assert.AreEqual(2, pieces.Count);
            var d = pieces[1].position.ToUnity() - pieces[0].position.ToUnity();
            Assert.That(Vector3.Distance(d, new Vector3(1f, 0f, 0.25f)), Is.LessThan(1e-4f), $"moved by {d}");
            precision.Set(false);
        }

        [UnityTest]
        public IEnumerator Precision_Select_MovesSelection_OneUndoStep()
        {
            var session = Find<BuilderSession>();
            session.Execute(new AddPiecesCommand(new[] { new Piece { id = "a", shape = "box", p = new ShapeParams(), position = new Vec3(0f, 0.5f, 0f) } }));
            Find<ToolManager>().SetTool(ToolKind.Select);
            Find<Selection>().Set("a");
            yield return null;

            var precision = Find<PrecisionMode>();
            precision.Set(true);
            precision.Nudge(new Vector3(0f, 0.5f, 0f));
            yield return null;
            precision.Nudge(new Vector3(0.1f, 0f, 0f));
            yield return null;
            precision.Set(false);

            var moved = session.Document.content.FindPiece("a").position.ToUnity();
            Assert.That(Vector3.Distance(moved, new Vector3(0.1f, 1f, 0f)), Is.LessThan(1e-4f), $"at {moved}");
            session.History.Undo();
            Assert.That(session.Document.content.FindPiece("a").position.y, Is.EqualTo(0.5f).Within(1e-4f), "one undo = whole precise move");
        }

        [UnityTest]
        public IEnumerator Cut_ChangesTheMesh_AndOneUndoMakesItWhole()
        {
            var session = Find<BuilderSession>();
            var view = Find<DocumentView>();
            session.Execute(new AddPiecesCommand(new[]
            {
                new Piece { id = "a", shape = "box", p = new ShapeParams(), position = new Vec3(0f, 0.5f, 0f) },
                new Piece { id = "small", shape = "box", p = new ShapeParams { width = 0.2f, height = 0.2f, length = 0.2f }, position = new Vec3(0.6f, 0.5f, 0f) },
            }));
            yield return null;

            // A 1 m box cutter over the right half of "a", swallowing "small" completely.
            var cutter = new EraseTool.Cutter { shape = "box", p = new ShapeParams(), position = new Vector3(0.5f, 0.5f, 0f), rotation = Quaternion.identity };
            var piece = session.Document.content.FindPiece("a");
            session.CutPieces(new Dictionary<string, Cut> { { "a", BuildTool.ToCut(piece, cutter) } }, new[] { "small" }, "Cut 2 pieces");
            yield return null;

            var mesh = view.Find("a").GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.bounds.max.x, Is.EqualTo(0f).Within(1e-3f), "right half cut away");
            Assert.IsNull(view.Find("small"), "piece fully inside is removed");

            session.History.Undo();
            yield return null;
            Assert.That(view.Find("a").GetComponent<MeshFilter>().sharedMesh.bounds.max.x, Is.EqualTo(0.5f).Within(1e-3f), "whole again");
            Assert.IsNotNull(view.Find("small"), "and the small piece is back");
        }

        [UnityTest]
        public IEnumerator Blueprint_SaveFromSelection_ThenPlaceFromHotbar()
        {
            var session = Find<BuilderSession>();
            session.Execute(new AddPiecesCommand(new[]
            {
                new Piece { id = "a", shape = "box", p = new ShapeParams(), position = new Vec3(3f, 0.5f, 3f), color = "#C0392B" },
                new Piece { id = "b", shape = "cylinder", p = new ShapeParams { segments = 8 }, position = new Vec3(4f, 0.5f, 3f) },
            }));
            Find<ToolManager>().SetTool(ToolKind.Select);
            Find<Selection>().SetMany(new[] { "a", "b" });
            yield return null;

            // "Save as Blueprint" -> type a name -> Save.
            var dialog = Find<Builder.UI.BlueprintDialog>();
            dialog.Open();
            yield return null;
            Assert.IsTrue(dialog.IsOpen);
            typeof(Builder.UI.BlueprintDialog).GetMethod("Save", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(dialog, null);
            yield return null;
            Assert.IsFalse(dialog.IsOpen);

            var library = Find<Builder.IO.BlueprintLibrary>();
            Assert.AreEqual(1, library.All.Count, "blueprint is in the library");
            var bp = library.All[0];
            Assert.AreEqual(2, bp.content.pieces.Count);
            Assert.That(bp.content.pieces.Min(p => p.position.y), Is.EqualTo(0.5f).Within(1e-4f), "measured from the bottom middle");

            // It shows up in the inventory, and can go on the hotbar and be placed like one piece.
            Find<Builder.UI.InventoryScreen>().SetTab(Builder.UI.InventoryScreen.AllTabs.Length - 1);   // "My stuff"
            yield return null;
            var buttons = Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.IsTrue(buttons.Any(b => b.name == "bp:" + bp.id), "inventory has a button for it (inventory is closed, so look at hidden ones too)");
            Find<ToolManager>().SetTool(ToolKind.Build);
            var hotbar = Find<Hotbar>();
            hotbar.Assign(hotbar.Selected, Builder.IO.BlueprintLibrary.ItemOf(bp));
            for (int i = 0; i < 2; i++) yield return null;
            var tool = Find<BuildTool>();
            Assert.AreEqual(bp, tool.CurrentBlueprint);
            Assert.AreEqual(2, tool.StampCount, "ghost = the whole blueprint");

            PlaceNow(tool);
            Assert.AreEqual(4, session.Document.content.pieces.Count, "2 new pieces in one click");
            session.History.Undo();
            Assert.AreEqual(2, session.Document.content.pieces.Count, "one undo removes the whole placed blueprint");
        }

        static void SaveBlueprint(string name, params string[] ids)
        {
            Find<ToolManager>().SetTool(ToolKind.Select);
            Find<Selection>().SetMany(ids);
            var dialog = Find<Builder.UI.BlueprintDialog>();
            dialog.Open();
            var nameBox = (TMPro.TMP_InputField)typeof(Builder.UI.BlueprintDialog)
                .GetField("nameBox", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(dialog);
            nameBox.text = name;
            typeof(Builder.UI.BlueprintDialog).GetMethod("Save", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(dialog, null);
            Find<Selection>().SetMany(new string[0]);
        }

        /// <summary>Places the blueprint once; returns the new pieces' ids.</summary>
        static List<string> PlaceBlueprint(Builder.IO.BlueprintLibrary library, string name)
        {
            var session = Find<BuilderSession>();
            Find<ToolManager>().SetTool(ToolKind.Build);
            var hotbar = Find<Hotbar>();
            hotbar.Assign(hotbar.Selected, Builder.IO.BlueprintLibrary.ItemOf(library.All.First(b => b.name == name)));
            var tool = Find<BuildTool>();
            tool.SetShape(Builder.IO.BlueprintLibrary.ItemOf(library.All.First(b => b.name == name)));
            typeof(BuildTool).GetMethod("BuildStamp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(tool, null);   // the ghost is rebuilt every frame; do it now
            var old = new HashSet<string>(session.Document.content.pieces.Select(p => p.id));
            PlaceNow(tool);
            return session.Document.content.pieces.Where(p => !old.Contains(p.id)).Select(p => p.id).ToList();
        }

        [UnityTest]
        public IEnumerator Blueprints_InsideBlueprints_UpdateWhenSavedAgain_EditedCopiesDont()
        {
            var session = Find<BuilderSession>();
            var library = Find<Builder.IO.BlueprintLibrary>();
            float Width(string id) => session.Document.content.FindPiece(id).p.width;
            session.Execute(new AddPiecesCommand(new[]
            {
                new Piece { id = "a", shape = "box", p = new ShapeParams { width = 0.2f }, position = new Vec3(5f, 0.5f, 0f) },
            }));
            for (int i = 0; i < 3; i++) yield return null;

            // Finger, then a Hand made of two Fingers.
            SaveBlueprint("Finger", "a");
            Assert.IsNotNull(session.Document.content.FindPiece("a").link, "the saved pieces become a linked copy");
            var f1 = PlaceBlueprint(library, "Finger");
            var f2 = PlaceBlueprint(library, "Finger");
            Assert.AreEqual(1, f1.Count);
            SaveBlueprint("Hand", f1[0], f2[0]);
            var hand = library.All.First(b => b.name == "Hand");
            Assert.AreEqual(2, hand.content.pieces.Count(p => p.link?.blueprintId == library.All.First(b => b.name == "Finger").id),
                "the Hand keeps its two Fingers linked");
            var h1 = PlaceBlueprint(library, "Hand");
            Assert.AreEqual(2, h1.Count);

            // A Finger copy that gets painted is edited on its own: it unlinks.
            var f3 = PlaceBlueprint(library, "Finger");
            var painted = session.Document.content.FindPiece(f3[0]).Clone();
            painted.color = "#FF0000";
            session.Modify(new[] { session.Document.content.FindPiece(f3[0]) }, new[] { painted }, "Paint");
            Assert.IsNull(session.Document.content.FindPiece(f3[0]).link, "edited copy is unlinked");

            // Moving a whole copy keeps it linked.
            var moved = h1.Select(id => { var c = session.Document.content.FindPiece(id).Clone(); c.position = new Vec3(c.position.x + 2f, c.position.y, c.position.z); return c; }).ToList();
            session.Modify(h1.Select(id => session.Document.content.FindPiece(id)).ToList(), moved, "Move");
            Assert.IsNotNull(session.Document.content.FindPiece(h1[0]).link, "moved whole: still linked");
            float h1x = session.Document.content.FindPiece(h1[0]).position.x;

            // Make the finger wider and save it again under the same name.
            var wide = session.Document.content.FindPiece("a").Clone();
            wide.p.width = 0.5f;
            session.Modify(new[] { session.Document.content.FindPiece("a") }, new[] { wide }, "Edit width");
            SaveBlueprint("Finger", "a");
            for (int i = 0; i < 3; i++) yield return null;

            Assert.AreEqual(2, library.All.Count, "same name replaced it");
            hand = library.All.First(b => b.name == "Hand");
            Assert.AreEqual(2, hand.version, "the Hand blueprint was updated too");
            Assert.That(hand.content.pieces.All(p => Mathf.Approximately(p.p.width, 0.5f)), "Fingers inside the Hand blueprint are wider");
            Assert.AreEqual(0.5f, Width(f1[0]), 1e-4f, "first hand (the pieces the Hand was saved from)");
            Assert.AreEqual(0.5f, Width(f2[0]), 1e-4f);
            Assert.AreEqual(0.5f, Width(h1[0]), 1e-4f, "placed hand");
            Assert.AreEqual(0.5f, Width(h1[1]), 1e-4f);
            Assert.AreEqual(h1x, session.Document.content.FindPiece(h1[0]).position.x, 1e-3f, "stays where it was moved to");
            Assert.AreEqual(0.2f, Width(f3[0]), 1e-4f, "the edited (unlinked) copy is not changed");

            session.History.Undo();
            Assert.AreEqual(0.2f, Width(h1[0]), 1e-4f, "one undo takes the update back");
        }

        [UnityTest]
        public IEnumerator SelectTool_BoxSelect_PicksPiecesInside_CtrlAdds()
        {
            var session = Find<BuilderSession>();
            session.Execute(new AddPiecesCommand(new[]
            {
                new Piece { id = "a", shape = "box", p = new ShapeParams(), position = new Vec3(-1f, 0.5f, 0f) },
                new Piece { id = "b", shape = "box", p = new ShapeParams(), position = new Vec3(1f, 0.5f, 0f) },
            }));
            Find<ToolManager>().SetTool(ToolKind.Select);
            for (int i = 0; i < 3; i++) yield return null;

            var tool = Find<SelectTool>();
            var cam = (Camera)typeof(SelectTool).GetField("viewCamera", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(tool);
            Vector2 Screen(string id) => cam.WorldToScreenPoint(Find<DocumentView>().Find(id).GetComponent<Renderer>().bounds.center);
            var boxSelect = typeof(SelectTool).GetMethod("BoxSelect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var selection = Find<Selection>();

            boxSelect.Invoke(tool, new object[] { Screen("a") - Vector2.one * 10f, Screen("a") + Vector2.one * 10f, false });
            CollectionAssert.AreEquivalent(new[] { "a" }, selection.Ids, "box around a");
            boxSelect.Invoke(tool, new object[] { Screen("b") - Vector2.one * 10f, Screen("b") + Vector2.one * 10f, true });
            CollectionAssert.AreEquivalent(new[] { "a", "b" }, selection.Ids, "Ctrl+box adds b");
            boxSelect.Invoke(tool, new object[] { Screen("b") + Vector2.one * 10f, Screen("b") - Vector2.one * 10f, false });
            CollectionAssert.AreEquivalent(new[] { "b" }, selection.Ids, "a new box replaces (dragged the other way)");
        }

        [UnityTest]
        public IEnumerator Blueprints_RenameDeleteFileRoundTrip_ModelsDelete()
        {
            var session = Find<BuilderSession>();
            var library = Find<Builder.IO.BlueprintLibrary>();
            session.Execute(new AddPiecesCommand(new[]
            {
                new Piece { id = "a", shape = "box", p = new ShapeParams(), position = new Vec3(5f, 0.5f, 0f) },
                new Piece { id = "b", shape = "box", p = new ShapeParams(), position = new Vec3(8f, 0.5f, 0f) },
            }));
            for (int i = 0; i < 3; i++) yield return null;
            SaveBlueprint("Finger", "a");
            SaveBlueprint("Hand", "a", "b");                       // Hand has a Finger inside
            var hand = library.All.First(b => b.name == "Hand");
            var fingerId = library.All.First(b => b.name == "Finger").id;

            library.Rename(hand.id, "Big hand");
            Assert.AreEqual("Big hand", library.Get(hand.id).name);

            // Save as file, delete both, import the file: both come back (the Finger was inside the file).
            string file = library.ExportFile(hand.id);
            Assert.IsTrue(System.IO.File.Exists(file));
            StringAssert.EndsWith("Big hand.aybp", file);
            library.Delete(hand.id);
            library.Delete(fingerId);
            Assert.AreEqual(0, library.All.Count);
            CollectionAssert.Contains(Builder.IO.BlueprintStore.FileCandidates(), file);
            var back = library.ImportFile(file);
            Assert.AreEqual(hand.id, back.id, "same blueprint (same id), so old copies are still its copies");
            Assert.IsNotNull(library.Get(fingerId), "the Finger inside came along");

            // Deleting a model: gone from the inventory list, but pieces made from it still work.
            var models = Find<Builder.IO.ModelLibrary>();
            System.IO.Directory.CreateDirectory(Builder.IO.ModelLibrary.ImportFolder);
            string obj = System.IO.Path.Combine(Builder.IO.ModelLibrary.ImportFolder, "longbox.obj");
            System.IO.File.WriteAllText(obj, ModelAndFlipTestsObj.Box);
            var task = models.Import(obj);
            while (!task.IsCompleted) yield return null;
            string shapeId = Builder.Shapes.ModelShape.Prefix + task.Result.id;
            models.Rename(task.Result.id, "Long box");
            Assert.AreEqual("Long box", Builder.Shapes.ShapeRegistry.Get(shapeId).DisplayName);
            models.Delete(task.Result.id);
            Assert.IsFalse(Builder.Shapes.ShapeRegistry.Models.Any(m => m.Id == shapeId), "not in the inventory");
            Assert.IsNotNull(Builder.Shapes.ShapeRegistry.Get(shapeId), "still known for pieces already placed");
        }

        [UnityTest]
        public IEnumerator Bones_PlaceChain_Bind_PoseTest_Blueprint()
        {
            var session = Find<BuilderSession>();
            var content = session.Document.content;
            session.Execute(new AddPiecesCommand(new[]
            {
                new Piece { id = "arm", shape = "box", p = new ShapeParams { width = 0.8f, height = 0.2f, length = 0.2f }, position = new Vec3(0.5f, 1f, 0f) },
            }));
            yield return null;

            // Click, click, click: two bones in a chain.
            var placer = Find<BonePlacer>();
            placer.Click(new Vector3(0f, 1f, 0f), null);
            placer.Click(new Vector3(1f, 1f, 0f), null);
            placer.Click(new Vector3(2f, 1f, 0f), null);
            placer.Cancel();
            Assert.AreEqual(2, content.bones.Count);
            var first = content.bones[0];
            var second = content.bones[1];
            Assert.AreEqual("Hips", first.name);
            Assert.AreEqual(first.id, second.parentId, "the chain makes parent -> child");
            yield return null;
            Assert.AreEqual(2, Find<BoneView>().Count, "both bones are drawn");

            // A new bone starting on the first bone's end joint becomes its child.
            var joint = Object.FindObjectsByType<BoneJointView>(FindObjectsSortMode.None).First(j => j.BoneId == first.id && j.IsTail);
            placer.Click(Vector3.zero, joint);
            placer.Click(new Vector3(1f, 2f, 0f), null);
            placer.Cancel();
            Assert.AreEqual(first.id, content.bones[2].parentId);
            session.History.Undo();
            Assert.AreEqual(2, content.bones.Count, "one undo removes a bone");

            // Bind the piece to the first bone, turn it 90 degrees in the pose test: the piece follows (picture only).
            session.BindPieces(new[] { "arm" }, first.id, "Bind");
            Find<Builder.UI.BonesPanel>().Open();   // the pose test lives in the bones panel (closing it = rest pose)
            var pose = Find<PoseTest>();
            pose.SetActive(true);
            pose.SetAngle(first.id, 2, 90f);
            yield return null;
            var armView = Find<DocumentView>().Find("arm").transform;
            Assert.That(Vector3.Distance(armView.position, new Vector3(0f, 1.5f, 0f)), Is.LessThan(1e-3f), "turned up around the joint");
            Assert.AreEqual(0.5f, content.FindPiece("arm").position.x, 1e-5f, "the model itself did not change");
            pose.SetActive(false);
            Assert.That(Vector3.Distance(armView.position, new Vector3(0.5f, 1f, 0f)), Is.LessThan(1e-3f), "back to the rest pose");

            // Delete the second bone: nothing bound to it, the first stays.
            session.DeleteBone(second.id);
            Assert.AreEqual(1, content.bones.Count);

            // A blueprint of the bound piece brings its bone along when placed.
            SaveBlueprint("Arm", "arm");
            var library = Find<Builder.IO.BlueprintLibrary>();
            Assert.AreEqual(1, library.All.First(b => b.name == "Arm").content.bones.Count, "the blueprint has the bone");
            var placed = PlaceBlueprint(library, "Arm");
            Assert.AreEqual(2, content.bones.Count, "a new bone came with it");
            var newPiece = content.FindPiece(placed[0]);
            Assert.IsNotNull(content.FindBone(newPiece.boneId), "and the new piece is bound to it");
            Assert.AreNotEqual(first.id, newPiece.boneId);
        }

        [UnityTest]
        public IEnumerator Bones_ExportedGlb_HasTheBoneTree_WhenLoaded()
        {
            var doc = ProjectDocument.CreateNew("rigged", 0);
            doc.content.bones.Add(new Bone { id = "h", name = "Hips", head = new Vec3(0f, 1f, 0f), tail = new Vec3(0f, 1.3f, 0f) });
            doc.content.bones.Add(new Bone { id = "a", name = "UpperArm.L", parentId = "h", head = new Vec3(0.3f, 1.3f, 0f), tail = new Vec3(0.8f, 1.3f, 0f) });
            doc.content.pieces.Add(new Piece { id = "p", shape = "box", p = new ShapeParams(), position = new Vec3(0.55f, 1.3f, 0f), boneId = "a" });
            string path = System.IO.Path.Combine(temp, "rigged.glb");
            var export = Builder.IO.GlbExporter.ExportAsync(doc, new Material(Shader.Find("Universal Render Pipeline/Lit")), path);
            while (!export.IsCompleted) yield return null;
            Assert.IsTrue(export.Result, "written");

            // Load it the way a Unity game would (glTFast).
            var gltf = new GLTFast.GltfImport();
            var load = gltf.LoadFile(path);
            while (!load.IsCompleted) yield return null;
            Assert.IsTrue(load.Result);
            var holder = new GameObject("Loaded");
            var make = gltf.InstantiateMainSceneAsync(holder.transform);
            while (!make.IsCompleted) yield return null;
            var arm = holder.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "UpperArm.L");
            Assert.IsNotNull(arm, "the bone is a named object");
            Assert.AreEqual("Hips", arm.parent.name, "inside its parent bone");
            Assert.IsNotNull(arm.Find("UpperArm.L Mesh")?.GetComponent<Renderer>(), "the bound mesh is inside the bone");
            Assert.That(Vector3.Distance(arm.position, new Vector3(0.3f, 1.3f, 0f)), Is.LessThan(0.01f), "at the arm's start joint");
            Object.Destroy(holder);
            gltf.Dispose();
        }

        [UnityTest]
        public IEnumerator Library_TabShowsParts_PlacingOneAddsItsColoredPieces()
        {
            var session = Find<BuilderSession>();
            var inventory = Find<Builder.UI.InventoryScreen>();
            inventory.SetTab(System.Array.IndexOf(Builder.UI.InventoryScreen.AllTabs, "Mech parts"));
            yield return null;
            var buttons = Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.IsTrue(buttons.Any(b => b.name == "lib:ay.gear"), "the Mech parts tab has the Gear");

            var hotbar = Find<Hotbar>();
            hotbar.Assign(hotbar.Selected, "lib:ay.piston");
            var tool = Find<BuildTool>();
            tool.SetShape("lib:ay.piston");
            Assert.AreEqual("Piston", tool.ItemName);
            for (int i = 0; i < 3; i++) yield return null;
            PlaceNow(tool);
            var pieces = session.Document.content.pieces;
            Assert.AreEqual(Builder.IO.PartLibrary.Get("ay.piston").blueprint.content.pieces.Count, pieces.Count, "one piece per color");
            Assert.That(pieces.Select(p => p.color).Distinct().Count(), Is.GreaterThan(1), "keeps its colors");
            Assert.IsTrue(pieces.All(p => p.link == null), "library parts are plain pieces (not linked)");
            session.History.Undo();
            Assert.AreEqual(0, pieces.Count, "one undo removes the whole part");
        }

        [UnityTest]
        public IEnumerator Paint_SprayAndFinishes_ShowAndExport()
        {
            var session = Find<BuilderSession>();
            var painted = new Piece
            {
                id = "a", shape = "box", p = new ShapeParams(), color = "#9AA3AD/metal", position = new Vec3(0f, 0.5f, 3f),
                paintCell = 0.25f, paint = new List<PaintDot> { new PaintDot(0, 1, 0, "#4FE3FF/glow") },
            };
            session.Execute(new AddPiecesCommand(new[] { painted }));
            yield return null;

            // Drawn with two materials: the metal body and the glowing square.
            var r = Find<DocumentView>().Find("a").GetComponent<MeshRenderer>();
            Assert.AreEqual(2, r.sharedMaterials.Length);
            Assert.That(r.sharedMaterials[0].GetFloat("_Metallic"), Is.GreaterThan(0.5f));
            Assert.IsTrue(r.sharedMaterials[1].IsKeywordEnabled("_EMISSION"));

            // The .glb gets both, with metal and glow (glTF: metallicFactor / emissiveFactor).
            string path = System.IO.Path.Combine(temp, "paint.glb");
            var export = Builder.IO.GlbExporter.ExportAsync(session.Document, new Material(Shader.Find("Universal Render Pipeline/Lit")), path);
            while (!export.IsCompleted) yield return null;
            Assert.IsTrue(export.Result);
            var bytes = System.IO.File.ReadAllBytes(path);
            int jsonLength = System.BitConverter.ToInt32(bytes, 12);
            string json = System.Text.Encoding.UTF8.GetString(bytes, 20, jsonLength);
            StringAssert.Contains("emissiveFactor", json, "the glow is in the file");
            StringAssert.Contains("metallicFactor", json, "the metal is in the file");

            // Colors window: add a color, change it, remove it.
            var window = Find<Builder.UI.ColorWindow>();
            var paint = Find<PaintTool>();
            int before = paint.Palette.Count;
            window.Open();
            yield return null;
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            typeof(Builder.UI.ColorWindow).GetMethod("AddNew", flags).Invoke(window, null);
            Assert.AreEqual(before + 1, paint.Palette.Count);
            Assert.AreEqual(paint.Palette.Count - 1, paint.ColorIndex, "the new color is chosen");
            typeof(Builder.UI.ColorWindow).GetMethod("Remove", flags).Invoke(window, null);
            Assert.AreEqual(before, paint.Palette.Count);
            Assert.IsTrue(session.Dirty, "the palette is saved with the project");
            window.Close();
        }

        [UnityTest]
        public IEnumerator Skeleton_Template_DragJoint_PoseByDragging()
        {
            var session = Find<BuilderSession>();
            var content = session.Document.content;
            session.Execute(new AddPiecesCommand(new[]
            {
                new Piece { id = "body", shape = "box", p = new ShapeParams { width = 1f, height = 2f, length = 1f }, position = new Vec3(0f, 3f, 5f) },
                new Piece { id = "legs", shape = "box", p = new ShapeParams { width = 1f, height = 2f, length = 1f }, position = new Vec3(0f, 1f, 5f) },
            }));
            var panel = Find<Builder.UI.BonesPanel>();
            panel.Open();
            yield return null;

            // Template: fitted to the model and every piece bound.
            typeof(Builder.UI.BonesPanel).GetMethod("ApplyTemplate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(panel, new object[] { "Biped" });
            Assert.That(content.bones.Count, Is.GreaterThan(10));
            Assert.IsTrue(content.pieces.All(p => content.BoneOf(p) != null), "every piece is bound");
            CollectionAssert.Contains(new[] { "Hips", "Spine", "Chest" }, content.FindBone(content.FindPiece("body").boneId).name, "the body goes on the torso");
            StringAssert.Contains("Leg", content.FindBone(content.FindPiece("legs").boneId).name, "the legs go on a leg bone");
            session.History.Undo();
            Assert.AreEqual(0, content.bones.Count, "one undo takes the whole skeleton (and binding) back");
            session.History.Redo();
            for (int i = 0; i < 2; i++) yield return null;

            // Click a joint ball (as the mouse would): the handles go on that joint. Its X arrow moves it; the chain stays joined.
            var gizmo = Find<BoneGizmo>();
            var cam = (Camera)typeof(BoneGizmo).GetField("viewCamera", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(gizmo);
            var upperArm = content.bones.First(b => b.name == "UpperArm.L");
            var elbow = upperArm.tail.ToUnity();
            Physics.SyncTransforms();
            Assert.IsTrue(gizmo.TryPick(cam.WorldToScreenPoint(elbow)), "the mouse is on the elbow joint");
            Assert.AreEqual(BoneGizmo.Mode.Joint, gizmo.TargetMode);
            gizmo.TestDrag(upperArm.id, BoneGizmo.Mode.Joint, true, "move", 0, 0.3f);
            Assert.AreEqual(elbow.x + 0.3f, content.FindBone(upperArm.id).tail.x, 1e-4f);
            Assert.AreEqual(elbow.y, content.FindBone(upperArm.id).tail.y, 1e-4f, "only along X");
            Assert.AreEqual(elbow.x + 0.3f, content.bones.First(b => b.name == "LowerArm.L").head.x, 1e-4f, "the lower arm moved with it");
            session.History.Undo();
            Assert.AreEqual(elbow.x, content.FindBone(upperArm.id).tail.x, 1e-4f, "one undo per drag");

            // Length cube: the upper arm gets 0.5 m longer, the hand moves along.
            var handBefore = content.bones.First(b => b.name == "Hand.L").tail.ToUnity();
            float lengthBefore = Vector3.Distance(upperArm.head.ToUnity(), upperArm.tail.ToUnity());
            gizmo.TestDrag(upperArm.id, BoneGizmo.Mode.Bone, false, "length", 0, 0.5f);
            var longer = content.FindBone(upperArm.id);
            Assert.AreEqual(lengthBefore + 0.5f, Vector3.Distance(longer.head.ToUnity(), longer.tail.ToUnity()), 1e-3f);
            Assert.That(Vector3.Distance(handBefore, content.bones.First(b => b.name == "Hand.L").tail.ToUnity()), Is.EqualTo(0.5f).Within(1e-3f));

            // Pose test: the Z ring turns the upper arm by 90 degrees (only the picture moves).
            var pose = Find<PoseTest>();
            pose.SetActive(true);
            var arm = content.FindBone(upperArm.id);
            var restDir = (arm.tail.ToUnity() - arm.head.ToUnity()).normalized;
            gizmo.TestDrag(arm.id, BoneGizmo.Mode.Bone, false, "turn", 2, 90f);
            var posedTail = pose.Matrices()[arm.id].MultiplyPoint3x4(arm.tail.ToUnity());
            var posedHead = pose.Matrices()[arm.id].MultiplyPoint3x4(arm.head.ToUnity());
            var dir = (posedTail - posedHead).normalized;
            Assert.That(Vector3.Dot(dir, Quaternion.AngleAxis(90f, Vector3.forward) * restDir), Is.GreaterThan(0.99f), "turned 90 degrees around Z");
            Assert.AreEqual(arm.tail.y, content.FindBone(arm.id).tail.y, 1e-5f, "the bone itself did not change");
            pose.SetActive(false);

            // Height box: type 7 -> the skeleton is 7 m tall.
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            typeof(Builder.UI.BonesPanel).GetMethod("SetHeight", flags).Invoke(panel, new object[] { "7" });
            var ys = content.bones.SelectMany(b => new[] { b.head.y, b.tail.y }).ToList();
            Assert.AreEqual(7f, ys.Max() - ys.Min(), 1e-3f);

            // Shift + click = the whole skeleton: its arrows move everything.
            var hips = content.bones.First(b => b.name == "Hips");
            var footBefore = content.bones.First(b => b.name == "Foot.L").tail.ToUnity();
            gizmo.TestDrag(hips.id, BoneGizmo.Mode.Skeleton, false, "move", 0, 2f);
            Assert.AreEqual(footBefore.x + 2f, content.bones.First(b => b.name == "Foot.L").tail.x, 1e-3f, "even the feet moved along");
        }

        [UnityTest]
        public IEnumerator SkeletonTree_ShowsTheBones_DragDropChangesTheParent()
        {
            var session = Find<BuilderSession>();
            var content = session.Document.content;
            session.ReplaceSkeleton(RigTools.Fitted("Biped", new Bounds(new Vector3(0f, 3f, 5f), new Vector3(2f, 6f, 2f))), "Biped", autoBind: false);
            Find<Builder.UI.BonesPanel>().Open();
            for (int i = 0; i < 3; i++) yield return null;

            var tree = Find<Builder.UI.SkeletonTree>();
            var rows = tree.GetComponentsInChildren<Builder.UI.TreeDrag>(true);
            Assert.That(Object.FindObjectsByType<Builder.UI.TreeDrag>(FindObjectsSortMode.None).Count(r => r.boneId != null),
                        Is.EqualTo(content.bones.Count), "one row per bone");

            var hand = content.bones.First(b => b.name == "Hand.L");
            var head = content.bones.First(b => b.name == "Head");
            tree.BeginDrag(hand.id);
            tree.DropOn(head.id);
            Assert.AreEqual(head.id, content.FindBone(hand.id).parentId, "Hand.L now hangs on Head");

            var hips = content.bones.First(b => b.name == "Hips");
            tree.BeginDrag(hips.id);
            tree.DropOn(head.id);
            Assert.IsNull(content.FindBone(hips.id).parentId, "refused: Head hangs on Hips (a loop)");

            session.History.Undo();
            Assert.AreEqual(content.bones.First(b => b.name == "LowerArm.L").id, content.FindBone(hand.id).parentId, "undo puts it back");
        }

        [UnityTest]
        public IEnumerator Touch_PlaceButton_Stick_AndLookDrag_Work()
        {
            var session = Find<BuilderSession>();
            var input = Find<Builder.Input.BuilderInput>();
            for (int i = 0; i < 3; i++) yield return null;   // the camera looks at the floor: the ghost has a spot

            // PLACE button: a finger down = a press (seen by every tool during the next frame), like a click.
            int before = session.Document.content.pieces.Count;
            input.TouchDown(input.Place);
            input.TouchUp(input.Place);
            yield return null;
            yield return null;
            Assert.AreEqual(before + 1, session.Document.content.pieces.Count, "tapping PLACE placed a piece");

            // Move stick: the camera moves forward.
            var cam = Find<Builder.CameraControl.CameraRig>().transform;
            var start = cam.position;
            input.TouchMove = new Vector2(0f, 1f);
            float until = Time.time + 0.5f;
            while (Time.time < until) yield return null;   // half a second of pushing the stick
            input.TouchMove = Vector2.zero;
            Assert.That(Vector3.Distance(cam.position, start), Is.GreaterThan(0.05f), "the stick moved the camera");

            // Dragging on the look area turns the view.
            var turn = cam.rotation;
            input.AddTouchLook(new Vector2(200f, 0f));
            yield return null;
            yield return null;
            Assert.That(Quaternion.Angle(turn, cam.rotation), Is.GreaterThan(1f), "dragging turned the camera");
        }

        [UnityTest]
        public IEnumerator Bones_SeveralChosen_HandlesChangeThemTogether()
        {
            var session = Find<BuilderSession>();
            var content = session.Document.content;
            session.ReplaceSkeleton(RigTools.Fitted("Biped", new Bounds(new Vector3(0f, 3f, 5f), new Vector3(2f, 6f, 2f))), "Biped", autoBind: false);
            var panel = Find<Builder.UI.BonesPanel>();
            panel.Open();
            yield return null;

            var left = content.bones.First(b => b.name == "UpperLeg.L");
            var right = content.bones.First(b => b.name == "UpperLeg.R");
            panel.Choose(left.id);
            panel.ToggleChosen(right.id);   // Ctrl + click
            CollectionAssert.AreEquivalent(new[] { left.id, right.id }, panel.ChosenBones);
            yield return null;

            float Length(Bone b) => Vector3.Distance(content.FindBone(b.id).head.ToUnity(), content.FindBone(b.id).tail.ToUnity());
            float before = Length(left);
            var footR = content.bones.First(b => b.name == "Foot.R").tail.ToUnity();
            var gizmo = Find<BoneGizmo>();
            gizmo.TestDrag(right.id, BoneGizmo.Mode.Bone, false, "length", 0, 0.4f);
            Assert.AreEqual(before + 0.4f, Length(left), 1e-3f, "the left leg got longer");
            Assert.AreEqual(before + 0.4f, Length(right), 1e-3f, "and the right one too");
            Assert.That(Vector3.Distance(footR, content.bones.First(b => b.name == "Foot.R").tail.ToUnity()), Is.EqualTo(0.4f).Within(1e-3f),
                        "the foot slid down with its leg");
            session.History.Undo();
            Assert.AreEqual(before, Length(left), 1e-3f, "one undo for both");

            var hipsY = content.bones.First(b => b.name == "Hips").head.y;
            gizmo.TestDrag(right.id, BoneGizmo.Mode.Bone, false, "move", 1, 0.5f);
            Assert.AreEqual(left.head.y + 0.5f, content.FindBone(left.id).head.y, 1e-3f, "both legs moved up");
            Assert.AreEqual(right.head.y + 0.5f, content.FindBone(right.id).head.y, 1e-3f);
            Assert.AreEqual(hipsY, content.bones.First(b => b.name == "Hips").head.y, 1e-5f, "the hips (not chosen) stayed");

            // Shift + click in the tree = a range; "All" = everything.
            panel.ChooseAll();
            Assert.AreEqual(content.bones.Count, panel.ChosenBones.Count);
        }

        [UnityTest]
        public IEnumerator FreeMode_HoldsTheItemInTheAir_BoneHandlesHideInBuildTool()
        {
            var session = Find<BuilderSession>();
            var tool = Find<BuildTool>();
            session.Document.editor.snapMode = SnapMode.Free;
            // Look straight up: nothing to aim at, so the item floats at the crosshair.
            Find<Builder.CameraControl.CameraRig>().LookFrom(new Vector3(0f, 2f, 0f), new Vector3(0f, 10f, 0.01f));
            for (int i = 0; i < 3; i++) yield return null;
            PlaceNow(tool);
            var placed = session.Document.content.pieces.Last();
            Assert.That(placed.position.y, Is.GreaterThan(5f), "placed floating, about 5 m above the camera");

            // Bone handles: shown in the Select tool with the Bones panel, gone in the Build tool.
            session.ReplaceSkeleton(RigTools.Fitted("Biped", new Bounds(new Vector3(0f, 3f, 5f), new Vector3(2f, 6f, 2f))), "Biped", autoBind: false);
            var panel = Find<Builder.UI.BonesPanel>();
            panel.Open();
            panel.Choose(session.Document.content.bones[0].id);
            for (int i = 0; i < 3; i++) yield return null;
            var handles = GameObject.Find("Bone Handles");
            Assert.IsTrue(handles.GetComponentsInChildren<MeshRenderer>(false).Length > 0, "handles shown");
            Find<ToolManager>().SetTool(ToolKind.Build);
            for (int i = 0; i < 2; i++) yield return null;
            Assert.AreEqual(0, handles.GetComponentsInChildren<MeshRenderer>(false).Length, "handles hidden in the Build tool");

            // E + wheel in the bones panel: the chosen legs get 10 % bigger.
            Find<ToolManager>().SetTool(ToolKind.Select);
            panel.Open();
            var content = session.Document.content;
            var leg = content.bones.First(b => b.name == "UpperLeg.L");
            panel.Choose(leg.id);
            yield return null;
            float before = Vector3.Distance(leg.head.ToUnity(), leg.tail.ToUnity());
            Find<BoneGizmo>().ScaleChosen(1.1f);
            var now = content.FindBone(leg.id);
            Assert.AreEqual(before * 1.1f, Vector3.Distance(now.head.ToUnity(), now.tail.ToUnity()), 1e-3f);
        }

        [UnityTest]
        public IEnumerator Placing_ShowsOverlaps_TheTouchedBone_AndTheHeight()
        {
            var session = Find<BuilderSession>();
            var tool = Find<BuildTool>();
            session.Document.editor.snapMode = SnapMode.Free;
            tool.SetShape("box");
            session.ReplaceSkeleton(RigTools.Fitted("Biped", new Bounds(new Vector3(0f, 3f, 0f), new Vector3(2f, 6f, 2f))), "Biped", autoBind: false);
            var head = session.Document.content.bones.First(b => b.name == "Head");
            var middle = (head.head.ToUnity() + head.tail.ToUnity()) * 0.5f;
            // A placed piece next to the head (beside the crosshair line, so the ghost still floats there).
            session.Execute(new AddPiecesCommand(new[]
            {
                new Piece { id = "side", shape = "box", p = new ShapeParams { width = 0.4f, height = 0.4f, length = 0.4f }, position = (middle + Vector3.right * 0.5f).ToDoc() },
            }, "add"));
            // Look at the head from 5 m away: the floating ghost sits right on it.
            Find<Builder.CameraControl.CameraRig>().LookFrom(middle + Vector3.back * tool.HoldDistance, middle);
            for (int i = 0; i < 3; i++) yield return null;

            Assert.AreEqual("Head", tool.TouchedBone?.name, "the head bone runs through the ghost");
            Assert.IsTrue(tool.OverlapCount >= 1, "the piece beside it glows red");
            Assert.AreEqual(middle.y - 0.5f, tool.FloatHeight, 0.05f, "the ghost floats this high above the floor");
            Assert.IsTrue(GameObject.Find("Height Line").activeSelf);

            PlaceNow(tool);
            Assert.AreEqual(head.id, session.Document.content.pieces.Last().boneId, "placed on the head: it moves with the head bone");

            // Away from the skeleton: no bone touched, nothing red, the new piece is not bound.
            Find<Builder.CameraControl.CameraRig>().LookFrom(new Vector3(20f, 3f, -5f), new Vector3(20f, 3f, 0f));
            for (int i = 0; i < 3; i++) yield return null;
            Assert.IsNull(tool.TouchedBone);
            Assert.AreEqual(0, tool.OverlapCount);
            PlaceNow(tool);
            Assert.IsNull(session.Document.content.pieces.Last().boneId);
        }

        [UnityTest]
        public IEnumerator FrameParts_AddFitAgainRemove_OneUndoStepEach()
        {
            var session = Find<BuilderSession>();
            session.Execute(new AddPiecesCommand(new[] { new Piece { id = "mine", shape = "box", p = new ShapeParams(), position = new Vec3(5f, 0.5f, 0f) } }, "add"));
            session.ReplaceSkeleton(RigTools.Fitted("Biped", new Bounds(new Vector3(0f, 1f, 0f), new Vector3(1.2f, 2f, 1.2f))), "Biped", autoBind: false);
            var panel = Find<Builder.UI.BonesPanel>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            void Press(string method) => typeof(Builder.UI.BonesPanel).GetMethod(method, flags).Invoke(panel, null);
            var content = session.Document.content;

            Press("AddFrame");
            yield return null;
            int frame = content.pieces.Count(p => p.frame);
            Assert.That(frame, Is.GreaterThan(20));

            // Move the left knee, press again: the frame is fitted again (same number of parts, not doubled).
            var shin = content.bones.First(b => b.name == "LowerLeg.L");
            var moved = shin.Clone();
            moved.head = new Vec3(shin.head.x - 0.3f, shin.head.y, shin.head.z);
            session.Execute(new BonesCommand(new[] { shin }, new[] { moved }, "move"));
            Press("AddFrame");
            yield return null;
            int refitted = content.pieces.Count(p => p.frame);
            Assert.That(refitted, Is.InRange(frame - 6, frame + 6), "fitted again, not doubled (a longer leg may get one more brace)");
            Assert.IsNotNull(content.FindPiece("mine"), "your own pieces stay");

            Press("RemoveFrame");
            yield return null;
            Assert.AreEqual(0, content.pieces.Count(p => p.frame));
            session.History.Undo();
            Assert.AreEqual(refitted, content.pieces.Count(p => p.frame), "Ctrl+Z brings the whole frame back");
        }

        [UnityTest]
        public IEnumerator Blueprint_CantContainItself()
        {
            var session = Find<BuilderSession>();
            var library = Find<Builder.IO.BlueprintLibrary>();
            session.Execute(new AddPiecesCommand(new[]
            {
                new Piece { id = "a", shape = "box", p = new ShapeParams(), position = new Vec3(5f, 0.5f, 0f) },
                new Piece { id = "b", shape = "box", p = new ShapeParams(), position = new Vec3(8f, 0.5f, 0f) },
            }));
            for (int i = 0; i < 3; i++) yield return null;
            SaveBlueprint("Finger", "a");
            SaveBlueprint("Hand", "a", "b");                       // Hand contains Finger
            var hand = PlaceBlueprint(library, "Hand");
            SaveBlueprint("Finger", hand.ToArray());               // Finger would contain Hand -> refused
            Assert.AreEqual(1, library.All.First(b => b.name == "Finger").version, "not saved");
            Find<Builder.UI.BlueprintDialog>().Close();
        }

        [UnityTest]
        public IEnumerator Points_B_BuildsTheSolid_OneUndoBringsPointsBack()
        {
            var session = Find<BuilderSession>();
            var points = new List<Piece>();
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
                points.Add(new Piece { id = $"p{x}{y}{z}", shape = Builder.Shapes.PointShape.ShapeId,
                                       p = Builder.Shapes.ShapeRegistry.Get("point").CreateDefaults(),
                                       position = new Vec3(x * 3f, y * 1f, z * 1f) });   // corners of a 3 x 1 x 1 block
            session.Execute(new AddPiecesCommand(points));
            yield return null;

            Assert.IsTrue(Find<PointHull>().Build());
            yield return null;
            var pieces = session.Document.content.pieces;
            Assert.AreEqual(1, pieces.Count, "8 points became 1 solid");
            Assert.AreEqual("hull", pieces[0].shape);
            var mesh = Find<DocumentView>().Find(pieces[0].id).GetComponent<MeshFilter>().sharedMesh;
            Assert.That(Vector3.Distance(mesh.bounds.size, new Vector3(3f, 1f, 1f)), Is.LessThan(1e-3f), "a 3 x 1 x 1 block");
            Assert.That(Vector3.Distance(pieces[0].position.ToUnity(), new Vector3(1.5f, 0.5f, 0.5f)), Is.LessThan(1e-4f));

            session.History.Undo();
            Assert.AreEqual(8, session.Document.content.pieces.Count, "one undo brings the 8 points back");
        }

        [UnityTest]
        public IEnumerator TurnStep_45_TurnsBy45()
        {
            var tool = Find<BuildTool>();
            yield return null;
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            typeof(BuildTool).GetField("rotateStepIndex", flags).SetValue(tool, 1);   // 45 degrees (like Shift+R once)
            Assert.AreEqual(45f, tool.RotateStep);
            typeof(BuildTool).GetMethod("Turn", flags).Invoke(tool, new object[] { 1 });   // one wheel notch
            Assert.That(Quaternion.Angle(tool.PieceRotation, Quaternion.Euler(0f, 45f, 0f)), Is.LessThan(0.01f));
        }

        [UnityTest]
        public IEnumerator ImportModels_ObjAndGlb_BecomePlaceableItems()
        {
            var models = Find<Builder.IO.ModelLibrary>();
            System.IO.Directory.CreateDirectory(Builder.IO.ModelLibrary.ImportFolder);

            // An .obj box (2 x 1 x 1) ...
            string obj = System.IO.Path.Combine(Builder.IO.ModelLibrary.ImportFolder, "longbox.obj");
            System.IO.File.WriteAllText(obj, ModelAndFlipTestsObj.Box);
            var objTask = models.Import(obj);
            while (!objTask.IsCompleted) yield return null;
            Assert.IsNull(objTask.Exception, objTask.Exception?.ToString());
            Assert.That(objTask.Result.size.x, Is.EqualTo(2f).Within(1e-4f));

            // ... and a .glb made by our own exporter (a 1 m box).
            var doc = ProjectDocument.CreateNew("glb", 0);
            doc.content.pieces.Add(new Piece { id = "x", shape = "box", p = new ShapeParams() });
            string glb = System.IO.Path.Combine(Builder.IO.ModelLibrary.ImportFolder, "cube.glb");
            var export = Builder.IO.GlbExporter.ExportAsync(doc, new Material(Shader.Find("Universal Render Pipeline/Lit")), glb);
            while (!export.IsCompleted) yield return null;
            var glbTask = models.Import(glb);
            while (!glbTask.IsCompleted) yield return null;
            Assert.IsNull(glbTask.Exception, glbTask.Exception?.ToString());
            Assert.That(Vector3.Distance(glbTask.Result.size.ToUnity(), Vector3.one), Is.LessThan(1e-3f), "glb box is 1 x 1 x 1");

            // Both are items now: on the hotbar, the ghost uses the model.
            var listed = Builder.Shapes.ShapeRegistry.Models.Select(m => m.Id).ToList();
            CollectionAssert.IsSubsetOf(new[] { Builder.Shapes.ModelShape.Prefix + objTask.Result.id, Builder.Shapes.ModelShape.Prefix + glbTask.Result.id }, listed);
            var hotbar = Find<Hotbar>();
            hotbar.Assign(hotbar.Selected, Builder.Shapes.ModelShape.Prefix + objTask.Result.id);
            yield return null;
            var tool = Find<BuildTool>();
            Assert.AreEqual("longbox", tool.ItemName);
            PlaceNow(tool);
            var placed = Find<BuilderSession>().Document.content.pieces.Last();
            Assert.That(Find<DocumentView>().Find(placed.id).GetComponent<MeshFilter>().sharedMesh.bounds.size.x, Is.EqualTo(2f).Within(1e-3f));
        }

        [UnityTest]
        public IEnumerator ImportModels_KeepTheirColors_OnePiecePerColor()
        {
            var models = Find<Builder.IO.ModelLibrary>();
            System.IO.Directory.CreateDirectory(Builder.IO.ModelLibrary.ImportFolder);

            // A .glb from our own exporter: a red box and a blue box next to it.
            var doc = ProjectDocument.CreateNew("two", 0);
            doc.content.pieces.Add(new Piece { id = "r", shape = "box", p = new ShapeParams(), color = "#E02020", position = new Vec3(0f, 0.5f, 0f) });
            doc.content.pieces.Add(new Piece { id = "b", shape = "box", p = new ShapeParams(), color = "#2040E0", position = new Vec3(2f, 0.5f, 0f) });
            string glb = System.IO.Path.Combine(Builder.IO.ModelLibrary.ImportFolder, "twobox.glb");
            var export = Builder.IO.GlbExporter.ExportAsync(doc, new Material(Shader.Find("Universal Render Pipeline/Lit")), glb);
            while (!export.IsCompleted) yield return null;
            var task = models.Import(glb);
            while (!task.IsCompleted) yield return null;
            Assert.IsNull(task.Exception, task.Exception?.ToString());
            var model = task.Result;
            Assert.AreEqual(2, model.colors.Length, string.Join(" ", model.colors));

            // Placing it: two pieces, each in (about) its color, where the boxes were.
            var hotbar = Find<Hotbar>();
            hotbar.Assign(hotbar.Selected, Builder.Shapes.ModelShape.Prefix + model.id);
            yield return null;
            var tool = Find<BuildTool>();
            Assert.IsNotNull(tool.CurrentBlueprint, "a colored model is placed like a library part");
            int before = Find<BuilderSession>().Document.content.pieces.Count;
            PlaceNow(tool);
            var placed = Find<BuilderSession>().Document.content.pieces.Skip(before).ToList();
            Assert.AreEqual(2, placed.Count);
            Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
            float Diff(string a, string b) { var x = Hex(a); var y = Hex(b); return Mathf.Abs(x.r - y.r) + Mathf.Abs(x.g - y.g) + Mathf.Abs(x.b - y.b); }
            var red = placed.OrderBy(p => p.position.x).First();
            var blue = placed.OrderBy(p => p.position.x).Last();
            Assert.That(Diff(red.color, "#E02020"), Is.LessThan(0.1f), red.color);
            Assert.That(Diff(blue.color, "#2040E0"), Is.LessThan(0.1f), blue.color);
            Assert.AreEqual(2f, blue.position.x - red.position.x, 1e-3f);
        }

        [UnityTest]
        public IEnumerator Projects_SaveNewOpen_SwapTheModel()
        {
            var session = Find<BuilderSession>();
            var projects = Find<Builder.IO.ProjectController>();
            var view = Find<Builder.View.DocumentView>();

            session.Execute(new AddPiecesCommand(new[] { new Piece { id = "a", shape = "box", p = new ShapeParams(), position = new Vec3(0f, 0.5f, 0f) } }));
            yield return null;
            Assert.IsTrue(session.Dirty);
            Assert.IsTrue(projects.Save(quiet: true));
            Assert.IsFalse(session.Dirty);
            string first = session.Document.id;

            projects.New();   // saves + opens an empty "Mech 1"
            yield return null;
            Assert.AreEqual(0, view.PieceCount, "new project is empty");
            Assert.AreEqual("Mech 1", session.Document.name);

            projects.Open(first);
            yield return null;
            Assert.AreEqual(1, view.PieceCount, "the first project's box is back");
            Assert.AreEqual(first, Builder.IO.ProjectStore.LastId, "opens again next time");
        }

        [UnityTest]
        public IEnumerator SelectTool_ShowsEditPanelForSelectedPiece()
        {
            var session = Find<BuilderSession>();
            session.Execute(new AddPiecesCommand(new[]
            {
                new Piece { id = "test", shape = "cylinder", p = ShapeRegistry.Get("cylinder").CreateDefaults(), position = new Vec3(0f, 0.5f, 0f) },
            }));

            Find<ToolManager>().SetTool(ToolKind.Select);
            Find<Selection>().Set("test");
            for (int i = 0; i < 3; i++) yield return null;

            var panel = GameObject.Find("Edit Panel");
            Assert.IsNotNull(panel, "edit panel should be visible");
            var rows = panel.transform.Find("Rows");
            // Cylinder, one piece: "Save as Blueprint" + 3 position + 3 rotation + width/height/length + bevel, taper,
            // hollow, sides = 14 rows.
            Assert.AreEqual(14, rows.childCount);

            // Deleting the piece empties the selection and hides the panel.
            session.Execute(new RemovePiecesCommand(new[] { "test" }));
            yield return null;
            Assert.AreEqual(0, Find<Selection>().Count);
            Assert.IsNull(GameObject.Find("Edit Panel"), "panel hides when nothing is selected");
        }

        [UnityTest]
        public IEnumerator Eraser_FindsOverlappingOrInsidePieces()
        {
            var session = Find<BuilderSession>();
            Piece Box(string id, float x) => new Piece { id = id, shape = "box", p = new ShapeParams(), position = new Vec3(x, 0.5f, 0.5f) };
            // The eraser below reaches x = -0.25 .. 1.25. "inside" sits in it, "edge" sticks out of it, "far" is away,
            // "neighbor" (x = -1.25 .. -0.25) only touches the eraser's side and must NOT be caught.
            session.Execute(new AddPiecesCommand(new[] { Box("inside", 0.5f), Box("edge", 1.5f), Box("far", 5.5f), Box("neighbor", -0.75f) }));
            yield return null;   // let the pieces' GameObjects and colliders appear

            var eraser = Find<ToolManager>();
            eraser.SetTool(ToolKind.Erase);
            yield return null;
            var tool = Find<EraseTool>();
            // A 1.5 m eraser box around the first piece: reaches x = -0.25 .. 1.25.
            var mesh = ShapeRegistry.GetMesh("box", new ShapeParams { width = 1.5f, height = 1.5f, length = 1.5f }, false);

            var overlap = new HashSet<string>();
            tool.Collect(mesh, new Vector3(0.5f, 0.5f, 0.5f), Quaternion.identity, overlap);
            CollectionAssert.AreEquivalent(new[] { "inside", "edge" }, overlap);

            // Q switches to "fully inside": only the first piece.
            tool.InsideOnly = true;
            var inside = new HashSet<string>();
            tool.Collect(mesh, new Vector3(0.5f, 0.5f, 0.5f), Quaternion.identity, inside);
            CollectionAssert.AreEquivalent(new[] { "inside" }, inside);
        }

        [UnityTest]
        public IEnumerator AllTools_MirrorAndPaste_RunWithoutErrors()
        {
            var session = Find<BuilderSession>();
            session.Document.editor.mirrorX = true;
            session.Place(new[] { new Piece { id = "p", shape = "wedge", p = new ShapeParams(), position = new Vec3(2f, 0.5f, 0f) } }, "Place");
            Assert.AreEqual(2, session.Document.content.pieces.Count, "mirror copy placed too");

            var tools = Find<ToolManager>();
            foreach (ToolKind kind in System.Enum.GetValues(typeof(ToolKind)))
            {
                tools.SetTool(kind);
                for (int i = 0; i < 3; i++) yield return null;
            }

            tools.SetTool(ToolKind.Build);
            Find<BuildTool>().StartPaste(new List<Piece> { session.Document.content.FindPiece("p").Clone() });
            for (int i = 0; i < 3; i++) yield return null;
            Assert.IsTrue(Find<BuildTool>().Pasting);

            session.Remove(new[] { "p" }, "Delete");
            Assert.AreEqual(0, session.Document.content.pieces.Count, "mirror partner deleted too");
        }

        [UnityTest]
        public IEnumerator GroupsPanel_ListsGroups_AndHidingHidesPieces()
        {
            var session = Find<BuilderSession>();
            session.Execute(new AddPiecesCommand(new[]
            {
                new Piece { id = "a", shape = "box", p = new ShapeParams(), position = new Vec3(0f, 0.5f, 0f) },
                new Piece { id = "b", shape = "box", p = new ShapeParams(), position = new Vec3(2f, 0.5f, 0f) },
            }));
            var group = session.CreateGroup(session.NextGroupName(), new[] { "a" }, new Vec3(0f, 0f, 0f));
            Assert.AreEqual("Body", group.name, "first preset name");

            Find<ToolManager>().SetTool(ToolKind.Select);
            for (int i = 0; i < 3; i++) yield return null;
            var rows = GameObject.Find("Groups Panel").transform.Find("Rows");
            Assert.AreEqual(2, rows.childCount, "Body + Ungrouped rows");

            var hidden = session.Document.content.FindGroup(group.id).Clone();
            hidden.visible = false;
            session.ChangeGroup(hidden, "Hide");
            yield return null;
            var view = Find<Builder.View.DocumentView>().Find("a");
            Assert.IsFalse(view.GetComponent<MeshRenderer>().enabled, "pieces of a hidden group are not drawn");
            Assert.IsTrue(Find<Builder.View.DocumentView>().Find("b").GetComponent<MeshRenderer>().enabled);
        }

        [UnityTest]
        public IEnumerator Menu_OpensControls_AndMarksDoubleUsedKeys()
        {
            var input = Find<Builder.Input.BuilderInput>();
            var menu = Find<Builder.UI.MenuScreen>();
            menu.Open();
            yield return null;
            Assert.IsTrue(input.MenuOpen, "menu pauses building");

            // Give "Fly up" the same key as "Grid size" (G) - only in memory, nothing is saved.
            var up = input.Actions.FindAction("Up");
            var entry = Builder.Input.ControlsCatalog.All.First(e => e.label == "Fly up");
            up.ApplyBindingOverride(entry.BindingIndex(up), "<Keyboard>/g");
            Assert.AreEqual("G", entry.KeyText(input.Actions));

            var controlsButton = Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None)
                .First(b => b.GetComponentInChildren<TMPro.TextMeshProUGUI>().text.StartsWith("Controls  (change"));
            controlsButton.onClick.Invoke();
            yield return null;

            var labels = Object.FindObjectsByType<TMPro.TextMeshProUGUI>(FindObjectsSortMode.None).Select(t => t.text).ToList();
            Assert.IsTrue(labels.Any(t => t.StartsWith("Fly up") && t.Contains("same key as Grid size")), "double key is marked");

            input.Actions.RemoveAllBindingOverrides();
            menu.Close();
            yield return null;
            Assert.IsFalse(input.MenuOpen);
        }

        [UnityTest]
        public IEnumerator BuildTool_PlacesEveryShapeWithoutErrors()
        {
            var session = Find<BuilderSession>();
            var hotbar = Find<Hotbar>();
            for (int slot = 0; slot < Hotbar.SlotCount; slot++)
            {
                hotbar.Select(slot);
                yield return null;
            }
            Assert.AreEqual(ShapeRegistry.All[Hotbar.SlotCount - 1].Id, Find<BuildTool>().ShapeId);
            Assert.AreEqual(0, session.Document.content.pieces.Count);
        }
    }
}
