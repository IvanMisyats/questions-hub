FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER root
RUN apt-get update && apt-get install -y krb5-user libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["QuestionsHub.Blazor/QuestionsHub.Blazor.csproj", "QuestionsHub.Blazor/"]
RUN dotnet restore "QuestionsHub.Blazor/QuestionsHub.Blazor.csproj"
COPY . .
WORKDIR "/src/QuestionsHub.Blazor"
RUN dotnet build "QuestionsHub.Blazor.csproj" -c $BUILD_CONFIGURATION -o /app/build
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "QuestionsHub.Blazor.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "QuestionsHub.Blazor.dll"]
