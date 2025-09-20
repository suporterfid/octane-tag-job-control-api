using Microsoft.VisualStudio.TestTools.UnitTesting;
using OctaneTagWritingTest.Helpers;
using System;

namespace OctaneTagJobControlAPI.Tests.Helpers
{
    [TestClass]
    public class TagTidParserTests
    {
        [TestMethod]
        public void Constructor_WithValidTid_InitializesCorrectly()
        {
            // Arrange & Act
            string validTid = "E2801190AABBCCDDEE11";
            var parser = new TagTidParser(validTid);

            // Assert
            Assert.IsNotNull(parser);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullTid_ThrowsArgumentNullException()
        {
            // Arrange & Act
            new TagTidParser(null);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Constructor_WithInvalidLength_ThrowsArgumentException()
        {
            // Arrange & Act
            new TagTidParser("E280119"); // Too short
        }

        [TestMethod]
        public void Get40BitSerialHex_WithImpinjTag_ReturnsCorrectSerial()
        {
            // Arrange
            string tid = "E280119012345678AABB"; // Impinj Monza R6
            var parser = new TagTidParser(tid);

            // Act
            string serial = parser.Get40BitSerialHex();

            // Assert
            Assert.AreEqual("1234567AAB", serial);
        }

        [TestMethod]
        public void GetTagModelName_WithKnownModel_ReturnsCorrectName()
        {
            // Arrange
            string tid = "E280119012345678AABB"; // Impinj Monza R6
            var parser = new TagTidParser(tid);

            // Act
            string modelName = parser.GetTagModelName();

            // Assert
            Assert.AreEqual("Impinj Monza R6", modelName);
        }

        [TestMethod]
        public void GetVendorFromTid_WithKnownPrefix_ReturnsCorrectVendor()
        {
            // Arrange
            string tid = "E280119012345678AABB"; // Impinj Monza R6
            var parser = new TagTidParser(tid);

            // Act
            string vendor = parser.GetVendorFromTid();

            // Assert
            Assert.AreEqual("Impinj Monza R6", vendor);
        }

        [TestMethod]
        public void GetVendorFromTid_WithUnknownPrefix_ReturnsUnknown()
        {
            // Arrange
            string tid = "A180119012345678AABB"; // Unknown prefix
            var parser = new TagTidParser(tid);

            // Act
            string vendor = parser.GetVendorFromTid();

            // Assert
            Assert.AreEqual("Desconhecido", vendor);
        }

        [TestMethod]
        public void Dispose_CanBeCalledMultipleTimes_WithoutException()
        {
            // Arrange
            string tid = "E280119012345678AABB";
            var parser = new TagTidParser(tid);

            // Act & Assert - should not throw
            parser.Dispose();
            parser.Dispose(); // Second dispose should not throw
        }
    }
}
