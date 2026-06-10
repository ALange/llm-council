using System.Text.Json;
using LlmCouncil.Models;
using LlmCouncil.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LlmCouncil.Tests;

/// <summary>
/// Tests for StorageService that use an isolated temp directory.
/// </summary>
public class StorageServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StorageService _storage;

    public StorageServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"llm-council-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);

        var options = Options.Create(new CouncilOptions { DataDir = _tempDir });
        _storage = new StorageService(options, NullLogger<StorageService>.Instance);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void CreateConversation_WritesJsonFile()
    {
        var id = Guid.NewGuid().ToString();
        var conv = _storage.CreateConversation(id);

        Assert.Equal(id, conv.Id);
        Assert.Equal("New Conversation", conv.Title);
        Assert.Empty(conv.Messages);
        Assert.True(File.Exists(Path.Combine(_tempDir, $"{id}.json")));
    }

    [Fact]
    public void GetConversation_ThrowsForNonGuidId()
    {
        Assert.Throws<ArgumentException>(() => _storage.GetConversation("../../etc/passwd"));
    }

    [Fact]
    public void GetConversation_ReturnsNullForMissingId()
    {
        var result = _storage.GetConversation(Guid.NewGuid().ToString());
        Assert.Null(result);
    }

    [Fact]
    public void GetConversation_ReturnsExistingConversation()
    {
        var id = Guid.NewGuid().ToString();
        _storage.CreateConversation(id);

        var loaded = _storage.GetConversation(id);
        Assert.NotNull(loaded);
        Assert.Equal(id, loaded!.Id);
    }

    [Fact]
    public void AddUserMessage_AppendsMessage()
    {
        var id = Guid.NewGuid().ToString();
        _storage.CreateConversation(id);

        _storage.AddUserMessage(id, "Hello, council!");

        var loaded = _storage.GetConversation(id);
        Assert.Single(loaded!.Messages);
    }

    [Fact]
    public void AddUserMessage_ThrowsForMissingConversation()
    {
        Assert.Throws<KeyNotFoundException>(() =>
            _storage.AddUserMessage(Guid.NewGuid().ToString(), "content"));
    }

    [Fact]
    public void UpdateConversationTitle_PersistsNewTitle()
    {
        var id = Guid.NewGuid().ToString();
        _storage.CreateConversation(id);

        _storage.UpdateConversationTitle(id, "My New Title");

        var loaded = _storage.GetConversation(id);
        Assert.Equal("My New Title", loaded!.Title);
    }

    [Fact]
    public void ListConversations_ReturnsSortedByCreationTimeDescending()
    {
        var id1 = Guid.NewGuid().ToString();
        var id2 = Guid.NewGuid().ToString();

        var conv1 = _storage.CreateConversation(id1);
        // Ensure different timestamps by introducing a slight delay
        Thread.Sleep(10);
        var conv2 = _storage.CreateConversation(id2);

        var list = _storage.ListConversations();
        Assert.Equal(2, list.Count);
        // Most recently created should be first
        Assert.Equal(id2, list[0].Id);
        Assert.Equal(id1, list[1].Id);
    }

    [Fact]
    public void ListConversations_ReturnsCorrectMessageCount()
    {
        var id = Guid.NewGuid().ToString();
        _storage.CreateConversation(id);
        _storage.AddUserMessage(id, "first");
        _storage.AddUserMessage(id, "second");

        var list = _storage.ListConversations();
        var meta = list.Single(c => c.Id == id);
        Assert.Equal(2, meta.MessageCount);
    }
}
