using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Builder.Input
{
    /// <summary>
    /// Saves and loads the player's own key choices (the "binding overrides") as a small JSON file:
    /// settings/bindings.json in the app's data folder. Only the changed keys are stored; everything else
    /// stays as in BuilderControls.
    /// </summary>
    public static class BindingStore
    {
        public static string FilePath => Path.Combine(Application.persistentDataPath, "settings", "bindings.json");

        public static void Load(InputActionAsset actions)
        {
            try
            {
                if (File.Exists(FilePath)) actions.LoadBindingOverridesFromJson(File.ReadAllText(FilePath));
            }
            catch (System.Exception e)
            {
                // A broken file must never stop the app: just use the normal keys.
                Debug.LogWarning("[Builder] Could not load key settings, using defaults: " + e.Message);
            }
        }

        public static void Save(InputActionAsset actions)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, actions.SaveBindingOverridesAsJson());
        }
    }
}
