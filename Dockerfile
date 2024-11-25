# Use the .NET SDK base image for building the app
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build

# Set the working directory in the container
WORKDIR /app

# Copy the project file and restore dependencies
COPY *.csproj ./
RUN dotnet restore

# Copy the rest of the application code
COPY . ./

# Build the application
RUN dotnet publish -c Release -o /out

# Use a smaller runtime image for the final container
FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS runtime

# Set the working directory in the container
WORKDIR /app

# Copy the built application from the previous stage
COPY --from=build /out ./

# Set the environment variable to configure logging
ENV DOTNET_EnableDiagnostics=0

# Run the application
ENTRYPOINT ["dotnet", "DiscordSpotifyBot.dll"]
