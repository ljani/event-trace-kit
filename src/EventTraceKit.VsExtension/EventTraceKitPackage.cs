﻿namespace EventTraceKit.VsExtension
{
    using System;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.ComponentModel.Composition;
    using System.ComponentModel.Design;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.Runtime.InteropServices;
    using Controls;
    using EnvDTE;
    using Microsoft.VisualStudio;
    using Microsoft.VisualStudio.Settings;
    using Microsoft.VisualStudio.Shell;
    using Microsoft.VisualStudio.Shell.Interop;
    using Microsoft.VisualStudio.Shell.Settings;
    using Serialization;
    using Settings;

    [PackageRegistration(UseManagedResourcesOnly = true)]
    [InstalledProductRegistration("#110", "#112", productId: "1.0", IconResourceID = 400)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(TraceLogPane))]
    [ProvideProfile(typeof(EventTraceKitProfileManager), "EventTraceKit", "General", 1001, 1002, false, DescriptionResourceID = 1003)]
    [Guid(PackageGuidString)]
    [SuppressMessage(
        "StyleCop.CSharp.DocumentationRules",
        "SA1650:ElementDocumentationMustBeSpelledCorrectly",
        Justification = "pkgdef, VS and vsixmanifest are valid VS terms")]
    public class EventTraceKitPackage : Package, IEventTraceKitSettingsService
    {
        public const string PackageGuidString = "7867DA46-69A8-40D7-8B8F-92B0DE8084D8";

        private OleMenuCommandService menuService;

        private Lazy<TraceLogPane> traceLogPane = new Lazy<TraceLogPane>(() => null);
        private GlobalSettings globalSettings;

        public EventTraceKitPackage()
        {
            AddOptionKey(EventTraceKitOptionKey);
        }

        /// <summary>
        /// Initialization of the package; this is the place where you can put all the initialization
        /// code that rely on services provided by VisualStudio.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();

            LoadSettings();

            AddMenuCommandHandlers();

            Func<IServiceProvider, TraceLogWindow> traceLogFactory = sp => {
                var dte = sp.GetService<SDTE, DTE>();
                var operationalModeProvider = new DteOperationalModeProvider(dte, this);

                var traceLog = new TraceLogWindowViewModel(this, operationalModeProvider);

                var mcs = sp.GetService<IMenuCommandService>();
                if (mcs != null)
                    traceLog.Attach(mcs);

                return new TraceLogWindow { DataContext = traceLog };
            };

            traceLogPane = new Lazy<TraceLogPane>(() => new TraceLogPane(traceLogFactory));
        }

        protected override void Dispose(bool disposing)
        {
            SaveSettings();
            base.Dispose(disposing);
        }

        private WritableSettingsStore CreateSettingsStore()
        {
            var mgr = new ShellSettingsManager(this);
            return mgr.GetWritableSettingsStore(SettingsScope.UserSettings);
        }

        private void LoadSettings()
        {
            var store = CreateSettingsStore();
            if (!store.PropertyExists("EventTraceKit", "GlobalSettings")) {
                GlobalSettings = new GlobalSettings();
                return;
            }

            var serializer = new SettingsSerializer();
            using (var stream = store.GetMemoryStream("EventTraceKit", "GlobalSettings"))
                GlobalSettings = serializer.Load<GlobalSettings>(stream);
        }

        private void SaveSettings()
        {
            if (GlobalSettings == null)
                return;

            var settingsStore = CreateSettingsStore();
            if (!settingsStore.CollectionExists("EventTraceKit"))
                settingsStore.CreateCollection("EventTraceKit");

            var serializer = new SettingsSerializer();
            using (var stream = new MemoryStream()) {
                serializer.Save(GlobalSettings, stream);
                settingsStore.SetMemoryStream("EventTraceKit", "GlobalSettings", stream);
            }
        }

        private void AddMenuCommandHandlers()
        {
            var id = new CommandID(Guids.TraceLogCmdSet, PkgCmdId.cmdidTraceLog);
            DefineCommandHandler(ShowTraceLogWindow, id);
        }

        internal void OutputString(Guid paneId, string text)
        {
            const int DO_NOT_CLEAR_WITH_SOLUTION = 0;
            const int VISIBLE = 1;

            var outputWindow = GetService(typeof(SVsOutputWindow)) as IVsOutputWindow;
            if (outputWindow == null)
                return;

            // The General pane is not created by default. We must force its creation
            if (paneId == VSConstants.OutputWindowPaneGuid.GeneralPane_guid) {
                ErrorHandler.ThrowOnFailure(
                    outputWindow.CreatePane(paneId, "General", VISIBLE, DO_NOT_CLEAR_WITH_SOLUTION));
            }

            IVsOutputWindowPane outputWindowPane;
            ErrorHandler.ThrowOnFailure(
                outputWindow.GetPane(paneId, out outputWindowPane));

            outputWindowPane?.OutputString(text);
        }

        /// <summary>
        /// Define a command handler.
        /// When the user press the button corresponding to the CommandID
        /// the EventHandler will be called.
        /// </summary>
        /// <param name="id">The CommandID (Guid/ID pair) as defined in the .vsct file</param>
        /// <param name="handler">Method that should be called to implement the command</param>
        /// <returns>The menu command. This can be used to set parameter such as the default visibility once the package is loaded</returns>
        internal OleMenuCommand DefineCommandHandler(EventHandler handler, CommandID id)
        {
            if (Zombied)
                return null;

            if (menuService == null) {
                // Get the OleCommandService object provided by the MPF; this object is the one
                // responsible for handling the collection of commands implemented by the package.
                menuService = this.GetService<IMenuCommandService, OleMenuCommandService>();
            }

            if (menuService == null)
                return null;

            var command = new OleMenuCommand(handler, id);
            menuService.AddCommand(command);
            return command;
        }

        protected override WindowPane InstantiateToolWindow(Type toolWindowType)
        {
            if (toolWindowType == typeof(TraceLogPane))
                return traceLogPane.Value;
            return base.InstantiateToolWindow(toolWindowType);
        }

        /// <summary>
        /// This method loads a localized string based on the specified resource.
        /// </summary>
        /// <param name="resourceName">Resource to load</param>
        /// <returns>String loaded for the specified resource</returns>
        internal string GetResourceString(string resourceName)
        {
            string resourceValue;
            IVsResourceManager resourceManager = (IVsResourceManager)GetService(typeof(SVsResourceManager));
            if (resourceManager == null)
                throw new InvalidOperationException("Could not get SVsResourceManager service. Make sure the package is Sited before calling this method");
            Guid packageGuid = GetType().GUID;
            int hr = resourceManager.LoadResourceString(ref packageGuid, -1, resourceName, out resourceValue);
            ErrorHandler.ThrowOnFailure(hr);
            return resourceValue;
        }

        private void ShowTraceLogWindow(object sender, EventArgs e)
        {
            this.ShowToolWindow<TraceLogPane>();
        }

        protected override void OnLoadOptions(string key, Stream stream)
        {
            if (key == EventTraceKitOptionKey) {
                //var serializer = new TraceSessionSettingsSerializer();
                //using (var reader = XmlReader.Create(stream))
                //    Settings = serializer.Read(reader);
            }
        }

        protected override void OnSaveOptions(string key, Stream stream)
        {
            if (key == EventTraceKitOptionKey) {
                //var serializer = new TraceSessionSettingsSerializer();
                //using (var writer = XmlWriter.Create(stream))
                //    Settings = serializer.Write(writer);
            }
        }

        private const string EventTraceKitOptionKey = "ETK_D438FC6445E7BF9BDEA29EA3B07";

        public GlobalSettings GlobalSettings
        {
            get { return globalSettings ?? (globalSettings = new GlobalSettings()); }
            set { globalSettings = value; }
        }

        public SolutionSettings SolutionSettings { get; set; }
    }

    public static partial class Extensions
    {
        public static WritableSettingsStore GetWritableSettingsStore(this SVsServiceProvider vsServiceProvider)
        {
            var shellSettingsManager = new ShellSettingsManager(vsServiceProvider);
            return shellSettingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);
        }
    }

    public interface IEtkGlobalSettings
    {
    }

    [Export(typeof(IEtkGlobalSettings))]
    internal sealed class EtkGlobalSettings : IEtkGlobalSettings
    {
        private const string CollectionPath = "EventTraceKit";
        private const string GlobalSettingsName = "GlobalSettings";
        private const string ErrorGetFormat = "Cannot get setting {0}";
        private const string ErrorSetFormat = "Cannot set setting {0}";

        private readonly WritableSettingsStore settingsStore;
        private GlobalSettings globalSettings;

        [ImportingConstructor]
        internal EtkGlobalSettings(
            SVsServiceProvider vsServiceProvider)
            : this(vsServiceProvider.GetWritableSettingsStore())
        {
        }

        internal EtkGlobalSettings(WritableSettingsStore settingsStore)
        {
            this.settingsStore = settingsStore;
        }

        private void Report(string format, Exception exception)
        {
            // FIXME
        }

        private void EnsureCollectionExists()
        {
            if (!settingsStore.CollectionExists(CollectionPath))
                settingsStore.CreateCollection(CollectionPath);
        }

        private string GetString(string propertyName, string defaultValue)
        {
            EnsureCollectionExists();
            try {
                if (!settingsStore.PropertyExists(CollectionPath, propertyName))
                    return defaultValue;
                return settingsStore.GetString(CollectionPath, propertyName);
            } catch (Exception ex) {
                Report(string.Format(ErrorGetFormat, propertyName), ex);
                return defaultValue;
            }
        }

        private void SetString(string propertyName, string value)
        {
            EnsureCollectionExists();
            try {
                settingsStore.SetString(CollectionPath, propertyName, value);
            } catch (Exception ex) {
                Report(string.Format(ErrorSetFormat, propertyName), ex);
            }
        }

        private T GetObject<T>(string propertyName, Func<T> defaultValue)
        {
            EnsureCollectionExists();
            try {
                if (!settingsStore.PropertyExists(CollectionPath, propertyName))
                    return defaultValue();
                var serializer = new SettingsSerializer();
                var stream = settingsStore.GetMemoryStream(CollectionPath, propertyName);
                using (stream)
                    return serializer.Load<T>(stream);
            } catch (Exception ex) {
                Report(string.Format(ErrorGetFormat, propertyName), ex);
                return defaultValue();
            }
        }

        private void SetObject<T>(string propertyName, T value)
        {
            EnsureCollectionExists();
            try {
                var serializer = new SettingsSerializer();
                var stream = serializer.SaveToStream(value);
                settingsStore.SetMemoryStream(CollectionPath, propertyName, stream);
            } catch (Exception ex) {
                Report(string.Format(ErrorSetFormat, propertyName), ex);
            }
        }

        public GlobalSettings GlobalSettings
        {
            get { return globalSettings ?? (GlobalSettings = GetObject(GlobalSettingsName, () => new GlobalSettings())); }
            set
            {
                globalSettings = value;
                SetObject(GlobalSettingsName, value);
            }
        }
    }

    [SerializedShape(typeof(Settings.GlobalSettings))]
    public class GlobalSettings
    {
        public ObservableCollection<HdvViewModelPreset> ModifiedPresets { get; } =
            new ObservableCollection<HdvViewModelPreset>();

        public ObservableCollection<HdvViewModelPreset> PersistedPresets { get; } =
            new ObservableCollection<HdvViewModelPreset>();

        public Guid ActiveSession { get; set; }

        public ObservableCollection<TraceSessionSettingsViewModel> Sessions { get; } =
            new ObservableCollection<TraceSessionSettingsViewModel>();
    }

    public class SolutionSettings
    {
        public Collection<ProfilePreset> ModifiedPresets { get; set; }

        public Collection<ProfilePreset> PersistedPresets { get; set; }

        public Guid ActiveSession { get; set; }

        public Collection<TraceSession> Sessions { get; set; }
    }

    public interface IEventTraceKitSettingsService
    {
        GlobalSettings GlobalSettings { get; }
        SolutionSettings SolutionSettings { get; }
    }

    [ComVisible(true)]
    [Guid("9619B7BF-69E2-4F5F-B95C-F2E6EDA02205")]
    public sealed class EventTraceKitProfileManager : Component, IProfileManager
    {
        private EventTraceKitPackage package;

        public override ISite Site
        {
            get { return base.Site; }
            set
            {
                base.Site = value;
                package = Site?.GetService<EventTraceKitPackage>();
            }
        }

        public void LoadSettingsFromStorage()
        {
        }

        public void LoadSettingsFromXml(IVsSettingsReader reader)
        {
            if (package == null)
                return;

            string xml;
            if (reader.ReadSettingXmlAsString("GlobalSettings", out xml) != VSConstants.S_OK)
                return;

            var serializer = new SettingsSerializer();
            try {
                package.GlobalSettings = serializer.LoadFromString<GlobalSettings>(xml);
            } catch {
            }
        }

        public void ResetSettings()
        {
        }

        public void SaveSettingsToStorage()
        {
        }

        public void SaveSettingsToXml(IVsSettingsWriter writer)
        {
            var settings = package?.GlobalSettings;
            if (settings == null)
                return;

            var serializer = new SettingsSerializer();
            var xml = serializer.SaveToString(settings);
            writer.WriteSettingXmlFromString(xml);
        }
    }
}
