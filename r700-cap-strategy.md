# Impinj R700 CAP API Documentation

This document provides comprehensive information about the Impinj R700 CAP (Container Application) strategy implementation in the RFID Job Control API. The R700 CAP strategy provides specialized functionality for reading and writing RFID tags using Impinj R700 readers through a RESTful API interface.

## Overview

The `ImpinjR700CapStrategy` is a specialized implementation designed to:

- Continuously monitor RFID tags in the field
- Record tag information (TID, EPC, RSSI, etc.)
- Provide tag data through a RESTful API
- Support write operations to update tags with new EPCs
- Support locking and permalocking of tags
- Track reading and writing performance metrics

The strategy operates as a long-running job in the RFID Job Control API system, allowing other applications to interact with it via HTTP requests.

## API Endpoints

### Base URL

All R700 CAP-specific API endpoints are prefixed with:

```
/api/r700
```

### Security

API requests may require an API key if configured. The key can be provided in:
- HTTP header: `X-API-KEY`
- Query parameter: `?apiKey=your_api_key`

## Endpoints Reference

### 1. Read Tags

Scans for tags in the RF field and returns detected tag information.

- **URL**: `/api/r700/read`
- **Method**: `POST`
- **Response**: List of currently detected tags with their metadata

#### Example Response

```json
{
  "tagCount": 2,
  "tags": [
    {
      "EPC": "E280110720004214A1234567",
      "TID": "E2003411B802011897200074",
      "EAN": "9781234567890",
      "accessMemory": "unlocked",
      "readerID": "Tunnel-01",
      "antennaID": 1,
      "RSSI": -65.5
    },
    {
      "EPC": "E280110720004214A7654321",
      "TID": "E2003411B802011897200082",
      "EAN": "9787654321098",
      "accessMemory": "locked",
      "readerID": "Tunnel-01",
      "antennaID": 1,
      "RSSI": -68.2
    }
  ]
}
```

#### CURL Example

```bash
curl -X POST "http://localhost:5000/api/r700/read" \
  -H "Content-Type: application/json" \
  -H "X-API-KEY: your_api_key"
```

### 2. Write Tags

Encodes tags with new EPCs and optional access passwords, then optionally locks them.

- **URL**: `/api/r700/write`
- **Method**: `POST`
- **Request Body**: List of tag write operations
- **Response**: Status of write operations

#### Request Body Format

```json
[
  {
    "TID": "E2003411B802011897200074",
    "NewEPC": "E280110720004214A9876543",
    "AccessPassword": "12345678"
  },
  {
    "TID": "E2003411B802011897200082",
    "NewEPC": "E280110720004214A0123456",
    "AccessPassword": "87654321"
  }
]
```

#### Example Response

```json
{
  "status": "success",
  "tags": [
    {
      "TID": "E2003411B802011897200074",
      "EPC": "E280110720004214A9876543",
      "WriteStatus": "Pending"
    },
    {
      "TID": "E2003411B802011897200082",
      "EPC": "E280110720004214A0123456",
      "WriteStatus": "Pending"
    }
  ]
}
```

#### CURL Example

```bash
curl -X POST "http://localhost:5000/api/r700/write" \
  -H "Content-Type: application/json" \
  -H "X-API-KEY: your_api_key" \
  -d '[
    {
      "TID": "E2003411B802011897200074",
      "NewEPC": "E280110720004214A9876543",
      "AccessPassword": "12345678"
    }
  ]'
```

### 3. Get Status

Gets the current status and metrics of the R700 CAP job.

- **URL**: `/api/r700/status`
- **Method**: `GET`
- **Response**: Current status, metrics, and performance data

#### Example Response

```json
{
  "success": true,
  "message": "R700 CAP status",
  "data": {
    "jobId": "r700_cap_20250516120000",
    "state": "Running",
    "jobName": "R700 CAP Job",
    "startTime": "2025-05-16T12:00:00Z",
    "totalTagsProcessed": 128,
    "successCount": 125,
    "failureCount": 3,
    "currentOperation": "Reading Tags",
    "metrics": {
      "UniqueTagsRead": 32,
      "AvgWriteTimeMs": 180.5,
      "AvgVerifyTimeMs": 85.2,
      "LockedTags": 28,
      "SuccessCount": 125,
      "FailureCount": 3,
      "ElapsedSeconds": 3600,
      "ReaderHostname": "192.168.1.100",
      "ReaderID": "Tunnel-01",
      "ReadRate": 25.6,
      "LockEnabled": true,
      "PermalockEnabled": false
    }
  }
}
```

