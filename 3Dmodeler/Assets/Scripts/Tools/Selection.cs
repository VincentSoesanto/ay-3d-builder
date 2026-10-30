using System;
using System.Collections.Generic;
using Builder.Core;
using Builder.View;
using UnityEngine;

namespace Builder.Tools
{
    /// <summary>
    /// Which pieces are selected (by id). The select tool changes it; the gizmo, edit panel and highlight read it.
    /// Pieces that disappear (deleted, undone) are dropped from the selection automatically.
    /// </summary>
    public class Selection : MonoBehaviour
    {
        [SerializeField] BuilderSession session;
        [SerializeField] DocumentView documentView;

        readonly List<string> ids = new List<string>();

        /// <summary>Raised when pieces are added to / removed from the selection.</summary>
        public event Action Changed;

        public IReadOnlyList<string> Ids => ids;
        public int Count => ids.Count;
        public bool Contains(string id) => ids.Contains(id);

        void Start()
        {
            session.Changed += OnDocumentChanged;
            session.Replaced += Clear;   // another project was opened
        }

        void OnDestroy()
        {
            if (session == null) return;
            session.Changed -= OnDocumentChanged;
            session.Replaced -= Clear;
        }

        /// <summary>Selects only this piece.</summary>
        public void Set(string id)
        {
            if (ids.Count == 1 && ids[0] == id) return;
            ids.Clear();
            ids.Add(id);
            Changed?.Invoke();
        }

        /// <summary>Selects exactly these pieces (e.g. "select all of a group").</summary>
        public void SetMany(IEnumerable<string> pieceIds)
        {
            ids.Clear();
            ids.AddRange(pieceIds);
            Changed?.Invoke();
        }

        /// <summary>Adds these pieces to the selection (ones already selected stay selected).</summary>
        public void Add(IEnumerable<string> pieceIds)
        {
            int count = ids.Count;
            foreach (var id in pieceIds)
                if (!ids.Contains(id)) ids.Add(id);
            if (ids.Count != count) Changed?.Invoke();
        }

        /// <summary>Adds the piece, or removes it if it was already selected (Shift+click).</summary>
        public void Toggle(string id)
        {
            if (!ids.Remove(id)) ids.Add(id);
            Changed?.Invoke();
        }

        public void Clear()
        {
            if (ids.Count == 0) return;
            ids.Clear();
            Changed?.Invoke();
        }

        /// <summary>The selected pieces as they are in the document now.</summary>
        public List<Piece> Pieces()
        {
            var content = session.Document.content;
            var list = new List<Piece>();
            foreach (var id in ids)
            {
                var p = content.FindPiece(id);
                if (p != null) list.Add(p);
            }
            return list;
        }

        /// <summary>The box around all selected pieces, or null when nothing is selected.</summary>
        public Bounds? Bounds => documentView.GetBounds(ids);

        void OnDocumentChanged(ChangeSet changes)
        {
            var content = session.Document.content;
            if (ids.RemoveAll(id => content.FindPiece(id) == null) > 0) Changed?.Invoke();
        }
    }
}
