# syntax=docker/dockerfile:1

ARG DOTNET_VERSION=10.0

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG VERSION=0.1.0
ARG INFORMATIONAL_VERSION=${VERSION}
WORKDIR /src

# Restore first so the package layer is cached between source changes.
COPY Directory.Build.props Directory.Packages.props PayslipArchiver.slnx ./
COPY src/PayslipArchiver.Domain/PayslipArchiver.Domain.csproj src/PayslipArchiver.Domain/
COPY src/PayslipArchiver.Application/PayslipArchiver.Application.csproj src/PayslipArchiver.Application/
COPY src/PayslipArchiver.Infrastructure/PayslipArchiver.Infrastructure.csproj src/PayslipArchiver.Infrastructure/
COPY src/PayslipArchiver.Worker/PayslipArchiver.Worker.csproj src/PayslipArchiver.Worker/
RUN dotnet restore src/PayslipArchiver.Worker/PayslipArchiver.Worker.csproj

COPY src/ src/
RUN dotnet publish src/PayslipArchiver.Worker/PayslipArchiver.Worker.csproj \
    --configuration Release --no-restore --output /app \
    -p:Version=${VERSION} -p:InformationalVersion=${INFORMATIONAL_VERSION} \
    -p:UseAppHost=false

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/runtime:${DOTNET_VERSION}
ARG VERSION=0.1.0
LABEL org.opencontainers.image.title="payslip-archiver" \
      org.opencontainers.image.description="Watches a payroll mailbox, strips the password from payslip PDFs and archives clean copies to Dropbox" \
      org.opencontainers.image.source="https://github.com/guibranco/payslip-archiver" \
      org.opencontainers.image.licenses="AGPL-3.0-only" \
      org.opencontainers.image.version="${VERSION}"

# qpdf is only needed when Pdf__Engine=Qpdf; it is small, so it ships by default.
RUN apt-get update \
    && apt-get install -y --no-install-recommends qpdf \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

# The SQLite ledger lives here; mount it so it survives container recreation
# (the image runs as the unprivileged `app` user, so a bind mount must be writable by UID 1654).
RUN mkdir -p /data && chown app:app /data
VOLUME ["/data"]
ENV Ledger__Path=/data/ledger.db \
    DOTNET_EnableDiagnostics=0

USER app
ENTRYPOINT ["dotnet", "payslip-archiver.dll"]
