# RFID Job Control API

A RESTful API service for managing and controlling RFID job operations. This API allows you to configure, start, monitor, and stop RFID tag reading and writing jobs remotely.

## Features

- REST API for remote job control
- Support for all existing job strategies
- Real-time job status monitoring
- Centralized configuration management
- Detailed job metrics and logs
- Swagger UI for API documentation and testing

## Getting Started

### Prerequisites

- .NET 8.0 SDK or Docker
- Existing RFID reader infrastructure

### Building and Running the API

#### Using .NET CLI

```bash
# Clone the repository
git clone https://github.com/your-repo/rfid-job-control.git
cd rfid-job-control

# Build the solution
dotnet build

# Run the API
cd JobControlAPI
dotnet run
```

#### Using Docker

```bash
# Build the Docker image
docker build -t rfid-job-control .

# Run the container
docker run -d -p 5000:5000 --name rfid-api rfid-job-control
```

### API Access

Once running, the API is available at:

- API Endpoint: `http://localhost:5000/api`
- Swagger UI: `http://localhost:5000/swagger`

## API Endpoints

### Jobs

- `GET /api/job` - Get all jobs
- `GET /api/job/{jobId}` - Get a specific job
- `POST /api/job` - Create a new job
- `POST /api/job/{jobId}/start` - Start a job
- `POST /api/job/{jobId}/stop` - Stop a job
- `GET /api/job/{jobId}/metrics` - Get job metrics
- `GET /api/job/{jobId}/logs` - Get job logs
- `GET /api/job/strategies` - Get available job strategies

### Configurations

- `GET /api/configuration` - Get all configurations
- `GET /api/configuration/{id}` - Get a specific configuration
- `POST /api/configuration` - Create a new configuration
- `PUT /api/configuration/{id}` - Update a configuration
- `DELETE /api/configuration/{id}` - Delete a configuration

### System Status

- `GET /api/status` - Get system status
- `GET /api/status/version` - Get API version
- `GET /api/status/readers` - Get connected readers
- `GET /api/status/metrics` - Get system metrics

## Example Usage

### Creating a Job

```bash
curl -X POST "http://localhost:5000/api/job" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Job1",
    "strategyType": "JobStrategy1SpeedStrategy",
    "readerSettings": {
      "writer": {
        "hostname": "192.168.1.100",
        "txPowerInDbm": 33
      }
    },
    "parameters": {
      "epcHeader": "E7",
      "sku": "012345678901"
    }
  }'
```

### Starting a Job

```bash
curl -X POST "http://localhost:5000/api/job/{jobId}/start" \
  -H "Content-Type: application/json" \
  -d '{
    "timeoutSeconds": 300
  }'
```

### Monitoring Job Status

```bash
curl -X GET "http://localhost:5000/api/job/{jobId}"
```

### Stopping a Job

```bash
curl -X POST "http://localhost:5000/api/job/{jobId}/stop"
```

## Architecture

The API is built with ASP.NET Core and follows a clean architecture pattern:

- **Controllers:** Handle HTTP requests and responses
- **Services:** Implement business logic and job management
- **Models:** Define data structures for API operations
- **Background Services:** Manage long-running operations

## Extending the API

To add a new job strategy:

1. Create a new class that implements `IJobStrategy` in the OctaneTagWritingTest project
2. Implement the `RunJob` method with your custom logic
3. The new strategy will automatically be available through the API

## License

This project is licensed under the MIT License - see the LICENSE file for details.