#### CURL Example

```bash
curl -X GET "http://localhost:5000/api/r700/status" \
  -H "X-API-KEY: your_api_key"
```

## Job Configuration and Management

The R700 CAP strategy runs within the RFID Job Control API's job system. You can also use the general job endpoints to configure, start and stop the R700 CAP job.

### 1. Create Configuration

Create a new configuration for the R700 CAP strategy.

- **URL**: `/api/configuration`
- **Method**: `POST`
- **Request Body**: Configuration details

#### Example Request

```json
{
  "name": "R700 CAP Configuration",
  "strategyType": "ImpinjR700Cap",
  "parameters": {
    "enableLock": "true",
    "enablePermalock": "false",
    "ReaderID": "Tunnel-01"
  },
  "readerSettingsGroup": {
    "writer": {
      "name": "writer",
      "hostname": "192.168.1.100",
      "includeFastId": true,
      "includePeakRssi": true,
      "includeAntennaPortNumber": true,
      "reportMode": "Individual",
      "rfMode": 1003,
      "antennaPort": 1,
      "txPowerInDbm": 33,
      "maxRxSensitivity": true,
      "rxSensitivityInDbm": -70,
      "searchMode": "DualTarget",
      "session": 0,
      "memoryBank": "Epc",
      "parameters": {
        "ReaderID": "Tunnel-01"
      }
    }
  }
}
```

#### CURL Example

```bash
curl -X POST "http://localhost:5000/api/configuration" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "R700 CAP Configuration",
    "strategyType": "ImpinjR700Cap",
    "parameters": {
      "enableLock": "true",
      "enablePermalock": "false",
      "ReaderID": "Tunnel-01"
    },
    "readerSettingsGroup": {
      "writer": {
        "name": "writer",
        "hostname": "192.168.1.100",
        "includeFastId": true,
        "includePeakRssi": true,
        "includeAntennaPortNumber": true,
        "reportMode": "Individual",
        "rfMode": 1003,
        "antennaPort": 1,
        "txPowerInDbm": 33,
        "maxRxSensitivity": true,
        "rxSensitivityInDbm": -70,
        "searchMode": "DualTarget",
        "session": 0,
        "memoryBank": "Epc",
        "parameters": {
          "ReaderID": "Tunnel-01"
        }
      }
    }
  }'
```

### 2. Create Job

Create a new job using a configuration ID.

- **URL**: `/api/job`
- **Method**: `POST`
- **Request Body**: Job creation details with configuration ID

#### Example Request

```json
{
  "name": "R700 CAP Job",
  "strategyType": "ImpinjR700Cap",
  "configurationId": "config_id_from_create_response"
}
```

#### CURL Example

```bash
curl -X POST "http://localhost:5000/api/job" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "R700 CAP Job",
    "strategyType": "ImpinjR700Cap",
    "configurationId": "config_id_from_create_response"
  }'
```

### 3. Start Job

Start a job with the given ID.

- **URL**: `/api/job/{jobId}/start`
- **Method**: `POST`
- **Request Body**: Optional timeout settings

#### Example Request

```json
{
  "timeoutSeconds": 86400
}
```

#### CURL Example

```bash
curl -X POST "http://localhost:5000/api/job/r700_cap_20250516120000/start" \
  -H "Content-Type: application/json" \
  -d '{
    "timeoutSeconds": 86400
  }'
```

### 4. Stop Job

Stop a running job.

- **URL**: `/api/job/{jobId}/stop`
- **Method**: `POST`

#### CURL Example

```bash
curl -X POST "http://localhost:5000/api/job/r700_cap_20250516120000/stop"
```

## Data Model

### Tag Information

