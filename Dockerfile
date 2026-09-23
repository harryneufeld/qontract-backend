# Multi-stage build for the Qontract backend (issue #35).
# The solution targets net10.0, so the SDK and runtime images must be
# .NET 10. Projects live in subdirectories, so their .csproj files are
# copied to their real paths before the restore layer (COPY *.csproj at
# the root matched nothing and restored an empty directory).

# --- Build stage -----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the solution + every project file first so the restore layer is
# cached until a .csproj actually changes.
COPY Qontract.sln ./
COPY Qontract.API/Qontract.API.csproj ./Qontract.API/
COPY Qontract.Application/Qontract.Application.csproj ./Qontract.Application/
COPY Qontract.Core/Qontract.Core.csproj ./Qontract.Core/
COPY Qontract.Domain/Qontract.Domain.csproj ./Qontract.Domain/
COPY Qontract.Infrastructure/Qontract.Infrastructure.csproj ./Qontract.Infrastructure/
COPY Qontract.Tests/Qontract.Tests.csproj ./Qontract.Tests/

# Restore the whole solution (project references make a single-project
# restore of Qontract.API alone insufficient).
RUN dotnet restore Qontract.sln

# Copy the rest of the sources and publish only the API project
# (framework-dependent: the runtime stage provides the ASP.NET runtime).
COPY . ./
RUN dotnet publish Qontract.API/Qontract.API.csproj -c Release -o /app/publish

# --- Runtime stage ---------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Qontract.API.dll"]
