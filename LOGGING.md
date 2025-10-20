# Conditional Compilation Logging System

This document explains the conditional compilation-based logging system implemented for performance optimization.

## Overview

The logging system uses C# conditional compilation attributes to completely eliminate logging code at compile time when it's not needed. This provides zero-performance overhead for disabled logging levels.

## Logging Levels

The logging levels are hierarchical and inclusive - enabling a level also enables all levels below it:

1. **TRACE_LOGGING** - Most verbose, includes performance tracing
   - Includes: Trace, Verbose, Debug, Info, Warning, Error
   - Use for: Beta releases, development builds with maximum detail

2. **VERBOSE_LOGGING** - Detailed operational information
   - Includes: Verbose, Debug, Info, Warning, Error
   - Use for: Production releases that need detailed troubleshooting

3. **DEBUG_LOGGING** - Debug information useful for development
   - Includes: Debug, Info, Warning, Error
   - Use for: Development builds with moderate detail

4. **INFO_LOGGING** - General information messages (default)
   - Includes: Info, Warning, Error
   - Use for: Standard production releases

5. **WARNING_LOGGING** - Warning messages only
   - Includes: Warning, Error
   - Use for: Minimal production builds

6. **ERROR_LOGGING** - Error messages only (always enabled)
   - Includes: Error only
   - Use for: Ultra-minimal builds

## Configuration

Edit `src/HomeAssistantPlugin.csproj` and uncomment the desired logging level:

```xml
<!-- For Beta/Development builds: Enable all verbose logging -->
<DefineConstants>$(DefineConstants);TRACE_LOGGING</DefineConstants>

<!-- For Production with detailed logs -->
<!-- <DefineConstants>$(DefineConstants);VERBOSE_LOGGING</DefineConstants> -->

<!-- For Standard Production (recommended) -->
<!-- <DefineConstants>$(DefineConstants);INFO_LOGGING</DefineConstants> -->
```

## Performance Benefits

### Before (Traditional Logging)
```csharp
// This string interpolation always executes, even if logging is disabled
PluginLog.Verbose($"Complex operation: {expensive.Calculate()} with {data.Count} items");
```

### After (Conditional Compilation)
```csharp
// This entire method call is removed at compile time if VERBOSE_LOGGING is not defined
PluginLog.Verbose(() => $"Complex operation: {expensive.Calculate()} with {data.Count} items");
```

## Key Features

1. **Zero Runtime Overhead** - Disabled logs are completely removed from compiled code
2. **Lambda-Based Deferred Execution** - Complex string operations only execute when logging is enabled
3. **Aggressive Inlining** - Logging methods are optimized for performance
4. **Hierarchical Levels** - Enable one level, get all lower levels automatically

## Examples

### High-Frequency Operations
```csharp
// Use Trace for very frequent operations that would spam logs
PluginLog.Trace(() => $"Mouse position: {x}, {y}");
```

### Detailed State Information
```csharp
// Use Verbose for detailed state that helps with troubleshooting
PluginLog.Verbose(() => $"Light state: entity={id}, brightness={bri}, color=({h},{s})");
```

### Development Information
```csharp
// Use Debug for information useful during development
PluginLog.Debug(() => $"Cache hit ratio: {hits}/{total} ({ratio:P})");
```

### Production Information
```csharp
// Use Info for important operational information
PluginLog.Info("Home Assistant connection established");
```

## Build Configurations

### Beta Release (Maximum Logging)
```xml
<DefineConstants>$(DefineConstants);TRACE_LOGGING</DefineConstants>
```

### Production Release (Standard Logging)
```xml
<DefineConstants>$(DefineConstants);INFO_LOGGING</DefineConstants>
```

### Production Release (Minimal Logging)
```xml
<DefineConstants>$(DefineConstants);WARNING_LOGGING</DefineConstants>
```

## Migration Guide

When updating existing logs:

1. **Complex string interpolations** → Use lambda syntax: `PluginLog.Level(() => $"message")`
2. **Frequent trace logs** → Change from `Verbose` to `Trace`
3. **Performance-critical logs** → Change from `Info` to `Debug` or `Verbose`
4. **Production-important logs** → Keep as `Info` or `Warning`

## Performance Impact

The problematic log identified in the original issue:
```csharp
// Before: Always executed, caused performance issues
PluginLog.Info($"[Light] {entityId} | name='{friendly}' | state={state} | dev='{deviceName}' mf='{mf}' model='{model}' bri={bri} tempMired={curM} range=[{minM},{maxM}] area='{areaId}'");

// After: Only executed when VERBOSE_LOGGING is enabled, zero cost otherwise  
PluginLog.Verbose(() => $"[Light] {entityId} | name='{friendly}' | state={state} | dev='{deviceName}' mf='{mf}' model='{model}' bri={bri} tempMired={curM} range=[{minM},{maxM}] area='{areaId}'");
```

This change eliminates the expensive string interpolation completely when verbose logging is disabled, providing significant performance improvements in production builds.