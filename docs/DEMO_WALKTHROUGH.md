# Skeleton API .NET - Demo Walkthrough

## 🎯 Demo Overview
Demonstrasi skeleton-api-net: Clean Architecture, multiple protocols (REST/gRPC/SSE/MQ), observability (APM/Logging/Diagnostics), advanced features (Feature Flags with Caching/Circuit Breaker/Redis Caching).

## 📋 Pre-Demo Checklist

### Services Running
```bash
docker ps | grep -E "mysql|redis|rabbitmq|apm-server|kibana|flipt"
```
Expected: MySQL:3307, Redis:6379, RabbitMQ:5672/15672, APM:8200, Kibana:5601, Flipt:8080

### Application
```bash
cd /home/mnz/Workspace/architect/my-skeleton/skeleton-api-net
make run
curl http://localhost:4021/health
```

### Credentials & Tokens
Set this variable in your terminal for use in subsequent commands:
```bash
export AUTH_TOKEN="eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkRlbW8gVXNlciIsImlhdCI6MTUxNjIzOTAyMn0.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c"
```

## 🎬 Demo Script

### Part 1: REST API (5 min)

**Create User:**
```bash
curl -X POST http://localhost:4021/v1/user \
  -H "Authorization: Bearer $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
  "username": "demo_user", "email": "demo@example.com",
  "password": "SecurePass123!", "full_name": "Demo User", "role": "user"
}'
```

**Get User (Cache Demo):**
```bash
# 1st call: ~50ms (DB) | 2nd call: ~5ms (Redis Cache)
curl http://localhost:4021/v1/user/{user_id} -H "Authorization: Bearer $AUTH_TOKEN"
```

**Get All Users Paginated:**
```bash
curl "http://localhost:4021/v1/users/paginated?page=1&page_size=10" -H "Authorization: Bearer $AUTH_TOKEN"
```

### Part 2: gRPC with JSON Transcoding (3 min)

**Using grpcurl:**
```bash
grpcurl -plaintext localhost:4022 list
grpcurl -plaintext -rpc-header "authorization: Bearer $AUTH_TOKEN" -d '{"id": "user-id"}' localhost:4022 proto.UserGrpcService/GetById
```

**Using REST (JSON Transcoding):**
```bash
# gRPC endpoint accessible via HTTP
curl http://localhost:4021/proto.UserGrpcService/GetAll -H "Authorization: Bearer $AUTH_TOKEN"
```

### Part 3: SSE Real-time (3 min)

**Terminal 1 - Open stream:**
```bash
curl -N http://localhost:4021/api/v1/users/stream -H "Authorization: Bearer $AUTH_TOKEN"
```

**Terminal 2 - Trigger event:**
```bash
curl -X POST http://localhost:4021/v1/user \
  -H "Authorization: Bearer $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
  "username": "sse_demo", "email": "sse@example.com",
  "password": "SecurePass123!", "full_name": "SSE Demo", "role": "user"
}'
```
Terminal 1 akan menampilkan event real-time!

### Part 4: Message Queue (4 min)

**RabbitMQ UI:** http://localhost:15672 (guest/guest)

**Watch logs & create user:**
```bash
# Terminal 1: Watch logs
dotnet run --project src/SkeletonApi/SkeletonApi.csproj | grep "user.created"

# Terminal 2: Create user
curl -X POST http://localhost:4021/v1/user \
  -H "Authorization: Bearer $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
  "username": "mq_demo", "email": "mq@example.com",
  "password": "SecurePass123!", "full_name": "MQ Demo", "role": "user"
}'
```

**Show in logs:**
1. "Published message to topic: user.created"
2. "Received message from RabbitMQ"
3. "Processing user created event"

### Part 5: Kibana APM (5 min)

**Open:** http://localhost:5601/app/apm → skeleton-api-net

**Show distributed trace:**
- HTTP request → DB INSERT → Redis SET → RabbitMQ publish
- Consumer transaction (linked via `parent_trace_id`) → External service call

**Service Map:** skeleton-api-net connections to MySQL/Redis/RabbitMQ

