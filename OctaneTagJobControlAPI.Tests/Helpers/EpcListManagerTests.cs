using Microsoft.VisualStudio.TestTools.UnitTesting;
using OctaneTagJobControlAPI.Strategies.Base;
using System;

namespace OctaneTagJobControlAPI.Tests.Helpers
{
    [TestClass]
    public class EpcListManagerTests
    {
        private EpcListManager _manager;

        [TestInitialize]
        public void Setup()
        {
            _manager = EpcListManager.Instance;
        }

        [TestMethod]
        public void CreateEpcWithCurrentDigits_WithValidInput_ProducesCorrectFormat()
        {
            // Arrange
            string currentEpc = "B20099999999999ABCDEF1234";
            string tid = "E280116020123456789012";

            // Act
            string epc = _manager.CreateEpcWithCurrentDigits(currentEpc, tid);

            // Assert
            Assert.IsNotNull(epc);
            Assert.AreEqual(24, epc.Length, "EPC should be 24 characters long");
            
            // Verify the first 14 characters are preserved from the header and item code
            Assert.AreEqual(currentEpc.Substring(0, 14), epc.Substring(0, 14));
            
            // Verify the last 10 characters are from the TID serial
            using (var parser = new OctaneTagWritingTest.Helpers.TagTidParser(tid))
            {
                string expectedSerial = parser.Get40BitSerialHex();
                Assert.AreEqual(expectedSerial, epc.Substring(14));
            }
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateEpcWithCurrentDigits_WithInvalidEpcLength_ThrowsArgumentException()
        {
            // Arrange
            string currentEpc = "B20099"; // Too short
            string tid = "E280116020123456789012";

            // Act
            _manager.CreateEpcWithCurrentDigits(currentEpc, tid);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateEpcWithCurrentDigits_WithEmptyTid_ThrowsArgumentException()
        {
            // Arrange
            string currentEpc = "B20099999999999ABCDEF1234";
            string tid = "";

            // Act
            _manager.CreateEpcWithCurrentDigits(currentEpc, tid);
        }

        [TestMethod]
        public void CreateEpcWithCurrentDigits_WithDifferentTagModels_GeneratesCorrectEpc()
        {
            // Test with different tag models
            var testCases = new[]
            {
                new { Tid = "E280119012345678AABB", Model = "Impinj Monza R6" },
                new { Tid = "E280691512345678AABB", Model = "NXP UCODE 9" }
            };

            foreach (var testCase in testCases)
            {
                // Arrange
                string currentEpc = "B20099999999999ABCDEF1234";

                // Act
                string epc = _manager.CreateEpcWithCurrentDigits(currentEpc, testCase.Tid);

                // Assert
                Assert.IsNotNull(epc);
                Assert.AreEqual(24, epc.Length, $"EPC should be 24 characters long for {testCase.Model}");
                
                using (var parser = new OctaneTagWritingTest.Helpers.TagTidParser(testCase.Tid))
                {
                    string expectedSerial = parser.Get40BitSerialHex();
                    Assert.AreEqual(expectedSerial, epc.Substring(14), 
                        $"Serial extraction failed for {testCase.Model}");
                }
            }
        }

        [TestMethod]
        public void InitEpcData_SetsCorrectValues()
        {
            // Arrange
            string header = "B200";
            string code = "99999999999999";
            long quantity = 100;

            // Act
            _manager.InitEpcData(header, code, quantity);

            // Verify through CreateEpcWithCurrentDigits
            string currentEpc = "000000000000000000000000";
            string tid = "E280119012345678AABB";
            string epc = _manager.CreateEpcWithCurrentDigits(currentEpc, tid);

            // Assert
            Assert.IsTrue(epc.StartsWith(header));
            Assert.IsTrue(epc.Substring(4, 14).StartsWith(code));
        }

        [TestMethod]
        public void GetNextEpc_ReturnsUniqueEpcsForDifferentTids()
        {
            // Arrange
            string currentEpc = "B20099999999999ABCDEF1234";
            var tids = new[]
            {
                "E280119012345678AABB",
                "E280119087654321CCDD"
            };

            // Act
            var epcs = new HashSet<string>();
            foreach (var tid in tids)
            {
                string epc = _manager.GetNextEpc(currentEpc, tid);
                epcs.Add(epc);
            }

            // Assert
            Assert.AreEqual(tids.Length, epcs.Count, "Each TID should generate a unique EPC");
        }
    }
}
