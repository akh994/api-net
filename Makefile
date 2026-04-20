.PHONY: help build build-generator run publish test clean proto docker-build docker-run restore watch format lint setup-deps migrate-up migrate-down migrate-create install-migrate install-proto-tools deploy-k8s kube-context set-context list-context gen

# Variables
# Detect OS for maxcpucount
# Detect OS and set MAX_CPU_COUNT
MAX_CPU_COUNT ?= 1
ifdef OS
	ifeq ($(OS),Windows_NT)
		MAX_CPU_COUNT = 1
	endif
else
	MAX_CPU_COUNT = $(shell nproc 2>/dev/null || echo 1)
endif


PROTO_DIR=proto
PROTO_FILES=$(shell find $(PROTO_DIR) -name '*.proto' 2>/dev/null | grep -v google || true)
PROJECT=src/SkeletonApi/SkeletonApi.csproj
SOLUTION=skeleton-api-net.sln
DOCKER_IMAGE=skeleton-api-net
DOCKER_TAG=latest
NUGET_CACHE = $(HOME)/.nuget/packages
COMMON_CSPROJ = src/SkeletonApi.Common/SkeletonApi.Common.csproj

# Help target
help:
	@echo "Available targets:"
	@echo "  build              - Build the application"
	@echo "  build-generator    - Build the project generator tool"
	@echo "  bump-common        - Increment patch version in SkeletonApi.Common"
	@echo "  clean              - Clean build artifacts"
	@echo "  deploy-k8s         - Deploy to Kubernetes (apply service.yaml)"
	@echo "  docker-build       - Build Docker image"
	@echo "  docker-run         - Run Docker container (foreground)"
	@echo "  docker-run-d       - Run Docker container (background)"
	@echo "  docker-stop        - Stop Docker container"
	@echo "  format             - Format code"
	@echo "  gen                - Run generator tool (Usage: make gen ARGS=\"...\")"
	@echo "  gen-remote         - Run generator with latest SkeletonApi.Common NuGet (Usage: make gen-remote ARGS=\"...\")"
	@echo "  install-migrate    - Install EF Core migration tool"
	@echo "  install-proto-tools - Install protobuf tools"
	@echo "  kube-context       - Show current Kubernetes context"
	@echo "  lint               - Lint code (check format)"
	@echo "  list-context       - List all available Kubernetes contexts"
	@echo "  merge-common       - Merge Common project to target projects (Usage: make merge-common TARGETS=\"path1 path2\")"
	@echo "  migrate-create     - Create new migration"
	@echo "  migrate-down       - Rollback last migration"
	@echo "  migrate-up         - Apply database migrations"
	@echo "  pack-common        - Bump version and pack SkeletonApi.Common"
	@echo "  proto              - Generate C# code from proto files"
	@echo "  publish            - Publish release build"
	@echo "  restore            - Restore NuGet packages"
	@echo "  run                - Run the application"
	@echo "  set-context        - Switch Kubernetes context (Usage: make set-context CONTEXT=name)"
	@echo "  setup-deps         - Setup Docker dependencies"
	@echo "  test               - Run tests"
	@echo "  test-coverage      - Run tests with coverage report"
	@echo "  watch              - Run with hot reload"
	@echo "  help               - Show this help message"
# Build the application
build:
	@echo "🚀 Restoring dependencies (only if needed)..."
	@dotnet restore $(SOLUTION) --packages $(NUGET_CACHE)
	@echo "⚡ Building solution in Release mode..."
	@dotnet build $(SOLUTION) --no-restore -c Release -maxcpucount:$(MAX_CPU_COUNT)
	@echo "✅ Build complete"

# Build the generator tool
build-generator:
	@echo "Building project generator..."
	@dotnet publish tools/generator/src/SkeletonApi.Generator.csproj -c Release -o tools/generator/bin
	@echo "Generator build complete: tools/generator/bin/SkeletonApi.Generator"

# Run generator tool
gen: build-generator
	@tools/generator/bin/SkeletonApi.Generator $(ARGS)

# Run generator with latest remote common package
gen-remote: build-generator
	@VERSION=$$(grep '<Version>' $(COMMON_CSPROJ) | sed 's/.*<Version>\(.*\)<\/Version>.*/\1/'); \
	PKG_NAME=$$(grep '<PackageId>' $(COMMON_CSPROJ) | sed 's/.*<PackageId>\(.*\)<\/PackageId>.*/\1/'); \
	if [ -z "$$PKG_NAME" ]; then PKG_NAME="SkeletonApi.Common"; fi; \
	echo "Using Remote Package: $$PKG_NAME v$$VERSION"; \
	FIRST_WORD=$$(echo "$(ARGS)" | awk '{print $$1}'); \
	REST_ARGS=$$(echo "$(ARGS)" | cut -d' ' -f2-); \
	tools/generator/bin/SkeletonApi.Generator $$FIRST_WORD --pkg-type remote --common-pkg-name $$PKG_NAME --common-pkg-version $$VERSION $$REST_ARGS

