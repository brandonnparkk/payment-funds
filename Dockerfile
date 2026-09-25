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

# The aspnet image ships a non-root "app" user and exposes its numeric id as
# APP_UID. Use the number, not the name: kubelet cannot verify that a named
# user is non-root, so `runAsNonRoot: true` refuses to start an image whose
# USER is a name.
USER $APP_UID

EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

ENTRYPOINT ["dotnet", "PaymentFunds.dll"]