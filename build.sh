#!/usr/bin/env bash

set -e
set -o pipefail

# Usage: build.sh [version] [config_file_or_registry]
# Example: build.sh v1.0.0 config/dev.yaml (Config ignored for .NET, kept for consistency)
# Example: build.sh v1.0.0 asia.gcr.io/my-project

if [ $# -lt 1 ]; then
    echo "Usage: build.sh [version] [config_file_or_registry (optional)]"
    echo "Example: build.sh v1.0.0"
    echo "Example: build.sh v1.0.0 asia.gcr.io/my-project"
    exit 1
fi

VERSION=$1
PARAM2=${2:-""}
SERVICE_NAME="skeleton-api-net"
REGISTRY=""

# Detect if param2 is a registry (not a config file)
# A registry looks like: asia.gcr.io/project or registry:5000
# A config file looks like: appsettings.Development.json or config.yaml
if [[ "$PARAM2" == *.json ]] || [[ "$PARAM2" == *.yaml ]] || [[ "$PARAM2" == *.yml ]]; then
    echo "Info: Second parameter '$PARAM2' is a config file, ignored for .NET build (configuration is handled via appsettings.json)"
elif [[ "$PARAM2" == *"."* ]] || [[ "$PARAM2" == *":"* ]]; then
    # It's a registry
    REGISTRY="$PARAM2"
    echo "Registry detected: $REGISTRY"
elif [[ -n "$PARAM2" ]]; then
    echo "Info: Second parameter '$PARAM2' provided but ignored for .NET build (configuration is handled via appsettings.json)"
fi

echo "========================================="
echo "Building ${SERVICE_NAME}:${VERSION}"
echo "========================================="

# Check if dotnet is available
if command -v dotnet &> /dev/null; then
    # Restore dependencies
    echo "Restoring NuGet packages..."
    dotnet restore skeleton-api-net.sln

    # Build application
    echo "Building application..."
    dotnet build skeleton-api-net.sln -c Release --no-restore

    # Run tests
    echo "Running tests..."
    dotnet test skeleton-api-net.sln -c Release --no-build \
        --collect:"XPlat Code Coverage" \
        --results-directory ./coverage
else
    echo "Warning: dotnet CLI not found on host. Skipping host-side build and tests."
    echo "The build and publish will still be performed inside the Docker container."
fi

# Build Docker image
echo "Building Docker image..."
docker build -f deployments/Dockerfile -t ${SERVICE_NAME}:${VERSION} .

# Push to Huawei Cloud if HUAWEI_PROJECT is set
if [ -n "$HUAWEI_PROJECT" ]; then
    HUAWEI_REGISTRY="swr.ap-southeast-4.myhuaweicloud.com"
    HUAWEI_IMAGE="${HUAWEI_REGISTRY}/${HUAWEI_PROJECT}/${SERVICE_NAME}:${VERSION}"
    
    echo "Tagging for Huawei Cloud..."
    docker tag ${SERVICE_NAME}:${VERSION} ${HUAWEI_IMAGE}
    
    echo "Pushing to Huawei Cloud..."
    docker push ${HUAWEI_IMAGE}
    
    echo "Cleaning up Huawei tag..."
    docker rmi ${HUAWEI_IMAGE}
    echo "Image pushed successfully to Huawei Cloud: ${HUAWEI_IMAGE}"
fi

# Tag and push to custom registry if provided
if [ -n "$REGISTRY" ]; then
    FULL_IMAGE="${REGISTRY}/${SERVICE_NAME}:${VERSION}"
    echo "Tagging image as ${FULL_IMAGE}"
    docker tag ${SERVICE_NAME}:${VERSION} ${FULL_IMAGE}
    
    echo "Pushing image to registry..."
    docker push ${FULL_IMAGE}
    
    echo "Cleaning up remote tag..."
    docker rmi ${FULL_IMAGE}
    
    echo "Image pushed successfully: ${FULL_IMAGE}"
elif [ -z "$HUAWEI_PROJECT" ]; then
    echo "No registry specified and HUAWEI_PROJECT not set, skipping push"
    echo "Local image created: ${SERVICE_NAME}:${VERSION}"
fi

echo "========================================="
echo "Build completed successfully!"
echo "========================================="
