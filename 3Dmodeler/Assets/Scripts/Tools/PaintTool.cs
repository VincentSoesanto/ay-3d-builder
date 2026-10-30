using System.Collections.Generic;
using Builder.Core;
using Builder.Input;
using Builder.UI;
using Builder.View;
using UnityEngine;

namespace Builder.Tools
{
    /// <summary>
    /// Paint tool (P). Aim with the crosshair. Two modes (C switches):
    ///   - WHOLE PIECE: left click / RT paints the aimed piece in the chosen color.
    ///   - SPRAY: hold left click / RT and move: paints small grid squares on the surface (like pixels).
    ///     The square size is the grid size (G changes it); E changes the brush (1, 3 or 5 squares wide).
    ///     Hold Shift while spraying to rub spray paint off again.
    ///   - right click / LT: pick up the color under the crosshair (eyedropper; the spray color too).
    ///   - 1-9, mouse wheel, LB / RB: choose a color; T opens the Colors window (make your own colors, metal / glow).
    /// Mirror on: the mirror partner gets the same paint (mirrored). A whole spray stroke is one undo step.
    /// </summary>
    public class PaintTool : MonoBehaviour
    {
        [SerializeField] BuilderSession session;
        [SerializeField] BuilderInput input;
        [SerializeField] Camera viewCamera;
        [SerializeField] Material previewMaterial;
        [SerializeField] ColorWindow colorWindow;
        [SerializeField] float reach = 60f;

        /// <summary>Starting palette: greys plus colors that work well on low-poly mechs (and a metal and a glow).</summary>
        public static readonly string[] DefaultPalette =
        {
            "#B0B0B0", "#6E6E73", "#2E2F33", "#F2F2F2", "#C0392B", "#E67E22", "#F1C40F", "#27AE60",
            "#16A085", "#2980B9", "#8E44AD", "#8D6E63", "#556B2F", "#34495E", "#9AA3AD/metal", "#4FE3FF/glow",
        };
        public const int MaxColors = 32;
        static readonly int[] BrushSizes = { 1, 3, 5 };

        /// <summary>The project's palette (filled with the default colors the first time it is used).</summary>
        public List<string> Palette
        {
            get
            {
                var palette = session.Document.palette;
                if (palette.Count == 0) palette.AddRange(DefaultPalette);
                return palette;
            }
        }

        public int ColorIndex { get; private set; }
        public string CurrentColor => Palette[Mathf.Clamp(ColorIndex, 0, Palette.Count - 1)];
        public bool Spraying { get; private set; }
        public int Brush => BrushSizes[brushIndex];

        static readonly int ColorId = Shader.PropertyToID("_Color");
        MeshFilter preview;
        MaterialPropertyBlock block;
        PieceView aimed;
        RaycastHit hit;
        int brushIndex;
        bool strokeOpen;

        void Awake()
        {
            var go = new GameObject("Paint Preview");
            go.transform.SetParent(transform, false);
            preview = go.AddComponent<MeshFilter>();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = previewMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            block = new MaterialPropertyBlock();
            go.SetActive(false);
        }

        void OnDisable()
        {
            if (preview != null) preview.gameObject.SetActive(false);
            EndStroke();
        }

        public void SelectColor(int index) => ColorIndex = Mathf.Clamp(index, 0, Palette.Count - 1);

        void Update()
        {
            if (input.Blocked)
            {
                preview.gameObject.SetActive(false);
                EndStroke();
                return;
            }

            if (input.Pressed(input.ColorWindow) && colorWindow != null)
            {
                colorWindow.Open();
                return;
            }
            if (input.Pressed(input.EraseCatch) && !BuilderInput.CtrlHeld)
            {
                Spraying = !Spraying;
                Toast.Show(Spraying ? $"Spray paint: hold the mouse button to paint {Brush}x{Brush} squares (G = square size, E = brush, Shift = rub off)"
                                    : "Paint whole pieces (click)");
            }
            if (input.Pressed(input.ScaleMode) && Spraying)
            {
                brushIndex = (brushIndex + 1) % BrushSizes.Length;
                Toast.Show($"Brush {Brush} x {Brush} squares");
            }

            ChooseColor();
            Aim();

            if (Spraying)
            {
                if (input.Held(input.Place) && aimed != null) Spray(BuilderInput.ShiftHeld);
                if (!input.Held(input.Place)) EndStroke();
            }
            else if (aimed != null && input.Pressed(input.Place)) Paint(aimed.PieceId);
            if (aimed != null && input.Pressed(input.Delete)) PickColor();
        }

        void EndStroke()
        {
            if (!strokeOpen) return;
            strokeOpen = false;
            session.History.SealMerge();   // the whole stroke = one undo step
        }

