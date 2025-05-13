using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OctaneTagWritingTest
{
    public static class InteractiveConfig
    {
        public static ApplicationConfig Configure(ApplicationConfig initialConfig = null)
        {
            ApplicationConfig config = initialConfig ?? new ApplicationConfig();

            Console.WriteLine("=== Interactive Configuration Mode ===");
            Console.WriteLine("Press Enter to keep current values or input new ones.");

            config.DetectorHostname = PromptForValue("Detector Reader Hostname", config.DetectorHostname);
            config.WriterHostname = PromptForValue("Writer Reader Hostname", config.WriterHostname);
            config.VerifierHostname = PromptForValue("Verifier Reader Hostname", config.VerifierHostname);
            config.TestDescription = PromptForValue("Test Description", config.TestDescription);
            config.EpcHeader = PromptForValue("EPC Header (2 chars)", config.EpcHeader);
            config.EpcPlainItemCode = PromptForValue("EPC Plain Item Code", config.EpcPlainItemCode);
            config.Sku = PromptForValue("SKU (12 digits)", config.Sku);

            string qtyStr = PromptForValue("Quantity", config.Quantity.ToString());
            if (long.TryParse(qtyStr, out long qty))
                config.Quantity = qty;

            string dPowerStr = PromptForValue("Detector TX Power (dBm)", config.DetectorTxPowerInDbm.ToString());
            if (int.TryParse(dPowerStr, out int dPower))
                config.DetectorTxPowerInDbm = dPower;

            string wPowerStr = PromptForValue("Writer TX Power (dBm)", config.WriterTxPowerInDbm.ToString());
            if (int.TryParse(wPowerStr, out int wPower))
                config.WriterTxPowerInDbm = wPower;

            string vPowerStr = PromptForValue("Verifier TX Power (dBm)", config.VerifierTxPowerInDbm.ToString());
            if (int.TryParse(vPowerStr, out int vPower))
                config.VerifierTxPowerInDbm = vPower;

            Console.WriteLine("\nConfiguration complete!");

            // Ask if user wants to save this as a configuration file
            Console.Write("\nSave this configuration to file? (y/n): ");
            string saveResponse = Console.ReadLine()?.ToLower();
            if (saveResponse == "y" || saveResponse == "yes")
            {
                Console.Write("Enter file name (default: config.json): ");
                string fileName = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(fileName))
                    fileName = "config.json";

                config.SaveToFile(fileName);
                Console.WriteLine($"Configuration saved to {fileName}");
            }

            return config;
        }

        private static string PromptForValue(string prompt, string currentValue)
        {
            Console.Write($"{prompt} [{currentValue}]: ");
            string input = Console.ReadLine();
            return string.IsNullOrWhiteSpace(input) ? currentValue : input;
        }
    }
}
