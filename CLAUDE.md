# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

MongoCTLite is a lightweight MongoDB change tracking library that implements the Unit of Work pattern with optimistic concurrency control. The library provides intelligent document diffing and bulk operations for efficient MongoDB updates.

## Development Commands

### Building
```bash
dotnet build MongoCTLite.sln
dotnet build --configuration Release MongoCTLite.sln
```

### Testing
```bash
# Run all tests
dotnet test MongoCTLite.Tests/MongoCTLite.Tests.csproj

# Run tests with verbose output
dotnet test MongoCTLite.Tests/MongoCTLite.Tests.csproj --verbosity normal

# Run specific test
dotnet test MongoCTLite.Tests/MongoCTLite.Tests.csproj --filter "DisplayName~Test1"
```

### Running Sample Application
```bash
dotnet run --project MongoCTLite.Samples.Console/MongoCTLite.Samples.Console.csproj
```

### Packaging
```bash
dotnet pack MongoCTLite/MongoCTLite.csproj --configuration Release
```

## Architecture

### Core Components

**TrackingContext (`ITrackingContext`)**: The main Unit of Work implementation that manages multiple tracked entities and executes bulk operations with optimistic concurrency control.

**TrackingEntry<T>**: Represents a tracked MongoDB document with snapshot capabilities for change detection. Requires documents to have `_id` and `version` fields.

**DiffEngine**: The heart of the change detection system that analyzes differences between original and current document states, generating optimized MongoDB update operations.

**UpdateOps**: Container for MongoDB update operations ($set, $unset, $inc, $push, $pull) that represents the computed differences.

**DiffPolicy**: Configuration object that controls diff behavior including field operation limits, array change thresholds, and path-specific policies for $inc and $unset operations.

### Key Design Patterns

- **Unit of Work**: TrackingContext collects changes and flushes them as a single transaction
- **Snapshot Pattern**: TrackingEntry captures initial document state for change detection
- **Optimistic Concurrency**: Version-based conflict detection prevents lost updates
- **Bulk Operations**: Multiple document changes are batched into efficient BulkWrite operations
- **Smart Diffing**: Intelligent analysis chooses between granular updates and document replacement

### Directory Structure

- `Abstractions/`: Core interfaces and policies (IRunLogger, DiffPolicy)
- `Tracking/`: Unit of Work implementation (TrackingContext, TrackingEntry)
- `Diff/`: Change detection engine (DiffEngine, UpdateOps, utility classes)
- `Infrastructure/`: Logging and utility implementations

### Concurrency Model

The library implements optimistic concurrency control using document versioning:
- Each tracked document must have a numeric `version` field
- Updates include version-based filters (`_id + version`)
- Failed version matches throw `ConcurrencyConflictException`
- Supports both transactional and non-transactional bulk operations

### Error Handling

- **ConcurrencyConflictException**: Thrown when version-based optimistic locking fails
- **Transient Error Retry**: Automatic retry logic for MongoDB connection issues
- **Transaction Rollback**: Automatic rollback on transaction failures
- **Validation**: Runtime validation of required fields (`_id`, `version`)

## Testing Framework

- Uses **xUnit** for unit testing
- **Shouldly** for fluent assertions  
- **coverlet.collector** for code coverage
- Test files should follow the pattern `*Test.cs` or `*Tests.cs`

## Dependencies

- **MongoDB.Driver 3.4.3**: Official MongoDB C# driver
- **.NET 8.0**: Target framework
- **Nullable reference types**: Enabled for better null safety

## Development Notes

- The codebase uses `_id` as the default identity field but supports custom ID fields
- Documents require a `version` field for optimistic concurrency (Int32 or Int64)
- Same document cannot be tracked multiple times within one TrackingContext
- Array operations support append-only and remove-only optimizations
- Change detection includes configurable thresholds for replacing vs updating arrays/documents