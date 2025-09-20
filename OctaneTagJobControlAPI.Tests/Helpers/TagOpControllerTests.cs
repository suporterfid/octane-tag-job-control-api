using Microsoft.VisualStudio.TestTools.UnitTesting;
using OctaneTagWritingTest.Helpers;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Diagnostics;
using Impinj.OctaneSdk;

namespace OctaneTagJobControlAPI.Tests.Helpers
{
    [TestClass]
    public class TagOpControllerTests
    {
        private TagOpController _controller;

        [TestInitialize]
        public void Setup()
        {
            _controller = TagOpController.Instance;
            _controller.CleanUp(); // Ensure clean state for each test
        }

        [TestMethod]
        public void GetOrGenerateSerial_ReturnsUniqueSerials()
        {
            // Arrange
            string tid1 = "E280119012345678AABB";
            string tid2 = "E280119087654321CCDD";

            // Act
            string serial1 = _controller.GetOrGenerateSerial(tid1);
            string serial2 = _controller.GetOrGenerateSerial(tid2);

            // Assert
            Assert.IsNotNull(serial1);
            Assert.IsNotNull(serial2);
            Assert.AreNotEqual(serial1, serial2, "Serials should be unique for different TIDs");
            Assert.AreEqual(10, serial1.Length, "Serial should be 10 characters long");
            Assert.AreEqual(10, serial2.Length, "Serial should be 10 characters long");
        }

        [TestMethod]
        public void GetOrGenerateSerial_ReturnsSameSerialForSameTid()
        {
            // Arrange
            string tid = "E280119012345678AABB";

            // Act
            string serial1 = _controller.GetOrGenerateSerial(tid);
            string serial2 = _controller.GetOrGenerateSerial(tid);

            // Assert
            Assert.AreEqual(serial1, serial2, "Same TID should return same serial");
        }

        [TestMethod]
        public void RecordExpectedEpc_AndGetExpectedEpc_WorksCorrectly()
        {
            // Arrange
            string tid = "E280119012345678AABB";
            string expectedEpc = "B20099999999999ABCDEF1234";

            // Act
            _controller.RecordExpectedEpc(tid, expectedEpc);
            string retrievedEpc = _controller.GetExpectedEpc(tid);

            // Assert
            Assert.AreEqual(expectedEpc, retrievedEpc);
        }

        [TestMethod]
        public void GetNextEpcForTag_GeneratesUniqueEpcs()
        {
            // Arrange
            string tid1 = "E280119012345678AABB";
            string tid2 = "E280119087654321CCDD";
            string currentEpc = "B20099999999999ABCDEF1234";

            // Act
            string epc1 = _controller.GetNextEpcForTag(currentEpc, tid1);
            string epc2 = _controller.GetNextEpcForTag(currentEpc, tid2);

            // Assert
            Assert.IsNotNull(epc1);
            Assert.IsNotNull(epc2);
            Assert.AreNotEqual(epc1, epc2, "EPCs should be unique for different TIDs");
            Assert.AreEqual(24, epc1.Length, "EPC should be 24 characters long");
            Assert.AreEqual(24, epc2.Length, "EPC should be 24 characters long");
        }

        [TestMethod]
        public void RecordResult_UpdatesSuccessCount()
        {
            // Arrange
            string tid = "E280119012345678AABB";
            string result = "Success";

            // Act
            _controller.RecordResult(tid, result, true);

            // Assert
            Assert.AreEqual(1, _controller.GetSuccessCount());
            Assert.IsTrue(_controller.HasResult(tid));
        }

        [TestMethod]
        public void CleanUp_ClearsAllData()
        {
            // Arrange
            string tid = "E280119012345678AABB";
            string expectedEpc = "B20099999999999ABCDEF1234";
            _controller.RecordExpectedEpc(tid, expectedEpc);
            _controller.RecordResult(tid, "Success", true);

            // Act
            _controller.CleanUp();

            // Assert
            Assert.AreEqual(0, _controller.GetSuccessCount());
            Assert.IsFalse(_controller.HasResult(tid));
            Assert.IsNull(_controller.GetExpectedEpc(tid));
        }

        [TestMethod]
        public void IsTidProcessed_ReturnsTrueForProcessedTid()
        {
            // Arrange
            string tid = "E280119012345678AABB";
            string result = "Success";

            // Act
            _controller.RecordResult(tid, result, true);

            // Assert
            Assert.IsTrue(_controller.IsTidProcessed(tid));
        }

        [TestMethod]
        public void GetExistingEpc_ReturnsTrueForExistingEpc()
        {
            // Arrange
            string tid = "E280119012345678AABB";
            string expectedEpc = "B20099999999999ABCDEF1234";

            // Act
            _controller.RecordExpectedEpc(tid, expectedEpc);

            // Assert
            Assert.IsTrue(_controller.GetExistingEpc(expectedEpc));
        }

        [TestMethod]
        public void ProcessVerificationResult_HandlesSuccessCorrectly()
        {
            // Arrange
            string tid = "E280119012345678AABB";
            string expectedEpc = "B20099999999999ABCDEF1234";
            _controller.RecordExpectedEpc(tid, expectedEpc);

            var recoveryCount = new ConcurrentDictionary<string, int>();
            var swWrite = new Stopwatch();
            var swVerify = new Stopwatch();
            var logFile = "test_log.csv";

            var tag = new Tag
            {
                Tid = TagData.FromHexString(tid),
                Epc = TagData.FromHexString(expectedEpc)
            };

            var readResult = new TagReadOpResult
            {
                Tag = tag,
                Data = TagData.FromHexString(expectedEpc),
                Result = TagOpResult.Success
            };

            // Act
            _controller.ProcessVerificationResult(readResult, tid, recoveryCount, swWrite, swVerify, 
                logFile, null, CancellationToken.None, "00000000", 3);

            // Assert
            Assert.IsTrue(_controller.HasResult(tid));
            Assert.AreEqual(1, _controller.GetSuccessCount());
        }
    }
}
