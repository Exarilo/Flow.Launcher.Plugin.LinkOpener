cls
$PluginName = "LinkOpener"

# Task 1: Project compilation and publication
Write-Host "✅ 1. Starting project publication for plugin $PluginName..." -ForegroundColor Cyan

try {
    # Build the full path to the .csproj project file
    $projectPath = ".\Flow.Launcher.Plugin.$PluginName.csproj"
    
    # Check if the project file exists
    if (-Not (Test-Path $projectPath)) {
        throw "❌ Project file $projectPath not found."
    }

    # Execute the dotnet publish command and check if it succeeds
    dotnet publish $projectPath -c Debug -r win-x64 --no-self-contained
    if ($LASTEXITCODE -ne 0) {
        throw "❌ Project publication failed with exit code $LASTEXITCODE."
    }
} catch {
    # In case of error, display the message and stop the script
    Write-Host "❌ Error: $_" -ForegroundColor Red
    exit 1
}

# Define necessary paths
$AppDataFolder = [Environment]::GetFolderPath("ApplicationData")
$flowLauncherExe = "$env:LOCALAPPDATA\FlowLauncher\Flow.Launcher.exe"

# Task 2: Check for the presence of Flow Launcher
if (Test-Path $flowLauncherExe) {
    Write-Host "✅ 2. Flow Launcher found. Stopping the application..." -ForegroundColor Cyan
    
    # Stop the Flow Launcher process if it is running
    Stop-Process -Name "Flow.Launcher" -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2

    # Define the plugin path
    $pluginPath = "$AppDataFolder\FlowLauncher\Plugins\$PluginName"
    
    # Task 3: Remove the old version of the plugin
    if (Test-Path $pluginPath) {
        Write-Host "✅ 3. Removing the old version of plugin $PluginName..." -ForegroundColor Cyan
        Remove-Item -Recurse -Force $pluginPath
    } else {
        Write-Host "✅ 3. No old version of plugin $PluginName found." -ForegroundColor Cyan
    }

    # Task 4: Copy the new published files
    Write-Host "✅ 4. Copying the published files to the plugins folder..." -ForegroundColor Cyan
    Copy-Item ".\bin\Debug\win-x64\publish" "$AppDataFolder\FlowLauncher\Plugins\" -Recurse -Force
    
    # Rename the copied folder to $PluginName
    Rename-Item -Path "$AppDataFolder\FlowLauncher\Plugins\publish" -NewName "$PluginName"

    # Task 5: Restart Flow Launcher
    Write-Host "✅ 5. Restarting Flow Launcher..." -ForegroundColor Cyan
    Start-Sleep -Seconds 2
    Start-Process $flowLauncherExe
    Write-Host "✅ 6. Update and restart successful!" -ForegroundColor Cyan
} else {
    # Flow Launcher not found
    Write-Host "❌ Error: Flow.Launcher.exe not found. Please install Flow Launcher first." -ForegroundColor Red
}
