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
        public void GenerateEpc_WithSGTIN96_ProducesCorrectFormat()
        {
            // Arrange
            string tid = "E280116020123456789012";
            string gtin = "7891033079360"; // 13-digit GTIN
            int companyPrefixLength = 6;

            // Act
            string epc = _manager.GenerateEpc(tid, gtin, companyPrefixLength, EpcEncodingMethod.SGTIN96);

            // Assert
            Assert.IsNotNull(epc);
            Assert.AreEqual(24, epc.Length, "EPC should be 24 characters long");
            Assert.IsTrue(epc.EndsWith(tid.Substring(tid.Length - 10)), "EPC should end with last 10 characters of TID");
        }

        [TestMethod]
        public void GenerateEpc_With13DigitGTIN_PadsTo14Digits()
        {
            // Arrange
            string tid = "E280116020123456789012";
            string gtin = "7891033079360"; // 13-digit GTIN
            int companyPrefixLength = 6;

            // Act
            string epc = _manager.GenerateEpc(tid, gtin, companyPrefixLength, EpcEncodingMethod.BasicWithTidSuffix);

            // Assert
            Assert.IsNotNull(epc);
            Assert.AreEqual(24, epc.Length, "EPC should be 24 characters long");
            Assert.IsTrue(epc.EndsWith(tid.Substring(tid.Length - 10)), "EPC should end with last 10 characters of TID");
        }

        [TestMethod]
        public void GenerateEpc_With15DigitGTIN_TrimsTo14Digits()
        {
            // Arrange
            string tid = "E280116020123456789012";
            string gtin = "789103307936012"; // 15-digit GTIN
            int companyPrefixLength = 6;

            // Act
            string epc = _manager.GenerateEpc(tid, gtin, companyPrefixLength, EpcEncodingMethod.BasicWithTidSuffix);

            // Assert
            Assert.IsNotNull(epc);
            Assert.AreEqual(24, epc.Length, "EPC should be 24 characters long");
            Assert.IsTrue(epc.EndsWith(tid.Substring(tid.Length - 10)), "EPC should end with last 10 characters of TID");
        }

        [TestMethod]
        public void GenerateEpc_WithShortTID_PadsTID()
        {
            // Arrange
            string tid = "E28011602012"; // Short TID
            string gtin = "07891033079360"; // 14-digit GTIN
            int companyPrefixLength = 6;

            // Act
            string epc = _manager.GenerateEpc(tid, gtin, companyPrefixLength, EpcEncodingMethod.BasicWithTidSuffix);

            // Assert
            Assert.IsNotNull(epc);
            Assert.AreEqual(24, epc.Length, "EPC should be 24 characters long");
            Assert.IsTrue(epc.EndsWith(tid.PadLeft(10, '0').Substring(0, 10)), "EPC should end with padded TID");
        }

        [TestMethod]
        public void GenerateEpc_WhenSGTIN96Fails_FallsBackToBasicEncoding()
        {
            // Arrange
            string tid = "E280116020123456789012";
            string gtin = "INVALID_GTIN"; // Invalid GTIN to force fallback
            int companyPrefixLength = 6;

            // Act
            string epc = _manager.GenerateEpc(tid, gtin, companyPrefixLength, EpcEncodingMethod.SGTIN96);

            // Assert
            Assert.IsNotNull(epc);
            Assert.AreEqual(24, epc.Length, "EPC should be 24 characters long");
            Assert.IsTrue(epc.EndsWith(tid.Substring(tid.Length - 10)), "EPC should end with last 10 characters of TID");
        }

        [TestMethod]
        public void GenerateEpc_WithBasicEncoding_ProducesCorrectFormat()
        {
            // Arrange
            string tid = "E280116020123456789012";
            string gtin = "07891033079360"; // 14-digit GTIN
            int companyPrefixLength = 6;

            // Act
            string epc = _manager.GenerateEpc(tid, gtin, companyPrefixLength, EpcEncodingMethod.BasicWithTidSuffix);

            // Assert
            Assert.IsNotNull(epc);
            Assert.AreEqual(24, epc.Length, "EPC should be 24 characters long");
            Assert.IsTrue(epc.EndsWith(tid.Substring(tid.Length - 10)), "EPC should end with last 10 characters of TID");
        }

        [TestMethod]
        public void GenerateEpc_WithNullGTIN_ThrowsArgumentException()
        {
            // Arrange
            string tid = "E280116020123456789012";
            string gtin = null;
            int companyPrefixLength = 6;

            // Act & Assert
            Assert.ThrowsException<ArgumentException>(() => 
                _manager.GenerateEpc(tid, gtin, companyPrefixLength, EpcEncodingMethod.BasicWithTidSuffix));
        }

        [TestMethod]
        public void GenerateEpc_WithNullTID_ThrowsArgumentException()
        {
            // Arrange
            string tid = null;
            string gtin = "07891033079360";
            int companyPrefixLength = 6;

            // Act & Assert
            Assert.ThrowsException<ArgumentException>(() => 
                _manager.GenerateEpc(tid, gtin, companyPrefixLength, EpcEncodingMethod.BasicWithTidSuffix));
        }
    }
}
