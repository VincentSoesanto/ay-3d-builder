using System;

namespace Builder.Core
{
    /// <summary>Makes the permanent ids used for pieces, groups, bones, projects and blueprints.</summary>
    public static class Ids
    {
        /// <summary>A new random id, e.g. "3f2a9c01b7e44d6a8e0c5b1d2f4a6e8c".</summary>
        public static string New() => Guid.NewGuid().ToString("N");
    }
}
