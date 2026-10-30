using Builder.Core;
using UnityEngine;

namespace Builder.View
{
    /// <summary>One-line converters between the document's types (Vec3, Quat, "#RRGGBB") and Unity's types.</summary>
    public static class Conversions
    {
        public static Vector3 ToUnity(this Vec3 v) => new Vector3(v.x, v.y, v.z);
        public static Vec3 ToDoc(this Vector3 v) => new Vec3(v.x, v.y, v.z);

        public static Quaternion ToUnity(this Quat q) => new Quaternion(q.x, q.y, q.z, q.w);
        public static Quat ToDoc(this Quaternion q) => new Quat(q.x, q.y, q.z, q.w);

        /// <summary>"#RRGGBB" to a Unity color. Falls back to grey if the text is not a valid color.</summary>
        /// <summary>The color of "#RRGGBB" (a finish after a slash, like "#RRGGBB/metal", is ignored here).</summary>
        public static Color HexToColor(string hex)
        {
            if (hex == null) return Color.gray;
            int slash = hex.IndexOf('/');
            if (slash >= 0) hex = hex.Substring(0, slash);
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.gray;
        }

        public static string ColorToHex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    }
}
