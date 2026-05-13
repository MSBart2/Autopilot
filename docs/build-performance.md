# Build Performance Optimizations

This document describes the build performance optimizations applied to the Cyberpilot project.

## Problem

The .NET 10 preview SDK introduces significant MSBuild evaluation overhead, causing `dotnet build --no-restore` to take ~26-30 seconds even though actual compilation is under 1 second.

## Optimizations Applied

### 1. `Directory.Build.props` (Repository Root)

Centralized build properties that apply to all projects:

| Property | Value | Purpose |
|----------|-------|---------|
| `UseSharedCompilation` | `true` | Reuses the Roslyn compiler server across builds |
| `ProduceReferenceAssembly` | `true` | Enables incremental builds by producing reference assemblies |
| `AccelerateBuildsInVisualStudio` | `true` | Enables VS-specific build acceleration |
| `GenerateDocumentationFile` | `false` | Skips XML doc generation by default (overridden in Release for main project) |
| `RunAnalyzers` (Debug only) | `false` | Suppresses analyzers during development builds |
| `RunAnalyzersDuringBuild` (Debug only) | `false` | Suppresses analyzer execution during build |

### 2. Project File Optimizations

- `web/Cyberpilot.Web.csproj`: Documentation file generation is conditional on Release configuration
- Test projects: No unnecessary documentation generation

## Before/After Benchmarks

| Metric | Before | After |
|--------|--------|-------|
| `dotnet build --no-restore` | ~26-30s | Improved with shared compilation |
| Actual compilation time | ~0.6s | ~0.6s (unchanged) |
| MSBuild evaluation overhead | ~22-26s | Reduced via shared compilation and analyzer suppression |

## How to Profile Builds

Use the binary log (`-bl`) flag to generate a detailed build log:

`ash
# Generate a binary log
dotnet build -bl

# The output file (msbuild.binlog) can be viewed with:
# - MSBuild Structured Log Viewer (https://msbuildlog.com/)
# - `dotnet build -bl -flp:v=diag` for text output
`

### Useful profiling commands

`ash
# Time the build without restore
dotnet build --no-restore

# Time just the restore
dotnet restore

# Verbose build with timing
dotnet build -v detailed

# Binary log for deep analysis
dotnet build -bl -clp:PerformanceSummary
`

## Design Decisions

1. **Analyzers suppressed only in Debug**: Release builds still run all analyzers for CI/CD quality gates
2. **Documentation generation conditional**: Only generates XML docs in Release to avoid overhead in development
3. **Shared compilation enabled**: The Roslyn VBCSCompiler server stays resident between builds, reducing startup cost
