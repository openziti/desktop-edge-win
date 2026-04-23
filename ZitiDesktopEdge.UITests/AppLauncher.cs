using System;
using System.IO;

namespace ZitiDesktopEdge.UITests {
	internal static class AppLauncher {
		private const string ExeName = "ZitiDesktopEdge.exe";
		private const string WpfProjectDir = "DesktopEdge";
		private const string SolutionMarker = "ZitiDesktopEdge.sln";

		// Resolves the built WPF exe by walking up from the test assembly to the repo root,
		// then into DesktopEdge/bin/{configuration}/. Release builds enforce single-instance
		// via a named mutex, so a Debug build is recommended for UI tests.
		public static string ResolveExePath(string configuration) {
			string repoRoot = FindRepoRoot();
			string candidate = Path.Combine(repoRoot, WpfProjectDir, "bin", configuration, ExeName);
			if (!File.Exists(candidate)) {
				throw new FileNotFoundException(
					$"Built {ExeName} not found at '{candidate}'. Build {WpfProjectDir} in {configuration} before running UI tests.",
					candidate);
			}
			return candidate;
		}

		private static string FindRepoRoot() {
			string? dir = AppContext.BaseDirectory;
			while (dir != null) {
				if (File.Exists(Path.Combine(dir, SolutionMarker))) {
					return dir;
				}
				dir = Path.GetDirectoryName(dir);
			}
			throw new DirectoryNotFoundException(
				$"Could not locate repository root containing '{SolutionMarker}' starting from '{AppContext.BaseDirectory}'.");
		}
	}
}
