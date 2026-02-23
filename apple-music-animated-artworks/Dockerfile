FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["apple-music-animated-artworks.csproj", "./"]
RUN dotnet restore "apple-music-animated-artworks.csproj"

COPY . .
RUN dotnet publish "apple-music-animated-artworks.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

RUN mkdir -p /app/data && mkdir -p /app/logs

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "apple-music-animated-artworks.dll"]