# Build Instructions

## Prerequisites

1. **Windows 10/11**: This is a WinUI3 application and requires Windows to build and run
2. **Visual Studio 2022** (version 17.0 or later) with:
   - .NET Desktop Development workload
   - Universal Windows Platform development workload
   - Windows 10 SDK (10.0.19041.0 or later)
3. **.NET 8.0 SDK** (included with Visual Studio 2022)

## Building from Visual Studio

1. Open `ProsekaTools.sln` in Visual Studio 2022
2. Select your target platform:
   - x64 (recommended for most modern PCs)
   - x86 (for 32-bit systems)
   - ARM64 (for ARM-based Windows devices)
3. Right-click the solution in Solution Explorer and select "Restore NuGet Packages"
4. Build the solution: Press `Ctrl+Shift+B` or select **Build > Build Solution**
5. Run the application: Press `F5` or select **Debug > Start Debugging**

## Building from Command Line

1. Open Developer Command Prompt for VS 2022
2. Navigate to the repository directory:
   ```cmd
   cd path\to\proseka_tools
   ```
3. Restore packages:
   ```cmd
   dotnet restore
   ```
4. Build the project:
   ```cmd
   dotnet build ProsekaToolsApp\ProsekaToolsApp.csproj -c Release
   ```

## Troubleshooting

### "Windows SDK not found"
- Install Windows 10 SDK version 10.0.19041.0 or later via Visual Studio Installer

### "Microsoft.WindowsAppSDK not found"
- Run `dotnet restore` to download NuGet packages

### Build errors related to XAML
- Ensure you're building on Windows (XAML compiler requires Windows)
- Clean and rebuild the solution

## Project Structure

```
ProsekaToolsApp/
├── App.xaml              # Application definition
├── App.xaml.cs           # Application entry point
├── MainWindow.xaml       # Main window with NavigationView
├── MainWindow.xaml.cs    # Navigation logic
├── app.manifest          # Windows manifest
├── Assets/               # Application icons and images
└── ProsekaToolsApp.csproj # Project configuration
```

## Features

The application includes:
- **Tab 1 (Home)**: Home page with welcome content
- **Tab 2 (Documents)**: Document management section
- **Tab 3 (Library)**: Library features section
- **Tab 4 (Settings)**: Settings and preferences

Each tab is implemented using WinUI3's NavigationView control with icon-based navigation items on the left sidebar.
