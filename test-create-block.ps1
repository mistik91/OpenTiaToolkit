# Test script for create-block function
# Run this from PowerShell

$cliProject = "src\OpenTiaToolkit.CLI\OpenTiaToolkit.CLI.csproj"
$testProject = "D:\Siemens\V21\MCP_TEST_V21\MCP_TEST_V21.ap21"

Write-Host "=== TIA Portal Create-Block Test ===" -ForegroundColor Cyan
Write-Host ""

# Step 1: Connect to TIA Portal
Write-Host "Step 1: Connecting to TIA Portal..." -ForegroundColor Yellow
dotnet run --project $cliProject -- connect-portal
Write-Host ""

# Step 2: Open project
Write-Host "Step 2: Opening test project..." -ForegroundColor Yellow
dotnet run --project $cliProject -- open-project --project-path $testProject
Write-Host ""

# Step 3: List PLCs
Write-Host "Step 3: Listing available PLCs..." -ForegroundColor Yellow
dotnet run --project $cliProject -- list-plcs
Write-Host ""

Write-Host "=== Test Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Note: Each command runs in a separate process." -ForegroundColor Gray
Write-Host "For true interactive testing, use: dotnet run --project $cliProject -- -i" -ForegroundColor Gray
