# Use the official .NET SDK image for building the application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copy the solution file and all .csproj files
COPY *.sln .
COPY OctaneTagJobControlAPI/*.csproj ./JobControlAPI/
COPY TagUtils/*.csproj ./TagUtils/
COPY EpcListGenerator/*.csproj ./EpcListGenerator/

# Restore dependencies
RUN dotnet restore

# Copy the rest of the source code
COPY . .

# Build the application
RUN dotnet publish OctaneTagJobControlAPI/OctaneTagJobControlAPI.csproj -c Release -o out

# Build the runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Copy the published application
COPY --from=build /app/out .

# Create necessary directories
RUN mkdir -p Logs Configs Data

# Set environment variables
ENV ASPNETCORE_URLS=http://+:5000

# Expose port 5000
EXPOSE 5000

# Set the entry point
ENTRYPOINT ["dotnet", "OctaneTagJobControlAPI.dll"]