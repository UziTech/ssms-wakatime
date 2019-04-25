﻿using System;
using System.ComponentModel.Design;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Timers;
using Microsoft.VisualStudio.Shell;
using WakaTime.Forms;
using Task = System.Threading.Tasks.Task;
using System.Collections.Concurrent;
using System.Collections;
using System.Web.Script.Serialization;
using EnvDTE;

namespace WakaTime
{
    [Guid(GuidList.GuidWakaTimePkgString)]
    [PackageRegistration(UseManagedResourcesOnly = true)]
    [ProvideAutoLoad("ADFC4E64-0397-11D1-9F4E-00A0C911004F")]    
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    public sealed class WakaTimePackage : Package
    {
        #region Fields
        internal static ConfigFile Config;
        private static SettingsForm _settingsForm;

        private DocumentEvents _docEvents;
        private WindowEvents _windowEvents;
        private SolutionEvents _solutionEvents;
        private DTEEvents _dteEvents;

        public static DTE ObjDte;

        private static readonly ConcurrentQueue<Heartbeat> HeartbeatQueue = new ConcurrentQueue<Heartbeat>();
        private static readonly Timer Timer = new Timer();

        private static readonly PythonCliParameters PythonCliParameters = new PythonCliParameters();
        private static string _lastFile;
        private DateTime _lastHeartbeat = DateTime.UtcNow.AddMinutes(-3);
        private static string _solutionName = string.Empty;
        private const int HeartbeatFrequency = 2; // minutes        
        #endregion

        #region Startup/Cleanup        
        protected override void Initialize()
        {
            base.Initialize();

            AddSkipLoading();

            // Load config file
            Config = new ConfigFile();
            Config.Read();

            ObjDte = (DTE)GetService(typeof(DTE));
            _dteEvents = ObjDte.Events.DTEEvents;
            _dteEvents.OnStartupComplete += OnOnStartupComplete;

            // Try force initializing in brackground
            Logger.Debug("Initializing in background thread.");
            Task.Run(() =>
            {
                InitializeAsync();                
            });
        }        

        private void InitializeAsync()
        {
            try
            {
                Logger.Info($"Initializing WakaTime v{Constants.PluginVersion}");

                // VisualStudio Object                
                _docEvents = ObjDte.Events.DocumentEvents;
                _windowEvents = ObjDte.Events.WindowEvents;
                _solutionEvents = ObjDte.Events.SolutionEvents;

                // Settings Form
                _settingsForm = new SettingsForm();
                _settingsForm.ConfigSaved += SettingsFormOnConfigSaved;

                try
                {
                    // Make sure python is installed
                    if (!Dependencies.IsPythonInstalled())
                    {
                        Dependencies.DownloadAndInstallPython();
                    }

                    if (!Dependencies.DoesCliExist() || !Dependencies.IsCliUpToDate())
                    {
                        Dependencies.DownloadAndInstallCli();
                    }
                }
                catch (WebException ex)
                {
                    Logger.Error("Are you behind a proxy? Try setting a proxy in WakaTime Settings with format https://user:pass@host:port. Exception Traceback:", ex);
                }
                catch (Exception ex)
                {
                    Logger.Error("Error detecting dependencies. Exception Traceback:", ex);
                }

                // Add our command handlers for menu (commands must exist in the .vsct file)
                if (GetService(typeof(IMenuCommandService)) is OleMenuCommandService mcs)
                {
                    // Create the command for the menu item.
                    var menuCommandId = new CommandID(GuidList.GuidWakaTimeCmdSet, (int)PkgCmdIdList.UpdateWakaTimeSettings);
                    var menuItem = new MenuCommand(MenuItemCallback, menuCommandId);
                    mcs.AddCommand(menuItem);
                }

                // setup event handlers
                _docEvents.DocumentOpened += DocEventsOnDocumentOpened;
                _docEvents.DocumentSaved += DocEventsOnDocumentSaved;
                _windowEvents.WindowActivated += WindowEventsOnWindowActivated;
                _solutionEvents.Opened += SolutionEventsOnOpened;

                // setup timer to process queued heartbeats every 8 seconds
                Timer.Interval = 1000 * 8;
                Timer.Elapsed += ProcessHeartbeats;
                Timer.Start();

                Logger.Info($"Finished initializing WakaTime v{Constants.PluginVersion}");
            }
            catch (Exception ex)
            {
                Logger.Error("Error Initializing WakaTime", ex);
            }
        }

        // Call this method from the Initialize method
        // to add the SkipLoading value back to the registry
        // 2 seconds after it’s removed by SSMS
        private void AddSkipLoading()
        {
            var timer = new Timer(2000);
            timer.Elapsed += (sender, args) =>
            {
                timer.Stop();

                var myPackage = UserRegistryRoot.CreateSubKey($@"Packages\{{{GuidList.GuidWakaTimePkgString}}}");
                myPackage?.SetValue("SkipLoading", 1);
            };
            timer.Start();
        }
        #endregion

        #region Event Handlers
        private void DocEventsOnDocumentOpened(Document document)
        {
            try
            {
                HandleActivity(document.FullName, false);
            }
            catch (Exception ex)
            {
                Logger.Error("DocEventsOnDocumentOpened", ex);
            }
        }

        private void DocEventsOnDocumentSaved(Document document)
        {
            try
            {
                HandleActivity(document.FullName, true);
            }
            catch (Exception ex)
            {
                Logger.Error("DocEventsOnDocumentSaved", ex);
            }
        }

        private void WindowEventsOnWindowActivated(Window gotFocus, Window lostFocus)
        {
            try
            {
                var document = ObjDte.ActiveWindow.Document;
                if (document != null)
                    HandleActivity(document.FullName, false);
            }
            catch (Exception ex)
            {
                Logger.Error("WindowEventsOnWindowActivated", ex);
            }
        }

        private static void SolutionEventsOnOpened()
        {
            try
            {
                _solutionName = ObjDte.Solution.FullName;
            }
            catch (Exception ex)
            {
                Logger.Error("SolutionEventsOnOpened", ex);
            }
        }

        private static void OnOnStartupComplete()
        {
            // Prompt for api key if not already set
            if (string.IsNullOrEmpty(Config.ApiKey))
                PromptApiKey();
        }
        #endregion

        #region Methods
        private void HandleActivity(string currentFile, bool isWrite)
        {
            if (currentFile == null)
                return;

            var now = DateTime.UtcNow;

            if (!isWrite && _lastFile != null && !EnoughTimePassed(now) && currentFile.Equals(_lastFile))
                return;

            _lastFile = currentFile;
            _lastHeartbeat = now;

            AppendHeartbeat(currentFile, isWrite, now);
        }

        private static void AppendHeartbeat(string fileName, bool isWrite, DateTime time)
        {
            var h = new Heartbeat
            {
                entity = fileName,
                timestamp = ToUnixEpoch(time),
                is_write = isWrite,
                project = GetProjectName()
            };
            HeartbeatQueue.Enqueue(h);
        }

        private static void ProcessHeartbeats(object sender, ElapsedEventArgs e)
        {
            Task.Run(() =>
            {
                ProcessHeartbeats();
            });
        }

        private static void ProcessHeartbeats()
        {
            var pythonBinary = Dependencies.GetPython();
            if (pythonBinary != null)
            {
                // get first heartbeat from queue
                var gotOne = HeartbeatQueue.TryDequeue(out var heartbeat);
                if (!gotOne)
                    return;

                // remove all extra heartbeats from queue
                var extraHeartbeats = new ArrayList();
                while (HeartbeatQueue.TryDequeue(out var h))
                    extraHeartbeats.Add(new Heartbeat(h));
                var hasExtraHeartbeats = extraHeartbeats.Count > 0;

                PythonCliParameters.Key = Config.ApiKey;
                PythonCliParameters.Plugin =
                    $"{Constants.EditorName}/{Constants.EditorVersion} {Constants.PluginName}/{Constants.PluginVersion}";
                PythonCliParameters.File = heartbeat.entity;
                PythonCliParameters.Time = heartbeat.timestamp;
                PythonCliParameters.IsWrite = heartbeat.is_write;
                PythonCliParameters.Project = heartbeat.project;
                PythonCliParameters.HasExtraHeartbeats = hasExtraHeartbeats;

                string extraHeartbeatsJson = null;
                if (hasExtraHeartbeats)
                    extraHeartbeatsJson = new JavaScriptSerializer().Serialize(extraHeartbeats);

                var process = new RunProcess(pythonBinary, PythonCliParameters.ToArray());
                if (Config.Debug)
                {
                    Logger.Debug(
                        $"[\"{pythonBinary}\", \"{string.Join("\", \"", PythonCliParameters.ToArray(true))}\"]");
                    process.Run(extraHeartbeatsJson);
                    if (!string.IsNullOrEmpty(process.Output))
                        Logger.Debug(process.Output);
                    if (!string.IsNullOrEmpty(process.Error))
                        Logger.Debug(process.Error);
                }
                else
                    process.RunInBackground(extraHeartbeatsJson);

                if (!process.Success)
                {
                    Logger.Error("Could not send heartbeat.");
                    if (!string.IsNullOrEmpty(process.Output))
                        Logger.Error(process.Output);
                    if (!string.IsNullOrEmpty(process.Error))
                        Logger.Error(process.Error);
                }
            }
            else
                Logger.Error("Could not send heartbeat because python is not installed");
        }

        private bool EnoughTimePassed(DateTime now)
        {
            return _lastHeartbeat < now.AddMinutes(-1 * HeartbeatFrequency);
        }

        private static void SettingsFormOnConfigSaved(object sender, EventArgs eventArgs)
        {
            Config.Read();
        }

        private static void MenuItemCallback(object sender, EventArgs e)
        {
            try
            {
                SettingsPopup();
            }
            catch (Exception ex)
            {
                Logger.Error("MenuItemCallback", ex);
            }
        }

        private static void PromptApiKey()
        {
            Logger.Info("Please input your api key into the wakatime window.");
            var form = new ApiKeyForm();
            form.ShowDialog();
        }

        private static void SettingsPopup()
        {
            _settingsForm.ShowDialog();
        }

        private static string GetProjectName()
        {
            return !string.IsNullOrEmpty(_solutionName)
                ? Path.GetFileNameWithoutExtension(_solutionName)
                : (ObjDte.Solution != null && !string.IsNullOrEmpty(ObjDte.Solution.FullName))
                    ? Path.GetFileNameWithoutExtension(ObjDte.Solution.FullName)
                    : string.Empty;
        }

        private static string ToUnixEpoch(DateTime date)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            var timestamp = date - epoch;
            var seconds = Convert.ToInt64(Math.Floor(timestamp.TotalSeconds));
            var milliseconds = timestamp.ToString("ffffff");
            return $"{seconds}.{milliseconds}";
        }

        public static WebProxy GetProxy()
        {
            WebProxy proxy = null;

            try
            {
                if (string.IsNullOrEmpty(Config.Proxy))
                {
                    Logger.Debug("No proxy will be used. It's either not set or badly formatted.");
                    return null;
                }

                var proxyStr = Config.Proxy;

                // Regex that matches proxy address with authentication
                var regProxyWithAuth = new Regex(@"\s*(https?:\/\/)?([^\s:]+):([^\s:]+)@([^\s:]+):(\d+)\s*");
                var match = regProxyWithAuth.Match(proxyStr);

                if (match.Success)
                {
                    var username = match.Groups[2].Value;
                    var password = match.Groups[3].Value;
                    var address = match.Groups[4].Value;
                    var port = match.Groups[5].Value;

                    var credentials = new NetworkCredential(username, password);
                    proxy = new WebProxy(string.Join(":", address, port), true, null, credentials);

                    Logger.Debug("A proxy with authentication will be used.");
                    return proxy;
                }

                // Regex that matches proxy address and port(no authentication)
                var regProxy = new Regex(@"\s*(https?:\/\/)?([^\s@]+):(\d+)\s*");
                match = regProxy.Match(proxyStr);

                if (match.Success)
                {
                    var address = match.Groups[2].Value;
                    var port = int.Parse(match.Groups[3].Value);

                    proxy = new WebProxy(address, port);

                    Logger.Debug("A proxy will be used.");
                    return proxy;
                }

                Logger.Debug("No proxy will be used. It's either not set or badly formatted.");
            }
            catch (Exception ex)
            {
                Logger.Error("Exception while parsing the proxy string from WakaTime config file. No proxy will be used.", ex);
            }

            return proxy;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (Timer == null) return;

            _docEvents.DocumentOpened -= DocEventsOnDocumentOpened;
            _docEvents.DocumentSaved -= DocEventsOnDocumentSaved;
            _windowEvents.WindowActivated -= WindowEventsOnWindowActivated;
            _solutionEvents.Opened -= SolutionEventsOnOpened;

            Timer.Stop();
            Timer.Elapsed -= ProcessHeartbeats;
            Timer.Dispose();

            // make sure the queue is empty	
            ProcessHeartbeats();
        }

        public static class CoreAssembly
        {
            private static readonly Assembly Reference = typeof(CoreAssembly).Assembly;
            public static readonly Version Version = Reference.GetName().Version;
        }
        #endregion        
    }
}
