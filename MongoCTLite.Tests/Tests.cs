using MongoCTLite.Abstractions;
using MongoCTLite.Tests;
using MongoCTLite.Tracking;
using MongoDB.Bson;
using MongoDB.Driver;
using Shouldly;

namespace MongoCTLite.Tests;

[CollectionDefinition(nameof(MongoCollection))]
public sealed class MongoCollection : ICollectionFixture<MongoFixture> {}

[Collection(nameof(MongoCollection))]
public sealed partial class Tests
{
    private readonly IMongoCollection<Player> _col;
    private readonly IMongoCollection<BsonDocument> _bsonCol;

    public Tests(MongoFixture fx)
    {
        _col = fx.Db.GetCollection<Player>("players_" + ObjectId.GenerateNewId().ToString()[..6]);
        _bsonCol = fx.Db.GetCollection<BsonDocument>(_col.CollectionNamespace.CollectionName);
        _col.Indexes.CreateOne(new CreateIndexModel<Player>(Builders<Player>.IndexKeys.Ascending(x => x.Id)));
    }

    [Fact]
    public async Task SaveChanges_succeeds_once_and_conflicts_on_stale_version()
    {
        // seed
        var p = new Player { Id = ObjectId.GenerateNewId(), version = 0, level = 1, gold = 100 };
        await _col.InsertOneAsync(p);

        // attach & modify
        var ctx = new TrackingContext();
        ctx.Attach(_col, p, expectedVersion: 0);
        p.level = 2;
        p.gold  = 90;

        // Save changes (should succeed)
        var policy = new DiffPolicy(AllowIncPath: path => path == "gold");
        var n1 = await ctx.SaveChangesAsync(policy, new NoopLogger());
        n1.ShouldBe(1);

        // Verify DB: version = 1
        var doc = await _bsonCol.Find(Builders<BsonDocument>.Filter.Eq("_id", p.Id)).FirstAsync();
        doc["version"].AsInt64.ShouldBe(1);
        doc["level"].AsInt32.ShouldBe(2);
        doc["gold"].AsInt64.ShouldBe(90);

        var ctx2 = new TrackingContext();
        ctx2.Attach(_col, p, expectedVersion: 0);
        p.level = 3;

        await Should.ThrowAsync<ConcurrencyConflictException>(async () =>
        {
            await ctx2.SaveChangesAsync(policy, new NoopLogger());
        });
    }

    [Fact]
    public async Task Replace_preserves_id_and_increments_version()
    {
        var p = new Player { Id = ObjectId.GenerateNewId(), version = 3, level = 10, gold = 50 };
        await _col.InsertOneAsync(p);

        // attach & large change → force replace path
        var ctx = new TrackingContext();
        ctx.Attach(_col, p, expectedVersion: 3);
        p.level = 99;
        p.gold  = 1;

        var policy = new DiffPolicy(MaxFieldOpsBeforeDocReplace: 0);
        var n = await ctx.SaveChangesAsync(policy, new NoopLogger());
        n.ShouldBe(1);

        var doc = await _bsonCol.Find(Builders<BsonDocument>.Filter.Eq("_id", p.Id)).FirstAsync();
        doc["_id"].AsObjectId.ShouldBe(p.Id);
        doc["version"].AsInt64.ShouldBe(4);
        doc["level"].AsInt32.ShouldBe(99);
        doc["gold"].AsInt64.ShouldBe(1);
    }

    [Fact]
    public async Task Attach_SameDocumentTwice_ThrowsException()
    {
        // Arrange
        var p = new Player { Id = ObjectId.GenerateNewId(), version = 1, level = 10, gold = 100 };
        await _col.InsertOneAsync(p);

        var ctx = new TrackingContext();
        ctx.Attach(_col, p, expectedVersion: 1);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() =>
        {
            ctx.Attach(_col, p, expectedVersion: 1); // Same document again
        });
    }

    [Fact]
    public async Task SaveChanges_NoChanges_ReturnsZero()
    {
        // Arrange
        var p = new Player { Id = ObjectId.GenerateNewId(), version = 5, level = 10, gold = 100 };
        await _col.InsertOneAsync(p);

        var ctx = new TrackingContext();
        ctx.Attach(_col, p, expectedVersion: 5);
        // No modifications made

        // Act
        var result = await ctx.SaveChangesAsync(new DiffPolicy(), new NoopLogger());

        // Assert
        result.ShouldBe(0);
    }

    [Fact]
    public async Task SaveChanges_IncOperationPolicy_UsesIncrement()
    {
        // Arrange
        var p = new Player { Id = ObjectId.GenerateNewId(), version = 1, level = 10, gold = 100 };
        await _col.InsertOneAsync(p);

        var ctx = new TrackingContext();
        ctx.Attach(_col, p, expectedVersion: 1);
        p.gold += 50; // Should use $inc if policy allows

        // Act
        var policy = new DiffPolicy(AllowIncPath: path => path == "gold");
        var result = await ctx.SaveChangesAsync(policy, new NoopLogger());

        // Assert
        result.ShouldBe(1);
        
        var doc = await _bsonCol.Find(Builders<BsonDocument>.Filter.Eq("_id", p.Id)).FirstAsync();
        doc["gold"].AsInt64.ShouldBe(150);
        doc["version"].AsInt64.ShouldBe(2);
    }

    [Fact]
    public async Task SaveChanges_MultipleDocuments_BatchesCorrectly()
    {
        // Arrange
        var players = Enumerable.Range(1, 5)
            .Select(i => new Player 
            { 
                Id = ObjectId.GenerateNewId(), 
                version = 1, 
                level = i * 10, 
                gold = i * 100 
            })
            .ToList();

        await _col.InsertManyAsync(players);

        var ctx = new TrackingContext();
        foreach (var p in players)
        {
            ctx.Attach(_col, p, expectedVersion: 1);
            p.level += 5; // Modify each player
        }

        // Act
        var result = await ctx.SaveChangesAsync(new DiffPolicy(), new NoopLogger());

        // Assert
        result.ShouldBe(5); // All 5 players updated

        foreach (var p in players)
        {
            var doc = await _bsonCol.Find(Builders<BsonDocument>.Filter.Eq("_id", p.Id)).FirstAsync();
            doc["version"].AsInt64.ShouldBe(2); // Version incremented
            doc["level"].AsInt32.ShouldBeGreaterThan(10); // Level modified
        }
    }
}
