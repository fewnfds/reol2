using UnityEditor;

namespace Reol2.Editor
{
    internal static class WindowsTestBuild
    {
        internal static BuildPlayerOptions Options(string scene, string executable) => new()
        {
            scenes = new[] { scene },
            locationPathName = executable,
            target = BuildTarget.StandaloneWindows64,
            // Unity's native Development Player opens a Profiler listener at startup.
            // Include the game's test tools on their own for unattended local builds.
            extraScriptingDefines = new[] { "REOL_TEST_BUILD" }
        };
    }
}
