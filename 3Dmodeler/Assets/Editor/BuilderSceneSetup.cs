using System;
using System.IO;
using Builder.CameraControl;
using Builder.Input;
using Builder.IO;
using Builder.Tools;
using Builder.UI;
using Builder.View;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Selection = Builder.Tools.Selection;   // not UnityEditor.Selection
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Builder.EditorTools
{
    /// <summary>
    /// Creates the Builder scene and its materials from code, so the setup is written down and repeatable.
    /// Menu: Builder > Rebuild Builder Scene. WARNING: this replaces Assets/Scenes/Builder.unity, so hand-made
    /// changes to that scene are lost. Put lasting changes in this script (or BuilderHudSetup) instead.
    /// </summary>
    public static class BuilderSceneSetup
    {
        public const string ScenePath = "Assets/Scenes/Builder.unity";
        public const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        [MenuItem("Builder/Rebuild Builder Scene")]
        public static void RebuildSceneMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath) == null)
            {
                ImportTextMeshProEssentials();
                EditorUtility.DisplayDialog("Builder", "TextMeshPro files were imported. Run Builder > Rebuild Builder Scene once more.", "OK");
                return;
            }
            BuildScene();
        }

        /// <summary>
        /// Imports TextMeshPro's basic files (default font, shaders, settings) into Assets/TextMesh Pro.
        /// Command line: -executeMethod Builder.EditorTools.BuilderSceneSetup.ImportTextMeshProEssentials
        /// </summary>
        public static void ImportTextMeshProEssentials()
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath) != null) return;
            string package = Path.GetFullPath("Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage");
            AssetDatabase.ImportPackage(package, false);
            AssetDatabase.Refresh();
        }

        /// <summary>Command line: -executeMethod Builder.EditorTools.BuilderSceneSetup.BuildScene</summary>
        public static void BuildScene()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font == null) throw new Exception("TextMeshPro font missing: run ImportTextMeshProEssentials first");

            // ---------- materials ----------
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var ghostShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Ghost.shader");
            var gridShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Grid.shader");
            var overlayShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Overlay.shader");

            var pieceMat = MakeMaterial("Assets/Materials/Piece.mat", lit, m =>
            {
                m.SetColor("_BaseColor", Color.white);   // each piece color is a copy of this with its own _BaseColor
                m.SetFloat("_Smoothness", 0.15f);
            });
            var floorMat = MakeMaterial("Assets/Materials/Floor.mat", lit, m =>
            {
                m.SetColor("_BaseColor", new Color(0.17f, 0.18f, 0.2f));
                m.SetFloat("_Smoothness", 0.05f);
            });
            var gridMat = MakeMaterial("Assets/Materials/Grid.mat", gridShader, null);
            var ghostMat = MakeMaterial("Assets/Materials/Ghost.mat", ghostShader,
                m => m.SetColor("_Color", new Color(0.4f, 0.85f, 1f, 0.3f)));
            var highlightMat = MakeMaterial("Assets/Materials/AimHighlight.mat", ghostShader, m =>
            {
                m.SetColor("_Color", new Color(1f, 1f, 1f, 0.06f));
                m.SetFloat("_RimStrength", 0.35f);
            });

            var selectedMat = MakeMaterial("Assets/Materials/Selected.mat", ghostShader, m =>
            {
                m.SetColor("_Color", new Color(1f, 0.7f, 0.2f, 0.14f));
                m.SetFloat("_RimStrength", 0.6f);
            });
            var gizmoMat = MakeMaterial("Assets/Materials/Gizmo.mat", overlayShader, null);

            // ---------- scene: starts with a Main Camera + Directional Light ----------
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Load these AFTER NewScene: opening a new scene unloads assets nothing uses yet.
            var controls = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/Input/BuilderControls.inputactions");
            if (controls == null) throw new Exception("Assets/Input/BuilderControls.inputactions is missing");
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

            // Soft sky-colored ambient light that needs no light baking.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.43f, 0.46f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.21f, 0.2f);

            var sun = Object.FindFirstObjectByType<Light>();
            sun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            sun.shadows = LightShadows.Soft;

            // Floor: solid surface you build on (it catches shadows and the crosshair ray).
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(40f, 1f, 40f);        // a plane is 10 m, so 400 m x 400 m
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;

            // Grid lines: a see-through quad a hair above the floor.
            var grid = GameObject.CreatePrimitive(PrimitiveType.Quad);
            grid.name = "Grid";
            Object.DestroyImmediate(grid.GetComponent<Collider>());       // the ray should hit the floor, not this
            grid.transform.SetPositionAndRotation(new Vector3(0f, 0.002f, 0f), Quaternion.Euler(90f, 0f, 0f));
            grid.transform.localScale = new Vector3(400f, 400f, 1f);
            var gridRenderer = grid.GetComponent<MeshRenderer>();
            gridRenderer.sharedMaterial = gridMat;
            gridRenderer.shadowCastingMode = ShadowCastingMode.Off;
            gridRenderer.receiveShadows = false;

            // ---------- the builder (all the logic lives here) ----------
            var cam = Camera.main;
            var builder = new GameObject("Builder");
            var input = builder.AddComponent<BuilderInput>();
            Set(input, "actions", controls);
            var session = builder.AddComponent<BuilderSession>();
            var view = builder.AddComponent<DocumentView>();
            Set(view, "session", session);
            Set(view, "pieceMaterial", pieceMat);
            var tool = builder.AddComponent<BuildTool>();
            Set(tool, "session", session);
            Set(tool, "input", input);
            Set(tool, "viewCamera", cam);
            Set(tool, "ghostMaterial", ghostMat);
            Set(tool, "highlightMaterial", highlightMat);
            var shortcuts = builder.AddComponent<EditShortcuts>();
            Set(shortcuts, "session", session);
            Set(shortcuts, "input", input);
            var library = builder.AddComponent<BlueprintLibrary>();
            var modelLibrary = builder.AddComponent<ModelLibrary>();
            Set(tool, "library", library);
            var icons = builder.AddComponent<ShapeIcons>();
            Set(icons, "iconMaterial", pieceMat);
            Set(icons, "library", library);
            Set(library, "session", session);
            Set(library, "icons", icons);
            Set(grid.AddComponent<GridOverlay>(), "session", session);

            // ---------- camera ----------
            cam.transform.position = new Vector3(0f, 3f, -6f);
            cam.transform.LookAt(new Vector3(0f, 0.5f, 0f));
            cam.nearClipPlane = 0.05f;
            var rig = cam.gameObject.AddComponent<CameraRig>();
            Set(rig, "input", input);
            Set(rig, "documentView", view);
            Set(tool, "cameraRig", rig);

            // ---------- select tool ----------
            var selection = builder.AddComponent<Selection>();
            Set(selection, "session", session);
            Set(selection, "documentView", view);
            Set(rig, "selection", selection);
            var gizmo = builder.AddComponent<TransformGizmo>();
            Set(gizmo, "session", session);
            Set(gizmo, "input", input);
            Set(gizmo, "selection", selection);
            Set(gizmo, "viewCamera", cam);
            Set(gizmo, "overlayMaterial", gizmoMat);
            var selectTool = builder.AddComponent<SelectTool>();
            Set(selectTool, "session", session);
            Set(selectTool, "input", input);
            Set(selectTool, "selection", selection);
            Set(selectTool, "gizmo", gizmo);
            Set(selectTool, "viewCamera", cam);
            // ---------- paint + eraser + clipboard + mirror ----------
            var paintTool = builder.AddComponent<PaintTool>();
            Set(paintTool, "session", session);
            Set(paintTool, "input", input);
            Set(paintTool, "viewCamera", cam);
            Set(paintTool, "previewMaterial", ghostMat);
            var eraseTool = builder.AddComponent<EraseTool>();
            Set(eraseTool, "session", session);
            Set(eraseTool, "ghostMaterial", ghostMat);
            Set(tool, "eraser", eraseTool);
            var clipboard = builder.AddComponent<Clipboard>();
            var projectController = builder.AddComponent<ProjectController>();
            Set(projectController, "session", session);
            Set(projectController, "input", input);
            var exporter = builder.AddComponent<ExportController>();
            Set(exporter, "session", session);
            Set(exporter, "input", input);
            Set(exporter, "pieceMaterial", pieceMat);
            var mirrorPlane = builder.AddComponent<MirrorPlaneView>();
            Set(mirrorPlane, "session", session);
            Set(mirrorPlane, "material", ghostMat);

            var precision = builder.AddComponent<PrecisionMode>();
            Set(precision, "session", session);
            Set(precision, "input", input);
            Set(precision, "viewCamera", cam);
            Set(tool, "precision", precision);
            Set(selectTool, "precision", precision);

            var tools = builder.AddComponent<ToolManager>();
            Set(tools, "input", input);
            Set(tools, "buildTool", tool);
            Set(tools, "selectTool", selectTool);
            Set(tools, "paintTool", paintTool);
            Set(tools, "precision", precision);
            Set(precision, "tools", tools);
            Set(shortcuts, "tools", tools);
            Set(shortcuts, "selection", selection);
            Set(shortcuts, "clipboard", clipboard);
            Set(shortcuts, "buildTool", tool);
            var pointHull = builder.AddComponent<PointHull>();
            Set(pointHull, "session", session);
            Set(pointHull, "input", input);
            Set(pointHull, "selection", selection);
            Set(pointHull, "tools", tools);
            var selectionView = builder.AddComponent<SelectionView>();
            Set(selectionView, "selection", selection);
            Set(selectionView, "documentView", view);
            Set(selectionView, "tools", tools);
            Set(selectionView, "selectedMaterial", selectedMat);

            var hotbar = builder.AddComponent<Hotbar>();
            Set(hotbar, "session", session);
            Set(hotbar, "input", input);
            Set(hotbar, "buildTool", tool);
            Set(hotbar, "tools", tools);
            Set(hotbar, "library", library);

            builder.AddComponent<Builder.UI.DevShots>();   // developer screenshots (-uiShot), does nothing normally

            // ---------- cloud sync (Phase 4) ----------
            var cloud = builder.AddComponent<Builder.IO.Cloud.CloudSync>();
            Set(cloud, "session", session);
            Set(cloud, "projects", projectController);
            Set(cloud, "blueprints", library);
            Set(cloud, "models", modelLibrary);

            // ---------- bones (rigger tubes) ----------
            var pose = builder.AddComponent<PoseTest>();
            Set(pose, "session", session);
            Set(pose, "documentView", view);
            var boneView = builder.AddComponent<BoneView>();
            Set(boneView, "session", session);
            Set(boneView, "pose", pose);
            Set(boneView, "overlayMaterial", gizmoMat);
            var bonePlacer = builder.AddComponent<BonePlacer>();
            Set(bonePlacer, "session", session);
            Set(bonePlacer, "view", boneView);
            Set(tool, "bonePlacer", bonePlacer);
            Set(tool, "boneView", boneView);
            Set(selectTool, "pose", pose);

            // ---------- HUD, hotbar, inventory ----------
            BuilderHudSetup.Build(new BuilderHudSetup.Parts
            {
                boneView = boneView,
                selectTool = selectTool,
                cloud = cloud,
                pose = pose,
                font = font,
                session = session,
                input = input,
                cameraRig = rig,
                buildTool = tool,
                hotbar = hotbar,
                documentView = view,
                icons = icons,
                tools = tools,
                selection = selection,
                paintTool = paintTool,
                eraseTool = eraseTool,
                exporter = exporter,
                projectController = projectController,
                precision = precision,
                library = library,
                models = modelLibrary,
                camera = cam,
                overlayMaterial = gizmoMat,
            });

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            RemoveTemplateLeftovers();
            AssetDatabase.SaveAssets();
            Debug.Log("[Builder] Scene built: " + ScenePath);
        }

        /// <summary>Loads the material at path (or creates it), then applies the settings.</summary>
        static Material MakeMaterial(string path, Shader shader, Action<Material> setup)
        {
            if (shader == null) throw new Exception("Shader missing for " + path);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            setup?.Invoke(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Fills a [SerializeField] on a component (what dragging into the Inspector does).</summary>
        public static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field) ?? throw new Exception($"{target.GetType().Name} has no field '{field}'");
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Removes the sample scene and sample input actions that came with the project template.</summary>
        static void RemoveTemplateLeftovers()
        {
            if (AssetDatabase.LoadAssetAtPath<Object>("Assets/Scenes/SampleScene.unity") != null)
                AssetDatabase.DeleteAsset("Assets/Scenes/SampleScene.unity");
            if (AssetDatabase.LoadAssetAtPath<Object>("Assets/InputSystem_Actions.inputactions") != null)
            {
                // It was set as the "project-wide actions"; we use our own BuilderControls instead.
                EditorBuildSettings.RemoveConfigObject("com.unity.input.settings.actions");
                AssetDatabase.DeleteAsset("Assets/InputSystem_Actions.inputactions");
            }
        }
    }
}
