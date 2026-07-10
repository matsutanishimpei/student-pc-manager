using System;
using System.IO;
using Server.Services;
using Xunit;

namespace Tests
{
    public class LogTests
    {
        [Fact]
        public void Write_WritesMessageToLogFile()
        {
            // Arrange
            string logPath = "C:\\Users\\Public\\sendCMD_server_log.txt";
            string testMessage = "Test message for xUnit LogTests - " + Guid.NewGuid().ToString();

            // Act
            Log.Write(testMessage);

            // Assert
            Assert.True(File.Exists(logPath), "Log file should exist");
            string logContent = File.ReadAllText(logPath);
            Assert.Contains(testMessage, logContent);
        }

        [Fact]
        public void Write_NullMessage_DoesNotThrow()
        {
            // Act & Assert
            var exception = Record.Exception(() => Log.Write(null!));
            Assert.Null(exception);
        }
    }
}
