using Builder.Tools;
using NUnit.Framework;
using UnityEngine;

namespace Builder.Tests
{
    public class PrecisionTests
    {
        [Test]
        public void GroundAxis_PicksClosestWorldAxis()
        {
            Assert.AreEqual(Vector3.right, PrecisionMath.GroundAxis(new Vector3(0.9f, 0.3f, 0.2f)));
            Assert.AreEqual(Vector3.back, PrecisionMath.GroundAxis(new Vector3(0.1f, -0.8f, -0.5f)));   // looking down and back
            Assert.AreEqual(Vector3.left, PrecisionMath.GroundAxis(new Vector3(-0.7f, 0f, 0.69f)));
        }

        [Test]
        public void TakeSteps_KeepsLeftoverPixels()
        {
            float pixels = 70f;
            Assert.AreEqual(2, PrecisionMath.TakeSteps(ref pixels, 30f));
            Assert.AreEqual(10f, pixels, 1e-4f);

            pixels += -45f;   // now -35: one step back, -5 left
            Assert.AreEqual(-1, PrecisionMath.TakeSteps(ref pixels, 30f));
            Assert.AreEqual(-5f, pixels, 1e-4f);
        }
    }
}
