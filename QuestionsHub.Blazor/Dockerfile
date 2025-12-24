# Use Alpine-based images for smaller size (~100MB vs ~200MB)
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base

# ICU provides cultures like uk-UA; tzdata is often useful too
# Also install Kerberos libraries required by Npgsql
RUN apk add --no-cache icu-libs icu-data-full tzdata krb5-libs

# Ensure invariant mode is OFF
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
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
