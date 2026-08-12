using System;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using VisionFlowStudio.VisionMaster;
using VisionFlowStudio.VisionPro;
using VisionFlowStudio.Halcon;
using VisionFlowStudio.Cameras;
using VisionFlowStudio.Communications;
using VisionFlowStudio.Licensing;

namespace VisionFlowStudio.App
{
    public partial class App : Application
    {
        private VisionMasterAdapter _visionMaster;
        private VisionProAdapter _visionPro;
        private HalconAdapter _halcon;
        private CameraRegistry _cameras;
        private CommunicationRegistry _communications;
        private DispatcherTimer _licenseValidationTimer;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var settings = ApplicationSettingsStore.Load();
            LocalizationService.Initialize(settings.Language);
            var licenseResult = LicenseStore.ValidateInstalled();
            if (!licenseResult.IsValid)
            {
                var activation = new ActivationWindow(licenseResult);
                if (activation.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
                licenseResult = LicenseStore.ValidateInstalled();
                if (!licenseResult.IsValid)
                {
                    Shutdown(-2);
                    return;
                }
            }
            if (e.Args.Any(x => string.Equals(x, "--camera-self-test", StringComparison.OrdinalIgnoreCase)))
            {
                RunIsolatedCameraSelfTest(e.Args);
                Shutdown();
                return;
            }
            var splash = SplashThreadHost.Start();
            try
            {
                VisionMasterRuntime.Initialize();
                _visionMaster = new VisionMasterAdapter();
                _visionPro = new VisionProAdapter();
                _halcon = new HalconAdapter();
                _cameras = new CameraRegistry();
                _communications = new CommunicationRegistry();
                var viewModel = new MainViewModel(_visionMaster, _visionPro, _halcon, _cameras, _communications);
                var projectPassword = settings.GetProjectPassword();
                string autoLoadError = null;
                var projectLoaded = false;
                if (settings.AutoLoadProject && !string.IsNullOrWhiteSpace(settings.AutoLoadProjectPath))
                {
                    try { viewModel.LoadProject(settings.AutoLoadProjectPath, projectPassword); projectLoaded = true; }
                    catch (Exception ex) { autoLoadError = ex.Message; }
                }
                var window = new MainWindow(viewModel);
                if (projectLoaded) window.SetProjectPassword(projectPassword);
                if (settings.StartMaximized) window.WindowState = WindowState.Maximized;
                MainWindow = window;
                window.Show();
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                StartLicenseValidationTimer(window);
                splash.CloseAfterMinimumDelay(900);
                if (!string.IsNullOrWhiteSpace(autoLoadError))
                {
                    var message = autoLoadError;
                    window.Dispatcher.BeginInvoke(new Action(delegate { MessageBox.Show(window, message, "自动加载方案失败", MessageBoxButton.OK, MessageBoxImage.Warning); }), DispatcherPriority.ApplicationIdle);
                }
            }
            catch (Exception ex)
            {
                splash.CloseAfterMinimumDelay(250);
                MessageBox.Show("软件初始化失败：" + ex.Message, "VisionFlow Studio", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-1);
            }
        }

        private void StartLicenseValidationTimer(Window owner)
        {
            _licenseValidationTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
            _licenseValidationTimer.Tick += delegate
            {
                var result = LicenseStore.ValidateInstalled();
                if (result.IsValid) return;
                _licenseValidationTimer.Stop();
                MessageBox.Show(owner,
                    LocalizationService.IsEnglish ? result.Message : "软件授权已失效，请重新激活。",
                    LocalizationService.IsEnglish ? "License verification failed" : "授权校验失败",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-3);
            };
            _licenseValidationTimer.Start();
        }

        private sealed class SplashThreadHost
        {
            private readonly DateTime _started = DateTime.Now;
            private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
            private StartupSplashWindow _window;
            private Thread _thread;

            public static SplashThreadHost Start()
            {
                var host = new SplashThreadHost();
                host._thread = new Thread(host.Run) { IsBackground = true, Name = "VisionFlowStudio.Splash" };
                host._thread.SetApartmentState(ApartmentState.STA);
                host._thread.Start();
                host._ready.Wait(4000);
                return host;
            }

            private void Run()
            {
                _window = new StartupSplashWindow();
                _window.Closed += delegate { Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); };
                _window.Show();
                _ready.Set();
                Dispatcher.Run();
            }

