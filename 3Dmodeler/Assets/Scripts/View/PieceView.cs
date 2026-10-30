using UnityEngine;

namespace Builder.View
{
    /// <summary>Sits on each piece's GameObject so a raycast hit can tell which document piece it is.</summary>
    public class PieceView : MonoBehaviour
    {
        public string PieceId { get; set; }
    }
}
