#!/bin/bash

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to print colored messages
print_info() {
    echo -e "${BLUE}ℹ ${NC}$1"
}

print_success() {
    echo -e "${GREEN}✓${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}⚠${NC} $1"
}

print_error() {
    echo -e "${RED}✗${NC} $1"
}

# Function to check if a Docker container is running
is_container_running() {
    local container_name=$1
    docker ps --filter "name=${container_name}" --format "{{.Names}}" | grep -q "^${container_name}$"
}

# Function to check if a port is in use
is_port_in_use() {
    local port=$1
    # Try ss first (most modern)
    if command -v ss &> /dev/null; then
        ss -tuln 2>/dev/null | grep -qE ":${port}[[:space:]]"
        return $?
    # Try netstat
    elif command -v netstat &> /dev/null; then
        netstat -tuln 2>/dev/null | grep -qE "[.:]${port}[[:space:]]"
        return $?
    # Try nc (netcat) as fallback
    elif command -v nc &> /dev/null; then
        nc -z localhost ${port} &> /dev/null
        return $?
    # Last resort: try to connect using bash
    else
        timeout 1 bash -c "cat < /dev/null > /dev/tcp/localhost/${port}" 2>/dev/null
        return $?
    fi
}

# Function to ask for confirmation
confirm() {
    local message=$1
    local response
    while true; do
        read -p "$(echo -e ${YELLOW}${message}${NC} [y/n]: )" response
        case $response in
            [Yy]* ) return 0;;
            [Nn]* ) return 1;;
            * ) echo "Please answer yes (y) or no (n).";;
        esac
    done
}

# Check if Docker is installed
print_info "Checking Docker installation..."
if ! command -v docker &> /dev/null; then
    print_error "Docker is not installed. Please install Docker first."
    exit 1
fi

if ! docker info &> /dev/null; then
    print_error "Docker daemon is not running. Please start Docker."
    
    # WSL specific advice
    if grep -qi microsoft /proc/version; then
        echo ""
        print_info "WSL detected. If you are using Docker Desktop for Windows:"
        echo "  1. Ensure Docker Desktop is running on Windows."
        echo "  2. Go to Settings > Resources > WSL Integration."
        echo "  3. Ensure your WSL distribution is enabled."
        echo ""
        print_info "If you are using native Docker engine in WSL:"
        echo "  Run: sudo service docker start"
    fi
    exit 1
fi

print_success "Docker is installed and running"
echo ""

# ============================================
# MySQL Setup
# ============================================
print_info "Checking MySQL..."
MYSQL_CONTAINER="skeleton-mysql"
MYSQL_PORT=3306

if is_port_in_use ${MYSQL_PORT}; then
    print_success "MySQL is already running on port ${MYSQL_PORT}"
    if is_container_running "${MYSQL_CONTAINER}"; then
        print_info "Running in Docker container: ${MYSQL_CONTAINER}"
    else
        print_info "Running outside Docker or in a different container"
    fi
elif is_container_running "${MYSQL_CONTAINER}"; then
    print_success "MySQL container '${MYSQL_CONTAINER}' is already running"
elif docker ps -a --filter "name=${MYSQL_CONTAINER}" --format "{{.Names}}" | grep -q "^${MYSQL_CONTAINER}$"; then
    print_warning "MySQL container '${MYSQL_CONTAINER}' exists but is not running"
    if confirm "Do you want to start the existing MySQL container?"; then
        docker start ${MYSQL_CONTAINER}
        print_success "MySQL container started"
    fi
else
    if confirm "MySQL is not detected on port ${MYSQL_PORT}. Do you want to install MySQL via Docker?"; then
        print_info "Installing MySQL..."
        docker run -d \
            --name ${MYSQL_CONTAINER} \
            -e MYSQL_ROOT_PASSWORD=@b15m1ll4h \
            -e MYSQL_DATABASE=skeleton \
            -e MYSQL_USER=skeleton \
            -e MYSQL_PASSWORD=skeleton \
            -p ${MYSQL_PORT}:3306 \
            --restart unless-stopped \
            mysql:8.0
        
        print_success "MySQL installed and running"
        print_info "MySQL credentials:"
        echo "  - Host: localhost"
        echo "  - Port: ${MYSQL_PORT}"
        echo "  - Database: skeleton"
        echo "  - Root Password: @b15m1ll4h"
        echo "  - User: skeleton / Password: skeleton"
    else
        print_warning "Skipping MySQL installation"
    fi
fi
echo ""

# ============================================
# Redis Setup
# ============================================
print_info "Checking Redis..."
REDIS_CONTAINER="skeleton-redis"
REDIS_PORT=6379

