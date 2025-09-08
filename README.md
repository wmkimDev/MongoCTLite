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
  You can configure which field acts as the entity’s key.

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

```cs
public class Player
{
    [BsonId] public ObjectId Id { get; set; } // or your own custom key
    public long version { get; set; }         // required
    public int Level { get; set; }
    public long Gold { get; set; }
}
```

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
    new DiffPolicy(AllowIncPath: path => path == "Gold"),
    new ConsoleLogger());
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
if (ctx.TryGetTrackedEntity(players, player.Id, out var trackedPlayer))
{
    trackedPlayer.Level += 5;
}
```
This is useful when you need to modify an entity later in the same request scope without re-attaching it.