# Bump version in SkeletonApi.Common
bump-common:
	@CURRENT_VERSION=$$(grep '<Version>' $(COMMON_CSPROJ) | sed 's/.*<Version>\(.*\)<\/Version>.*/\1/'); \
	NEXT_VERSION=$$(echo $$CURRENT_VERSION | awk -F. '{print $$1"."$$2"."$$3+1}'); \
	sed -i "s/<Version>$$CURRENT_VERSION<\/Version>/<Version>$$NEXT_VERSION<\/Version>/" $(COMMON_CSPROJ); \
	echo "✅ Version bumped: $$CURRENT_VERSION -> $$NEXT_VERSION"

# Bump version and pack SkeletonApi.Common
pack-common: bump-common
	@echo "📦 Packing SkeletonApi.Common..."
	@dotnet pack -c Release -o ./src/SkeletonApi.Common/nupkg src/SkeletonApi.Common/SkeletonApi.Common.csproj
	@echo "✅ Pack complete: src/SkeletonApi.Common/nupkg"

# Publish SkeletonApi.Common to GitLab Package Registry
publish-common: pack-common
	@if [ -z "$(GITLAB_TOKEN)" ]; then \
		echo "❌ GITLAB_TOKEN is not set. Usage: make publish-common GITLAB_TOKEN=your_token"; \
		exit 1; \
	fi
	@VERSION=$$(grep '<Version>' $(COMMON_CSPROJ) | sed 's/.*<Version>\(.*\)<\/Version>.*/\1/'); \
	echo "🚀 Publishing SkeletonApi.Common v$$VERSION to GitLab..."; \
	dotnet nuget push src/SkeletonApi.Common/nupkg/SkeletonApi.Common.$$VERSION.nupkg \
		--source "https://git.bluebird.id/api/v4/projects/architect%2Fmy-skeleton%2Fskeleton-api-net/packages/nuget/index.json" \
		--api-key $(GITLAB_TOKEN) \
		--skip-duplicate; \
	echo "✅ Publish complete!"