if is_port_in_use ${REDIS_PORT}; then
    print_success "Redis is already running on port ${REDIS_PORT}"
    if is_container_running "${REDIS_CONTAINER}"; then
        print_info "Running in Docker container: ${REDIS_CONTAINER}"
    else
        print_info "Running outside Docker or in a different container"
    fi
elif is_container_running "${REDIS_CONTAINER}"; then
    print_success "Redis container '${REDIS_CONTAINER}' is already running"
elif docker ps -a --filter "name=${REDIS_CONTAINER}" --format "{{.Names}}" | grep -q "^${REDIS_CONTAINER}$"; then
    print_warning "Redis container '${REDIS_CONTAINER}' exists but is not running"
    if confirm "Do you want to start the existing Redis container?"; then
        docker start ${REDIS_CONTAINER}
        print_success "Redis container started"
    fi
else
    if confirm "Redis is not detected on port ${REDIS_PORT}. Do you want to install Redis via Docker?"; then
        print_info "Installing Redis..."
        docker run -d \
            --name ${REDIS_CONTAINER} \
            -p ${REDIS_PORT}:6379 \
            --restart unless-stopped \
            redis:7-alpine
        
        print_success "Redis installed and running"
        print_info "Redis connection: localhost:${REDIS_PORT}"
    else
        print_warning "Skipping Redis installation"
    fi
fi
echo ""

# ============================================
# RabbitMQ Setup
# ============================================
print_info "Checking RabbitMQ..."
RABBITMQ_CONTAINER="skeleton-rabbitmq"
RABBITMQ_PORT=5672
RABBITMQ_MGMT_PORT=15672

if is_port_in_use ${RABBITMQ_PORT}; then
    print_success "RabbitMQ is already running on port ${RABBITMQ_PORT}"
    if is_container_running "${RABBITMQ_CONTAINER}"; then
        print_info "Running in Docker container: ${RABBITMQ_CONTAINER}"
    else
        print_info "Running outside Docker or in a different container"
    fi
elif is_container_running "${RABBITMQ_CONTAINER}"; then
    print_success "RabbitMQ container '${RABBITMQ_CONTAINER}' is already running"
elif docker ps -a --filter "name=${RABBITMQ_CONTAINER}" --format "{{.Names}}" | grep -q "^${RABBITMQ_CONTAINER}$"; then
    print_warning "RabbitMQ container '${RABBITMQ_CONTAINER}' exists but is not running"
    if confirm "Do you want to start the existing RabbitMQ container?"; then
        docker start ${RABBITMQ_CONTAINER}
        print_success "RabbitMQ container started"
    fi
else
    if confirm "RabbitMQ is not detected on port ${RABBITMQ_PORT}. Do you want to install RabbitMQ via Docker?"; then
        print_info "Installing RabbitMQ with delayed message plugin..."
        
        # Build custom RabbitMQ image with delayed message plugin
        print_info "Building RabbitMQ image with delayed message exchange plugin..."
        if ! docker build -t skeleton-rabbitmq:custom -f Dockerfile.rabbitmq .; then
            print_error "Failed to build custom RabbitMQ image. Please check your internet connection or Docker permissions."
            exit 1
        fi
        
        docker run -d \
            --name ${RABBITMQ_CONTAINER} \
            -e RABBITMQ_DEFAULT_USER=guest \
            -e RABBITMQ_DEFAULT_PASS=guest \
            -p ${RABBITMQ_PORT}:5672 \
            -p ${RABBITMQ_MGMT_PORT}:15672 \
            --restart unless-stopped \
            skeleton-rabbitmq:custom
        
        print_success "RabbitMQ installed and running with delayed message plugin"
        print_info "RabbitMQ credentials:"
        echo "  - AMQP: localhost:${RABBITMQ_PORT}"
        echo "  - Management UI: http://localhost:${RABBITMQ_MGMT_PORT}"
        echo "  - Username: guest"
        echo "  - Password: guest"
    else
        print_warning "Skipping RabbitMQ installation"
    fi
fi
echo ""

# ============================================
# Observability Stack (Elasticsearch + Kibana + APM)
# ============================================
print_info "Checking Observability Stack..."
KIBANA_CONTAINER="skeleton-kibana"
ELASTICSEARCH_CONTAINER="skeleton-elasticsearch"
APM_CONTAINER="skeleton-apm-server"
KIBANA_PORT=5601
ELASTICSEARCH_PORT=9200
APM_PORT=8200

