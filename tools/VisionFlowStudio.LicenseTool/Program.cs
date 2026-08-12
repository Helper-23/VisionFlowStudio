using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Forms;
using VisionFlowStudio.Licensing;

namespace VisionFlowStudio.LicenseTool
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0) return RunCommand(args);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static int RunCommand(string[] args)
        {
            var command = args[0].ToLowerInvariant();
            if (command == "--machine-code")
            {
                Console.WriteLine(MachineFingerprint.GetMachineCode());
                return 0;
            }
            if (command == "--generate-key-pair")
            {
                var directory = args.Length > 1 ? Path.GetFullPath(args[1]) : Environment.CurrentDirectory;
                GenerateKeyPair(directory);
                Console.WriteLine(Path.Combine(directory, "VisionFlowStudio.public.xml"));
                return 0;
            }
            if (command == "--issue")
            {
                var options = ParseOptions(args, 1);
                var key = Required(options, "private");
                var machine = Required(options, "machine");
                var output = Required(options, "out");
                DateTime? expires = null;
                string expiresText;
                if (options.TryGetValue("expires", out expiresText) && !string.IsNullOrWhiteSpace(expiresText))
                    expires = DateTime.ParseExact(expiresText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal).Date.AddDays(1).ToUniversalTime();
                var license = Issue(File.ReadAllText(key), machine,
                    Value(options, "customer", "Customer"), Value(options, "edition", "Professional"),
                    Value(options, "features", "All"), expires);
                File.WriteAllText(output, license);
                Console.WriteLine(Path.GetFullPath(output));
                return 0;
            }
            if (command == "--install")
            {
                if (args.Length < 2) throw new ArgumentException("Usage: --install <license.vfslic>");
                var result = LicenseStore.Install(File.ReadAllText(args[1]));
                Console.WriteLine(result.IsValid ? "LICENSE_OK" : result.ErrorCode + ": " + result.Message);
                return result.IsValid ? 0 : 2;
            }
            if (command == "--validate-installed")
            {
                var result = LicenseStore.ValidateInstalled();
                Console.WriteLine(result.IsValid
                    ? "INSTALLED_LICENSE_OK: " + result.License.LicenseId
                    : result.ErrorCode + ": " + result.Message);
                return result.IsValid ? 0 : 2;
            }
            if (command == "--self-test") return SelfTest();
            PrintHelp();
            return 2;
        }

        internal static string Issue(string privateKeyXml, string machineCode, string customer,
            string edition, string featureList, DateTime? expiresUtc)
        {
            var payload = new LicensePayload
            {
                MachineCode = MachineFingerprint.NormalizeMachineCode(machineCode),
                Customer = customer?.Trim() ?? string.Empty,
                Edition = string.IsNullOrWhiteSpace(edition) ? "Professional" : edition.Trim(),
                Features = SplitFeatures(featureList),
                IssuedUtcTicks = DateTime.UtcNow.Ticks,
                ExpiresUtcTicks = expiresUtc.HasValue ? expiresUtc.Value.ToUniversalTime().Ticks : 0
            };
            return LicenseCodec.CreateLicense(payload, privateKeyXml);
        }

        internal static void GenerateKeyPair(string directory)
        {
            Directory.CreateDirectory(directory);
            var parameters = new CspParameters { ProviderType = 24 };
            using (var rsa = new RSACryptoServiceProvider(3072, parameters) { PersistKeyInCsp = false })
            {
                File.WriteAllText(Path.Combine(directory, "VisionFlowStudio.private.xml"), rsa.ToXmlString(true));
                File.WriteAllText(Path.Combine(directory, "VisionFlowStudio.public.xml"), rsa.ToXmlString(false));
            }
        }

        private static int SelfTest()
        {
            var parameters = new CspParameters { ProviderType = 24 };
            using (var rsa = new RSACryptoServiceProvider(2048, parameters) { PersistKeyInCsp = false })
            {
                var machine = MachineFingerprint.GetMachineCode();
                var license = Issue(rsa.ToXmlString(true), machine, "Self Test", "Test", "All", DateTime.UtcNow.AddDays(1));
                var valid = LicenseCodec.Validate(license, machine, DateTime.UtcNow, rsa.ToXmlString(false));
                var wrongMachine = LicenseCodec.Validate(license, "VFS-00000-00000-00000-00000-00000-00000-00000-00000", DateTime.UtcNow, rsa.ToXmlString(false));
                var tampered = license.Substring(0, license.Length - 1) + (license.EndsWith("A", StringComparison.Ordinal) ? "B" : "A");
                var tamperedResult = LicenseCodec.Validate(tampered, machine, DateTime.UtcNow, rsa.ToXmlString(false));
                var expiredLicense = Issue(rsa.ToXmlString(true), machine, "Self Test", "Test", "All", DateTime.UtcNow.AddMinutes(-1));
                var expired = LicenseCodec.Validate(expiredLicense, machine, DateTime.UtcNow, rsa.ToXmlString(false));
                if (!valid.IsValid || wrongMachine.ErrorCode != LicenseErrorCode.WrongMachine ||
                    tamperedResult.IsValid || expired.ErrorCode != LicenseErrorCode.Expired)
                    throw new InvalidOperationException("License self-test failed.");
            }
            Console.WriteLine("LICENSE_SELF_TEST_OK");
            return 0;
        }

        private static string[] SplitFeatures(string value)
        {
            return (value ?? "All").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static Dictionary<string, string> ParseOptions(string[] args, int start)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = start; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length) continue;
                result[args[i].Substring(2)] = args[++i];
            }
            return result;
        }

        private static string Required(IDictionary<string, string> options, string name)
        {
            string value;
            if (!options.TryGetValue(name, out value) || string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Missing option --" + name);
            return value;
        }

        private static string Value(IDictionary<string, string> options, string name, string fallback)
        {
            string value;
            return options.TryGetValue(name, out value) ? value : fallback;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("VisionFlow Studio License Tool");
            Console.WriteLine("  --machine-code");
            Console.WriteLine("  --generate-key-pair <directory>");
            Console.WriteLine("  --issue --private <private.xml> --machine <code> --customer <name> --edition <name> [--expires yyyy-MM-dd] --out <file.vfslic>");
            Console.WriteLine("  --install <file.vfslic>");
            Console.WriteLine("  --validate-installed");
            Console.WriteLine("  --self-test");
        }
    }
}