        void ChooseColor()
        {
            for (int i = 0; i < input.Slots.Length; i++)
                if (input.Pressed(input.Slots[i]) && i < Palette.Count) ColorIndex = i;
            if (input.Pressed(input.TurnMinus) || input.Pressed(input.HotbarNext)) ColorIndex = (ColorIndex + 1) % Palette.Count;
            if (input.Pressed(input.TurnPlus) || input.Pressed(input.HotbarPrev)) ColorIndex = (ColorIndex + Palette.Count - 1) % Palette.Count;
        }

        float CellFor(Piece piece) => piece.HasPaint() && piece.paintCell > 0f ? piece.paintCell : session.Document.editor.gridSize;

        void Aim()
        {
            var ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            aimed = Physics.Raycast(ray, out hit, reach) ? hit.collider.GetComponent<PieceView>() : null;
            preview.gameObject.SetActive(aimed != null);
            if (aimed == null) return;

            var c = Conversions.HexToColor(CurrentColor);
            c.a = 0.55f;
            block.SetColor(ColorId, c);
            preview.GetComponent<MeshRenderer>().SetPropertyBlock(block);
            var t = aimed.transform;
            var piece = session.Document.content.FindPiece(aimed.PieceId);
            if (Spraying && piece != null)
            {
                // A see-through cube over the squares the brush would paint.
                float cell = CellFor(piece);
                var center = PaintedMesh.CellOf(t.InverseTransformPoint(hit.point), t.InverseTransformDirection(hit.normal), cell);
                preview.sharedMesh = GizmoMeshes.Cube;
                preview.transform.SetPositionAndRotation(t.TransformPoint(((Vector3)center + Vector3.one * 0.5f) * cell), t.rotation);
                preview.transform.localScale = Vector3.one * (cell * Brush * 1.02f);
                return;
            }
            // A see-through skin in the new color, just over the aimed piece.
            preview.sharedMesh = aimed.GetComponent<MeshCollider>().sharedMesh;
            preview.transform.SetPositionAndRotation(t.position, t.rotation);
            preview.transform.localScale = Vector3.one * 1.008f;
        }

        void Paint(string id)
        {
            var piece = session.Document.content.FindPiece(id);
            if (piece == null || piece.color == CurrentColor) return;
            var painted = piece.Clone();
            painted.color = CurrentColor;
            session.Modify(new[] { piece }, new[] { painted }, "Paint");
        }

        /// <summary>Paints (or rubs off) the squares under the brush on the aimed piece.</summary>
        void Spray(bool rubOff)
        {
            var piece = session.Document.content.FindPiece(aimed.PieceId);
            if (piece == null) return;
            var t = aimed.transform;
            float cell = CellFor(piece);
            var center = PaintedMesh.CellOf(t.InverseTransformPoint(hit.point), t.InverseTransformDirection(hit.normal), cell);

            var dots = new Dictionary<Vector3Int, string>();
            if (piece.paint != null) foreach (var d in piece.paint) dots[new Vector3Int(d.x, d.y, d.z)] = d.c;
            bool changed = false;
            int r = Brush / 2;
            for (int x = -r; x <= r; x++)
            for (int y = -r; y <= r; y++)
            for (int z = -r; z <= r; z++)
            {
                var key = center + new Vector3Int(x, y, z);
                if (rubOff) changed |= dots.Remove(key);
                else if (!dots.TryGetValue(key, out var old) || old != CurrentColor)
                {
                    dots[key] = CurrentColor;
                    changed = true;
                }
            }
            if (!changed) return;

            var after = piece.Clone();
            after.paintCell = cell;
            after.paint = new List<PaintDot>(dots.Count);
            foreach (var pair in dots) after.paint.Add(new PaintDot(pair.Key.x, pair.Key.y, pair.Key.z, pair.Value));
            if (after.paint.Count == 0)
            {
                after.paint = null;
                after.paintCell = 0f;
            }
            strokeOpen = true;
            session.Modify(new[] { piece }, new[] { after }, rubOff ? "Rub off spray paint" : "Spray paint", "spray");
        }

        /// <summary>Eyedropper: the color under the crosshair (spray paint first), added to the palette if needed.</summary>
        void PickColor()
        {
            var piece = session.Document.content.FindPiece(aimed.PieceId);
            if (piece == null) return;
            string color = piece.color;
            if (piece.HasPaint())
            {
                var t = aimed.transform;
                var cell = PaintedMesh.CellOf(t.InverseTransformPoint(hit.point), t.InverseTransformDirection(hit.normal), piece.paintCell);
                foreach (var d in piece.paint)
                    if (d.x == cell.x && d.y == cell.y && d.z == cell.z) color = d.c;
            }
            int index = Palette.FindIndex(c => string.Equals(c, color, System.StringComparison.OrdinalIgnoreCase));
            if (index < 0 && Palette.Count < MaxColors)
            {
                Palette.Add(color);
                session.MarkDirty();
                index = Palette.Count - 1;
            }
            if (index >= 0)
            {
                ColorIndex = index;
                Toast.Show("Picked color " + color);
            }
        }
    }
}
