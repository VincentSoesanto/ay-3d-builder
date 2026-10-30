namespace Builder.Tests
{
    /// <summary>Test 3D file content for the play-mode import test.</summary>
    public static class ModelAndFlipTestsObj
    {
        // A 2 x 1 x 1 box as Blender writes it (right-handed, faces counter-clockwise from outside, quads).
        public const string Box = @"# test box
v -1 -0.5 -0.5
v 1 -0.5 -0.5
v 1 0.5 -0.5
v -1 0.5 -0.5
v -1 -0.5 0.5
v 1 -0.5 0.5
v 1 0.5 0.5
v -1 0.5 0.5
f 1 4 3 2
f 5 6 7 8
f 1 2 6 5
f 4 8 7 3
f 1 5 8 4
f 2 3 7 6
";
    }
}
