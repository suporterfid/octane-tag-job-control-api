using Microsoft.VisualStudio.TestTools.UnitTesting;
using OctaneTagWritingTest.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OctaneTagJobControlAPI.Tests.Helpers
{
    [TestClass]
    public class SerialGeneratorTests
    {
        private SerialGenerator _generator;

        [TestInitialize]
        public void Setup()
        {
            _generator = new SerialGenerator();
        }

        [TestMethod]
        public void GenerateUniqueSerial_ReturnsValidHexString()
        {
            // Act
            string serial = _generator.GenerateUniqueSerial();

            // Assert
            Assert.IsNotNull(serial);
            Assert.AreEqual(10, serial.Length);
            // Verify it's a valid hex string
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(serial, "^[0-9A-F]{10}$"));
        }

        [TestMethod]
        public void GenerateUniqueSerial_GeneratesUniqueValues()
        {
            // Arrange
            var serials = new HashSet<string>();
            const int numSerials = 100;

            // Act
            for (int i = 0; i < numSerials; i++)
            {
                string serial = _generator.GenerateUniqueSerial();
                serials.Add(serial);
            }

            // Assert
            Assert.AreEqual(numSerials, serials.Count, "All generated serials should be unique");
        }

        [TestMethod]
        public void IsSerialUsed_ReturnsTrueForUsedSerial()
        {
            // Arrange
            string serial = _generator.GenerateUniqueSerial();

            // Act
            bool isUsed = _generator.IsSerialUsed(serial);

            // Assert
            Assert.IsTrue(isUsed);
        }

        [TestMethod]
        public void IsSerialUsed_ReturnsFalseForUnusedSerial()
        {
            // Arrange
            string unusedSerial = "AAAAAAAAAA";

            // Act
            bool isUsed = _generator.IsSerialUsed(unusedSerial);

            // Assert
            Assert.IsFalse(isUsed);
        }

        [TestMethod]
        public void Clear_RemovesAllStoredSerials()
        {
            // Arrange
            string serial = _generator.GenerateUniqueSerial();
            Assert.IsTrue(_generator.IsSerialUsed(serial));

            // Act
            _generator.Clear();

            // Assert
            Assert.IsFalse(_generator.IsSerialUsed(serial));
        }

        [TestMethod]
        public async Task GenerateUniqueSerial_ThreadSafe()
        {
            // Arrange
            const int numThreads = 10;
            const int numSerialsPerThread = 100;
            var allSerials = new HashSet<string>();
            var tasks = new List<Task>();

            // Act
            for (int i = 0; i < numThreads; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    var threadSerials = new List<string>();
                    for (int j = 0; j < numSerialsPerThread; j++)
                    {
                        threadSerials.Add(_generator.GenerateUniqueSerial());
                    }
                    lock (allSerials)
                    {
                        foreach (var serial in threadSerials)
                        {
                            allSerials.Add(serial);
                        }
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert
            Assert.AreEqual(numThreads * numSerialsPerThread, allSerials.Count, 
                "All serials should be unique across threads");
        }
    }
}
