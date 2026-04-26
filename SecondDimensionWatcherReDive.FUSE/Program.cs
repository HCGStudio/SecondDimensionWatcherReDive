using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using SecondDimensionWatcherReDive.FUSE.Client;
using SecondDimensionWatcherReDive.FUSE.Configuration;
using SecondDimensionWatcherReDive.FUSE.Fs;
using SecondDimensionWatcherReDive.FUSE.Native;

namespace SecondDimensionWatcherReDive.FUSE;

internal static unsafe class Program
{
    private const string Version = "0.1.0";

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return args.Length == 0 ? 2 : 0;
        }

        if (args[0] is "--version" or "-V")
        {
            Console.WriteLine($"sdwfuse {Version}");
            Console.WriteLine("Licensed under the Apache License 2.0.");
            Console.WriteLine("Dynamically links libfuse3 (LGPL-2.1-only) — see THIRD_PARTY_NOTICES.md.");
            return 0;
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Console.Error.WriteLine("sdwfuse only runs on Linux (libfuse3 is required).");
            return 2;
        }

        if (args[0] != "mount")
        {
            Console.Error.WriteLine($"Unknown command '{args[0]}'.");
            PrintUsage();
            return 2;
        }

        FuseClientOptions options;
        try
        {
            options = ParseMountArgs(args.AsSpan(1));
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"sdwfuse: {ex.Message}");
            PrintUsage();
            return 2;
        }

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(c =>
            {
                c.IncludeScopes = false;
                c.SingleLine = true;
                c.TimestampFormat = "HH:mm:ss ";
            });
            builder.SetMinimumLevel(options.DebugFuse ? LogLevel.Debug : LogLevel.Information);
        });

        var clientLogger = loggerFactory.CreateLogger<SdwClient>();
        var fsLogger = loggerFactory.CreateLogger<SdwFuseFs>();
        var programLogger = loggerFactory.CreateLogger("sdwfuse");

        using var client = new SdwClient(options.ServerUrl, options.Username, options.Password, options.UserAgent, clientLogger);
        var cache = new AttrCache(options.CacheTtl);
        var fs = new SdwFuseFs(client, cache, fsLogger);
        fs.InstallAsCurrent();

        try
        {
            // Probe credentials early — a failure here is much friendlier than a
            // mount that comes up empty because every getattr returns -EACCES.
            var root = client.StatAsync("/", CancellationToken.None).GetAwaiter().GetResult();
            if (root is null)
            {
                programLogger.LogError("Server returned 404 for the VFS root. Verify --server URL.");
                return 3;
            }
        }
        catch (SdwUnauthorizedException ex)
        {
            programLogger.LogError(ex, "Authentication failed against {Url}", options.ServerUrl);
            return 4;
        }
        catch (Exception ex)
        {
            programLogger.LogError(ex, "Failed to reach SDW server at {Url}", options.ServerUrl);
            return 5;
        }

        var fuseArgs = BuildFuseArgs(options);
        return InvokeFuseMain(fuseArgs, programLogger);
    }

    private static int InvokeFuseMain(string[] fuseArgs, ILogger logger)
    {
        var operations = SdwFuseFs.BuildOperations();

        var argvBuffers = new List<byte[]>(fuseArgs.Length);
        foreach (var arg in fuseArgs)
        {
            var bytes = new byte[Encoding.UTF8.GetByteCount(arg) + 1];
            Encoding.UTF8.GetBytes(arg, bytes);
            argvBuffers.Add(bytes);
        }

        var pinned = new GCHandle[argvBuffers.Count];
        var argv = stackalloc byte*[argvBuffers.Count];
        try
        {
            for (var i = 0; i < argvBuffers.Count; i++)
            {
                pinned[i] = GCHandle.Alloc(argvBuffers[i], GCHandleType.Pinned);
                argv[i] = (byte*)pinned[i].AddrOfPinnedObject();
            }

            logger.LogInformation("Mounting via libfuse3 with args: {Args}", string.Join(' ', fuseArgs));
            var rc = LibFuse.fuse_main_real(fuseArgs.Length, argv, &operations, (nuint)sizeof(FuseOperations), null);
            logger.LogInformation("libfuse3 exited with code {Rc}", rc);
            return rc;
        }
        catch (DllNotFoundException ex)
        {
            logger.LogError(ex, "libfuse3 is not installed. On Debian/Ubuntu: apt install fuse3 libfuse3-3");
            return 6;
        }
        finally
        {
            foreach (var h in pinned) if (h.IsAllocated) h.Free();
        }
    }

    private static string[] BuildFuseArgs(FuseClientOptions options)
    {
        var list = new List<string> { "sdwfuse", options.MountPoint };
        var mountOpts = new List<string> { "ro", "fsname=sdwfuse", "subtype=sdw" };
        if (options.AllowOther) mountOpts.Add("allow_other");
        list.Add("-o");
        list.Add(string.Join(',', mountOpts));
        if (options.Foreground) list.Add("-f");
        if (options.DebugFuse) list.Add("-d");
        return list.ToArray();
    }

    private static FuseClientOptions ParseMountArgs(ReadOnlySpan<string> args)
    {
        string? mountPoint = null;
        string? server = Environment.GetEnvironmentVariable("SDW_FUSE_SERVER");
        string? username = Environment.GetEnvironmentVariable("SDW_FUSE_USERNAME");
        string? password = Environment.GetEnvironmentVariable("SDW_FUSE_PASSWORD");
        var cacheSeconds = 5;
        var allowOther = false;
        var foreground = false;
        var debug = false;
        var userAgent = $"sdwfuse/{Version}";

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--server": server = Next(args, ref i, a); break;
                case "--username": username = Next(args, ref i, a); break;
                case "--password": password = Next(args, ref i, a); break;
                case "--cache-ttl":
                    if (!int.TryParse(Next(args, ref i, a), out cacheSeconds) || cacheSeconds < 0)
                        throw new ArgumentException("--cache-ttl must be a non-negative integer.");
                    break;
                case "--user-agent": userAgent = Next(args, ref i, a); break;
                case "--allow-other": allowOther = true; break;
                case "--foreground" or "-f": foreground = true; break;
                case "--debug" or "-d": debug = true; break;
                default:
                    if (a.StartsWith('-')) throw new ArgumentException($"Unknown option '{a}'.");
                    if (mountPoint is not null) throw new ArgumentException($"Unexpected positional argument '{a}'.");
                    mountPoint = a;
                    break;
            }
        }

        if (mountPoint is null) throw new ArgumentException("Mount point is required.");
        if (string.IsNullOrWhiteSpace(server)) throw new ArgumentException("--server (or SDW_FUSE_SERVER) is required.");
        if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("--username (or SDW_FUSE_USERNAME) is required.");
        if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("--password (or SDW_FUSE_PASSWORD) is required.");
        if (!Uri.TryCreate(server, UriKind.Absolute, out var serverUrl)
            || serverUrl.Scheme is not ("http" or "https"))
            throw new ArgumentException($"--server must be an http/https URL, got '{server}'.");

        return new FuseClientOptions(serverUrl, username, password, Path.GetFullPath(mountPoint),
            TimeSpan.FromSeconds(cacheSeconds), allowOther, foreground, debug, userAgent);
    }

    private static string Next(ReadOnlySpan<string> args, ref int i, string flag)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"{flag} expects a value.");
        return args[++i];
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            sdwfuse — mount the SDW virtual filesystem read-only via libfuse3

            Usage:
              sdwfuse mount <mountpoint> --server <url> --username <name> --password <token>
                                         [--cache-ttl 5] [--allow-other] [--foreground] [--debug]
                                         [--user-agent <ua>]
              sdwfuse --version

            Environment fallbacks: SDW_FUSE_SERVER, SDW_FUSE_USERNAME, SDW_FUSE_PASSWORD.

            Examples:
              sdwfuse mount /mnt/sdw --server http://sdw:5097 \\
                  --username sdw-AAAA --password XXXX --foreground
              fusermount3 -u /mnt/sdw      # unmount
            """);
    }
}
