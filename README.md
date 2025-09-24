# MongoCTLite

Lightweight change tracking and diff-based updates for MongoDB with optimistic concurrency based on a required `version` field.

## Features

- **Change Tracking**  
  Track entities in memory, snapshot their original state, and compute diffs (`$set`, `$unset`, `$inc`, `$push`, `$pull`) automatically.

- **Optimistic Concurrency**  
  Every update uses a filter with `id` + `version`.  
  If the `version` mismatches, the update is rejected with a `ConcurrencyConflictException`.

- **ID Field**  
  The identifier field does not have to be MongoDB’s default `_id`.  
  Annotate the member with `[MongoIdField]` (and `[MongoVersionField]`) to configure the tracked key and version column.

- **Diff Engine**  
  - Skips reserved keys (ID and `version`).  
  - Array handling: append/remove optimization, or full replace when the change ratio is high.  
  - Policy hooks (`AllowInc`, `AllowUnset`) to fine-tune behavior per field.

- **BulkWrite with Retry**  
  Groups changes per collection and executes bulk updates with retry on transient errors.  
  Optional transaction support (`SaveChangesOptions.UseTransaction`).

- **Version Field Required**  
  All tracked documents must contain a numeric `version` field. If absent, `Attach` throws an exception.


## Usage

### Entity Example

Annotate tracked documents once. The source generator picks up the attributes during build and registers the metadata automatically.

```cs
[MongoTrackedEntity]
public sealed class Player
{
    [BsonId]
    [MongoIdField] public ObjectId Id { get; set; }

    [MongoVersionField] public long version { get; set; }

    public int Level { get; set; }
    public long Gold { get; set; }
}
```

For alternative keys or version column names, place `[MongoIdField]` / `[MongoVersionField]` on the appropriate properties (they respect `[BsonElement]` when present).

> The analyzer packaged with MongoCTLite discovers these attributes at build time and emits a module initializer that registers the metadata automatically—no manual wiring required.

### Attaching and Saving
```cs
var ctx = new TrackingContext();

// Attach entity (snapshot taken)
ctx.Attach(players, player, expectedVersion: player.version);

// Modify in memory
player.Level += 1;
player.Gold += 100;

// Save changes (diffs automatically generated)
await ctx.SaveChangesAsync(
    DiffPolicy.WithInc(nameof(Player.Gold)),
    new ConsoleLogger());

// NOTE: TrackingContext clears its state after SaveChangesAsync and does not
//       mutate the in-memory entity's version. Update it manually or reload
//       from MongoDB if you need the new version value.
```

### Conflict Detection
```cs
try
{
    await ctx.SaveChangesAsync(new DiffPolicy(), new ConsoleLogger());
}
catch (ConcurrencyConflictException ex)
{
    Console.WriteLine($"Conflict count = {ex.ConflictCount}");
}
```
### Retrieving Tracked Entities

Inside the same TrackingContext, you can fetch an entity that was already attached:
```cs
// By type and id
var tracked = ctx.GetTrackedEntity<Player>(player.Id);

// Or safely with TryGet
if (ctx.TryGetTrackedEntity<Player>(player.Id, out var trackedPlayer))
{
    trackedPlayer.Level += 5;
}
```
This is useful when you need to modify an entity later in the same request scope without re-attaching it.
