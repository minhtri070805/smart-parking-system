# Build and publish the Parking Operations API.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first so package restore can be reused between builds.
COPY src/ParkingOperations.WebAPI/ParkingOperations.WebAPI.csproj src/ParkingOperations.WebAPI/
COPY src/ParkingOperations.Application/ParkingOperations.Application.csproj src/ParkingOperations.Application/
COPY src/ParkingOperations.Domain/ParkingOperations.Domain.csproj src/ParkingOperations.Domain/
COPY src/ParkingOperations.Infrastructure/ParkingOperations.Infrastructure.csproj src/ParkingOperations.Infrastructure/
COPY src/ParkingOperations.Persistence/ParkingOperations.Persistence.csproj src/ParkingOperations.Persistence/
RUN dotnet restore src/ParkingOperations.WebAPI/ParkingOperations.WebAPI.csproj

COPY . .
RUN dotnet publish src/ParkingOperations.WebAPI/ParkingOperations.WebAPI.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

# Run the API from the smaller ASP.NET runtime image.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "ParkingOperations.WebAPI.dll"]
