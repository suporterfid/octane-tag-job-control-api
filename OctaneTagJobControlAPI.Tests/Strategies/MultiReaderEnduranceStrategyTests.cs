using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OctaneTagJobControlAPI.Models;
using OctaneTagJobControlAPI.Strategies;
using OctaneTagJobControlAPI.Strategies.Base.Configuration;

namespace OctaneTagJobControlAPI.Tests.Strategies
{
    [TestClass]
    public class MultiReaderEnduranceStrategyTests
    {
        [TestMethod]
        public void Constructor_WithDetectorOnly_InitializesCorrectly()
        {
            // Arrange
            var readerSettings = new Dictionary<string, ReaderSettings>
            {
                ["detector"] = new ReaderSettings { Hostname = "detector1" }
            };

            // Act
            var strategy = new MultiReaderEnduranceStrategy(
                "detector1", // detector
                null,       // writer
                null,       // verifier
                "test.log",
                readerSettings);

            // Assert
            var status = strategy.GetStatus();
            Assert.IsTrue(status.Metrics.ContainsKey("HasDetectorRole"));
            Assert.IsTrue((bool)status.Metrics["HasDetectorRole"]);
            Assert.IsFalse((bool)status.Metrics["HasWriterRole"]);
            Assert.IsFalse((bool)status.Metrics["HasVerifierRole"]);

            var readerMetrics = status.Metrics["ReaderMetrics"] as Dictionary<string, ReaderMetrics>;
            Assert.IsNotNull(readerMetrics);
            Assert.IsTrue(readerMetrics.ContainsKey("detector"));
            Assert.AreEqual("detector", readerMetrics["detector"].Role);
            Assert.AreEqual("detector1", readerMetrics["detector"].Hostname);
        }

        [TestMethod]
        public void Constructor_WithWriterOnly_InitializesCorrectly()
        {
            // Arrange
            var readerSettings = new Dictionary<string, ReaderSettings>
            {
                ["writer"] = new ReaderSettings { Hostname = "writer1" }
            };

            // Act
            var strategy = new MultiReaderEnduranceStrategy(
                null,       // detector
                "writer1",  // writer
                null,       // verifier
                "test.log",
                readerSettings);

            // Assert
            var status = strategy.GetStatus();
            Assert.IsFalse((bool)status.Metrics["HasDetectorRole"]);
            Assert.IsTrue((bool)status.Metrics["HasWriterRole"]);
            Assert.IsFalse((bool)status.Metrics["HasVerifierRole"]);

            var readerMetrics = status.Metrics["ReaderMetrics"] as Dictionary<string, ReaderMetrics>;
            Assert.IsNotNull(readerMetrics);
            Assert.IsTrue(readerMetrics.ContainsKey("writer"));
            Assert.AreEqual("writer", readerMetrics["writer"].Role);
            Assert.AreEqual("writer1", readerMetrics["writer"].Hostname);
        }

        [TestMethod]
        public void Constructor_WithAllRoles_InitializesCorrectly()
        {
            // Arrange
            var readerSettings = new Dictionary<string, ReaderSettings>
            {
                ["detector"] = new ReaderSettings { Hostname = "detector1" },
                ["writer"] = new ReaderSettings { Hostname = "writer1" },
                ["verifier"] = new ReaderSettings { Hostname = "verifier1" }
            };

            // Act
            var strategy = new MultiReaderEnduranceStrategy(
                "detector1",  // detector
                "writer1",    // writer
                "verifier1",  // verifier
                "test.log",
                readerSettings);

            // Assert
            var status = strategy.GetStatus();
            Assert.IsTrue((bool)status.Metrics["HasDetectorRole"]);
            Assert.IsTrue((bool)status.Metrics["HasWriterRole"]);
            Assert.IsTrue((bool)status.Metrics["HasVerifierRole"]);

            var readerMetrics = status.Metrics["ReaderMetrics"] as Dictionary<string, ReaderMetrics>;
            Assert.IsNotNull(readerMetrics);
            
            Assert.IsTrue(readerMetrics.ContainsKey("detector"));
            Assert.AreEqual("detector", readerMetrics["detector"].Role);
            Assert.AreEqual("detector1", readerMetrics["detector"].Hostname);

            Assert.IsTrue(readerMetrics.ContainsKey("writer"));
            Assert.AreEqual("writer", readerMetrics["writer"].Role);
            Assert.AreEqual("writer1", readerMetrics["writer"].Hostname);

            Assert.IsTrue(readerMetrics.ContainsKey("verifier"));
            Assert.AreEqual("verifier", readerMetrics["verifier"].Role);
            Assert.AreEqual("verifier1", readerMetrics["verifier"].Hostname);
        }

        [TestMethod]
        public void Constructor_WithInvalidSettings_ThrowsArgumentException()
        {
            // Arrange
            var readerSettings = new Dictionary<string, ReaderSettings>();

            // Act & Assert
            Assert.ThrowsException<ArgumentException>(() => new MultiReaderEnduranceStrategy(
                null,       // detector
                null,       // writer
                null,       // verifier
                "test.log",
                readerSettings));
        }

        [TestMethod]
        public void Constructor_WithGPISettings_InitializesCorrectly()
        {
            // Arrange
            var readerSettings = new Dictionary<string, ReaderSettings>
            {
                ["detector"] = new ReaderSettings 
                { 
                    Hostname = "detector1",
                    Parameters = new Dictionary<string, string>
                    {
                        ["enableGpiTrigger"] = "true",
                        ["gpiPort"] = "1"
                    }
                }
            };

            // Act
            var strategy = new MultiReaderEnduranceStrategy(
                "detector1", // detector
                null,       // writer
                null,       // verifier
                "test.log",
                readerSettings);

            // Assert
            var status = strategy.GetStatus();
            Assert.IsTrue((bool)status.Metrics["HasDetectorRole"]);
            Assert.AreEqual("true", status.Metrics["DetectorGpiEnabled"]);
            Assert.AreEqual("1", status.Metrics["DetectorGpiPort"]);
        }
    }
}