# Check if any component is running
STACK_RUNNING=false
if is_container_running "${KIBANA_CONTAINER}" || is_container_running "${ELASTICSEARCH_CONTAINER}" || is_container_running "${APM_CONTAINER}"; then
    STACK_RUNNING=true
fi

if ${STACK_RUNNING}; then
    print_success "Observability components detected:"
    if is_container_running "${ELASTICSEARCH_CONTAINER}"; then
        print_info "  - Elasticsearch: http://localhost:${ELASTICSEARCH_PORT}"
    fi
    if is_container_running "${KIBANA_CONTAINER}"; then
        print_info "  - Kibana: http://localhost:${KIBANA_PORT}"
    fi
    if is_container_running "${APM_CONTAINER}"; then
        print_info "  - APM Server: http://localhost:${APM_PORT}"
    fi
else
    echo "Choose Observability installation mode:"
    echo "1) Full Elastic Stack (Recommended) - Includes Elasticsearch, Kibana, and APM Server"
    echo "2) Standalone APM Server - Lightweight, no Dashboard (Logging only)"
    echo "3) Skip"
    
    read -p "$(echo -e ${YELLOW}Select option [1-3]: ${NC})" choice
    
    case $choice in
        1)
            print_info "Installing Full Elastic Stack..."
            
            # Clean up old volumes if requested
            if docker volume ls | grep -q "elasticsearch_data"; then
                if confirm "Remove old Elasticsearch data volume? (Recommended for clean install)"; then
                    docker volume rm elasticsearch_data 2>/dev/null || true
                    print_info "Old volume removed"
                fi
            fi

            # Install Elasticsearch
            print_info "Installing Elasticsearch 7.17.15..."
            docker run -d \
                --name ${ELASTICSEARCH_CONTAINER} \
                -e "discovery.type=single-node" \
                -e "xpack.security.enabled=false" \
                -e "ES_JAVA_OPTS=-Xms512m -Xmx512m" \
                -e "cluster.routing.allocation.disk.threshold_enabled=false" \
                -p ${ELASTICSEARCH_PORT}:9200 \
                --restart unless-stopped \
                docker.elastic.co/elasticsearch/elasticsearch:7.17.15

            print_info "Waiting for Elasticsearch to start..."
            sleep 15

            # Install Kibana
            print_info "Installing Kibana 7.17.15..."
            docker run -d \
                --name ${KIBANA_CONTAINER} \
                --link ${ELASTICSEARCH_CONTAINER}:elasticsearch \
                -e "ELASTICSEARCH_HOSTS=http://elasticsearch:9200" \
                -p ${KIBANA_PORT}:5601 \
                --restart unless-stopped \
                docker.elastic.co/kibana/kibana:7.17.15

            # Install APM Server (Connected)
            print_info "Installing APM Server 7.17.15..."
            docker rm -f ${APM_CONTAINER} 2>/dev/null || true
            
            docker run -d \
                --name ${APM_CONTAINER} \
                --link ${ELASTICSEARCH_CONTAINER}:elasticsearch \
                -e "output.elasticsearch.hosts=['elasticsearch:9200']" \
                -e "apm-server.host=0.0.0.0:8200" \
                -p ${APM_PORT}:8200 \
                --restart unless-stopped \
                docker.elastic.co/apm/apm-server:7.17.15
                
            print_success "Full Elastic Stack installed!"
            print_info "Services:"
            echo "  - Elasticsearch: http://localhost:${ELASTICSEARCH_PORT}"
            echo "  - Kibana Dashboard: http://localhost:${KIBANA_PORT}"
            echo "  - APM Server: http://localhost:${APM_PORT}"
            echo ""
            print_warning "Note: Kibana may take 1-2 minutes to fully start up"
            print_info "Access Kibana at http://localhost:${KIBANA_PORT} to view APM data"
            ;;
            
        2)
            print_info "Installing Standalone APM Server..."
            docker run -d \
                --name ${APM_CONTAINER} \
                -p ${APM_PORT}:8200 \
                -e "output.elasticsearch.enabled=false" \
                -e "apm-server.host=0.0.0.0:8200" \
                -e "apm-server.rum.enabled=true" \
                -e "logging.level=info" \
                -e "logging.to_stderr=true" \
                --restart unless-stopped \
                docker.elastic.co/apm/apm-server:7.17.15
                
            print_success "Standalone APM Server installed!"
            print_info "APM Server URL: http://localhost:${APM_PORT}"
            print_warning "Note: This is standalone mode without dashboard"
            ;;
            
        *)
            print_warning "Skipping Observability setup"
            ;;
    esac
fi
echo ""

