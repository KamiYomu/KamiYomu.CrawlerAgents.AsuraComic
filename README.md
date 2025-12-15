# KamiYomu Crawler Agent — asuracomic.net

A dedicated crawler agent for accessing public manga data from `asuracomic.net`. Built on `KamiYomu.CrawlerAgents.Core`, this agent enables efficient search, metadata extraction, and integration with the KamiYomu platform.

## Features
- Search public `asuracomic.net` content (titles, authors, tags)
- Extract standardized metadata (titles, chapters, volumes, artists, tags)
- Designed for integration with the KamiYomu platform and its add-on model
- Extensible: implement additional parsing or enrichment logic
- Targets .NET 8

## Installation

### Option A — Install via KamiYomu platform (recommended for end users)
1. Open the KamiYomu Web application.
2. Go to the `Add-ons` menu.
3. Setup the source for KamiYomu Add-ons if not already done:
   - Navigate to `Add-ons > Sources`
   - Add a new source with the URL: `https://api.nuget.org/v3/index.json` 
     for public packages or `https://nuget.pkg.github.com/KamiYomu/index.json`
3. Locate and install the "KamiYomu Crawler Agent – mangapark.net" add-on.
4. Configure the add-on in the Add-ons UI as needed.

### Option B — Install as a package (for developers / extensibility)
The package is published as `KamiYomu.CrawlerAgents.AsuraComic`.

- Using `dotnet` (default NuGet or configured sources):

- Using GitHub Packages feed (example using `NuGet.config`):
1. Add the GitHub Packages feed to your `NuGet.config` (or configure your CI):
   ```xml
   <packageSources>
     <add key="github" value="https://nuget.pkg.github.com/KamiYomu/index.json" />
   </packageSources>
   ```
   See the [official GitHub documentation](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry#configuring-nuget-to-use-your-github-packages-hosted-gallery) for more information.

2. Install the package:
   ```bash
   dotnet add package KamiYomu.CrawlerAgents.AsuraComic --version <version>
   ```

Note: GitHub Packages may require authentication. Configure a personal access token or CI secret according to GitHub Packages documentation.

## Quick Start (high-level)
1. Install the package (see above) or install it via add-on in KamiYomu.
2. Register or enable the crawler within your KamiYomu instance (via Add-ons UI or your startup DI).
3. Use the platform search or API to query `asuracomic.net` data. Example usage patterns:
   - Run searches for a title or tag
   - Fetch metadata for a specific series or chapter
   - Map extracted metadata into KamiYomu entities

## Developer Guide

### Prerequisites

1. **Development Tools**
   - [Visual Studio 2022](https://visualstudio.microsoft.com/vs/) (Community edition or higher)
     - Ensure ".NET Desktop Development" workload is installed
   
   OR
   
   - [Visual Studio Code](https://code.visualstudio.com/)
     - Install [C# Dev Kit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit) extension
     - Install [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0)

2. **Git** - [Download and install Git](https://git-scm.com/downloads)

### Getting Started

1. Clone the repository:

2. Open the project:

**Using Visual Studio 2022:**
- Navigate to `/src/KamiYomu.CrawlerAgents.AsuraComic/`
- Double-click `KamiYomu.CrawlerAgents.AsuraComic.sln`
- Wait for Visual Studio to load and restore NuGet packages

**Using Visual Studio Code:**
- Open VS Code
- Select `File > Open Folder` and choose the cloned repository folder
- When prompted, installrecommended extensions
- Open the Command Palette (Ctrl+Shift+P) and run `.NET: Restore Project`
- The C# Dev Kit will automatically initialize the development environment

3. Build the solution:
- Visual Studio: Build > Build Solution (F6)
- VS Code: Terminal > `dotnet build src/KamiYomu.CrawlerAgents.AsuraComic.sln`

4. Validate
Run the console application to ensure everything is set up correctly:
1. Set `KamiYomu.CrawlerAgents.ConsoleApp` as the startup project.
2. Run the application (F5 or Ctrl+F5).
3. You should see console output indicating the crawler is operational.

