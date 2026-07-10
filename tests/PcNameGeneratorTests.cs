using System;
using System.Collections.Generic;
using client;
using Xunit;

namespace Tests
{
    public class PcNameGeneratorTests
    {
        [Fact]
        public void GenerateNames_ValidRange_GeneratesNames()
        {
            // Arrange
            string prefix = "PC-STUDENT-";
            int startNum = 1;
            int endNum = 3;
            int digits = 2;
            string portText = "";

            // Act
            var result = PcNameGenerator.GenerateNames(prefix, startNum, endNum, digits, portText);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(3, result.Count);
            Assert.Equal("PC-STUDENT-01", result[0]);
            Assert.Equal("PC-STUDENT-02", result[1]);
            Assert.Equal("PC-STUDENT-03", result[2]);
        }

        [Fact]
        public void GenerateNames_WithPort_AddsPortToAll()
        {
            // Arrange
            string prefix = "PC-";
            int startNum = 10;
            int endNum = 11;
            int digits = 1;
            string portText = "5000";

            // Act
            var result = PcNameGenerator.GenerateNames(prefix, startNum, endNum, digits, portText);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Equal("PC-10:5000", result[0]);
            Assert.Equal("PC-11:5000", result[1]);
        }

        [Fact]
        public void GenerateNames_InvalidRange_ReturnsEmptyList()
        {
            // Arrange
            string prefix = "PC-";
            int startNum = 5;
            int endNum = 3; // start > end
            int digits = 2;
            string portText = "";

            // Act
            var result = PcNameGenerator.GenerateNames(prefix, startNum, endNum, digits, portText);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void GenerateNames_ZeroOrNegativeDigits_ReturnsEmptyList()
        {
            // Arrange
            string prefix = "PC-";
            int startNum = 1;
            int endNum = 3;
            int digits = 0; // invalid digits
            string portText = "";

            // Act
            var result = PcNameGenerator.GenerateNames(prefix, startNum, endNum, digits, portText);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }
    }
}
