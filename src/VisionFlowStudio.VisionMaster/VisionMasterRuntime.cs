using System;
using System.IO;
using System.Reflection;
using VM.Core;
using VM.PlatformSDKCS;

namespace VisionFlowStudio.VisionMaster
{
    public static class VisionMasterRuntime
    {
        public const string InstallDirectory = @"C:\Program Files\VisionMaster4.4.0";
        public const string SdkAssemblyDirectory =
            InstallDirectory + @"\Development\V4.x\ComControls\Assembly";
        public const string ApplicationDirectory = InstallDirectory + @"\Applications";
        private static bool _initialized;

        public static bool IsInstalled
        {
            get { return File.Exists(Path.Combine(SdkAssemblyDirectory, "VM.Core.dll")); }
        }

        public static void Initialize()
        {
            if (_initialized)
                return;
            _initialized = true;
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            PrependPath(SdkAssemblyDirectory);
            PrependPath(ApplicationDirectory);
            PrependPath(Path.Combine(ApplicationDirectory, "ModuleProxy", "x64"));
            PrependPath(Path.Combine(ApplicationDirectory, "PublicFile", "x64"));
            PrependPath(Path.Combine(ApplicationDirectory, "3rdLib", "System"));
            PrependPath(Path.Combine(ApplicationDirectory, "3rdLib", "MVD"));
            PrependPath(Path.Combine(ApplicationDirectory, "3rdLib", "MvCameraControl"));
            PrependPath(Path.Combine(ApplicationDirectory, "Module(sp)", "x64"));
        }

        public static T InvokeWithApplicationDirectory<T>(Func<T> action)
        {
            if (action == null)
                throw new ArgumentNullException("action");

            var previous = Environment.CurrentDirectory;
            var changed = false;
            try
            {
                if (Directory.Exists(ApplicationDirectory) &&
                    !string.Equals(previous, ApplicationDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    Directory.SetCurrentDirectory(ApplicationDirectory);
                    changed = true;
                }
                return action();
            }
            finally
            {
                if (changed)
                {
                    try
                    {
                        if (Directory.Exists(previous))
                            Directory.SetCurrentDirectory(previous);
                    }
                    catch
                    {
                    }
                }
            }
        }

        public static void InvokeWithApplicationDirectory(Action action)
        {
            InvokeWithApplicationDirectory(delegate
            {
                action();
                return 0;
            });
        }

        public static int GetErrorCode(Exception exception)
        {
            var vmException = exception as VmException;
            if (vmException != null)
                return vmException.errorCode;
            try
            {
                vmException = VmSolution.GetVmException(exception);
                return vmException == null ? 0 : vmException.errorCode;
            }
            catch
            {
                return 0;
            }
        }

        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            var fileName = new AssemblyName(args.Name).Name + ".dll";
            var sdkPath = Path.Combine(SdkAssemblyDirectory, fileName);
            if (File.Exists(sdkPath))
                return Assembly.LoadFrom(sdkPath);
            var applicationPath = Path.Combine(ApplicationDirectory, fileName);
            return File.Exists(applicationPath) ? Assembly.LoadFrom(applicationPath) : null;
        }

        private static void PrependPath(string directory)
        {
            if (!Directory.Exists(directory))
                return;
            var current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            if (current.IndexOf(directory, StringComparison.OrdinalIgnoreCase) < 0)
                Environment.SetEnvironmentVariable("PATH", directory + ";" + current);
        }
    }
}
