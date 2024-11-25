# Use the .NET 9 SDK base image for building the app
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

# Set the working directory in the container
WORKDIR /app

# Copy the project file and restore dependencies
COPY *.csproj ./
RUN dotnet restore --source "https://api.nuget.org/v3/index.json"

# Copy the rest of the application code
COPY . ./

# Build the application
RUN dotnet publish -c Release -o /out

# Use a smaller .NET 9 runtime image for the final container
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

# Set the working directory in the container
WORKDIR /app

# Copy the built application from the previous stage
COPY --from=build /out ./

# Run the application
ENTRYPOINT ["dotnet", "spotify-radio-bot.dll"]
