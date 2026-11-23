using March7thHoney.Command;
using March7thHoney.Command.Command;
using March7thHoney.Configuration;
using March7thHoney.Data;
using March7thHoney.Database;
using March7thHoney.Database.Migrations;
using March7thHoney.Enums;
using March7thHoney.GameServer.Command;
using March7thHoney.GameServer.Server;
using March7thHoney.GameServer.Server.Packet;
using March7thHoney.Internationalization;
using March7thHoney.Kcp;
using March7thHoney.Program.Handbook;
using March7thHoney.Util;
using March7thHoney.WebServer;
using March7thHoney.WebServer.Server;
using March7thHoney.WebServer.Services;
using System.Globalization;
using System.Runtime.InteropServices;

namespace March7thHoney.Program.Program;

public class EntryPoint
{
    private static readonly Logger Logger = new("Program");
    public static readonly DatabaseHelper DatabaseHelper = new();
    public static readonly CommandManager CommandManager = new();
    private static PosixSignalRegistration? _sigtermRegistration;

    private const int StartupFailedExitCode = 1;
    private static int _cleanupStarted;

    public static async Task<int> Main(string[] args)
    {
        ServerConsole.InitConsole();
        ServerConsole.RedrawInput(ServerConsole.Input);
        // Capture log output from the very first line so the admin console's log viewer has the boot history.
        AdminLogBuffer.Attach();
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Logger.Info(I18NManager.Translate("Server.ServerInfo.Shutdown"));
            PerformCleanup();
        };
        AppDomain.CurrentDomain.UnhandledException += (obj, arg) =>
        {
            Logger.Error(I18NManager.Translate("Server.ServerInfo.UnhandledException", obj.GetType().Name),
                (Exception)arg.ExceptionObject);
            Logger.Info(I18NManager.Translate("Server.ServerInfo.Shutdown"));
            PerformCleanup();
            Environment.Exit(1);
        };

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            Logger.Info(I18NManager.Translate("Server.ServerInfo.CancelKeyPressed"));
            eventArgs.Cancel = true;
            Environment.Exit(0);
        };
        // .NET 10 dropped ProcessExit on window-close/logoff/shutdown; hook the Win32 console control events so the DB save runs (Windows-only kernel32 P/Invoke).
        if (OperatingSystem.IsWindows())
            SetConsoleCtrlHandler(ConsoleCtrlHandlerKeepAlive, true);
        else if (OperatingSystem.IsLinux())
            _sigtermRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSigTerm);
        var time = DateTime.Now;

        // Load the config
        try
        {
            ConfigManager.LoadConfig();
        }
        catch (Exception e)
        {
            // Uses the log directory of the configuration in memory: the default unless Config.json was read before
            // the failure.
            TryOpenLogFiles();
            Logger.Error(
                I18NManager.Translate("Server.ServerInfo.FailedToLoadItem", I18NManager.Translate("Word.Config")), e);
            WaitForEnterIfInteractive();
            return StartupFailedExitCode;
        }

        if (!TryOpenLogFiles())
        {
            WaitForEnterIfInteractive();
            return StartupFailedExitCode;
        }

        // Load the language
        try
        {
            I18NManager.LoadLanguage();
        }
        catch (Exception e)
        {
            Logger.Error(
                I18NManager.Translate("Server.ServerInfo.FailedToLoadItem", I18NManager.Translate("Word.Language")), e);
            ConfigManager.LogStartupNotices();
            WaitForEnterIfInteractive();
            return StartupFailedExitCode;
        }

        // Starting logs after i18n is ready
        Logger.Info(I18NManager.Translate("Server.ServerInfo.StartingServer"));
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem", I18NManager.Translate("Word.Config")));
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem", I18NManager.Translate("Word.Language")));
        ConfigManager.Logger.Info(
            I18NManager.Translate("Server.ServerInfo.CurrentVersion", GameConstants.GAME_VERSION));
        ConfigManager.LogStartupNotices();
        // Initialize the database
        Task databaseInitTask;
        try
        {
            databaseInitTask = Task.Run(DatabaseHelper.Initialize);

            while (!DatabaseHelper.LoadAccount)
            {
                if (databaseInitTask.IsFaulted)
                    databaseInitTask.GetAwaiter().GetResult();

                await Task.Delay(100);
            }

            Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItem",
                I18NManager.Translate("Word.DatabaseAccount")));
        }
        catch (Exception e)
        {
            Logger.Error(
                I18NManager.Translate("Server.ServerInfo.FailedToLoadItem", I18NManager.Translate("Word.Database")), e);
            WaitForEnterIfInteractive();
            return StartupFailedExitCode;
        }

        // Warm protobuf descriptors at startup so the first login doesn't pay the initialization cost.
        var protoDescriptorWarmUp = Task.Run(BuildProtoDescriptors);

        HandlerManager.Init();
        if (ConfigManager.Config.GameServer.UsePacketEncryption)
        {
            Crypto.ClientSecretKey = Crypto.InitEc2b();
        }

        Logger.Warn(I18NManager.Translate("Server.ServerInfo.WaitForAllDone"));

        var loadedFromResources = false;
        if (ConfigManager.Config.ServerOption.ServerConfig.RunGateway)
        {
            try
            {
                loadedFromResources = await GameDataStartup.LoadAsync();
            }
            catch (Exception e)
            {
                Logger.Error(
                    I18NManager.Translate("Server.ServerInfo.FailedToLoadItem", I18NManager.Translate("Word.GameData")),
                    e);
                WaitForEnterIfInteractive();
                return StartupFailedExitCode;
            }
        }

        // Register the command handlers
        try
        {
            CommandManager.RegisterCommands();
        }
        catch (Exception e)
        {
            Logger.Error(
                I18NManager.Translate("Server.ServerInfo.FailedToInitializeItem",
                    I18NManager.Translate("Word.Command")), e);
            WaitForEnterIfInteractive();
            return StartupFailedExitCode;
        }

        CommandExecutor.OnRunCommand += (sender, e) => { CommandManager.HandleCommand(e, sender); };

        MuipManager.CommandHandler = CommandManager.HandleCommandAsync;
        MuipManager.CommandCatalogProvider = CommandManager.BuildCatalog;

        // Generate handbooks only when this startup reads raw resources.
        if (loadedFromResources)
        {
            var handbookThread = new Thread(() => HandbookGenerator.GenerateAll(CommandManager.CommandInfo.Values))
            {
                IsBackground = true,
                Name = "HandbookGenerator",
                Priority = ThreadPriority.Lowest
            };
            handbookThread.Start();
        }

        if (!DatabaseHelper.LoadAllData)
        {
            Logger.Warn(I18NManager.Translate("Server.ServerInfo.WaitForAllDone"));
            try
            {
                while (!DatabaseHelper.LoadAllData) // wait for all data to be loaded
                {
                    if (databaseInitTask.IsFaulted)
                        databaseInitTask.GetAwaiter().GetResult();

                    await Task.Delay(100);
                }
            }
            catch (Exception e)
            {
                Logger.Error(
                    I18NManager.Translate("Server.ServerInfo.FailedToLoadItem",
                        I18NManager.Translate("Word.Database")), e);
                WaitForEnterIfInteractive();
                return StartupFailedExitCode;
            }

            Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItem", I18NManager.Translate("Word.Database")));
        }

        AuthRateLimiter.Initialize();

        ServerUtils.InitializeHandlers();

        FormalAvatarMigration.Run(DatabaseHelper);

        if (args.Contains("--upgrade-database")) DatabaseHelper.UpgradeDatabase();

        await protoDescriptorWarmUp;
        // The dispatch and game ports open only now, so no client reaches a server whose game data, commands, mission
        // handlers or save migrations are still loading.
        WebProgram.Main([], GetConfig().HttpServer.Port, GetConfig().HttpServer.GetBindDisplayAddress());
        Logger.Info(I18NManager.Translate("Server.ServerInfo.ServerRunning", I18NManager.Translate("Word.Dispatch"),
            GetConfig().HttpServer.GetDisplayAddress()));

        // The packet name maps back logging and the fallback replies, so they exist before any connection.
        March7thHoneyConnection.InitPacketNameMap();

        if (ConfigManager.Config.ServerOption.ServerConfig.RunGateway)
        {
            March7thHoneyListener.CreateConnection = (conversation, remote) => new Connection(conversation, remote);
            March7thHoneyListener.StartListener();
        }

        var elapsed = DateTime.Now - time;
        Logger.Info(I18NManager.Translate("Server.ServerInfo.ServerStarted",
            Math.Round(elapsed.TotalSeconds, 2).ToString(CultureInfo.InvariantCulture)));

        if (GetConfig().ServerOption.EnableMission)
            Logger.Warn(I18NManager.Translate("Server.ServerInfo.MissionEnabled"));

        await LifecycleNotifier.NotifyStartedAsync(GameConstants.GAME_VERSION);

        ServerConsole.OnConsoleExecuteCommand += command =>
        {
            CommandManager.HandleCommand(command, new ConsoleCommandSender(Logger));
            ServerConsole.RedrawInput(ServerConsole.Input);
        };

        ServerConsole.ListenConsole();
        GC.KeepAlive(_sigtermRegistration);
        return 0;
    }

    /// <summary>
    ///     Keeps a failed startup's console window open until the operator presses Enter. A service, a container or
    ///     any run with redirected input exits straight away so its supervisor sees the exit code.
    /// </summary>
    private static void WaitForEnterIfInteractive()
    {
        if (Console.IsInputRedirected || !Environment.UserInteractive) return;
        Console.ReadLine();
    }

    /// <summary>Opens this run's log files in the configured log directory, packing the earlier runs' logs.</summary>
    private static bool TryOpenLogFiles()
    {
        try
        {
            LogFiles.RotateAndOpen(GetConfig().Path.LogPath);
            return true;
        }
        catch (Exception e)
        {
            Logger.Error($"Failed to open the log files in {GetConfig().Path.LogPath}", e);
            return false;
        }
    }

    /// <summary>
    ///     Builds the descriptor of every generated .proto file. The active protocol is split across many .proto files,
    ///     each built on the first access to one of its messages; without this the first login pays for them while the
    ///     client waits on its replies. The build generates the list, so no reflection is involved (NativeAOT-safe).
    /// </summary>
    private static void BuildProtoDescriptors()
    {
        _ = March7thHoney.Proto.ProtocolDescriptors.BuildAll();
    }

    public static ConfigContainer GetConfig()
    {
        return ConfigManager.Config;
    }

    private delegate bool ConsoleCtrlHandlerRoutine(uint ctrlType);

    private static readonly ConsoleCtrlHandlerRoutine ConsoleCtrlHandlerKeepAlive = OnConsoleCtrl;

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandlerRoutine handler, bool add);

    // CTRL_CLOSE(2)/LOGOFF(5)/SHUTDOWN(6): exit cleanly so PerformCleanup saves; Ctrl+C/Break stay with CancelKeyPress.
    private static bool OnConsoleCtrl(uint ctrlType)
    {
        if (ctrlType is 2 or 5 or 6) Environment.Exit(0);
        return false;
    }

    private static void OnSigTerm(PosixSignalContext context)
    {
        context.Cancel = true;
        Environment.Exit(0);
    }

    private static void PerformCleanup()
    {
        // ProcessExit and the unhandled-exception handler can both reach this; save and notify only once.
        if (Interlocked.Exchange(ref _cleanupStarted, 1) != 0) return;
        var notificationTask = LifecycleNotifier.NotifyMaintenanceAsync();
        // Stopping a live connection on shutdown can throw (disposed CancellationTokenSource race); never let it skip the save.
        try { March7thHoneyListener.GetSnapshot().ForEach(x => x.Stop()); }
        catch (Exception e) { Logger.Error(I18NManager.Translate("Server.Log.StopConnectionsFailed"), e); }
        DatabaseHelper.SaveDatabase();
        notificationTask.GetAwaiter().GetResult();
    }
}
