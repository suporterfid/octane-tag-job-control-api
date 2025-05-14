# API Endpoints Curl Examples

## Configuration Controller

### Get All Configurations
```bash
curl -X GET "http://localhost:5000/api/Configuration"
```

### Get Configuration by ID
```bash
curl -X GET "http://localhost:5000/api/Configuration/123"
```

### Create Configuration
```bash
curl -X POST "http://localhost:5000/api/Configuration" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Test Configuration",
    "strategyType": "ReadOnlyLogging",
    "readerSettings": {
      "hostname": "192.168.1.100",
      "port": 5084
    },
    "parameters": {
      "param1": "value1",
      "param2": "value2"
    }
  }'
```

### Update Configuration
```bash
curl -X PUT "http://localhost:5000/api/Configuration/123" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Updated Configuration",
    "strategyType": "ReadOnlyLogging",
    "readerSettings": {
      "hostname": "192.168.1.100",
      "port": 5084
    },
    "parameters": {
      "param1": "updated_value1"
    }
  }'
```

### Delete Configuration
```bash
curl -X DELETE "http://localhost:5000/api/Configuration/123"
```

## Job Controller

### Get All Jobs
```bash
# Basic usage
curl -X GET "http://localhost:5000/api/Job"

# With sorting and running jobs first
curl -X GET "http://localhost:5000/api/Job?sortBy=date&runningFirst=true"

# Sort options: date, name, status, progress
curl -X GET "http://localhost:5000/api/Job?sortBy=name"
```

### Get Job by ID
```bash
curl -X GET "http://localhost:5000/api/Job/123"
```

### Create Job
```bash
curl -X POST "http://localhost:5000/api/Job" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Test Job",
    "strategyType": "ReadOnlyLogging",
    "readerSettings": {
      "hostname": "192.168.1.100",
      "port": 5084
    },
    "parameters": {
      "param1": "value1"
    }
  }'
```

### Start Job
```bash
# Basic start
curl -X POST "http://localhost:5000/api/Job/123/start" \
  -H "Content-Type: application/json" \
  -d '{}'

# Start with timeout
curl -X POST "http://localhost:5000/api/Job/123/start" \
  -H "Content-Type: application/json" \
  -d '{
    "timeoutSeconds": 600
  }'
```

### Stop Job
```bash
curl -X POST "http://localhost:5000/api/Job/123/stop"
```

### Get Job Metrics
```bash
curl -X GET "http://localhost:5000/api/Job/123/metrics"
```

### Get Job Logs
```bash
# Default (100 entries)
curl -X GET "http://localhost:5000/api/Job/123/logs"

# Custom number of entries
curl -X GET "http://localhost:5000/api/Job/123/logs?maxEntries=50"
```

### Get Available Strategies
```bash
curl -X GET "http://localhost:5000/api/Job/strategies"
```

## Status Controller

### Get System Status
```bash
curl -X GET "http://localhost:5000/api/Status"
```

### Get Version Information
```bash
curl -X GET "http://localhost:5000/api/Status/version"
```

### Get Connected Readers
```bash
curl -X GET "http://localhost:5000/api/Status/readers"
```

### Get System Metrics
```bash
curl -X GET "http://localhost:5000/api/Status/metrics"
```

### Get Health Status
```bash
curl -X GET "http://localhost:5000/api/Status/health"
```

### Get System Logs
```bash
# Default (100 entries)
curl -X GET "http://localhost:5000/api/Status/logs"

# Custom number of entries
curl -X GET "http://localhost:5000/api/Status/logs?maxEntries=50"
```

### Get System Files
```bash
# Root directory
curl -X GET "http://localhost:5000/api/Status/files"

# Specific directory
curl -X GET "http://localhost:5000/api/Status/files?path=Logs"
