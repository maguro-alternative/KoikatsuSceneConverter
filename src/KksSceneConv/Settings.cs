using System;
using System.IO;

namespace KksSceneConv
{
    /// <summary>Small persisted GUI settings, stored as plain files next to the exe
    /// (portable, no registry), like ui-lang.txt.</summary>
    static class Settings
    {
        static string LastOutputPath { get { return Path.Combine(AppContext.BaseDirectory, "last-output-dir.txt"); } }

        /// <summary>The output folder the user last chose or converted into; null when
        /// none was saved or the folder no longer exists.</summary>
        public static string LoadLastOutputDir()
        {
            try
            {
                if (!File.Exists(LastOutputPath)) return null;
                string s = File.ReadAllText(LastOutputPath).Trim();
                return s.Length > 0 && Directory.Exists(s) ? s : null;
            }
            catch { return null; }
        }

        public static void SaveLastOutputDir(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            try { File.WriteAllText(LastOutputPath, Path.GetFullPath(dir)); } catch { }
        }
    }
}
