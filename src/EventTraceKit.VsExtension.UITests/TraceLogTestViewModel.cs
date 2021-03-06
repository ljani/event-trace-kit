namespace EventTraceKit.VsExtension.UITests
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Input;
    using EventTraceKit.EventTracing.Schema;
    using EventTraceKit.VsExtension;
    using EventTraceKit.VsExtension.Settings;
    using EventTraceKit.VsExtension.Views;
    using EventTraceKit.VsExtension.Views.PresetManager;
    using Microsoft.VisualStudio.PlatformUI;
    using Microsoft.VisualStudio.Threading;
    using Task = System.Threading.Tasks.Task;

    public class TraceLogTestViewModel : TraceLogToolViewModel
    {
        private string selectedTheme;

        public TraceLogTestViewModel(ITraceController traceController)
            : base(new StubGlobalSettings(), traceController, x => new TraceSettingsViewModel(new StubSettingsContext()))
        {
            StartCommand = new AsyncDelegateCommand(Start, CanStart);
            StopCommand = new AsyncDelegateCommand(Stop, CanStop);
            ClearCommand = new AsyncDelegateCommand(Clear);
            ConfigureCommand = new DelegateCommand(Configure);
            OpenViewEditorCommand = new DelegateCommand(OpenViewEditor);
            OpenFilterCommand = new DelegateCommand(arg => OpenFilterEditor());

            foreach (var name in App.Current.AvailableThemes)
                Themes.Add(name);

            SelectedTheme = App.Current.ActiveTheme;

            viewModel = new TraceSettingsViewModel(null);

            var knownProviders = new Dictionary<Guid, string> {
                {new Guid("A0386E75-F70C-464C-A9CE-33C44E091623"), "DXVA2"},
                {new Guid("F8F10121-B617-4A56-868B-9DF1B27FE32C"), "MMCSS"},
                {new Guid("6E03DD26-581B-4EC5-8F22-601A4DE5F022"), "WMDRM"},
                {new Guid("8F2048E0-F260-4F57-A8D1-932376291682"), "WME"},
                {new Guid("681069C4-B785-466A-BC63-4AA616644B68"), "WMP"},
                {new Guid("75D4A1BB-7CC6-44B1-906D-D5E05BE6D060"), "DVD"},
                {new Guid("28CF047A-2437-4B24-B653-B9446A419A69"), "DSHOW"},
                {new Guid("F404B94E-27E0-4384-BFE8-1D8D390B0AA3"), "Microsoft-Windows-MediaFoundation-Performance"},
                {new Guid("362007F7-6E50-4044-9082-DFA078C63A73"), "MF2"},
                {new Guid("A6A00EFD-21F2-4A99-807E-9B3BF1D90285"), "AE"},
                {new Guid("63770680-05F1-47E0-928A-9ACFDCF52147"), "HME"},
                {new Guid("779D8CDC-666B-4BF4-A367-9DF89D6901E8"), "HDDVD"},
                {new Guid("71DD85BC-D474-4974-B0F6-93FFC5BFBD04"), "DWMAPIGUID"},
                {new Guid("8CC44E31-7F28-4F45-9938-4810FF517464"), "SCHEDULEGUID"},
                {new Guid("65CD4C8A-0848-4583-92A0-31C0FBAF00C0"), "DX"},
                {new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9"), "Microsoft-Windows-DXGI"},
                {new Guid("5D8087DD-3A9B-4F56-90DF-49196CDC4F11"), "D3D12"},
                {new Guid("DB6F6DDB-AC77-4E88-8253-819DF9BBF140"), "Microsoft-Windows-Direct3D11"},
                {new Guid("7E7D3382-023C-43CB-95D2-6F0CA6D70381"), "D3D10LEVEL9"},
                {new Guid("802EC45A-1E99-4B83-9920-87C98277BA9D"), "DXC"},
                {new Guid("A688EE40-D8D9-4736-B6F9-6B74935BA3B1"), "UMD"},
                {new Guid("A42C77DB-874F-422E-9B44-6D89FE2BD3E5"), "DWM"},
                {new Guid("8C9DD1AD-E6E5-4B07-B455-684A9D879900"), "DWM2"},
                {new Guid("9E9BBA3C-2E38-40CB-99F4-9E8281425164"), "Microsoft-Windows-Dwm-Core"},
                {new Guid("EA6D6E3B-7014-4AB1-85DB-4A50CDA32A82"), "CODEC"},
                {new Guid("E7C7EDF9-D0E4-4338-8AE3-BCA3C5B4B4A3"), "KMFD"},
                {new Guid("A70BC228-E778-4061-86FA-DEBB03FDA64A"), "UMFD"},
                {new Guid("31293F4F-F7BB-487D-8B3B-F537B827352F"), "TESTFRAMEWORK"},
                {new Guid("42C4E0C1-0D92-46F0-842C-1E791FA78D52"), "TEST"},
                {new Guid("30336ED4-E327-447C-9DE0-51B652C86108"), "Microsoft-Windows-Shell-Core"},
                {new Guid("531A35AB-63CE-4BCF-AA98-F88C7A89E455"), "XAML"},
                {new Guid("8C416C79-D49B-4F01-A467-E56D3AA8234C"), "Microsoft-Windows-Win32k"},
                {new Guid("DCB453DB-C652-48BE-A0F8-A64459D5162E"), "D2D"},
                {new Guid("712909C0-6E57-4121-B639-87C8BF9004E0"), "D2DSCENARIOS"},
            };

            {
                var collector = new EventCollectorViewModel();
                collector.Name = "Default Collector";

                foreach (var provider in knownProviders) {
                    collector.Providers.Add(new EventProviderViewModel {
                        Id = provider.Key,
                        Name = provider.Value
                    });
                }

                var knownProfile = new TraceProfileViewModel {
                    Name = "Known Providers",
                    Collectors = { collector }
                };
                viewModel.Profiles.Add(knownProfile);
            }

            {
                var collector = new EventCollectorViewModel();
                collector.Name = "Default Collector";
                collector.Providers.Add(new EventProviderViewModel {
                    Id = new Guid("716EFEF7-5AC2-4EE0-8277-D9226411A155"),
                    Name = "FFMF-FFMF-Sculptor",
                    Manifest = @"C:\Users\nrieck\dev\ffmf\src\Sculptor\Sculptor.man",
                    IsEnabled = true
                });
                collector.Providers.Add(new EventProviderViewModel {
                    Id = new Guid("E13B77A8-14B6-11DE-8069-001B212B5009"),
                    Name = "WPF",
                    Manifest = @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\WPF\wpf-etw.man",
                    IsEnabled = true
                });

                var profile = new TraceProfileViewModel {
                    Name = "Default Profile",
                    Collectors = { collector }
                };
                viewModel.Profiles.Add(profile);
                viewModel.ActiveProfile = profile;
            }

            traceProfile = viewModel.GetDescriptor();
        }

        public ObservableCollection<string> Themes { get; } =
            new ObservableCollection<string>();

        public string SelectedTheme
        {
            get => selectedTheme;
            set
            {
                if (selectedTheme == value)
                    return;

                if (App.Current.TryLoadTheme(value))
                    selectedTheme = value;
            }
        }

        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand ConfigureCommand { get; }
        public ICommand OpenViewEditorCommand { get; }
        public ICommand OpenFilterCommand { get; }

        private bool CanStart()
        {
            return !IsCollecting && traceProfile != null;
        }

        private async Task Start()
        {
            await StartCapture();
            CommandManager.InvalidateRequerySuggested();
        }

        private bool CanStop()
        {
            return IsCollecting;
        }

        private async Task Stop()
        {
            await StopCapture();
            CommandManager.InvalidateRequerySuggested();
        }

        private new Task Clear()
        {
            base.Clear();
            return Task.CompletedTask;
        }

        private TraceSettingsViewModel viewModel;

        private void Configure(object obj)
        {
            if (viewModel == null) {
                viewModel = new TraceSettingsViewModel(null);
            }

            var dialog = new TraceSettingsWindow();
            dialog.DataContext = viewModel;
            dialog.Owner = Application.Current.MainWindow;
            try {
                if (dialog.ShowDialog() != true)
                    return;
            } finally {
                var selectedSession = viewModel.ActiveProfile;
                viewModel.ActiveProfile = selectedSession;
                viewModel.DialogResult = null;
            }

            traceProfile = viewModel.GetDescriptor();
        }

        private void OpenViewEditor(object obj)
        {
            var dialog = PresetManagerDialog.CreateDialog(AdvModel);
            dialog.Owner = Application.Current.MainWindow;
            dialog.ShowDialog();
        }

        private class StubGlobalSettings : ISettingsService
        {
            private readonly ISettingsStore nullStore = new NullSettingsStore();

            event EventHandler ISettingsService.SettingsLayerChanged
            {
                add { }
                remove { }
            }

            public ISettingsStore GetGlobalStore()
            {
                return nullStore;
            }

            public ISettingsStore GetAmbientStore()
            {
                return nullStore;
            }

            public ISettingsStore GetProjectStore(ProjectInfo project)
            {
                return nullStore;
            }

            public AdvmPresetCollection GetViewPresets()
            {
                return new AdvmPresetCollection();
            }

            public void SaveViewPresets(AdvmPresetCollection presets)
            {
            }

            public void SaveAmbient()
            {
            }
        }

        private class StubSettingsContext : ITraceSettingsContext
        {
            public IReadOnlyList<ProjectInfo> ProjectsInSolution { get; } = new ProjectInfo[0];

            public AsyncLazy<IReadOnlyList<string>> ManifestsInSolution { get; } =
                new AsyncLazy<IReadOnlyList<string>>(() => Task.FromResult<IReadOnlyList<string>>(new string[0]));

            public Task<EventManifest> GetManifest(string manifestFile)
            {
                var manifest = new EventManifest();
                return Task.FromResult(manifest);
            }
        }
    }

    internal class NullSettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, object> entries = new Dictionary<string, object>();
        public string LayerId { get; }
        public string Name { get; }
        public string Origin { get; }

        public T GetValue<T>(SettingsKey<T> key)
        {
            if (entries.TryGetValue(key.KeyPath, out object obj) && obj is T value)
                return value;
            return default;
        }

        public void ClearValue<T>(SettingsKey<T> key)
        {
            entries.Remove(key.KeyPath);
        }

        public void SetValue<T>(SettingsKey<T> key, T value)
        {
            entries[key.KeyPath] = value;
        }

        public void Reload()
        {
        }

        public void Save()
        {
        }
    }
}
