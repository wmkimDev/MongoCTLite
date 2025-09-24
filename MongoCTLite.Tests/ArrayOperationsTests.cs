using MongoCTLite.Abstractions;
using MongoCTLite.Tracking;
using MongoDB.Bson;
using MongoDB.Driver;
using Shouldly;

namespace MongoCTLite.Tests;

[Collection(nameof(MongoCollection))]
public sealed class ArrayOperationsTests
{
    private readonly IMongoCollection<Player> _col;
    private readonly IMongoCollection<BsonDocument> _bsonCol;

    public ArrayOperationsTests(MongoFixture fx)
    {
        var collectionName = "array_players_" + ObjectId.GenerateNewId().ToString()[..6];
        _col = fx.Db.GetCollection<Player>(collectionName);
        _bsonCol = fx.Db.GetCollection<BsonDocument>(collectionName);
    }

    [Fact]
    public async Task SaveChanges_ArrayAppend_UsesPushOperation()
    {
        // Arrange
        var p = new Player 
        { 
            Id = ObjectId.GenerateNewId(), 
            version = 1, 
            level = 10, 
            gold = 100,
            items = new List<string> { "sword", "shield" }
        };
        await _col.InsertOneAsync(p);

        var ctx = new TrackingContext();
        ctx.Attach(_col, p, expectedVersion: 1);

        // Act - Add items (append-only)
        p.items.Add("potion");
        p.items.Add("bow");

        var result = await ctx.SaveChangesAsync(new DiffPolicy(), new NoopLogger());

        // Assert
        result.ShouldBe(1);
        
        var doc = await _bsonCol.Find(Builders<BsonDocument>.Filter.Eq("_id", p.Id)).FirstAsync();
        var items = doc["items"].AsBsonArray.Select(x => x.AsString).ToList();
        items.ShouldBe(new[] { "sword", "shield", "potion", "bow" });
        doc["version"].AsInt64.ShouldBe(2);
    }

    [Fact]
    public async Task SaveChanges_ArrayRemove_UsesPullOperation()
    {
        // Arrange
        var p = new Player 
        { 
            Id = ObjectId.GenerateNewId(), 
            version = 1, 
            level = 10, 
            gold = 100,
            items = new List<string> { "sword", "shield", "potion", "bow" }
        };
        await _col.InsertOneAsync(p);

        var ctx = new TrackingContext();
        ctx.Attach(_col, p, expectedVersion: 1);

        // Act - Remove items (remove-only)
        p.items.Remove("potion");
        p.items.Remove("bow");

        var result = await ctx.SaveChangesAsync(new DiffPolicy(), new NoopLogger());

        // Assert
        result.ShouldBe(1);
        
        var doc = await _bsonCol.Find(Builders<BsonDocument>.Filter.Eq("_id", p.Id)).FirstAsync();
        var items = doc["items"].AsBsonArray.Select(x => x.AsString).ToList();
        items.ShouldBe(new[] { "sword", "shield" });
        doc["version"].AsInt64.ShouldBe(2);
    }

    [Fact]
    public async Task SaveChanges_ArrayRemove_WithDuplicates_RemovesCorrectCount()
    {
        var p = new Player
        {
            Id = ObjectId.GenerateNewId(),
            version = 1,
            level = 10,
            gold = 100,
            items = new List<string> { "sword", "sword", "shield" }
        };
        await _col.InsertOneAsync(p);

        var ctx = new TrackingContext();
        ctx.Attach(_col, p, expectedVersion: 1);

        // Remove a single duplicate and one unique item
        p.items.Remove("sword");
        p.items.Remove("shield");

        var result = await ctx.SaveChangesAsync(new DiffPolicy(), new NoopLogger());

        result.ShouldBe(1);

        var doc = await _bsonCol.Find(Builders<BsonDocument>.Filter.Eq("_id", p.Id)).FirstAsync();
        var items = doc["items"].AsBsonArray.Select(x => x.AsString).ToList();
        items.ShouldBe(new[] { "sword" });
        doc["version"].AsInt64.ShouldBe(2);
    }

    [Fact]
    public async Task SaveChanges_ArrayMixedChanges_UsesReplacement()
    {
        // Arrange
        var p = new Player 
        { 
            Id = ObjectId.GenerateNewId(), 
            version = 1, 
            level = 10, 
            gold = 100,
            items = new List<string> { "sword", "shield", "potion" }
        };
        await _col.InsertOneAsync(p);

        var ctx = new TrackingContext();
        ctx.Attach(_col, p, expectedVersion: 1);

        // Act - Mixed changes (not append-only or remove-only)
        p.items.Remove("shield");
        p.items.Add("bow");
        p.items[0] = "magic_sword"; // Modify existing item

        var result = await ctx.SaveChangesAsync(new DiffPolicy(), new NoopLogger());

        // Assert
        result.ShouldBe(1);
        
        var doc = await _bsonCol.Find(Builders<BsonDocument>.Filter.Eq("_id", p.Id)).FirstAsync();
        var items = doc["items"].AsBsonArray.Select(x => x.AsString).ToList();
        items.ShouldBe(new[] { "magic_sword", "potion", "bow" });
        doc["version"].AsInt64.ShouldBe(2);
    }

    [Fact]
    public async Task SaveChanges_EmptyArrayToItems_Works()
    {
        // Arrange
        var p = new Player 
        { 
            Id = ObjectId.GenerateNewId(), 
            version = 1, 
            level = 10, 
            gold = 100,
            items = new List<string>() // Empty array
        };
        await _col.InsertOneAsync(p);

        var ctx = new TrackingContext();
        ctx.Attach(_col, p, expectedVersion: 1);

        // Act - Add to empty array
        p.items.Add("first_item");
        p.items.Add("second_item");

        var result = await ctx.SaveChangesAsync(new DiffPolicy(), new NoopLogger());

        // Assert
        result.ShouldBe(1);
        
        var doc = await _bsonCol.Find(Builders<BsonDocument>.Filter.Eq("_id", p.Id)).FirstAsync();
        var items = doc["items"].AsBsonArray.Select(x => x.AsString).ToList();
        items.ShouldBe(new[] { "first_item", "second_item" });
        doc["version"].AsInt64.ShouldBe(2);
    }
}
