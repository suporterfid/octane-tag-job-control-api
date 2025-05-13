using OctaneTagWritingTest.JobStrategies;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OctaneTagWritingTest
{
    internal class JobManager
    {
        private Dictionary<string, IJobStrategy> strategies = new Dictionary<string, IJobStrategy>();
        private Dictionary<string, ReaderSettings> readerSettings;

        public JobManager(string hostnameDetector, string hostnameWriter, string hostnameVerifier, string testDescription, Dictionary<string, ReaderSettings> settings, string sku = null)
        {
            this.readerSettings = settings;

            // Register available test strategies
            if(string.IsNullOrEmpty(testDescription))
            {
                testDescription = "Writing-Test-1-Round-1";
            }
            
            strategies.Add("0", new JobStrategy0ReadOnlyLogging(hostnameWriter, $"TestCase0_ReadOnlyLogging_Log-{testDescription}.csv", readerSettings));
            strategies.Add("1", new JobStrategy1SpeedStrategy(hostnameWriter, $"TestCase1_Log-SpeedStrategy-{testDescription}.csv", readerSettings));    
            strategies.Add("2", new JobStrategy2MultiAntennaWriteStrategy(hostnameWriter, $"TestCase3_MultiAntenna_Log-{testDescription}.csv", readerSettings));
            strategies.Add("3", new JobStrategy3BatchSerializationPermalockStrategy(hostnameWriter, $"TestCase3_Log-BatchSerializationPermalockStrategy-{testDescription}.csv", readerSettings));
            strategies.Add("4", new JobStrategy4VerificationCycleStrategy(hostnameWriter, $"TestCase4_VerificationCycle_Log-{testDescription}.csv", readerSettings));
            strategies.Add("5", new JobStrategy5EnduranceStrategy(hostnameWriter, $"TestCase8_Endurance_Log-{testDescription}.csv", readerSettings));
            strategies.Add("6", new JobStrategy6RobustnessStrategy(hostnameWriter, $"TestCase6_Robustness_Log-{testDescription}.csv", readerSettings));
            strategies.Add("7", new JobStrategy7OptimizedStrategy(hostnameWriter, $"TestCase7_Log-OptimizedStrategy-{testDescription}.csv", readerSettings));
            strategies.Add("8", new JobStrategy8MultipleReaderEnduranceStrategy(hostnameDetector, hostnameWriter, hostnameVerifier, $"TestCase8_Log-DualReaderEnduranceStrategy-{testDescription}.csv", readerSettings));
            strategies.Add("9", new JobStrategy9CheckBox(hostnameWriter, $"TestCase9_Log-CheckBox-{testDescription}.csv", readerSettings, sku));
        }

        public void DisplayMenu()
        {
            Console.WriteLine("\n=== Test Manager ===");
            Console.WriteLine("Select a test to execute:");
            foreach (var kvp in strategies)
            {
                Console.WriteLine($"[{kvp.Key}] - {kvp.Value.GetType().Name}");
            }
        }

        public void ExecuteTest(string key, CancellationToken cancellationToken = default)
        {
            if (strategies.ContainsKey(key))
            {
                strategies[key].RunJob(cancellationToken);
            }
            else
            {
                Console.WriteLine("Invalid option.");
            }
        }
    }
}