**Transaction Timeline:**
```
POST /proto.UserGrpcService/Add (21ms)
├─ Database → INSERT users (18ms)
├─ Redis → SET user:id (683μs)
└─ Messaging → publish_user_created (1.6ms)
```

### Part 6: Feature Flags with Caching (3 min)

**Get all flags:**
```bash
curl http://localhost:4021/api/v1/feature-flags -H "Authorization: Bearer $AUTH_TOKEN"
```

**Get cache stats:**
```bash
curl http://localhost:4021/api/v1/feature-flags/stats -H "Authorization: Bearer $AUTH_TOKEN"
```

**Performance comparison:**
```bash
# 1st call: ~50ms (Flipt) | 2nd call: ~7ms (Cache) - 85% faster!
time curl http://localhost:4021/api/v1/feature-flags -H "Authorization: Bearer $AUTH_TOKEN"
time curl http://localhost:4021/api/v1/feature-flags -H "Authorization: Bearer $AUTH_TOKEN"
```

**Invalidate cache:**
```bash
curl -X POST http://localhost:4021/api/v1/feature-flags/cache/invalidate -H "Authorization: Bearer $AUTH_TOKEN"
```

### Part 7: Diagnostic Server (3 min)

**Enable diagnostics** in `appsettings.json`:
```json
{
  "Profiling": {
    "Enabled": true,
    "Port": 6060
  }
}
```

**Collect memory dump:**
```bash
curl http://localhost:6060/debug/diagnostics/dump -o memory.dmp
```

**View GC stats:**
```bash
curl http://localhost:6060/debug/diagnostics/gc
```

**Analyze with dotnet-dump:**
```bash
dotnet-dump analyze memory.dmp
```

### Part 8: Swagger UI (2 min)

**Open:** http://localhost:4021/swagger/

**Show:**
- Auto-generated API documentation
- Try out endpoints interactively
- gRPC methods exposed as REST via JSON Transcoding

## 🎤 Key Points

**Architecture:** Clean Architecture with proper layer separation, testable, multiple delivery protocols (REST/gRPC/SSE)

**Performance:** 
- Redis caching (50ms→5ms)
- Feature flag caching (50ms→7ms, 85% faster)
- Connection pooling
- Circuit breaker with Polly

**Observability:** 
- Distributed tracing with Elastic APM
- Structured logging with Serilog
- Production diagnostics server
- Prometheus metrics for feature flags

**Production-ready:** 
- Health checks (liveness/readiness)
- Graceful shutdown (180s grace period)
- FluentValidation
- TLS/HTTPS support
- Non-root Docker user

## 📊 Metrics

- REST: ~50ms (DB), ~5ms (cache)
- gRPC: ~30ms avg
- SSE: <1ms delivery
- Feature Flag Cache Hit Rate: 97%
- Circuit Breaker: 3 retries with exponential backoff (2s, 4s, 8s)

## 🐛 Troubleshooting

**Services not running:** `make setup-deps`
**Port in use:** `lsof -i :4021` then `kill -9 <PID>`
**DB failed:** `docker restart skeleton-mysql`
**No APM traces:** Check `ElasticApm:Enabled` in appsettings.json, restart app
**Flipt not responding:** `docker restart skeleton-flipt`

## ✅ Cleanup

```bash
# Stop app: Ctrl+C

# Clean test data
mysql -u root -p@b15m1ll4h -h localhost -P 3307 skeleton_db \
  -e "DELETE FROM users WHERE username LIKE 'demo_%' OR username LIKE 'sse_%' OR username LIKE 'mq_%'"

redis-cli -p 6379 FLUSHDB
```

## 🚀 Generator Demo (Bonus)

**Generate new project from SQL schema:**
```bash
make gen ARGS="project --input examples/athlete_management.sql --type schema --name AthleteManagement --output ../AthleteManagement"
cd ../AthleteManagement
make run
```

**Generate with remote package:**
```bash
make gen-remote ARGS="project --input examples/athlete_management.sql --type schema --name TestRemote --output ../TestRemote"
cd ../TestRemote
# Setup NuGet registry
export GITLAB_USERNAME=your.username
export GITLAB_TOKEN=glpat-xxxxxxxxxxxx
chmod +x setup-nuget.sh
./setup-nuget.sh
dotnet restore
make run
```
