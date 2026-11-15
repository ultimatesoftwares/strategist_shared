namespace Strategist.Infrastructure.Configuration
{

    /// <summary>
    /// Minimal dotenv loader: hosts call <see cref="Load"/> first thing in Main so
    /// a developer's `.env` file (repo root, gitignored) feeds the same
    /// configuration paths as real environment variables (db, QUESTDB_URL,
    /// Server__*, …). Variables already set in the process
    /// environment always win — the file never overrides them — so compose,
    /// systemd and CI env blocks behave exactly as before.
    ///
    /// Resolution walks upward from the current working directory and, failing
    /// that, from the binary's folder, using the first `.env` found. Syntax is
    /// one KEY=VALUE per line with `#` comment lines, an optional `export `
    /// prefix, and optional single or double quotes around the value.
    /// </summary>
    public static class DotEnv
    {
        /// <summary>
        /// Loads the nearest `.env` into the process environment and returns its
        /// path, or null when no file was found. Must run before the host builds
        /// its configuration, or the environment-variable provider won't see the
        /// values.
        /// </summary>
        public static string? Load()
        {
            var path = Find(Directory.GetCurrentDirectory()) ?? Find(AppContext.BaseDirectory);
            if (path is null) return null;

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                if (line.StartsWith("export ", StringComparison.Ordinal))
                    line = line["export ".Length..].TrimStart();

                var eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var key = line[..eq].TrimEnd();
                var value = line[(eq + 1)..].Trim();
                if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
                    value = value[1..^1];

                // Empty counts as unset: the hosts' EnvOr helpers treat "" as
                // missing, so the file may still supply a real value.
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                    Environment.SetEnvironmentVariable(key, value);
            }

            return path;
        }

        private static string? Find(string? start)
        {
            for (var dir = start; !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
            {
                var candidate = Path.Combine(dir, ".env");
                if (File.Exists(candidate)) return candidate;
            }

            return null;
        }
    }
}
