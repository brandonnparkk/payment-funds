# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy project files first so restore is cached independently of source changes.
COPY PaymentFunds.slnx .
COPY src/PaymentFunds/PaymentFunds.csproj src/PaymentFunds/
RUN dotnet restore src/PaymentFunds/PaymentFunds.csproj

COPY src/ src/
RUN dotnet publish src/PaymentFunds/PaymentFunds.csproj \
    -c Release \
    -o /app \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=build /app .

# The aspnet image ships a non-root "app" user.
USER app

EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

ENTRYPOINT ["dotnet", "PaymentFunds.dll"]