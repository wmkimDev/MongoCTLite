# MongoCTLite

MongoDB change tracking library with Unit of Work pattern and optimistic concurrency control.

## Features

- Unit of Work pattern for batching changes
- Optimistic concurrency with version-based conflict detection  
- Intelligent document diffing
- Transaction support
- Retry logic for MongoDB transient errors

## Usage

```csharp
// Track entity changes (requires _id and version fields)
var context = new TrackingContext();
var tracked = context.Attach(collection, entity, entity.Version);

// Modify entity
tracked.Current.Name = "New Name";

// Save with optimistic concurrency
await context.SaveChangesAsync(new DiffPolicy(), new ConsoleRunLogger());
```

## Status

⚠️ **Work in Progress** - Not ready for production use
