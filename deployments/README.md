# Deployment Guide

## Docker Deployment

### Build Docker Image

```bash
make docker-build
```

Or manually:
```bash
docker build -t skeleton-api-net:latest -f deployments/Dockerfile .
```

### Run Docker Container

```bash
make docker-run
```

Or manually:
```bash
docker run -p 4021:4021 -p 4022:4022 -p 4023:4023 \
  -e Database__Password=your_password \
  skeleton-api-net:latest
```

### Push to Registry

```bash
# Tag for your registry
docker tag skeleton-api-net:latest your-registry/skeleton-api-net:v1.0.0

# Push
docker push your-registry/skeleton-api-net:v1.0.0
```

## Kubernetes Deployment

### Prerequisites

1. Kubernetes cluster (GKE, EKS, AKS, or local)
2. `kubectl` configured
3. Docker image pushed to accessible registry

### Create Secrets

```bash
# Database credentials
kubectl create secret generic skeleton-secrets \
  --from-literal=database-host=mysql-service \
  --from-literal=database-password=your_password

# APM configuration
kubectl create configmap configmap-apm \
  --from-literal=ELASTIC_APM_SERVER_URL=http://apm-server:8200 \
  --from-literal=ELASTIC_APM_ENVIRONMENT=production

# SSL certificates (if using keytool init container)
kubectl create secret generic ca-key \
  --from-file=ca-cert.pem=path/to/ca-cert.pem \
  --from-file=ca-key.pem=path/to/ca-key.pem
```

### Deploy Application

```bash
# Apply all manifests
kubectl apply -f deployments/service.yaml

# Check deployment status
kubectl get pods -l app=skeleton-api-net
kubectl get svc skeleton-api-net
kubectl get hpa skeleton-api-net-hpa

# View logs
kubectl logs -f deployment/skeleton-api-net

# Describe pod for troubleshooting
kubectl describe pod -l app=skeleton-api-net
```

### Update Deployment

```bash
# Update image
kubectl set image deployment/skeleton-api-net \
  skeleton-api-net=your-registry/skeleton-api-net:v1.1.0

# Or edit deployment
kubectl edit deployment skeleton-api-net

# Rollout status
kubectl rollout status deployment/skeleton-api-net

# Rollback if needed
kubectl rollout undo deployment/skeleton-api-net
```

### Access Diagnostic Endpoints

Diagnostic port (6060) is NOT exposed via Service for security. Use port-forward:

```bash
# Get pod name
POD_NAME=$(kubectl get pods -l app=skeleton-api-net -o jsonpath='{.items[0].metadata.name}')

# Port forward
kubectl port-forward pod/$POD_NAME 6060:6060

# Access in browser or curl
curl http://localhost:6060/debug/diagnostics/
```

### Scaling

```bash
# Manual scaling
kubectl scale deployment skeleton-api-net --replicas=5

# HPA will auto-scale between 2-10 replicas based on CPU/memory
kubectl get hpa skeleton-api-net-hpa
```

### Delete Deployment

```bash
kubectl delete -f deployments/service.yaml
kubectl delete secret skeleton-secrets
kubectl delete configmap configmap-apm skeleton-config
kubectl delete secret ca-key
```

## Environment Variables

Override configuration via environment variables:

```bash
# Database
Database__Host=mysql-host
Database__Port=3306
Database__Password=secret

# Cache
Cache__Host=redis-host
Cache__Port=6379

# Message Broker
MessageBroker__ClientId=3
MessageBroker__RabbitMQ__Host=rabbitmq-host

# APM
ElasticApm__ServerUrl=http://apm:8200
ElasticApm__Environment=production

# Profiling
Profiling__Enabled=true
Profiling__Port=6060
```

## Health Checks

- **Liveness**: `GET /health/live` - Returns 200 if app is running
- **Readiness**: `GET /health/ready` - Returns 200 if app is ready to serve traffic
- **General**: `GET /health` - Returns detailed health status

## Troubleshooting

### Pod not starting

```bash
# Check events
kubectl describe pod -l app=skeleton-api-net

# Check logs
kubectl logs -l app=skeleton-api-net --tail=100

# Check init container logs
kubectl logs -l app=skeleton-api-net -c keytool
```

### Database connection issues

```bash
# Verify secret
kubectl get secret skeleton-secrets -o yaml

# Test database connectivity from pod
kubectl exec -it deployment/skeleton-api-net -- /bin/sh
# Inside pod:
wget -O- http://mysql-service:3306
```

### Memory/CPU issues

```bash
# Check resource usage
kubectl top pods -l app=skeleton-api-net

# Check HPA status
kubectl get hpa skeleton-api-net-hpa

# Adjust resources in deployment if needed
kubectl edit deployment skeleton-api-net
```

## Monitoring

### Metrics

- Prometheus metrics: `GET /metrics` (if enabled)
- APM: Check Elastic APM dashboard
- Kubernetes metrics: `kubectl top pods`

### Logs

```bash
# Stream logs
kubectl logs -f deployment/skeleton-api-net

# Logs from all replicas
kubectl logs -l app=skeleton-api-net --tail=100 -f

# Logs with timestamps
kubectl logs deployment/skeleton-api-net --timestamps
```

## Production Checklist

- [ ] Update image tag in `service.yaml`
- [ ] Set correct database credentials in secrets
- [ ] Configure APM server URL
- [ ] Set resource limits appropriately
- [ ] Enable HTTPS with valid certificates
- [ ] Configure HPA min/max replicas
- [ ] Set up monitoring and alerting
- [ ] Test health check endpoints
- [ ] Verify graceful shutdown (terminationGracePeriodSeconds)
- [ ] Review security: non-root user, read-only volumes
- [ ] Disable profiling in production (or restrict access)
