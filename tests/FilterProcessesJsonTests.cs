using Microsoft.Extensions.Configuration;
using Server.Services;
using System.Text.Json;
using Xunit;

namespace Tests;

public class FilterProcessesJsonTests
{
    /// <summary>
    /// Helper to build an AdaptiveSessionExecutor with a given exclude list.
    /// </summary>
    private static AdaptiveSessionExecutor CreateExecutor(params string[] excludeProcesses)
    {
        var dict = new Dictionary<string, string?>();
        for (int i = 0; i < excludeProcesses.Length; i++)
        {
            dict[$"ExcludeProcesses:{i}"] = excludeProcesses[i];
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();

        return new AdaptiveSessionExecutor(config);
    }

    /// <summary>
    /// Helper to create a JSON array of process items.
    /// </summary>
    private static string MakeProcessesJson(params (string name, int id, string title)[] processes)
    {
        var items = processes.Select(p => new
        {
            ProcessName = p.name,
            Id = p.id,
            MainWindowTitle = p.title
        }).ToArray();

        return JsonSerializer.Serialize(items);
    }

    // --- 空・null 入力のハンドリング ---

    [Fact]
    public void EmptyArray_ReturnsAsIs()
    {
        var executor = CreateExecutor("notepad");
        string result = executor.FilterProcessesJson("[]");
        Assert.Equal("[]", result);
    }

    [Fact]
    public void EmptyString_ReturnsAsIs()
    {
        var executor = CreateExecutor("notepad");
        string result = executor.FilterProcessesJson("");
        Assert.Equal("", result);
    }

    [Fact]
    public void NullString_ReturnsAsIs()
    {
        var executor = CreateExecutor("notepad");
        string result = executor.FilterProcessesJson(null!);
        Assert.Null(result);
    }

    // --- 除外リストが空の場合 ---

    [Fact]
    public void NoExclusions_ReturnsOriginal()
    {
        var executor = CreateExecutor(); // No exclusions
        string input = MakeProcessesJson(
            ("chrome", 1, "Google Chrome"),
            ("notepad", 2, "Untitled"));

        string result = executor.FilterProcessesJson(input);
        Assert.Equal(input, result);
    }

    // --- フィルタリングの基本動作 ---

    [Fact]
    public void ExcludesMatchingProcesses()
    {
        var executor = CreateExecutor("TextInputHost", "SystemSettings");
        string input = MakeProcessesJson(
            ("chrome", 1, "Google Chrome"),
            ("TextInputHost", 2, "TextInputHost"),
            ("notepad", 3, "Untitled"),
            ("SystemSettings", 4, "Settings"));

        string result = executor.FilterProcessesJson(input);

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var filtered = JsonSerializer.Deserialize<AdaptiveSessionExecutor.ProcessItem[]>(result, options);

        Assert.NotNull(filtered);
        Assert.Equal(2, filtered!.Length);
        Assert.Equal("chrome", filtered[0].ProcessName);
        Assert.Equal("notepad", filtered[1].ProcessName);
    }

    [Fact]
    public void CaseInsensitive_Exclusion()
    {
        var executor = CreateExecutor("NOTEPAD");
        string input = MakeProcessesJson(
            ("chrome", 1, "Google Chrome"),
            ("notepad", 2, "Untitled - Notepad"));

        string result = executor.FilterProcessesJson(input);

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var filtered = JsonSerializer.Deserialize<AdaptiveSessionExecutor.ProcessItem[]>(result, options);

        Assert.NotNull(filtered);
        Assert.Single(filtered!);
        Assert.Equal("chrome", filtered[0].ProcessName);
    }

    [Fact]
    public void AllExcluded_ReturnsEmptyArray()
    {
        var executor = CreateExecutor("chrome", "notepad");
        string input = MakeProcessesJson(
            ("chrome", 1, "Google Chrome"),
            ("notepad", 2, "Untitled"));

        string result = executor.FilterProcessesJson(input);

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var filtered = JsonSerializer.Deserialize<AdaptiveSessionExecutor.ProcessItem[]>(result, options);

        Assert.NotNull(filtered);
        Assert.Empty(filtered!);
    }

    [Fact]
    public void NoneExcluded_ReturnsAll()
    {
        var executor = CreateExecutor("firefox");
        string input = MakeProcessesJson(
            ("chrome", 1, "Google Chrome"),
            ("notepad", 2, "Untitled"));

        string result = executor.FilterProcessesJson(input);

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var filtered = JsonSerializer.Deserialize<AdaptiveSessionExecutor.ProcessItem[]>(result, options);

        Assert.NotNull(filtered);
        Assert.Equal(2, filtered!.Length);
    }

    // --- エッジケースとエラーハンドリング ---

    [Fact]
    public void InvalidJson_ReturnsOriginal()
    {
        var executor = CreateExecutor("notepad");
        string badJson = "{this is not valid JSON!!!}";

        string result = executor.FilterProcessesJson(badJson);

        // Should return the original string without throwing
        Assert.Equal(badJson, result);
    }

    [Fact]
    public void SingleProcess_Excluded()
    {
        var executor = CreateExecutor("notepad");
        string input = MakeProcessesJson(("notepad", 1, "Untitled"));

        string result = executor.FilterProcessesJson(input);

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var filtered = JsonSerializer.Deserialize<AdaptiveSessionExecutor.ProcessItem[]>(result, options);

        Assert.NotNull(filtered);
        Assert.Empty(filtered!);
    }

    [Fact]
    public void SingleProcess_NotExcluded()
    {
        var executor = CreateExecutor("firefox");
        string input = MakeProcessesJson(("chrome", 1, "Google Chrome"));

        string result = executor.FilterProcessesJson(input);

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var filtered = JsonSerializer.Deserialize<AdaptiveSessionExecutor.ProcessItem[]>(result, options);

        Assert.NotNull(filtered);
        Assert.Single(filtered!);
        Assert.Equal("chrome", filtered![0].ProcessName);
    }

    [Fact]
    public void ManyExcludePatterns_CorrectlyFilters()
    {
        var executor = CreateExecutor(
            "TextInputHost", "ApplicationFrameHost",
            "SystemSettings", "RtkUWP");

        string input = MakeProcessesJson(
            ("chrome", 1, "Google Chrome"),
            ("TextInputHost", 2, ""),
            ("ApplicationFrameHost", 3, ""),
            ("notepad", 4, "Untitled"),
            ("SystemSettings", 5, "Settings"),
            ("RtkUWP", 6, "Realtek Audio"),
            ("explorer", 7, "Desktop"));

        string result = executor.FilterProcessesJson(input);

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var filtered = JsonSerializer.Deserialize<AdaptiveSessionExecutor.ProcessItem[]>(result, options);

        Assert.NotNull(filtered);
        Assert.Equal(3, filtered!.Length);
        Assert.Contains(filtered, p => p.ProcessName == "chrome");
        Assert.Contains(filtered, p => p.ProcessName == "notepad");
        Assert.Contains(filtered, p => p.ProcessName == "explorer");
    }
}
