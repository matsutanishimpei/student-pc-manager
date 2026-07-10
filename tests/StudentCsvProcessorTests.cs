using System;
using System.Collections.Generic;
using client;
using Xunit;

namespace Tests
{
    public class StudentCsvProcessorTests
    {
        [Fact]
        public void ParseCsv_ValidLines_ParsesCorrectly()
        {
            // Arrange
            var lines = new[]
            {
                "PC-STUDENT-01,Yamada Taro",
                "PC-STUDENT-02,Sato Hanako",
                "\"PC-STUDENT-03\",\"Suzuki Ichiro\"", // quoted fields
                "   " // empty line
            };

            // Act
            var result = StudentCsvProcessor.ParseCsv(lines);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(3, result.Count);
            Assert.Equal("PC-STUDENT-01", result[0].Key);
            Assert.Equal("Yamada Taro", result[0].StudentName);
            Assert.Equal("PC-STUDENT-02", result[1].Key);
            Assert.Equal("Sato Hanako", result[1].StudentName);
            Assert.Equal("PC-STUDENT-03", result[2].Key);
            Assert.Equal("Suzuki Ichiro", result[2].StudentName);
        }

        [Fact]
        public void ParseCsv_InvalidLines_SkipsThem()
        {
            // Arrange
            var lines = new[]
            {
                "PC-STUDENT-01", // missing value
                ",Yamada Taro",  // missing key
                "   ,   ",       // empty values
                "PC-STUDENT-02,Sato Hanako,ExtraField" // extra field (should still parse first 2)
            };

            // Act
            var result = StudentCsvProcessor.ParseCsv(lines);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Equal("PC-STUDENT-02", result[0].Key);
            Assert.Equal("Sato Hanako", result[0].StudentName);
        }

        [Theory]
        [InlineData("Yamada", "Yamada")]
        [InlineData("Yamada,Taro", "\"Yamada,Taro\"")] // contains comma
        [InlineData("Yamada\"Taro", "\"Yamada\"\"Taro\"")] // contains quote
        [InlineData("Yamada\nTaro", "\"Yamada\nTaro\"")] // contains newline
        [InlineData("", "")]
        [InlineData(null, "")]
        public void EscapeField_HandlesEscaping(string? input, string expected)
        {
            // Act
            string result = StudentCsvProcessor.EscapeField(input!);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void BuildCsvRow_BuildsCorrectRow()
        {
            // Arrange
            string key = "PC-STUDENT-01";
            string student = "Yamada, Taro"; // Comma needs escaping
            string ip = "192.168.1.10";
            string name = "PC01";
            string group = "Group A";

            // Act
            string row = StudentCsvProcessor.BuildCsvRow(key, student, ip, name, group);

            // Assert
            Assert.Equal("PC-STUDENT-01,\"Yamada, Taro\",192.168.1.10,PC01,Group A", row);
        }
    }
}
