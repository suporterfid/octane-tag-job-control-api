using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace EpcListGenerator
{
    internal class Program
    {
        // Entry point now supports async operations.
        static async Task Main(string[] args)
        {
            try
            {
                // Parse command-line arguments or prompt the user for input.
                var (epcHeader, gtin, quantity, outputFile, initalSerial) = ParseArgumentsOrPrompt(args);

                // Validate the inputs.
                ValidateInputs(epcHeader, gtin, quantity);

                // Check if the output file already exists and ask if it should be overwritten.
                if (File.Exists(outputFile))
                {
                    Console.Write($"Output file '{outputFile}' already exists. Overwrite? (y/n): ");
                    string overwriteResponse = Console.ReadLine();
                    if (!overwriteResponse.Equals("y", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("Operation cancelled.");
                        return;
                    }
                }

                Console.WriteLine("Generating EPC list...");

                // Generate the custom EPC list.
                List<string> epcList = EpcListGeneratorHelper.Instance.GenerateCustomEpcList(epcHeader, gtin, quantity, initalSerial);

                Console.WriteLine("Saving EPC list to file...");
                // Use asynchronous file writing for responsiveness.
                await SaveEpcListToFileAsync(epcList, outputFile);

                Console.WriteLine($"File '{outputFile}' created successfully with {epcList.Count} EPCs.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred: " + ex.Message);
            }
        }

        /// <summary>
        /// Parses command-line arguments if provided; otherwise, prompts the user for input.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>
        /// A tuple containing the EPC header, GTIN, quantity, output file path, and initial serial counter.
        /// </returns>
        private static (string epcHeader, string gtin, long quantity, string outputFile, long initialSerial) ParseArgumentsOrPrompt(string[] args)
        {
            // Default values.
            string epcHeader = "B071";
            string gtin = "";
            long quantity = 0;
            string outputFile = "epc_list.txt";
            long initialSerial = 1; // Default starting serial counter.

            if (args.Length >= 3)
            {
                // Use command-line arguments: header, gtin, quantity, [outputFile], [initialSerial]
                epcHeader = args[0];
                gtin = args[1];
                if (string.IsNullOrWhiteSpace(gtin))
                {
                    gtin = "99999999999999";
                }
                if (!long.TryParse(args[2], out quantity))
                {
                    throw new ArgumentException("Invalid quantity provided in arguments.");
                }
                if (args.Length >= 4)
                {
                    outputFile = args[3];
                }
                if (args.Length >= 5)
                {
                    if (!long.TryParse(args[4], out initialSerial))
                    {
                        throw new ArgumentException("Invalid initial serial counter provided in arguments.");
                    }
                }
            }
            else
            {
                // Prompt for EPC header.
                Console.Write($"Enter the EPC header (4 characters) [default: {epcHeader}]: ");
                string inputHeader = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(inputHeader))
                {
                    epcHeader = inputHeader;
                }

                // Prompt for GTIN.
                Console.Write("Enter the GTIN (14 characters): ");
                gtin = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(gtin))
                {
                    gtin = "99999999999999";
                    Console.WriteLine($"GTIN not provided. Using default GTIN: {gtin}");
                }

                // Prompt for quantity.
                Console.Write("Enter the number of EPCs to generate: ");
                string quantityInput = Console.ReadLine();
                if (!long.TryParse(quantityInput, out quantity))
                {
                    throw new ArgumentException("Invalid quantity entered.");
                }

                // Prompt for output file path.
                Console.Write($"Enter the output file path [default: {outputFile}]: ");
                string inputOutput = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(inputOutput))
                {
                    outputFile = inputOutput;
                }

                // Prompt for initial serial counter.
                Console.Write("Enter the initial serial counter [default: 1]: ");
                string initialSerialInput = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(initialSerialInput))
                {
                    if (!long.TryParse(initialSerialInput, out initialSerial))
                    {
                        throw new ArgumentException("Invalid initial serial counter entered.");
                    }
                }
            }

            return (epcHeader, gtin, quantity, outputFile, initialSerial);
        }


        /// <summary>
        /// Validates the user inputs.
        /// </summary>
        /// <param name="epcHeader">The EPC header string.</param>
        /// <param name="gtin">The GTIN string.</param>
        /// <param name="quantity">The number of EPCs to generate.</param>
        private static void ValidateInputs(string epcHeader, string gtin, long quantity)
        {
            if (epcHeader.Length != 4)
            {
                throw new ArgumentException("EPC header must be exactly 4 characters long.");
            }
            if (gtin.Length != 14)
            {
                throw new ArgumentException("GTIN must be exactly 14 characters long.");
            }
            if (quantity <= 0 || quantity > 999999)
            {
                throw new ArgumentException("Quantity must be between 1 and 999999.");
            }
        }

        /// <summary>
        /// Asynchronously saves the EPC list to a file. If the file already exists, it updates the file
        /// by merging the existing EPCs with the new ones (ensuring uniqueness).
        /// </summary>
        /// <param name="epcList">The list of EPC strings to be saved.</param>
        /// <param name="outputFile">The output file path.</param>
        /// <returns>A Task representing the asynchronous operation.</returns>
        private static async Task SaveEpcListToFileAsync(List<string> epcList, string outputFile)
        {
            if (File.Exists(outputFile))
            {
                // Read any existing EPCs from the file.
                var existingEpcs = await File.ReadAllLinesAsync(outputFile);

                // Use a HashSet to merge EPCs and ensure uniqueness.
                var mergedEpcs = new HashSet<string>(existingEpcs);
                foreach (var epc in epcList)
                {
                    mergedEpcs.Add(epc);
                }

                // Write the merged EPCs back to the file.
                await File.WriteAllLinesAsync(outputFile, mergedEpcs);
            }
            else
            {
                // Create a new file and write the EPC list.
                await File.WriteAllLinesAsync(outputFile, epcList);
            }
        }

    }
}
