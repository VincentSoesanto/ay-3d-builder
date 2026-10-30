using System.Linq;
using Builder.Input;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.InputSystem;

namespace Builder.Tests
{
    public class ControlsTests
    {
        static InputActionAsset Asset() =>
            AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/Input/BuilderControls.inputactions");

        [Test]
        public void EveryControl_FindsItsBinding()
        {
            var asset = Asset();
            foreach (var e in ControlsCatalog.All.Where(e => e.Rebindable))
                Assert.IsNotNull(e.BindingId(asset), $"\"{e.label}\" has no keyboard/mouse binding");
        }

        [Test]
        public void DefaultKeys_HaveNoDoubles()
        {
            // Different bindings must not share a key (the same binding listed under two modes is fine).
            var asset = Asset();
            var doubles = ControlsCatalog.All.Where(e => e.Rebindable)
                .GroupBy(e => e.EffectivePath(asset))
                .Where(g => g.Select(e => e.BindingId(asset)).Distinct().Count() > 1)
                .Select(g => g.Key + ": " + string.Join(", ", g.Select(e => e.label)))
                .ToList();
            Assert.IsEmpty(doubles, string.Join("\n", doubles));
        }
    }
}