| Property | Description |
|----------|-------------|
| EPC | Electronic Product Code |
| TID | Tag Identifier |
| EAN | European Article Number (barcode) derived from EPC |
| AccessMemory | Status of memory ("locked" or "unlocked") |
| ReaderID | Identifier of the reader that detected the tag |
| AntennaID | Antenna port number that detected the tag |
| RSSI | Received Signal Strength Indicator (in dBm) |

### Tag Write Request

| Property | Description |
|----------|-------------|
| TID | Tag Identifier to write to |
| NewEPC | New Electronic Product Code to write |
| AccessPassword | (Optional) Access password for the tag |

## Automatic Job Management

The ImpinjR700CapStrategy is designed to start automatically when needed. When you make a request to `/api/r700/read` or `/api/r700/write`, the system will:

1. Check if an R700 CAP job is already running
2. If not, automatically create and start a new job using the default R700 CAP configuration
3. Process your request using the active job

This means you typically don't need to manually create and start an R700 CAP job - the system handles this for you when you make API requests.

## Configuration Options

### Reader Settings

| Setting | Description | Default |
|---------|-------------|---------|
| hostname | Hostname or IP address of the reader | 192.168.1.100 |
| includeFastId | Whether to include FastID in tag reports | true |
| includePeakRssi | Whether to include RSSI in tag reports | true |
| includeAntennaPortNumber | Whether to include antenna port in tag reports | true |
| reportMode | Tag report mode ("Individual" or "BatchAfterStop") | Individual |
| rfMode | RF mode index | 1003 |
| antennaPort | Antenna port to use | 1 |
| txPowerInDbm | Transmit power in dBm | 33 |
| maxRxSensitivity | Whether to use maximum receive sensitivity | true |
| rxSensitivityInDbm | Receive sensitivity in dBm | -70 |
| searchMode | Gen2 search mode ("SingleTarget", "DualTarget", "TagFocus") | DualTarget |
| session | Gen2 session (0-3) | 0 |

### Strategy Parameters

| Parameter | Description | Default |
|-----------|-------------|---------|
| enableLock | Whether to lock tags after writing | true |
| enablePermalock | Whether to permalock tags after writing | false |
| ReaderID | Identifier for the reader | Tunnel-01 |

## Best Practices

1. **Read Before Write**: Always perform a read operation to verify tag presence before attempting writes
2. **Error Handling**: Monitor the job status endpoint to detect and respond to errors
3. **Job Lifecycle**: For long-term use, explicitly create a job with appropriate timeout settings
4. **Memory Management**: Clear old tag data periodically using the job management API to prevent memory growth

## Troubleshooting

Common issues and solutions:

| Problem | Solution |
|---------|----------|
| No tags detected | Verify antenna connection and power settings |
| Write failures | Check access password and ensure tag is in antenna field |
| API returns 500 | Check if the reader is connected and accessible |
| Multiple jobs error | Use the status endpoint to verify current job state, stop any running jobs |
| API key failures | Ensure the API key is provided in header or query parameter |

## Example Workflow

1. **Configure the R700 CAP strategy**:
   ```bash
   curl -X POST "http://localhost:5000/api/configuration" -H "Content-Type: application/json" -d '{
     "name": "R700 CAP Config",
     "strategyType": "ImpinjR700Cap",
     "parameters": {"enableLock": "true", "ReaderID": "Tunnel-01"},
     "readerSettingsGroup": {"writer": {"hostname": "192.168.1.100", "txPowerInDbm": 30}}
   }'
   ```

2. **Read tags in the field**:
   ```bash
   curl -X POST "http://localhost:5000/api/r700/read"
   ```

3. **Write a new EPC to a specific tag**:
   ```bash
   curl -X POST "http://localhost:5000/api/r700/write" -H "Content-Type: application/json" -d '[
     {"TID": "E2003411B802011897200074", "NewEPC": "E280110720004214A9876543"}
   ]'
   ```

4. **Check job status and metrics**:
   ```bash
   curl -X GET "http://localhost:5000/api/r700/status"
   ```

5. **Stop the job when finished**:
   ```bash
   curl -X POST "http://localhost:5000/api/job/r700_cap_20250516120000/stop"
   ```