# ============================================
# Flipt Feature Flag Server Setup
# ============================================
print_info "Checking Flipt Feature Flag Server..."
FLIPT_CONTAINER="skeleton-flipt"
FLIPT_PORT=8080
FLIPT_GRPC_PORT=9000

if is_port_in_use ${FLIPT_PORT}; then
    print_success "Flipt is already running on port ${FLIPT_PORT}"
    if is_container_running "${FLIPT_CONTAINER}"; then
        print_info "Running in Docker container: ${FLIPT_CONTAINER}"
    else
        print_info "Running outside Docker or in a different container"
    fi
elif is_container_running "${FLIPT_CONTAINER}"; then
    print_success "Flipt container '${FLIPT_CONTAINER}' is already running"
elif docker ps -a --filter "name=${FLIPT_CONTAINER}" --format "{{.Names}}" | grep -q "^${FLIPT_CONTAINER}$"; then
    print_warning "Flipt container '${FLIPT_CONTAINER}' exists but is not running"
    if confirm "Do you want to start the existing Flipt container?"; then
        docker start ${FLIPT_CONTAINER}
        print_success "Flipt container started"
    fi
else
    if confirm "Flipt Feature Flag Server is not detected on port ${FLIPT_PORT}. Do you want to install Flipt via Docker?"; then
        print_info "Installing Flipt Feature Flag Server..."
        docker run -d \
            --name ${FLIPT_CONTAINER} \
            -p ${FLIPT_PORT}:8080 \
            -p ${FLIPT_GRPC_PORT}:9000 \
            -v flipt_data:/var/opt/flipt \
            --restart unless-stopped \
            flipt/flipt:latest
        
        print_success "Flipt Feature Flag Server installed and running"
        print_info "Flipt access:"
        echo "  - Web UI: http://localhost:${FLIPT_PORT}"
        echo "  - gRPC: localhost:${FLIPT_GRPC_PORT}"
        echo "  - No authentication required (development mode)"
        print_info "Access the UI to create and manage feature flags"
    else
        print_warning "Skipping Flipt installation"
    fi
fi
echo ""

# ============================================
# Google Cloud Pub/Sub Emulator Setup
# ============================================
print_info "Checking Google Cloud Pub/Sub Emulator..."
PUBSUB_CONTAINER="skeleton-pubsub"
PUBSUB_PORT=8085

if is_port_in_use ${PUBSUB_PORT}; then
    print_success "Pub/Sub Emulator is already running on port ${PUBSUB_PORT}"
    if is_container_running "${PUBSUB_CONTAINER}"; then
        print_info "Running in Docker container: ${PUBSUB_CONTAINER}"
    else
        print_info "Running outside Docker or in a different container"
    fi
elif is_container_running "${PUBSUB_CONTAINER}"; then
    print_success "Pub/Sub Emulator container '${PUBSUB_CONTAINER}' is already running"
elif docker ps -a --filter "name=${PUBSUB_CONTAINER}" --format "{{.Names}}" | grep -q "^${PUBSUB_CONTAINER}$"; then
    print_warning "Pub/Sub Emulator container '${PUBSUB_CONTAINER}' exists but is not running"
    if confirm "Do you want to start the existing Pub/Sub Emulator container?"; then
        docker start ${PUBSUB_CONTAINER}
        print_success "Pub/Sub Emulator container started"
    fi
else
    if confirm "Pub/Sub Emulator is not detected on port ${PUBSUB_PORT}. Do you want to install it via Docker?"; then
        print_info "Installing Google Cloud Pub/Sub Emulator..."
        docker run -d \
            --name ${PUBSUB_CONTAINER} \
            -p ${PUBSUB_PORT}:8085 \
            --restart unless-stopped \
            google/cloud-sdk:emulators \
            gcloud beta emulators pubsub start --project=test-project --host-port=0.0.0.0:8085
        
        print_success "Pub/Sub Emulator installed and running"
        print_info "Pub/Sub Emulator connection: localhost:${PUBSUB_PORT}"
        print_info "Project ID: test-project"
        print_info "Set env var: export PUBSUB_EMULATOR_HOST=localhost:${PUBSUB_PORT}"
    else
        print_warning "Skipping Pub/Sub Emulator installation"
    fi
fi
echo ""

# ============================================
# Summary
# ============================================
echo "========================================"
print_info "Setup Summary"
echo "========================================"

echo ""
print_info "Running containers:"
docker ps --filter "name=skeleton-" --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"

echo ""
print_success "Setup complete! You can now run your application."
print_info "To stop all containers: docker stop \$(docker ps -q --filter 'name=skeleton-')"
print_info "To remove all containers: docker rm \$(docker ps -aq --filter 'name=skeleton-')"