            public void CloseAfterMinimumDelay(int minimumMilliseconds)
            {
                var window = _window;
                if (window == null) return;
                window.Dispatcher.BeginInvoke(new Action(delegate
                {
                    var remaining = Math.Max(0, minimumMilliseconds - (DateTime.Now - _started).TotalMilliseconds);
                    if (remaining <= 1) { window.CloseAnimated(); return; }
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(remaining) };
                    timer.Tick += delegate { timer.Stop(); window.CloseAnimated(); };
                    timer.Start();
                }), DispatcherPriority.Normal);
            }
        }

        private void RunIsolatedCameraSelfTest(string[] args)
        {
            var stage = args.SkipWhile(x => !string.Equals(x, "--camera-self-test-stage", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() ?? "mainwindow";
            var order = new[] { "bare", "vmruntime", "vmadapter", "vpadapter", "halcon", "vmstatus", "vpstatus", "halconstatus", "vmoutputs", "vmoutputsclose", "mainvm", "mainwindow" };
            var stageIndex = Array.FindIndex(order, x => string.Equals(x, stage, StringComparison.OrdinalIgnoreCase));
            if (stageIndex < 0) stageIndex = order.Length - 1;
            _cameras = new CameraRegistry();
            if (stageIndex >= 1) VisionMasterRuntime.Initialize();
            if (stageIndex >= 2) _visionMaster = new VisionMasterAdapter();
            if (stageIndex >= 3) _visionPro = new VisionProAdapter();
            if (stageIndex >= 4) _halcon = new HalconAdapter();
            if (stageIndex >= 5)
            {
                if (_visionMaster == null) _visionMaster = new VisionMasterAdapter();
                if (_visionPro == null) _visionPro = new VisionProAdapter();
                if (_halcon == null) _halcon = new HalconAdapter();
                if (string.Equals(stage, "vmstatus", StringComparison.OrdinalIgnoreCase)) _visionMaster.GetStatus();
                if (string.Equals(stage, "vpstatus", StringComparison.OrdinalIgnoreCase)) _visionPro.GetStatus();
                if (string.Equals(stage, "halconstatus", StringComparison.OrdinalIgnoreCase)) _halcon.GetStatus();
                if (string.Equals(stage, "vmoutputs", StringComparison.OrdinalIgnoreCase))
                    _visionMaster.GetOutputs(new VisionFlowStudio.Core.VisionMasterRunConfig { SolutionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VisionPrograms", "2DInspection.sol"), ProcedureName = "流程1" });
                if (string.Equals(stage, "vmoutputsclose", StringComparison.OrdinalIgnoreCase))
                {
                    _visionMaster.GetOutputs(new VisionFlowStudio.Core.VisionMasterRunConfig { SolutionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VisionPrograms", "2DInspection.sol"), ProcedureName = "流程1" });
                    _visionMaster.CloseSolution();
                }
            }
            if (stageIndex >= 10)
            {
                _communications = new CommunicationRegistry();
                var viewModel = new MainViewModel(_visionMaster, _visionPro, _halcon, _cameras, _communications);
                if (stageIndex >= 11) new MainWindow(viewModel);
            }
            RunCameraSelfTest(args, stage);
        }

        private void RunCameraSelfTest(string[] args, string stage)
        {
            var serial = args.SkipWhile(x => !string.Equals(x, "--camera-self-test", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() ?? string.Empty;
            var output = args.SkipWhile(x => !string.Equals(x, "--camera-self-test-output", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(output)) output = Path.Combine(Path.GetTempPath(), "VisionFlowStudio-camera-self-test.txt");
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                var devices = _cameras.EnumerateAll();
                var lines = devices.Select(x => string.Format("DEVICE vendor={0}, model={1}, serial={2}, ip={3}", x.Vendor, x.DisplayName, x.SerialNumber, x.IpAddress)).ToList();
                lines.Insert(0, string.Format("STAGE={0}; USER={1}; ADMIN={2}; BASE={3}", stage, identity.Name, principal.IsInRole(WindowsBuiltInRole.Administrator), AppDomain.CurrentDomain.BaseDirectory));
                var provider = _cameras.Connect("Hikrobot", serial);
                var settings = provider.GetSettings();
                lines.Add(string.Format("CONNECTED serial={0}; exposure={1}; gain={2}", serial, settings.ExposureUs, settings.Gain));
                provider.Disconnect();
                lines.Add("DISCONNECTED");
                File.WriteAllLines(output, lines);
            }
            catch (Exception ex)
            {
                File.WriteAllText(output, ex.ToString());
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Hardware connections are released first. Each component is isolated so
            // one vendor SDK throwing during shutdown cannot prevent camera cleanup.
            try { if (_cameras != null) _cameras.Dispose(); } catch { }
            try { if (_communications != null) _communications.Dispose(); } catch { }
            try { if (_visionMaster != null) _visionMaster.Dispose(); } catch { }
            try { if (_visionPro != null) _visionPro.Dispose(); } catch { }
            try { if (_halcon != null) _halcon.Dispose(); } catch { }
            base.OnExit(e);
        }
    }
}