# Merge Common project to target projects
merge-common:
	@if [ -z "$(TARGETS)" ]; then \
		echo "Usage: make merge-common TARGETS=\"/path/to/project1 /path/to/project2\""; \
		exit 1; \
	fi
	@for target in $(TARGETS); do \
		if [ ! -d "$$target" ]; then \
			echo "❌ Target directory not found: $$target"; \
			continue; \
		fi; \
		COMMON_DIR=$$(find "$$target/src" -maxdepth 1 -type d -name "*.Common" | head -n 1); \
		if [ -z "$$COMMON_DIR" ]; then \
			echo "❌ Could not find .Common directory in $$target/src"; \
			continue; \
		fi; \
		PROJECT_NAME=$$(basename "$$COMMON_DIR" .Common); \
		echo "Merging Common to $$target (Project: $$PROJECT_NAME)..."; \
		cp -rf src/SkeletonApi.Common/* "$$COMMON_DIR/"; \
		if [ -f "$$COMMON_DIR/SkeletonApi.Common.csproj" ]; then \
			mv "$$COMMON_DIR/SkeletonApi.Common.csproj" "$$COMMON_DIR/$$PROJECT_NAME.Common.csproj"; \
		fi; \
		find "$$COMMON_DIR" -type f \( -name "*.cs" -o -name "*.csproj" \) -exec sed -i "s/SkeletonApi/$$PROJECT_NAME/g" {} +; \
		echo "✅ Merged Common to $$target"; \
	done

# Run the application
run:
	@echo "Running application..."
	@dotnet run --project $(PROJECT)

# Publish release build
publish:
	@echo "Publishing release build..."
	@dotnet publish $(PROJECT) -c Release -o bin/Release/publish
	@echo "Publish complete: bin/Release/publish"

# Run tests
test:
	@echo "Running tests..."
	@dotnet test $(SOLUTION) -c Release --no-build
	@echo "Tests complete"

# Run tests with coverage
test-coverage:
	@echo "Running tests with coverage..."
	@dotnet test $(SOLUTION) -c Release \
		--collect:"XPlat Code Coverage" \
		--results-directory ./coverage
	@echo "Coverage report generated in ./coverage"

# Clean build artifacts
clean:
	@echo "Cleaning..."
	@dotnet clean $(SOLUTION)
	@rm -rf bin/ obj/ coverage/
	@find . -type d -name "bin" -o -name "obj" | xargs rm -rf
	@echo "Clean complete"

# Generate proto files
proto:
	@echo "Generating proto files..."
	@if [ -z "$(PROTO_FILES)" ]; then \
		echo "No proto files found in $(PROTO_DIR)"; \
	else \
		for proto in $(PROTO_FILES); do \
			protoc -I$(PROTO_DIR) \
				--csharp_out=$(PROTO_DIR) \
				--grpc_out=$(PROTO_DIR) \
				--plugin=protoc-gen-grpc=$$(which grpc_csharp_plugin) \
				$$proto; \
		done; \
		echo "Proto generation complete"; \
	fi

# Install proto tools
install-proto-tools:
	@echo "Installing proto tools..."
	@dotnet tool install --global Grpc.Tools
	@echo "Proto tools installed"

# Docker build
docker-build:
	@echo "Building Docker image..."
	@docker build -t $(DOCKER_IMAGE):$(DOCKER_TAG) -f deployments/Dockerfile .
	@echo "Docker build complete: $(DOCKER_IMAGE):$(DOCKER_TAG)"

# Docker alias
# Docker alias
docker: docker-build

# Deploy to Kubernetes
deploy-k8s:
	@echo "Deploying to Kubernetes..."
	@kubectl apply -f deployments/service.yaml
	@echo "Deployment applied"

# Show current Kubernetes context
kube-context:
	@echo "Current Kubernetes Context:"
	@kubectl config current-context
	@echo ""
	@echo "Cluster Info:"
	@kubectl cluster-info

# Switch Kubernetes context
set-context:
	@if [ -z "$(CONTEXT)" ]; then \
		echo "Usage: make set-context CONTEXT=<context_name>"; \
		echo "Available contexts:"; \
		kubectl config get-contexts -o name; \
		exit 1; \
	fi
	@kubectl config use-context $(CONTEXT)

# List Kubernetes contexts
list-context:
	@echo "Available Kubernetes Contexts:"
	@kubectl config get-contexts

# Docker run (foreground)
docker-run:
	@echo "Running Docker container..."
	@docker run --rm --network host --name $(DOCKER_IMAGE) $(DOCKER_IMAGE):$(DOCKER_TAG)

# Docker run (background)
docker-run-d:
	@echo "Running Docker container in background..."
	@docker run -d --rm --network host --name $(DOCKER_IMAGE) $(DOCKER_IMAGE):$(DOCKER_TAG)

# Docker stop
docker-stop:
	@echo "Stopping Docker container..."
	@docker stop $(DOCKER_IMAGE)

# Restore NuGet packages
restore:
	@echo "Restoring NuGet packages..."
	@dotnet restore $(SOLUTION)
	@echo "Restore complete"

# Run with hot reload
watch:
	@echo "Running with hot reload..."
	@dotnet watch --project $(PROJECT) run

# Format code
format:
	@echo "Formatting code..."
	@dotnet format $(SOLUTION)
	@echo "Format complete"

# Lint code
lint:
	@echo "Linting code..."
	@dotnet format $(SOLUTION) --verify-no-changes
	@echo "Lint complete"

# Setup Docker dependencies
setup-deps:
	@echo "Setting up Docker dependencies..."
	@tr -d '\r' < setup-dependencies.sh > .setup-dependencies.sh.tmp && mv .setup-dependencies.sh.tmp setup-dependencies.sh
	@chmod +x setup-dependencies.sh
	@bash ./setup-dependencies.sh

# Install golang-migrate tool
install-migrate:
	@echo "Installing golang-migrate..."
	@go install -tags 'mysql' github.com/golang-migrate/migrate/v4/cmd/migrate@latest
	@echo "Installed successfully. Make sure $(shell go env GOPATH)/bin is in your PATH"

# Apply migrations
migrate-up:
	@echo "Applying migrations..."
	@migrate -path migrations -database "mysql://root:@b15m1ll4h@tcp(127.0.0.1:3306)/skeleton" up
	@echo "Migrations applied"

# Rollback last migration
migrate-down:
	@echo "Rolling back last migration..."
	@migrate -path migrations -database "mysql://root:@b15m1ll4h@tcp(127.0.0.1:3306)/skeleton" down 1
	@echo "Rollback complete"

# Create new migration
migrate-create:
	@echo "Creating new migration..."
	@read -p "Enter migration name: " name; \
	migrate create -ext sql -dir migrations -seq $$name
	@echo "Migration created"
