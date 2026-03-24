#!/bin/bash

# Configuration: Set the path to your Cities: Skylines installation directory.
# You can also set the CITIES_SKYLINES_DIR environment variable instead of editing this script.
REAL_GAME_DIR="${CITIES_SKYLINES_DIR:-/var/mnt/schijven/1TB SSD/Games/Cities - Skylines/drive_c/Program Files (x86)/Cities.Skylines.v1.21.1.F5}"

# Check if the game directory exists
if [ ! -d "$REAL_GAME_DIR" ]; then
    echo "Error: Game directory not found at '$REAL_GAME_DIR'"
    echo "Please set the CITIES_SKYLINES_DIR environment variable or update the script."
    exit 1
fi

# Use a symbolic link to handle spaces in paths (important for MSBuild on some Linux versions)
GAME_DIR="/tmp/cs_game"
rm -f "$GAME_DIR"
ln -s "$REAL_GAME_DIR" "$GAME_DIR"

MANAGED_DIR="$GAME_DIR/Cities_Data/Managed"

echo "=== Building CSM.API submodule ==="
# Restore and build the CSM.API dependency from the submodule
dotnet build "submodule/CSM/src/api/CSM.API.csproj" -c Release \
    /p:CitiesSkylinesDir="$GAME_DIR" \
    /p:ManagedDir="$MANAGED_DIR"

if [ $? -ne 0 ]; then
    echo "CSM.API build failed!"
    exit 1
fi

echo "=== Building CSM.TmpeSync mod ==="

# Automatically search for mod dependencies (Harmony and TM:PE) in the game's Mods folder
HARMONY_DLL=$(find "$REAL_GAME_DIR/Files/Mods/" -name "CitiesHarmony.Harmony.dll" 2>/dev/null | head -n 1)

if [ -z "$HARMONY_DLL" ]; then
    echo "Error: CitiesHarmony.Harmony.dll not found in $REAL_GAME_DIR/Files/Mods/"
    exit 1
fi

echo "Found CitiesHarmony at: $HARMONY_DLL"

# Create a temporary reference directory for MSBuild
REF_DIR="/tmp/cs_refs"
rm -rf "$REF_DIR"
mkdir -p "$REF_DIR/Harmony"
mkdir -p "$REF_DIR/TMPE/Cities_Data/Managed"

# Link dependency DLLs into the reference directory
ln -s "$HARMONY_DLL" "$REF_DIR/Harmony/CitiesHarmony.Harmony.dll"

# Link all DLLs from the TM:PE folder (using the default folder name for stability)
# If your TM:PE mod folder has a different name, the find command will attempt a search.
TMPE_SEARCH_DIR=$(find "$REAL_GAME_DIR/Files/Mods/" -maxdepth 1 -type d -name "TMPE*" 2>/dev/null | head -n 1)

if [ -z "$TMPE_SEARCH_DIR" ]; then
    echo "Error: TM:PE mod directory not found!"
    exit 1
fi

echo "Found TM:PE at: $TMPE_SEARCH_DIR"
find "$TMPE_SEARCH_DIR/" -name "*.dll" -exec ln -s {} "$REF_DIR/TMPE/Cities_Data/Managed/" \;

# Verify TrafficManager.dll exists
if [ ! -f "$REF_DIR/TMPE/Cities_Data/Managed/TrafficManager.dll" ]; then
    echo "Error: TrafficManager.dll not found in TM:PE folder!"
    exit 1
fi

# Build the main mod project and its features
dotnet build "src/CSM.TmpeSync/CSM.TmpeSync.csproj" -c Release \
    /p:CitiesSkylinesDir="$GAME_DIR" \
    /p:ManagedDir="$MANAGED_DIR" \
    /p:HarmonyDllDir="$REF_DIR/Harmony" \
    /p:TmpeDir="$REF_DIR/TMPE" \
    /p:CsmApiDllPath="$(pwd)/submodule/CSM/src/api/bin/Release/CSM.API.dll"

if [ $? -ne 0 ]; then
    echo "CSM.TmpeSync build failed!"
    exit 1
fi

echo "=== Build Complete ==="
echo "Artifacts are located in: src/CSM.TmpeSync/bin/Release/net35/"
echo "Copy all .dll files from the above folder to your game's Addons/Mods directory."
