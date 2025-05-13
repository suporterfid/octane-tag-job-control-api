using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OctaneTagWritingTest
{
    public static class CommandLineParser
    {
        public static ApplicationConfig ParseArgs(string[] args)
        {
            ApplicationConfig config = new ApplicationConfig();

            // Load from config file if specified
            string configFile = GetArgumentValue(args, "--config");
            if (!string.IsNullOrEmpty(configFile) && File.Exists(configFile))
            {
                config = ApplicationConfig.LoadFromFile(configFile);
            }

            // Override with command line arguments if provided
            string detector = GetArgumentValue(args, "--detector");
            if (!string.IsNullOrEmpty(detector)) config.DetectorHostname = detector;

            string writer = GetArgumentValue(args, "--writer");
            if (!string.IsNullOrEmpty(writer)) config.WriterHostname = writer;

            string verifier = GetArgumentValue(args, "--verifier");
            if (!string.IsNullOrEmpty(verifier)) config.VerifierHostname = verifier;

            string desc = GetArgumentValue(args, "--desc");
            if (!string.IsNullOrEmpty(desc)) config.TestDescription = desc;

            string sku = GetArgumentValue(args, "--sku");
            if (!string.IsNullOrEmpty(sku)) config.Sku = sku;

            string header = GetArgumentValue(args, "--header");
            if (!string.IsNullOrEmpty(header)) config.EpcHeader = header;

            string code = GetArgumentValue(args, "--code");
            if (!string.IsNullOrEmpty(code)) config.EpcPlainItemCode = code;

            string quantity = GetArgumentValue(args, "--quantity");
            if (!string.IsNullOrEmpty(quantity) && long.TryParse(quantity, out long qty))
                config.Quantity = qty;

            // Parse power settings
            string dpower = GetArgumentValue(args, "--detector-power");
            if (!string.IsNullOrEmpty(dpower) && int.TryParse(dpower, out int dp))
                config.DetectorTxPowerInDbm = dp;

            string wpower = GetArgumentValue(args, "--writer-power");
            if (!string.IsNullOrEmpty(wpower) && int.TryParse(wpower, out int wp))
                config.WriterTxPowerInDbm = wp;

            string vpower = GetArgumentValue(args, "--verifier-power");
            if (!string.IsNullOrEmpty(vpower) && int.TryParse(vpower, out int vp))
                config.VerifierTxPowerInDbm = vp;

            return config;
        }

        private static string GetArgumentValue(string[] args, string argName)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(argName, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }

        public static void ShowHelp()
        {
            Console.WriteLine("RFID Tag Writing Application");
            Console.WriteLine("Usage: OctaneTagWritingTest [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --config <path>         Path to configuration file");
            Console.WriteLine("  --detector <hostname>   Detector reader hostname/IP");
            Console.WriteLine("  --writer <hostname>     Writer reader hostname/IP");
            Console.WriteLine("  --verifier <hostname>   Verifier reader hostname/IP");
            Console.WriteLine("  --desc <description>    Test description");
            Console.WriteLine("  --sku <sku>             SKU for encoding (12 digits)");
            Console.WriteLine("  --header <header>       EPC header (default: E7)");
            Console.WriteLine("  --code <code>           EPC plain item code");
            Console.WriteLine("  --quantity <number>     Quantity of EPCs to generate");
            Console.WriteLine("  --detector-power <dbm>  Detector TX power in dBm");
            Console.WriteLine("  --writer-power <dbm>    Writer TX power in dBm");
            Console.WriteLine("  --verifier-power <dbm>  Verifier TX power in dBm");
            Console.WriteLine("  --interactive           Start in interactive configuration mode");
            Console.WriteLine("  --help                  Show this help message");
        }
    }
}
