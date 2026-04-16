_default:
    @just --list --unsorted

# Start the app with hot reload
start:
    dotnet watch --project Nexus/Nexus.csproj

# Build the solution
build:
    dotnet build

# Run all tests
test:
    dotnet test

# Start the VSCode debugger. WARNING: targets the most recently focused VSCode window.
debug:
    code --command workbench.action.debug.start
