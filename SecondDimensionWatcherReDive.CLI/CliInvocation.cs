using System.Runtime.InteropServices;
using System.Text;

namespace SecondDimensionWatcherReDive.CLI;

internal static class CliInvocation
{
    public static string? GetAppletName()
    {
        // The managed entry assembly and ProcessPath resolve the apphost target, losing symlink names.
        var executable = GetNativeExecutableArgument() ?? Environment.GetCommandLineArgs().FirstOrDefault()
            ?? Environment.ProcessPath;
        var name = Path.GetFileNameWithoutExtension(executable);
        return string.Equals(name, "sdw-migrate", OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal)
            ? "migrate"
            : null;
    }

    private static string? GetNativeExecutableArgument()
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                // Read only argv[0]; the remaining arguments are not needed for dispatch.
                using var commandLine = File.OpenRead("/proc/self/cmdline");
                using var executable = new MemoryStream();
                int value;
                while ((value = commandLine.ReadByte()) > 0)
                    executable.WriteByte((byte)value);
                return executable.Length == 0 ? null : Encoding.UTF8.GetString(executable.ToArray());
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        if (OperatingSystem.IsWindows())
        {
            var commandLine = Marshal.PtrToStringUni(GetCommandLineW());
            if (string.IsNullOrEmpty(commandLine))
                return null;

            // Windows treats argv[0] specially: quotes group executable path segments, and
            // backslashes are literal. Stop at the first unquoted space or tab.
            var executable = new StringBuilder();
            var quoted = false;
            foreach (var character in commandLine)
            {
                if (character == '"')
                    quoted = !quoted;
                else if (!quoted && character is ' ' or '\t')
                    break;
                else
                    executable.Append(character);
            }

            return executable.Length == 0 ? null : executable.ToString();
        }

        return null;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetCommandLineW();
}
